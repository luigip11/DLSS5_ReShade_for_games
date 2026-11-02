using System.Net;
using System.Security.Cryptography;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

/// <summary>Downloads the pinned author RAR as inert data after an explicit user action.</summary>
public sealed class SmolModelSource
{
    public const long MaximumArchiveBytes = 32L * 1024 * 1024;
    public const long ArchiveBytes = 18424342;
    public const string ArchiveSha256 = "385e5a706266b252618b4018cba8970c14084481a3d6700d76b7f26df044282e";
    public const string DownloadUrl = "https://nfsmods.xyz/mod/uploads/mods/2721/fc7ebd-%5BNFSU2-Misc%5D%20the%20smol%20model%20fix%20%5BNFS%20.R%20Garage%20-%20www.nfsridescr.blogspot.com%5D.rar";
    public const string SourceUrl = "https://nfsmods.xyz/mod/2721";
    public static void VerifyArchive(string path, CancellationToken token = default) => Verify(path, new(ArchiveBytes, ArchiveSha256), token);

    internal sealed record Pin(long Bytes, string Sha256);
    private readonly string _cacheRoot;
    private readonly Func<HttpClient> _client;
    private readonly Pin _pin;

    public SmolModelSource(string? cacheRoot = null) : this(
        cacheRoot ?? Path.Combine(GamePanelPaths.Root, "Components", "Smol", ArchiveSha256),
        () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) },
        new(ArchiveBytes, ArchiveSha256)) { }

    // Only the friend test assembly supplies synthetic bytes/HTTP; there is no production override.
    internal SmolModelSource(string cacheRoot, Func<HttpClient> client, Pin pin)
    { _cacheRoot = Path.GetFullPath(cacheRoot); _client = client; _pin = pin; }

    public async Task<string> PrepareAsync(CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        if (_pin.Bytes is < 1 or > MaximumArchiveBytes || !ValidHash(_pin.Sha256))
            throw new InvalidDataException("Catalogo Smol incompleto.");
        GuardPath(_cacheRoot); Directory.CreateDirectory(_cacheRoot); GuardPath(_cacheRoot);
        var leasePath = Path.Combine(_cacheRoot, ".smol-download.lock"); GuardPath(leasePath);
        if (AttributesOrMissing(leasePath) is FileAttributes la && (la & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new InvalidDataException("Lock cache non regolare.");
        using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = Path.Combine(_cacheRoot, "smol-model-fix.rar");
        GuardPath(path);
        if (AttributesOrMissing(path) is not null) { Verify(path, _pin, token); return path; }
        var stage = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using var client = _client(); var uri = new Uri(DownloadUrl);
            for (var redirects = 0; ; redirects++)
            {
                token.ThrowIfCancellationRequested(); ValidateUri(uri);
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri is Uri actual) ValidateUri(actual);
                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    if (redirects >= 3 || response.Headers.Location is null) throw new InvalidDataException("Troppi redirect nel download Smol.");
                    uri = new Uri(uri, response.Headers.Location); ValidateUri(uri); continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Download Smol non disponibile (puoi selezionare il RAR originale già scaricato): " + response.StatusCode);
                if (response.Content.Headers.ContentLength is long declared && declared != _pin.Bytes)
                    throw new InvalidDataException("Dimensione download Smol diversa dal catalogo.");
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                GuardPath(stage);
                using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536]; long total = 0;
                    while (true)
                    {
                        var count = await input.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break;
                        total = checked(total + count);
                        if (total > _pin.Bytes) throw new InvalidDataException("Download Smol oltre il limite del catalogo.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        progress?.Report(new("Download componente Smol", (int)total, (int)_pin.Bytes));
                    }
                    output.Flush(true);
                }
                Verify(stage, _pin, token);
                token.ThrowIfCancellationRequested(); GuardPath(path); File.Move(stage, path, false);
                Verify(path, _pin, token);
                progress?.Report(new("Archivio Smol verificato; nessuno script eseguito", 1, 1));
                return path;
            }
        }
        finally { GuardPath(stage); if (File.Exists(stage)) File.Delete(stage); }
    }

    internal static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri != new Uri(DownloadUrl))
            throw new InvalidDataException("Destinazione download Smol non autorizzata.");
    }

    private static void Verify(string path, Pin pin, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); GuardPath(path);
        if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("File cache Smol non regolare.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != pin.Bytes) throw InvalidCache(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = input.Read(buffer, 0, buffer.Length); if (count == 0) break;
            total = checked(total + count); if (total > pin.Bytes) throw InvalidCache(path); hash.AppendData(buffer, 0, count);
        }
        if (total != pin.Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase))
            throw InvalidCache(path);
    }
    private static InvalidDataException InvalidCache(string path) => new("Cache Smol alterata o incompleta: " + path + ". Rimuovi esplicitamente questo file cache prima di scaricarlo di nuovo.");
}
