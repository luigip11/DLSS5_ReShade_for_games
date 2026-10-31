using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using static GamePanel.Core.BackupService;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GamePanel.Checks")]

namespace GamePanel.Core;

public static class ReShadeCatalog
{
    public const string Version = "6.8.0";
    public const string SetupUrl = "https://reshade.me/downloads/ReShade_Setup_6.8.0.exe";
    public const string CertificateThumbprint = "589690208A5E52FB96980C4A6698F50ACD47C49F";
    public const string SetupSha256 = "207aea16205fbf952bc8fe1879966672454cf04002e7ad34237c7990a5b3c0b4";
    public const string DllSha256 = "538998f66c0197adcdeadfe8e5ea19dd6ca3253e40399476d291f385ce518b88";
    public const long SetupBytes = 4075064;
    public const long DllBytes = 4136216;
}

public sealed class VerifiedReShade
{
    internal VerifiedReShade(string path, string sha256) { Path = path; Sha256 = sha256; }
    internal string Path { get; }
    internal string Sha256 { get; }
}

/// <summary>Explicit download of one immutable official package. Never executes the setup.
/// Exact SHA-256 approval is separate from Windows publisher trust.</summary>
public sealed class ReShadePackageSource
{
    private readonly string _cache;
    private readonly Func<HttpClient> _client;
    public static string DefaultCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePanel", "Components");
    public ReShadePackageSource(string cache) : this(cache, () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) }) { }
    internal ReShadePackageSource(string cache, Func<HttpClient> client) { _cache = Path.GetFullPath(cache); _client = client; }

    public async Task<VerifiedReShade> PrepareAsync(CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidHash(ReShadeCatalog.DllSha256)) throw new InvalidOperationException("Il catalogo ReShade non è stato verificato per questa build.");
        GuardPath(_cache); Directory.CreateDirectory(_cache); GuardPath(_cache);
        var lockPath = Path.Combine(_cache, ".component.lock"); GuardPath(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var dll = Path.Combine(_cache, "ReShade32-" + ReShadeCatalog.Version + ".dll"); GuardPath(dll);
        if (Matches(dll, ReShadeCatalog.DllSha256, ReShadeCatalog.DllBytes, token)) return new(dll, ReShadeCatalog.DllSha256);
        // Only these two version-specific cache files are owned here; no recursive extraction.
        if (AttributesOrMissing(dll) is not null) File.Delete(dll);
        var setup = Path.Combine(_cache, "ReShade_Setup_" + ReShadeCatalog.Version + ".exe"); GuardPath(setup);
        if (!Matches(setup, ReShadeCatalog.SetupSha256, ReShadeCatalog.SetupBytes, token))
        {
            if (AttributesOrMissing(setup) is not null) File.Delete(setup);
            var temporary = setup + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using var client = _client();
                using var response = await client.GetAsync(ReShadeCatalog.SetupUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK || response.RequestMessage?.RequestUri?.AbsoluteUri != ReShadeCatalog.SetupUrl)
                    throw new InvalidDataException("Download ReShade rifiutato: risposta o reindirizzamento inatteso.");
                if (response.Content.Headers.ContentLength is long declared && declared != ReShadeCatalog.SetupBytes)
                    throw new InvalidDataException("Dimensione dichiarata del pacchetto ReShade diversa dal catalogo.");
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024]; long total = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    {
                        total += count;
                        if (total > ReShadeCatalog.SetupBytes) throw new InvalidDataException("Download ReShade oltre la dimensione approvata.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        progress?.Report(new("Download ufficiale ReShade", (int)total, (int)ReShadeCatalog.SetupBytes));
                    }
                    output.Flush(true);
                }
                if (!Matches(temporary, ReShadeCatalog.SetupSha256, ReShadeCatalog.SetupBytes, token))
                    throw new InvalidDataException("SHA-256 ReShade diverso dal catalogo. Nessun file installato.");
                GuardPath(setup); File.Move(temporary, setup, false);
            }
            finally { GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
        }
        token.ThrowIfCancellationRequested();
        var bytes = ExtractX86(File.ReadAllBytes(setup));
        if (bytes.LongLength != ReShadeCatalog.DllBytes || Digest(bytes) != ReShadeCatalog.DllSha256)
            throw new InvalidDataException("DLL ReShade estratta diversa dal catalogo.");
        var stage = dll + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            var details = PeInspector.Read(stage);
            var version = FileVersionInfo.GetVersionInfo(stage);
            if (!details.IsDll || details.Architecture != "x86 (32 bit)" || version.FileMajorPart != 6 || version.FileMinorPart != 8 || version.FileBuildPart != 0)
                throw new InvalidDataException("Architettura o versione del componente ReShade non valida.");
            token.ThrowIfCancellationRequested(); GuardPath(dll); File.Move(stage, dll, false);
        }
        finally { GuardPath(stage); if (File.Exists(stage)) File.Delete(stage); }
        return new(dll, ReShadeCatalog.DllSha256);
    }
    internal static byte[] ExtractX86(byte[] setup)
    {
        if (setup.Length > 32 * 1024 * 1024) throw new InvalidDataException("Setup ReShade troppo grande.");
        var offset = -1;
        for (var i = 0; i + 30 < setup.Length; i += 512)
            if (setup[i] == 0x50 && setup[i + 1] == 0x4b && setup[i + 2] == 3 && setup[i + 3] == 4) { offset = i; break; }
        if (offset < 0) throw new InvalidDataException("Archivio incorporato ReShade non trovato.");
        using var stream = new MemoryStream(setup, offset, setup.Length - offset, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(e => e.FullName == "ReShade32.dll").ToArray();
        if (entries.Length != 1 || entries[0].Length is < 1 or > 32 * 1024 * 1024) throw new InvalidDataException("Archivio ReShade non valido.");
        using var input = entries[0].Open(); using var output = new MemoryStream();
        var buffer = new byte[64 * 1024]; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (output.Length + count > entries[0].Length) throw new InvalidDataException("DLL ReShade oltre il limite dichiarato.");
            output.Write(buffer, 0, count);
        }
        if (output.Length != entries[0].Length) throw new InvalidDataException("DLL ReShade incompleta.");
        return output.ToArray();
    }
    private static bool Matches(string path, string expectedHash, long bytes, CancellationToken token)
    {
        GuardPath(path); if (AttributesOrMissing(path) is null) return false;
        return new FileInfo(path).Length == bytes && HashFile(path, token) == expectedHash;
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
