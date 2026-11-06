using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GamePanel.Core;

internal static class RemixPackChecks
{
    public static void Register(Action<string, Action> check, string testRoot, Func<string, bool, bool, string, string> makeExe)
    {
        check("Pacchetto Remix: anteprima senza scritture, copia separata, EXE e FrontB conservati", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var exe = Hash(f.Exe);
            var plan = f.Prepare(); Require(!Directory.Exists(f.Destination), "Anteprima ha creato la copia.");
            var result = f.Service.Create(plan);
            Require(Hash(f.Exe) == exe && Hash(result.ExecutablePath) == exe, "EXE alterato.");
            Require(File.ReadAllText(Path.Combine(result.Folder, "FRONTEND", "FrontB.lzc")) == "original-ui", "FrontB sovrascritto.");
            Require(File.ReadAllText(f.GameFile("d3d9.dll")) == "old-sdr", "Sorgente modificata.");
            Require(File.ReadAllText(Path.Combine(result.Folder, "d3d9.dll")) == "reviewed-runtime", "Runtime errato.");
            Require(!File.Exists(Path.Combine(result.Folder, "ReShade.ini")) && !File.Exists(Path.Combine(result.Folder, "GamePanel-Old.json")), "Stato o ReShade ereditati.");
            Require(File.ReadAllText(Path.Combine(result.Folder, "GamePanelAssets", "smol-cars", "README.txt")) == "author-credits", "Crediti dei modelli esistenti persi.");
            Require(File.Exists(Path.Combine(result.Folder, RemixPackService.Marker)), "Identita della copia assente.");
        });
        check("Pacchetto Remix: modelli opzionali e configurazione personale conservata", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            File.WriteAllText(f.BundleFile("rtx.conf"), "rtx.dlfg.enable = False\n");
            var plan = f.Prepare(models: true); Require(plan.PersonalizedConfigurations == 1, "Personalizzazione non segnalata.");
            var result = f.Service.Create(plan);
            Require(File.ReadAllText(Path.Combine(result.Folder, "CARS", "GOLF", "GEOMETRY.BIN")) == "reviewed-model", "Modello non applicato.");
            Require(File.ReadAllText(Path.Combine(result.Folder, "rtx.conf")) == "rtx.dlfg.enable = False\n", "Configurazione alterata.");
        });
        check("Pacchetto Remix: componente corrotto, EXE cambiato e destinazione occupata rifiutati", () =>
        {
            using var f = new Fixture(testRoot, makeExe); File.WriteAllText(f.BundleFile("d3d9.dll"), "bad"); Refused(() => f.Prepare());
            File.WriteAllText(f.BundleFile("d3d9.dll"), "reviewed-runtime"); var report = f.Report(); File.AppendAllText(f.Exe, "changed");
            Refused(() => f.Service.Prepare(report, f.Bundle, f.Destination));
            makeExe(f.Exe, false, false, "d3d9.dll"); f.ResetService();
            Directory.CreateDirectory(f.Destination); File.WriteAllText(Path.Combine(f.Destination, "keep.txt"), "keep");
            Refused(() => f.Prepare()); Require(File.ReadAllText(Path.Combine(f.Destination, "keep.txt")) == "keep", "Destinazione alterata.");
        });
        check("Pacchetto Remix: modifiche dopo anteprima e interruzione non pubblicano copie parziali", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var plan = f.Prepare(); File.WriteAllText(f.GameFile("FRONTEND/FrontB.lzc"), "changed-ui");
            Refused(() => f.Service.Create(plan)); Require(!Directory.Exists(f.Destination), "Copia parziale pubblicata.");
            Require(!Directory.EnumerateDirectories(f.Root, ".gamepanel-package-*").Any(), "Staging rimasto.");
            plan = f.Prepare(); using var cancel = new CancellationTokenSource();
            try { f.Service.Create(plan, cancel.Token, new ImmediateProgress(_ => cancel.Cancel())); throw new Exception("Interruzione ignorata."); }
            catch (OperationCanceledException) { }
            Require(!Directory.Exists(f.Destination) && !Directory.EnumerateDirectories(f.Root, ".gamepanel-package-*").Any(), "Interruzione ha lasciato una copia pubblicata o temporanea.");
        });
        check("Pacchetto Remix: ZIP di esportazione include solo componenti, importazione con verifica", () =>
        {
            using var f = new Fixture(testRoot, makeExe); File.WriteAllText(f.BundleFile("speed2.exe"), "must-never-export");
            var path = Path.Combine(f.Root, "mod.zip"); var exported = f.Service.Export(f.Bundle, path);
            using (var zip = ZipFile.OpenRead(path)) Require(zip.Entries.Count == 3 && zip.Entries.All(e => !e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)), "File del gioco incluso.");
            Require(Hash(path) == exported.Sha256, "Hash ZIP errato.");
            var result = f.Service.Create(f.Service.Prepare(f.Report(), path, f.Destination));
            Require(File.Exists(result.ExecutablePath), "Importazione ZIP non completata.");
        });
        check("GitHub Remix: parti, hash e ricostruzione senza file del gioco", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            var zip = f.Service.Export(f.Bundle, Path.Combine(f.Root, "export.zip"));
            var release = f.Service.ExportReleaseParts(zip, Path.Combine(f.Root, "release"), 100);
            Require(release.Parts > 1, "Suddivisione non avvenuta.");
            var joined = f.Service.JoinReleaseParts(release.ManifestPath, Path.Combine(f.Root, "joined.zip"));
            Require(joined.Sha256 == zip.Sha256 && Hash(joined.Path) == Hash(zip.Path), "Ricostruzione diversa.");
            f.Service.Prepare(f.Report(), joined.Path, f.Destination);
            var first = Directory.GetFiles(release.Folder, "*.part001").Single(); File.AppendAllText(first, "corrupt");
            var failed = Path.Combine(f.Root, "corrupt.zip"); Refused(() => f.Service.JoinReleaseParts(release.ManifestPath, failed));
            Require(!File.Exists(failed) && Hash(zip.Path) == zip.Sha256, "Pubblicato ZIP corrotto o modificato originale.");
        });
        check("GitHub Remix: hash sorgente cambiato, manifest ostile e annullamento rifiutati", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            var zip = f.Service.Export(f.Bundle, Path.Combine(f.Root, "export.zip"));
            var destination = Path.Combine(f.Root, "cancelled"); using var cancel = new CancellationTokenSource();
            try { f.Service.ExportReleaseParts(zip, destination, 100, cancel.Token, new ImmediateProgress(_ => cancel.Cancel())); throw new Exception("Interruzione ignorata."); }
            catch (OperationCanceledException) { }
            Require(!Directory.Exists(destination) && !Directory.GetDirectories(f.Root, ".gamepanel-release-*").Any(), "Parti temporanee rimaste.");
            Refused(() => f.Service.ExportReleaseParts(zip with { Sha256 = new string('0', 64) }, destination, 100));
            var release = f.Service.ExportReleaseParts(zip, destination, 100);
            var json = File.ReadAllText(release.ManifestPath).Replace("GamePanel-UG2-Remix.zip.part001", "../escape");
            File.WriteAllText(release.ManifestPath, json);
            Refused(() => f.Service.JoinReleaseParts(release.ManifestPath, Path.Combine(f.Root, "invalid.zip")));
            Require(Hash(zip.Path) == zip.Sha256, "ZIP sorgente modificato.");
        });
        check("Pacchetto Remix: ZIP con traversal, file estranei o duplicati rifiutato", () =>
        {
            foreach (var entry in new[] { "../escape.dll", "speed2.exe", "d3d9.dll" })
            {
                using var f = new Fixture(testRoot, makeExe); var path = Path.Combine(f.Root, "mod.zip"); f.Service.Export(f.Bundle, path);
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("foreign"); }
                Refused(() => f.Service.Prepare(f.Report(), path, f.Destination));
                Require(!Directory.Exists(f.Destination) && !File.Exists(Path.Combine(f.Root, "escape.dll")), "ZIP non valido ha prodotto scritture.");
            }
        });
        check("Pacchetto Remix: percorsi annidati e configurazioni fuori limite rifiutati", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            Refused(() => f.Service.Prepare(f.Report(), f.Bundle, Path.Combine(f.Game, "copy")));
            Refused(() => f.Service.Prepare(f.Report(), f.Bundle, Path.Combine(f.Bundle, "copy")));
            File.WriteAllBytes(f.BundleFile("rtx.conf"), new byte[1024 * 1024 + 1]); Refused(() => f.Prepare());
        });
        check("Pacchetto Remix: metadati malformati non attivano il profilo e non causano crash", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var marker = Path.Combine(f.Game, RemixPackService.Marker);
            foreach (var text in new[] { "not-json", "{}", "{\"Game\":null}", "{\"Game\":\"UG2\",\"SourceFolder\":null}" })
            {
                File.WriteAllText(marker, text); Require(!RemixPackService.HasMetadata(f.Game, f.Report().Sha256), "Metadati non validi accettati.");
                Refused(() => RemixPackService.ReadOrigin(f.Game, f.Report().Sha256));
            }
        });
        check("Cataloghi Remix: nessun EXE gioco o FrontB UG2, modelli e runtime distinti", () =>
        {
            _ = new RemixPackService("UG1"); _ = new RemixPackService("UG2");
            var a = RemixPackDefinition.ForGame("UG1"); var b = RemixPackDefinition.ForGame("UG2");
            Require(a.Files.Count > 3000 && b.Files.Count > 2500, "Cataloghi incompleti.");
            Require(!a.Files.Any(f => f.Path.Equals(a.ExecutableName, StringComparison.OrdinalIgnoreCase)) && !b.Files.Any(f => f.Path.Equals(b.ExecutableName, StringComparison.OrdinalIgnoreCase)), "EXE gioco nel catalogo.");
            Require(!b.Files.Any(f => f.Path.Equals("FRONTEND/FrontB.lzc", StringComparison.OrdinalIgnoreCase)), "FrontB UG2 nel pacchetto.");
            Require(b.Files.Count(f => f.Kind == "models") == 5 && a.Files.Any(f => f.Kind == "base-change"), "Modelli o modifiche UG1 non separate.");
            Require(a.Files.Single(f => f.Path == "d3d9.dll").Sha256 != b.Files.Single(f => f.Path == "d3d9.dll").Sha256, "Runtime dei giochi unificati.");
        });
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game => Path.Combine(Root, "game");
        public string Bundle => Path.Combine(Root, "bundle");
        public string Exe => Path.Combine(Game, "speed2.exe");
        public string Destination => Path.Combine(Root, "copy");
        public RemixPackService Service { get; private set; } = null!;
        private readonly List<RemixPackFile> _files = [];
        public Fixture(string testRoot, Func<string, bool, bool, string, string> makeExe)
        {
            Root = Path.Combine(testRoot, "pack-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Game); Directory.CreateDirectory(Bundle);
            makeExe(Exe, false, false, "d3d9.dll");
            Write(GameFile("FRONTEND/FrontB.lzc"), "original-ui"); Write(GameFile("CARS/GOLF/GEOMETRY.BIN"), "original-model");
            Write(GameFile("d3d9.dll"), "old-sdr"); Write(GameFile("ReShade.ini"), "old-settings"); Write(GameFile("GamePanel-Old.json"), "old-state");
            foreach (var (path, text, kind) in new[] { ("d3d9.dll", "reviewed-runtime", "component"), ("rtx.conf", "rtx.dlfg.enable = True\n", "config"), ("rtx-remix/mods/mod.usda", "reviewed-layer", "asset"), ("CARS/GOLF/GEOMETRY.BIN", "reviewed-model", "models") })
            { var file = BundleFile(path); Write(file, text); _files.Add(new(path, new FileInfo(file).Length, Hash(file), kind)); }
            Write(GameFile("GamePanelAssets/smol-cars/README.txt"), "author-credits");
            ResetService();
        }
        public void ResetService() => Service = new(new RemixPackDefinition("fixture", "UG2", "speed2.exe", [Hash(Exe)], _files), _ => false);
        public string GameFile(string p) => Path.Combine(Game, p.Replace('/', Path.DirectorySeparatorChar));
        public string BundleFile(string p) => Path.Combine(Bundle, p.Replace('/', Path.DirectorySeparatorChar));
        public GameReport Report() => new(1, DateTimeOffset.UtcNow, Exe, Hash(Exe), "x86 (32 bit)", ["Direct3D 9"], [], [], false, false);
        public RemixPackPlan Prepare(bool models = false) => Service.Prepare(Report(), Bundle, Destination, models);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class ImmediateProgress(Action<BackupProgress> report) : IProgress<BackupProgress> { public void Report(BackupProgress value) => report(value); }
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refused(Action action)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException) { return; }
        throw new Exception("Operazione non valida accettata.");
    }
}
