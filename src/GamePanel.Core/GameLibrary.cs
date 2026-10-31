using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record LibraryGame(string Id, string Title, string ExecutablePath, string Sha256,
    string Architecture, IReadOnlyList<string> GraphicsApis, DateTimeOffset AddedAt,
    DateTimeOffset LastInspectedAt, DateTimeOffset? LastPlayedAt, bool CustomTitle = false);
public sealed record LibrarySnapshot(int SchemaVersion, string? SelectedId, IReadOnlyList<LibraryGame> Games);

/// <summary>Local metadata only. Opening a library never analyzes or executes saved paths.</summary>
public sealed class GameLibrary
{
    public const int MaximumGames = 500;
    private const int MaximumBytes = 1024 * 1024;
    private readonly string _folder;
    public static string DefaultFolder => Path.Combine(GamePanelPaths.Root, "Library");
    public string FilePath => Path.Combine(_folder, "library.json");
    public GameLibrary(string folder) => _folder = Path.GetFullPath(folder);
    public LibrarySnapshot Load()
    {
        GuardPath(FilePath);
        if (AttributesOrMissing(FilePath) is null) return new(1, null, []);
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Archivio della libreria oltre il limite.");
        LibrarySnapshot value;
        try { value = JsonSerializer.Deserialize<LibrarySnapshot>(stream) ?? throw new InvalidDataException("Libreria vuota o non leggibile."); }
        catch (JsonException e) { throw new InvalidDataException("Libreria danneggiata: il file viene conservato senza sovrascriverlo.", e); }
        Validate(value); return value;
    }
    public LibrarySnapshot Remember(GameReport report, string? replaceId = null)
    {
        var path = Path.GetFullPath(ExecutablePath.Normalize(report.ExecutablePath));
        if (!ValidHash(report.Sha256)) throw new InvalidDataException("Analisi non valida per l'importazione.");
        return Change(snapshot =>
        {
            var existing = replaceId is null ? snapshot.Games.FirstOrDefault(g => SamePath(g.ExecutablePath, path))
                : snapshot.Games.SingleOrDefault(g => g.Id == replaceId) ?? throw new InvalidDataException("Voce da ricollegare non trovata.");
            if (snapshot.Games.Any(g => SamePath(g.ExecutablePath, path) && g.Id != existing?.Id))
                throw new InvalidDataException("Questo eseguibile è già nella libreria.");
            var title = existing?.CustomTitle == true ? existing.Title : SuggestedTitle(path);
            var game = new LibraryGame(existing?.Id ?? Guid.NewGuid().ToString("N"), title, path, report.Sha256,
                report.Architecture, report.GraphicsApis.ToArray(), existing?.AddedAt ?? DateTimeOffset.UtcNow,
                report.InspectedAt, existing?.LastPlayedAt, existing?.CustomTitle ?? false);
            return new(1, game.Id, snapshot.Games.Where(g => g.Id != game.Id).Append(game).ToArray());
        });
    }
    public LibrarySnapshot Rename(string id, string title)
    {
        title = CleanTitle(title);
        return Change(snapshot =>
        {
            if (!snapshot.Games.Any(g => g.Id == id)) throw new InvalidDataException("Gioco non trovato nella libreria.");
            return snapshot with { Games = snapshot.Games.Select(g => g.Id == id ? g with { Title = title, CustomTitle = true } : g).ToArray() };
        });
    }
    public LibrarySnapshot Remove(string id) => Change(snapshot => snapshot with
    { SelectedId = snapshot.SelectedId == id ? null : snapshot.SelectedId, Games = snapshot.Games.Where(g => g.Id != id).ToArray() });
    public LibrarySnapshot MarkPlayed(string executablePath) => Change(snapshot =>
    {
        var path = Path.GetFullPath(ExecutablePath.Normalize(executablePath));
        var game = snapshot.Games.FirstOrDefault(g => SamePath(g.ExecutablePath, path));
        return game is null ? snapshot : snapshot with
        { SelectedId = game.Id, Games = snapshot.Games.Select(g => g.Id == game.Id ? g with { LastPlayedAt = DateTimeOffset.UtcNow } : g).ToArray() };
    });
    private LibrarySnapshot Change(Func<LibrarySnapshot, LibrarySnapshot> update)
    {
        GuardPath(_folder); Directory.CreateDirectory(_folder); GuardPath(_folder);
        var lockPath = Path.Combine(_folder, ".library.lock"); GuardPath(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var value = update(Load()); Validate(value);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, value, new JsonSerializerOptions { WriteIndented = true });
                if (output.Length > MaximumBytes) throw new InvalidDataException("La libreria ha raggiunto la dimensione massima.");
                output.Flush(true);
            }
            GuardPath(FilePath); File.Move(temporary, FilePath, true);
        }
        finally { GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
        return value;
    }
    private static void Validate(LibrarySnapshot value)
    {
        if (value.SchemaVersion != 1 || value.Games is null || value.Games.Count > MaximumGames) throw new InvalidDataException("Formato della libreria non supportato.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var g in value.Games)
        {
            if (g is null || !ValidId(g.Id) || !ids.Add(g.Id) || !ValidHash(g.Sha256) || CleanTitle(g.Title) != g.Title
                || string.IsNullOrWhiteSpace(g.ExecutablePath) || g.ExecutablePath.Length > 2048
                || !Path.IsPathFullyQualified(g.ExecutablePath) || Path.GetFullPath(ExecutablePath.Normalize(g.ExecutablePath)) != g.ExecutablePath
                || !paths.Add(g.ExecutablePath) || string.IsNullOrWhiteSpace(g.Architecture) || g.Architecture.Length > 80
                || g.GraphicsApis is null || g.GraphicsApis.Count > 16 || g.GraphicsApis.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 80 || a.Any(char.IsControl)))
                throw new InvalidDataException("Voce della libreria non valida: il file viene conservato.");
        }
        if (value.SelectedId is not null && !ids.Contains(value.SelectedId)) throw new InvalidDataException("Selezione della libreria non valida.");
    }
    private static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160 || title.Any(char.IsControl)) throw new InvalidDataException("Inserisci un nome tra 1 e 160 caratteri, su una riga.");
        return title.Trim();
    }
    private static string SuggestedTitle(string path)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(path));
        var title = string.IsNullOrWhiteSpace(parent) || new[] { "bin", "binaries", "win32", "win64", "x64", "x86" }.Contains(parent.ToLowerInvariant())
            ? Path.GetFileNameWithoutExtension(path) : parent;
        title = new string(title.Where(c => !char.IsControl(c)).Take(160).ToArray()).Trim();
        return title.Length == 0 ? "Gioco importato" : title;
    }
    private static bool SamePath(string a, string b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
