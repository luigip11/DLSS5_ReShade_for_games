using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class RemixBundleChecks
{
    public static void Register(Action<string, Action> check, string root)
    {
        check("Remix bundle: veri contenuti 7z selezionati, hash per membro e nessuna installazione", () =>
        {
            var (source, output, bundle) = Fixture(root);
            var original = File.ReadAllBytes(source); var result = bundle.Prepare(source, output);
            Require(result.FilesCount == 2 && result.Bytes == 50 && result.SourceSha256 == Hex(SHA256.HashData(original)), "Risultato bundle errato.");
            using var zip = ZipFile.OpenRead(output);
            Require(zip.Entries.Count == 3 && zip.GetEntry("huge.dds") is null, "Membri fuori allowlist esportati.");
            using var manifestStream = zip.GetEntry("GamePanel-RemixComponents.json")!.Open();
            using var manifest = JsonDocument.Parse(manifestStream);
            Require(!manifest.RootElement.GetRawText().Contains(root, StringComparison.Ordinal), "Percorso locale nel manifest.");
            foreach (var member in manifest.RootElement.GetProperty("Files").EnumerateArray())
            {
                var name = member.GetProperty("RelativePath").GetString()!;
                using var data = zip.GetEntry(name)!.Open(); using var bytes = new MemoryStream(); data.CopyTo(bytes);
                Require(member.GetProperty("Sha256").GetString() == Hex(SHA256.HashData(bytes.ToArray()))
                    && member.GetProperty("Bytes").GetInt64() == bytes.Length, "Hash o dimensione del membro errati.");
            }
            Require(File.ReadAllBytes(source).SequenceEqual(original) && !File.Exists(Path.Combine(root, "d3d9.dll")), "Archivio modificato o DLL installata.");
        });
        check("Remix bundle: hash sorgente diverso, traversal e membro mancante non pubblicano ZIP", () =>
        {
            var (source, output, _) = Fixture(root);
            var bytes = File.ReadAllBytes(source);
            var wrong = new RemixComponentBundle(new(bytes.Length, new string('0', 64), Hex(MD5.HashData(bytes)), Members));
            Refused(() => wrong.Prepare(source, output)); Require(!File.Exists(output), "ZIP pubblicato con sorgente errata.");
            var bad = Fixture(root, traversal: true); Refused(() => bad.Bundle.Prepare(bad.Source, bad.Output));
            Require(!File.Exists(bad.Output), "ZIP pubblicato con traversal.");
            var missing = new RemixComponentBundle(new(bytes.Length, Hex(SHA256.HashData(bytes)), Hex(MD5.HashData(bytes)),
                new Dictionary<string, long> { ["absent.asi"] = 1 }));
            Refused(() => missing.Prepare(source, output)); Require(!File.Exists(output), "ZIP incompleto pubblicato.");
            Require(!Directory.EnumerateFiles(root, ".pending-*").Any(), "File temporaneo residuo.");
        });
        check("Remix bundle: annullamento prima e durante lettura elimina il file provvisorio", () =>
        {
            var (source, output, bundle) = Fixture(root); using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Canceled(() => bundle.Prepare(source, output, canceled.Token));
            using var during = new CancellationTokenSource();
            Canceled(() => bundle.Prepare(source, output, during.Token, new InlineProgress(p =>
            { if (p.Message.StartsWith("Esportazione dati Remix:", StringComparison.Ordinal)) during.Cancel(); })));
            Require(!File.Exists(output) && !Directory.EnumerateFiles(root, ".pending-*").Any(), "Output lasciato dopo annullamento.");
        });
        check("Remix bundle: destinazione e archivio esistenti non vengono sovrascritti", () =>
        {
            var (source, output, bundle) = Fixture(root); File.WriteAllText(output, "original");
            Refused(() => bundle.Prepare(source, output)); Require(File.ReadAllText(output) == "original", "Destinazione sovrascritta.");
            var before = File.ReadAllBytes(source); Refused(() => bundle.Prepare(source, source));
            Require(File.ReadAllBytes(source).SequenceEqual(before), "Archivio sorgente sovrascritto.");
        });
    }

    private static readonly IReadOnlyDictionary<string, long> Members = new Dictionary<string, long>
    { ["d3d9.dll"] = 31, ["rtx.conf"] = 19 };
    // Tiny genuine 7z/copy archives generated with libarchive: three harmless text payloads, never executable code.
    private const string Good = "N3q8ryccAAO9izcoSQAAAAAAAAALAQAAAAAAADNvyvFGQUtFIERMTCBEQVRBLCBuZXZlciBleGVjdXRhYmxlcnR4LnNldHRpbmcgPSB0cnVlClVOU0VMRUNURUQgVEVYVFVSRSBEQVRBAQQGAAMJHxMXAAcLAwABAQABAQABAQAMHxMXAAgKAYiuhVNr/S9vSdFSVQAABQMRgMcAVQBuAGQAZQByAGcAcgBvAHUAbgBkACAAMgAgAFIAZQBtAGkAeAAgADAALgA0AC8AZAAzAGQAOQAuAGQAbABsAAAAVQBuAGQAZQByAGcAcgBvAHUAbgBkACAAMgAgAFIAZQBtAGkAeAAgADAALgA0AC8AcgB0AHgALgBjAG8AbgBmAAAAVQBuAGQAZQByAGcAcgBvAHUAbgBkACAAMgAgAFIAZQBtAGkAeAAgADAALgA0AC8AaAB1AGcAZQAuAGQAZABzAAAAFQ4BACCApIEggKSBIICkgQAA";
    private const string Traversal = "N3q8ryccAAN9OsQOSQAAAAAAAADnAAAAAAAAAE8bUi9GQUtFIERMTCBEQVRBLCBuZXZlciBleGVjdXRhYmxlcnR4LnNldHRpbmcgPSB0cnVlClVOU0VMRUNURUQgVEVYVFVSRSBEQVRBAQQGAAMJHxMXAAcLAwABAQABAQABAQAMHxMXAAgKAYiuhVNr/S9vSdFSVQAABQMRgKMAVQBuAGQAZQByAGcAcgBvAHUAbgBkACAAMgAgAFIAZQBtAGkAeAAgADAALgA0AC8AZAAzAGQAOQAuAGQAbABsAAAALgAuAC8AbwB1AHQAcwBpAGQAZQAuAGQAbABsAAAAVQBuAGQAZQByAGcAcgBvAHUAbgBkACAAMgAgAFIAZQBtAGkAeAAgADAALgA0AC8AaAB1AGcAZQAuAGQAZABzAAAAFQ4BACCApIEggKSBIICkgQAA";
    private static (string Source, string Output, RemixComponentBundle Bundle) Fixture(string root, bool traversal = false)
    {
        var bytes = Convert.FromBase64String(traversal ? Traversal : Good); var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(root, "components-" + id + ".7z"); File.WriteAllBytes(path, bytes);
        return (path, Path.Combine(root, "components-" + id + ".zip"),
            new(new(bytes.Length, Hex(SHA256.HashData(bytes)), Hex(MD5.HashData(bytes)), Members)));
    }
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refused(Action action)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Esportazione non valida accettata."); }
    private static void Canceled(Action action)
    { try { action(); } catch (OperationCanceledException) { return; } throw new Exception("Annullamento ignorato."); }
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress>
    { public void Report(BackupProgress value) => action(value); }
}
