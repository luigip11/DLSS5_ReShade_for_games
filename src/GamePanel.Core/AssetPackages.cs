using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GamePanel.Core;

public sealed record AssetPackFile(string Path, string Sha256, long Bytes);
public sealed record AssetPackManifest(int SchemaVersion, string Id, string Name, string Author,
    string SourceUrl, string Kind, IReadOnlyList<AssetPackFile> Files);
public sealed record PreparedAssetPack(string Folder, string ArchiveSha256, AssetPackManifest Manifest);

/// <summary>Imports declared data files into a verified cache. Never writes into a game or executes archive contents.</summary>
public sealed class AssetPackageImporter
{
    public const string ManifestName = "GamePanelAssetPack.json";
    public const int MaximumFiles = 2048;
    public const long MaximumArchiveBytes = 1024L * 1024 * 1024;
    public const long MaximumFileBytes = 512L * 1024 * 1024;
    public const int MaximumMetadataBytes = 256 * 1024;
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16, WriteIndented = true };
    private readonly string _cacheRoot;

    public AssetPackageImporter(string cacheRoot) => _cacheRoot = Path.GetFullPath(cacheRoot);

    internal static void VerifyPrepared(PreparedAssetPack package, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        token.ThrowIfCancellationRequested();
        if (!BackupService.ValidHash(package.ArchiveSha256) || package.Manifest is null
            || string.IsNullOrWhiteSpace(package.Folder)
            || !string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.Folder))), package.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Pacchetto preparato non valido.");
        ValidateManifest(package.Manifest); VerifyCache(package.Folder, package.Manifest, token);
    }

    public PreparedAssetPack Import(string zipPath, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        zipPath = Path.GetFullPath(zipPath);
        BackupService.GuardPath(zipPath); BackupService.GuardPath(_cacheRoot);
        using var source = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length is < 1 or > MaximumArchiveBytes) throw Invalid("Dimensione archivio non consentita.");
        var archiveLength = source.Length;
        var archiveHash = Digest(source, null, MaximumArchiveBytes, token).Hash;
        source.Position = 0;
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count is < 2 or > MaximumFiles * 5 + 1) throw Invalid("Numero di voci ZIP non consentito.");
        var entries = Inventory(zip);
        if (!entries.TryGetValue(ManifestName, out var manifestEntry) || manifestEntry.FullName != ManifestName)
            throw Invalid("Manifest GamePanelAssetPack.json mancante o ambiguo.");
        var manifest = ReadManifest(ReadBounded(manifestEntry, MaximumManifestBytes, token));
        ValidateManifest(manifest);
        var expected = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        if (entries.Count != expected.Count + 1 || entries.Keys.Any(p => p != ManifestName && !expected.ContainsKey(p)))
            throw Invalid("Lo ZIP contiene file non dichiarati o file mancanti.");
        foreach (var f in manifest.Files)
            if (!entries.TryGetValue(f.Path, out var entry) || entry.FullName != f.Path || entry.Length != f.Bytes)
                throw Invalid("Dimensione o percorso ZIP diverso dal manifest.");
        var allowedDirectories = Ancestors(manifest.Files.Select(f => f.Path));
        foreach (var dir in zip.Entries.Where(e => e.FullName.EndsWith('/')))
            if (!allowedDirectories.Contains(dir.FullName.TrimEnd('/'))) throw Invalid("Cartella ZIP non dichiarata.");

        var folder = Path.Combine(_cacheRoot, archiveHash);
        BackupService.GuardPath(folder);
        if (BackupService.AttributesOrMissing(folder) is not null)
        {
            VerifyCache(folder, manifest, token);
            VerifySource();
            return new(folder, archiveHash, manifest);
        }
        Directory.CreateDirectory(_cacheRoot); BackupService.GuardPath(_cacheRoot);
        var staging = Path.Combine(_cacheRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var filesRoot = Path.Combine(staging, "files"); Directory.CreateDirectory(filesRoot);
            for (var i = 0; i < manifest.Files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var f = manifest.Files[i]; var destination = BackupService.GameFile(filesRoot, f.Path);
                var parent = Path.GetDirectoryName(destination)!;
                BackupService.GuardPath(parent); Directory.CreateDirectory(parent); BackupService.GuardPath(parent);
                using (var input = entries[f.Path].Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var copied = Digest(input, output, f.Bytes, token);
                    if (copied.Bytes != f.Bytes || !SameHash(copied.Hash, f.Sha256)) throw Invalid("Hash o dimensione asset non corrispondente.");
                    output.Flush(flushToDisk: true);
                }
                ValidatePayload(destination, f, token);
                progress?.Report(new("Importazione asset: " + f.Path, i + 1, manifest.Files.Count));
            }
            var manifestPath = Path.Combine(staging, ManifestName);
            using (var output = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, manifest, JsonOptions); output.Flush(flushToDisk: true); }
            VerifyCache(staging, manifest, token); VerifySource();
            token.ThrowIfCancellationRequested(); BackupService.GuardPath(folder); BackupService.GuardPath(staging);
            // A concurrent importer may win. Never replace or repair an existing cache silently.
            if (BackupService.AttributesOrMissing(folder) is not null) VerifyCache(folder, manifest, token);
            else Directory.Move(staging, folder);
            return new(folder, archiveHash, manifest);
        }
        finally
        {
            // This operation owns only its unpredictable staging directory.
            if (Directory.Exists(staging))
            {
                BackupService.GuardPath(staging);
                foreach (var p in Directory.EnumerateFileSystemEntries(staging, "*", SearchOption.AllDirectories)) BackupService.GuardPath(p);
                Directory.Delete(staging, recursive: true);
            }
        }

        void VerifySource()
        {
            token.ThrowIfCancellationRequested(); BackupService.GuardPath(zipPath);
            source.Position = 0;
            if (source.Length != archiveLength || Digest(source, null, MaximumArchiveBytes, token).Hash != archiveHash)
                throw Invalid("L'archivio è cambiato durante l'importazione.");
            // Also detect replacement of the directory entry on platforms that permit it despite FileShare.Read.
            using var current = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (current.Length != archiveLength || Digest(current, null, MaximumArchiveBytes, token).Hash != archiveHash)
                throw Invalid("L'archivio selezionato è stato sostituito.");
        }
    }

    private static Dictionary<string, ZipArchiveEntry> Inventory(ZipArchive zip)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var directory = entry.FullName.EndsWith('/');
            var path = Relative(directory ? entry.FullName[..^1] : entry.FullName);
            if (!names.Add(path)) throw Invalid("Voci ZIP duplicate o con maiuscole ambigue.");
            var unixKind = (entry.ExternalAttributes >> 16) & 0xf000;
            if (unixKind is not (0 or 0x8000 or 0x4000)
                || ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                || (unixKind == 0x4000 && !directory) || (unixKind == 0x8000 && directory))
                throw Invalid("Collegamenti o file speciali nello ZIP non consentiti.");
            if (directory)
            {
                if (entry.Length != 0) throw Invalid("Cartella ZIP con contenuto.");
                continue;
            }
            if (entry.Length < 1 || entry.Length > (path == ManifestName ? MaximumManifestBytes : MaximumFileBytes))
                throw Invalid("Dimensione file ZIP non consentita.");
            total = checked(total + entry.Length);
            if (total > MaximumArchiveBytes + MaximumManifestBytes) throw Invalid("Dimensione totale ZIP non consentita.");
            files.Add(path, entry);
        }
        foreach (var path in names)
            foreach (var parent in Ancestors([path]))
                if (files.ContainsKey(parent)) throw Invalid("Conflitto tra file e cartella ZIP.");
        return files;
    }

    private static void ValidateManifest(AssetPackManifest m)
    {
        if (m.SchemaVersion != 1 || m.Id is null || !Regex.IsMatch(m.Id, "\\A[a-z0-9-]{3,48}\\z", RegexOptions.CultureInvariant)
            || !DisplayText(m.Name) || !DisplayText(m.Author) || m.Kind is not ("Textures" or "Models")
            || !Uri.TryCreate(m.SourceUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length != 0
            || m.Files is null || m.Files.Count is < 1 or > MaximumFiles)
            throw Invalid("Manifest asset non valido.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long bytes = 0;
        var models = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var f in m.Files)
        {
            if (f is null || !names.Add(Relative(f.Path)) || !BackupService.ValidHash(f.Sha256) || f.Bytes is < 1 or > MaximumFileBytes)
                throw Invalid("File del manifest asset non valido.");
            bytes = checked(bytes + f.Bytes);
            if (bytes > MaximumArchiveBytes) throw Invalid("Asset oltre il limite totale di 1 GiB.");
            var textureRoot = "TexturePacks/GamePanel-" + m.Id + "/";
            var docsRoot = "GamePanelAssets/" + m.Id + "/";
            if (f.Path.StartsWith(docsRoot, StringComparison.Ordinal) && (f.Path.EndsWith(".txt", StringComparison.Ordinal) || f.Path.EndsWith(".md", StringComparison.Ordinal))) continue;
            if (m.Kind == "Textures" && (f.Path == textureRoot + "meta.json" || f.Path == textureRoot + "textures.bin")) continue;
            if (m.Kind == "Models" && Regex.IsMatch(f.Path, "\\ACARS/[A-Z0-9_]{1,32}/(GEOMETRY|TEXTURES)\\.BIN\\z", RegexOptions.CultureInvariant))
            {
                var parts = f.Path.Split('/');
                if (!models.TryGetValue(parts[1], out var set)) models.Add(parts[1], set = []);
                set.Add(parts[2]); continue;
            }
            throw Invalid("Percorso asset non consentito: " + f.Path);
        }
        if (m.Kind == "Textures" && (!names.Contains("TexturePacks/GamePanel-" + m.Id + "/meta.json") || !names.Contains("TexturePacks/GamePanel-" + m.Id + "/textures.bin")))
            throw Invalid("Pack texture incompleto.");
        if (m.Kind == "Models" && (models.Count == 0 || models.Values.Any(s => s.Count != 2))) throw Invalid("Ogni modello richiede GEOMETRY.BIN e TEXTURES.BIN.");
        foreach (var path in names)
            if (Ancestors([path]).Any(names.Contains)) throw Invalid("Conflitto tra file e cartella nel manifest.");
    }

    private static void VerifyCache(string folder, AssetPackManifest expected, CancellationToken token)
    {
        BackupService.GuardPath(folder);
        if (!Directory.Exists(folder)) throw Invalid("La cache asset non è una cartella.");
        var manifestPath = Path.Combine(folder, ManifestName); BackupService.GuardPath(manifestPath);
        var actual = ReadManifest(ReadBoundedFile(manifestPath, MaximumManifestBytes, token)); ValidateManifest(actual);
        if (JsonSerializer.Serialize(actual, JsonOptions) != JsonSerializer.Serialize(expected, JsonOptions)) throw Invalid("Manifest cache diverso dall'archivio.");
        var allowed = expected.Files.Select(f => "files/" + f.Path).Append(ManifestName).ToHashSet(StringComparer.Ordinal);
        var dirs = Ancestors(allowed); var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(folder);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested(); var directory = pending.Pop(); BackupService.GuardPath(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested(); BackupService.GuardPath(path);
                var relative = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!found.Add(relative)) throw Invalid("Cache con percorsi ambigui.");
                if (Directory.Exists(path))
                { if (!dirs.Contains(relative)) throw Invalid("Cartella estranea nella cache."); pending.Push(path); }
                else if (!allowed.Contains(relative)) throw Invalid("File estraneo nella cache.");
            }
        }
        foreach (var f in expected.Files)
        {
            var path = BackupService.GameFile(Path.Combine(folder, "files"), f.Path);
            if (!found.Contains("files/" + f.Path) || new FileInfo(path).Length != f.Bytes || !SameHash(BackupService.HashFile(path, token), f.Sha256))
                throw Invalid("Cache asset danneggiata: " + f.Path);
            ValidatePayload(path, f, token);
        }
    }

    private static void ValidatePayload(string path, AssetPackFile f, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); BackupService.GuardPath(path);
        if (f.Path.EndsWith("/meta.json", StringComparison.Ordinal)) ValidateTextureMetadata(ReadBoundedFile(path, MaximumMetadataBytes, token));
        else if (f.Path.EndsWith(".BIN", StringComparison.Ordinal) || f.Path.EndsWith("/textures.bin", StringComparison.Ordinal))
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> signature = stackalloc byte[8];
            if (input.Read(signature) != 8 || (signature[0] == 'M' && signature[1] == 'Z')) throw Invalid("Contenitore BIN non valido.");
            input.Position = 0;
            ValidateChunkContainer(input, f.Path.EndsWith("/textures.bin", StringComparison.Ordinal)
                || f.Path.EndsWith("/TEXTURES.BIN", StringComparison.Ordinal), token);
        }
    }

    // NFS chunk containers may concatenate multiple texture packs. Walk headers without loading texture data.
    private static void ValidateChunkContainer(Stream input, bool textures, CancellationToken token)
    {
        Span<byte> header = stackalloc byte[8]; var packs = 0; var chunks = 0;
        while (input.Position < input.Length)
        {
            token.ThrowIfCancellationRequested();
            if (++chunks > 65536 || input.Length - input.Position < 8 || input.Read(header) != 8) throw Invalid("Header chunk texture troncato.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(header);
            var bytes = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            if (bytes < 0 || bytes > input.Length - input.Position) throw Invalid("Dimensione chunk texture fuori dal file.");
            if (id == 0xB3300000 || (!textures && id != 0))
            {
                if (bytes < 8) throw Invalid("Chunk texture vuoto.");
                packs++; input.Seek(bytes, SeekOrigin.Current);
            }
            else if (id == 0)
            {
                // Padding is permitted only when it contains no hidden data.
                var buffer = new byte[4096]; var remaining = bytes;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested(); var read = input.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read == 0 || buffer.AsSpan(0, read).ContainsAnyExcept((byte)0)) throw Invalid("Padding texture non valido.");
                    remaining -= read;
                }
            }
            else throw Invalid("Chunk sconosciuto nel contenitore texture.");
        }
        if (packs == 0) throw Invalid("Nessun blocco dati NFS nel contenitore.");
    }

    public static void ValidateTextureMetadata(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 1 or > MaximumMetadataBytes) throw Invalid("Metadati texture troppo grandi o vuoti.");
        using var document = ParseJson(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
            || !root.TryGetProperty("textures", out var textures)
            || textures.ValueKind != JsonValueKind.Array || textures.GetArrayLength() is < 1 or > 10000)
            throw Invalid("Elenco texture TexWizard non valido.");
        var oldHashes = new HashSet<uint>();
        foreach (var pair in textures.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2) throw Invalid("Mapping texture non valido.");
            var values = pair.EnumerateArray().ToArray();
            if (values.Any(v => v.ValueKind != JsonValueKind.String || !TextureName(v.GetString()))) throw Invalid("Nome texture non valido.");
            var oldHash = TextureHash(values[0].GetString()!);
            _ = TextureHash(values[1].GetString()!);
            if (!oldHashes.Add(oldHash)) throw Invalid("Texture originale o hash duplicato.");
        }
    }

    private static bool TextureName(string? name) => name is { Length: >= 1 and <= 128 }
        && name.All(c => c is >= ' ' and <= '~' && c is not ('/' or '\\' or ':')) && name.Trim().Length == name.Length;
    private static uint TextureHash(string name)
    {
        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Length is < 3 or > 10 || !uint.TryParse(name.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                throw Invalid("Hash texture esadecimale non valido.");
            return value;
        }
        var hash = uint.MaxValue;
        foreach (var c in name) hash = unchecked(hash * 33 + c);
        return hash;
    }
    private static bool DisplayText(string? s) => s is { Length: >= 1 and <= 100 } && !string.IsNullOrWhiteSpace(s) && !s.Any(char.IsControl);
    private static bool SameHash(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Relative(string? path)
    {
        var result = BackupService.ValidateRelative(path);
        if (path != result) throw Invalid("Utilizzare percorsi relativi con separatore '/'.");
        return result;
    }
    private static HashSet<string> Ancestors(IEnumerable<string> paths)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
            for (var at = path.LastIndexOf('/'); at > 0; at = path.LastIndexOf('/', at - 1)) result.Add(path[..at]);
        return result;
    }
    private static AssetPackManifest ReadManifest(byte[] bytes)
    {
        using var document = ParseJson(bytes);
        try { return document.RootElement.Deserialize<AssetPackManifest>(JsonOptions) ?? throw Invalid("Manifest asset mancante."); }
        catch (JsonException e) { throw new InvalidDataException("Manifest asset non leggibile.", e); }
    }
    private static JsonDocument ParseJson(byte[] bytes)
    {
        try
        {
            var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            try { Check(doc.RootElement); return doc; } catch { doc.Dispose(); throw; }
        }
        catch (JsonException e) { throw new InvalidDataException("JSON asset non valido.", e); }
        static void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in value.EnumerateObject()) { if (!keys.Add(p.Name)) throw Invalid("Proprietà JSON duplicata."); Check(p.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Check(item);
        }
    }
    private static byte[] ReadBounded(ZipArchiveEntry entry, int limit, CancellationToken token)
    {
        if (entry.Length > limit) throw Invalid("Metadati asset troppo grandi.");
        using var source = entry.Open(); using var output = new MemoryStream(); Digest(source, output, limit, token); return output.ToArray();
    }
    private static byte[] ReadBoundedFile(string path, int limit, CancellationToken token)
    {
        BackupService.GuardPath(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > limit) throw Invalid("Metadati asset troppo grandi.");
        using var output = new MemoryStream(); Digest(input, output, limit, token); return output.ToArray();
    }
    private static (string Hash, long Bytes) Digest(Stream input, Stream? output, long limit, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[64 * 1024]; long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = input.Read(buffer); if (count == 0) break;
            total = checked(total + count); if (total > limit) throw Invalid("Dimensione asset oltre il limite dichiarato.");
            hash.AppendData(buffer, 0, count); output?.Write(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested(); return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
    }
    private static InvalidDataException Invalid(string message) => new(message);
}
