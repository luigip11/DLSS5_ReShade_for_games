using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class RemixTrialChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> makeExe)
    {
        check("Remix trial: copia separata, EXE/CD e Wide conservati, AntiCull escluso, free camera spenta", () =>
        {
            using var f = new Fixture(root, makeExe); var before = Snapshot(f.Game); var plan = f.Prepare();
            Require(!Directory.Exists(f.Destination) && before.SequenceEqual(Snapshot(f.Game)), "Anteprima ha scritto nel gioco o nella destinazione.");
            Require(plan.OriginalBytes > 0 && plan.ArchiveBytes > 0 && plan.RequiredBytes >= plan.OriginalBytes + plan.ArchiveBytes
                && plan.AvailableBytes >= plan.RequiredBytes && plan.FileCount > 0, "Stima spazio non valida.");
            var result = f.Service.Create(plan);
            Require(result.ExecutablePath == Path.Combine(f.Destination, "speed2.exe") && Hash(result.ExecutablePath) == Hash(f.Exe), "EXE sostituito.");
            foreach (var path in WidescreenCatalog.Targets)
                Require(Hash(f.Clone(path)) == Hash(f.Path(path)), "Wide funzionante sostituito: " + path);
            Require(File.ReadAllText(f.Clone("dxvk.conf")).Contains("rtx.useFreeCamera = false", StringComparison.Ordinal)
                && File.ReadAllText(f.Clone("rtx.conf")) == "rtx.dlfg.enable = False\r\n", "Configurazione autore interpretata o free camera attiva.");
            Require(!File.Exists(f.Clone("scripts/NFSU2AntiCull.asi")) && !File.Exists(f.Clone("scripts/NFSU_XtendedInput.asii"))
                && File.ReadAllText(f.Clone("LICENSE.txt")) == "Author license fixture\n", "Plugin esclusi presenti o licenze perse.");
            Require(File.ReadAllText(f.Clone("CARS/SMOL/GEOMETRY.BIN")) == "existing model modification" && !Directory.Exists(f.Clone("TexturePacks"))
                && !File.Exists(f.Clone("ReShade.ini")) && !File.Exists(f.Clone("ReShade.log")), "Dati modello persi o stack/log alternativo copiato.");
            using var manifest = JsonDocument.Parse(File.ReadAllText(f.Clone("GamePanel-RemixTrial.json")));
            Require(!manifest.RootElement.GetProperty("AntiCullIncluded").GetBoolean()
                && manifest.RootElement.GetProperty("SourceExecutableSha256").GetString() == Hash(f.Exe)
                && manifest.RootElement.GetProperty("ArchiveFiles").GetArrayLength() == 5, "Manifest copia incompleto.");
            foreach (var file in manifest.RootElement.GetProperty("Files").EnumerateArray())
                Require(Hash(f.Clone(file.GetProperty("RelativePath").GetString()!)) == file.GetProperty("Sha256").GetString(), "Hash manifest non corrispondente.");
            Require(result.FileCount == Directory.GetFiles(f.Destination, "*", SearchOption.AllDirectories).Length
                && result.Bytes == Directory.GetFiles(f.Destination, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length), "Conteggio finale errato.");
            Require(before.SequenceEqual(Snapshot(f.Game)), "Creazione ha modificato la sorgente.");
        });
        check("Remix trial: cartella vuota ammessa; cartella non vuota, gioco, profili e sorgente archivio rifiutati", () =>
        {
            using var f = new Fixture(root, makeExe); Directory.CreateDirectory(f.Destination);
            var plan = f.Prepare(); f.Service.Create(plan); Require(File.Exists(f.Clone("speed2.exe")), "Cartella vuota non ammessa.");
            using var g = new Fixture(root, makeExe); Directory.CreateDirectory(g.Destination); File.WriteAllText(g.Clone("keep.txt"), "preserve");
            Refused(() => g.Prepare()); Require(File.ReadAllText(g.Clone("keep.txt")) == "preserve", "Cartella esistente alterata.");
            foreach (var destination in new[] { g.Game, g.Path("Trial"), g.Root, g.Store, g.Archive, Path.Combine(GamePanelPaths.Root, "trial") })
                Refused(() => g.Service.Prepare(g.Report(), g.Archive, destination));
        });
        check("Remix trial: plugin ignoti, proxy estranei e Wide non più verificato bloccano senza scritture", () =>
        {
            foreach (var name in new[] { "scripts/unknown.asi", "version.dll", WidescreenCatalog.Plugin, WidescreenCatalog.Loader })
            {
                using var f = new Fixture(root, makeExe); File.WriteAllText(f.Path(name), "foreign bytes"); var before = Snapshot(f.Game);
                Refused(() => f.Prepare()); Require(!Directory.Exists(f.Destination) && before.SequenceEqual(Snapshot(f.Game)), "Sorgente toccata dopo conflitto.");
            }
            using var missing = new Fixture(root, makeExe, installWide: false); Refused(() => missing.Prepare());
        });
        check("Remix trial: EXE, altri file o tabella sorgente cambiati dopo anteprima rifiutano la copia", () =>
        {
            foreach (var change in new Action<Fixture>[] { f => File.AppendAllText(f.Exe, "change"),
                f => File.AppendAllText(f.Path("CARS/SMOL/GEOMETRY.BIN"), "change"),
                f => File.WriteAllText(f.Path("new.dat"), "added"), f => File.Delete(f.Path("CARS/SMOL/GEOMETRY.BIN")),
                f => File.AppendAllText(f.Archive, "change") })
            {
                using var f = new Fixture(root, makeExe); var plan = f.Prepare(); change(f); var before = Snapshot(f.Game);
                Refused(() => f.Service.Create(plan)); Require(!Directory.Exists(f.Destination) && before.SequenceEqual(Snapshot(f.Game)) && !f.Staging().Any(), "Output parziale pubblicato o sorgente alterata.");
            }
        });
        check("Remix trial: annullamento prima, durante copia e durante estrazione pulisce solo lo staging posseduto", () =>
        {
            foreach (var prefix in new[] { "", "Copia gioco per Remix:", "Estrazione copia Remix:" })
            {
                using var f = new Fixture(root, makeExe); var plan = f.Prepare(); var before = Snapshot(f.Game);
                using var canceled = new CancellationTokenSource(); if (prefix == "") canceled.Cancel();
                Canceled(() => f.Service.Create(plan, canceled.Token, new InlineProgress(p => { if (prefix != "" && p.Message.StartsWith(prefix, StringComparison.Ordinal)) canceled.Cancel(); })));
                Require(!Directory.Exists(f.Destination) && !f.Staging().Any() && before.SequenceEqual(Snapshot(f.Game)), "Annullamento ha lasciato una copia parziale o alterato il gioco.");
            }
        });
        check("Remix trial: gioco aperto, hash archivio errato e destinazione diventata occupata non sovrascrivono", () =>
        {
            using var f = new Fixture(root, makeExe); var pin = f.Pin with { Sha256 = new string('0', 64) };
            Refused(() => new RemixTrialService(pin, f.Store, _ => false, f.WidePins).Prepare(f.Report(), f.Archive, f.Destination));
            Refused(() => new RemixTrialService(f.Pin, f.Store, _ => true, f.WidePins).Prepare(f.Report(), f.Archive, f.Destination));
            var plan = f.Prepare(); var occupied = false;
            Refused(() => f.Service.Create(plan, progress: new InlineProgress(p =>
            {
                if (!occupied && p.Message.StartsWith("Copia gioco per Remix:", StringComparison.Ordinal))
                { Directory.CreateDirectory(f.Destination); File.WriteAllText(f.Clone("keep.txt"), "preserve"); occupied = true; }
            })));
            Require(File.ReadAllText(f.Clone("keep.txt")) == "preserve" && !f.Staging().Any(), "Destinazione occupata sostituita.");
        });
        if (!OperatingSystem.IsWindows()) check("Remix trial: link in sorgente o destinazione rifiutati senza seguire il target", () =>
        {
            using var f = new Fixture(root, makeExe); var outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "preserve"); Directory.CreateSymbolicLink(f.Path("linked"), outside);
            Refused(() => f.Prepare()); Directory.Delete(f.Path("linked"));
            Directory.CreateSymbolicLink(f.Destination, outside); Refused(() => f.Prepare()); Directory.Delete(f.Destination);
            Require(File.ReadAllText(Path.Combine(outside, "keep.txt")) == "preserve", "Target di un link alterato.");
        });
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game => System.IO.Path.Combine(Root, "Game");
        public string Store => System.IO.Path.Combine(Root, "Profiles");
        public string Exe => Path("speed2.exe");
        public string Archive => System.IO.Path.Combine(Root, "author.7z");
        public string Destination => System.IO.Path.Combine(Root, "Remix trial");
        public RemixTrialService.Pin Pin { get; }
        public IReadOnlyDictionary<string, (long Bytes, string Hash)> WidePins { get; }
        public RemixTrialService Service { get; }
        public Fixture(string root, Func<string, bool, bool, string, string> makeExe, bool installWide = true)
        {
            Root = System.IO.Path.Combine(root, "remix-trial-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Game);
            makeExe(Exe, false, false, "d3d9.dll");
            var payloads = WidescreenCatalog.Targets.ToDictionary(p => p, p => Encoding.UTF8.GetBytes("Fixture bytes, never loaded: " + p));
            WidePins = payloads.ToDictionary(p => p.Key, p => (p.Value.LongLength, Hex(SHA256.HashData(p.Value))));
            if (installWide)
            {
                var wide = ReShadeProfileService.ForWidescreen(Store, _ => false);
                wide.Execute(wide.PrepareWidescreen(Report(), new VerifiedWidescreen(payloads)));
                File.WriteAllText(Path(WidescreenCatalog.Ini), "[MAIN]\r\nResX=3440\r\nResY=1440\r\nNoOpticalDriveFix=0\r\n");
            }
            Directory.CreateDirectory(Path("CARS/SMOL")); File.WriteAllText(Path("CARS/SMOL/GEOMETRY.BIN"), "existing model modification");
            Directory.CreateDirectory(Path("TexturePacks")); File.WriteAllText(Path("TexturePacks/disabled.dds"), "excluded texture stack");
            File.WriteAllText(Path("ReShade.ini"), "excluded config");
            File.WriteAllText(Path("ReShade.log"), "old renderer resolution log");
            var bytes = Convert.FromBase64String(ArchiveFixture); File.WriteAllBytes(Archive, bytes);
            Pin = new(bytes.LongLength, Hex(SHA256.HashData(bytes)), Hex(MD5.HashData(bytes)), Hash(Exe), Hex(SHA256.HashData(Encoding.UTF8.GetBytes("FAKE REMIX CLIENT; harmless text, never loaded"))));
            Service = new(Pin, Store, _ => false, WidePins);
        }
        public string Path(string relative) => System.IO.Path.Combine(Game, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public string Clone(string relative) => System.IO.Path.Combine(Destination, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public IEnumerable<string> Staging() => Directory.EnumerateDirectories(Root, ".gamepanel-remix-*");
        public GameReport Report() => new GameInspector().Inspect(Exe);
        public RemixTrialPlan Prepare() => Service.Prepare(Report(), Archive, Destination);
        public void Dispose() => Directory.Delete(Root, true);
    }
    // Genuine 7z/copy fixture generated with libarchive. All members are harmless text, never loaded or executed.
    private const string ArchiveFixture = "N3q8ryccAAP2fHHc/wAAAAAAAAClAwAAAAAAAO3X4QNGQUtFIFJFTUlYIENMSUVOVDsgaGFybWxlc3MgdGV4dCwgbmV2ZXIgbG9hZGVkIyBydHgudXNlRnJlZUNhbWVyYSA9IHRydWUNCnJ0eC51c2VGcmVlQ2FtZXJhID0gdHJ1ZQ0KcnR4LmRsZmcuZW5hYmxlID0gRmFsc2UNCk9NSVQgQU5USUNVTEw7IGhhcm1sZXNzIHRleHRSZXNYPTE5MjAKUmVzWT0xMDgwCkRJU0FCTEVEOyBoYXJtbGVzcyB0ZXh0T01JVCBMT0FERVI7IGhhcm1sZXNzIHRleHRBdXRob3IgbGljZW5zZSBmaXh0dXJlCiN1c2RhIDEuMAoBBAYACQkuNhkcFBcaFwoABwsJAAEBAAEBAAEBAAEBAAEBAAEBAAEBAAEBAAEBAAwuNhkcFBcaFwoACAoBuADTst+gAVcB/zmfwXuMKLJFBS3AZ/tYamETgv24wESCcEznAAAFCRGDEwBVAG4AZABlAHIAZwByAG8AdQBuAGQAIAAyACAAUgBlAG0AaQB4ACAAMAAuADQALwBkADMAZAA5AC4AZABsAGwAAABVAG4AZABlAHIAZwByAG8AdQBuAGQAIAAyACAAUgBlAG0AaQB4ACAAMAAuADQALwBkAHgAdgBrAC4AYwBvAG4AZgAAAFUAbgBkAGUAcgBnAHIAbwB1AG4AZAAgADIAIABSAGUAbQBpAHgAIAAwAC4ANAAvAHIAdAB4AC4AYwBvAG4AZgAAAFUAbgBkAGUAcgBnAHIAbwB1AG4AZAAgADIAIABSAGUAbQBpAHgAIAAwAC4ANAAvAHMAYwByAGkAcAB0AHMALwBOAEYAUwBVADIAQQBuAHQAaQBDAHUAbABsAC4AYQBzAGkAAABVAG4AZABlAHIAZwByAG8AdQBuAGQAIAAyACAAUgBlAG0AaQB4ACAAMAAuADQALwBzAGMAcgBpAHAAdABzAC8ATgBGAFMAVQBuAGQAZQByAGcAcgBvAHUAbgBkADIALgBXAGkAZABlAHMAYwByAGUAZQBuAEYAaQB4AC4AaQBuAGkAAABVAG4AZABlAHIAZwByAG8AdQBuAGQAIAAyACAAUgBlAG0AaQB4ACAAMAAuADQALwBzAGMAcgBpAHAAdABzAC8ATgBGAFMAVQBfAFgAdABlAG4AZABlAGQASQBuAHAAdQB0AC4AYQBzAGkAaQAAAFUAbgBkAGUAcgBnAHIAbwB1AG4AZAAgADIAIABSAGUAbQBpAHgAIAAwAC4ANAAvAGQAaQBuAHAAdQB0ADgALgBkAGwAbAAAAFUAbgBkAGUAcgBnAHIAbwB1AG4AZAAgADIAIABSAGUAbQBpAHgAIAAwAC4ANAAvAEwASQBDAEUATgBTAEUALgB0AHgAdAAAAFUAbgBkAGUAcgBnAHIAbwB1AG4AZAAgADIAIABSAGUAbQBpAHgAIAAwAC4ANAAvAHIAdAB4AC0AcgBlAG0AaQB4AC8AbQBvAGQAcwAvAHMAYwBlAG4AZQAuAHUAcwBkAGEAAAAVJgEAIICkgSCApIEggKSBIICkgSCApIEggKSBIICkgSCApIEggKSBAAA=";
    private static string[] Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(root, p) + ":" + Hash(p)).Order(StringComparer.Ordinal).ToArray();
    private static string Hash(string path) => Hex(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refused(Action body)
    { try { body(); } catch (InvalidDataException) { return; } throw new Exception("Prova non valida accettata."); }
    private static void Canceled(Action body)
    { try { body(); } catch (OperationCanceledException) { return; } throw new Exception("Annullamento ignorato."); }
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress>
    { public void Report(BackupProgress value) => action(value); }
}
