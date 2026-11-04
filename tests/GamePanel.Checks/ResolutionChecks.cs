using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class ResolutionChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> makeExe, bool verifyOfficial, string? archive)
    {
        Fixture Make() => new(root, makeExe);
        check("Risoluzione: Unity e Unreal usano argomenti separati e profili per EXE", () =>
        {
            using var f = Make(); var options = new GameResolutionOptions(ResolutionMethod.Unity, 3440, 1440, "monitor-a");
            var plan = f.Service.Prepare(f.Report, options); Require(plan.Changes.Count == 0 && f.Service.Load(f.Exe) is null, "Anteprima scrive o cambia gioco.");
            f.Service.Execute(plan);
            Require(f.Service.LaunchArguments(f.Report).SequenceEqual(new[] { "-screen-width", "3440", "-screen-height", "1440", "-screen-fullscreen", "1" }), "Argomenti Unity errati.");
            Require(f.Service.Load(f.OtherExe) is null && f.Service.LaunchArguments(new GameInspector().Inspect(f.OtherExe)).Count == 0, "Profilo applicato a un altro gioco.");
            Require(GameResolutionProfiles.Arguments(new(ResolutionMethod.Unreal, 2560, 1440)).SequenceEqual(new[] { "-ResX=2560", "-ResY=1440", "-Fullscreen" }), "Argomenti Unreal errati.");
            f.Service.Execute(f.Service.PrepareRemove(f.Report)); Require(f.Service.LaunchArguments(f.Report).Count == 0, "Parametri rimasti dopo rimozione.");
        });
        check("Risoluzione INI: preserva commenti, BOM, CRLF e altre sezioni; ripristino esatto", () =>
        {
            using var f = Make(); var original = new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("; preserve\r\n[Display]\r\nWidth = 800 ; width comment\r\nHeight=600\r\nOther=keep\r\n[Other]\r\nWidth=42\r\nHeight=43\r\n")).ToArray();
            File.WriteAllBytes(f.Ini, original); var options = new GameResolutionOptions(ResolutionMethod.Ini, 3440, 1440, ConfigPath: f.Ini, Section: "Display");
            var plan = f.Service.Prepare(f.Report, options); Require(original.SequenceEqual(File.ReadAllBytes(f.Ini)), "Anteprima cambia il file.");
            f.Service.Execute(plan); var after = File.ReadAllBytes(f.Ini); var text = Encoding.UTF8.GetString(after);
            Require(after.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) && text.Contains("Width = 3440 ; width comment\r\n") && text.Contains("Height=1440\r\n")
                && text.Contains("[Other]\r\nWidth=42\r\nHeight=43") && text.Contains("Other=keep"), "Formato o chiavi estranee alterati.");
            f.Service.Execute(f.Service.PrepareRemove(f.Report)); Require(original.SequenceEqual(File.ReadAllBytes(f.Ini)), "Ripristino non esatto.");
        });
        check("Risoluzione: file modificato dopo anteprima e EXE sostituito non vengono sovrascritti", () =>
        {
            using var f = Make(); File.WriteAllText(f.Ini, "Width=800\nHeight=600\n"); var options = new GameResolutionOptions(ResolutionMethod.Ini, 1920, 1080, ConfigPath: f.Ini);
            var plan = f.Service.Prepare(f.Report, options); File.AppendAllText(f.Ini, "external=keep\n"); Refused(() => f.Service.Execute(plan));
            Require(File.ReadAllText(f.Ini).Contains("external=keep"), "Modifica esterna persa.");
            plan = f.Service.Prepare(f.Report, options); File.AppendAllText(f.Exe, "changed"); Refused(() => f.Service.Execute(plan));
        });
        check("Risoluzione: annullamento con journal Pending blocca avvio e recupera prima/dopo", () =>
        {
            using var f = Make(); File.WriteAllText(f.Ini, "Width=800\nHeight=600\n"); var before = File.ReadAllBytes(f.Ini);
            var options = new GameResolutionOptions(ResolutionMethod.Ini, 2560, 1440, ConfigPath: f.Ini); var plan = f.Service.Prepare(f.Report, options);
            Canceled(() => f.Service.Execute(plan, progress: new Inline(_ => throw new OperationCanceledException())));
            Require(f.Service.HasPending(f.Exe), "Operazione sospesa non registrata."); Refused(() => f.Service.LaunchArguments(f.Report));
            f.Service.Recover(f.Report, true); Require(before.SequenceEqual(File.ReadAllBytes(f.Ini)) && f.Service.Load(f.Exe) is null && !f.Service.HasPending(f.Exe), "Rollback incompleto.");
            plan = f.Service.Prepare(f.Report, options); Canceled(() => f.Service.Execute(plan, progress: new Inline(_ => throw new OperationCanceledException())));
            f.Service.Recover(f.Report, false); Require(f.Service.Load(f.Exe)?.Options.Width == 2560 && !f.Service.HasPending(f.Exe), "Ripresa incompleta.");
        });
        check("Risoluzione: rimozione conserva configurazioni cambiate dal gioco", () =>
        {
            using var f = Make(); File.WriteAllText(f.Ini, "Width=800\nHeight=600\n");
            f.Service.Execute(f.Service.Prepare(f.Report, new(ResolutionMethod.Ini, 1920, 1080, ConfigPath: f.Ini)));
            File.AppendAllText(f.Ini, "game-setting=keep\n"); var plan = f.Service.PrepareRemove(f.Report);
            Require(plan.PreservedFiles.SequenceEqual(new[] { f.Ini }) && plan.Changes.Count == 0, "File modificato non segnalato.");
            f.Service.Execute(plan); Require(File.ReadAllText(f.Ini).Contains("game-setting=keep") && f.Service.Load(f.Exe) is null, "Rimozione perde i dati.");
        });
        check("Risoluzione: nuova anteprima conserva modifiche esterne e aggiorna il punto di ripristino", () =>
        {
            using var f = Make(); File.WriteAllText(f.Ini, "Width=800\nHeight=600\n");
            var options = new GameResolutionOptions(ResolutionMethod.Ini, 1920, 1080, ConfigPath: f.Ini);
            f.Service.Execute(f.Service.Prepare(f.Report, options)); File.AppendAllText(f.Ini, "user-setting=keep\n"); var before = File.ReadAllBytes(f.Ini);
            f.Service.Execute(f.Service.Prepare(f.Report, options with { Width = 2560, Height = 1440 }));
            Require(File.ReadAllText(f.Ini).Contains("user-setting=keep"), "Modifica esterna persa nell'aggiornamento.");
            f.Service.Execute(f.Service.PrepareRemove(f.Report)); Require(before.SequenceEqual(File.ReadAllBytes(f.Ini)), "Punto di ripristino non conserva le modifiche esterne.");
        });
        check("Risoluzione: INI ambigui, testo binario e parametri non validi sono rifiutati", () =>
        {
            var options = new GameResolutionOptions(ResolutionMethod.Ini, 1920, 1080);
            Refused(() => GameResolutionProfiles.PatchIni(Encoding.UTF8.GetBytes("Width=1\nWidth=2\nHeight=3\n"), options));
            Refused(() => GameResolutionProfiles.PatchIni(Encoding.UTF8.GetBytes("Width=1\n"), options));
            Refused(() => GameResolutionProfiles.PatchIni(new byte[] { 255, 0 }, options));
            Refused(() => GameResolutionProfiles.Arguments(options with { Width = 0 }));
            Refused(() => GameResolutionProfiles.Arguments(options with { HeightKey = "Width" }));
            Refused(() => GameResolutionProfiles.Arguments(options with { WidthKey = "Width\nscript" }));
        });
        check("Risoluzione: file e percorsi fuori ambito, journal corrotto e gioco aperto sono bloccati", () =>
        {
            using var f = Make(); File.WriteAllText(f.Ini, "Width=800\nHeight=600\n");
            Refused(() => f.Service.Prepare(f.Report, new(ResolutionMethod.Ini, 1920, 1080, ConfigPath: f.Exe)));
            Refused(() => f.Service.Prepare(f.Report, new(ResolutionMethod.Ini, 1920, 1080, ConfigPath: Path.Combine(f.Store, "other.ini"))));
            var running = new GameResolutionProfiles(f.Store, f.Common, _ => true, f.Report.Sha256);
            Refused(() => running.Prepare(f.Report, new(ResolutionMethod.Unity, 1920, 1080)));
            f.Service.Execute(f.Service.Prepare(f.Report, new(ResolutionMethod.Unity, 1920, 1080)));
            var journal = Directory.GetFiles(f.Store, "operation.json", SearchOption.AllDirectories).Single(); File.WriteAllText(journal, "{}");
            Refused(() => f.Service.HasPending(f.Exe));
        });
        check("Risoluzione UG1: impacchettamento coerente con RES.cfg2 del fix verificato", () =>
        {
            var packed = unchecked((uint)GameResolutionProfiles.Pack(3440, 1440));
            Require((packed & 0x80000000) != 0 && ((packed & 0x7fff0000) >> 16) == 3440 && (packed & 0xffff) == 1440, "Dimensioni UG1 non recuperabili dal formato upstream.");
        });
        if (verifyOfficial || archive is not null) check("Risoluzione UG1: archivio ufficiale, installazione statica, aggiornamento e ripristino senza eseguire DLL/ASI", () =>
        {
            using var f = Make(); var package = archive is not null ? Underground1DisplayFix.Read(archive)
                : Underground1DisplayFix.DownloadAsync(Path.Combine(f.Root, "Download")).GetAwaiter().GetResult();
            var saved = Path.Combine(f.Common, "NFS Underground", "RES.cfg2"); Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.WriteAllText(saved, "RES: 0\n");
            var options = new GameResolutionOptions(ResolutionMethod.Underground1, 3440, 1440);
            var plan = f.Service.Prepare(f.Report, options, package); Require(plan.Changes.Count == 6 && !File.Exists(Path.Combine(f.Game, "dinput8.dll")), "Anteprima installa il componente.");
            f.Service.Execute(plan); Require(File.ReadAllText(saved).Contains(GameResolutionProfiles.Pack(3440, 1440).ToString(System.Globalization.CultureInfo.InvariantCulture)), "RES.cfg2 non applicato.");
            var ini = Path.Combine(f.Game, "scripts", "NFSUnderground.WidescreenFix.ini"); File.AppendAllText(ini, "; user setting preserved\n"); var userIni = File.ReadAllBytes(ini);
            var updated = f.Service.Prepare(f.Report, options with { Width = 1920, Height = 1080 }, package); f.Service.Execute(updated);
            Require(userIni.SequenceEqual(File.ReadAllBytes(ini)), "INI personalizzato sovrascritto nell'aggiornamento.");
            f.Service.Execute(f.Service.PrepareRemove(f.Report)); Require(File.ReadAllText(saved) == "RES: 0\n" && !File.Exists(Path.Combine(f.Game, "dinput8.dll")), "Componenti o risoluzione non ripristinati.");
            Require(userIni.SequenceEqual(File.ReadAllBytes(ini)), "INI personalizzato perso nella rimozione.");
            plan = f.Service.Prepare(f.Report, options, package); Canceled(() => f.Service.Execute(plan, progress: new Inline(p => { if (p.Completed == 1) throw new OperationCanceledException(); })));
            f.Service.Recover(f.Report, true); Require(!File.Exists(Path.Combine(f.Game, "dinput8.dll")) && File.ReadAllText(saved) == "RES: 0\n", "Rollback installazione non esatto.");
        });
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game => Path.Combine(Root, "Game with spaces");
        public string Exe => Path.Combine(Game, "test.exe");
        public string OtherExe => Path.Combine(Game, "other.exe");
        public string Ini => Path.Combine(Game, "video.ini");
        public string Store => Path.Combine(Root, "Settings");
        public string Common => Path.Combine(Root, "ProgramData");
        public GameReport Report => new GameInspector().Inspect(Exe);
        public GameResolutionProfiles Service { get; }
        public Fixture(string root, Func<string, bool, bool, string, string> makeExe)
        {
            Root = Path.Combine(root, "resolution-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Game); Directory.CreateDirectory(Common);
            foreach (var folder in new[] { "GLOBAL", "TRACKS", "CARS", "MOVIES" }) Directory.CreateDirectory(Path.Combine(Game, folder));
            makeExe(Exe, false, false, "d3d9.dll"); File.Copy(Exe, OtherExe);
            Service = new(Store, Common, _ => false, Report.Sha256);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Inline(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress value) => action(value); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refused(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return; } throw new Exception("Operazione non valida accettata."); }
    private static void Canceled(Action action) { try { action(); } catch (OperationCanceledException) { return; } throw new Exception("Annullamento ignorato."); }
}
