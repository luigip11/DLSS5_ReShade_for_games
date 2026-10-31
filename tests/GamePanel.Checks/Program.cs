using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

var checks = new List<(string Name, Action Body)>();
var temp = Path.Combine(Path.GetTempPath(), "GamePanelChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
void Check(string name, Action body) => checks.Add((name, body));
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action body)
{
    try { body(); }
    catch (Exception e) when (e is BadImageFormatException or InvalidDataException or IOException or ArgumentException) { return; }
    throw new Exception("Input non valido accettato.");
}
string Fixture(string name, bool x64 = false, bool dll = false, string imported = "d3d9.dll")
{
    var bytes = new byte[1024];
    void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
    void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; U32(0x3c, 0x80);
    bytes[0x80] = (byte)'P'; bytes[0x81] = (byte)'E';
    U16(0x84, x64 ? (ushort)0x8664 : (ushort)0x14c); U16(0x86, 1);
    U16(0x94, x64 ? (ushort)0xf0 : (ushort)0xe0);
    U16(0x96, dll ? (ushort)0x2102 : (ushort)0x102);
    var opt = 0x98;
    U16(opt, x64 ? (ushort)0x20b : (ushort)0x10b);
    U32(opt + 16, 0x1000); U32(opt + 20, 0x1000);
    U32(opt + 32, 0x1000); U32(opt + 36, 0x200);
    U32(opt + 56, 0x2000); U32(opt + 60, 0x200); U16(opt + 68, 3);
    var directories = opt + (x64 ? 112 : 96);
    U32(directories - 4, 16); U32(directories + 8, 0x1000); U32(directories + 12, 40);
    var section = opt + (x64 ? 0xf0 : 0xe0);
    Encoding.ASCII.GetBytes(".rdata").CopyTo(bytes, section);
    U32(section + 8, 512); U32(section + 12, 0x1000);
    U32(section + 16, 512); U32(section + 20, 512); U32(section + 36, 0x40000040);
    U32(0x200 + 12, 0x1040); U32(0x200 + 16, 0x1060);
    Encoding.ASCII.GetBytes(imported + "\0").CopyTo(bytes, 0x240);
    var path = Path.Combine(temp, name);
    File.WriteAllBytes(path, bytes);
    return path;
}

Check("Percorso Windows con spazi, parentesi e virgolette di Explorer", () =>
{
    const string windowsPath = @"C:\Program Files (x86)\EA GAMES\Need for Speed Underground 2\speed2.exe";
    Require(ExecutablePath.Normalize("  \"" + windowsPath + "\"  ") == windowsPath, "Percorso quotato alterato.");
    Require(ExecutablePath.Normalize(windowsPath) == windowsPath, "Percorso non quotato alterato.");
});
Check("L'analizzatore accetta il percorso reale quotato con spazi", () =>
{
    var relative = Path.Combine("Program Files (x86)", "EA GAMES", "Need for Speed Underground 2", "speed2.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(temp, relative))!);
    var path = Fixture(relative);
    var report = new GameInspector().Inspect("\"" + path + "\"");
    Require(report.ExecutablePath == path, "File diverso da quello selezionato.");
    Require(report.GraphicsApis.Contains("Direct3D 9"), "File non analizzato.");
});
Check("Rifiuto percorsi vuoti, multipli o virgolette non bilanciate", () =>
{
    foreach (var invalid in new string?[] { null, "", "   ", "\"\"", "\"C:\\game.exe", "C:\\game.exe\"", "C:\\game.exe\nD:\\other.exe", "\"C:\\game.exe\" --argument", "C:\\game.lnk", "C:\\game.exe\0" })
        Reject(() => ExecutablePath.Normalize(invalid));
});
Check("File mancante distinto dagli errori PE", () =>
{
    try { new GameInspector().Inspect(Path.Combine(temp, "missing.exe")); }
    catch (FileNotFoundException) { return; }
    throw new Exception("File mancante non segnalato correttamente.");
});
Check("Una cartella con estensione EXE non è un eseguibile", () =>
{
    var directory = Path.Combine(temp, "folder.exe"); Directory.CreateDirectory(directory);
    try { new GameInspector().Inspect(directory); }
    catch (InvalidDataException) { return; }
    throw new Exception("Cartella accettata come eseguibile.");
});
Check("PE x86: import grafico e architettura", () =>
{
    var pe = PeInspector.Read(Fixture("legacy.exe"));
    Require(pe.Architecture == "x86 (32 bit)", "Architettura errata.");
    Require(pe.Imports.Contains("d3d9.dll"), "Import non trovato.");
});
Check("PE x64 e Direct3D 12", () =>
{
    var report = new GameInspector().Inspect(Fixture("modern.exe", true, imported: "d3d12.dll"));
    Require(report.Architecture.StartsWith("x64"), "Architettura errata.");
    Require(report.GraphicsApis.SequenceEqual(["Direct3D 12"]), "API errata.");
});
Check("L'analisi non modifica file e rileva componenti", () =>
{
    var path = Fixture("readonly.exe");
    var addon = Path.Combine(temp, "nvngx_dlss.dll");
    File.WriteAllText(addon, "Fixture, non una DLL eseguibile");
    var before = Directory.GetFiles(temp).ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    var report = new GameInspector().Inspect(path);
    var after = Directory.GetFiles(temp).ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    Require(before.Count == after.Count && before.All(kv => after[kv.Key] == kv.Value), "File modificati.");
    Require(report.Components.Any(c => c.Kind == "DLSS Super Resolution"), "Componente non rilevato.");
    Require(report.Sha256 == before[path].ToLowerInvariant(), "Hash errato.");
    using var json = JsonDocument.Parse(GameInspector.ToJson(report));
    Require(json.RootElement.GetProperty("SchemaVersion").GetInt32() == 1, "Schema report errato.");
});
Check("Rifiuto DLL rinominata EXE", () => Reject(() => new GameInspector().Inspect(Fixture("dll.exe", dll: true))));
Check("Rifiuto file non PE", () =>
{
    var path = Path.Combine(temp, "text.exe"); File.WriteAllText(path, "hello");
    Reject(() => PeInspector.Read(path));
});
Check("Rifiuto import RVA fuori dal file", () =>
{
    var path = Fixture("bad-rva.exe"); var bytes = File.ReadAllBytes(path);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x20c), 0x7fffffff);
    File.WriteAllBytes(path, bytes); Reject(() => PeInspector.Read(path));
});
Check("Rifiuto nome import con percorso", () => Reject(() => PeInspector.Read(Fixture("bad-name.exe", imported: "../bad.dll"))));
Check("Rifiuto estensione diversa da EXE", () => Reject(() => new GameInspector().Inspect(Fixture("game.dll"))));
Check("Cancellazione prima della scansione", () =>
{
    var path = Fixture("cancel.exe"); using var source = new CancellationTokenSource(); source.Cancel();
    try { new GameInspector().Inspect(path, source.Token); }
    catch (OperationCanceledException) { return; }
    throw new Exception("Cancellazione ignorata.");
});
Check("Indicatore anti-cheat", () =>
{
    File.WriteAllText(Path.Combine(temp, "EasyAntiCheat_EOS.dll"), "fixture");
    Require(new GameInspector().Inspect(Fixture("anticheat.exe")).HasAntiCheatIndicators, "Indicatore non trovato.");
});
Check("Scansione non segue collegamenti", () =>
{
    var link = Path.Combine(temp, "linked-directory");
    var target = Path.Combine(Path.GetTempPath(), "GamePanelOutside-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(target);
    try
    {
        File.WriteAllText(Path.Combine(target, "nvngx_dlssg.dll"), "outside");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception e) when (OperatingSystem.IsWindows() && e is UnauthorizedAccessException or IOException)
        { throw new SkipCheckException("Creazione symlink non disponibile per questo utente Windows."); }
        var report = new GameInspector().Inspect(Fixture("symlink.exe"));
        Require(report.ScanWasLimited, "Collegamento non segnalato.");
        Require(!report.Components.Any(c => c.RelativePath.StartsWith("linked-directory")), "Seguito collegamento.");
    }
    finally { if (Directory.Exists(link)) Directory.Delete(link); Directory.Delete(target, true); }
});
BackupChecks.Register(Check, temp);
LibraryChecks.Register(Check, temp, Fixture);
ProfileChecks.Register(Check, temp, Fixture, args.Contains("--verify-official-reshade", StringComparer.Ordinal));
var localWideAt = Array.IndexOf(args, "--widescreen-package");
WideChecks.Register(Check, temp, Fixture, args.Contains("--verify-official-reshade", StringComparer.Ordinal), localWideAt >= 0 && localWideAt + 1 < args.Length ? args[localWideAt + 1] : null);
UpdateChecks.Register(Check, temp, Fixture);
var failed = 0;
var skipped = 0;
try
{
    foreach (var check in checks)
    {
        try { check.Body(); Console.WriteLine("PASS " + check.Name); }
        catch (SkipCheckException e) { skipped++; Console.WriteLine("SKIP " + check.Name + ": " + e.Message); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + check.Name + ": " + e.Message); }
    }
    Console.WriteLine($"{checks.Count - failed - skipped}/{checks.Count} controlli superati; {skipped} saltati; {failed} falliti.");
}
finally { Directory.Delete(temp, true); }
return failed == 0 ? 0 : 1;

internal sealed class SkipCheckException(string message) : Exception(message);
