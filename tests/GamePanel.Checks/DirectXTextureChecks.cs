using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GamePanel.Core;

internal static class DirectXTextureChecks
{
    public static void Register(Action<string, Action> check, string root)
    {
        check("DirectX: UG2 con dimensione ed entrypoint diversi da TexWizard passa solo il preflight API", () =>
        {
            using var f = new Fixture(root); var exe = TextureRuntimeChecks.CreateCompatibleExe(Path.Combine(f.Folder, "speed2.exe"));
            var data = File.ReadAllBytes(exe); Array.Resize(ref data, data.Length + 1234);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x98 + 16), 0x1000); File.WriteAllBytes(exe, data);
            var report = new GameInspector().Inspect(exe);
            Require(TextureRuntimeCatalog.CheckGame(report) is not null, "Fixture avrebbe dovuto essere incompatibile con TexWizard.");
            Require(DirectXTextureCatalog.CheckGame(report) is null, "Preflight DirectX ha ereditato il vincolo sul layout del motore.");
            Require(DirectXTextureCatalog.CheckGame(report with { Sha256 = new string('a', 64) }) is not null, "EXE cambiato non rilevato.");
            foreach (var bad in new[] { report with { Architecture = "x64 (64 bit)" }, report with { GraphicsApis = [] }, report with { HasAntiCheatIndicators = true }, report with { ScanWasLimited = true } })
                Require(DirectXTextureCatalog.CheckGame(bad) is not null, "Report non compatibile accettato.");
            var dll = File.ReadAllBytes(exe); BinaryPrimitives.WriteUInt16LittleEndian(dll.AsSpan(0x96), 0x2102); File.WriteAllBytes(exe, dll);
            Require(DirectXTextureCatalog.CheckGame(report with { Sha256 = Hash(dll) }) is not null, "DLL mascherata da EXE accettata.");
        });
        check("DirectX: cache raw ASI verificata, riusata senza rete e non riparata silenziosamente", () =>
        {
            using var f = new Fixture(root); var payload = Encoding.UTF8.GetBytes("Synthetic component data; never execute"); var requests = 0;
            var source = Source(f, payload, _ => { requests++; return Response(payload); });
            var path = source.PrepareAsync().GetAwaiter().GetResult();
            Require(File.ReadAllBytes(path).SequenceEqual(payload), "Payload diverso.");
            Require(source.PrepareAsync().GetAwaiter().GetResult() == path && requests == 1, "Cache valida riscaricata.");
            File.WriteAllText(path, "tampered"); Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
            Require(File.ReadAllText(path) == "tampered" && requests == 1, "File alterato riparato senza richiesta.");
        });
        check("DirectX: download corto, lungo, hash errato e redirect ostile non lasciano un componente", () =>
        {
            foreach (var mode in new[] { "short", "long", "hash", "host", "loop" })
            {
                using var f = new Fixture(root); var payload = new byte[64]; var requests = 0;
                var source = Source(f, payload, _ =>
                {
                    requests++;
                    if (mode is "host" or "loop") { var res = new HttpResponseMessage(HttpStatusCode.Found); res.Headers.Location = new(mode == "host" ? "https://example.com/foreign.asi" : DirectXTextureCatalog.LoaderUrl); return res; }
                    return Response(mode == "short" ? payload[..^1] : mode == "long" ? payload.Append((byte)1).ToArray() : Enumerable.Repeat((byte)1, payload.Length).ToArray());
                });
                Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
                Require(!Directory.EnumerateFiles(f.Cache).Any(p => p.Contains("pending") || p.EndsWith(".asi")), "File non verificato rimasto in cache.");
                Require(requests <= 4, "Redirect senza limite.");
            }
        });
        check("DirectX: annullamento durante download non pubblica il payload", () =>
        {
            using var f = new Fixture(root); var payload = new byte[80000]; using var cancel = new CancellationTokenSource();
            var source = Source(f, payload, _ => Response(payload));
            try { source.PrepareAsync(cancel.Token, new ProgressNow(_ => cancel.Cancel())).GetAwaiter().GetResult(); throw new Exception("Annullamento ignorato."); }
            catch (OperationCanceledException) { }
            Require(!Directory.EnumerateFiles(f.Cache).Any(p => p.Contains("pending") || p.EndsWith(".asi")), "Payload parziale pubblicato.");
        });
        check("DirectX: solo release HTTPS esatta e CDN GitHub, senza credenziali, frammenti o porte estranee", () =>
        {
            DirectXTextureSource.ValidateUri(new(DirectXTextureCatalog.LoaderUrl));
            DirectXTextureSource.ValidateUri(new("https://release-assets.githubusercontent.com/asset?token=fixture"));
            foreach (var url in new[] { "http://github.com/a", "https://github.com/foreign.asi", DirectXTextureCatalog.LoaderUrl + "?other=1", DirectXTextureCatalog.LoaderUrl + "#fragment", "https://release-assets.githubusercontent.com:444/a", "https://user:pass@release-assets.githubusercontent.com/a", "https://example.com/a" })
                Refused(() => DirectXTextureSource.ValidateUri(new(url)));
        });
        check("DDS: hash pixel identico agli otto vettori compilati dalla sorgente upstream fissata", () =>
        {
            // Values produced by compiling upstream TextureHash.cpp unchanged at SourceCommit.
            var vectors = new (int Bytes, string Hash)[] { (0, "9353DFC8A195F3E2"), (1, "7018CF35E8C64914"), (7, "2A53D3B97A6C87AB"), (8, "CE50891000500E84"), (9, "EE22B6682325EC49"), (16, "9521DAE23785242E"), (127, "629E4CCFAC559502"), (512, "9FA82F5E0B8CED0F") };
            foreach (var v in vectors)
            {
                var pixels = Enumerable.Range(0, v.Bytes).Select(i => unchecked((byte)(i * 13 + 7))).ToArray();
                Require(DirectXTextureData.ComputeTopMipHash(pixels) == v.Hash, "Algoritmo texture diverso dall'upstream: " + v.Bytes);
            }
        });
        check("DDS: legacy BC1/2/3, RGBA/BGRA8 e header DX10 delle catture sono verificati con tutte le mip", () =>
        {
            foreach (var format in new[] { 0, 1, 3, 5 }) foreach (var dx10 in new[] { false, true })
            {
                var bytes = MakeDds(format, dx10); var info = DirectXTextureData.InspectDds(bytes);
                Require(info.Width == 8 && info.Height == 8 && info.MipLevels == 4 && info.FullMipChain && info.Bytes == bytes.Length, "DDS valido letto male.");
                Require(info.Sha256 == Hash(bytes), "Hash del file DDS non verificato.");
                var changed = (byte[])bytes.Clone(); changed[^1] ^= 1;
                Require(DirectXTextureData.InspectDds(changed).TopMipHash == info.TopMipHash, "L'identità texture dipende dalle mip secondarie.");
                changed[dx10 ? 148 : 128] ^= 1;
                Require(DirectXTextureData.InspectDds(changed).TopMipHash != info.TopMipHash, "Cambio dei pixel originali non rilevato.");
            }
        });
        check("DDS: dimensioni, cubemap/array/volume, formati, mip e payload malformati bloccati", () =>
        {
            var original = MakeDds(1, true);
            foreach (var mutation in new (int At, uint Value)[] { (0, 0), (4, 125), (12, 0), (16, 8193), (24, 2), (28, 30), (76, 31), (112, 0x200), (128, 98), (132, 4), (136, 4), (140, 2) })
            {
                var bytes = (byte[])original.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(mutation.At), mutation.Value);
                Refused(() => DirectXTextureData.InspectDds(bytes));
            }
            Refused(() => DirectXTextureData.InspectDds(original[..^1])); Refused(() => DirectXTextureData.InspectDds(original.Append((byte)0).ToArray()));
        });
        check("DDS: catture devono legare nome hash ai pixel; file invalidi segnalati senza bloccare quelli validi", () =>
        {
            using var f = new Fixture(root); var dump = Path.Combine(f.Folder, "resources", "dump"); Directory.CreateDirectory(dump);
            var bytes = MakeDds(1, true); var info = DirectXTextureData.InspectDds(bytes);
            File.WriteAllBytes(Path.Combine(dump, info.TopMipHash + ".dds"), bytes);
            File.WriteAllBytes(Path.Combine(dump, "0000000000000000.dds"), bytes);
            File.WriteAllBytes(Path.Combine(dump, "bad.dds"), bytes);
            var scan = DirectXTextureData.InspectCaptures(Path.GetDirectoryName(dump)!);
            Require(scan.Captures.Count == 1 && scan.Captures[0].Hash == info.TopMipHash && scan.Warnings.Count == 2 && !scan.WasLimited, "Inventario catture non validato.");
            var replacement = Path.Combine(f.Folder, "replacement.dds"); var other = MakeDds(5, false); File.WriteAllBytes(replacement, other);
            Require(DirectXTextureData.ValidateReplacement(info.TopMipHash, replacement).Sha256 == Hash(other), "Sostituzione valida rifiutata perché i suoi pixel sono diversi dall'originale.");
            foreach (var invalid in new[] { "", "../escape", "1234", new string('G', 16), "0000000000000000.dds" }) Refused(() => DirectXTextureData.ValidateReplacement(invalid, replacement));
        });
        check("DDS: limite inventario e collegamenti simbolici non seguiti", () =>
        {
            using var f = new Fixture(root); var resources = Path.Combine(f.Folder, "resources"); var dump = Path.Combine(resources, "dump"); Directory.CreateDirectory(dump);
            for (var i = 0; i <= DirectXTextureData.MaximumCaptureFiles; i++) File.WriteAllBytes(Path.Combine(dump, i.ToString("X16") + ".dds"), new byte[128]);
            Require(DirectXTextureData.InspectCaptures(resources).WasLimited, "Inventario catture senza limite.");
            if (OperatingSystem.IsWindows()) return;
            var real = Path.Combine(f.Folder, "real.dds"); File.WriteAllBytes(real, MakeDds(1, true));
            var linked = Path.Combine(f.Folder, "linked.dds"); File.CreateSymbolicLink(linked, real); Refused(() => DirectXTextureData.ReadDds(linked));
            var cacheTarget = Path.Combine(f.Folder, "cache-target"); Directory.CreateDirectory(cacheTarget); Directory.CreateSymbolicLink(f.Cache, cacheTarget);
            var source = Source(f, new byte[] { 1, 2, 3 }, _ => throw new Exception("Non deve richiedere rete.")); Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
        });
    }

    internal static byte[] MakeDds(int format = 1, bool dx10 = true)
    {
        var block = format == 0 ? 0 : format == 1 ? 8 : 16; var payload = new List<byte>();
        for (var width = 8; width >= 1; width /= 2)
        {
            var size = block == 0 ? width * width * 4 : Math.Max(1, (width + 3) / 4) * Math.Max(1, (width + 3) / 4) * block;
            payload.AddRange(Enumerable.Range(0, size).Select(i => unchecked((byte)(i * 13 + 7))));
        }
        var data = new byte[(dx10 ? 148 : 128) + payload.Count];
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
        U32(0, 0x20534444); U32(4, 124); U32(8, 0x21007); U32(12, 8); U32(16, 8); U32(28, 4); U32(76, 32); U32(108, 0x401008);
        if (dx10) { U32(80, 4); U32(84, 0x30315844); U32(128, format == 0 ? 87u : format == 1 ? 71u : format == 3 ? 74u : 77u); U32(132, 3); U32(140, 1); }
        else if (format != 0) { U32(80, 4); U32(84, format == 1 ? 0x31545844u : format == 3 ? 0x33545844u : 0x35545844u); }
        else { U32(80, 0x41); U32(88, 32); U32(92, 0x00ff0000); U32(96, 0x0000ff00); U32(100, 0x000000ff); U32(104, 0xff000000); }
        payload.ToArray().CopyTo(data, dx10 ? 148 : 128); return data;
    }
    private static DirectXTextureSource Source(Fixture f, byte[] data, Func<HttpRequestMessage, HttpResponseMessage> response)
        => new(f.Cache, () => new HttpClient(new Handler(response)), new(data.Length, Hash(data)));
    private static HttpResponseMessage Response(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static void Require(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static void Refused(Action action)
    { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return; } throw new Exception("Input non valido accettato."); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
    private sealed class ProgressNow(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress p) => action(p); }
    private sealed class Fixture : IDisposable
    {
        internal string Folder { get; }
        internal string Cache => Path.Combine(Folder, "cache");
        internal Fixture(string root) { Folder = Path.Combine(root, "directx-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Folder); }
        public void Dispose() => Directory.Delete(Folder, true);
    }
}
