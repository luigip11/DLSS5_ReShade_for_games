using System.Security.Cryptography;
using System.Text;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record ProfileFile(string Path, string Sha256, long Bytes);
public sealed record ProfileState(int SchemaVersion, string Revision, string ExecutablePath, string ExecutableSha256,
    string Version, bool Installed, SdrSettings Settings, IReadOnlyList<ProfileFile> Files, IReadOnlyList<string> PreservedFiles);
public sealed record ProfileChange(string Path, string? BeforeSha256, string? AfterSha256);
public sealed record ProfileStatus(ProfileState? State, bool Pending, string Message);
public sealed class ProfilePlan
{
    internal ProfilePlan(GameReport report, ProfileState? previous, ProfileState next,
        IReadOnlyList<ProfileChange> changes, IReadOnlyDictionary<string, byte[]> payloads)
    { Report = report; Previous = previous; Next = next; Changes = changes; Payloads = payloads; }
    internal GameReport Report { get; }
    internal ProfileState? Previous { get; }
    internal ProfileState Next { get; }
    internal IReadOnlyDictionary<string, byte[]> Payloads { get; }
    public IReadOnlyList<ProfileChange> Changes { get; }
    public IReadOnlyList<string> PreservedFiles => Next.PreservedFiles;
    public bool Removing => !Next.Installed;
}

/// <summary>Only fixed ReShade SDR paths, never the game EXE or existing third-party loaders.
/// A durable write-ahead journal holds both byte versions before any game-file change.</summary>
public sealed class ReShadeProfileService
{
    private readonly string _store;
    private readonly Func<string, bool> _running;
    private sealed record Operation(int SchemaVersion, string Id, string ExecutablePath, string ExecutableSha256,
        string Status, ProfileState? Previous, ProfileState Next, IReadOnlyList<ProfileChange> Changes);
    public static string DefaultStore => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePanel", "Profiles");
    public ReShadeProfileService(string store, Func<string, bool>? running = null)
    { _store = Path.GetFullPath(store); _running = running ?? IsGameRunning; }

    public static string? Eligibility(GameReport report)
    {
        if (!Path.GetFileName(report.ExecutablePath).Equals("speed2.exe", StringComparison.OrdinalIgnoreCase))
            return "Alpha.5: profilo sperimentale riservato a speed2.exe di Underground 2.";
        if (report.Architecture != "x86 (32 bit)" || !report.GraphicsApis.Contains("Direct3D 9"))
            return "Il profilo richiede un EXE x86 con import Direct3D 9. Nessun wrapper viene installato.";
        if (report.HasAntiCheatIndicators || report.ScanWasLimited)
            return "Scansione incompleta o indicatori anti-cheat: installazione sospesa.";
        return null;
    }

    public ProfileStatus Status(string executablePath)
    {
        var executable = Full(executablePath); var folder = Folder(executable);
        if (!Directory.Exists(folder)) return new(null, false, "Nessun profilo installato da GamePanel.");
        var operation = LoadOperation(folder, executable);
        var state = LoadState(folder, executable);
        return new(state, operation?.Status == "Pending", operation?.Status == "Pending"
            ? "Operazione interrotta: riprendila o annullala prima di modificare il profilo."
            : state?.Installed == true ? "ReShade installato da GamePanel · collaudo nel gioco ancora necessario."
            : "Profilo rimosso." + (state?.PreservedFiles.Count > 0 ? " Conservati file cambiati fuori da GamePanel: " + string.Join(", ", state.PreservedFiles) : ""));
    }

    public ProfilePlan PrepareApply(GameReport report, VerifiedReShade package, SdrSettings settings, CancellationToken token = default)
    {
        settings.Validate(); CheckReport(report, token);
        var executable = Full(report.ExecutablePath); var root = Path.GetDirectoryName(executable)!;
        var folder = Folder(executable); CheckNoPending(folder, executable);
        var previous = LoadState(folder, executable);
        if (previous is not null && previous.ExecutableSha256 != report.Sha256) throw new InvalidDataException("L'EXE è cambiato dopo la gestione del profilo: verifica il gioco prima di procedere.");
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var changes = new List<ProfileChange>();
        IReadOnlyList<ProfileFile> nextFiles;
        if (previous?.Installed == true)
        {
            VerifyInstalled(root, previous, token, allowRuntimeConfig: true);
            var preset = previous.Files.Single(f => f.Path == SdrProfile.PresetPath);
            var bytes = SdrProfile.Preset(settings); var hash = Hash(bytes);
            if (hash != preset.Sha256) { changes.Add(new(preset.Path, preset.Sha256, hash)); payloads.Add(preset.Path, bytes); }
            nextFiles = previous.Files.Select(f => f.Path == preset.Path ? new ProfileFile(f.Path, hash, bytes.Length) : f).ToArray();
        }
        else
        {
            CheckOtherLoaders(root);
            if (AttributesOrMissing(GameFile(root, "GamePanelFX")) is not null)
                throw new InvalidDataException("La cartella GamePanelFX è già presente. Conserva i suoi contenuti prima di procedere.");
            foreach (var path in SdrProfile.Targets)
                if (AttributesOrMissing(GameFile(root, path)) is not null) throw new InvalidDataException($"File già presente: {path}. GamePanel non lo sovrascrive.");
            GuardPath(package.Path);
            if (HashFile(package.Path, token) != package.Sha256) throw new InvalidDataException("Il componente ReShade verificato è cambiato.");
            payloads.Add(SdrProfile.LoaderPath, File.ReadAllBytes(package.Path));
            payloads.Add(SdrProfile.ConfigPath, SdrProfile.Configuration);
            payloads.Add(SdrProfile.PresetPath, SdrProfile.Preset(settings));
            payloads.Add(SdrProfile.ShaderPath, Encoding.UTF8.GetBytes(SdrProfile.Shader));
            nextFiles = payloads.Select(p => new ProfileFile(p.Key, Hash(p.Value), p.Value.LongLength)).ToArray();
            changes.AddRange(nextFiles.Select(f => new ProfileChange(f.Path, null, f.Sha256)));
        }
        var next = new ProfileState(1, Guid.NewGuid().ToString("N"), executable, report.Sha256,
            ReShadeCatalog.Version, true, settings, nextFiles, []);
        return new(report, previous, next, changes, payloads);
    }

    public ProfilePlan PrepareRemove(GameReport report, CancellationToken token = default)
    {
        CheckIdentity(report, token); var executable = Full(report.ExecutablePath);
        var folder = Folder(executable); CheckNoPending(folder, executable);
        var previous = LoadState(folder, executable);
        if (previous?.Installed != true) throw new InvalidOperationException("Nessuna installazione GamePanel da rimuovere.");
        if (previous.ExecutableSha256 != report.Sha256) throw new InvalidDataException("L'EXE è cambiato: rimozione sospesa.");
        var root = Path.GetDirectoryName(executable)!;
        var changes = new List<ProfileChange>(); var preserved = new List<string>();
        foreach (var file in previous.Files)
        {
            var hash = LiveHash(root, file.Path, token);
            if (hash is null) continue;
            if (hash == file.Sha256) changes.Add(new(file.Path, hash, null));
            else if (file.Path == SdrProfile.ConfigPath || file.Path == SdrProfile.PresetPath) preserved.Add(file.Path);
            else throw new InvalidDataException($"File modificato esternamente: {file.Path}. Rimozione bloccata.");
        }
        var next = previous with { Revision = Guid.NewGuid().ToString("N"), Installed = false, Files = [], PreservedFiles = preserved };
        return new(report, previous, next, changes, new Dictionary<string, byte[]>());
    }

    public ProfileState Execute(ProfilePlan plan, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var report = plan.Report;
        if (plan.Removing) CheckIdentity(report, token); else CheckReport(report, token);
        var folder = Folder(report.ExecutablePath); GuardPath(folder); Directory.CreateDirectory(folder); GuardPath(folder);
        using var lease = Acquire(folder);
        CheckNoPending(folder, report.ExecutablePath);
        var state = LoadState(folder, report.ExecutablePath);
        if (state?.Revision != plan.Previous?.Revision) throw new InvalidDataException("Stato del profilo cambiato: ripeti l'anteprima.");
        if (!plan.Removing && state?.Installed != true) CheckOtherLoaders(Path.GetDirectoryName(report.ExecutablePath)!);
        ValidateChanges(plan.Changes);
        foreach (var c in plan.Changes)
            if (LiveHash(Path.GetDirectoryName(report.ExecutablePath)!, c.Path, token) != c.BeforeSha256)
                throw new InvalidDataException($"File cambiato dopo l'anteprima: {c.Path}. Nessuna modifica avviata.");
        if (!plan.Removing && state?.Installed == true) VerifyInstalled(Path.GetDirectoryName(report.ExecutablePath)!, state, token, true);
        // Both versions are durable before the journal authorizes a single game-file write.
        var id = Guid.NewGuid().ToString("N"); var payloadFolder = Path.Combine(folder, id);
        Directory.CreateDirectory(payloadFolder);
        for (var i = 0; i < plan.Changes.Count; i++)
        {
            var change = plan.Changes[i]; token.ThrowIfCancellationRequested();
            if (change.BeforeSha256 is not null)
            {
                var copied = CopyVerified(GameFile(Path.GetDirectoryName(report.ExecutablePath)!, change.Path), Payload(payloadFolder, i, false), token, MaximumFileBytes);
                if (copied.Hash != change.BeforeSha256) throw new InvalidDataException("Il file è cambiato durante la copia di sicurezza.");
            }
            if (change.AfterSha256 is not null)
            {
                var bytes = plan.Payloads[change.Path];
                if (Hash(bytes) != change.AfterSha256) throw new InvalidDataException("Payload del profilo non valido.");
                DurableBytes(Payload(payloadFolder, i, true), bytes);
            }
        }
        CheckIdentity(report, token);
        var operation = new Operation(1, id, report.ExecutablePath, report.Sha256, "Pending", state, plan.Next, plan.Changes);
        WriteJson(Path.Combine(folder, "operation.json"), operation);
        return Transition(folder, operation, false, token, progress)!;
    }

    public ProfileState? Recover(string executablePath, bool rollback, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var executable = Full(executablePath); var folder = Folder(executable);
        using var lease = Acquire(folder);
        var operation = LoadOperation(folder, executable);
        if (operation?.Status != "Pending") throw new InvalidOperationException("Nessuna operazione interrotta.");
        return Transition(folder, operation, rollback, token, progress);
    }

    private ProfileState? Transition(string folder, Operation operation, bool rollback, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var executable = operation.ExecutablePath; var root = Path.GetDirectoryName(executable)!;
        EnsureStopped(executable);
        if (HashFile(executable, token) != operation.ExecutableSha256) throw new InvalidDataException("EXE cambiato: recupero del profilo sospeso.");
        var payloadFolder = Path.Combine(folder, operation.Id); GuardPath(payloadFolder);
        // Validate all byte copies and live files before starting/resuming either direction.
        for (var i = 0; i < operation.Changes.Count; i++)
        {
            var c = operation.Changes[i];
            if (c.BeforeSha256 is not null && HashFile(Payload(payloadFolder, i, false), token) != c.BeforeSha256
                || c.AfterSha256 is not null && HashFile(Payload(payloadFolder, i, true), token) != c.AfterSha256)
                throw new InvalidDataException("Copie dell'operazione danneggiate: nessuna ulteriore modifica.");
            var live = LiveHash(root, c.Path, token);
            if (live != c.BeforeSha256 && live != c.AfterSha256) throw new InvalidDataException($"Conflitto durante il recupero: {c.Path}.");
        }
        for (var i = 0; i < operation.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); EnsureStopped(executable);
            var c = operation.Changes[i]; var desired = rollback ? c.BeforeSha256 : c.AfterSha256;
            var expected = rollback ? c.AfterSha256 : c.BeforeSha256;
            var destination = GameFile(root, c.Path); var live = LiveHash(root, c.Path, token);
            if (live != desired)
            {
                if (live != expected) throw new InvalidDataException($"File cambiato durante l'operazione: {c.Path}.");
                if (desired is null) File.Delete(destination);
                else
                {
                    var parent = Path.GetDirectoryName(destination)!; GuardPath(parent); Directory.CreateDirectory(parent); GuardPath(parent);
                    var temporary = Path.Combine(parent, $".gamepanel-{operation.Id}-{i:D4}.tmp"); GuardPath(temporary);
                    if (AttributesOrMissing(temporary) is not null && HashFile(temporary, token) != desired) File.Delete(temporary);
                    if (AttributesOrMissing(temporary) is null) CopyVerified(Payload(payloadFolder, i, !rollback), temporary, token, MaximumFileBytes);
                    if (HashFile(temporary, token) != desired) throw new InvalidDataException("Temporanea del profilo danneggiata.");
                    token.ThrowIfCancellationRequested(); EnsureStopped(executable); GuardPath(destination);
                    if (LiveHash(root, c.Path, token) != expected) throw new InvalidDataException("Destinazione cambiata durante la preparazione.");
                    File.Move(temporary, destination, overwrite: expected is not null);
                }
            }
            progress?.Report(new($"{(rollback ? "Annullamento" : "Profilo")}: {c.Path}", i + 1, operation.Changes.Count));
        }
        foreach (var c in operation.Changes)
            if (LiveHash(root, c.Path, token) != (rollback ? c.BeforeSha256 : c.AfterSha256)) throw new InvalidDataException("Verifica finale del profilo non riuscita.");
        for (var i = 0; i < operation.Changes.Count; i++)
        {
            var destination = GameFile(root, operation.Changes[i].Path);
            var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".gamepanel-{operation.Id}-{i:D4}.tmp");
            GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary);
        }
        var result = rollback ? operation.Previous : operation.Next;
        var statePath = Path.Combine(folder, "state.json"); GuardPath(statePath);
        if (result is null) { if (File.Exists(statePath)) File.Delete(statePath); }
        else WriteJson(statePath, result);
        WriteJson(Path.Combine(folder, "operation.json"), operation with { Status = rollback ? "RolledBack" : "Complete" });
        // Never recursively remove directories: runtime-generated or user files remain intact.
        var effects = GameFile(root, "GamePanelFX");
        if (Directory.Exists(effects) && !Directory.EnumerateFileSystemEntries(effects).Any()) Directory.Delete(effects);
        return result;
    }

    private void CheckReport(GameReport report, CancellationToken token)
    {
        CheckIdentity(report, token);
        var fresh = new GameInspector().Inspect(report.ExecutablePath, token);
        var reason = Eligibility(fresh);
        if (reason is not null) throw new InvalidDataException(reason);
        if (fresh.Sha256 != report.Sha256) throw new InvalidDataException("EXE cambiato: rianalizza il gioco.");
    }
    private void CheckIdentity(GameReport report, CancellationToken token)
    {
        if (!ValidHash(report.Sha256)) throw new InvalidDataException("Report del gioco non valido.");
        var executable = Full(report.ExecutablePath); _ = Folder(executable); EnsureStopped(executable);
        if (HashFile(executable, token) != report.Sha256) throw new InvalidDataException("EXE cambiato: rianalizza il gioco.");
    }
    private static void VerifyInstalled(string root, ProfileState state, CancellationToken token, bool allowRuntimeConfig)
    {
        foreach (var file in state.Files)
        {
            var actual = LiveHash(root, file.Path, token);
            if (actual != file.Sha256 && !(allowRuntimeConfig && file.Path == SdrProfile.ConfigPath && actual is not null))
                throw new InvalidDataException($"Componente gestito cambiato o mancante: {file.Path}. Non viene sovrascritto.");
        }
        ValidateRuntimeConfiguration(root);
        CheckOtherLoaders(root);
    }
    private static void ValidateRuntimeConfiguration(string root)
    {
        var path = GameFile(root, SdrProfile.ConfigPath);
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Configurazione ReShade oltre il limite.");
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim(); if (line.Length == 0 || line[0] is ';' or '/') continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var at = line.IndexOf('='); if (at < 1) continue;
            var key = line[..at].Trim(); var value = line[(at + 1)..].Trim();
            if (section.Equals("INSTALL", StringComparison.OrdinalIgnoreCase) && key.Equals("BasePath", StringComparison.OrdinalIgnoreCase) && value.Length != 0)
                throw new InvalidDataException("ReShade è stato reindirizzato a un'altra configurazione: aggiornamento sospeso.");
            if (!section.Equals("GENERAL", StringComparison.OrdinalIgnoreCase) || key is not ("PresetPath" or "StartupPresetPath" or "EffectSearchPaths")) continue;
            if (!keys.TryAdd(key, value)) throw new InvalidDataException("Percorsi duplicati nella configurazione ReShade.");
        }
        foreach (var (key, expected) in new[] { ("PresetPath", SdrProfile.PresetPath), ("StartupPresetPath", SdrProfile.PresetPath), ("EffectSearchPaths", "GamePanelFX") })
        {
            if (!keys.TryGetValue(key, out var value) || value.Contains(',') || value.Contains('*') || value.Contains('\0'))
                throw new InvalidDataException("La configurazione ReShade non usa più il profilo GamePanel: aggiornamento sospeso.");
            var actual = Path.GetFullPath(Path.Combine(root, value.Replace('\\', Path.DirectorySeparatorChar)));
            if (!SamePath(actual, GameFile(root, expected))) throw new InvalidDataException("Percorsi ReShade modificati esternamente: aggiornamento sospeso.");
        }
    }
    private static void CheckOtherLoaders(string root)
    {
        foreach (var name in new[] { "dxgi.dll", "d3d11.dll", "d3d12.dll", "ddraw.dll", "dxvk.conf", "dgVoodoo.conf" })
            if (AttributesOrMissing(GameFile(root, name)) is not null) throw new InvalidDataException($"Renderer o wrapper già presente: {name}. Questo percorso non è supportato da alpha.5.");
    }
    private void EnsureStopped(string executable)
    {
        bool active;
        try { active = _running(executable); }
        catch (Exception e) { throw new InvalidOperationException("Impossibile verificare che il gioco sia chiuso.", e); }
        if (active) throw new InvalidOperationException("Chiudi il gioco prima di modificare o recuperare il profilo.");
    }
    private string Folder(string executable)
    {
        var root = Path.GetDirectoryName(executable)!; GuardPath(root); GuardPath(_store);
        if (Inside(root, _store) || Inside(_store, root)) throw new InvalidDataException("Archivio profili e gioco devono trovarsi in cartelle separate.");
        var key = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var folder = Path.Combine(_store, Hash(Encoding.UTF8.GetBytes(key))); GuardPath(folder); return folder;
    }
    private static string Full(string path) => Path.GetFullPath(ExecutablePath.Normalize(path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string? LiveHash(string root, string path, CancellationToken token)
    {
        var full = GameFile(root, path);
        if (AttributesOrMissing(full) is null) return null;
        if (File.GetAttributes(full).HasFlag(FileAttributes.Directory)) throw new InvalidDataException($"Cartella al posto del file: {path}.");
        return HashFile(full, token);
    }
    private static FileStream Acquire(string folder)
    { GuardPath(folder); var path = Path.Combine(folder, ".operation.lock"); GuardPath(path); return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private static string Payload(string folder, int i, bool after)
    { var path = Path.Combine(folder, $"{i:D4}-{(after ? "after" : "before")}.bin"); GuardPath(path); return path; }
    private static void DurableBytes(string path, byte[] bytes)
    { GuardPath(path); using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(bytes); stream.Flush(true); }
    private static void CheckNoPending(string folder, string executable)
    { if (LoadOperation(folder, executable)?.Status == "Pending") throw new InvalidOperationException("Riprendi o annulla l'operazione interrotta prima di procedere."); }
    private static ProfileState? LoadState(string folder, string executable)
    {
        var path = Path.Combine(folder, "state.json"); GuardPath(path);
        if (!File.Exists(path)) return null;
        var state = ReadJson<ProfileState>(path); ValidateState(state, executable); return state;
    }
    private static void ValidateState(ProfileState s, string executable)
    {
        if (s is null || s.SchemaVersion != 1 || !ValidId(s.Revision) || !SamePath(s.ExecutablePath, executable) || !ValidHash(s.ExecutableSha256)
            || s.Version != ReShadeCatalog.Version || s.Files is null || s.PreservedFiles is null || s.Settings is null)
            throw new InvalidDataException("Stato del profilo non valido o legato a un altro eseguibile.");
        s.Settings.Validate();
        if (s.Installed ? s.Files.Count != SdrProfile.Targets.Count : s.Files.Count != 0) throw new InvalidDataException("Elenco componenti del profilo non valido.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in s.Files)
            if (f is null || !SdrProfile.Targets.Contains(f.Path) || !names.Add(f.Path) || !ValidHash(f.Sha256) || f.Bytes is < 1 or > 32 * 1024 * 1024)
                throw new InvalidDataException("Componente nel registro non valido.");
        if (s.PreservedFiles.Count > 2 || s.PreservedFiles.Any(p => p != SdrProfile.ConfigPath && p != SdrProfile.PresetPath)) throw new InvalidDataException("File conservati non validi.");
    }
    private static void ValidateChanges(IReadOnlyList<ProfileChange> changes)
    {
        if (changes is null || changes.Count > SdrProfile.Targets.Count) throw new InvalidDataException("Operazione del profilo non valida.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in changes)
            if (c is null || !SdrProfile.Targets.Contains(c.Path) || !names.Add(c.Path)
                || (c.BeforeSha256 is not null && !ValidHash(c.BeforeSha256)) || (c.AfterSha256 is not null && !ValidHash(c.AfterSha256))
                || c.BeforeSha256 == c.AfterSha256) throw new InvalidDataException("Percorsi o hash dell'operazione non validi.");
    }
    private static Operation? LoadOperation(string folder, string executable)
    {
        var path = Path.Combine(folder, "operation.json"); GuardPath(path);
        if (!File.Exists(path)) return null;
        var op = ReadJson<Operation>(path);
        if (op.SchemaVersion != 1 || !ValidId(op.Id) || !SamePath(op.ExecutablePath, executable) || !ValidHash(op.ExecutableSha256)
            || op.Status is not ("Pending" or "Complete" or "RolledBack") || op.Next is null) throw new InvalidDataException("Journal del profilo non valido.");
        ValidateState(op.Next, executable); if (op.Previous is not null) ValidateState(op.Previous, executable); ValidateChanges(op.Changes);
        if (op.Next.ExecutableSha256 != op.ExecutableSha256 || op.Previous is not null && op.Previous.ExecutableSha256 != op.ExecutableSha256)
            throw new InvalidDataException("Identità del journal non valida.");
        foreach (var c in op.Changes)
        {
            var before = op.Previous?.Files.SingleOrDefault(f => f.Path == c.Path)?.Sha256;
            var after = op.Next.Files.SingleOrDefault(f => f.Path == c.Path)?.Sha256;
            if (before != c.BeforeSha256 || after != c.AfterSha256)
                throw new InvalidDataException("Journal non coerente con le versioni del profilo.");
        }
        return op;
    }
    private static bool SamePath(string a, string b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
