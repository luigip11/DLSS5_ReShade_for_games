using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed class RemixTrialPlan
{
    internal RemixTrialPlan(string owner, string source, string executable, string executableHash, string archive,
        string destination, IReadOnlyList<RemixTrialService.SourceFile> sourceFiles,
        IReadOnlyList<RemixArchiveFile> archiveFiles, long available, IReadOnlyList<string> warnings)
    {
        Owner = owner; SourceFolder = source; Executable = executable; ExecutableHash = executableHash;
        ArchivePath = archive; Destination = destination; Sources = sourceFiles; ArchiveFiles = archiveFiles;
        OriginalBytes = sourceFiles.Where(f => f.Copy).Sum(f => f.Bytes);
        ArchiveBytes = archiveFiles.Where(f => !f.IsDirectory).Sum(f => f.Bytes);
        FileCount = sourceFiles.Count(f => f.Copy) + archiveFiles.Count(f => !f.IsDirectory);
        RequiredBytes = checked(OriginalBytes + ArchiveBytes + RemixTrialService.SpaceReserveBytes + FileCount * 4096L);
        AvailableBytes = available;
        Warnings = warnings;
    }
    public string SourceFolder { get; }
    public string ArchivePath { get; }
    public string Destination { get; }
    public long OriginalBytes { get; }
    public long ArchiveBytes { get; }
    public long RequiredBytes { get; }
    public long AvailableBytes { get; }
    public int FileCount { get; }
    public IReadOnlyList<string> Warnings { get; }
    internal string Owner { get; }
    internal string Executable { get; }
    internal string ExecutableHash { get; }
    internal IReadOnlyList<RemixTrialService.SourceFile> Sources { get; }
    internal IReadOnlyList<RemixArchiveFile> ArchiveFiles { get; }
}
public sealed record RemixTrialResult(string Folder, string ExecutablePath, long Bytes, int FileCount, IReadOnlyList<string> Warnings);

/// <summary>Builds a separate, bounded local trial. Never writes to the source game or launches any image.</summary>
public sealed class RemixTrialService
{
    public const string WorkingExecutableSha256 = "3233ef990fe5ac6a554ec65b821e7a5231c955c3419fe7e910e11d336cce1471";
    public const string ClientSha256 = "79448dd4dbe82ae4df13839323cd51e860fab681833b004e30d204c299c40b95";
    public const long SpaceReserveBytes = 256L * 1024 * 1024;
    public const int MaximumSourceFiles = 50000;
    private const long MaximumSourceBytes = 128L * 1024 * 1024 * 1024, MaximumSourceFileBytes = 8L * 1024 * 1024 * 1024;
    private const string Wrapper = "Underground 2 Remix 0.4/", Marker = "GamePanel-RemixTrial.json", Ownership = ".gamepanel-remix-owner";
    internal sealed record Pin(long Bytes, string Sha256, string Md5, string ExecutableSha256, string ClientSha256);
    internal sealed record SourceFile(string RelativePath, long Bytes, string Sha256, bool Copy);
    private readonly Pin _pin;
    private readonly string _store, _owner = Guid.NewGuid().ToString("N");
    private readonly Func<string, bool> _running;
    private readonly IReadOnlyDictionary<string, (long Bytes, string Hash)> _wide;
    public RemixTrialService(string? profileStore = null, Func<string, bool>? running = null)
        : this(new(RemixArchiveAudit.PublishedArchiveBytes, RemixComponentBundle.SourceSha256,
            RemixArchiveAudit.PublishedMd5, WorkingExecutableSha256, ClientSha256),
            profileStore ?? ReShadeProfileService.DefaultStore, running ?? IsGameRunning, WidescreenCatalog.Entries) { }
    internal RemixTrialService(Pin pin, string profileStore, Func<string, bool> running,
        IReadOnlyDictionary<string, (long Bytes, string Hash)> knownWide)
    { _pin = pin; _store = Path.GetFullPath(profileStore); _running = running; _wide = knownWide; }

    public RemixTrialPlan Prepare(GameReport report, string archivePath, string destination, CancellationToken token = default,
        IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(report);
        var executable = Path.GetFullPath(ExecutablePath.Normalize(report.ExecutablePath));
        var source = Path.GetDirectoryName(executable)!; archivePath = Path.GetFullPath(archivePath); destination = Path.GetFullPath(destination);
        CheckExecutable(executable, report.Sha256, token);
        if (report.HasAntiCheatIndicators) throw Invalid("Indicatori anti-cheat: prova Remix sospesa.");
        CheckDestination(source, archivePath, destination); CheckWide(executable, report.Sha256, token);
        var sources = ScanSource(source, token, progress);
        CheckOriginalPlugins(executable, sources, token);
        var audit = Audit(archivePath, token, progress); var members = SelectMembers(audit);
        // Reserve the metadata paths and prevent the archive from supplying another game executable or loader.
        if (sources.Any(f => f.RelativePath.Equals(Marker, StringComparison.OrdinalIgnoreCase) || f.RelativePath.Equals(Ownership, StringComparison.OrdinalIgnoreCase)))
            throw Invalid("La sorgente contiene già metadati di una prova Remix.");
        var original = sources.Where(f => f.Copy).Sum(f => f.Bytes); var expanded = members.Sum(f => f.Bytes);
        var required = checked(original + expanded + SpaceReserveBytes + (sources.Count(f => f.Copy) + members.Count) * 4096L); var available = CheckSpace(destination, required);
        IReadOnlyList<string> warnings = Array.AsReadOnly(new[]
        {
            "Prova sperimentale separata: compatibilità e resa visiva sul CD EXE ancora da verificare nel gioco.",
            "AntiCull escluso: richiede AEP 0x35BCC7, diverso da 0x53309E dell'EXE funzionante. Possibili oggetti mancanti o problemi di visibilità.",
            "Conservati EXE CD, loader, Widescreen Fix e dati/modelli presenti (anche pack Smol): questi modelli possono non corrispondere ai mesh hash del profilo autore vanilla. Compatibilità non verificata; nessun ripristino automatico dei dati originali.",
            "Esclusi ReShade, Texture Toolkit, TexWizard e i plugin scripts dell'archivio. Restano necessari il disco e i requisiti del gioco CD originale.",
            "Free camera disattivata in dxvk.conf; rtx.conf dell'autore conservato. DLSS e Ray Reconstruction non verificati; Frame Generation disabilitata nel preset dell'autore.",
            "La copia può condividere il profilo e i salvataggi globali in Documenti/registro con il gioco originale. Nessun salvataggio esterno viene copiato o isolato.",
            "Nessun avvio automatico. Hash della copia locale dell'utente: non sono una firma indipendente né una garanzia di sicurezza. Licenze dell'autore conservate."
        });
        return new(_owner, source, executable, report.Sha256, archivePath, destination,
            Array.AsReadOnly(sources.ToArray()), Array.AsReadOnly(members.ToArray()), available, warnings);
    }

    public RemixTrialResult Create(RemixTrialPlan plan, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan); token.ThrowIfCancellationRequested();
        if (plan.Owner != _owner) throw Invalid("Anteprima non appartenente a questa sessione: preparala di nuovo.");
        CheckExecutable(plan.Executable, plan.ExecutableHash, token); CheckWide(plan.Executable, plan.ExecutableHash, token);
        CheckDestination(plan.SourceFolder, plan.ArchivePath, plan.Destination); CheckSpace(plan.Destination, plan.RequiredBytes);
        var parent = Path.GetDirectoryName(plan.Destination)!;
        var staging = Path.Combine(parent, ".gamepanel-remix-" + Guid.NewGuid().ToString("N"));
        var ownership = Guid.NewGuid().ToString("N"); bool owned = false;
        try
        {
            // Windows denies archive writes/replacement while this handle remains open; hash again after extraction on every OS.
            using var archiveInput = new FileStream(plan.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
            var audit = Audit(plan.ArchivePath, token, progress);
            if (!SelectMembers(audit).SequenceEqual(plan.ArchiveFiles)) throw Invalid("Tabella archivio cambiata: riprepara la prova.");
            VerifySourceSet(plan, token, progress);
            GuardPath(staging); if (AttributesOrMissing(staging) is not null) throw Invalid("Cartella temporanea già presente.");
            Directory.CreateDirectory(staging); GuardPath(staging);
            File.WriteAllText(Path.Combine(staging, Ownership), ownership); owned = true;
            var final = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase); int copied = 0;
            foreach (var file in plan.Sources.Where(f => f.Copy))
            {
                token.ThrowIfCancellationRequested(); var output = GameFile(staging, file.RelativePath); MakeParent(output);
                using var input = OpenRegular(GameFile(plan.SourceFolder, file.RelativePath));
                using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var actual = Transfer(input, target, file.Bytes, token);
                    if (actual.Bytes != file.Bytes || !Same(actual.Sha256, file.Sha256)) throw Invalid("File originale cambiato: " + file.RelativePath);
                    target.Flush(true);
                }
                if (!Same(Digest(output, file.Bytes, token).Sha256, file.Sha256)) throw Invalid("Verifica copia fallita: " + file.RelativePath);
                final.Add(file.RelativePath, file);
                progress?.Report(new("Copia gioco per Remix: " + file.RelativePath, ++copied, plan.FileCount));
            }
            Extract(archiveInput, plan, staging, final, token, progress, ref copied);
            var archiveHashes = plan.ArchiveFiles.Select(f => final[f.RelativePath]).ToArray();
            var config = GameFile(staging, "dxvk.conf");
            if (new FileInfo(config).Length > 256 * 1024) throw Invalid("dxvk.conf fuori limite.");
            var lines = File.ReadAllLines(config); int cameraKeys = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim(); if (line.StartsWith('#') || line.StartsWith(';')) continue;
                var equals = line.IndexOf('='); if (equals < 0 || line[..equals].Trim() != "rtx.useFreeCamera") continue;
                if (!bool.TryParse(line[(equals + 1)..].Trim(), out _)) throw Invalid("Valore free camera non riconosciuto.");
                lines[i] = "rtx.useFreeCamera = false"; cameraKeys++;
            }
            if (cameraKeys != 1) throw Invalid("Chiave attiva free camera assente o duplicata: nessuna interpretazione automatica.");
            File.WriteAllText(config, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            var changed = Digest(config, 256 * 1024, token); final["dxvk.conf"] = new("dxvk.conf", changed.Bytes, changed.Sha256, true);
            VerifyArchive(archiveInput, token); VerifySourceSet(plan, token, progress);
            CheckExecutable(plan.Executable, plan.ExecutableHash, token); CheckWide(plan.Executable, plan.ExecutableHash, token);
            var metadata = GameFile(staging, Marker);
            using (var output = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, new { SchemaVersion = 1, Purpose = "Experimental isolated local Remix trial; never auto-launched",
                    SourceFolder = plan.SourceFolder, SourceExecutableSha256 = plan.ExecutableHash,
                    ArchiveSha256 = _pin.Sha256, ArchiveBytes = _pin.Bytes, AntiCullIncluded = false,
                    SourceFiles = plan.Sources, ArchiveFiles = archiveHashes,
                    Files = final.Values.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray(), plan.Warnings },
                    new JsonSerializerOptions { WriteIndented = true }); output.Flush(true);
            }
            ValidateStaging(staging, final, ownership, token);
            token.ThrowIfCancellationRequested(); CheckDestination(plan.SourceFolder, plan.ArchivePath, plan.Destination);
            if (Directory.Exists(plan.Destination)) Directory.Delete(plan.Destination, recursive: false); // validated empty; never recursive
            GuardPath(staging); GuardPath(plan.Destination); Directory.Move(staging, plan.Destination); owned = false;
            return new(plan.Destination, Path.Combine(plan.Destination, Path.GetFileName(plan.Executable)),
                final.Values.Sum(f => f.Bytes) + new FileInfo(Path.Combine(plan.Destination, Marker)).Length + ownership.Length,
                final.Count + 2, plan.Warnings);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is not InvalidDataException && (e is IOException or ArgumentException or InvalidOperationException
            or OverflowException or NotSupportedException || e.GetType().Assembly == typeof(SevenZipArchive).Assembly))
        { throw new InvalidDataException("Creazione della copia Remix interrotta; il gioco originale non è stato modificato.", e); }
        finally { if (owned) TryCleanOwned(staging, ownership); }
    }

    private void CheckExecutable(string executable, string expected, CancellationToken token)
    {
        if (_running(executable)) throw Invalid("Chiudi il gioco prima di preparare o creare la copia Remix.");
        if (!Path.GetFileName(executable).Equals("speed2.exe", StringComparison.OrdinalIgnoreCase) || !Same(expected, _pin.ExecutableSha256)
            || !Same(Digest(executable, 64 * 1024 * 1024, token).Sha256, expected)) throw Invalid("EXE diverso dal CD EXE funzionante verificato: rianalizza il gioco.");
        var pe = PeInspector.Read(executable);
        if (pe.IsDll || pe.Architecture != "x86 (32 bit)" || !pe.Imports.Contains("d3d9.dll", StringComparer.OrdinalIgnoreCase))
            throw Invalid("La prova richiede speed2.exe x86 Direct3D 9.");
    }
    private void CheckWide(string executable, string hash, CancellationToken token)
    {
        var status = ReShadeProfileService.ForWidescreen(_store, _running).Status(executable); var state = status.State;
        if (status.Pending || state?.Installed != true || !Same(state.ExecutableSha256, hash)) throw Invalid("Serve il Widescreen Fix funzionante gestito da GamePanel, senza operazioni pendenti.");
        var root = Path.GetDirectoryName(executable)!;
        foreach (var path in WidescreenCatalog.Targets)
        {
            var bound = state.Files.SingleOrDefault(f => f.Path == path) ?? throw Invalid("Stato Widescreen incompleto.");
            var data = Digest(GameFile(root, path), path == WidescreenCatalog.Ini ? 256 * 1024 : MaximumSourceFileBytes, token);
            if (path != WidescreenCatalog.Ini && (!_wide.TryGetValue(path, out var pin) || data.Bytes != pin.Bytes
                || !Same(data.Sha256, pin.Hash) || !Same(data.Sha256, bound.Sha256))) throw Invalid("Componente Widescreen cambiato: " + path);
        }
        if (new ReShadeProfileService(_store, _running).Status(executable).Pending
            || new AssetProfileService(_store, _running).Status(executable).Pending
            || new DirectXTextureProfileService(_store, _running).Status(executable).Pending)
            throw Invalid("Completa o annulla le operazioni GamePanel pendenti prima della copia.");
    }
    private List<SourceFile> ScanSource(string root, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var files = new List<SourceFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(); queue.Enqueue(root); long total = 0; int entries = 0;
        while (queue.Count > 0)
        {
            var folder = queue.Dequeue(); GuardPath(folder);
            foreach (var item in Directory.EnumerateFileSystemEntries(folder))
            {
                token.ThrowIfCancellationRequested(); GuardPath(item); var attr = File.GetAttributes(item);
                if (++entries > MaximumSourceFiles * 2 || (attr & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw Invalid("Troppi elementi, link o file speciali nel gioco originale.");
                var relative = ValidateRelative(Path.GetRelativePath(root, item).Replace('\\', '/'));
                if (!names.Add(relative)) throw Invalid("Percorsi originali ambigui o duplicati.");
                if (relative.Equals(".trex", StringComparison.OrdinalIgnoreCase) || relative.Equals("rtx-remix", StringComparison.OrdinalIgnoreCase)
                    || relative.Equals("rtx.conf", StringComparison.OrdinalIgnoreCase) || relative.Equals("dxvk.conf", StringComparison.OrdinalIgnoreCase))
                    throw Invalid("La sorgente contiene già un renderer Remix/DXVK: scegli il gioco funzionante originale.");
                if ((attr & FileAttributes.Directory) != 0) { queue.Enqueue(item); continue; }
                if (files.Count >= MaximumSourceFiles) throw Invalid("Oltre 50.000 file nel gioco originale.");
                var data = Digest(item, MaximumSourceFileBytes, token); total = checked(total + data.Bytes);
                if (total > MaximumSourceBytes) throw Invalid("Gioco originale oltre il limite di 128 GiB.");
                files.Add(new(relative, data.Bytes, data.Sha256, !SkipOriginal(relative)));
                progress?.Report(new("Verifica sorgente Remix: " + relative, files.Count, MaximumSourceFiles));
            }
        }
        files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath)); return files;
    }
    private void CheckOriginalPlugins(string executable, IReadOnlyList<SourceFile> files, CancellationToken token)
    {
        foreach (var file in files)
        {
            var path = file.RelativePath;
            if (path.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
            {
                var expected = path.Equals(WidescreenCatalog.Plugin, StringComparison.OrdinalIgnoreCase) ? _wide[WidescreenCatalog.Plugin].Hash
                    : path.Equals(DirectXTextureCatalog.LoaderRelativePath, StringComparison.OrdinalIgnoreCase) ? DirectXTextureCatalog.LoaderSha256
                    : path.Equals(TextureRuntimeCatalog.Loader, StringComparison.OrdinalIgnoreCase) ? TextureRuntimeCatalog.LoaderSha256 : null;
                if (expected is null || !Same(file.Sha256, expected)) throw Invalid("Plugin ASI originale non identificato: " + path);
            }
            if (path.Contains('/')) continue;
            if (path.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase))
            {
                var status = new ReShadeProfileService(_store, _running).Status(executable);
                if (status.Pending || status.State?.Installed != true || !Same(status.State.ExecutableSha256, _pin.ExecutableSha256)
                    || !Same(file.Sha256, ReShadeCatalog.DllSha256)
                    || status.State.Files.All(f => !f.Path.Equals(path, StringComparison.OrdinalIgnoreCase) || !Same(f.Sha256, file.Sha256)))
                    throw Invalid("d3d9.dll originale non corrisponde al ReShade gestito e verificato.");
            }
            else if (new[] { "dxgi.dll", "d3d8.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "ddraw.dll", "dinput.dll", "dsound.dll", "version.dll", "winmm.dll", "opengl32.dll", "xinput1_3.dll", "xinput1_4.dll" }.Contains(path, StringComparer.OrdinalIgnoreCase))
                throw Invalid("Proxy originale non identificato: " + path);
        }
        token.ThrowIfCancellationRequested();
    }
    private static bool SkipOriginal(string p) => p.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)
        || p.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase) || p.Equals("ReShade.log", StringComparison.OrdinalIgnoreCase)
        || p.Equals("ReShadePreset.ini", StringComparison.OrdinalIgnoreCase)
        || p.Equals(SdrProfile.PresetPath, StringComparison.OrdinalIgnoreCase) || p.StartsWith("GamePanelFX/", StringComparison.OrdinalIgnoreCase)
        || p.StartsWith("TexturePacks/", StringComparison.OrdinalIgnoreCase)
        || p.Equals(DirectXTextureCatalog.LoaderRelativePath, StringComparison.OrdinalIgnoreCase)
        || p.Equals(DirectXTextureProfileService.ConfigRelativePath, StringComparison.OrdinalIgnoreCase)
        || p.Equals(TextureRuntimeCatalog.Loader, StringComparison.OrdinalIgnoreCase) || p.Equals("scripts/TexWizard.json", StringComparison.OrdinalIgnoreCase);
    private RemixArchiveReport Audit(string path, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var audit = new RemixArchiveAudit(new(_pin.Bytes, _pin.Md5)).Analyze(path, token, progress);
        if (!audit.MatchesPublishedSource || !Same(audit.Sha256, _pin.Sha256) || audit.InstallationRoot != Wrapper[..^1])
            throw Invalid("Archivio diverso dalla copia Remix 0.4 identificata o cartella autore non riconosciuta.");
        return audit;
    }
    private static List<RemixArchiveFile> SelectMembers(RemixArchiveReport audit)
    {
        var selected = new List<RemixArchiveFile>();
        foreach (var file in audit.Files)
        {
            if (file.IsDirectory) continue;
            if (!file.RelativePath.StartsWith(Wrapper, StringComparison.Ordinal)) throw Invalid("Membro fuori dalla cartella autore.");
            var path = ValidateRelative(file.RelativePath[Wrapper.Length..]);
            if (path.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) || path.Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (path.Equals("speed2.exe", StringComparison.OrdinalIgnoreCase) || path.Equals(Marker, StringComparison.OrdinalIgnoreCase)
                || path.Equals(Ownership, StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
                throw Invalid("L'archivio tenta di sostituire EXE, metadati o plugin non previsti.");
            selected.Add(file with { RelativePath = path });
        }
        foreach (var required in new[] { "d3d9.dll", "dxvk.conf", "rtx.conf" })
            if (selected.All(f => !f.RelativePath.Equals(required, StringComparison.Ordinal))) throw Invalid("Componente Remix obbligatorio mancante: " + required);
        return selected;
    }
    private void Extract(Stream source, RemixTrialPlan plan, string staging, Dictionary<string, SourceFile> final,
        CancellationToken token, IProgress<BackupProgress>? progress, ref int copied)
    {
        source.Position = 0; var expected = plan.ArchiveFiles.ToDictionary(f => Wrapper + f.RelativePath, StringComparer.Ordinal);
        using var cancelable = new CancelableStream(source, token);
        using var archive = (SevenZipArchive)SevenZipArchive.OpenArchive(cancelable, new ReaderOptions { LeaveStreamOpen = true, LookForHeader = false });
        using var reader = archive.ExtractAllEntries(); var visited = new HashSet<string>(StringComparer.Ordinal); int entries = 0; long total = 0;
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested(); if (++entries > RemixArchiveAudit.MaximumEntries) throw Invalid("Troppe voci durante l'estrazione.");
            var name = (reader.Entry.Key ?? "").Replace('\\', '/'); if (!expected.TryGetValue(name, out var member)) continue;
            if (!visited.Add(name) || reader.Entry.IsDirectory || reader.Entry.Size != member.Bytes) throw Invalid("Membro Remix cambiato.");
            var path = GameFile(staging, member.RelativePath); MakeParent(path);
            if (File.Exists(path)) { GuardPath(path); File.Delete(path); } // only an owned staging copy can be replaced
            using var data = reader.OpenEntryStream(); (string Sha256, long Bytes) result;
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { result = Transfer(data, output, member.Bytes, token); output.Flush(true); }
            total = checked(total + result.Bytes);
            if (result.Bytes != member.Bytes || total > plan.ArchiveBytes || !Same(Digest(path, member.Bytes, token).Sha256, result.Sha256)) throw Invalid("Membro Remix troncato o oltre limite.");
            if (member.RelativePath == "d3d9.dll" && !Same(result.Sha256, _pin.ClientSha256)) throw Invalid("Client Remix diverso dall'audit statico.");
            final[member.RelativePath] = new(member.RelativePath, result.Bytes, result.Sha256, true);
            progress?.Report(new("Estrazione copia Remix: " + member.RelativePath, ++copied, plan.FileCount));
        }
        if (visited.Count != expected.Count || total != plan.ArchiveBytes) throw Invalid("Estrazione Remix incompleta.");
    }
    private void VerifySourceSet(RemixTrialPlan plan, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        if (!ScanSource(plan.SourceFolder, token, progress).SequenceEqual(plan.Sources)) throw Invalid("Il gioco originale è cambiato dall'anteprima: preparala di nuovo.");
    }
    private void VerifyArchive(Stream source, CancellationToken token)
    {
        source.Position = 0; var data = Transfer(source, null, _pin.Bytes, token);
        if (data.Bytes != _pin.Bytes || !Same(data.Sha256, _pin.Sha256)) throw Invalid("Archivio cambiato durante la creazione.");
    }
    private void CheckDestination(string source, string archive, string destination)
    {
        GuardPath(source); GuardPath(archive); GuardPath(destination); GuardPath(_store);
        if (Overlap(source, destination) || Overlap(archive, destination) || Overlap(_store, destination)) throw Invalid("La copia deve essere esterna al gioco, all'archivio e ai profili GamePanel.");
        var forbidden = new[] { GamePanelPaths.Root, AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        var blocked = forbidden.Where(p => !string.IsNullOrWhiteSpace(p)).FirstOrDefault(p => Overlap(Path.GetFullPath(p), destination));
        if (blocked is not null)
            throw Invalid("Destinazione in conflitto con la cartella protetta: " + blocked + ". Scegli una cartella esterna. Avvia GamePanel da una sua cartella dedicata, non dalla radice del disco.");
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) throw Invalid("La cartella che conterrà la copia deve esistere.");
        if (AttributesOrMissing(destination) is FileAttributes a && ((a & FileAttributes.Directory) == 0 || Directory.EnumerateFileSystemEntries(destination).Any()))
            throw Invalid("Scegli una cartella nuova o completamente vuota: nessun contenuto esistente viene sostituito.");
    }
    private static long CheckSpace(string destination, long required)
    {
        var volume = new DriveInfo(Path.GetPathRoot(destination)!);
        if (!volume.IsReady || volume.AvailableFreeSpace < required) throw Invalid("Spazio insufficiente per la copia, i contenuti Remix e 256 MiB di riserva.");
        return volume.AvailableFreeSpace;
    }
    private static bool Overlap(string a, string b) => Inside(a, b) || Inside(b, a);
    private static void MakeParent(string path) { var parent = Path.GetDirectoryName(path)!; GuardPath(parent); Directory.CreateDirectory(parent); GuardPath(parent); }
    private static FileStream OpenRegular(string path)
    {
        GuardPath(path); var attr = File.GetAttributes(path);
        if ((attr & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0) throw Invalid("File regolare richiesto.");
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
    }
    private static (string Sha256, long Bytes) Digest(string path, long limit, CancellationToken token)
    { using var input = OpenRegular(path); return Transfer(input, null, limit, token); }
    private static (string Sha256, long Bytes) Transfer(Stream input, Stream? output, long limit, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1024 * 1024]; long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = input.Read(buffer); if (count == 0) break;
            total = checked(total + count); if (total > limit) throw Invalid("File oltre la dimensione autorizzata.");
            hash.AppendData(buffer, 0, count); output?.Write(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested(); return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
    }
    private static void ValidateStaging(string staging, IReadOnlyDictionary<string, SourceFile> expected, string ownership, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var queue = new Queue<string>(); queue.Enqueue(staging); int entries = 0;
        while (queue.Count > 0)
            foreach (var path in Directory.EnumerateFileSystemEntries(queue.Dequeue()))
            {
                token.ThrowIfCancellationRequested(); GuardPath(path); var attr = File.GetAttributes(path);
                if (++entries > MaximumSourceFiles * 3 + RemixArchiveAudit.MaximumEntries || (attr & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw Invalid("Link, file speciali o elementi inattesi nella copia temporanea.");
                if ((attr & FileAttributes.Directory) != 0) { queue.Enqueue(path); continue; }
                var relative = ValidateRelative(Path.GetRelativePath(staging, path).Replace('\\', '/'));
                if (!seen.Add(relative)) throw Invalid("Percorsi duplicati nella copia temporanea.");
                if (relative == Marker) continue;
                if (relative == Ownership) { if (new FileInfo(path).Length != ownership.Length || File.ReadAllText(path) != ownership) throw Invalid("Proprietà staging cambiata."); continue; }
                if (!expected.TryGetValue(relative, out var file) || Digest(path, file.Bytes, token) is var actual
                    && (actual.Bytes != file.Bytes || !Same(actual.Sha256, file.Sha256))) throw Invalid("Copia temporanea cambiata: " + relative);
            }
        if (seen.Count != expected.Count + 2 || !seen.Contains(Marker) || !seen.Contains(Ownership)) throw Invalid("Copia temporanea incompleta.");
    }
    private static void TryCleanOwned(string staging, string ownership)
    {
        try
        {
            GuardPath(staging); var marker = GameFile(staging, Ownership);
            if (!Directory.Exists(staging) || !File.Exists(marker) || new FileInfo(marker).Length != ownership.Length || File.ReadAllText(marker) != ownership) return;
            var files = new List<string>(); var dirs = new List<string>(); var pending = new Queue<string>(); pending.Enqueue(staging);
            while (pending.Count > 0)
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(pending.Dequeue()))
                {
                    GuardPath(path); var attr = File.GetAttributes(path);
                    if ((attr & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 || files.Count + dirs.Count > MaximumSourceFiles * 3 + RemixArchiveAudit.MaximumEntries) return;
                    if ((attr & FileAttributes.Directory) != 0) { dirs.Add(path); pending.Enqueue(path); } else files.Add(path);
                }
            }
            foreach (var path in files.Where(p => p != marker)) { GuardPath(path); File.Delete(path); }
            foreach (var dir in dirs.OrderByDescending(p => p.Length)) { GuardPath(dir); Directory.Delete(dir, false); }
            GuardPath(marker); if (File.ReadAllText(marker) != ownership) return; File.Delete(marker); GuardPath(staging); Directory.Delete(staging, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { /* Refuse cleanup if ownership/path safety cannot be proven. */ }
    }
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static InvalidDataException Invalid(string message) => new(message);
    private sealed class CancelableStream(Stream inner, CancellationToken token) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { token.ThrowIfCancellationRequested(); inner.Position = value; } }
        public override int Read(byte[] buffer, int offset, int count) { token.ThrowIfCancellationRequested(); return inner.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { token.ThrowIfCancellationRequested(); return inner.Read(buffer); }
        public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return inner.Seek(offset, origin); }
        public override void Flush() => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
