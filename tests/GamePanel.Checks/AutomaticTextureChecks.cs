using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class AutomaticTextureChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> fixture)
    {
        check("Automatic DDS: pixel rosso golden, 2x e mip completi compressi", () =>
        {
            var output = DdsAutomaticEnhancer.Enhance(Dds(4, 4, 255, 0, 0, 255));
            var path = Path.Combine(root, "automatic-golden.dds"); File.WriteAllBytes(path, output);
            var info = DirectXTextureData.ReadDds(path); var pixels = DdsAutomaticEnhancer.Decode(output, info);
            Require(info.Width == 8 && info.Height == 8 && info.FullMipChain && info.MipLevels == 4 && info.Format == "DXT1 / BC1", "Dimensioni, mip o compressione errati.");
            Require(output.Length == 184, "Uscita BC1 non compatta.");
            for (var i = 0; i < pixels.Length; i += 4) Require(pixels[i] == 255 && pixels[i + 1] == 0 && pixels[i + 2] == 0 && pixels[i + 3] == 255, "Pixel golden alterato.");
            Require(output.SequenceEqual(DdsAutomaticEnhancer.Enhance(Dds(4, 4, 255, 0, 0, 255))), "Uscita non deterministica.");
        });
        check("Automatic DDS: interpolazione golden fra pixel neri e bianchi", () =>
        {
            var source = Dds(4, 4, 0, 0, 0, 255);
            for (var y = 0; y < 4; y++) for (var x = 2; x < 4; x++) for (var c = 0; c < 3; c++) source[128 + (y * 4 + x) * 4 + c] = 255;
            var output = DdsAutomaticEnhancer.Enhance(source); var path = Path.Combine(root, "automatic-interpolation.dds"); File.WriteAllBytes(path, output);
            var pixels = DdsAutomaticEnhancer.Decode(output, DirectXTextureData.ReadDds(path));
            Require(pixels.AsSpan(3 * 4, 4).SequenceEqual(new byte[] { 66, 65, 66, 255 }), "Primo pixel interpolato golden errato.");
            Require(pixels.AsSpan(4 * 4, 4).SequenceEqual(new byte[] { 189, 190, 189, 255 }), "Secondo pixel interpolato golden errato.");
        });
        check("Automatic DDS: alpha preservato in BC3, niente RGBA espanso", () =>
        {
            var output = DdsAutomaticEnhancer.Enhance(Dds(2, 2, 255, 0, 0, 128));
            var path = Path.Combine(root, "automatic-alpha.dds"); File.WriteAllBytes(path, output);
            var info = DirectXTextureData.ReadDds(path); var pixels = DdsAutomaticEnhancer.Decode(output, info);
            Require(info.Format == "DXT5 / BC3" && info.FullMipChain, "Alpha richiede BC3 e mip completi.");
            for (var i = 3; i < pixels.Length; i += 4) Require(pixels[i] == 128, "Alpha uniforme non preservato.");
            Throws(() => DdsAutomaticEnhancer.Enhance(Dds(1024, 1, 10, 20, 30, 255)));
        });
        check("Automatic batch: oltre 256 catture, cache esterna, installazione e ripresa identica", () =>
        {
            using var f = new Fixture(root, fixture); f.Install();
            for (var i = 0; i < 257; i++) f.Capture(Dds(2, 2, (byte)i, (byte)(i >> 8), 40, 255));
            var prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot);
            Require(prepared.FilesCount == 257 && !prepared.WasLimited, "Batch troncato a 256.");
            Require(!Directory.EnumerateFiles(Path.Combine(f.ResourceRoot, "inject")).Any(), "Preparazione ha scritto nel gioco/inject.");
            var result = f.Batch.Install(f.Report(), f.ResourceRoot, prepared);
            Require(result.InstalledCount == 257 && !result.Cancelled, "Batch incompleto.");
            result = f.Batch.Install(f.Report(), f.ResourceRoot, prepared);
            Require(result.InstalledCount == 0 && result.AlreadyInstalledCount == 257, "Ripresa non idempotente.");
        });
        check("Automatic batch: limite 2048 esplicito, nessun taglio silenzioso", () =>
        {
            var directory = Path.Combine(root, "automatic-limit"); var dump = Path.Combine(directory, "dump"); Directory.CreateDirectory(dump);
            for (var i = 0; i < 2049; i++)
            {
                var path = Path.Combine(directory, "capture.dds"); File.WriteAllBytes(path, Dds(1, 1, (byte)i, (byte)(i >> 8), 10, 255));
                var hash = DirectXTextureData.ReadDds(path).TopMipHash; File.Move(path, Path.Combine(dump, hash + ".dds"), true);
            }
            var scan = DirectXTextureBatchService.Scan(directory, CancellationToken.None);
            Require(scan.Limited && scan.Captures.Count == 2048 && scan.Warnings.Count > 0, "Limite non dichiarato.");
        });
        check("Automatic batch: dump vuoto, DDS non supportato e catture grandi dichiarati", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); var prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot);
            Require(prepared.FilesCount == 0 && prepared.Warnings.Count > 0, "Dump vuoto non dichiarato.");
            f.Capture(Dds(1024, 1, 10, 20, 30, 255)); File.WriteAllText(Path.Combine(f.ResourceRoot, "dump", "0123456789ABCDEF.dds"), "unsupported");
            prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot);
            Require(prepared.FilesCount == 0 && prepared.SkippedCount == 2 && prepared.Warnings.Count > 0, "Catture non elaborate non conteggiate.");
        });
        check("Automatic batch: cattura modificata blocca tutte le scritture", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); var path = f.Capture(Dds(4, 4, 255, 0, 0, 255));
            var prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot); var data = File.ReadAllBytes(path); data[32] ^= 1; File.WriteAllBytes(path, data);
            Throws(() => f.Batch.Install(f.Report(), f.ResourceRoot, prepared));
            Require(!Directory.EnumerateFiles(Path.Combine(f.ResourceRoot, "inject")).Any(), "Scritture prima della verifica sorgenti.");
        });
        check("Automatic batch: conflitto non gestito blocca prima della prima DDS", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); f.Capture(Dds(4, 4, 255, 0, 0, 255)); f.Capture(Dds(4, 4, 0, 255, 0, 255));
            var prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot); var conflict = Path.Combine(f.ResourceRoot, "inject", "0123456789ABCDEF.dds"); File.WriteAllText(conflict, "unmanaged");
            Throws(() => f.Batch.Install(f.Report(), f.ResourceRoot, prepared));
            Require(Directory.GetFiles(Path.Combine(f.ResourceRoot, "inject")).Length == 1 && File.ReadAllText(conflict) == "unmanaged", "Conflitto sovrascritto o batch parziale.");
        });
        check("Automatic batch: SHA uscita e identità EXE vincolati al manifest", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); f.Capture(Dds(4, 4, 10, 20, 30, 255)); var prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot);
            var output = Directory.GetFiles(prepared.Folder, "*.dds").Single(); var data = File.ReadAllBytes(output); data[^1] ^= 1; File.WriteAllBytes(output, data);
            Throws(() => f.Batch.Install(f.Report(), f.ResourceRoot, prepared));
            prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot); var manifestPath = Path.Combine(prepared.Folder, "manifest.json");
            var manifest = JsonSerializer.Deserialize<DirectXBatchManifest>(File.ReadAllText(manifestPath))!;
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { ExecutableSha256 = new string('0', 64) }));
            Throws(() => f.Batch.Install(f.Report(), f.ResourceRoot, prepared));
        });
        check("Automatic batch: manifest approvato e conteggi immutabili prima delle scritture", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); f.Capture(Dds(4, 4, 10, 20, 30, 255));
            var prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot); var manifestPath = Path.Combine(prepared.Folder, "manifest.json");
            var originalBytes = File.ReadAllBytes(manifestPath); var manifest = JsonSerializer.Deserialize<DirectXBatchManifest>(originalBytes)!;
            Require(prepared.ManifestSha256 == Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant(), "SHA manifest non corrisponde ai byte preparati.");
            foreach (var changed in new[] { manifest with { SkippedCount = manifest.SkippedCount + 1 }, manifest with { Files = manifest.Files.Concat(manifest.Files).ToArray(), OutputBytes = manifest.OutputBytes * 2 }, manifest with { ResourceRoot = Path.Combine(f.ResourceRoot, "other") } })
            {
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(changed));
                Throws(() => f.Batch.Install(f.Report(), f.ResourceRoot, prepared));
                Require(!Directory.EnumerateFiles(Path.Combine(f.ResourceRoot, "inject")).Any(), "Manifest cambiato ha installato DDS.");
            }
            File.WriteAllBytes(manifestPath, originalBytes);
            foreach (var changed in new[] { prepared with { FilesCount = prepared.FilesCount + 1 }, prepared with { OutputBytes = prepared.OutputBytes + 1 }, prepared with { WasLimited = !prepared.WasLimited }, prepared with { SkippedCount = prepared.SkippedCount + 1 } })
                Throws(() => f.Batch.Install(f.Report(), f.ResourceRoot, changed));
            Require(!Directory.EnumerateFiles(Path.Combine(f.ResourceRoot, "inject")).Any(), "Conteggi cambiati hanno installato DDS.");
        });
        check("Automatic batch: preparazione durante cattura, installazione solo a gioco chiuso e cancellazione riprendibile", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); f.Capture(Dds(4, 4, 255, 0, 0, 255)); f.Capture(Dds(4, 4, 0, 255, 0, 255)); f.Running = true;
            var prepared = f.Batch.Prepare(f.Report(), f.ResourceRoot); Throws(() => f.Batch.Install(f.Report(), f.ResourceRoot, prepared)); f.Running = false;
            using var cancel = new CancellationTokenSource();
            var result = f.Batch.Install(f.Report(), f.ResourceRoot, prepared, cancel.Token, new CallbackProgress(_ => cancel.Cancel()));
            Require(result.Cancelled && result.InstalledCount == 1, "Conteggio parziale cancellazione errato.");
            result = f.Batch.Install(f.Report(), f.ResourceRoot, prepared);
            Require(!result.Cancelled && result.InstalledCount == 1 && result.AlreadyInstalledCount == 1, "Ripresa batch errata.");
        });
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { return; } throw new InvalidOperationException("Rifiuto atteso."); }
    private sealed class CallbackProgress(Action<BackupProgress> callback) : IProgress<BackupProgress> { public void Report(BackupProgress value) => callback(value); }
    private static byte[] Dds(int width, int height, byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[128 + width * height * 4];
        void U(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at, 4), value);
        U(0, 0x20534444); U(4, 124); U(8, 0x100f); U(12, (uint)height); U(16, (uint)width); U(20, (uint)(width * 4)); U(76, 32); U(80, 0x41); U(88, 32);
        U(92, 0xff); U(96, 0xff00); U(100, 0xff0000); U(104, 0xff000000); U(108, 0x1000);
        for (var i = 128; i < bytes.Length; i += 4) { bytes[i] = r; bytes[i + 1] = g; bytes[i + 2] = b; bytes[i + 3] = a; } return bytes;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly Dictionary<string, (long Bytes, string Hash)> _wide = new(StringComparer.Ordinal);
        private readonly string _loader;
        public string Exe { get; }
        public bool Running { get; set; }
        public DirectXTextureProfileService Profiles { get; }
        public DirectXTextureBatchService Batch { get; }
        public string ResourceRoot => Profiles.Status(Exe).ResourceRoot;
        public Fixture(string root, Func<string, bool, bool, string, string> fixture)
        {
            _root = Path.Combine(root, "automatic-" + Guid.NewGuid().ToString("N")); var game = Path.Combine(_root, "game"); Directory.CreateDirectory(game);
            Exe = Path.Combine(game, "speed2.exe"); fixture(Exe, false, false, "d3d9.dll");
            _loader = Path.Combine(_root, "approved-runtime.bin"); File.WriteAllText(_loader, "approved runtime fixture, never loaded");
            foreach (var path in WidescreenCatalog.Targets)
            {
                var target = Path.Combine(game, path.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); var bytes = Encoding.UTF8.GetBytes("wide fixture: " + path);
                File.WriteAllBytes(target, bytes); _wide[path] = (bytes.Length, Hash(bytes));
            }
            Profiles = new(Path.Combine(_root, "profiles"), Path.Combine(_root, "data"), _ => Running, Hash(File.ReadAllBytes(_loader)), new FileInfo(_loader).Length, _wide);
            var folder = Profiles.ProfileFolder(Exe); Directory.CreateDirectory(folder);
            var state = new ProfileState(1, Guid.NewGuid().ToString("N"), Exe, Report().Sha256, WidescreenCatalog.Version, true, SdrSettings.Original,
                WidescreenCatalog.Targets.Select(p => new ProfileFile(p, _wide[p].Hash, _wide[p].Bytes)).ToArray(), []);
            File.WriteAllText(Path.Combine(folder, "widescreen-state.json"), JsonSerializer.Serialize(state));
            Batch = new(Profiles, Path.Combine(_root, "prepared"));
        }
        public GameReport Report() => new GameInspector().Inspect(Exe);
        public void Install() => Profiles.Execute(Profiles.PrepareInstall(Report(), _loader));
        public string Capture(byte[] bytes)
        {
            var path = Path.Combine(ResourceRoot, "capture.dds"); File.WriteAllBytes(path, bytes); var hash = DirectXTextureData.ReadDds(path).TopMipHash;
            var target = Path.Combine(ResourceRoot, "dump", hash + ".dds"); File.Move(path, target, true); return target;
        }
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
