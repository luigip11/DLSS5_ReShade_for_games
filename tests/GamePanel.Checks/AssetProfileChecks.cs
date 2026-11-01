using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamePanel.Core;

internal static class AssetProfileChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> fixture)
    {
        check("Asset deployment: modelli ripristinati byte per byte dopo toggle e rimozione", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Models(); var originals = f.CarBytes();
            var plan = f.Service.PrepareInstall(f.Report(), pack); Require(plan.Changes.Count == 2, "Anteprima modello errata.");
            Require(f.CarBytes().SequenceEqual(originals), "Anteprima ha cambiato il gioco.");
            var installed = f.Service.Execute(plan); Require(installed.Packs.Single().Enabled, "Stato mancante.");
            Require(!f.CarBytes().SequenceEqual(originals), "Modello non distribuito.");
            f.Service.Execute(f.Service.PrepareToggle(f.Report(), pack.Manifest.Id, false)); Require(f.CarBytes().SequenceEqual(originals), "Toggle non ripristina gli originali.");
            f.Service = f.NewService(); f.Service.Execute(f.Service.PrepareToggle(f.Report(), pack.Manifest.Id, true));
            f.Service.Execute(f.Service.PrepareRemove(f.Report(), pack.Manifest.Id)); Require(f.CarBytes().SequenceEqual(originals), "Originali persi dopo riavvio.");
            Require(f.Service.Status(f.Exe).State!.Packs.Count == 0, "Pack ancora registrato.");
        });
        check("Asset deployment: più texture, configurazione esclusiva e rimozione finale", () =>
        {
            using var f = new Fixture(root, fixture); f.Widescreen(); var a = f.Textures("first-pack"); var b = f.Textures("second-pack");
            f.Service.Execute(f.Service.PrepareInstall(f.Report(), a, f.Loader)); f.Service.Execute(f.Service.PrepareInstall(f.Report(), b, f.Loader));
            Require(f.ConfigPacks().SequenceEqual(["TexturePacks\\GamePanel-first-pack", "TexturePacks\\GamePanel-second-pack"]), "Ordine texture errato.");
            f.Service.Execute(f.Service.PrepareToggle(f.Report(), a.Manifest.Id, false)); Require(f.ConfigPacks().SequenceEqual(["TexturePacks\\GamePanel-second-pack"]), "Texture disattivata ancora attiva.");
            f.Service = f.NewService(); f.Service.Execute(f.Service.PrepareRemove(f.Report(), b.Manifest.Id)); Require(f.ConfigPacks().Length == 0, "Pack rimossi ancora configurati.");
            f.Service.Execute(f.Service.PrepareToggle(f.Report(), a.Manifest.Id, true)); f.Service.Execute(f.Service.PrepareRemove(f.Report(), a.Manifest.Id));
            Require(!File.Exists(f.File("scripts/TexWizard.asi")) && !File.Exists(f.File("scripts/TexWizard.json")), "Runtime non rimosso.");
            Require(File.ReadAllText(f.File("scripts/foreign.ini")) == "preserva", "File scripts estraneo alterato.");
        });
        check("Asset deployment: resume e rollback da nuova istanza a ogni passo", () =>
        {
            foreach (var rollback in new[] { false, true })
            foreach (var stop in new[] { 1, 2 })
            {
                using var f = new Fixture(root, fixture); var original = f.CarBytes(); var pack = f.Models(); using var cancellation = new CancellationTokenSource();
                Cancelled(() => f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack), cancellation.Token,
                    new InlineProgress(p => { if (p.Completed == stop) cancellation.Cancel(); })));
                Require(f.Service.Status(f.Exe).Pending, "Journal Pending mancante.");
                string? temporary = null;
                if (stop == 1)
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(f.ProfileFolder, "assets-operation.json")));
                    var id = doc.RootElement.GetProperty("Id").GetString()!;
                    var target = doc.RootElement.GetProperty("Changes")[1].GetProperty("Path").GetString()!;
                    temporary = Path.Combine(Path.GetDirectoryName(f.File(target))!, $".gamepanel-assets-{id}-0001.tmp");
                    File.Copy(Path.Combine(f.ProfileFolder, id, "0001-after.bin"), temporary);
                }
                f.Service = f.NewService(); f.Service.Recover(f.Exe, rollback);
                Require(temporary is null || !File.Exists(temporary), "Temporanea del crash lasciata nel gioco.");
                Require(!f.Service.Status(f.Exe).Pending, "Journal non concluso.");
                Require(f.CarBytes().SequenceEqual(original) == rollback, "Direzione di recupero errata.");
            }
        });
        check("Asset deployment: cache e backup corrotti fermano scrittura e recupero", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Models(); var plan = f.Service.PrepareInstall(f.Report(), pack); var original = f.CarBytes();
            File.AppendAllText(Path.Combine(pack.Folder, "files", "CARS", "COROLLA", "GEOMETRY.BIN"), "corrotto");
            Refused(() => f.Service.Execute(plan)); Require(f.CarBytes().SequenceEqual(original), "Input corrotto scritto nel gioco.");
            pack = f.Models("another-model"); using var cancellation = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack), cancellation.Token, new InlineProgress(_ => cancellation.Cancel())));
            var live = f.CarBytes(); var operation = JsonDocument.Parse(File.ReadAllText(Path.Combine(f.ProfileFolder, "assets-operation.json"))).RootElement.GetProperty("Id").GetString()!;
            File.AppendAllText(Path.Combine(f.ProfileFolder, operation, "0000-before.bin"), "corrotto");
            Refused(() => f.NewService().Recover(f.Exe, true)); Require(f.CarBytes().SequenceEqual(live), "Backup corrotto ha causato nuove scritture.");
        });
        check("Asset deployment: modifiche esterne e EXE cambiato restano intatti", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Models(); f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack));
            File.AppendAllText(f.File("CARS/COROLLA/GEOMETRY.BIN"), "modifica utente"); var bytes = f.CarBytes();
            Refused(() => f.Service.PrepareRemove(f.Report(), pack.Manifest.Id)); Require(f.CarBytes().SequenceEqual(bytes), "Modifica utente sovrascritta.");
            File.AppendAllText(f.Exe, "nuovo EXE"); Refused(() => f.Service.PrepareToggle(f.Report(), pack.Manifest.Id, false));
        });
        check("Asset deployment: duplicati, overlap, target e auto mancanti sono bloccati", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Models(); f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack));
            Refused(() => f.Service.PrepareInstall(f.Report(), pack)); Refused(() => f.Service.PrepareInstall(f.Report(), f.Models("overlap-pack")));
            f.Service.Execute(f.Service.PrepareRemove(f.Report(), pack.Manifest.Id)); File.Delete(f.File("CARS/COROLLA/TEXTURES.BIN"));
            Refused(() => f.Service.PrepareInstall(f.Report(), f.Models("missing-original")));
            using var g = new Fixture(root, fixture); g.Widescreen(); var texture = g.Textures("texture-pack"); File.WriteAllText(g.File("scripts/TexWizard.json"), "estraneo");
            Refused(() => g.Service.PrepareInstall(g.Report(), texture, g.Loader)); Require(File.ReadAllText(g.File("scripts/TexWizard.json")) == "estraneo", "Config sconosciuta modificata.");
        });
        check("Asset deployment: lock e journal sono condivisi con immagine e schermo", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Models(); Directory.CreateDirectory(f.ProfileFolder);
            foreach (var name in new[] { "operation.json", "widescreen-operation.json" })
            {
                var path = Path.Combine(f.ProfileFolder, name); File.WriteAllText(path, "{\"Status\":\"Pending\"}");
                Refused(() => f.Service.PrepareInstall(f.Report(), pack)); File.Delete(path);
            }
            var plan = f.Service.PrepareInstall(f.Report(), pack);
            using var lease = new FileStream(Path.Combine(f.ProfileFolder, ".operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Refused(() => f.Service.Execute(plan));
        });
        check("Asset deployment: symlink di auto, scripts e archivio non vengono seguiti", () =>
        {
            foreach (var kind in new[] { "car", "scripts", "cache" })
            {
                using var f = new Fixture(root, fixture); var outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside); var sentinel = Path.Combine(outside, "preserva"); File.WriteAllText(sentinel, "intatto");
                var pack = kind == "scripts" ? f.Textures("texture-pack") : f.Models(); if (kind == "scripts") f.Widescreen();
                var target = kind == "car" ? f.File("CARS/COROLLA") : kind == "scripts" ? f.File("scripts") : pack.Folder;
                Directory.Move(target, target + ".original");
                try { Directory.CreateSymbolicLink(target, outside); }
                catch (Exception e) when (OperatingSystem.IsWindows() && e is IOException or UnauthorizedAccessException) { throw new SkipCheckException("Symlink non disponibile."); }
                Refused(() => f.Service.PrepareInstall(f.Report(), pack, f.Loader)); Require(File.ReadAllText(sentinel) == "intatto", "Link seguito durante la scrittura.");
                Directory.Delete(target);
            }
        });
        check("Asset deployment: texture richiedono wide e hash runtime, modelli non richiedono loader", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Textures("texture-pack"); Refused(() => f.Service.PrepareInstall(f.Report(), pack, f.Loader));
            f.Widescreen(); File.AppendAllText(f.Loader, "corrotto"); Refused(() => f.Service.PrepareInstall(f.Report(), pack, f.Loader));
            f.Service.Execute(f.Service.PrepareInstall(f.Report(), f.Models()));
        });
        check("Asset deployment: crash dopo commit stato consente resume o rollback idempotente", () =>
        {
            foreach (var rollback in new[] { false, true })
            {
                using var f = new Fixture(root, fixture); var original = f.CarBytes(); var pack = f.Models(); f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack));
                var journal = Path.Combine(f.ProfileFolder, "assets-operation.json"); var op = JsonNode.Parse(File.ReadAllText(journal))!; op["Status"] = "Pending"; File.WriteAllText(journal, op.ToJsonString());
                f.NewService().Recover(f.Exe, rollback); Require(f.CarBytes().SequenceEqual(original) == rollback, "Commit parziale non recuperato.");
                Require(!f.NewService().Status(f.Exe).Pending, "Journal resta interrotto.");
            }
        });
        check("Asset deployment: journal alterati, percorsi estranei e JSON duplicati sono rifiutati", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Models(); using var cancellation = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack), cancellation.Token, new InlineProgress(_ => cancellation.Cancel())));
            var before = f.CarBytes(); var journal = Path.Combine(f.ProfileFolder, "assets-operation.json"); var original = File.ReadAllText(journal);
            foreach (var mutate in new Action<JsonNode>[]
            {
                op => op["Changes"]![0]!["Path"] = "GLOBAL/textures.bin",
                op => op["Changes"]![0]!["Path"] = "../outside.bin",
                op => op["Changes"]![0]!["BeforeSha256"] = new string('0',64),
                op => ((JsonArray)op["Changes"]!).RemoveAt(0)
            })
            {
                var op = JsonNode.Parse(original)!; mutate(op); File.WriteAllText(journal, op.ToJsonString());
                Refused(() => f.NewService().Recover(f.Exe, true)); Require(f.CarBytes().SequenceEqual(before), "Journal alterato ha scritto nel gioco.");
            }
            // Write a duplicate independently of serializer formatting.
            File.WriteAllText(journal, "{\"Status\":\"Pending\"," + original.TrimStart()[1..]); Refused(() => f.NewService().Recover(f.Exe, false));
            File.WriteAllText(journal, original); f.NewService().Recover(f.Exe, true);
        });
        check("Asset deployment: configurazione journal resta legata ai soli pack attivi", () =>
        {
            using var f = new Fixture(root, fixture); f.Widescreen(); var pack = f.Textures("texture-pack"); using var cancellation = new CancellationTokenSource();
            Cancelled(() => f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack, f.Loader), cancellation.Token, new InlineProgress(_ => cancellation.Cancel())));
            var journal = Path.Combine(f.ProfileFolder, "assets-operation.json"); var op = JsonNode.Parse(File.ReadAllText(journal))!;
            var bytes = Encoding.UTF8.GetBytes("{\"packs\":[\"..\\other-pack\"],\"autoScan\":true}");
            var hash = Hash(bytes); var changes = (JsonArray)op["Changes"]!; var index = Enumerable.Range(0,changes.Count).Single(i => changes[i]!["Path"]!.GetValue<string>() == "scripts/TexWizard.json");
            changes[index]!["AfterSha256"] = hash;
            var managed = ((JsonArray)op["Next"]!["ManagedFiles"]!).Single(n => n!["Path"]!.GetValue<string>() == "scripts/TexWizard.json")!; managed["Sha256"] = hash; managed["Bytes"] = bytes.Length;
            File.WriteAllBytes(Path.Combine(f.ProfileFolder, op["Id"]!.GetValue<string>(), $"{index:D4}-after.bin"), bytes); File.WriteAllText(journal, op.ToJsonString());
            Refused(() => f.NewService().Recover(f.Exe, false)); Require(!File.Exists(f.File("scripts/TexWizard.json")), "Configurazione esterna scritta.");
        });
        check("Asset deployment: pin TexWizard rifiuta payload ASI contraffatti in resume e rollback", () =>
        {
            foreach (var rollback in new[] { false, true })
            {
                using var f = new Fixture(root, fixture); f.Widescreen(); var pack = f.Textures("texture-pack");
                if (rollback)
                {
                    f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack, f.Loader)); f.Service.Execute(f.Service.PrepareRemove(f.Report(), pack.Manifest.Id));
                }
                else
                {
                    using var cancellation = new CancellationTokenSource();
                    Cancelled(() => f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack, f.Loader), cancellation.Token, new InlineProgress(_ => cancellation.Cancel())));
                }
                Require(!File.Exists(f.File("scripts/TexWizard.asi")), "Fixture deve lasciare il loader assente.");
                var journal = Path.Combine(f.ProfileFolder, "assets-operation.json"); var op = JsonNode.Parse(File.ReadAllText(journal))!; op["Status"] = "Pending";
                var bytes = Encoding.UTF8.GetBytes("MZ arbitrary unapproved ASI payload"); var hash = Hash(bytes); var changes = (JsonArray)op["Changes"]!;
                var index = Enumerable.Range(0, changes.Count).Single(i => changes[i]!["Path"]!.GetValue<string>() == "scripts/TexWizard.asi");
                changes[index]![rollback ? "BeforeSha256" : "AfterSha256"] = hash;
                var managed = ((JsonArray)op[rollback ? "Previous" : "Next"]!["ManagedFiles"]!).Single(n => n!["Path"]!.GetValue<string>() == "scripts/TexWizard.asi")!;
                managed["Sha256"] = hash; managed["Bytes"] = bytes.Length;
                File.WriteAllBytes(Path.Combine(f.ProfileFolder, op["Id"]!.GetValue<string>(), $"{index:D4}-{(rollback ? "before" : "after")}.bin"), bytes); File.WriteAllText(journal, op.ToJsonString());
                try { f.NewService().Recover(f.Exe, rollback); throw new InvalidOperationException("Pin TexWizard ignorato."); }
                catch (InvalidDataException e) when (e.Message.Contains("Identità TexWizard", StringComparison.Ordinal)) { }
                Require(!File.Exists(f.File("scripts/TexWizard.asi")), "ASI non approvata scritta dal recupero.");
            }
        });
        check("Asset deployment: toggle e rimozione interrotti mantengono i backup originali", () =>
        {
            foreach (var remove in new[] { false, true })
            foreach (var rollback in new[] { false, true })
            foreach (var stop in new[] { 1, 2 })
            {
                using var f = new Fixture(root, fixture); var original = f.CarBytes(); var pack = f.Models(); f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack)); var installed = f.CarBytes();
                using var cancellation = new CancellationTokenSource(); var plan = remove ? f.Service.PrepareRemove(f.Report(), pack.Manifest.Id) : f.Service.PrepareToggle(f.Report(), pack.Manifest.Id, false);
                Cancelled(() => f.Service.Execute(plan, cancellation.Token, new InlineProgress(p => { if (p.Completed == stop) cancellation.Cancel(); })));
                f.Service = f.NewService(); f.Service.Recover(f.Exe, rollback); Require(f.CarBytes().SequenceEqual(rollback ? installed : original), "Recupero lifecycle ha perso byte.");
                if (rollback || !remove) { f.Service.Execute(f.Service.PrepareRemove(f.Report(), pack.Manifest.Id)); Require(f.CarBytes().SequenceEqual(original), "Baseline persa dopo recupero."); }
            }
        });
        check("Asset deployment: collisioni texture tra pack, anche disattivati e hash esadecimali, sono bloccate", () =>
        {
            using var f = new Fixture(root, fixture); f.Widescreen(); const string original = "ORIGINAL_ROAD";
            var pack = f.Textures("first-pack", original); f.Service.Execute(f.Service.PrepareInstall(f.Report(), pack, f.Loader));
            Refused(() => f.Service.PrepareInstall(f.Report(), f.Textures("same-name", original), f.Loader));
            uint hash = uint.MaxValue; foreach (var c in original) hash = unchecked(hash * 33 + c);
            var alias = f.Textures("hash-alias", "0x" + hash.ToString("X8")); Refused(() => f.Service.PrepareInstall(f.Report(), alias, f.Loader));
            f.Service.Execute(f.Service.PrepareToggle(f.Report(), pack.Manifest.Id, false)); Refused(() => f.Service.PrepareInstall(f.Report(), alias, f.Loader));
            Require(f.Service.Status(f.Exe).State!.Packs.Count == 1, "Pack in conflitto registrato.");
        });
        check("Asset deployment: texture bloccate con plugin ASI, config loader o componenti wide alterati", () =>
        {
            foreach (var target in new[] { WidescreenCatalog.Plugin, WidescreenCatalog.Data, "scripts/Unknown.asi", "dinput8.ini" })
            {
                using var f = new Fixture(root, fixture); f.Widescreen(); var pack = f.Textures("texture-pack"); File.WriteAllText(f.File(target), "alterato");
                Refused(() => f.Service.PrepareInstall(f.Report(), pack, f.Loader)); Require(!File.Exists(f.File("scripts/TexWizard.asi")), "Runtime installato con prerequisiti non verificati.");
            }
        });
        check("Asset deployment: annullamento iniziale non modifica gioco né stato", () =>
        {
            using var f = new Fixture(root, fixture); var pack = f.Models(); var plan = f.Service.PrepareInstall(f.Report(), pack); var before = f.CarBytes();
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); Cancelled(() => f.Service.Execute(plan, cancelled.Token));
            Require(f.Service.Status(f.Exe).State is null && f.CarBytes().SequenceEqual(before), "Annullamento ha iniziato scritture.");
        });
    }
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress value) => action(value); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Refused(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException or UnauthorizedAccessException) { return; } throw new InvalidOperationException("Operazione non sicura accettata."); }
    private static void Cancelled(Action action) { try { action(); } catch (OperationCanceledException) { return; } throw new InvalidOperationException("Annullamento ignorato."); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Game => Path.Combine(Root, "game");
        public string Exe => Path.Combine(Game, "speed2.exe");
        public string Store => Path.Combine(Root, "profiles");
        public string Loader => Path.Combine(Root, "TexWizard.asi");
        public AssetProfileService Service { get; set; }
        public string ProfileFolder => Path.Combine(Store, Hash(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? Game.ToUpperInvariant() : Game)));
        public Fixture(string root, Func<string, bool, bool, string, string> fixture)
        {
            Root = Path.Combine(root, "asset-deploy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Game); fixture(Exe, false, false, "d3d9.dll");
            Directory.CreateDirectory(File("CARS/COROLLA")); System.IO.File.WriteAllBytes(File("CARS/COROLLA/GEOMETRY.BIN"), Encoding.UTF8.GetBytes("original geometry data"));
            System.IO.File.WriteAllBytes(File("CARS/COROLLA/TEXTURES.BIN"), Encoding.UTF8.GetBytes("original texture data"));
            System.IO.File.WriteAllText(Loader, "test runtime fixture"); Service = NewService();
        }
        public AssetProfileService NewService() => new(Store, _ => false, _ => null, Hash(Encoding.UTF8.GetBytes("test runtime fixture")), Hash(Encoding.UTF8.GetBytes("test asi loader fixture")));
        public string File(string path) => Path.Combine(Game, path.Replace('/', Path.DirectorySeparatorChar));
        public GameReport Report() => new GameInspector().Inspect(Exe);
        public byte[] CarBytes() => System.IO.File.ReadAllBytes(File("CARS/COROLLA/GEOMETRY.BIN")).Concat(System.IO.File.ReadAllBytes(File("CARS/COROLLA/TEXTURES.BIN"))).ToArray();
        public PreparedAssetPack Models(string id = "model-pack") => Package(id, "Models", new() { ["CARS/COROLLA/GEOMETRY.BIN"] = Chunk(0x80134000, 23), ["CARS/COROLLA/TEXTURES.BIN"] = Chunk(0xB3300000, 31) });
        public PreparedAssetPack Textures(string id, string? original = null) => Package(id, "Textures", new() { [$"TexturePacks/GamePanel-{id}/meta.json"] = JsonSerializer.SerializeToUtf8Bytes(new { textures = new[] { new[] { original ?? "OLD_" + id, "NEW_" + id } } }), [$"TexturePacks/GamePanel-{id}/textures.bin"] = Chunk(0xB3300000, 42) });
        private static byte[] Chunk(uint id, byte fill)
        { var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, id); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 16); data.AsSpan(8).Fill(fill); return data; }
        private PreparedAssetPack Package(string id, string kind, Dictionary<string, byte[]> files)
        {
            var manifest = new AssetPackManifest(1, id, id, "Fixture", "https://example.org/pack", kind, files.Select(p => new AssetPackFile(p.Key, Hash(p.Value).ToUpperInvariant(), p.Value.Length)).ToArray());
            var zip = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var stream = archive.CreateEntry(AssetPackageImporter.ManifestName).Open()) JsonSerializer.Serialize(stream, manifest);
                foreach (var f in files) { using var stream = archive.CreateEntry(f.Key).Open(); stream.Write(f.Value); }
            }
            return new AssetPackageImporter(Path.Combine(Root, "import-cache")).Import(zip);
        }
        public void Widescreen()
        {
            Directory.CreateDirectory(File("scripts")); System.IO.File.WriteAllText(File("scripts/foreign.ini"), "preserva");
            var files = WidescreenCatalog.Targets.Select(path =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File(path))!); var bytes = Encoding.UTF8.GetBytes(path == "dinput8.dll" ? "test asi loader fixture" : "wide fixture");
                System.IO.File.WriteAllBytes(File(path), bytes); return new ProfileFile(path, Hash(bytes), bytes.Length);
            }).ToArray();
            Directory.CreateDirectory(ProfileFolder); var report = Report();
            var state = new ProfileState(1, Guid.NewGuid().ToString("N"), Exe, report.Sha256, WidescreenCatalog.Version, true, SdrSettings.Original, files, []);
            System.IO.File.WriteAllText(Path.Combine(ProfileFolder, "widescreen-state.json"), JsonSerializer.Serialize(state));
        }
        public string[] ConfigPacks()
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(File("scripts/TexWizard.json")));
            Require(!doc.RootElement.GetProperty("autoScan").GetBoolean(), "autoScan attivo.");
            return doc.RootElement.GetProperty("packs").EnumerateArray().Select(x => x.GetString()!).ToArray();
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
