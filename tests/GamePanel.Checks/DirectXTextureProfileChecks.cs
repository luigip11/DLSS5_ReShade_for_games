using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamePanel.Core;

internal static class DirectXTextureProfileChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> fixture)
    {
        check("DirectX runtime: installazione solo su conferma, catture scrivibili esterne e rimozione conserva i dati", () =>
        {
            using var f = new Fixture(root, fixture); var exe = File.ReadAllBytes(f.Exe);
            var plan = f.Service.PrepareInstall(f.Report(), f.Loader);
            Require(plan.Changes.Count == 2 && !File.Exists(f.GameFile(DirectXTextureCatalog.LoaderRelativePath)), "Anteprima ha scritto nel gioco.");
            var installed = f.Service.Execute(plan); var status = f.Service.Status(f.Exe);
            Require(installed.Installed && status.Installed && status.InjectionEnabled == false && !status.State!.EnableInjection, "Installazione non parte in sola cattura.");
            var capture = f.Capture(); var source = File.ReadAllBytes(capture);
            f.Service.Execute(f.Service.PrepareRemove(f.Report()));
            Require(!f.Service.Status(f.Exe).Installed && !File.Exists(f.GameFile(DirectXTextureCatalog.LoaderRelativePath))
                && !File.Exists(f.GameFile(DirectXTextureProfileService.ConfigRelativePath)), "Rimozione incompleta.");
            Require(File.ReadAllBytes(capture).SequenceEqual(source) && File.ReadAllBytes(f.Exe).SequenceEqual(exe), "Catture o EXE alterati.");
        });
        check("DirectX runtime: nessun vincolo dimensione/EP di TexWizard, hash loader e prerequisiti obbligatori", () =>
        {
            using var f = new Fixture(root, fixture);
            Require(new FileInfo(f.Exe).Length != TextureRuntimeCatalog.ExpectedExeBytes, "Fixture accidentalmente TexWizard.");
            Require(DirectXTextureCatalog.CheckGame(f.Report()) is null, "EXE PE x86 DX9 regolare rifiutato.");
            File.AppendAllText(f.Loader, "alterato"); Refused(() => f.Service.PrepareInstall(f.Report(), f.Loader));
            Require(!File.Exists(f.GameFile(DirectXTextureCatalog.LoaderRelativePath)), "Loader non approvato copiato.");
            using var g = new Fixture(root, fixture); File.Delete(g.GameFile(WidescreenCatalog.Data));
            Refused(() => g.Service.PrepareInstall(g.Report(), g.Loader));
        });
        check("DirectX runtime: resume/rollback da nuova istanza a ogni modifica conserva l'EXE", () =>
        {
            foreach (var remove in new[] { false, true })
            foreach (var rollback in new[] { false, true })
            foreach (var stop in new[] { 1, 2 })
            {
                using var f = new Fixture(root, fixture); var exe = File.ReadAllBytes(f.Exe);
                if (remove) f.Install();
                var plan = remove ? f.Service.PrepareRemove(f.Report()) : f.Service.PrepareInstall(f.Report(), f.Loader);
                using var cancel = new CancellationTokenSource();
                Cancelled(() => f.Service.Execute(plan, cancel.Token, new InlineProgress(p => { if (p.Completed == stop) cancel.Cancel(); })));
                Require(f.Service.Status(f.Exe).Pending, "Journal interrotto assente.");
                f.Service = f.NewService(); f.Service.Recover(f.Exe, rollback);
                Require(f.Service.Status(f.Exe).Installed == (remove ? rollback : !rollback) && !f.Service.Status(f.Exe).Pending, "Recupero in direzione errata.");
                Require(File.ReadAllBytes(f.Exe).SequenceEqual(exe), "Recupero ha modificato EXE.");
            }
        });
        check("DirectX runtime: INI del pannello ammesso, cartella diversa e DLL esterne bloccate", () =>
        {
            using var f = new Fixture(root, fixture); f.Install();
            f.EditConfig(s => s.Replace("HighlightSelected=0", "HighlightSelected=1").Replace("AutoDump=0", "AutoDump=1"));
            f.Service.Execute(f.Service.PrepareRemove(f.Report())); Require(!f.Service.Status(f.Exe).Installed, "INI modificato dal pannello impedisce rimozione.");
            f.Install(); f.EditConfig(s => s.Replace(f.ResourceRoot, Path.Combine(f.Root, "outside")));
            Refused(() => f.Service.PrepareRemove(f.Report())); Require(File.Exists(f.GameFile(DirectXTextureCatalog.LoaderRelativePath)), "INI estraneo sovrascritto.");
            using var g = new Fixture(root, fixture); File.WriteAllText(g.GameFile("scripts/TexWizard.asi"), "conflitto");
            Refused(() => g.Service.PrepareInstall(g.Report(), g.Loader));
            File.Delete(g.GameFile("scripts/TexWizard.asi")); File.WriteAllText(g.GameFile("d3d9.dll"), "DLL sconosciuta");
            Refused(() => g.Service.PrepareInstall(g.Report(), g.Loader));
        });
        check("DirectX runtime: sostituzioni richiedono DDS valide e attivazione esplicita", () =>
        {
            using var f = new Fixture(root, fixture); f.Install();
            Refused(() => f.Service.PrepareSettings(f.Report(), true));
            var capture = f.Capture(); var hash = DirectXTextureData.ReadDds(capture).TopMipHash;
            var source = f.Replacement(); var replacements = new DirectXTextureReplacementService(f.Service);
            replacements.Install(f.Report(), f.ResourceRoot, hash, source);
            Require(f.Service.Status(f.Exe).InjectionEnabled == false, "Importazione ha attivato le texture senza conferma.");
            f.Service.Execute(f.Service.PrepareSettings(f.Report(), true)); Require(f.Service.Status(f.Exe).State!.EnableInjection, "Attivazione non applicata.");
            f.Service.Execute(f.Service.PrepareSettings(f.Report(), false)); Require(f.Service.Status(f.Exe).InjectionEnabled == false, "Disattivazione non applicata.");
        });
        check("DirectX runtime: anteprima vecchia, EXE cambiato e gioco aperto non scrivono", () =>
        {
            using var f = new Fixture(root, fixture); var report = f.Report(); var plan = f.Service.PrepareInstall(report, f.Loader);
            f.IsRunning = true; Refused(() => f.Service.Execute(plan)); f.IsRunning = false;
            File.AppendAllText(f.Exe, "nuovo EXE"); Refused(() => f.Service.Execute(plan));
            Require(!File.Exists(f.GameFile(DirectXTextureCatalog.LoaderRelativePath)), "EXE cambiato o gioco aperto hanno consentito la scrittura.");
        });
        check("DirectX runtime: lock e journal condivisi bloccano operazioni concorrenti", () =>
        {
            using var f = new Fixture(root, fixture);
            foreach (var file in new[] { "operation.json", "widescreen-operation.json", "assets-operation.json", "directx-replacements-operation.json" })
            {
                var path = Path.Combine(f.ProfileFolder, file); File.WriteAllText(path, "{\"Status\":\"Pending\"}");
                Refused(() => f.Service.PrepareInstall(f.Report(), f.Loader)); File.Delete(path);
            }
            var plan = f.Service.PrepareInstall(f.Report(), f.Loader);
            using var lease = new FileStream(Path.Combine(f.ProfileFolder, ".operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Refused(() => f.Service.Execute(plan));
        });
        check("DirectX runtime: payload e journal corrotti non introducono un'ASI arbitraria", () =>
        {
            foreach (var change in new[] { "payload", "pin", "path", "duplicate", "null", "config-toggle", "id-newline" })
            {
                using var f = new Fixture(root, fixture); using var cancel = new CancellationTokenSource();
                Cancelled(() => f.Service.Execute(f.Service.PrepareInstall(f.Report(), f.Loader), cancel.Token, new InlineProgress(_ => cancel.Cancel())));
                var opPath = Path.Combine(f.ProfileFolder, "directx-operation.json"); var op = JsonNode.Parse(File.ReadAllText(opPath))!;
                var data = Path.Combine(f.ProfileFolder, "directx-" + op["Id"]!.GetValue<string>(), "0000-after.bin");
                if (change == "payload") File.AppendAllText(data, "alterato");
                if (change == "pin") { File.WriteAllText(data, "ASI estranea"); op["Changes"]![0]!["AfterSha256"] = Hash(File.ReadAllBytes(data)); File.WriteAllText(opPath, op.ToJsonString()); }
                if (change == "path") { op["Changes"]![0]!["Path"] = "../escape.dll"; File.WriteAllText(opPath, op.ToJsonString()); }
                if (change == "duplicate") File.WriteAllText(opPath, File.ReadAllText(opPath).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 1,\"SchemaVersion\": 1"));
                if (change == "null") { op["Changes"]![0] = null; File.WriteAllText(opPath, op.ToJsonString()); }
                if (change == "id-newline") { op["Id"] = op["Id"]!.GetValue<string>() + "\n"; File.WriteAllText(opPath, op.ToJsonString()); }
                if (change == "config-toggle")
                {
                    var config = Path.Combine(Path.GetDirectoryName(data)!, "0001-after.bin");
                    File.WriteAllText(config, File.ReadAllText(config).Replace("EnableInjection=0", "EnableInjection=1"), Encoding.Unicode);
                    op["Changes"]![1]!["AfterSha256"] = Hash(File.ReadAllBytes(config)); File.WriteAllText(opPath, op.ToJsonString());
                }
                var before = File.ReadAllBytes(f.GameFile(DirectXTextureCatalog.LoaderRelativePath));
                Refused(() => f.NewService().Recover(f.Exe, false)); Require(File.ReadAllBytes(f.GameFile(DirectXTextureCatalog.LoaderRelativePath)).SequenceEqual(before), "Payload non approvato introdotto nel gioco.");
            }
        });
        check("DirectX runtime: file estranei e config preesistente non vengono sostituiti", () =>
        {
            using var f = new Fixture(root, fixture); var config = f.GameFile(DirectXTextureProfileService.ConfigRelativePath);
            File.WriteAllText(config, "preserva"); Refused(() => f.Service.PrepareInstall(f.Report(), f.Loader)); Require(File.ReadAllText(config) == "preserva", "Config estranea sovrascritta.");
            File.Delete(config); f.Install(); File.AppendAllText(f.GameFile(DirectXTextureCatalog.LoaderRelativePath), "utente");
            Refused(() => f.Service.PrepareRemove(f.Report())); Require(File.ReadAllText(f.GameFile(DirectXTextureCatalog.LoaderRelativePath)).EndsWith("utente"), "Modifica utente cancellata.");
        });
        check("DirectX runtime: junction/symlink scripts e cartella dati bloccati", () =>
        {
            if (OperatingSystem.IsWindows()) return;
            foreach (var target in new[] { "scripts", "data" })
            {
                using var f = new Fixture(root, fixture); var outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside);
                var path = target == "scripts" ? f.GameFile("scripts") : f.ResourceRoot;
                if (Directory.Exists(path)) Directory.Delete(path, true); Directory.CreateDirectory(Path.GetDirectoryName(path)!); Directory.CreateSymbolicLink(path, outside);
                Refused(() => f.Service.PrepareInstall(f.Report(), f.Loader)); Require(!Directory.EnumerateFileSystemEntries(outside).Any(), "Symlink seguito.");
            }
        });
        check("DirectX DDS: installazione e rimozione preservano cattura ed EXE e usano solo inject", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); var captured = f.Capture(); var captureBytes = File.ReadAllBytes(captured); var exe = File.ReadAllBytes(f.Exe);
            var hash = DirectXTextureData.ReadDds(captured).TopMipHash; var source = f.Replacement(); var service = new DirectXTextureReplacementService(f.Service);
            var next = service.Install(f.Report(), f.ResourceRoot, hash, source);
            Require(next.DestinationPath == Path.Combine(f.ResourceRoot, "inject", hash + ".dds") && service.ListInstalled(f.Report(), f.ResourceRoot).Count == 1, "Destinazione DDS errata.");
            service.Remove(f.Report(), f.ResourceRoot, hash);
            Require(service.ListInstalled(f.Report(), f.ResourceRoot).Count == 0 && !File.Exists(next.DestinationPath), "Rimozione DDS incompleta.");
            Require(File.ReadAllBytes(captured).SequenceEqual(captureBytes) && File.ReadAllBytes(f.Exe).SequenceEqual(exe), "DDS ha alterato cattura/gioco.");
        });
        check("DirectX DDS: cattura verificata obbligatoria, DDS malformate e destinazione estranea rifiutate", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); var source = f.Replacement(); var service = new DirectXTextureReplacementService(f.Service);
            Refused(() => service.Install(f.Report(), f.ResourceRoot, "0123456789ABCDEF", source));
            var hash = DirectXTextureData.ReadDds(f.Capture()).TopMipHash; File.WriteAllText(source, "DDS malformata");
            Refused(() => service.Install(f.Report(), f.ResourceRoot, hash, source)); source = f.Replacement();
            var target = Path.Combine(f.ResourceRoot, "inject", hash + ".dds"); File.WriteAllText(target, "utente");
            Refused(() => service.Install(f.Report(), f.ResourceRoot, hash, source)); Require(File.ReadAllText(target) == "utente", "DDS estranea sovrascritta.");
        });
        check("DirectX DDS: modifiche esterne e gioco avviato bloccano rimozione e nuova importazione", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); var hash = DirectXTextureData.ReadDds(f.Capture()).TopMipHash; var service = new DirectXTextureReplacementService(f.Service);
            var next = service.Install(f.Report(), f.ResourceRoot, hash, f.Replacement()); File.AppendAllText(next.DestinationPath, "utente");
            Refused(() => service.Remove(f.Report(), f.ResourceRoot, hash)); Require(File.ReadAllText(next.DestinationPath).EndsWith("utente"), "Modifica DDS cancellata.");
            f.IsRunning = true; Refused(() => service.ListInstalled(f.Report(), f.ResourceRoot));
        });
        check("DirectX DDS: aggiornamento stessa texture conserva inventario e ripristino rimane possibile", () =>
        {
            using var f = new Fixture(root, fixture); f.Install(); var hash = DirectXTextureData.ReadDds(f.Capture()).TopMipHash;
            var service = new DirectXTextureReplacementService(f.Service); var first = service.Install(f.Report(), f.ResourceRoot, hash, f.Replacement(2));
            var second = service.Install(f.Report(), f.ResourceRoot, hash, f.Replacement(3));
            Require(first.Sha256 != second.Sha256 && service.ListInstalled(f.Report(), f.ResourceRoot).Single().Sha256 == second.Sha256, "Aggiornamento DDS non applicato.");
            service.Remove(f.Report(), f.ResourceRoot, hash); Require(!File.Exists(second.DestinationPath), "Rimozione DDS aggiornata fallita.");
        });
        check("DirectX DDS: recupero dopo commit dati e blocco runtime durante journal Pending", () =>
        {
            foreach (var rollback in new[] { false, true })
            {
                using var f = new Fixture(root, fixture); f.Install(); var hash = DirectXTextureData.ReadDds(f.Capture()).TopMipHash;
                var service = new DirectXTextureReplacementService(f.Service); var installed = service.Install(f.Report(), f.ResourceRoot, hash, f.Replacement());
                var path = Path.Combine(f.ProfileFolder, "directx-replacements-operation.json"); var op = JsonNode.Parse(File.ReadAllText(path))!;
                op["Status"] = "Pending"; File.WriteAllText(path, op.ToJsonString());
                Require(service.HasPending(f.Report(), f.ResourceRoot), "Pending DDS non rilevato.");
                Refused(() => f.Service.PrepareRemove(f.Report())); Refused(() => f.Service.PrepareSettings(f.Report(), false));
                Refused(() => service.ListInstalled(f.Report(), f.ResourceRoot));
                service.Recover(f.Report(), f.ResourceRoot, rollback);
                Require(!service.HasPending(f.Report(), f.ResourceRoot)
                    && service.ListInstalled(f.Report(), f.ResourceRoot).Count == (rollback ? 0 : 1)
                    && File.Exists(installed.DestinationPath) == !rollback, "Recupero DDS errato.");
            }
        });
        check("DirectX DDS: journal duplicati, hash con newline e payload DDS forgiati bloccano il recupero", () =>
        {
            foreach (var mode in new[] { "duplicate", "hash-newline", "malformed-before" })
            {
                using var f = new Fixture(root, fixture); f.Install(); var hash = DirectXTextureData.ReadDds(f.Capture()).TopMipHash;
                var service = new DirectXTextureReplacementService(f.Service); var replacement = service.Install(f.Report(), f.ResourceRoot, hash, f.Replacement());
                var original = File.ReadAllBytes(replacement.DestinationPath); var journal = Path.Combine(f.ProfileFolder, "directx-replacements-operation.json");
                var op = JsonNode.Parse(File.ReadAllText(journal))!; op["Status"] = "Pending";
                if (mode == "hash-newline") op["Hash"] = hash + "\n";
                if (mode == "malformed-before")
                {
                    var bad = Enumerable.Repeat((byte)0x42, 256).ToArray(); var badHash = Hash(bad);
                    var before = op["After"]!["Files"]![0]!.DeepClone(); before["Sha256"] = badHash; before["Bytes"] = bad.Length;
                    op["Before"]!["Files"] = new JsonArray(before); op["BeforeSha256"] = badHash;
                    File.WriteAllBytes(Path.Combine(f.ProfileFolder, "directx-dds-backups", op["Id"]!.GetValue<string>(), "before.bin"), bad);
                }
                var json = op.ToJsonString();
                if (mode == "duplicate") json = json.Insert(1, "\"SchemaVersion\":1,");
                File.WriteAllText(journal, json);
                Refused(() => service.Recover(f.Report(), f.ResourceRoot, true));
                Require(File.ReadAllBytes(replacement.DestinationPath).SequenceEqual(original), "Recupero ha scritto una DDS malformata o da journal ambiguo.");
            }
        });
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Refused(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) { return; } throw new InvalidOperationException("Operazione non sicura accettata."); }
    private static void Cancelled(Action action) { try { action(); } catch (OperationCanceledException) { return; } throw new InvalidOperationException("Annullamento ignorato."); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class InlineProgress(Action<BackupProgress> callback) : IProgress<BackupProgress> { public void Report(BackupProgress value) => callback(value); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game => Path.Combine(Root, "game");
        public string Exe => GameFile("speed2.exe");
        public string Store => Path.Combine(Root, "profiles");
        public string Data => Path.Combine(Root, "data");
        public string Loader => Path.Combine(Root, "approved-runtime.bin");
        public bool IsRunning { get; set; }
        public DirectXTextureProfileService Service { get; set; }
        public string ResourceRoot => Service.Status(Exe).ResourceRoot;
        public string ProfileFolder => Service.ProfileFolder(Exe);
        private readonly Dictionary<string, (long Bytes, string Hash)> _wide = new(StringComparer.Ordinal);
        public Fixture(string root, Func<string, bool, bool, string, string> fixture)
        {
            Root = Path.Combine(root, "dx-runtime-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Game); fixture(Exe, false, false, "d3d9.dll");
            File.WriteAllText(Loader, "approved API runtime fixture, never loaded");
            foreach (var path in WidescreenCatalog.Targets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GameFile(path))!); var bytes = Encoding.UTF8.GetBytes("wide fixture: " + path);
                File.WriteAllBytes(GameFile(path), bytes); _wide[path] = (bytes.Length, Hash(bytes));
            }
            Service = NewService(); Directory.CreateDirectory(ProfileFolder); var report = Report();
            var state = new ProfileState(1, Guid.NewGuid().ToString("N"), Exe, report.Sha256, WidescreenCatalog.Version, true, SdrSettings.Original,
                WidescreenCatalog.Targets.Select(p => new ProfileFile(p, _wide[p].Hash, _wide[p].Bytes)).ToArray(), []);
            File.WriteAllText(Path.Combine(ProfileFolder, "widescreen-state.json"), JsonSerializer.Serialize(state));
        }
        public DirectXTextureProfileService NewService() => new(Store, Data, _ => IsRunning, Hash(Encoding.UTF8.GetBytes("approved API runtime fixture, never loaded")), Encoding.UTF8.GetByteCount("approved API runtime fixture, never loaded"), _wide);
        public string GameFile(string relative) => Path.Combine(Game, relative.Replace('/', Path.DirectorySeparatorChar));
        public GameReport Report() => new GameInspector().Inspect(Exe);
        public void Install() => Service.Execute(Service.PrepareInstall(Report(), Loader));
        public void EditConfig(Func<string, string> change) { var path = GameFile(DirectXTextureProfileService.ConfigRelativePath); File.WriteAllText(path, change(File.ReadAllText(path)), Encoding.Unicode); }
        public string Capture()
        {
            var path = Path.Combine(ResourceRoot, "original.dds"); File.WriteAllBytes(path, Dds(1)); var hash = DirectXTextureData.ReadDds(path).TopMipHash;
            var target = Path.Combine(ResourceRoot, "dump", hash + ".dds"); File.Move(path, target, true); return target;
        }
        public string Replacement(byte fill = 2) { var path = Path.Combine(Root, "replacement.dds"); File.WriteAllBytes(path, Dds(fill)); return path; }
        private static byte[] Dds(byte fill)
        {
            var sizes = new[] { 2048, 512, 128, 32, 8, 8, 8 }; var data = new byte[128 + sizes.Sum()];
            void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
            U32(0, 0x20534444); U32(4, 124); U32(8, 0xA1007); U32(12, 64); U32(16, 64); U32(20, 2048); U32(28, 7);
            U32(76, 32); U32(80, 4); U32(84, 0x31545844); U32(108, 0x401008); data.AsSpan(128).Fill(fill); return data;
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
