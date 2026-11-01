using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record DirectXTextureState(int SchemaVersion, string Revision, string ExecutablePath,
    string ExecutableSha256, bool Installed, string ResourceRoot, bool EnableInjection);
public sealed record DirectXTextureStatus(DirectXTextureState? State, bool Pending, string ResourceRoot,
    string Message, bool? InjectionEnabled = null)
{ public bool Installed => State?.Installed == true; }
public sealed class DirectXTexturePlan
{
    internal DirectXTexturePlan(GameReport report, DirectXTextureState? previous, DirectXTextureState next,
        IReadOnlyList<AssetChange> changes, IReadOnlyDictionary<string, byte[]> payloads, string action)
    { Report = report; Previous = previous; Next = next; Changes = changes; Payloads = payloads; Action = action; }
    internal GameReport Report { get; }
    internal DirectXTextureState? Previous { get; }
    internal DirectXTextureState Next { get; }
    internal IReadOnlyDictionary<string, byte[]> Payloads { get; }
    public IReadOnlyList<AssetChange> Changes { get; }
    public string Action { get; }
    public string ResourceRoot => Next.ResourceRoot;
}

/// <summary>Explicit, experimental API texture pilot. Only two fixed game files are ever changed;
/// captures and replacements live outside the game and survive removal and app updates.</summary>
public sealed class DirectXTextureProfileService
{
    public const string ConfigRelativePath = "scripts/TextureToolkit.ini";
    private const string StateFile = "directx-state.json", OperationFile = "directx-operation.json";
    private const int MaximumMetadataBytes = 128 * 1024;
    private readonly string _store, _dataRoot, _loaderHash;
    private readonly long _loaderBytes;
    private readonly Func<string, bool> _running;
    private readonly IReadOnlyDictionary<string, (long Bytes, string Hash)> _wideEntries;
    private sealed record Operation(int SchemaVersion, string Id, string ExecutablePath, string ExecutableSha256,
        string Status, DirectXTextureState? Previous, DirectXTextureState Next, IReadOnlyList<AssetChange> Changes);

    public DirectXTextureProfileService(string store, Func<string, bool>? running = null)
        : this(store, Path.Combine(GamePanelPaths.Root, "DirectXTextures"), running,
            DirectXTextureCatalog.LoaderSha256, DirectXTextureCatalog.LoaderBytes, WidescreenCatalog.Entries) { }
    internal DirectXTextureProfileService(string store, string dataRoot, Func<string, bool>? running,
        string loaderHash, long loaderBytes, IReadOnlyDictionary<string, (long Bytes, string Hash)> wideEntries)
    {
        _store = Path.GetFullPath(store); _dataRoot = Path.GetFullPath(dataRoot);
        _running = running ?? IsGameRunning; _loaderHash = loaderHash; _loaderBytes = loaderBytes; _wideEntries = wideEntries;
        if (!ValidHash(loaderHash) || loaderBytes <= 0 || loaderBytes > 32 * 1024 * 1024) throw Invalid("Identità runtime DirectX non valida.");
    }

    public DirectXTextureStatus Status(string executablePath)
    {
        var exe = Full(executablePath); var folder = Folder(exe); var root = ResourceRoot(exe);
        var state = LoadState(folder, exe); var op = LoadOperation(folder, exe);
        bool? enabled = null;
        if (state?.Installed == true && op?.Status != "Pending")
        {
            VerifyRuntime(exe, state, CancellationToken.None);
            enabled = ReadConfig(GameFile(Path.GetDirectoryName(exe)!, ConfigRelativePath), root).Enabled;
            state = state with { EnableInjection = enabled.Value };
        }
        return new(state, op?.Status == "Pending", root,
            op?.Status == "Pending" ? "Operazione texture DirectX interrotta: riprendi o annulla."
            : state?.Installed == true ? "Texture DirectX sperimentali installate; compatibilità nel gioco da collaudare."
            : "Texture DirectX non installate. Nessun cambiamento all'eseguibile.", enabled);
    }

    public DirectXTexturePlan PrepareInstall(GameReport report, string verifiedLoaderPath, CancellationToken token = default)
    {
        var (_, state) = Begin(report, token); var root = Path.GetDirectoryName(Full(report.ExecutablePath))!;
        if (state?.Installed == true) throw Invalid("Texture DirectX già installate.");
        CheckDependencies(report, token);
        foreach (var path in Targets)
            if (Live(root, path, token) is not null) throw Invalid("Componente texture DirectX preesistente: " + path);
        var loader = ReadBounded(verifiedLoaderPath, checked((int)_loaderBytes));
        if (loader.Length != _loaderBytes || Hash(loader) != _loaderHash) throw Invalid("Runtime DirectX non corrispondente alla release verificata.");
        var resource = ResourceRoot(report.ExecutablePath);
        CheckDataPaths(resource);
        var config = EncodeConfig("[TextureToolkit]\r\nHotKey=0x2D\r\nResourceRoot=" + resource +
            "\r\nEnableInjection=0\r\nAutoDump=0\r\nFilterSmallTextures=1\r\nShowCurrentFrameOnly=1\r\n" +
            "AcceptSpecialKNames=0\r\nHighlightSelected=0\r\nShowOSDBanner=1\r\nUIScale=0\r\nVerbose=0\r\n");
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        { [DirectXTextureCatalog.LoaderRelativePath] = loader, [ConfigRelativePath] = config };
        return Plan(report, state, true, false, payloads, "Installa texture DirectX", token);
    }

    public DirectXTexturePlan PrepareRemove(GameReport report, CancellationToken token = default)
    {
        var (_, state) = Begin(report, token);
        if (state?.Installed != true) throw Invalid("Texture DirectX non installate da GamePanel.");
        // Removal does not require other plugins to remain installed; only our owned files matter.
        return Plan(report, state, false, false, new Dictionary<string, byte[]>(), "Rimuovi texture DirectX", token);
    }

    public DirectXTexturePlan PrepareSettings(GameReport report, bool enableInjection, CancellationToken token = default)
    {
        var (_, state) = Begin(report, token);
        if (state?.Installed != true) throw Invalid("Installa prima il percorso texture DirectX.");
        CheckDependencies(report, token);
        if (enableInjection) CheckReplacementFiles(state.ResourceRoot, token);
        var config = ReadConfig(GameFile(Path.GetDirectoryName(Full(report.ExecutablePath))!, ConfigRelativePath), state.ResourceRoot);
        var text = config.Text;
        text = Regex.Replace(text, @"(?im)^(\s*EnableInjection\s*=)[^\r\n]*", "${1}" + (enableInjection ? "1" : "0"));
        return Plan(report, state, true, enableInjection,
            new Dictionary<string, byte[]> { [ConfigRelativePath] = EncodeConfig(text) },
            enableInjection ? "Attiva sostituzioni DirectX" : "Disattiva sostituzioni DirectX", token);
    }

    // Replacements use the same per-game lock and dependency/identity gates as runtime mutations.
    // The caller must hold this lease until its own atomic data transaction has completed.
    internal string ProfileFolder(string executablePath) => Folder(Full(executablePath));
    internal IDisposable AcquireForReplacement(GameReport report, string resourceRoot, CancellationToken token = default)
    {
        CheckReport(report, token); var exe = Full(report.ExecutablePath); var folder = Folder(exe);
        if (!SamePath(resourceRoot, ResourceRoot(exe))) throw Invalid("Cartella texture non appartenente al gioco.");
        Directory.CreateDirectory(folder); GuardPath(folder); var lease = Acquire(folder);
        try
        {
            CheckReplacementContext(report, resourceRoot, token);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    internal void CheckReplacementContext(GameReport report, string resourceRoot, CancellationToken token = default)
    {
        CheckReport(report, token); var exe = Full(report.ExecutablePath); var folder = Folder(exe);
        if (!SamePath(resourceRoot, ResourceRoot(exe))) throw Invalid("Cartella texture non appartenente al gioco.");
        CheckNoPending(folder, allowReplacementRecovery: true); var state = LoadState(folder, exe);
        if (state?.Installed != true || state.ExecutableSha256 != report.Sha256) throw Invalid("Runtime texture DirectX non installato per questo EXE.");
        VerifyRuntime(exe, state, token); CheckDependencies(report, token); CheckDataPaths(resourceRoot);
    }

    public DirectXTextureState Execute(DirectXTexturePlan plan, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        CheckReport(plan.Report, token); var exe = Full(plan.Report.ExecutablePath); var folder = Folder(exe); var root = Path.GetDirectoryName(exe)!;
        Directory.CreateDirectory(folder); GuardPath(folder); using var lease = Acquire(folder); CheckNoPending(folder);
        var previous = LoadState(folder, exe);
        if (previous?.Revision != plan.Previous?.Revision) throw Invalid("Stato DirectX cambiato: prepara una nuova anteprima.");
        if (previous?.Installed == true) VerifyRuntime(exe, previous, token);
        if (plan.Next.Installed) CheckDependencies(plan.Report, token);
        if (plan.Next.Installed && plan.Next.EnableInjection) CheckReplacementFiles(plan.Next.ResourceRoot, token);
        foreach (var change in plan.Changes)
            if (Live(root, change.Path, token) != change.BeforeSha256) throw Invalid("File cambiato dopo l'anteprima: " + change.Path);
        var id = Guid.NewGuid().ToString("N"); var payload = Path.Combine(folder, "directx-" + id); Directory.CreateDirectory(payload); GuardPath(payload);
        for (var i = 0; i < plan.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); var c = plan.Changes[i];
            if (c.BeforeSha256 is not null) CopyExpected(GameFile(root, c.Path), Payload(payload, i, false), c.BeforeSha256, token);
            if (c.AfterSha256 is not null)
            {
                if (!plan.Payloads.TryGetValue(c.Path, out var bytes) || Hash(bytes) != c.AfterSha256) throw Invalid("Payload DirectX non valido.");
                DurableBytes(Payload(payload, i, true), bytes);
            }
        }
        CheckReport(plan.Report, token);
        foreach (var change in plan.Changes)
            if (Live(root, change.Path, token) != change.BeforeSha256) throw Invalid("Destinazione DirectX cambiata durante la preparazione.");
        var op = new Operation(1, id, exe, plan.Report.Sha256, "Pending", previous, plan.Next, plan.Changes);
        ValidateOperation(op, exe); ValidatePayloads(folder, op, token);
        WriteJson(Path.Combine(folder, OperationFile), op);
        return Transition(folder, op, false, token, progress);
    }

    public DirectXTextureState Recover(string executablePath, bool rollback, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var exe = Full(executablePath); var folder = Folder(exe); using var lease = Acquire(folder); CheckPeers(folder);
        var op = LoadOperation(folder, exe);
        if (op?.Status != "Pending") throw new InvalidOperationException("Nessuna operazione texture DirectX da recuperare.");
        return Transition(folder, op, rollback, token, progress);
    }

    private DirectXTextureState Transition(string folder, Operation op, bool rollback, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        CheckIdentity(op.ExecutablePath, op.ExecutableSha256, token); ValidatePayloads(folder, op, token);
        var targetState = rollback ? op.Previous : op.Next; var root = Path.GetDirectoryName(op.ExecutablePath)!;
        if (targetState?.Installed == true) CheckDependencies(new GameInspector().Inspect(op.ExecutablePath, token), token);
        if (targetState?.Installed == true && targetState.EnableInjection) CheckReplacementFiles(targetState.ResourceRoot, token);
        var currentState = LoadState(folder, op.ExecutablePath);
        if (currentState?.Revision != op.Previous?.Revision && currentState?.Revision != op.Next.Revision) throw Invalid("Stato DirectX estraneo al journal.");
        foreach (var path in Targets.Where(p => op.Changes.All(c => c.Path != p)))
            if (op.Previous?.Installed == true && path == DirectXTextureCatalog.LoaderRelativePath && Live(root, path, token) != _loaderHash)
                throw Invalid("Runtime DirectX modificato durante l'operazione.");
        foreach (var c in op.Changes)
        {
            var actual = Live(root, c.Path, token);
            if (actual != c.BeforeSha256 && actual != c.AfterSha256) throw Invalid("File DirectX modificato esternamente: " + c.Path);
        }
        // Creating writable storage does not erase preexisting captures or replacements.
        if (targetState?.Installed == true)
        {
            CheckDataPaths(targetState.ResourceRoot);
            foreach (var path in new[] { targetState.ResourceRoot, Path.Combine(targetState.ResourceRoot, "dump"), Path.Combine(targetState.ResourceRoot, "inject") })
            { GuardPath(path); Directory.CreateDirectory(path); GuardPath(path); }
        }
        var payload = Path.Combine(folder, "directx-" + op.Id);
        for (var i = 0; i < op.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); CheckIdentity(op.ExecutablePath, op.ExecutableSha256, token);
            var c = op.Changes[i]; var wanted = rollback ? c.BeforeSha256 : c.AfterSha256; var other = rollback ? c.AfterSha256 : c.BeforeSha256;
            var path = GameFile(root, c.Path); var actual = Live(root, c.Path, token);
            if (actual != wanted)
            {
                if (actual != other) throw Invalid("Destinazione DirectX cambiata: " + c.Path);
                if (wanted is null) { GuardPath(path); File.Delete(path); }
                else
                {
                    var parent = Path.GetDirectoryName(path)!; GuardPath(parent); Directory.CreateDirectory(parent); GuardPath(parent);
                    var temporary = Path.Combine(parent, $".gamepanel-directx-{op.Id}-{i:D4}.tmp"); GuardPath(temporary);
                    if (AttributesOrMissing(temporary) is not null && HashFile(temporary, token) != wanted) File.Delete(temporary);
                    if (!File.Exists(temporary)) CopyExpected(Payload(payload, i, !rollback), temporary, wanted, token);
                    if (HashFile(temporary, token) != wanted) throw Invalid("Copia temporanea DirectX danneggiata.");
                    token.ThrowIfCancellationRequested(); CheckIdentity(op.ExecutablePath, op.ExecutableSha256, token);
                    if (Live(root, c.Path, token) != other) throw Invalid("Destinazione DirectX cambiata durante la copia.");
                    File.Move(temporary, path, overwrite: other is not null);
                }
            }
            progress?.Report(new("Texture DirectX: " + c.Path, i + 1, op.Changes.Count));
        }
        token.ThrowIfCancellationRequested(); CheckIdentity(op.ExecutablePath, op.ExecutableSha256, token);
        foreach (var c in op.Changes)
            if (Live(root, c.Path, token) != (rollback ? c.BeforeSha256 : c.AfterSha256)) throw Invalid("Verifica finale DirectX fallita.");
        if (targetState?.Installed == true) VerifyRuntime(op.ExecutablePath, targetState, token);
        for (var i = 0; i < op.Changes.Count; i++)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(GameFile(root, op.Changes[i].Path))!, $".gamepanel-directx-{op.Id}-{i:D4}.tmp");
            GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary);
        }
        var statePath = Path.Combine(folder, StateFile); GuardPath(statePath);
        if (targetState is null) { if (File.Exists(statePath)) File.Delete(statePath); }
        else WriteJson(statePath, targetState);
        WriteJson(Path.Combine(folder, OperationFile), op with { Status = rollback ? "RolledBack" : "Complete" });
        return targetState ?? new(1, Guid.NewGuid().ToString("N"), op.ExecutablePath, op.ExecutableSha256, false, ResourceRoot(op.ExecutablePath), false);
    }

    private DirectXTexturePlan Plan(GameReport report, DirectXTextureState? previous, bool installed, bool enabled,
        Dictionary<string, byte[]> payloads, string action, CancellationToken token)
    {
        var root = Path.GetDirectoryName(Full(report.ExecutablePath))!; var changes = new List<AssetChange>();
        foreach (var path in Targets)
        {
            var before = Live(root, path, token);
            var after = payloads.TryGetValue(path, out var bytes) ? Hash(bytes) : installed ? before : null;
            if (before != after) changes.Add(new(path, before, after));
        }
        return new(report, previous, new(1, Guid.NewGuid().ToString("N"), Full(report.ExecutablePath), report.Sha256,
            installed, ResourceRoot(report.ExecutablePath), enabled), changes, payloads, action);
    }
    private (string Folder, DirectXTextureState? State) Begin(GameReport report, CancellationToken token)
    {
        CheckReport(report, token); var exe = Full(report.ExecutablePath); var folder = Folder(exe); CheckNoPending(folder);
        var state = LoadState(folder, exe);
        if (state?.Installed == true)
        {
            if (state.ExecutableSha256 != report.Sha256) throw Invalid("EXE cambiato dopo l'installazione texture DirectX.");
            VerifyRuntime(exe, state, token);
            state = state with { EnableInjection = ReadConfig(GameFile(Path.GetDirectoryName(exe)!, ConfigRelativePath), state.ResourceRoot).Enabled };
        }
        return (folder, state);
    }
    private void CheckReport(GameReport report, CancellationToken token)
    {
        CheckIdentity(Full(report.ExecutablePath), report.Sha256, token);
        var fresh = new GameInspector().Inspect(report.ExecutablePath, token);
        if (DirectXTextureCatalog.CheckGame(fresh) is { } reason) throw Invalid(reason);
        if (fresh.Sha256 != report.Sha256) throw Invalid("EXE cambiato: rianalizza il gioco.");
    }
    private void CheckIdentity(string exe, string hash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); _ = Folder(exe);
        if (_running(exe)) throw new InvalidOperationException("Chiudi il gioco prima di gestire le texture DirectX.");
        if (!ValidHash(hash) || HashFile(exe, token) != hash) throw Invalid("EXE cambiato: operazione texture DirectX sospesa.");
    }
    private void CheckDependencies(GameReport report, CancellationToken token)
    {
        var root = Path.GetDirectoryName(Full(report.ExecutablePath))!;
        var wide = ReShadeProfileService.ForWidescreen(_store, _running).Status(report.ExecutablePath);
        if (wide.Pending || wide.State?.Installed != true || wide.State.ExecutableSha256 != report.Sha256)
            throw Invalid("Applica prima il Widescreen Fix gestito da GamePanel con ASI loader verificato.");
        foreach (var path in new[] { WidescreenCatalog.Loader, WidescreenCatalog.Plugin, WidescreenCatalog.Data })
        {
            if (!_wideEntries.TryGetValue(path, out var expected) || Live(root, path, token) != expected.Hash
                || new FileInfo(GameFile(root, path)).Length != expected.Bytes)
                throw Invalid("Componente ASI/Widescreen modificato o non verificato: " + path);
        }
        foreach (var path in new[] { "dinput8.ini", "global.ini", "UltimateASILoader.ini", "scripts/dinput8.ini", "scripts/global.ini", "scripts/UltimateASILoader.ini" })
            if (AttributesOrMissing(GameFile(root, path)) is not null) throw Invalid("Configurazione ASI loader non verificata: " + path);
        foreach (var relative in new[] { "", "scripts", "plugins" })
        {
            var directory = relative.Length == 0 ? root : GameFile(root, relative);
            if (AttributesOrMissing(directory) is null) continue;
            GuardPath(directory); var count = 0;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested(); GuardPath(path);
                if (++count > 256) throw Invalid("Troppi componenti da verificare nella cartella ASI.");
                if (!Path.GetExtension(path).Equals(".asi", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (name == WidescreenCatalog.Plugin || name == DirectXTextureCatalog.LoaderRelativePath && HashFile(path, token) == _loaderHash) continue;
                throw Invalid("Plugin ASI preesistente non verificato o in conflitto: " + name);
            }
        }
        foreach (var name in new[] { "dxgi.dll", "d3d11.dll", "d3d12.dll", "version.dll", "winmm.dll", "opengl32.dll" })
            if (AttributesOrMissing(GameFile(root, name)) is not null) throw Invalid("DLL proxy aggiuntiva non verificata: " + name);
        if (AttributesOrMissing(GameFile(root, "d3d9.dll")) is not null)
        {
            var fx = new ReShadeProfileService(_store, _running).Status(report.ExecutablePath);
            var file = fx.State?.Files.SingleOrDefault(f => f.Path.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase));
            if (fx.Pending || fx.State?.Installed != true || file is null || fx.State.ExecutableSha256 != report.Sha256
                || Live(root, "d3d9.dll", token) != file.Sha256) throw Invalid("ReShade/DLL DirectX preesistente non verificata da GamePanel.");
        }
    }
    private void VerifyRuntime(string exe, DirectXTextureState state, CancellationToken token)
    {
        ValidateState(state, exe); var root = Path.GetDirectoryName(exe)!;
        if (Live(root, DirectXTextureCatalog.LoaderRelativePath, token) != _loaderHash
            || new FileInfo(GameFile(root, DirectXTextureCatalog.LoaderRelativePath)).Length != _loaderBytes)
            throw Invalid("Runtime texture DirectX modificato o mancante.");
        _ = ReadConfig(GameFile(root, ConfigRelativePath), state.ResourceRoot); CheckDataPaths(state.ResourceRoot);
    }

    private static byte[] EncodeConfig(string text) => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
    private static (string Text, bool Enabled) ReadConfig(string path, string resourceRoot)
    {
        var bytes = ReadBounded(path, 64 * 1024); string text;
        try
        {
            text = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })
                ? new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2)
                : new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
        }
        catch (DecoderFallbackException e) { throw new InvalidDataException("Configurazione texture DirectX non leggibile.", e); }
        if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t')) throw Invalid("Caratteri non validi nella configurazione DirectX.");
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            { section = line[1..^1]; if (section is not ("TextureToolkit" or "Mods" or "ModEnabled") || !sections.Add(section)) throw Invalid("Sezione INI DirectX non riconosciuta o duplicata."); continue; }
            var eq = line.IndexOf('='); if (eq < 1 || section.Length == 0) throw Invalid("Riga INI DirectX non valida.");
            var key = line[..eq].Trim(); var value = line[(eq + 1)..].Trim();
            if (!keys.TryAdd(section + "/" + key, value)) throw Invalid("Chiave INI DirectX duplicata.");
        }
        if (!keys.TryGetValue("TextureToolkit/ResourceRoot", out var configured) || !SamePath(configured, resourceRoot)) throw Invalid("La cartella texture nell'INI è diversa da quella gestita.");
        if (!keys.TryGetValue("TextureToolkit/EnableInjection", out var enabled) || enabled is not ("0" or "1")) throw Invalid("Stato sostituzioni DirectX non valido.");
        foreach (var key in new[] { "AutoDump", "FilterSmallTextures", "ShowCurrentFrameOnly", "AcceptSpecialKNames", "HighlightSelected", "ShowOSDBanner", "Verbose" })
            if (keys.TryGetValue("TextureToolkit/" + key, out var value) && value is not ("0" or "1")) throw Invalid("Parametro DirectX non valido: " + key);
        return (text, enabled == "1");
    }
    private static void CheckDataPaths(string root)
    {
        foreach (var path in new[] { root, Path.Combine(root, "dump"), Path.Combine(root, "inject") })
        {
            GuardPath(path);
            if (AttributesOrMissing(path) is { } attrs && (attrs & FileAttributes.Directory) == 0) throw Invalid("Cartella texture occupata da un file.");
        }
    }
    private static void CheckReplacementFiles(string root, CancellationToken token)
    {
        CheckDataPaths(root); var directory = Path.Combine(root, "inject"); var count = 0;
        if (Directory.Exists(root))
        {
            var entries = 0;
            foreach (var path in Directory.EnumerateDirectories(root))
            {
                token.ThrowIfCancellationRequested(); GuardPath(path); var name = Path.GetFileName(path);
                if (++entries > 64 || !name.Equals("inject", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("dump", StringComparison.OrdinalIgnoreCase))
                    throw Invalid("La cartella texture contiene mod aggiuntive non gestite: attivazione sospesa.");
            }
        }
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested(); GuardPath(path);
                if (++count > 2048 || !Regex.IsMatch(Path.GetFileName(path), "^[A-Fa-f0-9]{16}\\.dds\\z", RegexOptions.CultureInvariant))
                    throw Invalid("La cartella sostituzioni contiene file non riconosciuti.");
                _ = DirectXTextureData.ReadDds(path);
            }
        if (count == 0) throw Invalid("Prepara almeno una sostituzione DDS verificata prima di attivarla.");
    }

    private DirectXTextureState? LoadState(string folder, string exe)
    {
        var path = Path.Combine(folder, StateFile); GuardPath(path); if (AttributesOrMissing(path) is null) return null;
        var state = ReadMetadata<DirectXTextureState>(path); ValidateState(state, exe); return state;
    }
    private Operation? LoadOperation(string folder, string exe)
    {
        var path = Path.Combine(folder, OperationFile); GuardPath(path); if (AttributesOrMissing(path) is null) return null;
        var op = ReadMetadata<Operation>(path); ValidateOperation(op, exe); return op;
    }
    private void ValidateState(DirectXTextureState state, string exe)
    {
        if (state.SchemaVersion != 1 || !ValidId(state.Revision) || !SamePath(state.ExecutablePath, exe)
            || !ValidHash(state.ExecutableSha256) || !SamePath(state.ResourceRoot, ResourceRoot(exe)) || !state.Installed && state.EnableInjection)
            throw Invalid("Stato texture DirectX non valido.");
    }
    private void ValidateOperation(Operation op, string exe)
    {
        if (op.SchemaVersion != 1 || !ValidId(op.Id) || !SamePath(op.ExecutablePath, exe)
            || !ValidHash(op.ExecutableSha256) || op.Status is not ("Pending" or "Complete" or "RolledBack")
            || op.Next is null || op.Changes is null || op.Changes.Count > 2) throw Invalid("Journal texture DirectX non valido.");
        ValidateState(op.Next, exe); if (op.Previous is not null) ValidateState(op.Previous, exe);
        if (op.Next.ExecutableSha256 != op.ExecutableSha256 || op.Previous?.Installed == true && op.Previous.ExecutableSha256 != op.ExecutableSha256)
            throw Invalid("Identità EXE nel journal DirectX non coerente.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in op.Changes)
        {
            if (c is null || !Targets.Contains(c.Path, StringComparer.Ordinal) || !paths.Add(c.Path)
                || c.BeforeSha256 is not null && !ValidHash(c.BeforeSha256) || c.AfterSha256 is not null && !ValidHash(c.AfterSha256)
                || c.BeforeSha256 == c.AfterSha256) throw Invalid("Percorso o hash nel journal DirectX non valido.");
            if (c.Path == DirectXTextureCatalog.LoaderRelativePath
                && (c.BeforeSha256 is not null && c.BeforeSha256 != _loaderHash || c.AfterSha256 is not null && c.AfterSha256 != _loaderHash))
                throw Invalid("Identità runtime DirectX nel journal non approvata.");
            if ((op.Previous?.Installed == true) != (c.BeforeSha256 is not null) || op.Next.Installed != (c.AfterSha256 is not null))
                throw Invalid("Transizione DirectX non coerente con i file gestiti.");
        }
        if ((op.Previous?.Installed == true) != op.Next.Installed && paths.Count != 2) throw Invalid("Transizione runtime DirectX incompleta.");
    }
    private void ValidatePayloads(string folder, Operation op, CancellationToken token)
    {
        var payload = Path.Combine(folder, "directx-" + op.Id); GuardPath(payload);
        for (var i = 0; i < op.Changes.Count; i++)
            foreach (var after in new[] { false, true })
            {
                var c = op.Changes[i]; var hash = after ? c.AfterSha256 : c.BeforeSha256; if (hash is null) continue;
                var path = Payload(payload, i, after);
                if (HashFile(path, token) != hash) throw Invalid("Backup texture DirectX danneggiato.");
                if (c.Path == ConfigRelativePath)
                {
                    var config = ReadConfig(path, ResourceRoot(op.ExecutablePath));
                    if (config.Enabled != (after ? op.Next.EnableInjection : op.Previous!.EnableInjection))
                        throw Invalid("Configurazione DirectX nel journal diversa dallo stato dichiarato.");
                }
                else if (new FileInfo(path).Length != _loaderBytes) throw Invalid("Dimensione runtime DirectX nel backup non valida.");
            }
    }
    private void CheckNoPending(string folder, bool allowReplacementRecovery = false)
    {
        CheckPeers(folder, allowReplacementRecovery); var path = Path.Combine(folder, OperationFile);
        if (AttributesOrMissing(path) is not null && ReadStatus(path) == "Pending") throw new InvalidOperationException("Riprendi o annulla l'operazione texture DirectX interrotta.");
    }
    private static void CheckPeers(string folder, bool allowReplacementRecovery = false)
    {
        foreach (var file in new[] { "operation.json", "widescreen-operation.json", "assets-operation.json", "directx-replacements-operation.json" })
        {
            if (allowReplacementRecovery && file == "directx-replacements-operation.json") continue;
            var path = Path.Combine(folder, file); GuardPath(path);
            if (AttributesOrMissing(path) is not null && ReadStatus(path) == "Pending") throw new InvalidOperationException("Prima recupera l'operazione immagine/schermo/asset interrotta.");
        }
    }
    private static string? ReadStatus(string path)
    {
        using var doc = ReadDocument(path);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Status", out var status)
            || status.ValueKind != JsonValueKind.String) throw Invalid("Journal condiviso privo di stato valido.");
        var value = status.GetString();
        if (value is not ("Pending" or "Complete" or "RolledBack")) throw Invalid("Stato journal condiviso non riconosciuto.");
        return value;
    }
    private static T ReadMetadata<T>(string path)
    { using var doc = ReadDocument(path); try { return doc.RootElement.Deserialize<T>() ?? throw Invalid("Metadati DirectX mancanti."); } catch (JsonException e) { throw new InvalidDataException("Metadati DirectX non validi.", e); } }
    private static JsonDocument ReadDocument(string path)
    {
        var bytes = ReadBounded(path, MaximumMetadataBytes); JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes, new() { MaxDepth = 12 }); }
        catch (JsonException e) { throw new InvalidDataException("JSON DirectX non valido.", e); }
        try { Duplicates(doc.RootElement); return doc; } catch { doc.Dispose(); throw; }
        static void Duplicates(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object) { var keys = new HashSet<string>(StringComparer.Ordinal); foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name)) throw Invalid("Proprietà JSON DirectX duplicata."); Duplicates(p.Value); } }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Duplicates(item);
        }
    }
    private string Folder(string exe)
    {
        var root = Path.GetDirectoryName(Full(exe))!; GuardPath(root); GuardPath(_store); GuardPath(_dataRoot);
        if (Inside(root, _store) || Inside(_store, root) || Inside(root, _dataRoot) || Inside(_dataRoot, root)) throw Invalid("Gioco, profili e cartella texture devono essere separati.");
        var folder = Path.Combine(_store, GameKey(root)); GuardPath(folder); return folder;
    }
    private string ResourceRoot(string exe)
    { _ = Folder(exe); var path = Path.Combine(_dataRoot, GameKey(Path.GetDirectoryName(Full(exe))!)); GuardPath(path); if (path.Any(char.IsControl)) throw Invalid("Percorso texture non valido."); return path; }
    private static string GameKey(string root) => Hash(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root));
    private static string Full(string path) => Path.GetFullPath(ExecutablePath.Normalize(path));
    private static bool SamePath(string? a, string b) => a is not null && string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static IReadOnlyList<string> Targets => [DirectXTextureCatalog.LoaderRelativePath, ConfigRelativePath];
    private static string? Live(string root, string relative, CancellationToken token) { var path = GameFile(root, relative); return AttributesOrMissing(path) is null ? null : HashFile(path, token); }
    private static byte[] ReadBounded(string path, int maximum)
    {
        GuardPath(path); using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length < 1 || input.Length > maximum) throw Invalid("File texture DirectX oltre i limiti.");
        var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); if (input.ReadByte() != -1) throw Invalid("File DirectX cambiato durante la lettura."); return bytes;
    }
    private static string Payload(string folder, int i, bool after) { var path = Path.Combine(folder, $"{i:D4}-{(after ? "after" : "before")}.bin"); GuardPath(path); return path; }
    private static FileStream Acquire(string folder) { GuardPath(folder); var path = Path.Combine(folder, ".operation.lock"); GuardPath(path); return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private static void CopyExpected(string source, string destination, string hash, CancellationToken token) { if (CopyVerified(source, destination, token, MaximumFileBytes).Hash != hash) throw Invalid("Copie DirectX diverse dalla sorgente."); }
    private static void DurableBytes(string path, byte[] bytes) { GuardPath(path); using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); output.Write(bytes); output.Flush(true); }
    private static bool ValidId(string? value) => value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static InvalidDataException Invalid(string message) => new(message);
}
