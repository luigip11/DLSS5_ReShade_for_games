using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class RemixArchiveChecks
{
    public static void Register(Action<string, Action> check, string root)
    {
        check("Remix: cartella autore riconosciuta senza confondere runtime x64 e client di installazione", () =>
        {
            const string wrapper = "Underground 2 Remix 0.4";
            var (path, audit) = Fixture(root, wrapper + "/d3d9.dll", wrapper + "/.trex/d3d9.dll");
            var report = audit.Analyze(path);
            Require(!report.HasRootD3d9 && report.HasInstallationD3d9 && report.InstallationRoot == wrapper, "Client dentro la cartella autore non riconosciuto.");
            Require(RemixArchiveAudit.FindInstallationRoot([new(".trex/d3d9.dll", 1, false), new("README.txt", 1, false)]) is null, "Runtime interno scambiato per client nella radice.");
            Require(RemixArchiveAudit.FindInstallationRoot([new("one/d3d9.dll", 1, false), new("two/data.dds", 1, false)]) is null, "Radice ambigua normalizzata automaticamente.");
        });
        check("Remix: vera tabella 7z, hash e report relativo senza estrazione", () =>
        {
            var (path, audit) = Fixture(root, "d3d9.dll", "rtx-remix/mods/test/mod.usda");
            var before = File.ReadAllBytes(path); var report = audit.Analyze(path);
            Require(report.MatchesPublishedSource && report.Files.Count == 2 && report.HasRootD3d9, "Inventario 7z incompleto.");
            Require(report.Sha256 == Hex(SHA256.HashData(before)) && report.Md5 == Hex(MD5.HashData(before)), "Hash errati.");
            var json = JsonSerializer.Serialize(report);
            Require(!json.Contains(root, StringComparison.Ordinal) && !json.Contains(path, StringComparison.Ordinal), "Percorso locale nel report.");
            Require(File.ReadAllBytes(path).SequenceEqual(before) && !File.Exists(Path.Combine(root, "d3d9.dll")), "Contenuto estratto o archivio modificato.");
        });
        check("Remix: percorsi traversal, assoluti, duplicati e collisioni file/cartella rifiutati", () =>
        {
            foreach (var names in new[] { new[] { "../outside.bin" }, new[] { "C:/Users/private/file" }, new[] { "/tmp/file" },
                new[] { "a.dll", "A.DLL" }, new[] { "scripts", "scripts/mod.asi" }, new[] { "a:stream" } })
            {
                var (path, audit) = Fixture(root, names); Refused(() => audit.Analyze(path));
            }
            foreach (var attr in new uint[] { 0x400, 0xa1ff8000 })
            {
                var (path, audit) = Build(root, attr, ["link"]); Refused(() => audit.Analyze(path));
            }
        });
        check("Remix: sorgente sconosciuta non passa al parser e limite voci applicato", () =>
        {
            var (path, audit) = Fixture(root, "d3d9.dll");
            var unknown = new RemixArchiveAudit(new(new FileInfo(path).Length, new string('0', 32))).Analyze(path);
            Require(!unknown.MatchesPublishedSource && unknown.Files.Count == 0 && !unknown.HasRootD3d9, "Archivio sconosciuto interpretato.");
            Refused(() => new RemixArchiveAudit().Analyze(path));
            (path, audit) = Fixture(root, Enumerable.Range(0, RemixArchiveAudit.MaximumEntries + 1).Select(i => "f" + i).ToArray());
            Refused(() => audit.Analyze(path));
        });
        check("Remix: magic, tabella fuori limite, cancellazione e archivio mutato rifiutati", () =>
        {
            var (path, audit) = Fixture(root, "file");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { audit.Analyze(path, canceled.Token); throw new Exception("Cancellazione ignorata."); }
            catch (OperationCanceledException) { }
            var bytes = File.ReadAllBytes(path); bytes[0] ^= 1; File.WriteAllBytes(path, bytes); Refused(() => audit.Analyze(path));
            (path, audit) = Fixture(root, "file"); bytes = File.ReadAllBytes(path);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(20), 17 * 1024 * 1024); File.WriteAllBytes(path, bytes); Refused(() => audit.Analyze(path));
            (path, audit) = Fixture(root, "file");
            Refused(() => audit.Analyze(path, progress: new InlineProgress(_ => File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1)))));
        });
    }

    // Actual uncompressed 7z header with empty regular files: no native tools or embedded executable.
    private static (string Path, RemixArchiveAudit Audit) Fixture(string root, params string[] names) => Build(root, null, names);
    private static (string Path, RemixArchiveAudit Audit) Build(string root, uint? attr, string[] names)
    {
        using var table = new MemoryStream(); table.WriteByte(1); table.WriteByte(5); Number(table, (ulong)names.Length);
        Property(14, Enumerable.Repeat((byte)255, (names.Length + 7) / 8).ToArray());
        Property(15, Enumerable.Repeat((byte)255, (names.Length + 7) / 8).ToArray());
        Property(17, new byte[] { 0 }.Concat(Encoding.Unicode.GetBytes(string.Join('\0', names) + '\0')).ToArray());
        if (attr is uint value)
        {
            var attributes = new byte[2 + 4 * names.Length]; attributes[0] = 1;
            for (var i = 0; i < names.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(2 + 4 * i), value);
            Property(21, attributes);
        }
        table.WriteByte(0); table.WriteByte(0); var content = table.ToArray();
        var header = new byte[32]; new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c, 0, 4 }.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(20), (ulong)content.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), Crc(content));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), Crc(header.AsSpan(12)));
        var bytes = header.Concat(content).ToArray(); var path = Path.Combine(root, "remix-" + Guid.NewGuid().ToString("N") + ".7z");
        File.WriteAllBytes(path, bytes); return (path, new(new(bytes.Length, Hex(MD5.HashData(bytes)))));
        void Property(byte id, byte[] value) { table.WriteByte(id); Number(table, (ulong)value.Length); table.Write(value); }
    }
    private static void Number(Stream stream, ulong value)
    {
        for (var extra = 0; extra < 8; extra++)
            if (value < (1UL << (7 + 7 * extra)))
            {
                stream.WriteByte((byte)((0xff << (8 - extra)) | (int)(value >> (8 * extra))));
                for (var i = 0; i < extra; i++) stream.WriteByte((byte)(value >> (8 * i)));
                return;
            }
        stream.WriteByte(255); for (var i = 0; i < 8; i++) stream.WriteByte((byte)(value >> (8 * i)));
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint value = uint.MaxValue;
        foreach (var b in bytes) { value ^= b; for (var i = 0; i < 8; i++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320); }
        return ~value;
    }
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refused(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Archivio non valido accettato.");
    }
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress>
    { public void Report(BackupProgress value) => action(value); }
}
