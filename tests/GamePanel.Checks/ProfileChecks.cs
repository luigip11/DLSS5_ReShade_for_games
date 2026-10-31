using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using GamePanel.Core;

internal static class ProfileChecks
{
    public static void Register(Action<string, Action> check, string testRoot, Func<string, bool, bool, string, string> makeExe, bool officialDownload)
    {
        check("Profilo: installazione limitata ai quattro file approvati, EXE invariato", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var original = Hash(f.Exe);
            var plan = f.Plan(); Require(!SdrProfile.Targets.Any(p => File.Exists(f.File(p))), "Anteprima con scritture.");
            f.Service.Execute(plan);
            Require(Hash(f.Exe) == original && f.Service.Status(f.Exe).State?.Installed == true, "EXE o stato errato.");
            Require(Directory.GetFiles(f.Game, "*", SearchOption.AllDirectories).Length == 5, "Scritture inattese.");
            var service = new ReShadeProfileService(f.Store, _ => false);
            service.Execute(service.PrepareRemove(f.Report()));
            Require(Hash(f.Exe) == original && !SdrProfile.Targets.Any(p => File.Exists(f.File(p))), "Stato originale non recuperato.");
        });
        check("Profilo: DLL, INI o cartella preesistenti non sovrascritti", () =>
        {
            foreach (var name in new[] { "d3d9.dll", "ReShade.ini", "GamePanelPreset.ini", "GamePanelFX" })
            {
                using var f = new Fixture(testRoot, makeExe);
                if (name == "GamePanelFX") Directory.CreateDirectory(f.File(name)); else File.WriteAllText(f.File(name), "altro strumento");
                Refused(() => f.Plan());
                Require(name == "GamePanelFX" ? Directory.Exists(f.File(name)) : File.ReadAllText(f.File(name)) == "altro strumento", "Dato precedente alterato.");
            }
        });
        check("Profilo: wrapper esistente e nuovo anti-cheat bloccano l'applicazione", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            File.WriteAllText(f.File("dxgi.dll"), "wrapper"); Refused(() => f.Plan()); File.Delete(f.File("dxgi.dll"));
            var plan = f.Plan(); File.WriteAllText(f.File("EasyAntiCheat_EOS.dll"), "fixture");
            Refused(() => f.Service.Execute(plan)); Require(!File.Exists(f.File("d3d9.dll")), "Indicatore recente ignorato.");
        });
        check("Profilo: x64, nomi diversi e Direct3D 11 non ammessi", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            makeExe(f.Exe, true, false, "d3d9.dll"); Refused(() => f.Plan());
            makeExe(f.Exe, false, false, "d3d11.dll"); Refused(() => f.Plan());
            Require(ReShadeProfileService.Eligibility(f.Report() with { ExecutablePath = f.File("other.exe") }) is not null, "Nome diverso ammesso.");
        });
        check("Profilo: EXE o destinazione cambiati dopo anteprima bloccano le scritture", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var plan = f.Plan();
            File.WriteAllText(f.File("ReShade.ini"), "nuovo file"); Refused(() => f.Service.Execute(plan));
            Require(!File.Exists(f.File("d3d9.dll")), "Scrittura parziale prima del conflitto.");
            File.Delete(f.File("ReShade.ini")); plan = f.Plan(); File.AppendAllText(f.Exe, "update"); Refused(() => f.Service.Execute(plan));
        });
        check("Profilo: parametri aggiornano soltanto il preset; Originale disattiva la tecnica", () =>
        {
            using var f = new Fixture(testRoot, makeExe); f.Service.Execute(f.Plan());
            var dllHash = Hash(f.File("d3d9.dll")); var configHash = Hash(f.File("ReShade.ini"));
            var settings = new SdrSettings(true, 62, 40, 25);
            f.Service.Execute(f.Plan(settings));
            var preset = File.ReadAllText(f.File(SdrProfile.PresetPath));
            Require(preset.Contains("Sharpness=0.6200") && preset.Contains("Saturation=0.9000") && preset.Contains("Strength=0.2500"), "Parametri scollegati.");
            Require(Hash(f.File("d3d9.dll")) == dllHash && Hash(f.File("ReShade.ini")) == configHash, "Altri componenti riscritti.");
            f.Service.Execute(f.Plan(SdrSettings.Original));
            Require(File.ReadAllText(f.File(SdrProfile.PresetPath)).StartsWith("Techniques=\r\n"), "Originale lascia l'effetto attivo.");
        });
        check("Profilo: configurazione riscritta dal runtime conservata alla rimozione", () =>
        {
            using var f = new Fixture(testRoot, makeExe); f.Service.Execute(f.Plan());
            File.AppendAllText(f.File("ReShade.ini"), "\n[Runtime]\nchanged=1");
            f.Service.Execute(f.Plan(SdrSettings.Original));
            var configHash = Hash(f.File("ReShade.ini")); var remove = f.Service.PrepareRemove(f.Report());
            Require(remove.PreservedFiles.SequenceEqual(["ReShade.ini"]), "File mutabile non segnalato.");
            f.Service.Execute(remove);
            Require(Hash(f.File("ReShade.ini")) == configHash && !File.Exists(f.File("d3d9.dll")), "Rimozione distruttiva o loader ancora attivo.");
        });
        check("Profilo: preset esterno preservato, DLL esterna blocca la rimozione", () =>
        {
            using var f = new Fixture(testRoot, makeExe); f.Service.Execute(f.Plan());
            File.WriteAllText(f.File(SdrProfile.PresetPath), "preset esterno"); Refused(() => f.Plan(SdrSettings.Original));
            File.WriteAllText(f.File("d3d9.dll"), "loader esterno"); Refused(() => f.Service.PrepareRemove(f.Report()));
            Require(File.ReadAllText(f.File(SdrProfile.PresetPath)) == "preset esterno", "Preset esterno perso.");
        });
        check("Profilo: configurazione reindirizzata impedisce aggiornamenti apparenti del preset", () =>
        {
            using var f = new Fixture(testRoot, makeExe); f.Service.Execute(f.Plan());
            var path = f.File("ReShade.ini"); var config = File.ReadAllText(path); var preset = Hash(f.File(SdrProfile.PresetPath));
            File.WriteAllText(path, config.Replace("PresetPath=.\\GamePanelPreset.ini", "PresetPath=.\\OtherPreset.ini", StringComparison.Ordinal));
            Refused(() => f.Plan(SdrSettings.Original)); Require(Hash(f.File(SdrProfile.PresetPath)) == preset, "Preset aggiornato con percorso reindirizzato.");
        });
        check("Profilo: interruzione a ogni passo ripresa e annullata da una nuova istanza", () =>
        {
            foreach (var rollback in new[] { false, true })
            foreach (var stopAfter in Enumerable.Range(1, 4))
            {
                using var f = new Fixture(testRoot, makeExe); using var source = new CancellationTokenSource();
                var plan = f.Plan();
                Cancelled(() => f.Service.Execute(plan, source.Token, new InlineProgress(p => { if (p.Completed == stopAfter) source.Cancel(); })));
                Require(f.Service.Status(f.Exe).Pending, "Journal non persistente.");
                var fresh = new ReShadeProfileService(f.Store, _ => false);
                fresh.Recover(f.Exe, rollback);
                Require(!fresh.Status(f.Exe).Pending, "Recupero incompleto.");
                Require(SdrProfile.Targets.All(p => File.Exists(f.File(p)) == !rollback), "Byte non recuperati.");
                Require(!Directory.GetFiles(f.Game, ".gamepanel-*.tmp", SearchOption.AllDirectories).Any(), "Temporanee residue.");
            }
        });
        check("Profilo: aggiornamento interrotto annullato con byte e impostazioni precedenti", () =>
        {
            using var f = new Fixture(testRoot, makeExe); f.Service.Execute(f.Plan()); var original = Hash(f.File(SdrProfile.PresetPath));
            using var source = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Plan(SdrSettings.Original), source.Token, new InlineProgress(_ => source.Cancel())));
            f.Service.Recover(f.Exe, true);
            Require(Hash(f.File(SdrProfile.PresetPath)) == original && f.Service.Status(f.Exe).State?.Settings == SdrSettings.Balanced, "Stato precedente perso.");
        });
        check("Profilo: rimozione interrotta annullata recupera tutti i file", () =>
        {
            using var f = new Fixture(testRoot, makeExe); f.Service.Execute(f.Plan());
            var hashes = SdrProfile.Targets.ToDictionary(p => p, p => Hash(f.File(p)));
            using var source = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Service.PrepareRemove(f.Report()), source.Token, new InlineProgress(_ => source.Cancel())));
            f.Service.Recover(f.Exe, true);
            Require(hashes.All(p => Hash(f.File(p.Key)) == p.Value), "Rimozione non reversibile.");
        });
        check("Profilo: copie corrotte bloccano il recupero prima delle scritture", () =>
        {
            using var f = new Fixture(testRoot, makeExe); using var source = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Plan(), source.Token, new InlineProgress(_ => source.Cancel())));
            var payload = Directory.GetFiles(f.Store, "*-after.bin", SearchOption.AllDirectories).First(); File.AppendAllText(payload, "corrupt");
            Refused(() => f.Service.Recover(f.Exe, false));
            Require(!File.Exists(f.File("ReShade.ini")), "Copia corrotta non bloccata prima delle scritture.");
        });
        check("Profilo: conflitto durante recupero preserva il file comparso", () =>
        {
            using var f = new Fixture(testRoot, makeExe); using var source = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Plan(), source.Token, new InlineProgress(_ => source.Cancel())));
            File.WriteAllText(f.File("ReShade.ini"), "esterno");
            Refused(() => f.Service.Recover(f.Exe, true));
            Require(File.ReadAllText(f.File("ReShade.ini")) == "esterno" && File.Exists(f.File("d3d9.dll")), "Recupero distruttivo.");
        });
        check("Profilo: journal con traversal o metadati incoerenti rifiutato", () =>
        {
            using var f = new Fixture(testRoot, makeExe); using var source = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Plan(), source.Token, new InlineProgress(_ => source.Cancel())));
            var path = Directory.GetFiles(f.Store, "operation.json", SearchOption.AllDirectories).Single();
            var node = JsonNode.Parse(File.ReadAllText(path))!; node["Changes"]![0]!["Path"] = "../foreign.dll"; File.WriteAllText(path, node.ToJsonString());
            Refused(() => f.Service.Recover(f.Exe, false));
        });
        check("Profilo: gioco aperto o controllo processi fallito impediscono scritture", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            Refused(() => new ReShadeProfileService(f.Store, _ => true).PrepareApply(f.Report(), f.Package, SdrSettings.Balanced));
            Refused(() => new ReShadeProfileService(f.Store, _ => throw new IOException("process check")).PrepareApply(f.Report(), f.Package, SdrSettings.Balanced));
            Require(!File.Exists(f.File("d3d9.dll")), "Scrittura con gioco aperto.");
        });
        check("Profilo: gioco avviato a metà sospende; recupero dopo chiusura", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var running = false;
            var service = new ReShadeProfileService(f.Store, _ => running);
            var plan = service.PrepareApply(f.Report(), f.Package, SdrSettings.Balanced);
            Refused(() => service.Execute(plan, progress: new InlineProgress(_ => running = true)));
            Require(service.Status(f.Exe).Pending, "Operazione incompleta non segnalata."); running = false; service.Recover(f.Exe, true);
        });
        check("Profilo: annullamento prima dell'applicazione non installa file", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var plan = f.Plan(); using var source = new CancellationTokenSource(); source.Cancel();
            Cancelled(() => f.Service.Execute(plan, source.Token)); Require(!File.Exists(f.File("d3d9.dll")), "Annullamento ignorato.");
        });
        check("Profilo: archivio interno al gioco rifiutato", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            Refused(() => new ReShadeProfileService(f.File("store"), _ => false).PrepareApply(f.Report(), f.Package, SdrSettings.Balanced));
        });
        check("Profilo: EXE cambiato o altro EXE nella stessa cartella non eredita il profilo", () =>
        {
            using var f = new Fixture(testRoot, makeExe); f.Service.Execute(f.Plan());
            Refused(() => f.Service.Status(f.File("different.exe"))); File.AppendAllText(f.Exe, "update");
            Refused(() => f.Service.PrepareRemove(f.Report())); Require(File.Exists(f.File("d3d9.dll")), "Profilo rimosso su altra identità.");
        });
        check("Profilo: symlink comparso dopo anteprima non viene seguito", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var plan = f.Plan(); var outside = Path.Combine(f.Root, "outside.dll"); File.WriteAllText(outside, "preserve");
            try { File.CreateSymbolicLink(f.File("d3d9.dll"), outside); }
            catch (Exception e) when (OperatingSystem.IsWindows() && e is IOException or UnauthorizedAccessException) { throw new SkipCheckException("Symlink non disponibili."); }
            Refused(() => f.Service.Execute(plan)); Require(File.ReadAllText(outside) == "preserve", "Symlink seguito.");
        });
        check("Componente: redirect e dimensione dichiarata errata non producono DLL verificata", () =>
        {
            foreach (var response in new[] { HttpStatusCode.Redirect, HttpStatusCode.OK })
            {
                using var f = new Fixture(testRoot, makeExe);
                var source = new ReShadePackageSource(Path.Combine(f.Root, "cache"), () => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(response) { Content = new ByteArrayContent([1, 2, 3]) })));
                Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
                Require(!Directory.GetFiles(f.Root, "*.download", SearchOption.AllDirectories).Any(), "Download incompleto rimasto.");
            }
        });
        check("Componente: corpo di dimensione corretta ma SHA-256 errato rifiutato", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            var source = new ReShadePackageSource(Path.Combine(f.Root, "cache"), () => new HttpClient(new FakeHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[ReShadeCatalog.SetupBytes]) })));
            Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
            Require(!Directory.GetFiles(f.Root, "*.download", SearchOption.AllDirectories).Any(), "File alterato rimasto in staging.");
        });
        check("Componente: limite reale del corpo applicato anche con header ingannevole", () =>
        {
            using var f = new Fixture(testRoot, makeExe);
            var source = new ReShadePackageSource(Path.Combine(f.Root, "cache"), () => new HttpClient(new FakeHandler(_ =>
            {
                var content = new ByteArrayContent(new byte[ReShadeCatalog.SetupBytes + 1]); content.Headers.ContentLength = ReShadeCatalog.SetupBytes;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            })));
            Refused(() => source.PrepareAsync().GetAwaiter().GetResult());
        });
        check("Componente: annullamento impedisce download e lascia il gioco invariato", () =>
        {
            using var f = new Fixture(testRoot, makeExe); using var source = new CancellationTokenSource(); source.Cancel();
            var component = new ReShadePackageSource(Path.Combine(f.Root, "cache"), () => new HttpClient(new FakeHandler(_ => throw new Exception("Richiesta non attesa"))));
            Cancelled(() => component.PrepareAsync(source.Token).GetAwaiter().GetResult()); Require(!File.Exists(f.File("d3d9.dll")), "File installato dopo annullamento.");
        });
        check("Componente: cache alterata non viene accettata", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var cache = Path.Combine(f.Root, "cache"); Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "ReShade32-6.8.0.dll"), "fake cached DLL");
            var source = new ReShadePackageSource(cache, () => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))));
            Refused(() => source.PrepareAsync().GetAwaiter().GetResult()); Require(!File.Exists(f.File("d3d9.dll")), "Cache alterata installata.");
        });
        check("Componente: estrazione selettiva, duplicati e archivi senza DLL rifiutati", () =>
        {
            byte[] Archive(params string[] names)
            {
                using var output = new MemoryStream();
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                    foreach (var name in names) { using var stream = zip.CreateEntry(name).Open(); stream.Write([1, 2, 3]); }
                return output.ToArray();
            }
            Require(ReShadePackageSource.ExtractX86(Archive("../escape.dll", "ReShade32.dll")).SequenceEqual(new byte[] { 1, 2, 3 }), "Estrazione selettiva errata.");
            Refused(() => ReShadePackageSource.ExtractX86(Archive("ReShade32.dll", "ReShade32.dll")));
            Refused(() => ReShadePackageSource.ExtractX86(Archive("../ReShade32.dll")));
        });
        if (officialDownload) check("Componente ufficiale: download reale, hash fissati, estrazione e cache verificati senza esecuzione", () =>
        {
            using var f = new Fixture(testRoot, makeExe); var source = new ReShadePackageSource(Path.Combine(f.Root, "official-cache"));
            var package = source.PrepareAsync().GetAwaiter().GetResult();
            Require(Hash(package.Path) == ReShadeCatalog.DllSha256, "Componente ufficiale diverso dal catalogo.");
            Require(source.PrepareAsync().GetAwaiter().GetResult().Sha256 == package.Sha256, "Cache non riutilizzabile.");
            f.Service.Execute(f.Service.PrepareApply(f.Report(), package, SdrSettings.Balanced));
            Require(Hash(f.File("d3d9.dll")) == ReShadeCatalog.DllSha256, "Installazione non identica al componente verificato.");
            f.Service.Execute(f.Service.PrepareRemove(f.Report()));
        });
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refused(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { return; }
        throw new Exception("Operazione non valida accettata.");
    }
    private static void Cancelled(Action action) { try { action(); } catch (OperationCanceledException) { return; } throw new Exception("Annullamento ignorato."); }
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress p) => action(p); }
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { var response = reply(request); response.RequestMessage = request; return Task.FromResult(response); } }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game { get; }
        public string Store => Path.Combine(Root, "profiles");
        public string Exe => Path.Combine(Game, "speed2.exe");
        public ReShadeProfileService Service { get; }
        public VerifiedReShade Package { get; }
        public Fixture(string testRoot, Func<string, bool, bool, string, string> makeExe)
        {
            Root = Path.Combine(testRoot, "profile-" + Guid.NewGuid().ToString("N")); Game = Path.Combine(Root, "Test Game"); Directory.CreateDirectory(Game);
            makeExe(Exe, false, false, "d3d9.dll"); var source = Path.Combine(Root, "fixture.dll"); System.IO.File.WriteAllText(source, "Test payload — never loaded or executed.");
            Package = new(source, Hash(source)); Service = new(Store, _ => false);
        }
        public string File(string relative) => Path.Combine(Game, relative.Replace('/', Path.DirectorySeparatorChar));
        public GameReport Report() => new GameInspector().Inspect(Exe);
        public ProfilePlan Plan(SdrSettings? settings = null) => Service.PrepareApply(Report(), Package, settings ?? SdrSettings.Balanced);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
