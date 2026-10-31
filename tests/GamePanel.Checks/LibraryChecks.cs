using System.Text.Json;
using GamePanel.Core;

internal static class LibraryChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> makeExe)
    {
        GameReport Report(string folder, string name = "game.exe")
        { Directory.CreateDirectory(folder); return new GameInspector().Inspect(makeExe(Path.Combine(folder, name), false, false, "d3d9.dll")); }
        string Folder() { var p = Path.Combine(root, "library-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        check("Libreria: importazione persistente, deduplicata e senza eseguire il gioco", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var service = new GameLibrary(Path.Combine(p, "metadata"));
            var before = File.ReadAllBytes(report.ExecutablePath); var first = service.Remember(report); service.Remember(report);
            var loaded = new GameLibrary(Path.Combine(p, "metadata")).Load();
            Require(loaded.Games.Count == 1 && loaded.Games[0].Id == first.Games[0].Id && loaded.SelectedId == first.SelectedId, "Importazione duplicata o non persistente.");
            Require(File.ReadAllBytes(report.ExecutablePath).SequenceEqual(before), "Importazione ha toccato il gioco.");
        });
        check("Libreria: nome personalizzato conservato dopo nuova analisi e avvio", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var service = new GameLibrary(Path.Combine(p, "metadata"));
            var id = service.Remember(report).SelectedId!; service.Rename(id, "Underground · Dell"); service.Remember(report); var result = service.MarkPlayed(report.ExecutablePath);
            Require(result.Games.Single().Title == "Underground · Dell" && result.Games.Single().LastPlayedAt is not null, "Nome o avvio perso.");
        });
        check("Libreria: file mancante resta elencato; ricollegamento mantiene identità", () =>
        {
            var p = Folder(); var old = Report(Path.Combine(p, "Old")); var next = Report(Path.Combine(p, "New")); var service = new GameLibrary(Path.Combine(p, "metadata"));
            var id = service.Remember(old).SelectedId!; service.Rename(id, "Il mio gioco"); File.Delete(old.ExecutablePath);
            Require(service.Load().Games.Single().Id == id, "Voce mancante eliminata automaticamente.");
            var state = service.Remember(next, id);
            Require(state.Games.Single().Id == id && state.Games.Single().ExecutablePath == next.ExecutablePath && state.Games.Single().Title == "Il mio gioco", "Ricollegamento perde identità.");
        });
        check("Libreria: ricollegamento a un gioco già importato rifiutato", () =>
        {
            var p = Folder(); var a = Report(Path.Combine(p, "A")); var b = Report(Path.Combine(p, "B")); var service = new GameLibrary(Path.Combine(p, "metadata"));
            var id = service.Remember(a).SelectedId!; service.Remember(b); Refused(() => service.Remember(b, id)); Require(service.Load().Games.Count == 2, "Voci perse.");
        });
        check("Libreria: rimozione cambia solo i metadati e cancella la selezione", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var service = new GameLibrary(Path.Combine(p, "metadata"));
            var id = service.Remember(report).SelectedId!; var backup = Path.Combine(p, "backup.bin"); File.WriteAllText(backup, "preserve");
            var result = service.Remove(id);
            Require(result.Games.Count == 0 && result.SelectedId is null && File.Exists(report.ExecutablePath) && File.ReadAllText(backup) == "preserve", "Rimozione distruttiva.");
        });
        check("Libreria: JSON corrotto non viene sovrascritto da una nuova importazione", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var service = new GameLibrary(Path.Combine(p, "metadata")); service.Remember(report);
            File.WriteAllText(service.FilePath, "broken-json"); Refused(() => service.Remember(report)); Require(File.ReadAllText(service.FilePath) == "broken-json", "Archivio corrotto cancellato.");
        });
        check("Libreria: schema sconosciuto, selezione estranea e percorsi con argomenti rifiutati", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var service = new GameLibrary(Path.Combine(p, "metadata")); var state = service.Remember(report);
            foreach (var bad in new[] { state with { SchemaVersion = 99 }, state with { SelectedId = new string('a', 32) },
                state with { Games = [state.Games[0] with { ExecutablePath = report.ExecutablePath + " --extra" }] } })
            { File.WriteAllText(service.FilePath, JsonSerializer.Serialize(bad)); Refused(() => service.Load()); }
        });
        check("Libreria: scrittori serializzati e limite di dimensione prima della lettura", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var folder = Path.Combine(p, "metadata"); var service = new GameLibrary(folder); service.Remember(report);
            using (var lease = new FileStream(Path.Combine(folder, ".library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) Refused(() => service.Remove(service.Load().SelectedId!));
            Require(service.Load().Games.Count == 1, "Writer concorrente ha modificato la libreria.");
            using (var output = File.OpenWrite(service.FilePath)) output.SetLength(1024 * 1024 + 1); Refused(() => service.Load());
        });
        check("Libreria: nomi vuoti e multilinea rifiutati senza perdere il nome precedente", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var service = new GameLibrary(Path.Combine(p, "metadata")); var state = service.Remember(report); var id = state.SelectedId!;
            foreach (var name in new[] { " ", "first\nsecond", new string('x', 161) }) Refused(() => service.Rename(id, name));
            Require(service.Load().Games.Single().Title == state.Games.Single().Title, "Nome originale perso.");
        });
        check("Libreria: symlink del database non seguito e file esterno preservato", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var service = new GameLibrary(Path.Combine(p, "metadata")); service.Remember(report);
            var outside = Path.Combine(p, "outside.json"); File.WriteAllText(outside, "preserve"); File.Delete(service.FilePath);
            try { File.CreateSymbolicLink(service.FilePath, outside); }
            catch (Exception e) when (OperatingSystem.IsWindows() && e is IOException or UnauthorizedAccessException) { throw new SkipCheckException("Symlink non disponibili."); }
            Refused(() => service.Remember(report)); Require(File.ReadAllText(outside) == "preserve", "File esterno modificato.");
        });
        check("Log: risoluzione dell'ultimo device, eseguibile e compilazione letti senza inventare dati", () =>
        {
            var p = Folder(); var report = Report(Path.Combine(p, "Game")); var path = Path.Combine(Path.GetDirectoryName(report.ExecutablePath)!, "ReShade.log");
            File.WriteAllText(path, $"Initializing into '{report.ExecutablePath}'\n| BackBufferWidth | 1600 |\n| BackBufferHeight | 1200 |\n| BackBufferWidth | 3440 |\n| BackBufferHeight | 1440 |\nSuccessfully compiled '{Path.GetDirectoryName(path)}/GamePanelFX/GamePanelSDR.fx'\n");
            var evidence = RuntimeEvidence.Read(report.ExecutablePath);
            Require(evidence.Width == 3440 && evidence.Height == 1440 && evidence.ShaderCompiled, "Evidenza errata.");
            File.WriteAllText(path, "log without identity"); Refused(() => RuntimeEvidence.Read(report.ExecutablePath));
        });
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Refused(Action action)
    { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { return; } throw new Exception("Dati non validi accettati."); }
}
