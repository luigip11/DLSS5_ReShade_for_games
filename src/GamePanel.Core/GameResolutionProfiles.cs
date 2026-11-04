using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public enum ResolutionMethod { Underground1, Unity, Unreal, Ini }
public sealed record GameResolutionOptions(ResolutionMethod Method, int Width, int Height, string? DisplayId = null,
    string? ConfigPath = null, string Section = "", string WidthKey = "Width", string HeightKey = "Height");
public sealed record ResolutionFile(string Path, byte[]? Original, string? CurrentHash);
public sealed record GameResolutionState(int SchemaVersion, string Executable, string ExecutableHash,
    GameResolutionOptions Options, IReadOnlyList<ResolutionFile> Files);
public sealed record ResolutionChange(string Path, byte[]? Before, byte[]? After);
public sealed class GameResolutionPlan
{
    internal GameResolutionPlan(string owner, GameReport report, GameResolutionState? previous, GameResolutionState? next,
        IReadOnlyList<ResolutionChange> changes, IReadOnlyList<string> preserved)
    { Owner = owner; Report = report; Previous = previous; Next = next; Changes = changes; PreservedFiles = preserved; }
    internal string Owner { get; }
    internal GameReport Report { get; }
    internal GameResolutionState? Previous { get; }
    internal GameResolutionState? Next { get; }
    public IReadOnlyList<ResolutionChange> Changes { get; }
    public IReadOnlyList<string> PreservedFiles { get; }
    public bool Removing => Next is null;
    public GameResolutionOptions Options => (Next ?? Previous)!.Options;
}

/// <summary>Per-game adapters. Never changes Windows display modes or runs shell scripts.</summary>
public sealed class GameResolutionProfiles
{
    private const int Limit = 64 * 1024 * 1024;
    private readonly string _store, _commonData, _ug1Hash, _owner = Guid.NewGuid().ToString("N");
    private readonly Func<string, bool> _running;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record Operation(int SchemaVersion, string Executable, string ExecutableHash, string Status,
        GameResolutionState? Previous, GameResolutionState? Next, IReadOnlyList<ResolutionChange> Changes);
    public GameResolutionProfiles(string store) : this(store, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), IsGameRunning, Underground1DisplayFix.ExecutableSha256) { }
    internal GameResolutionProfiles(string store, string commonData, Func<string, bool> running, string ug1Hash)
    { _store = Path.GetFullPath(store); _commonData = Path.GetFullPath(commonData); _running = running; _ug1Hash = ug1Hash; }

    public GameResolutionState? Load(string executable)
    {
        var path = Path.Combine(Folder(executable), "state.json");
        var state = Read<GameResolutionState>(path); if (state is not null) ValidateState(state, Full(executable)); return state;
    }
    public bool HasPending(string executable) => LoadOperation(Full(executable))?.Status == "Pending";
    public IReadOnlyList<string> LaunchArguments(GameReport report)
    {
        if (HasPending(report.ExecutablePath)) throw Invalid("Recupera il profilo di risoluzione prima di avviare il gioco.");
        var state = Load(report.ExecutablePath); if (state is null) return [];
        CheckExecutable(report.ExecutablePath, state.ExecutableHash);
        return Arguments(state.Options);
    }
    public static IReadOnlyList<string> Arguments(GameResolutionOptions o)
    {
        ValidateOptions(o); var w = o.Width.ToString(CultureInfo.InvariantCulture); var h = o.Height.ToString(CultureInfo.InvariantCulture);
        return o.Method switch
        {
            ResolutionMethod.Unity => ["-screen-width", w, "-screen-height", h, "-screen-fullscreen", "1"],
            ResolutionMethod.Unreal => ["-ResX=" + w, "-ResY=" + h, "-Fullscreen"],
            _ => []
        };
    }
    public GameResolutionPlan Prepare(GameReport report, GameResolutionOptions options, Underground1DisplayFix? package = null)
    {
        ValidateOptions(options); var executable = Full(report.ExecutablePath); Stopped(executable); CheckExecutable(executable, report.Sha256);
        if (HasPending(executable)) throw Invalid("Recupera prima l'operazione risoluzione interrotta.");
        var previous = Load(executable);
        if (previous is not null && (previous.Options.Method != options.Method || !SamePath(previous.Options.ConfigPath, options.ConfigPath)))
            throw Invalid("Rimuovi il profilo attuale prima di cambiare metodo o file di configurazione.");
        var output = new Dictionary<string, byte[]>();
        if (options.Method == ResolutionMethod.Underground1)
        {
            if (report.Sha256 != _ug1Hash || report.Architecture != "x86 (32 bit)" || !report.GraphicsApis.Contains("Direct3D 9") || report.HasAntiCheatIndicators || report.ScanWasLimited)
                throw Invalid("Questo fix richiede l'EXE Underground 1 identificato e un'analisi completa.");
            var gameRoot = Path.GetDirectoryName(executable)!;
            if (Inside(gameRoot, _store) || Inside(_store, gameRoot)) throw Invalid("Gioco e dati di GamePanel devono essere separati.");
            foreach (var name in new[] { "GLOBAL", "TRACKS", "CARS", "MOVIES" })
            { var path = GameFile(gameRoot, name); GuardPath(path); if (!Directory.Exists(path)) throw Invalid("Cartella Underground 1 incompleta: manca " + name); }
            if (Directory.Exists(GameFile(gameRoot, ".trex")) || File.Exists(GameFile(gameRoot, "rtx.conf"))) throw Invalid("Questo profilo UG1 è per il gioco originale; la variante Remix richiede una propria integrazione.");
            if (previous is null)
            {
                foreach (var name in new[] { "version.dll", "winmm.dll", "dsound.dll", "d3d8.dll", "ddraw.dll" })
                    if (File.Exists(GameFile(gameRoot, name))) throw Invalid("Loader o wrapper già presente: " + name);
                var scripts = GameFile(gameRoot, "scripts"); GuardPath(scripts);
                if (Directory.EnumerateFiles(gameRoot).Any(p => Path.GetExtension(p).Equals(".asi", StringComparison.OrdinalIgnoreCase))
                    || Directory.Exists(scripts) && Directory.EnumerateFileSystemEntries(scripts).Any(p => !Path.GetFileName(p).Equals("NFSUnderground.WidescreenFix.ini", StringComparison.OrdinalIgnoreCase)))
                    throw Invalid("Sono già presenti plugin ASI: verifica la configurazione prima di installare il fix UG1.");
            }
            if (package is null) throw Invalid("Prepara prima il pacchetto UG1 verificato.");
            if (!package.Files.Keys.Order().SequenceEqual(Underground1DisplayFix.Entries.Keys.Order())) throw Invalid("Inventario UG1 non valido.");
            foreach (var (name, bytes) in package.Files) output[GameFile(Path.GetDirectoryName(executable)!, name)] = bytes.ToArray();
            output[Ug1ResolutionFile] = Encoding.ASCII.GetBytes("RES: " + Pack(options.Width, options.Height).ToString(CultureInfo.InvariantCulture) + "\r\n");
        }
        if (options.Method == ResolutionMethod.Ini)
        {
            var path = ConfigPath(options, executable);
            var before = Bytes(path) ?? throw Invalid("Scegli un file INI/CFG esistente.");
            output[path] = PatchIni(before, options);
        }
        var changes = new List<ResolutionChange>(); var files = new List<ResolutionFile>();
        foreach (var (path, bytes) in output)
        {
            var before = Bytes(path); var old = previous?.Files.SingleOrDefault(f => SamePath(f.Path, path));
            var observedChange = old is not null && Hash(before) != old.CurrentHash;
            if (observedChange && (before is null || !Mutable(options, path))) throw Invalid("File modificato fuori da GamePanel: " + path);
            if (old is null && before is not null && options.Method == ResolutionMethod.Underground1 && !Mutable(options, path))
                throw Invalid("Componente già presente: conserva la tua installazione e verifica prima il file " + path);
            var after = options.Method == ResolutionMethod.Underground1 && path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && before is not null ? before : bytes;
            if (options.Method == ResolutionMethod.Underground1 && path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) ValidateText(after);
            files.Add(new(path, observedChange ? before : old?.Original ?? (old is null ? before : null), Hash(after)));
            changes.Add(new(path, before, after));
        }
        var next = new GameResolutionState(1, executable, report.Sha256, options, files);
        ValidateState(next, executable);
        return new(_owner, report, previous, next, changes, []);
    }
    public GameResolutionPlan PrepareRemove(GameReport report)
    {
        var executable = Full(report.ExecutablePath); Stopped(executable); CheckExecutable(executable, report.Sha256);
        if (HasPending(executable)) throw Invalid("Recupera prima l'operazione risoluzione interrotta.");
        var state = Load(executable) ?? throw Invalid("Nessun profilo risoluzione salvato.");
        var changes = new List<ResolutionChange>(); var preserved = new List<string>();
        foreach (var f in state.Files)
        {
            var live = Bytes(f.Path);
            if (Hash(live) != f.CurrentHash) preserved.Add(f.Path);
            else changes.Add(new(f.Path, live, f.Original));
        }
        return new(_owner, report, state, null, changes, preserved);
    }
    public GameResolutionState? Execute(GameResolutionPlan plan, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        if (plan.Owner != _owner) throw Invalid("Anteprima di un'altra sessione: preparala di nuovo.");
        var executable = Full(plan.Report.ExecutablePath); Stopped(executable); CheckExecutable(executable, plan.Report.Sha256);
        var folder = Folder(executable); GuardPath(folder); Directory.CreateDirectory(folder); GuardPath(folder);
        var lockPath = Path.Combine(folder, ".lock"); GuardPath(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (HasPending(executable) || !SameState(Load(executable), plan.Previous)) throw Invalid("Il profilo è cambiato: prepara di nuovo l'anteprima.");
        foreach (var change in plan.Changes) if (Hash(Bytes(change.Path)) != Hash(change.Before)) throw Invalid("File cambiato dopo l'anteprima: " + change.Path);
        token.ThrowIfCancellationRequested();
        var operation = new Operation(1, executable, plan.Report.Sha256, "Pending", plan.Previous, plan.Next, plan.Changes);
        ValidateOperation(operation, executable); WriteJson(Path.Combine(folder, "operation.json"), operation);
        return Run(operation, false, token, progress);
    }
    public GameResolutionState? Recover(GameReport report, bool rollback, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var executable = Full(report.ExecutablePath); Stopped(executable);
        var folder = Folder(executable); GuardPath(folder);
        var lockPath = Path.Combine(folder, ".lock"); GuardPath(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var operation = LoadOperation(executable) ?? throw Invalid("Nessuna operazione risoluzione da recuperare.");
        CheckExecutable(executable, operation.ExecutableHash);
        return operation.Status == "Pending" ? Run(operation, rollback, token, progress) : Load(executable);
    }
    private GameResolutionState? Run(Operation operation, bool rollback, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var stored = Load(operation.Executable);
        if (!SameState(stored, operation.Previous) && !SameState(stored, operation.Next)) throw Invalid("Stato risoluzione modificato fuori dall'operazione.");
        foreach (var c in operation.Changes)
        { var hash = Hash(Bytes(c.Path)); if (hash != Hash(c.Before) && hash != Hash(c.After)) throw Invalid("File modificato fuori dall'operazione: " + c.Path); }
        for (var i = 0; i < operation.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); Stopped(operation.Executable); CheckExecutable(operation.Executable, operation.ExecutableHash);
            var c = operation.Changes[i]; var live = Hash(Bytes(c.Path));
            if (live != Hash(c.Before) && live != Hash(c.After)) throw Invalid("File cambiato durante l'operazione: " + c.Path);
            var target = rollback ? c.Before : c.After;
            if (live != Hash(target)) Replace(c.Path, target);
            progress?.Report(new("Risoluzione: " + Path.GetFileName(c.Path), i + 1, operation.Changes.Count));
        }
        token.ThrowIfCancellationRequested(); Stopped(operation.Executable); CheckExecutable(operation.Executable, operation.ExecutableHash);
        foreach (var c in operation.Changes) if (Hash(Bytes(c.Path)) != Hash(rollback ? c.Before : c.After)) throw Invalid("Verifica finale della risoluzione fallita.");
        stored = Load(operation.Executable);
        if (!SameState(stored, operation.Previous) && !SameState(stored, operation.Next)) throw Invalid("Stato risoluzione cambiato prima del completamento.");
        var next = rollback ? operation.Previous : operation.Next;
        var path = Path.Combine(Folder(operation.Executable), "state.json"); GuardPath(path);
        if (next is null) { if (File.Exists(path)) File.Delete(path); } else WriteJson(path, next);
        WriteJson(Path.Combine(Folder(operation.Executable), "operation.json"), operation with { Status = rollback ? "RolledBack" : "Committed" });
        return next;
    }
    private Operation? LoadOperation(string executable)
    { var op = Read<Operation>(Path.Combine(Folder(executable), "operation.json")); if (op is not null) ValidateOperation(op, executable); return op; }
    private void ValidateOperation(Operation op, string executable)
    {
        if (op.SchemaVersion != 1 || !SamePath(op.Executable, executable) || !ValidHash(op.ExecutableHash) || op.Status is not ("Pending" or "Committed" or "RolledBack")
            || op.Previous is null && op.Next is null || op.Changes is null || op.Changes.Count > 6) throw Invalid("Journal risoluzione non valido.");
        if (op.Previous is not null) ValidateState(op.Previous, executable);
        if (op.Next is not null)
        { ValidateState(op.Next, executable); if (op.Next.ExecutableHash != op.ExecutableHash) throw Invalid("Identità risoluzione non coerente."); }
        var state = op.Next ?? op.Previous!; var allowed = Allowed(state.Options, executable);
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var c in op.Changes)
            if (c is null || !names.Add(c.Path) || !allowed.Any(a => SamePath(a, c.Path)) || c.Before?.Length > 16 * 1024 * 1024 || c.After?.Length > 16 * 1024 * 1024)
                throw Invalid("Destinazione del journal risoluzione non valida.");
        if (op.Next is not null && (op.Changes.Count != op.Next.Files.Count || op.Changes.Any(c => !op.Next.Files.Any(f => SamePath(f.Path, c.Path) && f.CurrentHash == Hash(c.After)))))
            throw Invalid("Payload del journal risoluzione non coerenti.");
        foreach (var c in op.Changes)
        {
            var prior = op.Previous?.Files.SingleOrDefault(f => SamePath(f.Path, c.Path));
            var next = op.Next?.Files.SingleOrDefault(f => SamePath(f.Path, c.Path));
            var observedChange = prior is not null && Hash(c.Before) != prior.CurrentHash;
            if (observedChange && !(op.Next is not null && c.Before is not null && Mutable(op.Next.Options, c.Path))
                || prior is null && Hash(c.Before) != Hash(next?.Original)
                || op.Next is null && Hash(c.After) != Hash(prior?.Original)
                || prior is not null && next is not null && Hash(next.Original) != (observedChange ? Hash(c.Before) : Hash(prior.Original)))
                throw Invalid("Backup del journal risoluzione non coerente.");
            if (op.Next?.Options.Method == ResolutionMethod.Ini && (c.Before is null || Hash(c.After) != Hash(PatchIni(c.Before, op.Next.Options))))
                throw Invalid("Modifiche INI del journal diverse dalle sole chiavi di risoluzione.");
            if (op.Next?.Options.Method == ResolutionMethod.Underground1 && c.Path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) ValidateText(c.After!);
        }
    }
    private void ValidateState(GameResolutionState state, string executable)
    {
        if (state.SchemaVersion != 1 || !SamePath(state.Executable, executable) || !ValidHash(state.ExecutableHash) || state.Options is null || state.Files is null) throw Invalid("Profilo risoluzione non valido.");
        ValidateOptions(state.Options); var allowed = Allowed(state.Options, executable);
        if (state.Files.Count != allowed.Count || state.Files.Any(f => f is null || !allowed.Any(p => SamePath(p, f.Path)) || !ValidHash(f.CurrentHash) || f.Original?.Length > 16 * 1024 * 1024)
            || state.Files.Select(f => f.Path.ToUpperInvariant()).Distinct().Count() != state.Files.Count) throw Invalid("Inventario risoluzione non valido.");
        if (state.Options.Method == ResolutionMethod.Underground1)
        {
            if (state.ExecutableHash != _ug1Hash) throw Invalid("EXE UG1 non identificato.");
            foreach (var file in state.Files)
            {
                if (SamePath(file.Path, Ug1ResolutionFile))
                { if (file.CurrentHash != Hash(Encoding.ASCII.GetBytes("RES: " + Pack(state.Options.Width, state.Options.Height).ToString(CultureInfo.InvariantCulture) + "\r\n"))) throw Invalid("Risoluzione UG1 non coerente."); }
                else
                {
                    var entry = Underground1DisplayFix.Entries.Single(p => SamePath(GameFile(Path.GetDirectoryName(executable)!, p.Key), file.Path));
                    if (entry.Key.EndsWith(".ini", StringComparison.Ordinal)) { if (file.Original is not null) ValidateText(file.Original); }
                    else if (file.CurrentHash != entry.Value.Hash || file.Original is not null) throw Invalid("Componente UG1 non corrispondente al catalogo.");
                }
            }
        }
    }
    private IReadOnlyList<string> Allowed(GameResolutionOptions o, string executable) => o.Method switch
    {
        ResolutionMethod.Underground1 => Underground1DisplayFix.Entries.Keys.Select(p => GameFile(Path.GetDirectoryName(executable)!, p)).Append(Ug1ResolutionFile).ToArray(),
        ResolutionMethod.Ini => [ConfigPath(o, executable)],
        _ => []
    };
    private string ConfigPath(GameResolutionOptions o, string executable)
    {
        if (string.IsNullOrWhiteSpace(o.ConfigPath) || !Path.IsPathFullyQualified(o.ConfigPath)) throw Invalid("Seleziona il file INI/CFG del gioco.");
        var path = Path.GetFullPath(o.ConfigPath); GuardPath(path);
        if (!new[] { ".ini", ".cfg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw Invalid("Sono ammessi soltanto file INI o CFG di testo.");
        var game = Path.GetDirectoryName(executable)!;
        var roots = new[] { game, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (!roots.Where(p => !string.IsNullOrEmpty(p)).Any(p => Inside(path, p)) || Inside(path, _store)) throw Invalid("Seleziona una configurazione nella cartella del gioco, Documenti o AppData locale.");
        return path;
    }
    public static int Pack(int width, int height)
    { ValidateOptions(new(ResolutionMethod.Underground1, width, height)); return unchecked((int)(0x80000000u | ((uint)width << 16) | (uint)height)); }
    public static byte[] PatchIni(byte[] bytes, GameResolutionOptions o)
    {
        ValidateOptions(o); if (bytes.Length > 512 * 1024) throw Invalid("Configurazione troppo grande.");
        var bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
        string text; try { text = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bom ? 3 : 0)); } catch (DecoderFallbackException) { throw Invalid("Configurazione non UTF-8: richiede un adattatore dedicato."); }
        if (text.Contains('\0')) throw Invalid("Il file non è una configurazione INI di testo.");
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"; var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var section = ""; var width = -1; var height = -1; var foundSections = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim(); if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); if (section.Equals(o.Section, StringComparison.OrdinalIgnoreCase)) foundSections++; continue; }
            if (!section.Equals(o.Section, StringComparison.OrdinalIgnoreCase) || line.StartsWith(';') || line.StartsWith('#')) continue;
            var eq = line.IndexOf('='); if (eq < 0) continue; var key = line[..eq].Trim();
            if (key.Equals(o.WidthKey, StringComparison.OrdinalIgnoreCase)) { if (width >= 0) throw Invalid("Chiave larghezza duplicata."); width = i; }
            if (key.Equals(o.HeightKey, StringComparison.OrdinalIgnoreCase)) { if (height >= 0) throw Invalid("Chiave altezza duplicata."); height = i; }
        }
        if (width < 0 || height < 0 || foundSections > 1) throw Invalid("Sezione o chiavi non trovate, oppure duplicate. Verifica il profilo INI.");
        string Set(string line, int value)
        {
            var eq = line.IndexOf('='); var tail = line[(eq + 1)..]; var comment = tail.IndexOfAny([';', '#']);
            var leading = tail[..(tail.Length - tail.TrimStart().Length)];
            return line[..(eq + 1)] + leading + value.ToString(CultureInfo.InvariantCulture) + (comment < 0 ? "" : " " + tail[comment..]);
        }
        lines[width] = Set(lines[width], o.Width); lines[height] = Set(lines[height], o.Height);
        var output = Encoding.UTF8.GetBytes(string.Join(newline, lines)); return bom ? new byte[] { 239, 187, 191 }.Concat(output).ToArray() : output;
    }
    private static void ValidateOptions(GameResolutionOptions o)
    {
        if (o is null || !Enum.IsDefined(o.Method) || o.Width is < 320 or > 16384 || o.Height is < 200 or > 16384
            || o.DisplayId?.Length > 1024 || o.DisplayId?.Any(char.IsControl) == true) throw Invalid("Risoluzione o monitor non validi.");
        foreach (var value in new[] { o.Section, o.WidthKey, o.HeightKey })
            if (value is null || value.Length > 128 || value.Any(c => char.IsControl(c) || c is '[' or ']' or '=')) throw Invalid("Sezione o chiave INI non valida.");
        if (string.IsNullOrWhiteSpace(o.WidthKey) || string.IsNullOrWhiteSpace(o.HeightKey) || o.WidthKey.Equals(o.HeightKey, StringComparison.OrdinalIgnoreCase)) throw Invalid("Le due chiavi di risoluzione devono essere distinte.");
    }
    private string Ug1ResolutionFile => Path.Combine(_commonData, "NFS Underground", "RES.cfg2");
    private bool Mutable(GameResolutionOptions options, string path) => options.Method == ResolutionMethod.Ini
        || options.Method == ResolutionMethod.Underground1 && (SamePath(path, Ug1ResolutionFile) || path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    private static void ValidateText(byte[] bytes)
    {
        if (bytes.Length > 512 * 1024) throw Invalid("Configurazione di testo troppo grande.");
        try { if (new UTF8Encoding(false, true).GetString(bytes).Contains('\0')) throw Invalid("Configurazione di testo non valida."); }
        catch (DecoderFallbackException) { throw Invalid("Configurazione di testo non UTF-8."); }
    }
    private string Folder(string executable) { GuardPath(_store); return Path.Combine(_store, Hash(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? Full(executable).ToUpperInvariant() : Full(executable)))!); }
    private void Stopped(string executable) { if (_running(executable)) throw Invalid("Chiudi il gioco prima di modificare il profilo di risoluzione."); }
    private static void CheckExecutable(string executable, string expected)
    {
        GuardPath(executable);
        if (!ValidHash(expected) || new FileInfo(executable).Length > 1024L * 1024 * 1024) throw Invalid("EXE non valido.");
        using var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw Invalid("EXE cambiato: analizza di nuovo il gioco.");
    }
    private static string Full(string executable) => Path.GetFullPath(ExecutablePath.Normalize(executable));
    private static bool SamePath(string? a, string? b) => string.Equals(a, b, PathComparison);
    private static bool SameState(GameResolutionState? a, GameResolutionState? b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
    private static string? Hash(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[]? Bytes(string path)
    {
        GuardPath(path); var attributes = AttributesOrMissing(path); if (attributes is null) return null;
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0 || new FileInfo(path).Length > 16 * 1024 * 1024) throw Invalid("File non valido o troppo grande: " + path);
        return File.ReadAllBytes(path);
    }
    private static T? Read<T>(string path) where T : class
    {
        GuardPath(path); if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > Limit) throw Invalid("Metadati risoluzione troppo grandi.");
        try { return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path)) ?? throw Invalid("Metadati risoluzione assenti."); }
        catch (JsonException e) { throw new InvalidDataException("Metadati risoluzione non leggibili.", e); }
    }
    private static void Replace(string path, byte[]? bytes)
    {
        GuardPath(path);
        if (bytes is null) { File.Delete(path); return; }
        var parent = Path.GetDirectoryName(path)!; Directory.CreateDirectory(parent); GuardPath(path);
        var temp = Path.Combine(parent, ".gamepanel-resolution-" + Guid.NewGuid().ToString("N"));
        try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); } GuardPath(path); File.Move(temp, path, true); }
        finally { GuardPath(temp); if (File.Exists(temp)) File.Delete(temp); }
    }
    private static InvalidDataException Invalid(string message) => new(message);
}
