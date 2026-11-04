using System.Security.Cryptography;
using GamePanel.Core;

internal static class Underground1RemixChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> makeExe)
    {
        check("UG1 Remix: registrazione locale non scrive e non inventa varianti originali", () =>
        {
            using var f = new Fixture(root, makeExe); var before = Snapshot(f.Game);
            var status = f.Service.Status(f.Exe);
            Require(status.Supported && status.LocalInstallation && status.HasSuperResolution && status.HasRayReconstruction && status.HasFrameGeneration && !status.HasTransformer, "Disponibilità locale errata.");
            var plan = f.Service.PrepareAdopt(f.Report); Require(plan.Changes.Count == 0, "Adozione scrive configurazioni.");
            status = f.Service.Execute(plan);
            Require(status.Adopted && !status.Changed && !f.Service.ListVariants().Any() && before.SequenceEqual(Snapshot(f.Game)), "Registrazione cambia il gioco o crea una relazione inventata.");
        });
        check("UG1 Remix: DLSS conserva risoluzione, GPU, asset e binari; ripristino byte per byte", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt(); var before = Snapshot(f.Game);
            var plan = f.Service.PrepareApply(f.Report, new(DlssMode: 4, Upscaler: 1, RayReconstruction: true, FrameGeneration: true, GeneratedFrames: 1, Reflex: 1));
            Require(before.SequenceEqual(Snapshot(f.Game)) && plan.SettingChanges.All(c => c.Path == "rtx.conf"), "Anteprima scrive o cambia file non previsti.");
            var status = f.Service.Execute(plan); Require(status.Settings.DlssMode == 4 && status.Settings.GeneratedFrames == 1, "DLSS non applicato.");
            var after = Snapshot(f.Game); Require(before.Where(x => !x.StartsWith("rtx.conf:", StringComparison.Ordinal)).SequenceEqual(after.Where(x => !x.StartsWith("rtx.conf:", StringComparison.Ordinal))), "Risoluzione/GPU/runtime cambiati.");
            f.Service.Execute(f.Service.PrepareRestore(f.Report)); Require(before.SequenceEqual(Snapshot(f.Game)), "Ripristino diverso dall'originale.");
            Refused(() => f.Service.PrepareApply(f.Report, recommended: true));
            Refused(() => f.Service.PrepareApply(f.Report, new(Transformer: true)));
            Refused(() => f.Service.PrepareApply(f.Report, new(FrameGeneration: true, GeneratedFrames: 3)));
        });
        check("UG1 Remix: cambi runtime dopo anteprima e dopo registrazione bloccano scritture", () =>
        {
            foreach (var file in new[] { "d3d9.dll", ".trex/d3d9.dll", "scripts/RTXRemix.asi", ".trex/nvngx_dlss.dll" })
            {
                using var f = new Fixture(root, makeExe); var plan = f.Service.PrepareAdopt(f.Report);
                File.AppendAllText(f.Path(file), "external-change"); var before = Snapshot(f.Game);
                Refused(() => f.Service.Execute(plan)); Require(before.SequenceEqual(Snapshot(f.Game)), "Anteprima ignora cambio runtime.");
            }
            using var g = new Fixture(root, makeExe); g.Adopt(); File.AppendAllText(g.Path("scripts/NFSUnderground.WidescreenFix.asi"), "external-change");
            Require(!g.Service.Status(g.Exe).Supported, "Runtime cambiato dopo adozione accettato.");
            Refused(() => g.Service.PrepareCapture(g.Report));
        });
        check("UG1 Remix: componenti assenti e modello non dichiarato non abilitano funzioni", () =>
        {
            using var f = new Fixture(root, makeExe); File.Delete(f.Path(".trex/nvngx_dlssd.dll")); File.Delete(f.Path(".trex/nvngx_dlssg.dll"));
            f.Adopt(); var status = f.Service.Status(f.Exe); Require(!status.HasRayReconstruction && !status.HasFrameGeneration, "Funzioni non installate abilitate.");
            Refused(() => f.Service.PrepareApply(f.Report, new(RayReconstruction: true)));
            Refused(() => f.Service.PrepareApply(f.Report, new(FrameGeneration: true)));
            using var g = new Fixture(root, makeExe); File.Delete(g.Path("scripts/RTXRemix.asi"));
            Require(!g.Service.Status(g.Exe).Supported && !Underground1RemixInstallation.HasRenderer(g.Game), "Nomi di file scambiati per installazione completa.");
            using var h = new Fixture(root, makeExe); File.AppendAllText(h.Exe, "unknown"); Require(!h.Service.Status(h.Exe).Supported, "EXE sconosciuto accettato.");
        });
        check("UG1 Remix: operazioni interrotte recuperabili senza toccare Widescreen Fix", () =>
        {
            foreach (var rollback in new[] { false, true })
            {
                using var f = new Fixture(root, makeExe); f.Adopt(); var before = Snapshot(f.Game);
                try { f.Service.Execute(f.Service.PrepareApply(f.Report, new(DlssMode: 4)), progress: new CancelProgress()); throw new Exception("Annullamento ignorato."); }
                catch (OperationCanceledException) { }
                Require(f.Service.HasPendingOperation(f.Exe), "Journal interrotto non riconosciuto.");
                f.Service.Recover(f.Exe, rollback);
                Require(!f.Service.HasPendingOperation(f.Exe) && (rollback ? before.SequenceEqual(Snapshot(f.Game)) : f.Service.Status(f.Exe).Settings.DlssMode == 4), "Recupero locale errato.");
            }
        });
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public string Game { get; }
        public string Exe => Path("Speed.exe");
        public GameReport Report => new GameInspector().Inspect(Exe);
        public RemixManagedService Service { get; }
        public Fixture(string root, Func<string, bool, bool, string, string> makeExe)
        {
            _root = System.IO.Path.Combine(root, "ug1-remix-" + Guid.NewGuid().ToString("N")); Game = System.IO.Path.Combine(_root, "Game");
            Directory.CreateDirectory(Path(".trex")); Directory.CreateDirectory(Path("scripts"));
            makeExe(Exe, false, false, "d3d9.dll");
            foreach (var p in new[] { "d3d9.dll", "dinput8.dll", "scripts/NFSUnderground.WidescreenFix.asi", "scripts/RTXRemix.asi" }) makeExe(Path(p), false, true, "kernel32.dll");
            makeExe(Path(".trex/d3d9.dll"), true, true, "vulkan-1.dll"); makeExe(Path(".trex/NvRemixBridge.exe"), true, false, "kernel32.dll");
            foreach (var name in new[] { "nvngx_dlss.dll", "nvngx_dlssd.dll", "nvngx_dlssg.dll" }) makeExe(Path(".trex/" + name), true, true, "kernel32.dll");
            File.WriteAllText(Path("scripts/NFSUnderground.WidescreenFix.ini"), "[MAIN]\r\nResX=3440\r\nResY=1440\r\n[MISC]\r\nShowLangSelect=1\r\n");
            File.WriteAllText(Path("dxvk.conf"), "d3d9.customDeviceId = 0110\nd3d9.customVendorId = 10de\n");
            File.WriteAllText(Path("rtx.conf"), "# author's configuration\nrtx.qualityDLSS = 1\nrtx.dlfg.enable = False\nrtx.uiTextures = 0x1122334455667788\n");
            Service = new(System.IO.Path.Combine(_root, "Profiles"), _ => false,
                new(new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64), Underground1Executable: Hash(File.ReadAllBytes(Exe))));
        }
        public string Path(string relative) => System.IO.Path.Combine(Game, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public void Adopt() => Service.Execute(Service.PrepareAdopt(Report));
        public void Dispose() => Directory.Delete(_root, true);
    }
    private sealed class CancelProgress : IProgress<BackupProgress> { public void Report(BackupProgress p) { if (p.Completed == 1) throw new OperationCanceledException(); } }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string[] Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(p => System.IO.Path.GetRelativePath(root, p).Replace('\\', '/') + ":" + Hash(File.ReadAllBytes(p))).Order(StringComparer.Ordinal).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refused(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Operazione non valida accettata."); }
}
