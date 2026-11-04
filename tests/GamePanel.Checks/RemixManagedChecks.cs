using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class RemixManagedChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> makeExe)
    {
        check("Remix gestito: adozione senza scritture, nullable reali e relazione libreria persistente", () =>
        {
            using var f = new Fixture(root, makeExe); var source = Snapshot(f.Original); var copy = Snapshot(f.Game);
            var status = f.Service.Status(f.Exe); Require(status.Supported && !status.Adopted && status.Settings.FrameGeneration is null, "Inventati valori assenti o copia non riconosciuta.");
            var plan = f.Service.PrepareAdopt(f.Report); Require(plan.Changes.Count == 0 && plan.SettingChanges.Count == 0 && copy.SequenceEqual(Snapshot(f.Game)), "Anteprima ha scritto nel gioco.");
            status = f.Service.Execute(plan); Require(status.Adopted && !status.Pending && !status.Changed, "Adozione incompleta.");
            Require(copy.SequenceEqual(Snapshot(f.Game)) && source.SequenceEqual(Snapshot(f.Original)), "Adozione ha cambiato i file del gioco.");
            var again = f.NewService(); Require(again.Status(f.Exe).Adopted && again.ListVariants().Single() == new RemixManagedVariant(f.OriginalExe, f.Exe), "Stato/relazione non persistente.");
            Refused(() => f.Service.PrepareAdopt(f.Report));
        });
        check("Remix gestito: DLSS modifica solo chiavi approvate, preserva hash/commenti e user.conf, ripristino esatto", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt(); var initial = f.Configs(); var source = Snapshot(f.Original);
            var plan = f.Service.PrepareApply(f.Report, new(DlssMode: 4, FrameGeneration: true, GeneratedFrames: 3, Reflex: 1));
            Require(plan.SettingChanges.Count == 8 && plan.SettingChanges.All(c => c.Key is "rtx.qualityDLSS" or "rtx.dlfg.enable" or "rtx.dlfg.maxInterpolatedFrames" or "rtx.reflexMode")
                && plan.SettingChanges.Any(c => c.Before == "2" && c.After == "4"), "Diff parametri non corrispondente all'anteprima.");
            Require(initial.SequenceEqual(f.Configs()) && plan.Changes.Count == 2, "Anteprima di modifica non confinata.");
            var status = f.Service.Execute(plan); Require(status.Settings.DlssMode == 4 && status.Settings.FrameGeneration == true && status.Settings.GeneratedFrames == 3, "Override user.conf non aggiornato.");
            foreach (var name in new[] { "rtx.conf", "user.conf" })
            {
                var text = File.ReadAllText(f.Path(name)); Require(text.Contains("# preserve this comment", StringComparison.Ordinal)
                    && text.Contains("custom.unknown = keep", StringComparison.Ordinal) && text.Contains("0x1122334455667788", StringComparison.Ordinal), "Persi commenti, hash o chiavi estranee.");
            }
            f.Service.Execute(f.Service.PrepareRestore(f.Report)); Require(initial.SequenceEqual(f.Configs()), "Ripristino byte per byte non esatto.");
            Require(source.SequenceEqual(Snapshot(f.Original)), "Il gioco originale è stato toccato.");
        });
        check("Remix gestito: preset con provenienza esplicita, cattura non inventa opzioni e configurazione assente non viene creata", () =>
        {
            Require(Hash(RemixRecommendedProfile.UploadedConfiguration) == RemixRecommendedProfile.UploadedSha256, "Export incorporato diverso dall'upload.");
            using var f = new Fixture(root, makeExe, user: false); f.Adopt();
            var status = f.Service.Execute(f.Service.PrepareApply(f.Report, recommended: true));
            Require(!File.Exists(f.Path("user.conf")) && status.Settings.DlssMode == 4 && status.Settings.GeneratedFrames == 3 && status.Settings.FrameGeneration == true, "Preset o user.conf inatteso.");
            var text = File.ReadAllText(f.Path("rtx.conf")); Require(text.Contains("rtx.localtonemap.shadows = 2", StringComparison.Ordinal)
                && text.Contains("rtx.localtonemap.highlights = 4", StringComparison.Ordinal) && text.Contains("rtx.pathMaxBounces = 4", StringComparison.Ordinal), "Valori autore obsoleti non ripristinati.");
            File.AppendAllText(f.Path("rtx.conf"), "\ncustom.edited = external\n"); var before = f.Configs();
            Require(f.Service.Status(f.Exe).Changed, "Modifica esterna non segnalata."); Refused(() => f.Service.PrepareRestore(f.Report));
            f.Service.Execute(f.Service.PrepareCapture(f.Report)); Require(before.SequenceEqual(f.Configs()) && !f.Service.Status(f.Exe).Changed, "Cattura ha modificato il gioco.");
        });
        check("Remix gestito: EXE, client, bridge, marker e sorgente estranei rifiutati prima delle scritture", () =>
        {
            foreach (var name in new[] { "speed2.exe", "d3d9.dll", ".trex/NvRemixBridge.exe", ".trex/d3d9.dll", "GamePanel-RemixTrial.json" })
            {
                using var f = new Fixture(root, makeExe); var plan = f.Service.PrepareAdopt(f.Report); File.AppendAllText(f.Path(name), "foreign");
                var before = Snapshot(f.Game); Refused(() => f.Service.Execute(plan)); Require(before.SequenceEqual(Snapshot(f.Game)), "Copia estranea modificata.");
            }
            using var original = new Fixture(root, makeExe); File.AppendAllText(original.OriginalExe, "changed"); Refused(() => original.Service.PrepareAdopt(original.Report));
            using var noMarker = new Fixture(root, makeExe); File.Delete(noMarker.Path("GamePanel-RemixTrial.json")); Require(!noMarker.Service.Status(noMarker.Exe).Supported, "Gioco generico trattato come Remix.");
            using var remote = new Fixture(root, makeExe);
            var marker = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(remote.Path("GamePanel-RemixTrial.json")))!;
            marker["SourceFolder"] = "\\\\not-a-real-host.invalid\\untrusted";
            File.WriteAllText(remote.Path("GamePanel-RemixTrial.json"), marker.ToJsonString());
            Refused(() => remote.Service.PrepareAdopt(remote.Report));
        });
        check("Remix gestito: journal recupera annullamento dopo il primo file e blocca modifiche esterne", () =>
        {
            foreach (var rollback in new[] { false, true })
            {
                using var f = new Fixture(root, makeExe); f.Adopt(); var before = f.Configs(); var plan = f.Service.PrepareApply(f.Report, new(DlssMode: 4, Reflex: 1));
                using var cts = new CancellationTokenSource(); Canceled(() => f.Service.Execute(plan, cts.Token, new InlineProgress(p => { if (p.Completed == 1) cts.Cancel(); })));
                Require(f.Service.Status(f.Exe).Pending, "Journal non pendente dopo annullamento."); Refused(() => f.Service.PrepareCapture(f.Report));
                var status = f.NewService().Recover(f.Exe, rollback); Require(!status.Pending && (rollback ? before.SequenceEqual(f.Configs()) : status.Settings.DlssMode == 4), "Recupero non esatto.");
            }
            using var g = new Fixture(root, makeExe); g.Adopt(); var p2 = g.Service.PrepareApply(g.Report, new(DlssMode: 4));
            using var cancel = new CancellationTokenSource(); Canceled(() => g.Service.Execute(p2, cancel.Token, new InlineProgress(p => cancel.Cancel())));
            File.AppendAllText(g.Path("user.conf"), "\nforeign = preserve\n"); var changed = g.Configs();
            Refused(() => g.NewService().Recover(g.Exe, false)); Require(changed.SequenceEqual(g.Configs()), "Recupero ha sovrascritto modifiche esterne.");
        });
        check("Remix gestito: configurazioni duplicate, gioco aperto e operazioni peer pendenti bloccano", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt();
            var running = f.NewService(_ => true); Refused(() => running.PrepareApply(f.Report, new(Reflex: 1)));
            File.AppendAllText(f.Path("rtx.conf"), "\nrtx.qualityDLSS = 1\n"); Require(!f.Service.Status(f.Exe).Supported, "Chiave duplicata accettata.");
            using var g = new Fixture(root, makeExe); g.Adopt();
            var stateFile = Directory.GetFiles(g.Store, "remix-managed-state.json", SearchOption.AllDirectories).Single();
            foreach (var name in new[] { "assets-operation.json", "widescreen-operation.json", "directx-replacements-operation.json" })
            {
                var peer = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(stateFile)!, name); File.WriteAllText(peer, "{\"Status\":\"Pending\"}");
                Refused(() => g.Service.PrepareApply(g.Report, new(Reflex: 1))); File.Delete(peer);
            }
            Refused(() => g.Service.PrepareApply(g.Report, new(GeneratedFrames: 8)));
        });
        check("Remix gestito: marker incompleto e client cambiato all'ultimo passo non pubblicano un successo", () =>
        {
            using var malformed = new Fixture(root, makeExe); File.WriteAllText(malformed.Path("GamePanel-RemixTrial.json"), "{\"SchemaVersion\":1}");
            Require(!malformed.Service.Status(malformed.Exe).Supported, "Manifest incompleto accettato.");
            try { malformed.Service.PrepareAdopt(malformed.Report); throw new Exception("Manifest incompleto accettato."); }
            catch (InvalidDataException) { }
            using var f = new Fixture(root, makeExe); f.Adopt(); var before = f.Configs(); var client = File.ReadAllBytes(f.Path("d3d9.dll"));
            var plan = f.Service.PrepareApply(f.Report, new(DlssMode: 4));
            Refused(() => f.Service.Execute(plan, progress: new InlineProgress(p => { if (p.Completed == 2) File.AppendAllText(f.Path("d3d9.dll"), "changed"); })));
            File.WriteAllBytes(f.Path("d3d9.dll"), client); Require(f.Service.Status(f.Exe).Pending, "Operazione dichiarata riuscita con client cambiato.");
            f.NewService().Recover(f.Exe, true); Require(before.SequenceEqual(f.Configs()), "Rollback dopo client ripristinato non esatto.");
            var statePath = Directory.GetFiles(f.Store, "remix-managed-state.json", SearchOption.AllDirectories).Single();
            var journal = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(statePath)!, "remix-managed-operation.json");
            File.WriteAllText(journal, "{\"SchemaVersion\":1}");
            Refused(() => f.Service.HasPendingOperation(f.Exe));
        });
        check("Remix gestito: EXE alternativo identificato richiede cattura esplicita, conserva il journal concluso e non scrive nel gioco", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt();
            var alternate = File.ReadAllBytes(f.Exe).Concat(new byte[] { 42 }).ToArray(); var service = f.VariantService(alternate);
            File.WriteAllBytes(f.Exe, alternate); File.WriteAllBytes(f.OriginalExe, alternate);
            var copy = Snapshot(f.Game); var original = Snapshot(f.Original);
            var status = service.Status(f.Exe);
            Require(status.Supported && status.Adopted && status.Changed && !service.HasPendingOperation(f.Exe), "Cambio EXE o journal precedente non riconosciuti.");
            Refused(() => service.PrepareApply(f.Report, new(DlssMode: 4))); Refused(() => service.PrepareRestore(f.Report));
            var capture = service.PrepareCapture(f.Report);
            Require(capture.PreviousExecutableSha256 != capture.ExecutableSha256 && capture.ExecutableSha256 == Hash(alternate) && capture.Changes.Count == 0, "Cambio EXE non esposto o cattura tenta scritture.");
            status = service.Execute(capture);
            Require(!status.Changed && status.Adopted && copy.SequenceEqual(Snapshot(f.Game)) && original.SequenceEqual(Snapshot(f.Original)), "Registrazione cambia gioco/originale o resta incoerente.");
            status = service.Execute(service.PrepareApply(f.Report, new(DlssMode: 4)));
            Require(status.Settings.DlssMode == 4 && !service.HasPendingOperation(f.Exe), "Configurazione sul nuovo EXE non gestita.");
        });
        check("Remix gestito: migrazione EXE interrotta recuperabile, varianti sconosciute e operazioni precedenti pendenti bloccate", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt(); var initialExe = File.ReadAllBytes(f.Exe);
            var alternate = initialExe.Concat(new byte[] { 43 }).ToArray(); var service = f.VariantService(alternate);
            var oldPlan = f.Service.PrepareApply(f.Report, new(DlssMode: 4));
            Canceled(() => f.Service.Execute(oldPlan, progress: new InlineProgress(p => { if (p.Completed == 1) throw new OperationCanceledException(); })));
            File.WriteAllBytes(f.Exe, alternate); File.WriteAllBytes(f.OriginalExe, alternate);
            Refused(() => service.PrepareCapture(f.Report)); Refused(() => service.Recover(f.Exe, false));
            File.WriteAllBytes(f.Exe, initialExe); File.WriteAllBytes(f.OriginalExe, initialExe); f.Service.Recover(f.Exe, true);
            File.WriteAllBytes(f.Exe, alternate); File.WriteAllBytes(f.OriginalExe, alternate);
            var capture = service.PrepareCapture(f.Report); var configs = f.Configs();
            Canceled(() => service.Execute(capture, progress: new InlineProgress(p => { if (p.Completed == 1) throw new OperationCanceledException(); })));
            service.Recover(f.Exe, true);
            Require(service.Status(f.Exe).Changed && configs.SequenceEqual(f.Configs()), "Rollback migrazione perde lo stato precedente o cambia configurazioni.");
            capture = service.PrepareCapture(f.Report); File.AppendAllText(f.Exe, "unknown");
            Refused(() => service.Execute(capture)); Require(!service.Status(f.Exe).Supported, "EXE sconosciuto accettato.");
        });
        check("Remix gestito: journal concluso rifiuta un nome EXE diverso nella stessa cartella", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt(); var before = f.Configs();
            Refused(() => f.Service.HasPendingOperation(f.Path("other-speed2.exe")));
            Require(before.SequenceEqual(f.Configs()) && !f.Service.HasPendingOperation(f.Exe), "Journal valido modificato o avvio riconosciuto bloccato.");
        });
        if (OperatingSystem.IsWindows()) check("Remix gestito: maiuscole del percorso Windows non invalidano journal, stato o recupero", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt(); var before = f.Configs();
            var alternate = f.Exe.ToUpperInvariant();
            Require(!f.Service.HasPendingOperation(alternate) && f.Service.Status(alternate).Adopted, "Journal concluso o stato rifiutati per sole maiuscole.");
            var report = new GameInspector().Inspect(alternate);
            var plan = f.Service.PrepareApply(report, new(DlssMode: 4));
            Canceled(() => f.Service.Execute(plan, progress: new InlineProgress(p => { if (p.Completed == 1) throw new OperationCanceledException(); })));
            Require(f.Service.HasPendingOperation(f.Exe), "Journal sospeso non riconosciuto dal percorso originale.");
            f.Service.Recover(f.Exe, true);
            Require(before.SequenceEqual(f.Configs()) && !f.Service.HasPendingOperation(alternate), "Recupero fra percorsi equivalenti non esatto.");
        });
        if (!OperatingSystem.IsWindows()) check("Remix gestito: link configurazione e stato non seguiti", () =>
        {
            using var f = new Fixture(root, makeExe); f.Adopt(); var outside = System.IO.Path.Combine(f.Root, "outside.conf"); File.WriteAllText(outside, "preserve = external\n");
            File.Delete(f.Path("user.conf")); File.CreateSymbolicLink(f.Path("user.conf"), outside);
            Require(!f.Service.Status(f.Exe).Supported, "Link configurazione seguito."); Refused(() => f.Service.PrepareCapture(f.Report));
            Require(File.ReadAllText(outside) == "preserve = external\n", "Target del link modificato."); File.Delete(f.Path("user.conf"));
        });
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game => System.IO.Path.Combine(Root, "Remix");
        public string Original => System.IO.Path.Combine(Root, "Original");
        public string Store => System.IO.Path.Combine(Root, "Profiles");
        public string Exe => Path("speed2.exe");
        public string OriginalExe => System.IO.Path.Combine(Original, "speed2.exe");
        private RemixManagedService.Pins Pins { get; }
        public RemixManagedService Service { get; }
        public GameReport Report => new GameInspector().Inspect(Exe);
        public Fixture(string root, Func<string, bool, bool, string, string> makeExe, bool user = true)
        {
            Root = System.IO.Path.Combine(root, "managed-remix-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Game); Directory.CreateDirectory(Original); Directory.CreateDirectory(Path(".trex"));
            makeExe(Exe, false, false, "d3d9.dll"); File.Copy(Exe, OriginalExe);
            File.WriteAllText(Path("d3d9.dll"), "inert client fixture, never loaded"); File.WriteAllText(Path(".trex/NvRemixBridge.exe"), "inert bridge fixture, never loaded");
            File.WriteAllText(Path(".trex/d3d9.dll"), "inert server fixture, never loaded");
            Pins = new(Hash(File.ReadAllBytes(Exe)), Hash(File.ReadAllBytes(Path("d3d9.dll"))), Hash(File.ReadAllBytes(Path(".trex/NvRemixBridge.exe"))), new string('a', 64));
            File.WriteAllText(Path("GamePanel-RemixTrial.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, AntiCullIncluded = false, ArchiveSha256 = Pins.Archive,
                SourceExecutableSha256 = Pins.Executable, SourceFolder = Original,
                Files = new[] { new { RelativePath = "d3d9.dll", Sha256 = Pins.Client, Bytes = new FileInfo(Path("d3d9.dll")).Length },
                    new { RelativePath = ".trex/NvRemixBridge.exe", Sha256 = Pins.Bridge, Bytes = new FileInfo(Path(".trex/NvRemixBridge.exe")).Length },
                    new { RelativePath = ".trex/d3d9.dll", Sha256 = Hash(File.ReadAllBytes(Path(".trex/d3d9.dll"))), Bytes = new FileInfo(Path(".trex/d3d9.dll")).Length } } }));
            var config = "# preserve this comment\r\ncustom.unknown = keep\r\nrtx.uiTextures = 0x1122334455667788\r\nrtx.qualityDLSS = 2\r\nrtx.localtonemap.shadows = 0\r\nrtx.localtonemap.highlights = 5\r\n";
            File.WriteAllText(Path("rtx.conf"), config); if (user) File.WriteAllText(Path("user.conf"), config);
            Service = NewService();
        }
        public RemixManagedService NewService(Func<string, bool>? running = null) => new(Store, running ?? (_ => false), Pins);
        public RemixManagedService VariantService(byte[] executable) => new(Store, _ => false, Pins with { AlternateExecutable = Hash(executable) });
        public void Adopt() => Service.Execute(Service.PrepareAdopt(Report));
        public string Path(string relative) => System.IO.Path.Combine(Game, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public string[] Configs() => new[] { "rtx.conf", "user.conf" }.Select(p => File.Exists(Path(p)) ? Convert.ToBase64String(File.ReadAllBytes(Path(p))) : "MISSING").ToArray();
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class InlineProgress(Action<BackupProgress> callback) : IProgress<BackupProgress> { public void Report(BackupProgress value) => callback(value); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string[] Snapshot(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(p => System.IO.Path.GetRelativePath(folder, p) + ":" + Hash(File.ReadAllBytes(p))).Order(StringComparer.Ordinal).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refused(Action body)
    {
        try { body(); } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException) { return; }
        throw new Exception("Operazione non valida accettata.");
    }
    private static void Canceled(Action body) { try { body(); } catch (OperationCanceledException) { return; } throw new Exception("Annullamento ignorato."); }
}
