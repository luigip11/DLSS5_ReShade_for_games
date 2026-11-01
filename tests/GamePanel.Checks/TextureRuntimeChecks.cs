using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GamePanel.Core;

internal static class TextureRuntimeChecks
{
    public static void Register(Action<string, Action> check, string root, bool official = false, string? localPackage = null, string? builtinPackage = null)
    {
        check("TexWizard preflight: tutti gli 82 CALL verificati staticamente su PE32 compatibile", () =>
        {
            using var f = new Fixture(root); Require(TextureRuntimeCatalog.TextureCallsites.Count == 81, "Catalogo callsite incompleto.");
            Require(TextureRuntimeCatalog.CheckGame(f.Report()) is null, "Fixture PE compatibile rifiutata.");
        });
        check("TexWizard preflight: ciascun opcode e target relativo alterato blocca", () =>
        {
            using var f = new Fixture(root); var original = File.ReadAllBytes(f.Exe);
            foreach (var va in TextureRuntimeCatalog.TextureCallsites.Append(0x57ed99u))
                foreach (var change in new[] { 0, 1, 4 })
                {
                    var bytes = (byte[])original.Clone(); bytes[Offset(va) + change] ^= 1; File.WriteAllBytes(f.Exe, bytes);
                    Require(TextureRuntimeCatalog.CheckGame(f.Report())?.Contains("Codice hook diverso") == true, "CALL alterato accettato.");
                }
        });
        check("TexWizard preflight: dimensione, EP, base, ASLR e RVA corrotti bloccano", () =>
        {
            using var f = new Fixture(root); var original = File.ReadAllBytes(f.Exe); var report = f.Report();
            foreach (var at in new[] { 0x98 + 16, 0x98 + 28, 0x98 + 70, 0x178 + 12, 0x178 + 20, 0x178 + 39, 0x3c, 0x98 + 108 })
            {
                var bytes = (byte[])original.Clone();
                if (at == 0x98 + 108) bytes[at] = 0;
                else bytes[at] ^= at == 0x98 + 70 ? (byte)0x40 : at == 0x178 + 39 ? (byte)0x20 : (byte)0xff;
                File.WriteAllBytes(f.Exe, bytes);
                Require(TextureRuntimeCatalog.CheckGame(report with { Sha256 = Hash(bytes) }) is not null, "PE alterato accettato: " + at);
            }
            File.WriteAllBytes(f.Exe, original[..^1]);
            Require(TextureRuntimeCatalog.CheckGame(report with { Sha256 = Hash(original[..^1]) }) is not null, "Dimensione EXE diversa accettata.");
        });
        check("TexWizard preflight: report vecchio, gioco/API/architettura errati e scansione incompleta bloccano", () =>
        {
            using var f = new Fixture(root); var report = f.Report();
            foreach (var bad in new[] { report with { Architecture = "x64 (64 bit)" }, report with { HasAntiCheatIndicators = true }, report with { ScanWasLimited = true }, report with { GraphicsApis = [] }, report with { ExecutablePath = Path.Combine(f.Folder, "other.exe") } })
                Require(TextureRuntimeCatalog.CheckGame(bad) is not null, "Report non compatibile accettato.");
            using (var output = new FileStream(f.Exe, FileMode.Open, FileAccess.Write)) { output.Position = output.Length - 1; output.WriteByte(1); }
            Require(TextureRuntimeCatalog.CheckGame(report)?.Contains("cambiato") == true, "EXE cambiato non rilevato.");
        });
        check("TexWizard preflight: collegamenti simbolici nel percorso EXE bloccano", () =>
        {
            if (OperatingSystem.IsWindows()) return; using var f = new Fixture(root);
            var target = Path.Combine(f.Folder, "actual.exe"); File.Move(f.Exe, target); File.CreateSymbolicLink(f.Exe, target);
            var report = new GameReport(1, DateTimeOffset.UtcNow, f.Exe, Hash(File.ReadAllBytes(target)), "x86 (32 bit)", ["Direct3D 9"], [], [], false, false);
            Require(TextureRuntimeCatalog.CheckGame(report) is not null, "Symlink EXE accettato.");
        });
        check("TexWizard download: redirect solo HTTPS verso release approvata, credenziali e host estranei bloccati", () =>
        {
            TextureRuntimeSource.ValidateUri(new(TextureRuntimeCatalog.ZipUrl));
            TextureRuntimeSource.ValidateUri(new("https://release-assets.githubusercontent.com/asset?token=fixture"));
            foreach (var uri in new[] { "http://github.com/R-033/texwizard/releases/download/1.3/TexWizard_UG2.zip", "https://github.com/R-033/texwizard/releases/download/1.4/TexWizard_UG2.zip", "https://github.com/foreign/project/releases/download/1.3/file.zip", "https://release-assets.githubusercontent.com:444/a", "https://user:pass@release-assets.githubusercontent.com/a", "https://example.com/a", TextureRuntimeCatalog.ZipUrl + "?other=1" })
                Refused(() => TextureRuntimeSource.ValidateUri(new(uri)));
        });
        check("TexWizard download: cache atomica verificata a ogni riuso, ASI e ZIP alterati rifiutati senza riparazione silenziosa", () =>
        {
            using var f = new Fixture(root); var loader = Encoding.UTF8.GetBytes("Synthetic ASI data, never loaded"); var zip = MakeZip(loader); var requests = 0;
            var source = Source(f, zip, loader, _ => { requests++; return Response(zip); });
            var path = source.PrepareAsync().GetAwaiter().GetResult(); Require(File.ReadAllBytes(path).SequenceEqual(loader), "Payload differente.");
            Require(source.PrepareAsync().GetAwaiter().GetResult() == path && requests == 1, "Cache valida riscaricata.");
            File.WriteAllText(path, "tampered"); Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
            Require(File.ReadAllText(path) == "tampered" && requests == 1, "Cache alterata riparata senza consenso.");
            File.WriteAllBytes(path, loader); File.WriteAllText(Path.Combine(f.Cache, "TexWizard_UG2-1.3.zip"), "tampered ZIP");
            Refused(() => source.PrepareAsync().GetAwaiter().GetResult()); Require(requests == 1, "Archivio alterato riscaricato silenziosamente.");
        });
        check("TexWizard download: hash/size mancanti o errati, redirect loop e host estraneo non lasciano payload", () =>
        {
            foreach (var mode in new[] { "hash", "short", "oversize", "redirect", "foreign" })
            {
                using var f = new Fixture(root); var loader = new byte[] { 1, 2, 3 }; var zip = MakeZip(loader); var requests = 0;
                var source = Source(f, zip, loader, _ =>
                {
                    requests++;
                    if (mode is "redirect" or "foreign") { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.Location = new(mode == "foreign" ? "https://example.com/file" : TextureRuntimeCatalog.ZipUrl); return r; }
                    var data = (byte[])zip.Clone();
                    if (mode == "hash") data[^1] ^= 1;
                    if (mode == "short") data = data[..^1];
                    if (mode == "oversize") data = data.Concat(new byte[] { 0 }).ToArray();
                    return Response(data);
                });
                Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
                Require(!Directory.EnumerateFiles(f.Cache).Any(p => p.EndsWith(".zip") || p.Contains("pending") || p.EndsWith(".asi")), "Payload non verificato rimasto in cache.");
                Require(requests <= 4, "Redirect non limitati.");
            }
        });
        check("TexWizard download: annullamento rimuove staging e non pubblica cache", () =>
        {
            using var f = new Fixture(root); var loader = new byte[80000]; var zip = MakeZip(loader); using var cancel = new CancellationTokenSource();
            var source = Source(f, zip, loader, _ => Response(zip));
            try { source.PrepareAsync(cancel.Token, new InlineProgress(_ => cancel.Cancel())).GetAwaiter().GetResult(); throw new Exception("Annullamento ignorato."); }
            catch (OperationCanceledException) { }
            Require(!Directory.EnumerateFiles(f.Cache).Any(p => p.Contains("pending") || p.EndsWith(".zip") || p.EndsWith(".asi")), "File incompleti pubblicati.");
        });
        check("TexWizard ZIP: path traversal, duplicati, case ambiguo, symlink e hash ASI bloccati", () =>
        {
            var loader = new byte[] { 1, 2, 3 };
            foreach (var extra in new[] { "../evil", "scripts/TEXWIZARD.asi", TextureRuntimeCatalog.Loader, "symlink" })
            {
                var archive = MakeZip(loader, extra, extra == "symlink");
                Refused(() => TextureRuntimeSource.ExtractLoader(archive, loader.Length, Hash(loader)));
            }
            Refused(() => TextureRuntimeSource.ExtractLoader(MakeZip(loader), loader.Length, new string('0', 64)));
        });
        check("TexWizard cache: reparse ZIP/ASI/cartella non seguiti", () =>
        {
            if (OperatingSystem.IsWindows()) return;
            foreach (var which in new[] { "zip", "asi", "folder" })
            {
                using var f = new Fixture(root); var loader = new byte[] { 1, 2, 3 }; var zip = MakeZip(loader);
                var external = Path.Combine(f.Folder, "external"); Directory.CreateDirectory(external);
                var source = Source(f, zip, loader, _ => Response(zip));
                if (which == "folder") Directory.CreateSymbolicLink(f.Cache, external);
                else { Directory.CreateDirectory(f.Cache); var target = Path.Combine(external, "payload"); File.WriteAllBytes(target, which == "zip" ? zip : loader); File.CreateSymbolicLink(Path.Combine(f.Cache, which == "zip" ? "TexWizard_UG2-1.3.zip" : "TexWizard.asi"), target); }
                Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
            }
        });
        if (builtinPackage is not null) check("Asphalt HD integrato: ZIP fissato, manifest stretto e cache verificata a ogni riuso", () =>
        {
            using var f = new Fixture(root); var cache = Path.Combine(f.Folder, "Components", "AsphaltHD", "alpha8"); Directory.CreateDirectory(cache);
            File.Copy(builtinPackage, Path.Combine(cache, "GamePanel-AsphaltHD.zip"));
            var source = new TextureRuntimeSource(f.Cache, () => throw new Exception("Cache valida ha richiesto rete."),
                new(TextureRuntimeCatalog.ZipUrl, TextureRuntimeCatalog.ZipBytes, TextureRuntimeCatalog.ZipSha256), TextureRuntimeCatalog.LoaderBytes, TextureRuntimeCatalog.LoaderSha256, f.Folder);
            var pack = source.PrepareBuiltinPackAsync().GetAwaiter().GetResult();
            Require(pack.ArchiveSha256 == BuiltinAsphaltCatalog.Sha256 && pack.Manifest.Kind == "Textures", "Pack integrato diverso dal catalogo.");
            Require(source.PrepareBuiltinPackAsync().GetAwaiter().GetResult().Folder == pack.Folder, "Cache pack non riusata.");
            var payload = pack.Manifest.Files.First(x => x.Path.EndsWith("textures.bin", StringComparison.Ordinal));
            var path = Path.Combine(pack.Folder, "files", payload.Path.Replace('/', Path.DirectorySeparatorChar));
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)) { file.Position = file.Length - 1; var value = file.ReadByte(); file.Position--; file.WriteByte((byte)(value ^ 1)); }
            Refused(() => source.PrepareBuiltinPackAsync().GetAwaiter().GetResult());
        });
        if (official || localPackage is not null) check("TexWizard ufficiale 1.3: ZIP e ASI corrispondono ai pin senza esecuzione", () =>
        {
            using var f = new Fixture(root);
            if (localPackage is not null) { Directory.CreateDirectory(f.Cache); File.Copy(localPackage, Path.Combine(f.Cache, "TexWizard_UG2-1.3.zip")); }
            var path = new TextureRuntimeSource(f.Cache).PrepareAsync().GetAwaiter().GetResult();
            Require(new FileInfo(path).Length == TextureRuntimeCatalog.LoaderBytes && Hash(File.ReadAllBytes(path)) == TextureRuntimeCatalog.LoaderSha256, "ASI upstream differente.");
        });
    }
    internal static string CreateCompatibleExe(string path)
    {
        var bytes = new byte[TextureRuntimeCatalog.ExpectedExeBytes];
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        U16(0, 0x5a4d); U32(0x3c, 0x80); U32(0x80, 0x4550); U16(0x84, 0x14c); U16(0x86, 1); U16(0x94, 224); U16(0x96, 0x102);
        const int opt = 0x98; U16(opt, 0x10b); U32(opt + 16, 0x35bcc7); U32(opt + 20, 0x1000); U32(opt + 28, 0x400000);
        U32(opt + 32, 0x1000); U32(opt + 36, 0x200); U32(opt + 56, 0x500000); U32(opt + 60, 0x200); U16(opt + 68, 3);
        U32(opt + 92, 16); U32(opt + 104, 0x1000); U32(opt + 108, 40);
        const int section = 0x178; Encoding.ASCII.GetBytes(".text").CopyTo(bytes, section); U32(section + 8, (uint)bytes.Length - 0x200); U32(section + 12, 0x1000);
        U32(section + 16, (uint)bytes.Length - 0x200); U32(section + 20, 0x200); U32(section + 36, 0x60000020);
        U32(0x200 + 12, 0x1040); U32(0x200 + 16, 0x1060); Encoding.ASCII.GetBytes("d3d9.dll\0").CopyTo(bytes, 0x240);
        void Call(uint va, uint target) { var at = Offset(va); bytes[at] = 0xe8; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 1), checked((int)((long)target - va - 5))); }
        foreach (var va in TextureRuntimeCatalog.TextureCallsites) Call(va, 0x4901d0); Call(0x57ed99, 0x57fb40);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); return path;
    }
    private static int Offset(uint va) => checked((int)(va - 0x400000 - 0x1000 + 0x200));
    private static TextureRuntimeSource Source(Fixture f, byte[] zip, byte[] loader, Func<HttpRequestMessage, HttpResponseMessage> response) => new(f.Cache, () => new HttpClient(new Handler(response)), new(TextureRuntimeCatalog.ZipUrl, zip.Length, Hash(zip)), loader.Length, Hash(loader));
    private static byte[] MakeZip(byte[] loader, string? extra = null, bool symlink = false)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var stream = zip.CreateEntry(TextureRuntimeCatalog.Loader, CompressionLevel.NoCompression).Open()) stream.Write(loader);
            if (extra is not null) { var entry = zip.CreateEntry(extra); if (symlink) entry.ExternalAttributes = unchecked((int)0xa1ff0000); using var stream = entry.Open(); stream.WriteByte(1); }
        }
        return output.ToArray();
    }
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refused(Action action)
    { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or InvalidOperationException) { return; } throw new Exception("Operazione non valida accettata."); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress p) => action(p); }
    private sealed class Fixture : IDisposable
    {
        internal string Folder { get; }
        internal string Exe => Path.Combine(Folder, "Game", "speed2.exe");
        internal string Cache => Path.Combine(Folder, "cache");
        internal Fixture(string root) { Folder = Path.Combine(root, "texture-runtime-" + Guid.NewGuid().ToString("N")); CreateCompatibleExe(Exe); }
        internal GameReport Report() => new GameInspector().Inspect(Exe);
        public void Dispose() => Directory.Delete(Folder, true);
    }
}
