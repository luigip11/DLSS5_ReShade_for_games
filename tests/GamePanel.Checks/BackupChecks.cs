using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamePanel.Core;

internal static class BackupChecks
{
    public static void Register(Action<string, Action> check, string testRoot)
    {
        check("Backup: copie persistenti senza modificare il gioco", () =>
        {
            using var f = new Fixture(testRoot); var before = f.GameHashes();
            var snapshot = f.Create(); var after = f.GameHashes();
            Require(before.Count == after.Count && before.All(p => after[p.Key] == p.Value), "Il backup ha modificato il gioco.");
            Require(snapshot.Files.Count(x => x.Existed) == 3, "File originali mancanti nel backup.");
            Require(snapshot.Files.Any(x => x.RelativePath == "dxgi.dll" && !x.Existed), "Assenza originale non registrata.");
            var list = new BackupService(f.Store, _ => false).List(f.Exe);
            Require(list.Single().Id == snapshot.Id, "Backup non persistente.");
        });
        check("Backup: verifica degli originali già presenti", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create();
            var plan = f.Service.Preview(f.Exe, s.Id);
            Require(!plan.HasConflicts && plan.MissingCount == 0, "Baseline non riconosciuta.");
            Require(f.Service.Restore(f.Exe, s.Id).Restored == 0, "File identici riscritti.");
        });
        check("Ripristino: EXE e DLL mancanti tornano identici", () =>
        {
            using var f = new Fixture(testRoot); var expected = f.GameHashes(); var s = f.Create();
            File.Delete(f.Exe); File.Delete(f.Dll);
            Require(f.Service.Preview(f.Exe, s.Id).MissingCount == 2, "Anteprima errata.");
            var result = f.Service.Restore(f.Exe, s.Id);
            Require(result.Restored == 2, "Recupero incompleto.");
            Require(expected.All(p => f.GameHashes()[p.Key] == p.Value), "Hash originali non ripristinati.");
            Require(f.Service.List(f.Exe).Count == 1, "Backup consumato o eliminato.");
        });
        check("Ripristino: file cambiati bloccano anche gli altri recuperi", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create();
            File.WriteAllText(f.Dll, "mod o aggiornamento esterno"); File.Delete(f.Exe);
            Require(f.Service.Preview(f.Exe, s.Id).HasConflicts, "Conflitto non visto.");
            Refused(() => f.Service.Restore(f.Exe, s.Id));
            Require(!File.Exists(f.Exe) && File.ReadAllText(f.Dll) == "mod o aggiornamento esterno", "Scrittura non autorizzata.");
        });
        check("Ripristino: un nuovo loader non viene cancellato", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create();
            var added = Path.Combine(f.Game, "dxgi.dll"); File.WriteAllText(added, "altro tool");
            Refused(() => f.Service.Restore(f.Exe, s.Id));
            Require(File.ReadAllText(added) == "altro tool", "Loader esterno eliminato.");
        });
        check("Ripristino: copia danneggiata rifiutata prima delle scritture", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create();
            File.AppendAllText(Directory.GetFiles(Path.Combine(f.Snapshot(s.Id), "files")).First(), "corruzione");
            File.Delete(f.Exe);
            Refused(() => f.Service.Preview(f.Exe, s.Id)); Refused(() => f.Service.Restore(f.Exe, s.Id));
            Require(!File.Exists(f.Exe), "Ripristinato da backup corrotto.");
        });
        check("Backup: report obsoleto non crea snapshot utilizzabile", () =>
        {
            using var f = new Fixture(testRoot); var report = f.Report(); File.WriteAllText(f.Exe, "aggiornamento");
            Refused(() => f.Service.Create(report));
            Require(f.Service.List(f.Exe).Count == 0, "Snapshot obsoleto offerto al ripristino.");
        });
        check("Backup: annullamento mantiene gli originali e scarta copie incomplete", () =>
        {
            using var f = new Fixture(testRoot); var before = f.GameHashes(); using var c = new CancellationTokenSource();
            Cancelled(() => f.Service.Create(f.Report(), c.Token, new InlineProgress(_ => c.Cancel())));
            Require(f.Service.List(f.Exe).Count == 0, "Backup incompleto elencato.");
            Require(before.All(p => f.GameHashes()[p.Key] == p.Value), "Originali cambiati durante annullamento.");
        });
        check("Ripristino: annullamento e ripresa dopo riavvio del servizio", () =>
        {
            using var f = new Fixture(testRoot); var expected = f.GameHashes(); var s = f.Create();
            File.Delete(f.Dll); File.Delete(f.Exe); using var c = new CancellationTokenSource();
            Cancelled(() => f.Service.Restore(f.Exe, s.Id, c.Token, new InlineProgress(_ => c.Cancel())));
            Require(File.Exists(f.Dll) && !File.Exists(f.Exe), "Stato parziale inatteso.");
            var result = new BackupService(f.Store, _ => false).Restore(f.Exe, s.Id);
            Require(result.Resumed && result.Restored == 1, "Journal non ripreso.");
            Require(expected.All(p => f.GameHashes()[p.Key] == p.Value), "Hash errati dopo ripresa.");
        });
        check("Ripristino: guasto filesystem a metà operazione recuperabile", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Dll); File.Delete(f.Exe);
            Refused(() => f.Service.Restore(f.Exe, s.Id, progress: new InlineProgress(p => { if (p.Completed == 1) Directory.CreateDirectory(f.Exe); })));
            Require(File.Exists(f.Dll) && Directory.Exists(f.Exe), "Originale recuperato perso dopo guasto.");
            Directory.Delete(f.Exe);
            Require(f.Service.Restore(f.Exe, s.Id).Resumed && File.Exists(f.Exe), "Ripresa dopo errore IO fallita.");
        });
        check("Ripristino: copia temporanea parziale viene ricreata dal backup", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Dll); File.Delete(f.Exe);
            using var c = new CancellationTokenSource();
            Cancelled(() => f.Service.Restore(f.Exe, s.Id, c.Token, new InlineProgress(_ => c.Cancel())));
            var journal = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Snapshot(s.Id), "repair.json")))!;
            var op = journal["OperationId"]!.GetValue<string>();
            var index = s.Files.ToList().FindIndex(x => x.RelativePath == "game.exe");
            var temp = Path.Combine(f.Game, $".gamepanel-{op}-{index:D4}.tmp"); File.WriteAllText(temp, "copia incompleta");
            Require(f.Service.Restore(f.Exe, s.Id).Resumed, "Copia parziale non recuperata.");
            Require(!File.Exists(temp) && File.Exists(f.Exe), "Temporaneo non sostituito correttamente.");
        });
        check("Ripristino: chiusura dopo ultimo file può essere finalizzata", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Exe);
            using var c = new CancellationTokenSource();
            Cancelled(() => f.Service.Restore(f.Exe, s.Id, c.Token, new InlineProgress(_ => c.Cancel())));
            var plan = f.Service.Preview(f.Exe, s.Id);
            Require(plan.MissingCount == 0 && plan.HasPendingRepair, "Stato finale interrotto non visibile.");
            Require(f.Service.Restore(f.Exe, s.Id).Resumed, "Finalizzazione non eseguita.");
            Require(!f.Service.Preview(f.Exe, s.Id).HasPendingRepair, "Journal lasciato aperto.");
        });
        check("Ripristino: journal senza ultimo completamento resta recuperabile", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Dll); File.Delete(f.Exe);
            using var c = new CancellationTokenSource();
            Cancelled(() => f.Service.Restore(f.Exe, s.Id, c.Token, new InlineProgress(_ => c.Cancel())));
            var path = Path.Combine(f.Snapshot(s.Id), "repair.json"); var j = JsonNode.Parse(File.ReadAllText(path))!;
            j["CompletedFiles"] = new JsonArray(); File.WriteAllText(path, j.ToJsonString());
            Require(f.Service.Restore(f.Exe, s.Id).Resumed, "Stato dopo rename non recuperabile.");
        });
        check("Ripristino: metadati con traversal non scrivono fuori dal gioco", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); var outside = Path.Combine(f.Root, "outside.dll"); File.WriteAllText(outside, "preserva");
            f.EditManifest(s.Id, m => m["Files"]![0]!["RelativePath"] = "../outside.dll");
            Refused(() => f.Service.Restore(f.Exe, s.Id)); Require(File.ReadAllText(outside) == "preserva", "Scrittura fuori root.");
        });
        check("Ripristino: identità del gioco vincolata al percorso originale", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create();
            f.EditManifest(s.Id, m => m["ExecutablePath"] = Path.Combine(f.Root, "other", "game.exe"));
            Refused(() => f.Service.Preview(f.Exe, s.Id));
        });
        check("Ripristino: ID snapshot non può contenere percorsi", () =>
        {
            using var f = new Fixture(testRoot); f.Create();
            Refused(() => f.Service.Preview(f.Exe, "../outside"));
        });
        check("Backup: archivio dentro la cartella del gioco rifiutato", () =>
        {
            using var f = new Fixture(testRoot);
            Refused(() => new BackupService(Path.Combine(f.Game, "backups"), _ => false).Create(f.Report()));
            Require(!Directory.Exists(Path.Combine(f.Game, "backups")), "Archivio creato nel gioco.");
        });
        check("Backup e ripristino: gioco in esecuzione blocca le operazioni", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); var active = new BackupService(f.Store, _ => true);
            Refused(() => active.Create(f.Report())); Refused(() => active.Preview(f.Exe, s.Id)); Refused(() => active.Restore(f.Exe, s.Id));
        });
        check("Backup: errore nel controllo processi blocca le scritture", () =>
        {
            using var f = new Fixture(testRoot);
            Refused(() => new BackupService(f.Store, _ => throw new IOException("process query failed")).Create(f.Report()));
            Require(!Directory.Exists(f.Store), "Archivio creato senza controllo processi.");
        });
        check("Backup: il gioco avviato durante la copia invalida lo snapshot", () =>
        {
            using var f = new Fixture(testRoot); var running = false; var service = new BackupService(f.Store, _ => running);
            Refused(() => service.Create(f.Report(), progress: new InlineProgress(_ => running = true)));
            Require(f.Service.List(f.Exe).Count == 0, "Snapshot durante partita considerato completo.");
        });
        check("Ripristino: un solo writer per gioco", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Exe);
            using var locked = new FileStream(Path.Combine(Path.GetDirectoryName(f.Snapshot(s.Id))!, ".operation.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Refused(() => f.Service.Restore(f.Exe, s.Id)); Require(!File.Exists(f.Exe), "Lock ignorato.");
        });
        check("Ripristino: file comparso dopo anteprima causa conflitto", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Exe);
            Require(f.Service.Preview(f.Exe, s.Id).MissingCount == 1, "Anteprima inattesa.");
            File.WriteAllText(f.Exe, "nuova build"); Refused(() => f.Service.Restore(f.Exe, s.Id));
            Require(File.ReadAllText(f.Exe) == "nuova build", "Nuova build sovrascritta.");
        });
        check("Ripristino: loader nuovo durante recupero sospende senza cancellarlo", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Dll); File.Delete(f.Exe);
            var path = Path.Combine(f.Game, "dxgi.dll");
            Refused(() => f.Service.Restore(f.Exe, s.Id, progress: new InlineProgress(_ => File.WriteAllText(path, "tool esterno"))));
            Require(File.ReadAllText(path) == "tool esterno" && !File.Exists(f.Exe), "Conflitto in corso ignorato.");
            File.Delete(path); Require(f.Service.Restore(f.Exe, s.Id).Resumed, "Ripresa non riuscita.");
        });
        check("Backup: limite reale di dimensione controllato prima della copia", () =>
        {
            using var f = new Fixture(testRoot); using (var stream = File.OpenWrite(f.Dll)) stream.SetLength(BackupService.MaximumFileBytes + 1);
            Refused(() => f.Create()); Require(f.Service.List(f.Exe).Count == 0, "File oltre limite accettato.");
        });
        check("Backup: percorsi dei componenti non possono uscire dalla root", () =>
        {
            using var f = new Fixture(testRoot);
            var report = f.Report() with { Components = [new("../foreign.dll", "fixture", "1")] };
            Refused(() => f.Service.Create(report));
        });
        check("Backup: nomi dispositivo e path ambigui sono rifiutati", () =>
        {
            using var f = new Fixture(testRoot);
            foreach (var name in new[] { "NUL.dll", "LPT1.ini", "folder./file.dll", "file.dll:stream", "folder//file.dll" })
                Refused(() => f.Service.Create(f.Report() with { Components = [new(name, "fixture", "1")] }));
        });
        check("Ripristino: journal malformato non attiva scritture", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create(); File.Delete(f.Exe);
            File.WriteAllText(Path.Combine(f.Snapshot(s.Id), "repair.json"), "not json");
            Refused(() => f.Service.Restore(f.Exe, s.Id)); Require(!File.Exists(f.Exe), "Journal corrotto ignorato.");
        });
        check("Backup: link nei componenti o nell'archivio bloccati", () =>
        {
            using var f = new Fixture(testRoot); var s = f.Create();
            var payload = Directory.GetFiles(Path.Combine(f.Snapshot(s.Id), "files")).First();
            var outside = Path.Combine(f.Root, "outside.bin"); File.WriteAllText(outside, "preserva"); File.Delete(payload);
            try { File.CreateSymbolicLink(payload, outside); }
            catch (Exception e) when (OperatingSystem.IsWindows() && e is IOException or UnauthorizedAccessException)
            { throw new SkipCheckException("Symlink non disponibili per questo utente."); }
            Refused(() => f.Service.Preview(f.Exe, s.Id));
            Require(File.ReadAllText(outside) == "preserva", "File esterno alterato.");
        });
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refused(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException) { return; }
        throw new Exception("Operazione pericolosa non rifiutata.");
    }
    private static void Cancelled(Action action)
    {
        try { action(); } catch (OperationCanceledException) { return; }
        throw new Exception("Operazione non annullata.");
    }
    private sealed class InlineProgress(Action<BackupProgress> callback) : IProgress<BackupProgress>
    { public void Report(BackupProgress value) => callback(value); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game { get; }
        public string Store { get; }
        public string Exe => Path.Combine(Game, "game.exe");
        public string Dll => Path.Combine(Game, "d3d9.dll");
        public BackupService Service { get; }
        public Fixture(string root)
        {
            Root = Path.Combine(root, "backup-" + Guid.NewGuid().ToString("N"));
            Game = Path.Combine(Root, "Program Files (x86)", "Test Game"); Store = Path.Combine(Root, "persistent-store");
            Directory.CreateDirectory(Game); File.WriteAllText(Exe, "test executable bytes, never executed");
            File.WriteAllText(Dll, "test original DLL, never loaded"); File.WriteAllText(Path.Combine(Game, "ReShade.ini"), "original config");
            Service = new(Store, _ => false);
        }
        public GameReport Report() => new(1, DateTimeOffset.UtcNow, Exe, Hash(Exe), "x86 (32 bit)", ["Direct3D 9"],
            [new("d3d9.dll", "fixture", "1"), new("ReShade.ini", "fixture", "1")], [], false, false);
        public BackupManifest Create() => Service.Create(Report());
        public string Snapshot(string id) => Directory.GetDirectories(Store).Select(p => Path.Combine(p, id)).Single(Directory.Exists);
        public Dictionary<string, string> GameHashes() => Directory.GetFiles(Game).ToDictionary(p => Path.GetFileName(p), Hash);
        public void EditManifest(string id, Action<JsonNode> edit)
        {
            var path = Path.Combine(Snapshot(id), "snapshot.json"); var node = JsonNode.Parse(File.ReadAllText(path))!;
            edit(node); File.WriteAllText(path, node.ToJsonString());
        }
        private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
