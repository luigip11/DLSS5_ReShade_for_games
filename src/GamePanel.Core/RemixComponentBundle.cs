using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record RemixComponentBundleResult(int FilesCount, long Bytes, string SourceSha256, string OutputPath);

/// <summary>Exports a fixed, small set of archive members as data for review. Never installs or executes them.</summary>
public sealed class RemixComponentBundle
{
    public const long MaximumExpandedBytes = 8L * 1024 * 1024;
    public const long MaximumZipBytes = 12L * 1024 * 1024;
    public const string SourceSha256 = "dc17cc4a462935c129e08b84ee13251a4405abd91350363127f79287d36cc37f";
    private const string Prefix = "Underground 2 Remix 0.4/";
    internal sealed record Pin(long Bytes, string Sha256, string Md5, IReadOnlyDictionary<string, long> Members);
    private readonly Pin _pin;
    // This SHA-256 and the member sizes come from the user's local inventory, not an independent publisher signature.
    public RemixComponentBundle() : this(new(RemixArchiveAudit.PublishedArchiveBytes, SourceSha256,
        RemixArchiveAudit.PublishedMd5, KnownMembers)) { }
    internal RemixComponentBundle(Pin pin) { _pin = pin; }

    public RemixComponentBundleResult Prepare(string archivePath, string outputZipPath, CancellationToken token = default,
        IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        archivePath = Path.GetFullPath(archivePath); outputZipPath = Path.GetFullPath(outputZipPath);
        GuardPath(archivePath); GuardPath(outputZipPath);
        if (archivePath.Equals(outputZipPath, StringComparison.OrdinalIgnoreCase) || AttributesOrMissing(outputZipPath) is not null)
            throw Invalid("Scegli un nuovo file ZIP: nessun file esistente viene sostituito.");
        var parent = Path.GetDirectoryName(outputZipPath)!;
        if (!Directory.Exists(parent)) throw Invalid("La cartella di destinazione non esiste.");
        var pending = Path.Combine(parent, ".pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Hold a read-only handle denying writes on Windows through both hash passes and extraction.
            using var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
            var audit = new RemixArchiveAudit(new(_pin.Bytes, _pin.Md5));
            var before = audit.Analyze(archivePath, token, progress);
            if (!before.MatchesPublishedSource || !Same(before.Sha256, _pin.Sha256))
                throw Invalid("Archivio diverso dalla copia identificata nel report dell'utente.");
            var selected = before.Files.Where(f => !f.IsDirectory && f.RelativePath.StartsWith(Prefix, StringComparison.Ordinal)
                && _pin.Members.ContainsKey(f.RelativePath[Prefix.Length..])).ToArray();
            if (selected.Length != _pin.Members.Count || selected.Any(f => f.Bytes != _pin.Members[f.RelativePath[Prefix.Length..]])
                || selected.Sum(f => f.Bytes) > MaximumExpandedBytes)
                throw Invalid("Componenti Remix mancanti o dimensioni diverse dall'inventario.");
            VerifySource(source, token, progress);
            var expected = selected.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
            var exported = new List<BundleMember>(); long total = 0;
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                using (var cancelable = new CancelableStream(source, token))
                using (var archive = (SevenZipArchive)SevenZipArchive.OpenArchive(cancelable,
                    new ReaderOptions { LeaveStreamOpen = true, LookForHeader = false }))
                using (var reader = archive.ExtractAllEntries())
                {
                    var visited = new HashSet<string>(StringComparer.Ordinal); var entries = 0;
                    var buffer = new byte[65536];
                    while (reader.MoveToNextEntry())
                    {
                        token.ThrowIfCancellationRequested();
                        if (++entries > RemixArchiveAudit.MaximumEntries) throw Invalid("Troppe voci nell'archivio.");
                        var name = (reader.Entry.Key ?? "").Replace('\\', '/');
                        if (!expected.TryGetValue(name, out var member)) continue;
                        if (!visited.Add(name) || reader.Entry.IsDirectory || reader.Entry.Size != member.Bytes)
                            throw Invalid("Membro Remix cambiato durante l'esportazione.");
                        var relative = name[Prefix.Length..];
                        using var data = reader.OpenEntryStream();
                        using var target = zip.CreateEntry(relative, CompressionLevel.Optimal).Open();
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        long size = 0;
                        while (true)
                        {
                            token.ThrowIfCancellationRequested(); var count = data.Read(buffer); if (count == 0) break;
                            size = checked(size + count); total = checked(total + count);
                            if (size > member.Bytes || total > MaximumExpandedBytes) throw Invalid("Componenti estratti oltre il limite di 8 MiB.");
                            target.Write(buffer, 0, count); hash.AppendData(buffer, 0, count);
                            if (output.Length > MaximumZipBytes) throw Invalid("ZIP oltre il limite di 12 MiB.");
                        }
                        if (size != member.Bytes) throw Invalid("Componente Remix troncato.");
                        exported.Add(new(relative, size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
                        progress?.Report(new("Esportazione dati Remix: " + relative, exported.Count, expected.Count));
                    }
                    if (exported.Count != expected.Count) throw Invalid("Esportazione Remix incompleta.");
                    var manifest = zip.CreateEntry("GamePanel-RemixComponents.json", CompressionLevel.Optimal);
                    using var manifestStream = manifest.Open();
                    JsonSerializer.Serialize(manifestStream, new { SchemaVersion = 1, SourceVersion = RemixArchiveAudit.SourceVersion,
                        SourceArchiveBytes = _pin.Bytes, SourceSha256 = before.Sha256, SourceMd5 = before.Md5,
                        SourceIdentity = "SHA-256 del report locale dell'utente; nessuna firma indipendente",
                        Purpose = "Componenti come dati per audit statico; nessuna installazione o esecuzione",
                        Files = exported.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray() }, new JsonSerializerOptions { WriteIndented = true });
                }
                output.Flush(flushToDisk: true);
                if (output.Length > MaximumZipBytes) throw Invalid("ZIP oltre il limite di 12 MiB.");
            }
            VerifySource(source, token, progress);
            token.ThrowIfCancellationRequested(); GuardPath(outputZipPath); GuardPath(pending);
            File.Move(pending, outputZipPath, overwrite: false);
            return new(exported.Count, total, before.Sha256, outputZipPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is not InvalidDataException &&
            (e is IOException or ArgumentException or InvalidOperationException or OverflowException or NotSupportedException or FormatException
                || e.GetType().Assembly == typeof(SevenZipArchive).Assembly))
        { throw new InvalidDataException("Esportazione Remix non riuscita: nessun componente è stato installato o eseguito.", e); }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private void VerifySource(Stream source, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        if (source.Length != _pin.Bytes) throw Invalid("Dimensione archivio cambiata durante l'esportazione.");
        source.Position = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024]; long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = source.Read(buffer); if (count == 0) break;
            total = checked(total + count); if (total > _pin.Bytes) throw Invalid("Archivio cambiato durante l'esportazione.");
            hash.AppendData(buffer, 0, count);
            progress?.Report(new("Verifica sorgente componenti Remix", (int)(total * 1000 / _pin.Bytes), 1000));
        }
        if (total != _pin.Bytes || !Same(Convert.ToHexString(hash.GetHashAndReset()), _pin.Sha256))
            throw Invalid("Archivio diverso o cambiato durante l'esportazione.");
        source.Position = 0;
    }
    private sealed record BundleMember(string RelativePath, long Bytes, string Sha256);
    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    private static InvalidDataException Invalid(string message) => new(message);
    private sealed class CancelableStream(Stream inner, CancellationToken token) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { token.ThrowIfCancellationRequested(); inner.Position = value; } }
        public override int Read(byte[] buffer, int offset, int count) { token.ThrowIfCancellationRequested(); return inner.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { token.ThrowIfCancellationRequested(); return inner.Read(buffer); }
        public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return inner.Seek(offset, origin); }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static readonly IReadOnlyDictionary<string, long> KnownMembers = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        [".trex/bridge.conf"] = 58,
        [".trex/NvRemixBridge.exe"] = 1104384,
        [".trex/XESS_LICENSE.txt"] = 4194,
        ["d3d9.dll"] = 854016,
        ["dinput8.dll"] = 2170368,
        ["dxvk.conf"] = 15140,
        ["LICENSE.txt"] = 1166,
        ["license_widescreenfix.txt"] = 1067,
        ["NvRemixLauncher32.exe"] = 137216,
        ["rtx-remix/mods/gameReadyAssets/02_materials/AperturePBR_Model.mdl"] = 5688,
        ["rtx-remix/mods/gameReadyAssets/02_materials/AperturePBR_Normal.mdl"] = 7537,
        ["rtx-remix/mods/gameReadyAssets/02_materials/AperturePBR_Opacity.mdl"] = 22947,
        ["rtx-remix/mods/gameReadyAssets/02_materials/AperturePBR_SpriteSheet.mdl"] = 3223,
        ["rtx-remix/mods/gameReadyAssets/02_materials/AperturePBR_Translucent.mdl"] = 12941,
        ["rtx-remix/mods/gameReadyAssets/adamplayer.usda"] = 17977,
        ["rtx-remix/mods/gameReadyAssets/emissive_lights.usda"] = 38994,
        ["rtx-remix/mods/gameReadyAssets/headlights.usda"] = 57747,
        ["rtx-remix/mods/gameReadyAssets/highways.usda"] = 1496,
        ["rtx-remix/mods/gameReadyAssets/metal.usda"] = 8646,
        ["rtx-remix/mods/gameReadyAssets/mod.usda"] = 1295,
        ["rtx-remix/mods/gameReadyAssets/mod_lights.usda"] = 114803,
        ["rtx-remix/mods/gameReadyAssets/README.md"] = 11701,
        ["rtx-remix/mods/gameReadyAssets/rough_only.usda"] = 400980,
        ["rtx-remix/mods/gameReadyAssets/signs.usda"] = 203491,
        ["rtx-remix/mods/gameReadyAssets/stone.usda"] = 7346,
        ["rtx-remix/mods/gameReadyAssets/tail_lights.usda"] = 80908,
        ["rtx-remix/mods/gameReadyAssets/tiles.usda"] = 121,
        ["rtx-remix/mods/gameReadyAssets/traffic.usda"] = 39298,
        ["rtx-remix/mods/gameReadyAssets/tunnels.usda"] = 4695,
        ["rtx-remix/mods/gameReadyAssets/uncle_burrito.usda"] = 94714,
        ["rtx-remix/mods/gameReadyAssets/vegetation.usda"] = 50844,
        ["rtx-remix/mods/gameReadyAssets/wet_roads.usda"] = 16643,
        ["rtx-remix/mods/gameReadyAssets/windows.usda"] = 21240,
        ["rtx-remix/mods/gameReadyAssets/wood.usda"] = 3390,
        ["rtx-remix/mods/gameReadyAssets/world_sprites.usda"] = 37903,
        ["rtx.conf"] = 9617,
        ["scripts/NFSU2AntiCull.asi"] = 84992,
        ["scripts/NFSUnderground2.WidescreenFix.asi"] = 365056,
        ["scripts/NFSUnderground2.WidescreenFix.dat"] = 5736,
        ["scripts/NFSUnderground2.WidescreenFix.ini"] = 2570,
        ["scripts/NFSU_XtendedInput.asii"] = 43520,
        ["scripts/NFSU_XtendedInput.ini"] = 9708,
        ["ThirdPartyLicenses-bridge.txt"] = 6750,
        ["ThirdPartyLicenses-dxvk.txt"] = 145587,
    };
}
