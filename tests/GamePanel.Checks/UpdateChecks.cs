using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class UpdateChecks
{
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException) { return; }
        throw new Exception("Input dell'aggiornamento non valido accettato.");
    }
    private static Dictionary<string, byte[]> Files(string value) => PackageInventory.Required.ToDictionary(p => p, p => Encoding.UTF8.GetBytes(p + value));
    private static void WritePackage(string folder, Dictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in files) { var p = Path.Combine(folder, name); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, content); }
        File.WriteAllText(Path.Combine(folder, PackageInventory.ManifestName), string.Join('\n', files.Select(p => Hash(p.Value) + "  " + p.Key)) + "\n");
    }
    private static byte[] Zip(Dictionary<string, byte[]> files, string? extra = null, bool symlink = false)
    {
        using var memory = new MemoryStream();
        using (var z = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Add(string name, byte[] bytes) { var entry = z.CreateEntry(name); using var stream = entry.Open(); stream.Write(bytes); }
            foreach (var f in files) Add(f.Key, f.Value);
            Add(PackageInventory.ManifestName, Encoding.UTF8.GetBytes(string.Join('\n', files.Select(f => Hash(f.Value) + "  " + f.Key)) + "\n"));
            if (extra is not null) { var entry = z.CreateEntry(extra); if (symlink) entry.ExternalAttributes = unchecked((int)0xa1ff0000); using var stream = entry.Open(); stream.WriteByte(42); }
        }
        return memory.ToArray();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); } }
    private sealed class Inline(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress p) => action(p); }
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> fixture)
    {
        string TestRoot() { var r = Path.Combine(root, "updates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(r); return r; }
        (string App, string Data, string Ready, UpdateInstallation Install) Setup(Dictionary<string, byte[]>? old = null, Dictionary<string, byte[]>? next = null)
        {
            var r = TestRoot(); var app = Path.Combine(r, "application"); var data = Path.Combine(r, "user-data"); var ready = Path.Combine(data, "Updates", Guid.NewGuid().ToString("N"), "ready");
            WritePackage(app, old ?? Files("old")); WritePackage(ready, next ?? Files("new")); return (app, data, ready, new(app, ready, data));
        }
        byte[] Releases(params string[] versions) => JsonSerializer.SerializeToUtf8Bytes(versions.Select((v, i) => new
        {
            id = i + 1, draft = false, prerelease = v.Contains('-'), tag_name = "v" + v,
            assets = new[] { SoftwareUpdates.PackageName, SoftwareUpdates.MetadataName }.Select((n, j) => new
            { id = j + 1, name = n, size = 100, digest = "sha256:" + new string('a', 64), browser_download_url = "https://github.com/" + SoftwareUpdates.Repository + "/releases/download/v" + v + "/" + n })
        }));
        check("Update: ordinamento SemVer alpha.9/alpha.10 e stabile", () =>
        {
            Require(AppVersion.Parse("0.1.0-alpha.10").CompareTo(AppVersion.Parse("v0.1.0-alpha.9")) > 0, "Alpha ordinate come testo.");
            Require(AppVersion.Parse("0.1.0").CompareTo(AppVersion.Parse("0.1.0-rc.999")) > 0, "Stabile inferiore alla prerelease.");
            foreach (var bad in new[] { "../0.1", "0.1.0-alpha.01", "01.2.3", "1.2.3/evil" }) Reject(() => AppVersion.Parse(bad));
        });
        check("Update: nessuna release distinta da software aggiornato, canale e nessun downgrade", () =>
        {
            Require(!SoftwareUpdates.ParseReleases("[]"u8.ToArray(), AppVersion.Current).PublishedReleasesExist, "Assenza release mascherata.");
            Require(SoftwareUpdates.ParseReleases(Releases("0.1.0-alpha.6", "0.1.0-alpha.7"), AppVersion.Current).Update is null, "Downgrade offerto.");
            Require(SoftwareUpdates.ParseReleases(Releases("0.1.0-alpha.10", "0.1.0-alpha.9"), AppVersion.Current).Update?.Version == "0.1.0-alpha.10", "Non selezionata la più recente.");
            Require(SoftwareUpdates.ParseReleases(Releases("1.0.1-alpha.1"), AppVersion.Parse("1.0.0")).Update is null, "Stabile spostata su alpha.");
        });
        check("Update: release incompleta o senza digest rifiutata", () =>
        {
            Reject(() => SoftwareUpdates.ParseReleases(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Releases("0.2.0")).Replace("sha256:", "missing:")), AppVersion.Current));
            Reject(() => SoftwareUpdates.ParseReleases(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Releases("0.2.0")).Replace(SoftwareUpdates.PackageName, "different.zip")), AppVersion.Current));
        });
        check("Update: rete e rate limit non significano già aggiornato", () =>
        {
            using var c = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden)));
            try { new SoftwareUpdates(() => c).CheckAsync(AppVersion.Current).GetAwaiter().GetResult(); }
            catch (HttpRequestException) { return; }
            throw new Exception("Rate limit mascherato da successo.");
        });
        check("Update: inventario rifiuta traversal, dati utente, collisioni e file alterati", () =>
        {
            foreach (var p in new[] { "../evil", "Library/library.json", "Components/a.dll", "d3d9.dll", ".gamepanel-file" }) Reject(() => PackageInventory.PathName(p));
            Reject(() => PackageInventory.Parse(new string('a', 64) + "  a.dll\n" + new string('b', 64) + "  A.dll"));
            var t = Setup(); File.WriteAllText(Path.Combine(t.App, "GamePanel.dll"), "tampered"); Reject(() => t.Install.Preview());
            var next = Files("new"); next["personal.txt"] = [3]; t = Setup(next: next); File.WriteAllText(Path.Combine(t.App, "personal.txt"), "mine"); Reject(() => t.Install.Preview());
        });
        check("Update: ZIP rigoroso, niente traversal, symlink, duplicati o file extra", () =>
        {
            foreach (var bad in new[] { "../outside.txt", "GamePanel.EXE", "unlisted.txt" })
            { var r = TestRoot(); var zip = Path.Combine(r, "a.zip"); File.WriteAllBytes(zip, Zip(Files("new"), bad)); Reject(() => SoftwareUpdates.Extract(zip, Path.Combine(r, "ready"))); Require(!File.Exists(Path.Combine(r, "outside.txt")), "Traversal scritto."); }
            var path = TestRoot(); var archive = Path.Combine(path, "a.zip"); File.WriteAllBytes(archive, Zip(Files("new"), "link", true)); Reject(() => SoftwareUpdates.Extract(archive, Path.Combine(path, "ready")));
            var missing = Files("new"); missing.Remove("GamePanel.Updater.exe"); path = TestRoot(); archive = Path.Combine(path, "a.zip"); File.WriteAllBytes(archive, Zip(missing)); Reject(() => SoftwareUpdates.Extract(archive, Path.Combine(path, "ready")));
        });
        check("Update: download verificato da digest GitHub e metadata fino allo staging", () =>
        {
            var package = Zip(Files("new")); var metadata = JsonSerializer.SerializeToUtf8Bytes(new SoftwareManifest(1, "0.2.0", SoftwareUpdates.PackageName, package.Length, Hash(package), new string('a', 40)));
            ReleaseAsset Asset(string n, byte[] b) => new(1, n, "https://github.com/" + SoftwareUpdates.Repository + "/releases/download/v0.2.0/" + n, b.Length, Hash(b));
            var release = new SoftwareRelease(1, "0.2.0", "v0.2.0", Asset(SoftwareUpdates.PackageName, package), Asset(SoftwareUpdates.MetadataName, metadata));
            SoftwareUpdates Client(bool corrupt = false, bool redirect = false) => new(() => new(new Handler(r => redirect ? new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.invalid/payload") } } : Response(r.RequestUri!.AbsolutePath.EndsWith(".json") ? metadata : corrupt ? [42] : package))));
            var prepared = Client().PrepareAsync(release, TestRoot()).GetAwaiter().GetResult(); PackageInventory.Verify(prepared.ReadyDirectory, PackageInventory.Read(prepared.ReadyDirectory));
            Reject(() => Client(corrupt: true).PrepareAsync(release, TestRoot()).GetAwaiter().GetResult());
            Reject(() => Client(redirect: true).PrepareAsync(release, TestRoot()).GetAwaiter().GetResult());
        });
        check("Update: sostituisce solo file propri, rimuove obsoleti e conserva dati ed extra", () =>
        {
            var old = Files("old"); old["obsolete.dll"] = [1, 2]; var next = Files("new"); next["new.dll"] = [3, 4]; var t = Setup(old, next);
            File.WriteAllText(Path.Combine(t.App, "my-note.txt"), "keep");
            foreach (var name in new[] { "Library/library.json", "Components/cache.bin", "Backups/copy.bin", "Settings/display.json" }) { var p = Path.Combine(t.Data, name); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "user-data"); }
            t.Install.ProbeWriteAccess(); t.Install.Apply(); Require(!t.Install.HasPendingJournal, "Journal ancora pending.");
            PackageInventory.Verify(t.App, PackageInventory.Read(t.Ready)); Require(!File.Exists(Path.Combine(t.App, "obsolete.dll")), "Obsoleto rimasto.");
            Require(File.ReadAllText(Path.Combine(t.App, "my-note.txt")) == "keep", "Extra cancellato.");
            Require(Directory.GetFiles(t.Data, "*", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "Updates" + Path.DirectorySeparatorChar)).All(p => File.ReadAllText(p) == "user-data"), "Dati utente alterati.");
        });
        foreach (var rollback in new[] { false, true }) check("Update: interruzione dopo scritture e " + (rollback ? "rollback" : "ripresa") + " da nuova istanza", () =>
        {
            var t = Setup(); using var cancel = new CancellationTokenSource();
            try { t.Install.Apply(cancel.Token, new Inline(p => { if (p.Completed == 2) cancel.Cancel(); })); }
            catch (OperationCanceledException) { }
            Require(t.Install.HasPendingJournal, "Interruzione non conservata.");
            new UpdateInstallation(t.App, t.Ready, t.Data).Recover(rollback);
            var expected = Files(rollback ? "old" : "new"); foreach (var f in expected) Require(File.ReadAllBytes(Path.Combine(t.App, f.Key)).SequenceEqual(f.Value), "Byte non recuperati.");
        });
        check("Update: non sovrascrive modifiche successive e rileva backup corrotto", () =>
        {
            var t = Setup();
            try { t.Install.Apply(progress: new Inline(_ => throw new IOException("simulated power loss"))); } catch (IOException) { }
            File.WriteAllText(Path.Combine(t.App, "GamePanel.exe"), "external"); Reject(() => t.Install.Recover(true)); Require(File.ReadAllText(Path.Combine(t.App, "GamePanel.exe")) == "external", "Modifica esterna sovrascritta.");
            t = Setup(); try { t.Install.Apply(progress: new Inline(_ => throw new IOException("simulated power loss"))); } catch (IOException) { }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(t.Ready)!, "before", "0000.bin"), "corrupt"); Reject(() => t.Install.Recover(true));
        });
        check("Update: dati e cartella app sovrapposti rifiutati", () =>
        {
            var t = Setup(); Reject(() => new UpdateInstallation(t.Data, t.Ready, t.Data)); Reject(() => new UpdateInstallation(Path.GetDirectoryName(t.Data)!, t.Ready, t.Data));
        });
        check("Update: journal grande recuperabile oltre 128 KB", () =>
        {
            var old = Files("old"); var next = Files("new");
            for (var i = 0; i < 450; i++) { old[$"old/{i:D4}-" + new string('a', 150) + ".txt"] = [1]; next[$"new/{i:D4}-" + new string('b', 150) + ".txt"] = [2]; }
            var t = Setup(old, next); try { t.Install.Apply(progress: new Inline(_ => throw new IOException("simulated power loss"))); } catch (IOException) { }
            Require(new FileInfo(Path.Combine(Path.GetDirectoryName(t.Ready)!, "install-journal.json")).Length > 128 * 1024, "Fixture journal non abbastanza grande.");
            new UpdateInstallation(t.App, t.Ready, t.Data).Recover(true); PackageInventory.Verify(t.App, PackageInventory.Read(t.App));
        });
        check("Schermo: preferenza persistente, default automatico e dati corrotti non cancellati", () =>
        {
            var r = TestRoot(); var preferences = new DisplayPreferences(r); Require(preferences.Load().DisplayId is null, "Default non automatico.");
            preferences.Save("DELL-stable-id"); Require(new DisplayPreferences(r).Load().DisplayId == "DELL-stable-id", "Preferenza non salvata.");
            preferences.Save(null); Require(preferences.Load().DisplayId is null, "Automatico non ripristinato.");
            File.WriteAllText(Path.Combine(r, "display.json"), "corrupt");
            try { preferences.Save("new"); } catch (InvalidDataException) { Require(File.ReadAllText(Path.Combine(r, "display.json")) == "corrupt", "Corruzione sovrascritta."); return; }
            throw new Exception("Corruzione non rilevata.");
        });
        check("Profili DX9 classici sperimentali separati dal Widescreen Fix specifico UG2", () =>
        {
            foreach (var name in new[] { "speed.exe", "nfsc.exe", "gta_sa.exe" })
            {
                var folder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                var game = fixture(Path.Combine(folder, name), false, false, "d3d9.dll"); var report = new GameInspector().Inspect(game);
                Require(ReShadeProfileService.Eligibility(report) is null, "DX9 classico rifiutato."); Require(ReShadeProfileService.Eligibility(report, true) is not null, "Fix UG2 applicabile ad altro gioco.");
                game = fixture(Path.Combine(folder, name), false, false, "d3d8.dll"); Require(ReShadeProfileService.Eligibility(new GameInspector().Inspect(game)) is not null, "DX8 accettato come DX9.");
            }
        });
    }
}
