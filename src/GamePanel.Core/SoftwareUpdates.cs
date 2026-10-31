using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record ReleaseAsset(long Id, string Name, string Url, long Bytes, string Sha256);
public sealed record SoftwareRelease(long Id, string Version, string Tag, ReleaseAsset Package, ReleaseAsset Manifest);
public sealed record UpdateCheck(bool PublishedReleasesExist, SoftwareRelease? Update);
public sealed record SoftwareManifest(int SchemaVersion, string Version, string PackageFileName, long PackageBytes, string PackageSha256, string Commit);
public sealed record PreparedSoftwareUpdate(string Folder, string ReadyDirectory, SoftwareManifest Manifest);

public static class PackageInventory
{
    public const string ManifestName = "FILES-SHA256SUMS.txt";
    public const int MaximumFiles = 1400;
    public static readonly string[] Required = ["GamePanel.exe", "GamePanel.dll", "GamePanel.deps.json", "GamePanel.runtimeconfig.json", "GamePanel.Core.dll", "GamePanel.Updater.exe", "GamePanel.Updater.dll", "GamePanel.Updater.deps.json", "GamePanel.Updater.runtimeconfig.json"];
    public static string PathName(string relative)
    {
        var name = ValidateRelative(relative);
        var first = name.Split('/')[0];
        if (new[] { "Library", "Backups", "Profiles", "Components", "Updates", "Settings", "library.json", "d3d9.dll", "dinput8.dll", "dxgi.dll", "speed2.exe" }.Contains(first, StringComparer.OrdinalIgnoreCase)
            || first.StartsWith(".gamepanel", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Il pacchetto contiene dati o componenti di gioco non ammessi.");
        return name;
    }
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        if (text.Length > 512 * 1024) throw new InvalidDataException("Manifest del pacchetto troppo grande.");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n').Select(s => s.TrimEnd('\r')).Where(s => s.Length > 0))
        {
            if (line.Length < 67 || line.Substring(64, 2) != "  " || !ValidHash(line[..64])) throw new InvalidDataException("Manifest SHA-256 non valido.");
            var name = PathName(line[66..]);
            if (name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || !files.TryAdd(name, line[..64].ToLowerInvariant()) || files.Count > MaximumFiles)
                throw new InvalidDataException("Voci duplicate o non valide nel pacchetto.");
        }
        if (files.Count == 0) throw new InvalidDataException("Pacchetto vuoto.");
        return files;
    }
    public static IReadOnlyDictionary<string, string> Read(string folder, bool requireUpdater = true)
    {
        var path = GameFile(folder, ManifestName); if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("Manifest troppo grande.");
        var files = Parse(File.ReadAllText(path));
        if (requireUpdater && Required.Any(p => !files.ContainsKey(p))) throw new InvalidDataException("Il pacchetto non contiene il programma e l'updater completi.");
        return files;
    }
    public static void Verify(string folder, IReadOnlyDictionary<string, string> files, CancellationToken token = default)
    {
        foreach (var (name, hash) in files)
        { token.ThrowIfCancellationRequested(); if (HashFile(GameFile(folder, name), token) != hash) throw new InvalidDataException("File del pacchetto alterato: " + name); }
    }
}

public sealed class SoftwareUpdates
{
    public const string Repository = "luigip11/DLSS5_ReShade_for_games";
    public const string ReleasesUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=30";
    public const string PackageName = "GamePanel-win-x64.zip";
    public const string MetadataName = "GamePanel-update.json";
    public const long MaximumZipBytes = 256L * 1024 * 1024;
    public const long MaximumUnpackedBytes = 700L * 1024 * 1024;
    private readonly Func<HttpClient> _client;
    public SoftwareUpdates() : this(() => new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = TimeSpan.FromMinutes(15) }) { }
    internal SoftwareUpdates(Func<HttpClient> client) => _client = client;
    public async Task<UpdateCheck> CheckAsync(AppVersion current, CancellationToken token = default)
    {
        using var client = Client();
        using var response = await client.GetAsync(ReleasesUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429) throw new HttpRequestException("GitHub limita temporaneamente i controlli: riprova più tardi.");
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBounded(response, 1024 * 1024, token).ConfigureAwait(false);
        return ParseReleases(bytes, current);
    }
    internal static UpdateCheck ParseReleases(byte[] json, AppVersion current)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 30) throw new InvalidDataException("Risposta GitHub non valida.");
        var candidates = new List<(JsonElement Data, AppVersion Version)>(); var any = false;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue; any = true;
            AppVersion version;
            try { version = AppVersion.Parse(release.GetProperty("tag_name").GetString() ?? ""); } catch (InvalidDataException) { continue; }
            if (current.PreRelease is null && version.PreRelease is not null || version.CompareTo(current) <= 0) continue;
            candidates.Add((release, version));
        }
        if (candidates.Count == 0) return new(any, null);
        var best = candidates.OrderByDescending(c => c.Version).First(); var tag = best.Data.GetProperty("tag_name").GetString()!;
        ReleaseAsset Asset(string name, long maximum)
        {
            var matches = best.Data.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("La nuova release è incompleta: manca " + name);
            var a = matches[0]; var size = a.GetProperty("size").GetInt64(); var digest = a.GetProperty("digest").GetString(); var url = a.GetProperty("browser_download_url").GetString()!;
            if (size < 1 || size > maximum || digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !ValidHash(digest[7..])) throw new InvalidDataException("La release non espone dimensioni e hash verificabili.");
            var expected = "https://github.com/" + Repository + "/releases/download/" + tag + "/" + name;
            if (url != expected) throw new InvalidDataException("Origine della release inattesa.");
            return new(a.GetProperty("id").GetInt64(), name, url, size, digest[7..].ToLowerInvariant());
        }
        return new(any, new(best.Data.GetProperty("id").GetInt64(), best.Version.ToString(), tag, Asset(PackageName, MaximumZipBytes), Asset(MetadataName, 64 * 1024)));
    }
    public async Task<PreparedSoftwareUpdate> PrepareAsync(SoftwareRelease release, string updatesRoot, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var root = System.IO.Path.GetFullPath(updatesRoot); GuardPath(root); Directory.CreateDirectory(root); GuardPath(root);
        var folder = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            using var client = Client();
            var manifestPath = System.IO.Path.Combine(folder, MetadataName);
            await Download(client, release.Manifest, manifestPath, token, progress).ConfigureAwait(false);
            var m = JsonSerializer.Deserialize<SoftwareManifest>(File.ReadAllBytes(manifestPath)) ?? throw new InvalidDataException("Metadata aggiornamento mancanti.");
            if (m.SchemaVersion != 1 || m.Version != release.Version || m.PackageFileName != PackageName || m.PackageBytes != release.Package.Bytes
                || m.PackageSha256 != release.Package.Sha256 || m.Commit is null || m.Commit.Length != 40 || !m.Commit.All(Uri.IsHexDigit))
                throw new InvalidDataException("Metadata e asset GitHub non corrispondono.");
            var zip = System.IO.Path.Combine(folder, PackageName);
            await Download(client, release.Package, zip, token, progress).ConfigureAwait(false);
            var ready = System.IO.Path.Combine(folder, "ready");
            Extract(zip, ready, token);
            return new(folder, ready, m);
        }
        catch { GuardPath(folder); Directory.Delete(folder, true); throw; }
    }
    internal static void Extract(string zipPath, string ready, CancellationToken token = default)
    {
        GuardPath(zipPath); GuardPath(ready); if (Directory.Exists(ready)) throw new InvalidDataException("Cartella di staging già presente.");
        Directory.CreateDirectory(ready);
        using var stream = File.OpenRead(zipPath); using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > PackageInventory.MaximumFiles + 150) throw new InvalidDataException("Troppe voci nell'aggiornamento.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long bytes = 0;
        foreach (var entry in archive.Entries)
        {
            var raw = entry.FullName.Replace('\\', '/');
            if (raw.EndsWith('/')) { PackageInventory.PathName(raw.TrimEnd('/')); continue; }
            var name = PackageInventory.PathName(raw);
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || !entries.TryAdd(name, entry)) throw new InvalidDataException("Link o percorso duplicato nell'aggiornamento.");
            bytes = checked(bytes + entry.Length);
            if (entry.Length > 128L * 1024 * 1024 || bytes > MaximumUnpackedBytes) throw new InvalidDataException("Aggiornamento oltre il limite di estrazione.");
        }
        if (!entries.TryGetValue(PackageInventory.ManifestName, out var manifest) || manifest.Length > 512 * 1024) throw new InvalidDataException("Manifest aggiornamento assente o troppo grande.");
        string text; using (var reader = new StreamReader(manifest.Open(), Encoding.UTF8)) text = reader.ReadToEnd();
        var expected = PackageInventory.Parse(text);
        if (entries.Count != expected.Count + 1 || expected.Keys.Any(p => !entries.ContainsKey(p)) || PackageInventory.Required.Any(p => !expected.ContainsKey(p))) throw new InvalidDataException("Elenco dei file aggiornamento non corrispondente.");
        foreach (var (name, entry) in entries)
        {
            token.ThrowIfCancellationRequested(); var target = GameFile(ready, name); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); GuardPath(target);
            using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long total = 0; int count;
            while ((count = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); total += count; if (total > entry.Length) throw new InvalidDataException("Voce ZIP oltre la dimensione dichiarata."); output.Write(buffer, 0, count); }
            if (total != entry.Length) throw new InvalidDataException("Estrazione incompleta."); output.Flush(true);
        }
        PackageInventory.Verify(ready, expected, token);
    }
    private HttpClient Client()
    { var client = _client(); client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GamePanel", AppVersion.Current.ToString())); client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json")); return client; }
    private static void Allowed(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !(uri.Host == "release-assets.githubusercontent.com"
            || uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal))) throw new InvalidDataException("Redirect aggiornamento non autorizzato.");
    }
    private static async Task Download(HttpClient client, ReleaseAsset asset, string destination, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var uri = new Uri(asset.Url);
        for (var redirects = 0; ; redirects++)
        {
            Allowed(uri); using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and <= 399)
            { if (redirects >= 3 || response.Headers.Location is null) throw new InvalidDataException("Troppi redirect per l'aggiornamento."); uri = new Uri(uri, response.Headers.Location); continue; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != asset.Bytes) throw new InvalidDataException("Dimensione aggiornamento diversa da GitHub.");
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                total += count; if (total > asset.Bytes) throw new InvalidDataException("Download aggiornamento oltre il limite."); digest.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                progress?.Report(new("Scaricamento " + asset.Name, (int)(total / 1024), (int)(asset.Bytes / 1024)));
            }
            output.Flush(true);
            if (total != asset.Bytes || Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant() != asset.Sha256) throw new InvalidDataException("SHA-256 aggiornamento diverso da GitHub.");
            return;
        }
    }
    private static async Task<byte[]> ReadBounded(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Risposta aggiornamento troppo grande.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false); using var output = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { if (output.Length + count > maximum) throw new InvalidDataException("Risposta aggiornamento oltre il limite."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
}
