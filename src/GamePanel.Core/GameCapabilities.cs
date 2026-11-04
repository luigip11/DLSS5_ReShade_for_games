using System.Security.Cryptography;
using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

/// <summary>Advisory presentation capabilities. Every installation service must still perform its own preflight.</summary>
public sealed record GameCapabilities(bool IsUnderground2, bool HasRemixRenderer, bool HasRemixTrial,
    bool SupportsReShade, bool SupportsWidescreen, bool SupportsDirectXTextures, bool SupportsTexWizard,
    string DisplayName, string RemixMessage)
{
    public bool IsUnderground1 { get; init; }
    public static GameCapabilities From(GameReport? report)
    {
        if (report is null) return new(false, false, false, false, false, false, false, "Nessun gioco selezionato", "Seleziona un gioco dalla libreria.");
        try
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(report.ExecutablePath))!;
            var candidate = Path.GetFileName(report.ExecutablePath).Equals("speed2.exe", StringComparison.OrdinalIgnoreCase)
                && report.Architecture == "x86 (32 bit)" && report.GraphicsApis.Contains("Direct3D 9");
            var known = candidate && RemixExecutableCatalog.Contains(report.Sha256);
            // The strict TexWizard signature also recognizes the inert compatibility fixture used in Windows checks.
            var texWizard = candidate && TextureRuntimeCatalog.CheckGame(report) is null;
            var gameFolders = candidate && new[] { "GLOBAL", "TRACKS", "CARS" }.All(name => RegularDirectory(Path.Combine(root, name)));
            var underground = known || texWizard || gameFolders;
            var ug1 = Path.GetFileName(report.ExecutablePath).Equals("Speed.exe", StringComparison.OrdinalIgnoreCase)
                && report.Sha256.Equals(Underground1DisplayFix.ExecutableSha256, StringComparison.OrdinalIgnoreCase);
            var renderer = candidate && KnownRemixRenderer(root) || ug1 && Underground1RemixInstallation.HasRenderer(root);
            var trial = renderer && known && OwnedTrial(root);
            var safe = !report.HasAntiCheatIndicators && !report.ScanWasLimited;
            var legacy = !renderer && safe;
            return new(underground, renderer, trial,
                legacy && ReShadeProfileService.Eligibility(report) is null,
                underground && legacy && ReShadeProfileService.Eligibility(report, true) is null,
                underground && legacy && DirectXTextureCatalog.CheckGame(report) is null,
                underground && legacy && texWizard,
                underground ? "Need for Speed Underground 2" : ug1 ? "Need for Speed Underground" : "Gioco selezionato",
                trial ? "Copia Remix riconosciuta. Puoi conservarne la configurazione e gestire il profilo."
                    : renderer && ug1 ? "Installazione locale UG1 Remix rilevata. Registra la configurazione per gestire DLSS."
                    : renderer ? "Runtime Remix riconosciuto; la copia non dispone dei metadati GamePanel richiesti per gestirla."
                    : underground ? "Per questo gioco è disponibile una preparazione Remix su copia separata."
                    : "Nessun percorso Remix verificato disponibile per questo gioco.") { IsUnderground1 = ug1 };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or BadImageFormatException)
        { return new(false, false, false, false, false, false, false, "Gioco selezionato", "Disponibilità da verificare: " + e.Message); }
    }

    private static bool RegularDirectory(string path)
    {
        GuardPath(path);
        return AttributesOrMissing(path) is FileAttributes a && (a & FileAttributes.Directory) != 0
            && (a & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;
    }
    private static bool KnownRemixRenderer(string root)
    {
        var client = GameFile(root, "d3d9.dll");
        if (!RegularDirectory(GameFile(root, ".trex")) || AttributesOrMissing(client) is not FileAttributes a
            || (a & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) return false;
        using var stream = new FileStream(client, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Length == 854016 && Convert.ToHexString(SHA256.HashData(stream))
            .Equals(RemixTrialService.ClientSha256, StringComparison.OrdinalIgnoreCase);
    }
    private static bool OwnedTrial(string root)
    {
        var marker = GameFile(root, "GamePanel-RemixTrial.json");
        if (AttributesOrMissing(marker) is not FileAttributes a
            || (a & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) return false;
        using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 2 or > 16 * 1024 * 1024) return false;
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            var value = document.RootElement;
            return value.TryGetProperty("SchemaVersion", out var schema) && schema.TryGetInt32(out var version) && version == 1
                && value.TryGetProperty("SourceExecutableSha256", out var hash) && hash.ValueKind == JsonValueKind.String
                && hash.GetString()!.Equals(RemixTrialService.WorkingExecutableSha256, StringComparison.OrdinalIgnoreCase)
                && value.TryGetProperty("ArchiveSha256", out var archive) && archive.ValueKind == JsonValueKind.String
                && archive.GetString()!.Equals(RemixComponentBundle.SourceSha256, StringComparison.OrdinalIgnoreCase)
                && value.TryGetProperty("SourceFolder", out var source) && source.ValueKind == JsonValueKind.String
                && Path.IsPathFullyQualified(source.GetString()!)
                && !Path.GetFullPath(source.GetString()!).Equals(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }
}

public sealed record GameVariantGroup(LibraryGame Original, IReadOnlyList<LibraryGame> Variants)
{
    public LibraryGame Preferred(string? selectedId) => Variants.FirstOrDefault(g => g.Id == selectedId)
        ?? Variants.Where(g => g.LastPlayedAt is not null).OrderByDescending(g => g.LastPlayedAt).FirstOrDefault() ?? Original;
}

/// <summary>Groups only explicit registered relationships; never guesses identity from an EXE name or hash.</summary>
public static class GameLibraryVariants
{
    public static IReadOnlyList<GameVariantGroup> Group(LibrarySnapshot snapshot,
        IEnumerable<(string OriginalExecutablePath, string RemixExecutablePath)> registered)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var byPath = snapshot.Games.ToDictionary(g => g.ExecutablePath, comparison);
        var pairs = registered.Where(p => p.OriginalExecutablePath is not null && p.RemixExecutablePath is not null
            && byPath.ContainsKey(p.OriginalExecutablePath) && byPath.ContainsKey(p.RemixExecutablePath)
            && !comparison.Equals(p.OriginalExecutablePath, p.RemixExecutablePath)).Distinct().ToArray();
        var ambiguous = pairs.GroupBy(p => p.RemixExecutablePath, comparison)
            .Where(g => g.Select(p => p.OriginalExecutablePath).Distinct(comparison).Count() != 1)
            .Select(g => g.Key).ToHashSet(comparison);
        var copies = pairs.Select(p => p.RemixExecutablePath).ToHashSet(comparison);
        pairs = pairs.Where(p => !ambiguous.Contains(p.RemixExecutablePath) && !copies.Contains(p.OriginalExecutablePath)).ToArray();
        var groupedCopies = pairs.Select(p => p.RemixExecutablePath).ToHashSet(comparison);
        return snapshot.Games.Where(g => !groupedCopies.Contains(g.ExecutablePath)).Select(original =>
        {
            var variants = pairs.Where(p => comparison.Equals(p.OriginalExecutablePath, original.ExecutablePath))
                .Select(p => byPath[p.RemixExecutablePath]).DistinctBy(g => g.Id).OrderBy(g => g.Title).ToArray();
            return new GameVariantGroup(original, Array.AsReadOnly(new[] { original }.Concat(variants).ToArray()));
        }).ToArray();
    }
}
