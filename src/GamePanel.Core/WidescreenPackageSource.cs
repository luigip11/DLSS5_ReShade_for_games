using System.IO.Compression;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public static class WidescreenCatalog
{
    public const string Version = "nfsu2-558911953";
    public const string Url = "https://github.com/ThirteenAG/WidescreenFixesPack/releases/download/nfsu2/NFSUnderground2.WidescreenFix.zip";
    public const string Sha256 = "791fcf486db6916642b02a51dd6ed786da7bdbd6c4ad7e207c36ba1f0a5cca59";
    public const long Bytes = 8832311;
    public const string Loader = "dinput8.dll";
    public const string Plugin = "scripts/NFSUnderground2.WidescreenFix.asi";
    public const string Data = "scripts/NFSUnderground2.WidescreenFix.dat";
    public const string Ini = "scripts/NFSUnderground2.WidescreenFix.ini";
    public static readonly IReadOnlyList<string> Targets = Array.AsReadOnly(new[] { Loader, Plugin, Data, Ini });
    internal static readonly IReadOnlyDictionary<string, (long Bytes, string Hash)> Entries = new Dictionary<string, (long, string)>
    {
        [Loader] = (5418336, "d5a059aa467a7a7127c8f6169f79fa63ff0f55986ee9eb2fd9a281bebf2aa2e6"),
        [Plugin] = (6695040, "e7bdceeca0343fa394ac202aea0e48492a8a707cffa694e5a07aa98ba4a40f19"),
        [Data] = (5736, "2b8a08a7d99d4f3f28dd7adbaa0a49136688b948558fa5f3357723e0213b03f2"),
        [Ini] = (4508, "cf5d7e58230dd92d0bde5d1a0d7ae5dd7f0b4173fce578c17251d6acb6bea430")
    };
    // Only display/HUD corrections enabled. No optical-drive, save/registry,
    // camera, controller or language changes requested by GamePanel.
    public static byte[] Configuration => Encoding.UTF8.GetBytes("""
        ; GamePanel display profile. Upstream Widescreen Fix by ThirteenAG (MIT).
        ; Native modes are selected in the game's video menu; no obsolete ResX/ResY.
        [LANGUAGE]
        Language=
        [MAIN]
        FixHUD=1
        FixFOV=1
        Scaling=1
        HudAspectRatioConstraint=Auto
        FMVWidescreenMode=0
        FEScale=1.00
        FMVScale=1.00
        AutoFitFE=0
        AutoFitFMV=0
        [MISC]
        SkipIntro=0
        WindowedMode=0
        DisableCutsceneBorders=0
        CustomUserFilesDirectoryInGameDir=0
        WriteSettingsToFile=0
        ImproveGamepadSupport=4
        LeftStickDeadzone=10.0
        RightStickDeadzone=10.0
        FPSLimit=0
        HighFPSCutscenes=0
        SingleCoreAffinity=0
        NoOpticalDriveFix=0
        RainDropletsScale=1.0
        [CAMERA]
        Enable=0
        [GRAPHICS]
        ConsoleGamma=0
        SMAA=0
        HideAABug=2
        [NOSTrail]
        FixNOSTrailLength=1
        CustomNOSTrailLength=1.0
        """);
}

public sealed class VerifiedWidescreen
{
    internal VerifiedWidescreen(IReadOnlyDictionary<string, byte[]> files) => Files = files;
    internal IReadOnlyDictionary<string, byte[]> Files { get; }
}

public sealed class WidescreenPackageSource
{
    private readonly string _cache;
    private readonly Func<HttpClient> _client;
    public WidescreenPackageSource(string cache) : this(cache, () => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) }) { }
    internal WidescreenPackageSource(string cache, Func<HttpClient> client) { _cache = Path.GetFullPath(cache); _client = client; }
    public async Task<VerifiedWidescreen> PrepareAsync(CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested(); GuardPath(_cache); Directory.CreateDirectory(_cache); GuardPath(_cache);
        var lockPath = Path.Combine(_cache, ".widescreen.lock"); GuardPath(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = Path.Combine(_cache, WidescreenCatalog.Version + ".zip"); GuardPath(path);
        if (AttributesOrMissing(path) is null || new FileInfo(path).Length != WidescreenCatalog.Bytes || HashFile(path, token) != WidescreenCatalog.Sha256)
        {
            if (File.Exists(path)) File.Delete(path);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using var client = _client();
                var uri = new Uri(WidescreenCatalog.Url);
                HttpResponseMessage? response = null;
                try
                {
                    for (var redirects = 0; ; redirects++)
                    {
                        ValidateUri(uri);
                        response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                        if ((int)response.StatusCode is >= 300 and <= 399)
                        {
                            if (redirects >= 3 || response.Headers.Location is null) throw new InvalidDataException("Troppi redirect durante il download widescreen.");
                            var next = new Uri(uri, response.Headers.Location); ValidateUri(next); response.Dispose(); response = null; uri = next; continue;
                        }
                        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Download widescreen non disponibile: " + response.StatusCode);
                        if (response.Content.Headers.ContentLength is long length && length != WidescreenCatalog.Bytes) throw new InvalidDataException("Dimensione del fix diversa dal catalogo.");
                        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[64 * 1024]; long total = 0; int count;
                            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                            {
                                total += count; if (total > WidescreenCatalog.Bytes) throw new InvalidDataException("Pacchetto widescreen oltre il limite.");
                                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                                progress?.Report(new("Download Widescreen Fix", (int)total, (int)WidescreenCatalog.Bytes));
                            }
                            output.Flush(true);
                        }
                        if (new FileInfo(temporary).Length != WidescreenCatalog.Bytes || HashFile(temporary, token) != WidescreenCatalog.Sha256)
                            throw new InvalidDataException("SHA-256 del fix diverso dalla versione approvata. Nessuna installazione.");
                        token.ThrowIfCancellationRequested(); GuardPath(path); File.Move(temporary, path, false); break;
                    }
                }
                finally { response?.Dispose(); }
            }
            finally { GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
        }
        token.ThrowIfCancellationRequested();
        return Extract(File.ReadAllBytes(path));
    }
    internal static void ValidateUri(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || (uri.Host != "release-assets.githubusercontent.com" && !(uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/ThirteenAG/WidescreenFixesPack/", StringComparison.Ordinal))))
            throw new InvalidDataException("Destinazione del download widescreen non autorizzata.");
    }
    internal static VerifiedWidescreen Extract(byte[] data)
    {
        if (data.LongLength != WidescreenCatalog.Bytes || Digest(data) != WidescreenCatalog.Sha256) throw new InvalidDataException("Archivio widescreen non approvato.");
        using var input = new MemoryStream(data); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 16) throw new InvalidDataException("Archivio widescreen con troppe voci.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, expected) in WidescreenCatalog.Entries)
        {
            var entries = zip.Entries.Where(e => e.FullName == path).ToArray();
            if (entries.Length != 1 || entries[0].Length != expected.Bytes) throw new InvalidDataException("Voce del fix non valida: " + path);
            using var source = entries[0].Open(); using var output = new MemoryStream(); var buffer = new byte[64 * 1024]; int count;
            while ((count = source.Read(buffer)) > 0) { if (output.Length + count > expected.Bytes) throw new InvalidDataException("Voce del fix oltre il limite."); output.Write(buffer, 0, count); }
            var bytes = output.ToArray(); if (bytes.LongLength != expected.Bytes || Digest(bytes) != expected.Hash) throw new InvalidDataException("Hash del componente widescreen non valido: " + path);
            if (path is WidescreenCatalog.Loader or WidescreenCatalog.Plugin)
            {
                using var image = new PEReader(new MemoryStream(bytes));
                if (!image.PEHeaders.IsDll || image.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.I386) throw new InvalidDataException("Il fix richiede componenti x86.");
            }
            files.Add(path, bytes);
        }
        // Do not deploy the upstream options that also change disc/save/camera behavior.
        files[WidescreenCatalog.Ini] = WidescreenCatalog.Configuration;
        return new(files);
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
