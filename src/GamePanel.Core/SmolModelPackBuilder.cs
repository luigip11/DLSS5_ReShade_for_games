using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpCompress.Readers;
using SharpCompress.Readers.Rar;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record PreparedSmolModels(PreparedAssetPack Package, IReadOnlyList<AssetPackFile> Originals,
    string ExecutablePath, string ExecutableSha256);

/// <summary>Builds a local three-car subset. Reads RAR data; never runs END scripts or other code.</summary>
public sealed class SmolModelPackBuilder
{
    internal sealed record Member(string ArchivePath, string TargetPath, long Bytes, string Sha256);
    internal sealed record Pin(long Bytes, string Sha256, IReadOnlyList<Member> Members);
    private static readonly string[] Cars = ["GOLF", "IS300", "SENTRA"];
    private static readonly Pin AuthorPin = new(SmolModelSource.ArchiveBytes, SmolModelSource.ArchiveSha256,
    [
        new("_setup/GOLF/GEOMETRY.BIN", "CARS/GOLF/GEOMETRY.BIN", 3988404, "1c90ce5fe0b07d7804d40455baf5479ca560796a03d72f322dc843388a690932"),
        new("_setup/IS300/GEOMETRY.BIN", "CARS/IS300/GEOMETRY.BIN", 3644716, "9364c606bc3cbf74c2598b48a5e37e216e3fdac4e844a34eaf9ea1d5d898f55f"),
        new("_setup/SENTRA/GEOMETRY.BIN", "CARS/SENTRA/GEOMETRY.BIN", 4015836, "342dd577d228957ad7ad3b8fd76e59bacc551e41bbad8c703483654c1a1e6203"),
        new("readme/english.html", "GamePanelAssets/smol-cars/README-English.html.txt", 5016, "aec57fdd839dc9b11ad959e03c053be9172be1e4678ea48595e8a195121ac531"),
        new("readme/español.html", "GamePanelAssets/smol-cars/README-Espanol.html.txt", 5416, "7e698029ae4046b89cf49b51295eefe8e7ccef09ad1c085f48887709bf3536b1")
    ]);
    private readonly string _cacheRoot;
    private readonly Pin _pin;
    public SmolModelPackBuilder(string? cacheRoot = null) : this(cacheRoot ?? Path.Combine(GamePanelPaths.Root, "SmolModels"), AuthorPin) { }
    internal SmolModelPackBuilder(string cacheRoot, Pin pin) { _cacheRoot = Path.GetFullPath(cacheRoot); _pin = pin; }

    public PreparedSmolModels Prepare(GameReport report, string archivePath, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var executable = Path.GetFullPath(report.ExecutablePath); var game = Path.GetDirectoryName(executable)!;
        if (!ValidHash(report.Sha256) || !Same(HashFile(executable, token), report.Sha256)) throw Invalid("Il gioco è cambiato: analizzalo di nuovo.");
        var originals = Cars.SelectMany(car => new[] { "GEOMETRY.BIN", "TEXTURES.BIN" }.Select(file =>
        {
            var relative = "CARS/" + car + "/" + file; var path = GameFile(game, relative);
            GuardPath(path); var bytes = new FileInfo(path).Length;
            if (bytes is < 8 or > AssetPackageImporter.MaximumFileBytes) throw Invalid("Originale auto mancante o troppo grande: " + relative);
            return new AssetPackFile(relative, HashFile(path, token), bytes);
        })).ToArray();
        GuardPath(_cacheRoot); Directory.CreateDirectory(_cacheRoot); GuardPath(_cacheRoot);
        var staging = Path.Combine(_cacheRoot, ".pending-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try
        {
            archivePath = Path.GetFullPath(archivePath); GuardPath(archivePath);
            using var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            VerifySource(source, token); source.Position = 0;
            var selected = Extract(source, staging, token, progress);
            VerifySource(source, token);
            using (var current = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)) VerifySource(current, token);
            var files = new List<(AssetPackFile File, string Source)>();
            foreach (var member in _pin.Members) files.Add((new(member.TargetPath, member.Sha256, member.Bytes), selected[member.ArchivePath]));
            foreach (var original in originals.Where(f => f.Path.EndsWith("/TEXTURES.BIN", StringComparison.Ordinal)))
            {
                var copy = Path.Combine(staging, "texture-" + files.Count + ".bin");
                using var input = new FileStream(GameFile(game, original.Path), FileMode.Open, FileAccess.Read, FileShare.Read);
                using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None)) Copy(input, output, original.Bytes, token);
                if (new FileInfo(copy).Length != original.Bytes || !Same(HashFile(copy, token), original.Sha256)) throw Invalid("Texture originale cambiata durante la preparazione.");
                files.Add((original, copy));
            }
            var credit = Path.Combine(staging, "credit.txt");
            File.WriteAllText(credit, "Smol model fix: AJ_Lethal (Ariel Aguilar Orozco) / Need for Speed .R Garage.\nhttps://nfsmods.xyz/mod/2721\nSubset locale: Golf, IS300, Sentra; sole geometrie. Texture originali conservate.\nEsclusi: database, A3, Mustang, RSX, ruote, Unlimiter e suoni.\nPacchetto contenente texture del gioco: conservare localmente, non redistribuire.\n", new UTF8Encoding(false));
            files.Add((new("GamePanelAssets/smol-cars/CREDITS.txt", HashFile(credit, token), new FileInfo(credit).Length), credit));
            var manifest = new AssetPackManifest(1, "smol-cars", "Correzioni Golf, IS300 e Sentra · Smol parziale", "AJ_Lethal", SmolModelSource.SourceUrl, "Models", files.Select(f => f.File).ToArray());
            var zipPath = Path.Combine(staging, "local-models.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry(AssetPackageImporter.ManifestName).Open()) JsonSerializer.Serialize(stream, manifest);
                foreach (var item in files)
                {
                    token.ThrowIfCancellationRequested(); using var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = zip.CreateEntry(item.File.Path, CompressionLevel.NoCompression).Open(); Copy(input, output, item.File.Bytes, token);
                }
            }
            var package = new AssetPackageImporter(Path.Combine(_cacheRoot, "packages")).Import(zipPath, token, progress);
            var prepared = new PreparedSmolModels(package, originals, executable, report.Sha256);
            VerifyForInstall(report, prepared, token); return prepared;
        }
        finally
        {
            GuardPath(staging);
            foreach (var path in Directory.EnumerateFileSystemEntries(staging, "*", SearchOption.AllDirectories)) GuardPath(path);
            Directory.Delete(staging, true);
        }
    }

    public static void VerifyForInstall(GameReport report, PreparedSmolModels prepared, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!string.Equals(Path.GetFullPath(report.ExecutablePath), prepared.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            || !Same(report.Sha256, prepared.ExecutableSha256) || !Same(HashFile(prepared.ExecutablePath, token), prepared.ExecutableSha256))
            throw Invalid("Eseguibile cambiato dalla preparazione Smol: analizzalo e prepara di nuovo.");
        var root = Path.GetDirectoryName(prepared.ExecutablePath)!;
        foreach (var f in prepared.Originals)
        {
            var path = GameFile(root, f.Path); GuardPath(path);
            if (new FileInfo(path).Length != f.Bytes || !Same(HashFile(path, token), f.Sha256))
                throw Invalid("Originale auto cambiato dalla preparazione Smol: " + f.Path + ". Prepara di nuovo.");
        }
        AssetPackageImporter.VerifyPrepared(prepared.Package, token);
    }

    private Dictionary<string, string> Extract(Stream source, string staging, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        try { return ExtractCore(source, staging, token, progress); }
        catch (Exception e) when (e is not InvalidDataException &&
            (e is IOException or InvalidOperationException or ArgumentException or OverflowException or NotSupportedException or FormatException
                || e.GetType().Assembly == typeof(RarReader).Assembly))
        { throw new InvalidDataException("RAR Smol danneggiato, incompleto o non supportato. Nessuno script è stato eseguito.", e); }
    }

    private Dictionary<string, string> ExtractCore(Stream source, string staging, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        // The pinned author file is RAR4, not SFX or RAR5 (whose redirect extras are not exposed as LinkTarget).
        Span<byte> magic = stackalloc byte[7];
        if (source.Read(magic) != magic.Length || !magic.SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0 }))
            throw Invalid("Formato RAR Smol non riconosciuto.");
        source.Position = 0;
        using var reader = RarReader.OpenReader(source, new ReaderOptions { LeaveStreamOpen = true, LookForHeader = false });
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var selected = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0; var count = 0;
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested(); if (++count > 128) throw Invalid("Troppe voci nel RAR Smol.");
            var entry = reader.Entry; var name = (entry.Key ?? "").Replace('\\', '/').TrimEnd('/');
            if (name.Length == 0 || ValidateRelative(name) != name || !names.Add(name)) throw Invalid("Percorso RAR ambiguo o duplicato.");
            var kind = ((entry.Attrib ?? 0) >> 12) & 15;
            if (entry.LinkTarget is not null || entry.IsEncrypted || entry.IsSplitAfter || entry.VolumeIndexFirst != 0 || entry.VolumeIndexLast != 0
                || ((entry.Attrib ?? 0) & (int)FileAttributes.ReparsePoint) != 0 || kind is not (0 or 4 or 8))
                throw Invalid("Link, file speciali, cifratura o volumi RAR non ammessi.");
            if (entry.IsDirectory) { if (entry.Size != 0) throw Invalid("Cartella RAR con dati."); continue; }
            if (entry.Size is < 1 or > 64L * 1024 * 1024 || (total = checked(total + entry.Size)) > 64L * 1024 * 1024)
                throw Invalid("RAR Smol oltre il limite di 64 MiB estratti.");
            var member = _pin.Members.SingleOrDefault(m => m.ArchivePath == name);
            var destination = member is null ? null : Path.Combine(staging, "member-" + count + ".bin");
            using var input = reader.OpenEntryStream();
            using var output = destination is null ? null : new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var actual = Copy(input, output, entry.Size, token);
            if (actual.Crc != unchecked((uint)entry.Crc)) throw Invalid("CRC RAR Smol non valido: " + name);
            if (member is not null)
            {
                if (entry.Size != member.Bytes || !Same(actual.Hash, member.Sha256)) throw Invalid("Dati Smol diversi dal catalogo: " + name);
                selected.Add(name, destination!);
            }
            progress?.Report(new("Lettura dati Smol: " + name, count, 128));
        }
        if (selected.Count != _pin.Members.Count) throw Invalid("Geometrie o readme Smol mancanti.");
        return selected;
    }

    private void VerifySource(Stream source, CancellationToken token)
    {
        if (_pin.Bytes is < 1 or > SmolModelSource.MaximumArchiveBytes || source.Length != _pin.Bytes) throw Invalid("Dimensione archivio Smol non riconosciuta.");
        source.Position = 0;
        if (!Same(Copy(source, null, _pin.Bytes, token).Hash, _pin.Sha256)) throw Invalid("Archivio Smol non riconosciuto o cambiato.");
    }
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    { uint crc = (uint)n; for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; return crc; }).ToArray();
    private static (string Hash, uint Crc) Copy(Stream input, Stream? output, long bytes, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); uint crc = uint.MaxValue; long total = 0; var buffer = new byte[65536];
        while (true)
        {
            token.ThrowIfCancellationRequested(); var n = input.Read(buffer); if (n == 0) break;
            total = checked(total + n); if (total > bytes) throw Invalid("Dati Smol oltre la dimensione dichiarata.");
            hash.AppendData(buffer, 0, n); for (var i = 0; i < n; i++) crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
            output?.Write(buffer, 0, n);
        }
        if (total != bytes) throw Invalid("Dati Smol troncati.");
        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), ~crc);
    }
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static InvalidDataException Invalid(string message) => new(message);
}
