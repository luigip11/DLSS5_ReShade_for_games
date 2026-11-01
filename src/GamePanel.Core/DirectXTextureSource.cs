using System.Net;
using System.Security.Cryptography;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

/// <summary>Downloads the pinned x86 ASI as data, only after an explicit user action.</summary>
public sealed class DirectXTextureSource
{
    internal sealed record Pin(long Bytes, string Sha256);
    private readonly string _cacheRoot;
    private readonly Func<HttpClient> _client;
    private readonly Pin _pin;

    public DirectXTextureSource(string? cacheRoot = null) : this(
        cacheRoot ?? Path.Combine(GamePanelPaths.Root, "Components", "TextureToolkit", DirectXTextureCatalog.Version),
        () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) },
        new(DirectXTextureCatalog.LoaderBytes, DirectXTextureCatalog.LoaderSha256)) { }

    // Only the friend test assembly supplies synthetic bytes/HTTP; there is no production override.
    internal DirectXTextureSource(string cacheRoot, Func<HttpClient> client, Pin pin)
    { _cacheRoot = Path.GetFullPath(cacheRoot); _client = client; _pin = pin; }

    public async Task<string> PrepareAsync(CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        if (_pin.Bytes is < 1 or > 16 * 1024 * 1024 || !ValidHash(_pin.Sha256))
            throw new InvalidDataException("Catalogo DirectX incompleto.");
        GuardPath(_cacheRoot); Directory.CreateDirectory(_cacheRoot); GuardPath(_cacheRoot);
        var leasePath = Path.Combine(_cacheRoot, ".directx-download.lock"); GuardPath(leasePath);
        if (AttributesOrMissing(leasePath) is FileAttributes la && (la & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new InvalidDataException("Lock cache non regolare.");
        using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = Path.Combine(_cacheRoot, "TextureToolkit-x86.asi");
        GuardPath(path);
        if (AttributesOrMissing(path) is not null) { Verify(path, _pin, token); return path; }
        var stage = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using var client = _client(); var uri = new Uri(DirectXTextureCatalog.LoaderUrl);
            for (var redirects = 0; ; redirects++)
            {
                token.ThrowIfCancellationRequested(); ValidateUri(uri);
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri is Uri actual) ValidateUri(actual);
                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    if (redirects >= 3 || response.Headers.Location is null) throw new InvalidDataException("Troppi redirect nel download DirectX.");
                    uri = new Uri(uri, response.Headers.Location); ValidateUri(uri); continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Download DirectX non disponibile: " + response.StatusCode);
                if (response.Content.Headers.ContentLength is long declared && declared != _pin.Bytes)
                    throw new InvalidDataException("Dimensione download DirectX diversa dal catalogo.");
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                GuardPath(stage);
                using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536]; long total = 0;
                    while (true)
                    {
                        var count = await input.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break;
                        total = checked(total + count);
                        if (total > _pin.Bytes) throw new InvalidDataException("Download DirectX oltre il limite del catalogo.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        progress?.Report(new("Download componente DirectX", (int)total, (int)_pin.Bytes));
                    }
                    output.Flush(true);
                }
                Verify(stage, _pin, token);
                token.ThrowIfCancellationRequested(); GuardPath(path); File.Move(stage, path, false);
                Verify(path, _pin, token);
                progress?.Report(new("Texture Toolkit verificato; nessun codice eseguito", 1, 1));
                return path;
            }
        }
        finally { GuardPath(stage); if (File.Exists(stage)) File.Delete(stage); }
    }

    internal static void ValidateUri(Uri uri)
    {
        var source = new Uri(DirectXTextureCatalog.LoaderUrl);
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Host == "release-assets.githubusercontent.com" || uri.Host == "github.com" && uri.AbsolutePath == source.AbsolutePath && uri.Query.Length == 0))
            throw new InvalidDataException("Destinazione download DirectX non autorizzata.");
    }

    private static void Verify(string path, Pin pin, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); GuardPath(path);
        if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("File cache DirectX non regolare.");
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
    private static InvalidDataException InvalidCache(string path) => new("Cache DirectX alterata o incompleta: " + path + ". Rimuovi esplicitamente questo file cache prima di scaricarlo di nuovo.");
}
