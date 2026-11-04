using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed class Underground1DisplayFix
{
    public const string ExecutableSha256 = "c0fad450912952f53809a54690d235aa8b4e25b9f46650693c7cbee1b4c074b7";
    public const string Url = "https://github.com/ThirteenAG/WidescreenFixesPack/releases/download/nfsu/NFSUnderground.WidescreenFix.zip";
    public const string Sha256 = "c4ea2a23caa5f34f52afd35a8a1e4da58a439eafe45dc605ceaa6685fc7e69b9";
    public const int Bytes = 8264601;
    internal static readonly IReadOnlyDictionary<string, (int Bytes, string Hash)> Entries = new Dictionary<string, (int, string)>
    {
        ["dinput8.dll"] = (5418336, "d5a059aa467a7a7127c8f6169f79fa63ff0f55986ee9eb2fd9a281bebf2aa2e6"),
        ["scripts/NFSUnderground.WidescreenFix.asi"] = (5552000, "b5415cf48b13ba42cd9064dad6c37db79fb7874b2d0945f5e6ec35621b52e7ed"),
        ["scripts/NFSUnderground.WidescreenFix.dat"] = (21944, "148d2e4c7e193d11f7946d84730c2d5471663e62ffa3e65f69e1ea88868a1aa2"),
        ["scripts/NFSUnderground.WidescreenFix.ini"] = (3729, "21074e7c95c247fdaadb5058e07666f18f95fdd7ab8cffb789a1f86d58d13b8c"),
        ["scripts/NFSUnderground.WidescreenFix.tpk"] = (42704, "aa0fc803dff6d066b3e9879033d3e401e7f36f42503ee153e8dcf5b3b355d3af")
    };
    internal IReadOnlyDictionary<string, byte[]> Files { get; }
    internal Underground1DisplayFix(IReadOnlyDictionary<string, byte[]> files) => Files = files;
    public static Underground1DisplayFix Read(string path)
    {
        GuardPath(path);
        if (new FileInfo(path).Length != Bytes) throw new InvalidDataException("Dimensione del fix UG1 diversa dal catalogo.");
        var data = File.ReadAllBytes(path);
        if (Digest(data) != Sha256) throw new InvalidDataException("Archivio UG1 diverso dalla versione verificata.");
        using var zip = new ZipArchive(new MemoryStream(data));
        if (zip.Entries.Count > 16 || zip.Entries.Count(e => !e.FullName.EndsWith('/')) != Entries.Count) throw new InvalidDataException("Inventario del fix UG1 non valido.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, pin) in Entries)
        {
            var matches = zip.Entries.Where(e => e.FullName == name).ToArray();
            if (matches.Length != 1 || matches[0].Length != pin.Bytes) throw new InvalidDataException("Membro UG1 non valido: " + name);
            using var input = matches[0].Open(); using var output = new MemoryStream(); input.CopyTo(output);
            var bytes = output.ToArray();
            if (bytes.Length != pin.Bytes || Digest(bytes) != pin.Hash) throw new InvalidDataException("Hash UG1 non valido: " + name);
            files[name] = bytes;
        }
        // Keep this display-only: preserve the boot flow and original camera/controller behavior.
        files["scripts/NFSUnderground.WidescreenFix.ini"] = Configuration;
        return new(files);
    }
    internal static byte[] Configuration => Encoding.UTF8.GetBytes("[MAIN]\r\nFixHUD=1\r\nFixFOV=1\r\nScaling=0\r\nHudAspectRatioConstraint=Auto\r\nFMVWidescreenMode=1\r\nAutoFitFMV=1\r\n[MISC]\r\nSkipIntro=0\r\nShowLangSelect=0\r\nWindowedMode=0\r\nHideDebugObjects=0\r\nCustomUserFilesDirectoryInGameDir=0\r\nImproveGamepadSupport=3\r\nForcedAudioSampleRate=44100\r\nFPSLimit=0\r\nBlackMagazineFix=0\r\n[CAMERA]\r\nEnable=0\r\n[GRAPHICS]\r\nConsoleGamma=0\r\nSMAA=0\r\nHideAABug=2\r\n");
    public static async Task<Underground1DisplayFix> DownloadAsync(string cache, CancellationToken token = default)
    {
        GuardPath(cache); Directory.CreateDirectory(cache); GuardPath(cache);
        var target = Path.Combine(cache, "ug1-display-" + Sha256 + ".zip"); GuardPath(target);
        if (File.Exists(target)) return Read(target);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) };
            var uri = new Uri(Url);
            for (var redirects = 0; ; redirects++)
            {
                WidescreenPackageSource.ValidateUri(uri);
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (redirects >= 3 || response.Headers.Location is null) throw new InvalidDataException("Redirect UG1 non valido.");
                    uri = new Uri(uri, response.Headers.Location); continue;
                }
                response.EnsureSuccessStatusCode();
                using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536]; long count = 0; int read;
                    while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    { count += read; if (count > Bytes) throw new InvalidDataException("Download UG1 troppo grande."); await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); }
                }
                var package = Read(temporary); token.ThrowIfCancellationRequested(); File.Move(temporary, target); return package;
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
