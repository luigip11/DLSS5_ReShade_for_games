using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

/// <summary>Pinned upstream bytes and static, fail-closed checks; no image is ever loaded.</summary>
public static class TextureRuntimeCatalog
{
    public const string Version = "1.3";
    public const string SourceTagCommit = "51e7d29c070aa8f9b7c3b44b78fadfda299777a3";
    public const string SourceUrl = "https://github.com/R-033/texwizard/tree/" + SourceTagCommit;
    public const string License = "GPL-3.0 (upstream); binary SHA-256 is an integrity pin, not a Windows signature or malware guarantee.";
    public const string ZipUrl = "https://github.com/R-033/texwizard/releases/download/1.3/TexWizard_UG2.zip";
    public const long ZipBytes = 169134;
    public const string ZipSha256 = "2d0988ab482219cf97a0d3781139c3a5ef6388d3a50030fedc8803c7de9d8604";
    public const string LoaderSha256 = "63b464597f60d7e2f29dc65bd48919d9336896d1f54c9be8247157c14ebfe25b";
    public const long LoaderBytes = 335872;
    public const string Loader = "scripts/TexWizard.asi";
    public const int ExpectedExeBytes = 4800512;
    // UG2_Address.h, pinned commit above. Only these audited callsites are mapped.
    internal static readonly IReadOnlyList<uint> TextureCallsites = Array.AsReadOnly(new uint[] {
        4430024,4489790,4489810,4489830,4784921,4784970,4784989,4785008,4785027,4785046,4785632,
        4786117,4786327,4786430,4788845,4791502,4808101,4858928,4859001,4871029,4914722,4914808,
        4916056,4924768,4924854,4936624,4973667,4973753,5007108,5007348,5037501,5037914,5204205,
        5289231,5290994,5294974,5295166,5310974,5311005,5311033,5311061,5357308,5466250,5466846,
        5467406,5487024,5766138,6002882,6002913,6002944,6003004,6003034,6003064,6003094,6003124,
        6003154,6014726,6015152,6015530,6051682,6058952,6061020,6077254,6092449,6107818,6342645,
        6369396,6369426,6412205,6412967,6413066,6413981,6414035,6414051,6424138,6522624,6522643,
        6522919,6522968,6522999,6523017 });

    public static string? CheckGame(GameReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!string.Equals(Path.GetFileName(report.ExecutablePath), "speed2.exe", StringComparison.OrdinalIgnoreCase)
            || report.Architecture != "x86 (32 bit)" || !report.GraphicsApis.Contains("Direct3D 9"))
            return "TexWizard 1.3 richiede Need for Speed Underground 2, speed2.exe x86 Direct3D 9.";
        if (report.HasAntiCheatIndicators || report.ScanWasLimited)
            return "TexWizard non disponibile: indicatori anti-cheat o scansione incompleta.";
        try
        {
            var image = ReadImage(report.ExecutablePath);
            if (!string.Equals(Digest(image), report.Sha256, StringComparison.OrdinalIgnoreCase))
                return "L'EXE è cambiato dopo l'analisi: esegui di nuovo la scansione.";
            CheckImage(image);
            // Detect directory-entry replacement as well as writes on platforms with different sharing rules.
            if (!string.Equals(Digest(ReadImage(report.ExecutablePath)), report.Sha256, StringComparison.OrdinalIgnoreCase))
                return "L'EXE è cambiato durante la verifica: esegui di nuovo la scansione.";
            return null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { return "TexWizard non compatibile: " + e.Message + " Serve una build funzionante che corrisponda al layout e ai controlli del runtime."; }
    }

    private static byte[] ReadImage(string path)
    {
        GuardPath(path);
        if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("L'EXE non è un file regolare.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != ExpectedExeBytes || input.Length > 5 * 1024 * 1024) throw new InvalidDataException("Dimensione EXE diversa dai 4.800.512 byte attesi.");
        var bytes = new byte[ExpectedExeBytes]; input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("Dimensione EXE cambiata.");
        return bytes;
    }

    private static void CheckImage(byte[] bytes)
    {
        ushort U16(int at) { Bounds(at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2)); }
        uint U32(int at) { Bounds(at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)); }
        void Bounds(int at, int size) { if (at < 0 || size < 0 || at > bytes.Length - size) throw new InvalidDataException("Header PE fuori dal file."); }
        if (U16(0) != 0x5a4d) throw new InvalidDataException("Firma MZ non valida.");
        var peValue = U32(0x3c);
        if (peValue < 0x40 || peValue > 1024 * 1024) throw new InvalidDataException("Offset PE fuori limite.");
        var pe = (int)peValue; Bounds(pe, 24);
        var count = U16(pe + 6); var optionalSize = U16(pe + 20); var opt = pe + 24;
        if (U32(pe) != 0x4550 || U16(pe + 4) != 0x14c || count is < 1 or > 96
            || (U16(pe + 22) & 0x2002) != 2 || optionalSize < 224)
            throw new InvalidDataException("Header EXE x86 non valido.");
        Bounds(opt, optionalSize);
        if (U16(opt) != 0x10b || U32(opt + 28) != 0x400000 || U32(opt + 16) != 0x35bcc7
            || (U16(opt + 70) & 0x40) != 0)
            throw new InvalidDataException("PE32, ImageBase, entry point o ASLR diversi dal runtime supportato.");
        var headers = U32(opt + 60); var imageSize = U32(opt + 56); var table = opt + optionalSize;
        Bounds(table, count * 40);
        if (headers < table + count * 40 || headers > bytes.Length || imageSize < headers || imageSize > 32 * 1024 * 1024)
            throw new InvalidDataException("Limiti immagine PE non validi.");
        var sections = new List<(uint Va, uint Size, uint Raw, uint Bytes, uint Flags)>();
        for (var i = 0; i < count; i++)
        {
            var at = table + i * 40; var va = U32(at + 12); var raw = U32(at + 20); var rawBytes = U32(at + 16);
            var size = Math.Max(U32(at + 8), rawBytes);
            if (size == 0 || va < headers || (ulong)va + size > imageSize
                || (rawBytes > 0 && (raw < headers || (ulong)raw + rawBytes > (ulong)bytes.Length)))
                throw new InvalidDataException("Sezione PE fuori dai limiti.");
            if (sections.Any(s => (ulong)va < (ulong)s.Va + s.Size && (ulong)s.Va < (ulong)va + size
                || rawBytes > 0 && s.Bytes > 0 && (ulong)raw < (ulong)s.Raw + s.Bytes && (ulong)s.Raw < (ulong)raw + rawBytes))
                throw new InvalidDataException("Sezioni PE sovrapposte.");
            sections.Add((va, size, raw, rawBytes, U32(at + 36)));
        }
        int Map(uint rva, int length, bool code = false)
        {
            var matching = sections.Where(s => rva >= s.Va && (ulong)rva + (uint)length <= (ulong)s.Va + s.Bytes).ToArray();
            if (matching.Length != 1 || code && (matching[0].Flags & 0x20000000) == 0)
                throw new InvalidDataException("Indirizzo del runtime non mappato in una sezione valida.");
            return checked((int)(matching[0].Raw + rva - matching[0].Va));
        }
        _ = Map(0x35bcc7, 1, true); _ = Map(0x4901d0 - 0x400000, 1, true); _ = Map(0x57fb40 - 0x400000, 1, true);
        void Call(uint va, uint target)
        {
            var offset = Map(va - 0x400000, 5, true);
            var relative = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 1, 4));
            if (bytes[offset] != 0xe8 || (long)va + 5 + relative != target)
                throw new InvalidDataException($"Codice hook diverso a 0x{va:X8}: atteso CALL 0x{target:X8}.");
        }
        foreach (var va in TextureCallsites) Call(va, 0x4901d0);
        Call(0x57ed99, 0x57fb40);
        // Verify a bounded import table from the same bytes, rather than relying only on report text.
        if (U32(opt + 92) < 2) throw new InvalidDataException("Import Direct3D 9 assente.");
        var imports = U32(opt + 104); var importBytes = U32(opt + 108);
        if (importBytes is < 20 or > 40960) throw new InvalidDataException("Tabella import fuori limite.");
        var foundD3d = false; var terminated = false;
        for (var i = 0; i < importBytes / 20; i++)
        {
            var at = Map(checked(imports + (uint)i * 20), 20);
            if ((U32(at) | U32(at + 4) | U32(at + 8) | U32(at + 12) | U32(at + 16)) == 0) { terminated = true; break; }
            var name = U32(at + 12); var chars = new List<byte>();
            for (uint c = 0; c < 512; c++)
            {
                var value = bytes[Map(checked(name + c), 1)];
                if (value == 0) break;
                if (value is < 32 or > 126 || c == 511) throw new InvalidDataException("Nome import non valido.");
                chars.Add(value);
            }
            if (System.Text.Encoding.ASCII.GetString(chars.ToArray()).Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)) foundD3d = true;
        }
        if (!terminated || !foundD3d) throw new InvalidDataException("Import Direct3D 9 assente o tabella incompleta.");
    }
    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

public static class BuiltinAsphaltCatalog
{
    public const string Url = "https://github.com/luigip11/DLSS5_ReShade_for_games/releases/download/v0.1.0-alpha.8/GamePanel-AsphaltHD.zip";
    public const string Sha256 = "45fe214ba832782c2f46ed32265f5428bf34e82ac0c7bc3f1b5c4d9c11e3d67a";
    public const long Bytes = 8397130;
}

/// <summary>Downloads only on explicit request. Verified cache files are never executed or silently repaired.</summary>
public sealed class TextureRuntimeSource
{
    internal sealed record Pin(string Url, long Bytes, string Sha256);
    private readonly string _cacheRoot;
    private readonly Func<HttpClient> _client;
    private readonly Pin _zip;
    private readonly long _loaderBytes;
    private readonly string _loaderHash;
    private readonly string? _assetRoot;
    public TextureRuntimeSource(string? cacheRoot = null) : this(cacheRoot ?? Path.Combine(GamePanelPaths.Root, "Components", "TexWizard"),
        () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) },
        new(TextureRuntimeCatalog.ZipUrl, TextureRuntimeCatalog.ZipBytes, TextureRuntimeCatalog.ZipSha256),
        TextureRuntimeCatalog.LoaderBytes, TextureRuntimeCatalog.LoaderSha256, null) { }
    // Friend test assembly can supply deterministic HTTP fixtures. No production override or user setting.
    internal TextureRuntimeSource(string cacheRoot, Func<HttpClient> client, Pin zip, long loaderBytes, string loaderHash, string? assetRoot = null)
    { _cacheRoot = Path.GetFullPath(cacheRoot); _client = client; _zip = zip; _loaderBytes = loaderBytes; _loaderHash = loaderHash; _assetRoot = assetRoot; }

    public async Task<string> PrepareAsync(CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        using var lease = Lease(_cacheRoot);
        var archive = await EnsureArchiveAsync(_cacheRoot, "TexWizard_UG2-1.3.zip", _zip, token, progress).ConfigureAwait(false);
        var target = Path.Combine(_cacheRoot, "TexWizard.asi"); GuardPath(target);
        var payload = ExtractLoader(ReadVerified(archive, _zip.Bytes, _zip.Sha256, token), _loaderBytes, _loaderHash, token);
        if (AttributesOrMissing(target) is not null)
        {
            _ = ReadVerified(target, _loaderBytes, _loaderHash, token);
            return target;
        }
        var stage = target + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            GuardPath(stage);
            using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await output.WriteAsync(payload, token).ConfigureAwait(false); output.Flush(true); }
            _ = ReadVerified(stage, _loaderBytes, _loaderHash, token);
            token.ThrowIfCancellationRequested(); GuardPath(target); File.Move(stage, target, false);
            _ = ReadVerified(target, _loaderBytes, _loaderHash, token);
            progress?.Report(new("TexWizard 1.3 verificato (non eseguito)", 1, 1));
            return target;
        }
        finally { DeleteStage(stage); }
    }

    public async Task<PreparedAssetPack> PrepareBuiltinPackAsync(CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        // Refuse before any network or filesystem activity until release pins are filled.
        var pin = new Pin(BuiltinAsphaltCatalog.Url, BuiltinAsphaltCatalog.Bytes, BuiltinAsphaltCatalog.Sha256);
        ValidatePin(pin);
        var root = _assetRoot ?? GamePanelPaths.Root;
        var cache = Path.Combine(root, "Components", "AsphaltHD", "alpha8");
        using var lease = Lease(cache);
        var archive = await EnsureArchiveAsync(cache, "GamePanel-AsphaltHD.zip", pin, token, progress).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new AssetPackageImporter(Path.Combine(root, "AssetPacks")).Import(archive, token, progress);
    }

    private static FileStream Lease(string folder)
    {
        GuardPath(folder); Directory.CreateDirectory(folder); GuardPath(folder);
        var path = Path.Combine(folder, ".texture-download.lock"); GuardPath(path);
        if (AttributesOrMissing(path) is FileAttributes attributes && (attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new InvalidDataException("Lock cache non regolare.");
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private async Task<string> EnsureArchiveAsync(string folder, string name, Pin pin, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        ValidatePin(pin); token.ThrowIfCancellationRequested();
        var path = Path.Combine(folder, name); GuardPath(path);
        if (AttributesOrMissing(path) is not null) { _ = ReadVerified(path, pin.Bytes, pin.Sha256, token); return path; }
        var stage = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using var client = _client(); var uri = new Uri(pin.Url);
            for (var redirects = 0; ; redirects++)
            {
                ValidateUri(uri, pin.Url); token.ThrowIfCancellationRequested();
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri is Uri actual) ValidateUri(actual, pin.Url);
                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    if (redirects >= 3 || response.Headers.Location is null) throw new InvalidDataException("Troppi redirect nel download texture.");
                    uri = new Uri(uri, response.Headers.Location); ValidateUri(uri, pin.Url); continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Download texture non disponibile: " + response.StatusCode);
                if (response.Content.Headers.ContentLength is long length && length != pin.Bytes) throw new InvalidDataException("Dimensione download diversa dal catalogo.");
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                GuardPath(stage);
                using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536]; long total = 0;
                    while (true)
                    {
                        var count = await input.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break;
                        total = checked(total + count); if (total > pin.Bytes) throw new InvalidDataException("Download oltre il limite del catalogo.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        progress?.Report(new("Download componente texture", (int)total, (int)pin.Bytes));
                    }
                    output.Flush(true);
                }
                _ = ReadVerified(stage, pin.Bytes, pin.Sha256, token);
                token.ThrowIfCancellationRequested(); GuardPath(path); File.Move(stage, path, false);
                _ = ReadVerified(path, pin.Bytes, pin.Sha256, token); return path;
            }
        }
        finally { DeleteStage(stage); }
    }
    internal static void ValidateUri(Uri uri, string sourceUrl = TextureRuntimeCatalog.ZipUrl)
    {
        var source = new Uri(sourceUrl);
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Host == "release-assets.githubusercontent.com" || uri.Host == "github.com" && uri.AbsolutePath == source.AbsolutePath && uri.Query.Length == 0)
            || source.Scheme != "https" || source.Host != "github.com"
            || source.AbsoluteUri != TextureRuntimeCatalog.ZipUrl && source.AbsoluteUri != BuiltinAsphaltCatalog.Url)
            throw new InvalidDataException("Destinazione download texture non autorizzata.");
    }
    private static void ValidatePin(Pin pin)
    {
        if (pin.Bytes is < 1 or > 64 * 1024 * 1024 || !ValidHash(pin.Sha256))
            throw new InvalidDataException("Pacchetto texture non ancora disponibile: catalogo release incompleto.");
        ValidateUri(new Uri(pin.Url), pin.Url);
    }
    private static byte[] ReadVerified(string path, long bytes, string hash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); GuardPath(path);
        if (AttributesOrMissing(path) is not FileAttributes attributes || (attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("File cache non regolare.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (bytes is < 1 or > 64 * 1024 * 1024 || input.Length != bytes) throw CacheInvalid(path);
        var data = new byte[checked((int)bytes)];
        for (var offset = 0; offset < data.Length;)
        {
            token.ThrowIfCancellationRequested(); var count = input.Read(data, offset, Math.Min(65536, data.Length - offset));
            if (count == 0) throw CacheInvalid(path); offset += count;
        }
        token.ThrowIfCancellationRequested();
        if (input.ReadByte() != -1 || !TextureRuntimeCatalog.Digest(data).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw CacheInvalid(path);
        return data;
    }
    internal static byte[] ExtractLoader(byte[] archive, long expectedBytes, string expectedHash, CancellationToken token = default)
    {
        using var input = new MemoryStream(archive, writable: false); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 1 or > 32) throw new InvalidDataException("Numero voci TexWizard non valido.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); ZipArchiveEntry? selected = null;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested(); var name = entry.FullName; var directory = name.EndsWith('/');
            var relative = directory ? name[..^1] : name;
            if (ValidateRelative(relative) != relative || !names.Add(relative)) throw new InvalidDataException("Percorsi ZIP TexWizard ambigui.");
            var kind = (entry.ExternalAttributes >> 16) & 0xf000;
            if (kind is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || kind == 0x4000 && !directory || kind == 0x8000 && directory || entry.Length < 0 || entry.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("Voce speciale o dimensione ZIP TexWizard non valida.");
            if (name.Equals(TextureRuntimeCatalog.Loader, StringComparison.OrdinalIgnoreCase))
            { if (name != TextureRuntimeCatalog.Loader || selected is not null || directory) throw new InvalidDataException("ASI TexWizard ambiguo."); selected = entry; }
        }
        if (selected is null || selected.Length != expectedBytes || expectedBytes is < 1 or > 1024 * 1024)
            throw new InvalidDataException("ASI TexWizard mancante o di dimensione diversa.");
        using var source = selected.Open(); var data = new byte[(int)expectedBytes];
        for (var offset = 0; offset < data.Length;)
        { token.ThrowIfCancellationRequested(); var count = source.Read(data, offset, Math.Min(65536, data.Length - offset)); if (count == 0) throw new InvalidDataException("ASI TexWizard troncato."); offset += count; }
        if (source.ReadByte() != -1 || !TextureRuntimeCatalog.Digest(data).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 ASI TexWizard diverso dal catalogo.");
        return data;
    }
    private static InvalidDataException CacheInvalid(string path) => new("Cache texture alterata o incompleta: " + path + ". Rimuovi esplicitamente questo file cache prima di scaricarlo di nuovo.");
    private static void DeleteStage(string path) { GuardPath(path); if (File.Exists(path)) File.Delete(path); }
}
