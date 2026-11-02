using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GamePanel.Core;

internal static class SmolModelChecks
{
    public static void Register(Action<string, Action> check, string root, string? originalArchive = null)
    {
        check("Smol: tre geometrie, texture originali e readme conservati senza scrivere nel gioco", () =>
        {
            using var f = new Fixture(root); var archive = f.Archive(); var before = Snapshot(f.Game);
            var prepared = f.Builder(archive).Prepare(f.Report, archive);
            Require(prepared.Package.Manifest.Id == "smol-cars" && prepared.Package.Manifest.Kind == "Models", "Identità pack errata.");
            Require(prepared.Package.Manifest.Files.Count == 9 && prepared.Originals.Count == 6, "Inventario subset errato.");
            Require(prepared.Package.Manifest.Files.All(x => x.Path.StartsWith("GamePanelAssets/smol-cars/", StringComparison.Ordinal)
                || new[] { "GOLF", "IS300", "SENTRA" }.Any(c => x.Path == "CARS/" + c + "/GEOMETRY.BIN" || x.Path == "CARS/" + c + "/TEXTURES.BIN")), "Percorsi estranei al subset.");
            foreach (var car in new[] { "GOLF", "IS300", "SENTRA" })
            {
                var relative = "CARS/" + car + "/TEXTURES.BIN";
                Require(File.ReadAllBytes(Path.Combine(prepared.Package.Folder, "files", relative)).SequenceEqual(File.ReadAllBytes(Path.Combine(f.Game, relative))), "Texture alterata.");
            }
            var english = File.ReadAllBytes(Path.Combine(prepared.Package.Folder, "files/GamePanelAssets/smol-cars/README-English.html.txt"));
            Require(english.SequenceEqual(f.Members[3].Bytes), "Readme originale modificato.");
            Require(Snapshot(f.Game).SequenceEqual(before), "Preparazione ha scritto nel gioco.");
            SmolModelPackBuilder.VerifyForInstall(f.Report, prepared);
            Require(!Directory.EnumerateDirectories(f.Cache, ".pending-*").Any(), "Staging residuo.");
        });
        check("Smol: variazioni a EXE, geometria o texture dopo la preparazione bloccano l'installazione", () =>
        {
            foreach (var relative in new[] { "speed2.exe", "CARS/GOLF/GEOMETRY.BIN", "CARS/IS300/TEXTURES.BIN" })
            {
                using var f = new Fixture(root); var archive = f.Archive(); var prepared = f.Builder(archive).Prepare(f.Report, archive);
                var path = Path.Combine(f.Game, relative); var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
                Refused(() => SmolModelPackBuilder.VerifyForInstall(f.Report, prepared));
            }
        });
        check("Smol: traversal, duplicati e file speciali nel RAR sono rifiutati", () =>
        {
            foreach (var extra in new[] { new Item("../outside.bin", [1]), new Item("_setup/GOLF/GEOMETRY.BIN", [1]), new Item("special.bin", [1], Attributes: 0xa1ff), new Item("C:/outside.bin", [1]) })
            {
                using var f = new Fixture(root); var archive = f.Archive(extra); Refused(() => f.Builder(archive).Prepare(f.Report, archive));
                Require(!File.Exists(Path.Combine(f.Root, "outside.bin")), "Traversal ha scritto fuori staging.");
            }
        });
        check("Smol: CRC di ogni membro, anche script esclusi, viene verificato", () =>
        {
            using var f = new Fixture(root); var archive = f.Archive(new Item("_setup/setup.end", Encoding.UTF8.GetBytes("never execute"), CorruptCrc: true));
            Refused(() => f.Builder(archive).Prepare(f.Report, archive));
        });
        check("Smol: hash di geometrie/readme e hash archivio sono obbligatori", () =>
        {
            using var f = new Fixture(root); var archive = f.Archive(); var builder = f.Builder(archive);
            File.AppendAllText(archive, "changed"); Refused(() => builder.Prepare(f.Report, archive));
            archive = f.Archive(); var pin = f.Pin(archive); var wrong = pin.Members.ToArray(); wrong[0] = wrong[0] with { Sha256 = new string('0', 64) };
            Refused(() => new SmolModelPackBuilder(f.Cache, pin with { Members = wrong }).Prepare(f.Report, archive));
            Refused(() => new SmolModelPackBuilder(f.Cache).Prepare(f.Report, archive));
        });
        check("Smol: limiti RAR di voci e dimensione estratta", () =>
        {
            using var f = new Fixture(root); var archive = f.Archive(new Item("large.bin", [1], DeclaredSize: 64 * 1024 * 1024 + 1));
            Refused(() => f.Builder(archive).Prepare(f.Report, archive));
            archive = f.Archive(Enumerable.Range(0, 124).Select(n => new Item("extra/" + n + ".bin", [1])).ToArray());
            Refused(() => f.Builder(archive).Prepare(f.Report, archive));
        });
        check("Smol: cancellazione non scrive nel gioco", () =>
        {
            using var f = new Fixture(root); var archive = f.Archive(); var before = Snapshot(f.Game);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { f.Builder(archive).Prepare(f.Report, archive, cancel.Token); throw new Exception("Cancellazione ignorata."); }
            catch (OperationCanceledException) { }
            Require(Snapshot(f.Game).SequenceEqual(before), "Gioco modificato dopo annullamento.");
        });
        check("Smol: download fissato, cache verificata e fallback locale su HTTP 403", () =>
        {
            using var f = new Fixture(root); byte[] bytes = [1, 2, 3, 4]; var pin = new SmolModelSource.Pin(bytes.Length, Hash(bytes));
            var source = new SmolModelSource(f.Cache, () => new HttpClient(new Handler(HttpStatusCode.OK, bytes)), pin);
            var path = source.PrepareAsync().GetAwaiter().GetResult(); Require(File.ReadAllBytes(path).SequenceEqual(bytes), "Download diverso.");
            File.WriteAllBytes(path, [4, 3, 2, 1]); Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
            File.Delete(path);
            source = new(f.Cache, () => new HttpClient(new Handler(HttpStatusCode.Forbidden, [])), pin);
            Refused(() => source.PrepareAsync().GetAwaiter().GetResult()); Require(!File.Exists(path), "HTTP 403 ha pubblicato una cache.");
            Refused(() => SmolModelSource.ValidateUri(new Uri("https://example.org/archive.rar")));
        });
        if (originalArchive is not null) check("Smol: RAR originale verificato con geometrie e readme dell'audit", () =>
        {
            using var f = new Fixture(root); SmolModelSource.VerifyArchive(originalArchive);
            var prepared = new SmolModelPackBuilder(f.Cache).Prepare(f.Report, originalArchive);
            Require(prepared.Package.Manifest.Files.Count == 9, "Subset originale incompleto.");
            SmolModelPackBuilder.VerifyForInstall(f.Report, prepared);
        });
    }
    private sealed record Item(string Name, byte[] Bytes, bool CorruptCrc = false, uint Attributes = 0x20, int? DeclaredSize = null);
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game => Path.Combine(Root, "game");
        public string Cache => Path.Combine(Root, "cache");
        public GameReport Report { get; }
        public Item[] Members { get; }
        public Fixture(string root)
        {
            Root = Path.Combine(root, "smol-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Game);
            var exe = Path.Combine(Game, "speed2.exe"); File.WriteAllBytes(exe, [1, 2, 3, 4]);
            Report = new(1, DateTimeOffset.UtcNow, exe, Hash(File.ReadAllBytes(exe)), "x86 (32 bit)", ["D3D9"], [], [], false, false);
            byte[] geometry = [0, 0, 0x13, 0x80, 8, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];
            byte[] texture = [0, 0, 0x30, 0xb3, 8, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];
            Members = new[] { "GOLF", "IS300", "SENTRA" }.Select(c => new Item("_setup/" + c + "/GEOMETRY.BIN", geometry)).Concat([
                new("readme/english.html", Encoding.UTF8.GetBytes("<html>AJ_Lethal original readme</html>\r\n")),
                new("readme/spanish.html", Encoding.UTF8.GetBytes("<html>AJ_Lethal original Spanish</html>\r\n"))]).ToArray();
            foreach (var car in new[] { "GOLF", "IS300", "SENTRA" })
            {
                var dir = Path.Combine(Game, "CARS", car); Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "GEOMETRY.BIN"), geometry); File.WriteAllBytes(Path.Combine(dir, "TEXTURES.BIN"), texture);
            }
        }
        public SmolModelPackBuilder.Pin Pin(string archive) => new(new FileInfo(archive).Length, Hash(File.ReadAllBytes(archive)), Members.Select((m, i) =>
            new SmolModelPackBuilder.Member(m.Name, i < 3 ? m.Name.Replace("_setup/", "CARS/", StringComparison.Ordinal)
            : "GamePanelAssets/smol-cars/README-" + (i == 3 ? "English" : "Espanol") + ".html.txt", m.Bytes.Length, Hash(m.Bytes))).ToArray());
        public SmolModelPackBuilder Builder(string archive) => new(Cache, Pin(archive));
        // Minimal RAR4 STORE writer for synthetic data only. No external tool or binary fixture is needed.
        public string Archive(params Item[] extras)
        {
            var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".rar");
            using var output = new BinaryWriter(File.Create(path)); output.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0 });
            Header(output, 0x73, 0, new byte[6]);
            foreach (var item in Members.Concat(extras))
            {
                var name = Encoding.ASCII.GetBytes(item.Name); using var body = new MemoryStream(); using (var writer = new BinaryWriter(body, Encoding.UTF8, true))
                {
                    writer.Write(item.Bytes.Length); writer.Write(item.DeclaredSize ?? item.Bytes.Length); writer.Write((byte)2);
                    writer.Write(item.CorruptCrc ? Crc(item.Bytes) ^ 1 : Crc(item.Bytes)); writer.Write(0u); writer.Write((byte)20); writer.Write((byte)0x30);
                    writer.Write((ushort)name.Length); writer.Write(item.Attributes); writer.Write(name);
                }
                Header(output, 0x74, 0x8000, body.ToArray()); output.Write(item.Bytes);
            }
            Header(output, 0x7b, 0, []); return path;
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private static void Header(BinaryWriter output, byte type, ushort flags, byte[] body)
    {
        using var data = new MemoryStream(); using (var writer = new BinaryWriter(data, Encoding.UTF8, true))
        { writer.Write(type); writer.Write(flags); writer.Write((ushort)(7 + body.Length)); writer.Write(body); }
        var bytes = data.ToArray(); output.Write((ushort)Crc(bytes)); output.Write(bytes);
    }
    private static uint Crc(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var b in bytes) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; }
        return ~crc;
    }
    private sealed class Handler(HttpStatusCode status, byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(bytes) });
    }
    private static string[] Snapshot(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p => p + ":" + Hash(File.ReadAllBytes(p))).ToArray();
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Refused(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; } catch (SharpCompress.Common.ArchiveException) { return; }
        throw new InvalidOperationException("Input Smol non valido accettato.");
    }
}
