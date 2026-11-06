using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record RemixDlssSettings(int? Upscaler = null, int? DlssMode = null, bool? RayReconstruction = null,
    bool? Transformer = null, bool? FrameGeneration = null, int? GeneratedFrames = null, int? Reflex = null);
public sealed record RemixManagedVariant(string OriginalExecutablePath, string RemixExecutablePath);
public sealed record RemixManagedStatus(bool Supported, bool Adopted, bool Pending, bool Changed, string Message,
    string? OriginalExecutablePath, RemixDlssSettings Settings, bool LocalInstallation = false,
    bool HasSuperResolution = false, bool HasRayReconstruction = false, bool HasFrameGeneration = false, bool HasTransformer = false);
public sealed record RemixManagedChange(string Path, string? BeforeSha256, string? AfterSha256);
public sealed record RemixSettingChange(string Path, string Key, string? Before, string? After);
public sealed class RemixManagedPlan
{
    internal RemixManagedPlan(string owner, string action, GameReport report, RemixManagedService.Identity identity,
        RemixManagedService.SavedState? previous, RemixManagedService.SavedState next,
        IReadOnlyList<RemixManagedService.ConfigFile> before, IReadOnlyList<RemixManagedService.ConfigFile> after)
    { Owner = owner; Action = action; Report = report; Identity = identity; Previous = previous; Next = next; Before = before; After = after;
        Changes = before.Select((b, i) => new RemixManagedChange(b.Path, b.Sha256, after[i].Sha256)).Where(c => c.BeforeSha256 != c.AfterSha256).ToArray();
        SettingChanges = RemixManagedService.ConfigurationChanges(before, after); }
    public string Action { get; }
    public string ExecutablePath => Report.ExecutablePath;
    public string? PreviousExecutableSha256 => Previous?.ExecutableSha256;
    public string ExecutableSha256 => Identity.ExecutableHash;
    public IReadOnlyList<RemixManagedChange> Changes { get; }
    public IReadOnlyList<RemixSettingChange> SettingChanges { get; }
    public IReadOnlyList<string> Warnings => Identity.Executable == Identity.Original
        ? ["Installazione locale: la registrazione non certifica la versione della mod né le funzioni del runtime.",
            "Si modificano solo rtx.conf e user.conf. EXE, runtime, asset, dxvk.conf e risoluzione restano conservati.",
            "Verifica nel pannello Remix le opzioni effettive dopo il riavvio."]
        : ["La copia può condividere salvataggi e registro con l'originale.",
        "Il profilo consigliato combina il file utente con le scelte confermate nel pannello; non certifica prestazioni su altri PC.",
        "Le impostazioni salvate non provano le opzioni effettive durante l'esecuzione; controllarle nel pannello Remix dopo il riavvio."];
    internal string Owner { get; }
    internal GameReport Report { get; }
    internal RemixManagedService.Identity Identity { get; }
    internal RemixManagedService.SavedState? Previous { get; }
    internal RemixManagedService.SavedState Next { get; }
    internal IReadOnlyList<RemixManagedService.ConfigFile> Before { get; }
    internal IReadOnlyList<RemixManagedService.ConfigFile> After { get; }
}

/// <summary>Adopts an identified alpha12 copy and manages only rtx.conf/user.conf. Never starts an uploaded image.</summary>
public sealed class RemixManagedService
{
    private const string StateName = "remix-managed-state.json", JournalName = "remix-managed-operation.json";
    private const string Marker = "GamePanel-RemixTrial.json";
    private const int ConfigLimit = 256 * 1024, StateLimit = 8 * 1024 * 1024;
    private static readonly string[] ConfigPaths = ["rtx.conf", "user.conf"];
    private readonly string _store, _owner = Guid.NewGuid().ToString("N");
    private readonly Func<string, bool> _running;
    private readonly Pins _pins;
    internal sealed record Pins(string Executable, string Client, string Bridge, string Archive, string? AlternateExecutable = null,
        string Underground1Executable = Underground1DisplayFix.ExecutableSha256);
    internal sealed record Identity(string Executable, string Original, string ExecutableHash, string MarkerHash);
    internal sealed record ConfigFile(string Path, byte[]? Bytes, string? Sha256);
    internal sealed record SavedState(int SchemaVersion, string Revision, string ExecutablePath, string OriginalExecutablePath,
        string ExecutableSha256, string MarkerSha256, IReadOnlyList<ConfigFile> Captured, IReadOnlyList<ConfigFile> Current);
    internal sealed record Operation(int SchemaVersion, string Id, string Action, Identity Identity, string Status,
        SavedState? Previous, SavedState Next, IReadOnlyList<ConfigFile> Before, IReadOnlyList<ConfigFile> After);
    public RemixManagedService(string? store = null, Func<string, bool>? running = null)
        : this(store ?? ReShadeProfileService.DefaultStore, running ?? IsGameRunning,
            new(RemixTrialService.WorkingExecutableSha256, RemixTrialService.ClientSha256,
                "4a9acb0191a78a48afb92879de372c807887294e40503ddb3d9530605142a911", RemixComponentBundle.SourceSha256, RemixExecutableCatalog.NoCdSha256)) { }
    internal RemixManagedService(string store, Func<string, bool> running, Pins pins)
    { _store = Path.GetFullPath(store); _running = running; _pins = pins; }

    public RemixManagedStatus Status(string executablePath)
    {
        var executable = Full(executablePath);
        try
        {
            var identity = VerifyIdentity(executable, null, CancellationToken.None); var folder = Folder(executable);
            var state = LoadState(folder, executable); var pending = LoadOperation(folder, executable)?.Status == "Pending";
            if (state is not null && (state.MarkerSha256 != identity.MarkerHash || !SamePath(state.OriginalExecutablePath, identity.Original))) throw Invalid("Il manifest della copia è cambiato dopo l'adozione.");
            var live = ReadConfigs(Path.GetDirectoryName(executable)!);
            var executableChanged = state is not null && !Same(state.ExecutableSha256, identity.ExecutableHash);
            var changed = state is not null && (!SameFiles(state.Current, live) || executableChanged);
            var local = IsUnderground1(executable, identity.ExecutableHash);
            return new(true, state is not null, pending, changed,
                pending ? "Operazione Remix interrotta: riprendi o annulla."
                : state is null ? "Copia Remix riconosciuta. Adottala per salvare e gestire il profilo."
                : executableChanged ? "Nuovo EXE identificato: usa Salva configurazione attuale per registrare il cambio con anteprima. La compatibilità di Remix nel gioco resta da collaudare."
                : changed ? "Configurazione cambiata fuori da GamePanel: salva la configurazione attuale prima di applicare altre modifiche."
                : "Profilo Remix gestito. Le impostazioni effettive vanno verificate nel gioco.", identity.Original, ReadSettings(live), local,
                local && HasLocalDll(executable, "nvngx_dlss.dll"), local && HasLocalDll(executable, "nvngx_dlssd.dll"), local && HasLocalDll(executable, "nvngx_dlssg.dll"),
                local && live.Any(f => f.Bytes is not null && Parse(f.Bytes).ContainsKey("rtx.rayreconstruction.enableTransformerModelD")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException or BadImageFormatException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return new(false, false, false, false, e.Message, null, new()); }
    }

    /// <summary>Local registered relationships only; no EXE hashing or execution during library rendering.</summary>
    public IReadOnlyList<RemixManagedVariant> ListVariants()
    {
        var result = new List<RemixManagedVariant>(); GuardPath(_store); if (!Directory.Exists(_store)) return result;
        foreach (var folder in Directory.EnumerateDirectories(_store).Take(4096))
        {
            if (result.Count >= GameLibrary.MaximumGames) break;
            try
            {
                GuardPath(folder); var path = Path.Combine(folder, StateName); if (!File.Exists(path)) continue;
                var state = ReadJson<SavedState>(path, StateLimit); ValidateState(state, state.ExecutablePath);
                if (!SamePath(Folder(state.ExecutablePath), folder)) continue;
                if (!SamePath(state.OriginalExecutablePath, state.ExecutablePath)) result.Add(new(state.OriginalExecutablePath, state.ExecutablePath));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException) { }
        }
        return result.AsReadOnly();
    }
    public IReadOnlyList<RemixManagedVariant> ListAdopted() => ListVariants();
    /// <summary>Strict journal check for launch guards. Malformed journals throw instead of allowing launch.</summary>
    public bool HasPendingOperation(string executablePath)
    {
        var executable = Full(executablePath); return LoadOperation(Folder(executable), executable)?.Status == "Pending";
    }

    public RemixManagedPlan PrepareAdopt(GameReport report) => Prepare(report, "Adopt", null, false);
    public RemixManagedPlan PrepareCapture(GameReport report) => Prepare(report, "Capture", null, false);
    public RemixManagedPlan PrepareApply(GameReport report, RemixDlssSettings? settings = null, bool recommended = false)
        => Prepare(report, "Apply", settings, recommended);
    public RemixManagedPlan PrepareRestore(GameReport report) => Prepare(report, "Restore", null, false);
    private RemixManagedPlan Prepare(GameReport report, string action, RemixDlssSettings? settings, bool recommended)
    {
        var executable = Full(report.ExecutablePath); Stopped(executable);
        if (report.HasAntiCheatIndicators) throw Invalid("Indicatori anti-cheat: modifica Remix sospesa.");
        var identity = VerifyIdentity(executable, report.Sha256, CancellationToken.None); var folder = Folder(executable);
        CheckOtherPending(folder); if (LoadOperation(folder, executable)?.Status == "Pending") throw Invalid("Prima recupera l'operazione Remix interrotta.");
        var previous = LoadState(folder, executable); var before = ReadConfigs(Path.GetDirectoryName(executable)!);
        if (previous is not null && (previous.MarkerSha256 != identity.MarkerHash || !SamePath(previous.OriginalExecutablePath, identity.Original))) throw Invalid("Il manifest della copia è cambiato dopo l'adozione.");
        if (action == "Adopt" && previous is not null) throw Invalid("Copia già adottata: usa Salva configurazione attuale.");
        if (action != "Adopt" && previous is null) throw Invalid("Adotta prima la copia Remix esistente.");
        if (previous is not null && !Same(previous.ExecutableSha256, identity.ExecutableHash) && action != "Capture")
            throw Invalid("EXE cambiato: registra prima il cambio con Salva configurazione attuale.");
        if (action is "Apply" or "Restore" && !SameFiles(previous!.Current, before)) throw Invalid("Configurazione cambiata: salva la configurazione attuale prima di applicare modifiche.");
        var captured = action is "Adopt" or "Capture" ? before : previous!.Captured;
        IReadOnlyList<ConfigFile> after = before;
        if (action == "Restore") after = captured;
        if (action == "Apply")
        {
            if (IsUnderground1(executable, identity.ExecutableHash))
            {
                if (recommended) throw Invalid("Il profilo consigliato UG2 non è applicabile a Underground 1.");
                ValidateLocalSettings(executable, before, settings);
            }
            var edits = recommended ? RemixRecommendedProfile.Values.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            if (settings is not null) foreach (var pair in SettingValues(settings)) edits[pair.Key] = pair.Value;
            if (edits.Count == 0) throw Invalid("Nessuna impostazione selezionata.");
            after = before.Select(f => f.Bytes is null ? f : Config(f.Path, Patch(f.Bytes, edits))).ToArray();
        }
        var next = new SavedState(1, Guid.NewGuid().ToString("N"), executable, identity.Original, identity.ExecutableHash,
            identity.MarkerHash, Clone(captured), Clone(after));
        return new(_owner, action, report with { ExecutablePath = executable }, identity, previous, next, Clone(before), Clone(after));
    }

    public RemixManagedStatus Execute(RemixManagedPlan plan, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested(); if (plan.Owner != _owner) throw Invalid("Anteprima di un'altra sessione: preparala di nuovo.");
        var executable = plan.ExecutablePath; Stopped(executable); var identity = VerifyIdentity(executable, plan.Report.Sha256, token);
        if (!SameIdentity(identity, plan.Identity)) throw Invalid("Identità della copia cambiata dall'anteprima.");
        var folder = Folder(executable); Directory.CreateDirectory(folder); GuardPath(folder); using var lease = Acquire(folder);
        CheckOtherPending(folder); var operation = LoadOperation(folder, executable);
        if (operation?.Status == "Pending") throw Invalid("Prima recupera l'operazione Remix interrotta.");
        var previous = LoadState(folder, executable);
        if (!SameState(previous, plan.Previous) || !SameFiles(ReadConfigs(Path.GetDirectoryName(executable)!), plan.Before))
            throw Invalid("Configurazione o stato cambiati dall'anteprima: nessuna modifica applicata.");
        var next = new Operation(1, Guid.NewGuid().ToString("N"), plan.Action, identity, "Pending", previous, plan.Next, plan.Before, plan.After);
        ValidateOperation(next, executable); WriteJson(Path.Combine(folder, JournalName), next);
        Run(next, folder, rollback: false, token, progress); return Status(executable);
    }
    public RemixManagedStatus Recover(string executablePath, bool rollback, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var executable = Full(executablePath); Stopped(executable); var identity = VerifyIdentity(executable, null, token);
        var folder = Folder(executable); GuardPath(folder); using var lease = Acquire(folder); CheckOtherPending(folder);
        var operation = LoadOperation(folder, executable) ?? throw Invalid("Nessuna operazione Remix da recuperare.");
        if (operation.Status != "Pending") return Status(executable);
        if (!SameIdentity(identity, operation.Identity)) throw Invalid("Identità della copia cambiata: recupero bloccato.");
        Run(operation, folder, rollback, token, progress); return Status(executable);
    }
    private void Run(Operation operation, string folder, bool rollback, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var root = Path.GetDirectoryName(operation.Identity.Executable)!;
        var stored = LoadState(folder, operation.Identity.Executable);
        if (!SameState(stored, operation.Previous) && !SameState(stored, operation.Next)) throw Invalid("Stato Remix cambiato fuori dall'operazione: recupero bloccato.");
        // Verify every target before touching either file. Current bytes must be one of the journal's exact versions.
        var live = ReadConfigs(root);
        for (var i = 0; i < ConfigPaths.Length; i++)
            if (!SameFile(live[i], operation.Before[i]) && !SameFile(live[i], operation.After[i])) throw Invalid("File cambiato fuori dall'operazione: " + live[i].Path);
        var target = rollback ? operation.Before : operation.After;
        for (var i = 0; i < target.Count; i++)
        {
            token.ThrowIfCancellationRequested(); Stopped(operation.Identity.Executable);
            if (!SameIdentity(VerifyIdentity(operation.Identity.Executable, operation.Identity.ExecutableHash, token), operation.Identity)) throw Invalid("Copia cambiata durante l'operazione.");
            var current = ReadConfigs(root)[i];
            if (!SameFile(current, operation.Before[i]) && !SameFile(current, operation.After[i])) throw Invalid("File cambiato durante l'operazione: " + current.Path);
            if (!SameFile(current, target[i])) Replace(root, current, target[i], token);
            progress?.Report(new("Configurazione Remix: " + target[i].Path, i + 1, target.Count));
        }
        token.ThrowIfCancellationRequested(); Stopped(operation.Identity.Executable);
        if (!SameIdentity(VerifyIdentity(operation.Identity.Executable, operation.Identity.ExecutableHash, token), operation.Identity)) throw Invalid("Copia cambiata prima del completamento.");
        if (!SameFiles(ReadConfigs(root), target)) throw Invalid("Verifica finale della configurazione fallita.");
        stored = LoadState(folder, operation.Identity.Executable);
        if (!SameState(stored, operation.Previous) && !SameState(stored, operation.Next)) throw Invalid("Stato Remix cambiato prima del completamento.");
        var state = rollback ? operation.Previous : operation.Next;
        if (state is null) { var path = Path.Combine(folder, StateName); GuardPath(path); if (File.Exists(path)) File.Delete(path); }
        else WriteJson(Path.Combine(folder, StateName), state);
        WriteJson(Path.Combine(folder, JournalName), operation with { Status = rollback ? "RolledBack" : "Committed" });
    }
    private static void Replace(string root, ConfigFile current, ConfigFile target, CancellationToken token)
    {
        var path = GameFile(root, target.Path); var temporary = path + ".gamepanel-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (target.Bytes is not null)
            {
                using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(target.Bytes); stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            if (!SameFile(ReadConfig(root, current.Path), current)) throw Invalid("Configurazione cambiata prima della scrittura: " + current.Path);
            if (target.Bytes is null) { if (File.Exists(path)) File.Delete(path); }
            else { GuardPath(path); File.Move(temporary, path, true); }
        }
        finally { GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private Identity VerifyIdentity(string executable, string? expected, CancellationToken token)
    {
        try { return VerifyIdentityCore(executable, expected, token); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Manifest della copia Remix non valido o incompleto.", e); }
    }
    private Identity VerifyIdentityCore(string executable, string? expected, CancellationToken token)
    {
        GuardPath(executable); var root = Path.GetDirectoryName(executable)!; _ = Folder(executable);
        var executableHash = FileHash(executable, 64 * 1024 * 1024, token);
        if (IsUnderground1(executable, executableHash))
        {
            if (expected is not null && !Same(expected, executableHash)) throw Invalid("EXE UG1 cambiato dall'analisi.");
            var game = PeInspector.Read(executable);
            if (game.IsDll || game.Architecture != "x86 (32 bit)" || !game.Imports.Contains("d3d9.dll", StringComparer.OrdinalIgnoreCase)) throw Invalid("EXE UG1 non compatibile.");
            var fingerprint = Underground1RemixInstallation.Fingerprint(root, token);
            if (File.Exists(GameFile(root, RemixPackService.Marker)))
            {
                var origin = RemixPackService.ReadOrigin(root, executableHash) ?? throw Invalid("Origine del pacchetto UG1 assente.");
                var packageHash = FileHash(GameFile(root, RemixPackService.Marker), 4 * 1024 * 1024, token);
                return new(executable, origin, executableHash, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint + packageHash))).ToLowerInvariant());
            }
            return new(executable, executable, executableHash, fingerprint);
        }
        if (!Path.GetFileName(executable).Equals("speed2.exe", StringComparison.OrdinalIgnoreCase)
            || !KnownUnderground2(executableHash) || expected is not null && !Same(expected, executableHash))
            throw Invalid("EXE Underground 2 non identificato per la gestione della configurazione Remix.");
        var pe = PeInspector.Read(executable);
        if (pe.IsDll || pe.Architecture != "x86 (32 bit)" || !pe.Imports.Contains("d3d9.dll", StringComparer.OrdinalIgnoreCase)) throw Invalid("EXE Remix non compatibile.");
        if (File.Exists(GameFile(root, RemixPackService.Marker)))
        {
            var origin = RemixPackService.ReadOrigin(root, executableHash) ?? throw Invalid("Origine del pacchetto non disponibile.");
            return new(executable, origin, executableHash, FileHash(GameFile(root, RemixPackService.Marker), 4 * 1024 * 1024, token));
        }
        var marker = GameFile(root, Marker); var bytes = ReadBytes(marker, 24 * 1024 * 1024);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 20 }); RejectDuplicates(document.RootElement);
        var m = document.RootElement;
        if (m.GetProperty("SchemaVersion").GetInt32() != 1 || m.GetProperty("AntiCullIncluded").GetBoolean()
            || !Same(m.GetProperty("ArchiveSha256").GetString() ?? "", _pins.Archive)
            || !Same(m.GetProperty("SourceExecutableSha256").GetString() ?? "", _pins.Executable)) throw Invalid("Manifest della copia Remix non identificato.");
        var originalFolder = m.GetProperty("SourceFolder").GetString() ?? throw Invalid("Sorgente Remix assente.");
        if (!Path.IsPathFullyQualified(originalFolder) || originalFolder.StartsWith("\\\\", StringComparison.Ordinal) || originalFolder.StartsWith("//", StringComparison.Ordinal))
            throw Invalid("La sorgente Remix deve essere una cartella locale assoluta.");
        originalFolder = Path.GetFullPath(originalFolder); GuardPath(originalFolder);
        if (Inside(root, originalFolder) || Inside(originalFolder, root) || Inside(originalFolder, _store) || Inside(_store, originalFolder)) throw Invalid("Sorgente e copia Remix non separate.");
        var original = GameFile(originalFolder, Path.GetFileName(executable));
        if (!KnownUnderground2(FileHash(original, 64 * 1024 * 1024, token))) throw Invalid("L'originale associato è mancante o non identificato: ricollega una copia identificata.");
        foreach (var (path, hash) in new[] { ("d3d9.dll", _pins.Client), (".trex/NvRemixBridge.exe", _pins.Bridge) })
        {
            var matches = m.GetProperty("Files").EnumerateArray().Where(f => f.GetProperty("RelativePath").GetString() == path).ToArray();
            if (matches.Length != 1 || !Same(matches[0].GetProperty("Sha256").GetString() ?? "", hash)
                || !Same(FileHash(GameFile(root, path), 64 * 1024 * 1024, token), hash)) throw Invalid("Componente Remix cambiato o non identificato: " + path);
        }
        var servers = m.GetProperty("Files").EnumerateArray().Where(f => f.GetProperty("RelativePath").GetString() == ".trex/d3d9.dll").ToArray();
        if (servers.Length != 1) throw Invalid("Runtime server Remix assente o duplicato nel manifest.");
        var serverHash = servers[0].GetProperty("Sha256").GetString(); var serverBytes = servers[0].GetProperty("Bytes").GetInt64();
        var server = GameFile(root, ".trex/d3d9.dll");
        if (!ValidHash(serverHash) || serverBytes is < 1 or > 64 * 1024 * 1024 || new FileInfo(server).Length != serverBytes
            || !Same(FileHash(server, 64 * 1024 * 1024, token), serverHash!)) throw Invalid("Runtime server Remix cambiato dalla copia identificata.");
        if (File.Exists(GameFile(root, "scripts/NFSU2AntiCull.asi"))) throw Invalid("AntiCull ASI non è compatibile con questo profilo.");
        return new(executable, original, executableHash, Hash(bytes));
    }
    private static IReadOnlyList<ConfigFile> ReadConfigs(string root) => ConfigPaths.Select(p => ReadConfig(root, p)).ToArray();
    private static ConfigFile ReadConfig(string root, string path)
    {
        var file = GameFile(root, path);
        if (AttributesOrMissing(file) is null) { if (path == "rtx.conf") throw Invalid("rtx.conf mancante nella copia Remix."); return new(path, null, null); }
        var bytes = ReadBytes(file, ConfigLimit); _ = Parse(bytes); return Config(path, bytes);
    }
    private static ConfigFile Config(string path, byte[] bytes) => new(path, bytes, Hash(bytes));
    private static IReadOnlyList<ConfigFile> Clone(IReadOnlyList<ConfigFile> files) => files.Select(f => f with { Bytes = f.Bytes?.ToArray() }).ToArray();
    private static bool SameFile(ConfigFile a, ConfigFile b) => a.Path == b.Path && a.Sha256 == b.Sha256 && a.Bytes?.Length == b.Bytes?.Length;
    private static bool SameFiles(IReadOnlyList<ConfigFile> a, IReadOnlyList<ConfigFile> b) => a.Count == b.Count && a.Zip(b).All(p => SameFile(p.First, p.Second));
    private static bool SameState(SavedState? a, SavedState? b) => a is null || b is null ? a is null && b is null
        : Hash(JsonSerializer.SerializeToUtf8Bytes(a)) == Hash(JsonSerializer.SerializeToUtf8Bytes(b));
    internal static IReadOnlyList<RemixSettingChange> ConfigurationChanges(IReadOnlyList<ConfigFile> before, IReadOnlyList<ConfigFile> after)
    {
        var result = new List<RemixSettingChange>();
        for (var i = 0; i < before.Count; i++)
        {
            var previous = before[i].Bytes is null ? new Dictionary<string, string>(StringComparer.Ordinal) : Parse(before[i].Bytes!);
            var next = after[i].Bytes is null ? new Dictionary<string, string>(StringComparer.Ordinal) : Parse(after[i].Bytes!);
            foreach (var key in previous.Keys.Union(next.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                previous.TryGetValue(key, out var old); next.TryGetValue(key, out var value);
                if (old != value) result.Add(new(before[i].Path, key, old, value));
            }
        }
        return result.AsReadOnly();
    }
    private static Dictionary<string, string> Parse(byte[] bytes)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException e) { throw new InvalidDataException("Configurazione Remix non UTF-8.", e); }
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var lines = text.Split('\n');
        if (lines.Length > 12000) throw Invalid("Configurazione Remix troppo lunga.");
        foreach (var raw in lines)
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.Length > 32768 || line.Any(c => char.IsControl(c) && c != '\t')) throw Invalid("Riga Remix non valida.");
            var at = line.IndexOf('='); if (at < 1) throw Invalid("Configurazione Remix non interpretabile: riga senza chiave e valore.");
            var key = line[..at].Trim(); if (!values.TryAdd(key, line[(at + 1)..].Trim())) throw Invalid("Chiave Remix duplicata: " + key);
        }
        return values;
    }
    private static byte[] Patch(byte[] bytes, IReadOnlyDictionary<string, string> edits)
    {
        _ = Parse(bytes); var text = new UTF8Encoding(false, true).GetString(bytes); var bom = text.StartsWith('\uFEFF');
        if (bom) text = text[1..]; var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim(); if (line.StartsWith('#') || line.StartsWith(';')) continue;
            var at = line.IndexOf('='); if (at < 1) continue; var key = line[..at].Trim();
            if (edits.TryGetValue(key, out var value)) { lines[i] = key + " = " + value; seen.Add(key); }
        }
        foreach (var pair in edits.Where(p => !seen.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal)) lines.Add(pair.Key + " = " + pair.Value);
        var result = Encoding.UTF8.GetBytes((bom ? "\uFEFF" : "") + string.Join(newline, lines));
        if (result.Length > ConfigLimit) throw Invalid("Configurazione aggiornata oltre il limite."); _ = Parse(result); return result;
    }
    private static RemixDlssSettings ReadSettings(IReadOnlyList<ConfigFile> files)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in files.Where(f => f.Bytes is not null)) foreach (var pair in Parse(f.Bytes!)) values[pair.Key] = pair.Value;
        int? Int(string key) => values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        bool? Bool(string key) => values.TryGetValue(key, out var v) && bool.TryParse(v, out var n) ? n : null;
        return new(Int("rtx.upscalerType"), Int("rtx.qualityDLSS"), Bool("rtx.enableRayReconstruction"),
            Bool("rtx.rayreconstruction.enableTransformerModelD"), Bool("rtx.dlfg.enable"), Int("rtx.dlfg.maxInterpolatedFrames"), Int("rtx.reflexMode"));
    }
    private static IReadOnlyDictionary<string, string> SettingValues(RemixDlssSettings settings)
    {
        var edits = new Dictionary<string, string>(StringComparer.Ordinal);
        void Number(string key, int? value, int min, int max) { if (value is null) return; if (value < min || value > max) throw Invalid("Valore DLSS fuori intervallo: " + key); edits[key] = value.Value.ToString(CultureInfo.InvariantCulture); }
        void Boolean(string key, bool? value) { if (value is not null) edits[key] = value.Value ? "True" : "False"; }
        Number("rtx.upscalerType", settings.Upscaler, 1, 1); Number("rtx.qualityDLSS", settings.DlssMode, 0, 5);
        Boolean("rtx.enableRayReconstruction", settings.RayReconstruction); Boolean("rtx.rayreconstruction.enableTransformerModelD", settings.Transformer);
        Boolean("rtx.dlfg.enable", settings.FrameGeneration); Number("rtx.dlfg.maxInterpolatedFrames", settings.GeneratedFrames, 0, 3); Number("rtx.reflexMode", settings.Reflex, 0, 2);
        return edits;
    }
    private SavedState? LoadState(string folder, string executable)
    {
        var path = Path.Combine(folder, StateName); GuardPath(path); if (!File.Exists(path)) return null;
        var state = ReadJson<SavedState>(path, StateLimit); ValidateState(state, executable); return state;
    }
    private Operation? LoadOperation(string folder, string executable)
    {
        var path = Path.Combine(folder, JournalName); GuardPath(path); if (!File.Exists(path)) return null;
        var operation = ReadJson<Operation>(path, StateLimit); ValidateOperation(operation, executable); return operation;
    }
    private void ValidateState(SavedState state, string executable)
    {
        if (state is null || state.SchemaVersion != 1 || !ValidId(state.Revision) || !SamePath(state.ExecutablePath, executable) || !Path.IsPathFullyQualified(state.OriginalExecutablePath)
            || !KnownExecutable(state.ExecutableSha256) || !ValidHash(state.MarkerSha256)
            || (SamePath(state.OriginalExecutablePath, executable) != IsUnderground1(executable, state.ExecutableSha256)))
            throw Invalid("Stato Remix non valido.");
        ValidateFiles(state.Captured); ValidateFiles(state.Current);
    }
    private void ValidateOperation(Operation operation, string executable)
    {
        if (operation.SchemaVersion != 1 || !ValidId(operation.Id) || operation.Identity is null
            || !KnownExecutable(operation.Identity.ExecutableHash) || !ValidHash(operation.Identity.MarkerHash)
            || operation.Action is not ("Adopt" or "Capture" or "Apply" or "Restore") || operation.Status is not ("Pending" or "Committed" or "RolledBack"))
            throw Invalid("Journal Remix non valido.");
        if (!SamePath(operation.Identity.Executable, executable))
            throw Invalid("Il journal Remix appartiene a un altro eseguibile. Rianalizza il percorso registrato: " + operation.Identity.Executable);
        ValidateState(operation.Next, executable); if (operation.Previous is not null) ValidateState(operation.Previous, executable);
        if (operation.Previous is not null && !Same(operation.Previous.ExecutableSha256, operation.Next.ExecutableSha256) && operation.Action != "Capture")
            throw Invalid("Il cambio di EXE richiede una registrazione esplicita della configurazione.");
        if (!SamePath(operation.Next.OriginalExecutablePath, operation.Identity.Original) || operation.Next.MarkerSha256 != operation.Identity.MarkerHash
            || !Same(operation.Next.ExecutableSha256, operation.Identity.ExecutableHash))
            throw Invalid("Journal Remix non legato alla copia.");
        if (operation.Previous is not null && (!SamePath(operation.Previous.OriginalExecutablePath, operation.Identity.Original)
            || operation.Previous.MarkerSha256 != operation.Identity.MarkerHash || operation.Previous.Revision == operation.Next.Revision))
            throw Invalid("Stato precedente del journal Remix non legato alla copia.");
        ValidateFiles(operation.Before); ValidateFiles(operation.After);
        if (!SameFiles(operation.Next.Current, operation.After) || operation.Previous is not null && operation.Action is not "Capture" && !SameFiles(operation.Previous.Current, operation.Before))
            throw Invalid("Versioni journal Remix incoerenti.");
        if (operation.Action is "Adopt" or "Capture" && !SameFiles(operation.Before, operation.After)) throw Invalid("La cattura non può scrivere nel gioco.");
        if (operation.Action == "Adopt" && operation.Previous is not null || operation.Action != "Adopt" && operation.Previous is null)
            throw Invalid("Transizione di adozione Remix non valida.");
        if (operation.Action is "Adopt" or "Capture" && !SameFiles(operation.Next.Captured, operation.Before)
            || operation.Action is "Apply" or "Restore" && !SameFiles(operation.Next.Captured, operation.Previous!.Captured)
            || operation.Action == "Restore" && !SameFiles(operation.After, operation.Previous!.Captured))
            throw Invalid("Punto di ripristino del journal Remix incoerente.");
    }
    private static void ValidateFiles(IReadOnlyList<ConfigFile> files)
    {
        if (files is null || files.Count != 2) throw Invalid("Inventario configurazione Remix non valido.");
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i]; if (file is null || file.Path != ConfigPaths[i] || file.Bytes is null && (file.Sha256 is not null || i == 0)
                || file.Bytes is not null && (file.Bytes.Length > ConfigLimit || file.Sha256 != Hash(file.Bytes))) throw Invalid("Payload configurazione Remix non valido.");
            if (file.Bytes is not null) _ = Parse(file.Bytes);
        }
    }
    private static void CheckOtherPending(string folder)
    {
        foreach (var name in new[] { "operation.json", "widescreen-operation.json", "assets-operation.json", "directx-operation.json", "directx-replacements-operation.json" })
        {
            var path = Path.Combine(folder, name); GuardPath(path); if (!File.Exists(path)) continue;
            using var doc = JsonDocument.Parse(ReadBytes(path, 4 * 1024 * 1024));
            if (doc.RootElement.TryGetProperty("Status", out var status) && status.GetString() == "Pending") throw Invalid("Completa prima l'altra operazione GamePanel interrotta.");
        }
    }
    private string Folder(string executable)
    {
        var root = Path.GetDirectoryName(executable)!; GuardPath(root); GuardPath(_store);
        if (Inside(root, _store) || Inside(_store, root)) throw Invalid("Cartella profili e gioco devono essere separate.");
        return Path.Combine(_store, Hash(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root)));
    }
    private static FileStream Acquire(string folder) { GuardPath(folder); var path = Path.Combine(folder, ".operation.lock"); GuardPath(path); return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private void Stopped(string executable) { if (_running(executable)) throw Invalid("Chiudi il gioco prima di gestire il profilo Remix."); }
    private static string Full(string path) => Path.GetFullPath(ExecutablePath.Normalize(path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private bool IsUnderground1(string executable, string hash) => Path.GetFileName(executable).Equals("Speed.exe", StringComparison.OrdinalIgnoreCase)
        && Same(hash, _pins.Underground1Executable);
    private static bool HasLocalDll(string executable, string name) => Underground1RemixInstallation.HasDlssFile(Path.GetDirectoryName(executable)!, name);
    private static void ValidateLocalSettings(string executable, IReadOnlyList<ConfigFile> files, RemixDlssSettings? settings)
    {
        if (settings is null) return;
        if ((settings.Upscaler is not null || settings.DlssMode is not null) && !HasLocalDll(executable, "nvngx_dlss.dll")) throw Invalid("Libreria DLSS Super Resolution non rilevata nel runtime UG1.");
        if (settings.RayReconstruction == true && !HasLocalDll(executable, "nvngx_dlssd.dll")) throw Invalid("Libreria Ray Reconstruction non rilevata nel runtime UG1.");
        if (settings.Transformer is not null && !files.Any(f => f.Bytes is not null && Parse(f.Bytes).ContainsKey("rtx.rayreconstruction.enableTransformerModelD")))
            throw Invalid("Il runtime UG1 non dichiara il parametro Transformer: configuralo prima nel pannello Remix se disponibile.");
        if (settings.FrameGeneration == true && !HasLocalDll(executable, "nvngx_dlssg.dll")) throw Invalid("Libreria Frame Generation non rilevata nel runtime UG1.");
        if (settings.GeneratedFrames is > 1 || settings.FrameGeneration == true && (settings.GeneratedFrames ?? ReadSettings(files).GeneratedFrames) is > 1) throw Invalid("La compatibilità Multi Frame Generation del runtime UG1 non è verificata. Usa 2× o il pannello Remix.");
    }
    private bool KnownExecutable(string hash) => KnownUnderground2(hash) || Same(hash, _pins.Underground1Executable);
    private bool KnownUnderground2(string hash) => Same(hash, _pins.Executable)
        || _pins.AlternateExecutable is not null && Same(hash, _pins.AlternateExecutable);
    private static bool SamePath(string? a, string? b) => string.Equals(a, b,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool SameIdentity(Identity a, Identity b) => SamePath(a.Executable, b.Executable)
        && SamePath(a.Original, b.Original) && Same(a.ExecutableHash, b.ExecutableHash) && Same(a.MarkerHash, b.MarkerHash);
    private static string FileHash(string path, int maximum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); GuardPath(path); var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0) throw Invalid("File regolare richiesto.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw Invalid("File oltre il limite previsto.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1024 * 1024]; long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = input.Read(buffer); if (count == 0) break; total += count;
            if (total > maximum) throw Invalid("File oltre il limite previsto."); hash.AppendData(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested(); return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static byte[] ReadBytes(string path, int maximum)
    {
        GuardPath(path); var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0) throw Invalid("File regolare richiesto.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw Invalid("File oltre il limite previsto.");
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes); return bytes;
    }
    private static T ReadJson<T>(string path, int maximum)
    {
        try
        {
            var bytes = ReadBytes(path, maximum); using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 }); RejectDuplicates(document.RootElement);
            return JsonSerializer.Deserialize<T>(bytes) ?? throw Invalid("Metadati Remix mancanti.");
        }
        catch (JsonException e) { throw new InvalidDataException("Metadati Remix non leggibili.", e); }
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw Invalid("Proprietà JSON duplicata."); RejectDuplicates(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
    private static InvalidDataException Invalid(string text) => new(text);
}
