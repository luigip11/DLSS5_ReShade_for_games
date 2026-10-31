using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamePanel.Core;

internal static class WideChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> makeExe, bool official, string? localPackage)
    {
        check("Migrazione alpha.5: impostazioni legacy leggibili con nuovi effetti neutri", () =>
        {
            var value = JsonSerializer.Deserialize<SdrSettings>("{\"Enabled\":true,\"Sharpness\":35,\"Color\":58,\"Intensity\":70}")!;
            Require(value == SdrSettings.Balanced && value.Contrast == 0 && value.Bloom == 0 && value.Vignette == 0, "Impostazioni legacy cambiate.");
        });
        check("Migrazione alpha.5: shader e preset precedenti aggiornati con rollback esatto", () =>
        {
            using var f = new Fixture(root, makeExe); var service = new ReShadeProfileService(f.Store, _ => false);
            var dll = Path.Combine(f.Root, "ReShade-fixture.dll"); File.WriteAllText(dll, "not loaded"); var package = new VerifiedReShade(dll, Hash(dll));
            service.Execute(service.PrepareApply(f.Report(), package, SdrSettings.Balanced));
            File.WriteAllText(f.Path(SdrProfile.ShaderPath), "legacy owned shader");
            File.WriteAllText(f.Path(SdrProfile.PresetPath), "Techniques=GamePanelSDR@GamePanelSDR.fx\r\n[GamePanelSDR.fx]\r\nSharpness=0.3500\r\nSaturation=1.0800\r\nStrength=0.7000\r\n");
            var statePath = Directory.GetFiles(f.Store, "state.json", SearchOption.AllDirectories).Single();
            var state = JsonNode.Parse(File.ReadAllText(statePath))!;
            foreach (var name in new[] { "Contrast", "Bloom", "Vignette" }) state["Settings"]!.AsObject().Remove(name);
            foreach (var entry in state["Files"]!.AsArray())
                if (entry!["Path"]!.GetValue<string>() is SdrProfile.ShaderPath or SdrProfile.PresetPath)
                { var path = f.Path(entry["Path"]!.GetValue<string>()); entry["Sha256"] = Hash(path); entry["Bytes"] = new FileInfo(path).Length; }
            File.WriteAllText(statePath, state.ToJsonString());
            var operationPath = Directory.GetFiles(f.Store, "operation.json", SearchOption.AllDirectories).Single();
            var operation = JsonNode.Parse(File.ReadAllText(operationPath))!; operation["Next"] = state.DeepClone(); File.WriteAllText(operationPath, operation.ToJsonString());
            foreach (var change in operation["Changes"]!.AsArray())
            {
                var file = state["Files"]!.AsArray().Single(f => f!["Path"]!.GetValue<string>() == change!["Path"]!.GetValue<string>());
                change!["AfterSha256"] = file!["Sha256"]!.GetValue<string>();
            }
            File.WriteAllText(operationPath, operation.ToJsonString());
            var oldShader = Hash(f.Path(SdrProfile.ShaderPath)); var oldPreset = Hash(f.Path(SdrProfile.PresetPath));
            var plan = service.PrepareApply(f.Report(), package, SdrSettings.Cinema);
            Require(plan.Changes.Count == 2 && plan.Changes.Any(c => c.Path == SdrProfile.ShaderPath), "Migrazione non aggiorna lo shader.");
            using var cancellation = new CancellationTokenSource();
            try { service.Execute(plan, cancellation.Token, new InlineProgress(_ => cancellation.Cancel())); throw new Exception("Annullamento ignorato."); }
            catch (OperationCanceledException) { }
            service.Recover(f.Exe, true);
            Require(Hash(f.Path(SdrProfile.ShaderPath)) == oldShader && Hash(f.Path(SdrProfile.PresetPath)) == oldPreset, "Rollback della migrazione non esatto.");
            service.Execute(service.PrepareApply(f.Report(), package, SdrSettings.Cinema));
            Require(File.ReadAllText(f.Path(SdrProfile.PresetPath)).Contains("Contrast=0.3500"), "Cinema non collegato al preset.");
        });
        check("Widescreen: installazione/rimozione limitata a quattro file, EXE invariato", () =>
        {
            using var f = new Fixture(root, makeExe); var before = Hash(f.Exe); var plan = f.Service.PrepareWidescreen(f.Report(), f.Package);
            Require(!WidescreenCatalog.Targets.Any(p => File.Exists(f.Path(p))), "Anteprima ha scritto file.");
            f.Service.Execute(plan); Require(f.Service.Status(f.Exe).State?.Installed == true, "Stato fix non persistente.");
            f.Service.Execute(f.Service.PrepareRemove(f.Report()));
            Require(Hash(f.Exe) == before && !WidescreenCatalog.Targets.Any(p => File.Exists(f.Path(p))), "Rimozione o EXE errati.");
        });
        check("Widescreen: loader e scripts preesistenti bloccano senza sovrascrivere", () =>
        {
            foreach (var name in new[] { "dinput8.dll", "version.dll", "winmm.dll", "scripts/Other.asi" })
            {
                using var f = new Fixture(root, makeExe); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.Path(name))!); File.WriteAllText(f.Path(name), "preserve");
                Refused(() => f.Service.PrepareWidescreen(f.Report(), f.Package)); Require(File.ReadAllText(f.Path(name)) == "preserve", "Mod esistente alterata.");
            }
        });
        check("Widescreen: interruzione a ogni passo ripresa o annullata con nuova istanza", () =>
        {
            foreach (var rollback in new[] { false, true }) foreach (var after in Enumerable.Range(1, 4))
            {
                using var f = new Fixture(root, makeExe); using var cancellation = new CancellationTokenSource();
                try { f.Service.Execute(f.Service.PrepareWidescreen(f.Report(), f.Package), cancellation.Token, new InlineProgress(p => { if (p.Completed == after) cancellation.Cancel(); })); throw new Exception("Annullamento ignorato."); }
                catch (OperationCanceledException) { }
                Require(f.Service.Status(f.Exe).Pending, "Journal non persistente.");
                ReShadeProfileService.ForWidescreen(f.Store, _ => false).Recover(f.Exe, rollback);
                Require(WidescreenCatalog.Targets.All(p => File.Exists(f.Path(p)) == !rollback), "Recupero incompleto.");
            }
        });
        check("Widescreen e ReShade: journal separati, lock condiviso e operazione pendente blocca il peer", () =>
        {
            using var f = new Fixture(root, makeExe); var shaderService = new ReShadeProfileService(f.Store, _ => false);
            var dll = System.IO.Path.Combine(f.Root, "fixture.dll"); File.WriteAllText(dll, "not loaded"); var package = new VerifiedReShade(dll, Hash(dll));
            var shaderPlan = shaderService.PrepareApply(f.Report(), package, SdrSettings.Balanced);
            using var cancellation = new CancellationTokenSource();
            try { f.Service.Execute(f.Service.PrepareWidescreen(f.Report(), f.Package), cancellation.Token, new InlineProgress(_ => cancellation.Cancel())); }
            catch (OperationCanceledException) { }
            Refused(() => shaderService.Execute(shaderPlan)); Refused(() => shaderService.PrepareApply(f.Report(), package, SdrSettings.Balanced));
            f.Service.Recover(f.Exe, true);
            f.Service.Execute(f.Service.PrepareWidescreen(f.Report(), f.Package));
            shaderService.Execute(shaderService.PrepareApply(f.Report(), package, SdrSettings.Balanced));
            f.Service.Execute(f.Service.PrepareRemove(f.Report()));
            Require(shaderService.Status(f.Exe).State?.Installed == true && File.Exists(f.Path(SdrProfile.LoaderPath)), "Rimozione fix ha toccato ReShade.");
        });
        check("Widescreen: configurazione modificata conservata, plugin modificato blocca rimozione", () =>
        {
            using var f = new Fixture(root, makeExe); f.Service.Execute(f.Service.PrepareWidescreen(f.Report(), f.Package));
            File.AppendAllText(f.Path(WidescreenCatalog.Ini), "\n; user change"); var hash = Hash(f.Path(WidescreenCatalog.Ini));
            var plan = f.Service.PrepareRemove(f.Report()); Require(plan.PreservedFiles.SequenceEqual([WidescreenCatalog.Ini]), "INI esterno non segnalato.");
            File.AppendAllText(f.Path(WidescreenCatalog.Plugin), "foreign change"); Refused(() => f.Service.Execute(plan));
            Require(Hash(f.Path(WidescreenCatalog.Ini)) == hash && File.Exists(f.Path(WidescreenCatalog.Loader)), "Rimozione parziale prima del conflitto.");
        });
        check("Widescreen: redirect verso HTTP, credenziali o repository estranei rifiutati", () =>
        {
            foreach (var uri in new[] { "http://github.com/ThirteenAG/WidescreenFixesPack/a", "https://github.com/other/repo/a", "https://user:password@release-assets.githubusercontent.com/a", "https://release-assets.githubusercontent.com:444/a", "https://example.com/asset.zip" })
                Refused(() => WidescreenPackageSource.ValidateUri(new Uri(uri)));
            WidescreenPackageSource.ValidateUri(new Uri(WidescreenCatalog.Url));
            WidescreenPackageSource.ValidateUri(new Uri("https://release-assets.githubusercontent.com/public-asset"));
        });
        check("Widescreen: pacchetto non approvato rifiutato prima dell'estrazione", () => Refused(() => WidescreenPackageSource.Extract([1, 2, 3])));
        if (official || localPackage is not null) check("Widescreen ufficiale: ZIP e singoli componenti verificati, deployment statico senza eseguire ASI", () =>
        {
            using var f = new Fixture(root, makeExe);
            var package = localPackage is null ? new WidescreenPackageSource(System.IO.Path.Combine(f.Root, "cache")).PrepareAsync().GetAwaiter().GetResult()
                : WidescreenPackageSource.Extract(File.ReadAllBytes(localPackage));
            Require(Encoding.UTF8.GetString(package.Files[WidescreenCatalog.Ini]).Contains("NoOpticalDriveFix=0"), "Opzione disco non disattivata.");
            Require(Encoding.UTF8.GetString(package.Files[WidescreenCatalog.Ini]).Contains("WriteSettingsToFile=0"), "Salvataggi/registro alterati.");
            f.Service.Execute(f.Service.PrepareWidescreen(f.Report(), package));
            Require(Hash(f.Path(WidescreenCatalog.Plugin)) == WidescreenCatalog.Entries[WidescreenCatalog.Plugin].Hash, "Plugin diverso dal catalogo.");
            f.Service.Execute(f.Service.PrepareRemove(f.Report()));
        });
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Refused(Action action)
    { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or InvalidOperationException) { return; } throw new Exception("Operazione non valida accettata."); }
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress p) => action(p); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game { get; }
        public string Exe => System.IO.Path.Combine(Game, "speed2.exe");
        public string Store => System.IO.Path.Combine(Root, "profiles");
        public ReShadeProfileService Service { get; }
        public VerifiedWidescreen Package { get; }
        public Fixture(string root, Func<string, bool, bool, string, string> makeExe)
        {
            Root = System.IO.Path.Combine(root, "wide-" + Guid.NewGuid().ToString("N")); Game = System.IO.Path.Combine(Root, "Game"); Directory.CreateDirectory(Game);
            makeExe(Exe, false, false, "d3d9.dll"); Service = ReShadeProfileService.ForWidescreen(Store, _ => false);
            Package = new(WidescreenCatalog.Targets.ToDictionary(p => p, p => Encoding.UTF8.GetBytes("Test payload, never loaded: " + p)));
        }
        public string Path(string relative) => System.IO.Path.Combine(Game, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public GameReport Report() => new GameInspector().Inspect(Exe);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
