using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GamePanel.Core;

public sealed record BackupFile(string RelativePath, bool Existed, string? Sha256, long Length);
public sealed record BackupManifest(int SchemaVersion, string Id, DateTimeOffset CreatedAt,
    string ExecutablePath, string ExecutableSha256, IReadOnlyList<BackupFile> Files);
public sealed record BackupSummary(string Id, DateTimeOffset CreatedAt, int FileCount, long Bytes);
public enum RestoreDisposition { Identical, Missing, OriginallyAbsent, Conflict }
public sealed record RestoreFile(string RelativePath, RestoreDisposition Disposition);
public sealed record RestorePlan(string SnapshotId, IReadOnlyList<RestoreFile> Files, bool HasPendingRepair = false)
{
    public bool HasConflicts => Files.Any(f => f.Disposition == RestoreDisposition.Conflict);
    public int MissingCount => Files.Count(f => f.Disposition == RestoreDisposition.Missing);
}
public sealed record BackupProgress(string Message, int Completed, int Total);
public sealed record RestoreResult(int Restored, int Verified, bool Resumed);

/// <summary>
/// Baseline snapshots and non-overwriting repair of missing files. No DLL deployment yet.
/// A journal makes an interrupted repair resumable; changed existing files always block it.
/// </summary>
public sealed class BackupService
{
    public const int MaximumFiles = 256;
    public const long MaximumFileBytes = 512L * 1024 * 1024;
    public const long MaximumSnapshotBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly string[] FutureTargets =
    [ "d3d9.dll", "dxgi.dll", "d3d11.dll", "d3d12.dll", "dinput8.dll", "version.dll", "winmm.dll",
      "opengl32.dll", "ReShade.ini", "ReShadePreset.ini", "dgVoodoo.conf", "dxvk.conf" ];
    private readonly string _store;
    private readonly Func<string, bool> _gameIsRunning;

    private sealed record RepairJournal(int SchemaVersion, string SnapshotId, string OperationId,
        string State, IReadOnlyList<string> CompletedFiles);

    public BackupService(string store, Func<string, bool>? gameIsRunning = null)
    {
        _store = Canonical(store);
        _gameIsRunning = gameIsRunning ?? IsGameRunning;
    }

    public static string DefaultStore
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Cartella dati utente non disponibile.");
            return Path.Combine(local, "GamePanel", "Backups");
        }
    }
    public string StorePath => _store;

    public BackupManifest Create(GameReport report, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var executable = FullExecutable(report.ExecutablePath);
        var gameRoot = Path.GetDirectoryName(executable)!;
        CheckRoots(gameRoot);
        EnsureStopped(executable);
        if (!ValidHash(report.Sha256)) throw new InvalidDataException("Hash del report non valido: rianalizza il gioco.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileName(executable) };
        foreach (var c in report.Components) paths.Add(ValidateRelative(c.RelativePath));
        foreach (var target in FutureTargets) paths.Add(target);
        if (paths.Count > MaximumFiles) throw new InvalidDataException($"Troppi file per questo backup (massimo {MaximumFiles}).");
        var selected = paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var gameStore = GameStore(executable);
        GuardPath(gameStore); Directory.CreateDirectory(gameStore); GuardPath(gameStore);
        using var operationLock = Lock(gameStore);
        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(gameStore, id + ".pending");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(Path.Combine(staging, "files"));
        var files = new List<BackupFile>();
        long total = 0;
        for (var i = 0; i < selected.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            EnsureStopped(executable);
            var relative = ValidateRelative(selected[i]);
            var source = GameFile(gameRoot, relative);
            var attributes = AttributesOrMissing(source);
            if (attributes is null) files.Add(new(relative, false, null, 0));
            else
            {
                if (attributes.Value.HasFlag(FileAttributes.Directory)) throw new InvalidDataException($"È una cartella, non un file: {relative}");
                var destination = Payload(staging, i);
                var copied = CopyVerified(source, destination, token, MaximumSnapshotBytes - total);
                total = checked(total + copied.Length);
                files.Add(new(relative, true, copied.Hash, copied.Length));
            }
            progress?.Report(new($"Backup: {relative}", i + 1, selected.Length));
        }
        var exe = files.Single(f => string.Equals(f.RelativePath, Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase));
        if (!exe.Existed || !string.Equals(exe.Sha256, report.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L'eseguibile è cambiato dopo l'analisi. Rianalizza prima di creare il backup.");
        // Validate the complete live baseline before committing a snapshot; no game writes.
        foreach (var item in files)
        {
            token.ThrowIfCancellationRequested();
            var current = GameFile(gameRoot, item.RelativePath);
            var exists = AttributesOrMissing(current) is not null;
            if (exists != item.Existed || (exists && HashFile(current, token) != item.Sha256))
                throw new InvalidDataException($"File cambiato durante il backup: {item.RelativePath}");
        }
        EnsureStopped(executable);
        var manifest = new BackupManifest(1, id, DateTimeOffset.UtcNow, executable, report.Sha256.ToLowerInvariant(), files);
        WriteJson(Path.Combine(staging, "snapshot.json"), manifest);
        token.ThrowIfCancellationRequested();
        GuardPath(staging);
        Directory.Move(staging, Path.Combine(gameStore, id));
        return manifest;
    }

    public IReadOnlyList<BackupSummary> List(string executablePath)
    {
        var executable = FullExecutable(executablePath);
        CheckRoots(Path.GetDirectoryName(executable)!);
        var store = GameStore(executable);
        GuardPath(store);
        if (!Directory.Exists(store)) return [];
        var summaries = new List<BackupSummary>();
        var directories = new DirectoryInfo(store).EnumerateDirectories().Take(1001).ToArray();
        if (directories.Length > 1000) throw new InvalidDataException("Troppi backup nella cartella del gioco: elenco non caricato.");
        foreach (var directory in directories)
        {
            if (!ValidId(directory.Name)) continue; // Incomplete snapshots are never offered.
            var manifest = Load(executable, directory.Name);
            summaries.Add(new(manifest.Id, manifest.CreatedAt, manifest.Files.Count(f => f.Existed), manifest.Files.Sum(f => f.Length)));
        }
        return summaries.OrderByDescending(s => s.CreatedAt).ToArray();
    }

    public RestorePlan Preview(string executablePath, string snapshotId, CancellationToken token = default)
    {
        var executable = FullExecutable(executablePath);
        EnsureStopped(executable);
        var plan = Plan(executable, Load(executable, snapshotId), token);
        var journal = ReadJournal(Path.Combine(SnapshotFolder(executable, snapshotId), "repair.json"), snapshotId);
        return plan with { HasPendingRepair = journal?.State == "Pending" };
    }

    public RestoreResult Restore(string executablePath, string snapshotId, CancellationToken token = default,
        IProgress<BackupProgress>? progress = null)
    {
        var executable = FullExecutable(executablePath);
        var root = Path.GetDirectoryName(executable)!;
        var manifest = Load(executable, snapshotId);
        var folder = SnapshotFolder(executable, snapshotId);
        using var operationLock = Lock(GameStore(executable));
        EnsureStopped(executable);
        var plan = Plan(executable, manifest, token);
        if (plan.HasConflicts) throw new InvalidDataException("Ripristino bloccato: alcuni file presenti differiscono dal backup. Nessun file è stato sovrascritto.");
        using var baseline = new BaselineLease(root, manifest, plan, token);
        if (Plan(executable, manifest, token, verifyPayloads: false).HasConflicts)
            throw new InvalidDataException("File cambiati durante la preparazione del ripristino.");
        var journalPath = Path.Combine(folder, "repair.json");
        var existing = ReadJournal(journalPath, snapshotId);
        var resumed = existing is { State: "Pending" };
        var journal = resumed ? existing! : new RepairJournal(1, snapshotId, Guid.NewGuid().ToString("N"), "Pending", []);
        var completed = new HashSet<string>(journal.CompletedFiles, StringComparer.OrdinalIgnoreCase);
        // A previous interrupted operation may already have created these originals.
        var allowed = manifest.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (completed.Any(p => !allowed.Contains(p))) throw new InvalidDataException("Journal non valido.");
        if (plan.MissingCount == 0)
        {
            if (resumed)
            {
                CleanupTemporaryFiles(root, manifest, journal.OperationId);
                WriteJson(journalPath, journal with { State = "Complete" });
            }
            return new(0, plan.Files.Count(f => f.Disposition == RestoreDisposition.Identical), resumed);
        }
        WriteJson(journalPath, journal);
        var restored = 0;
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            token.ThrowIfCancellationRequested(); EnsureStopped(executable);
            // Baseline files are held read-only without write/delete sharing on Windows.
            // Recheck absent targets because no file handle can reserve a missing path.
            CheckAbsentTargets(root, manifest);
            var item = manifest.Files[i];
            if (!item.Existed || plan.Files[i].Disposition != RestoreDisposition.Missing) continue;
            var destination = GameFile(root, item.RelativePath);
            if (AttributesOrMissing(destination) is not null)
            {
                baseline.Add(destination, item.Sha256!, token);
                continue;
            }
            var parent = Path.GetDirectoryName(destination)!;
            GuardPath(parent); Directory.CreateDirectory(parent); GuardPath(parent);
            var temporary = Path.Combine(parent, $".gamepanel-{journal.OperationId}-{i:D4}.tmp");
            GuardPath(temporary);
            // Never trust or execute a leftover staging file from an interrupted run.
            if (AttributesOrMissing(temporary) is not null)
            {
                if (HashFile(temporary, token) != item.Sha256)
                {
                    // The journal reserved this exact temporary name before creation.
                    // An interrupted copy is replaced from the verified snapshot.
                    File.Delete(temporary);
                    CopyVerified(Payload(folder, i), temporary, token, MaximumSnapshotBytes);
                }
            }
            else CopyVerified(Payload(folder, i), temporary, token, MaximumSnapshotBytes);
            if (HashFile(temporary, token) != item.Sha256) throw new InvalidDataException("Copia temporanea del backup danneggiata.");
            token.ThrowIfCancellationRequested(); EnsureStopped(executable);
            GuardPath(destination);
            CheckAbsentTargets(root, manifest);
            // Atomic creation only: never overwrite a file that appeared in the meantime.
            File.Move(temporary, destination, overwrite: false);
            baseline.Add(destination, item.Sha256!, token);
            restored++;
            completed.Add(item.RelativePath);
            journal = journal with { CompletedFiles = completed.Order(StringComparer.OrdinalIgnoreCase).ToArray() };
            WriteJson(journalPath, journal);
            progress?.Report(new($"Ripristinato: {item.RelativePath}", restored, plan.MissingCount));
        }
        var finalPlan = Plan(executable, manifest, token, verifyPayloads: false);
        if (finalPlan.HasConflicts || finalPlan.MissingCount != 0) throw new InvalidDataException("Ripristino incompleto: è possibile riprenderlo dopo aver risolto il problema.");
        CleanupTemporaryFiles(root, manifest, journal.OperationId);
        WriteJson(journalPath, journal with { State = "Complete" });
        return new(restored, finalPlan.Files.Count(f => f.Disposition == RestoreDisposition.Identical), resumed);
    }

    private RestorePlan Plan(string executable, BackupManifest manifest, CancellationToken token, bool verifyPayloads = true)
    {
        var root = Path.GetDirectoryName(executable)!;
        var folder = SnapshotFolder(executable, manifest.Id);
        CheckRoots(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("La cartella del gioco non esiste più. Il backup resta conservato.");
        var result = new List<RestoreFile>();
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = manifest.Files[i];
            if (entry.Existed && verifyPayloads)
            {
                var payload = Payload(folder, i);
                GuardPath(payload);
                if (new FileInfo(payload).Length != entry.Length || HashFile(payload, token) != entry.Sha256)
                    throw new InvalidDataException($"Backup danneggiato: {entry.RelativePath}. Nessun ripristino eseguito.");
            }
            var live = GameFile(root, entry.RelativePath);
            var attributes = AttributesOrMissing(live);
            var state = attributes is null
                ? entry.Existed ? RestoreDisposition.Missing : RestoreDisposition.OriginallyAbsent
                : attributes.Value.HasFlag(FileAttributes.Directory) || !entry.Existed || HashFile(live, token) != entry.Sha256
                    ? RestoreDisposition.Conflict : RestoreDisposition.Identical;
            result.Add(new(entry.RelativePath, state));
        }
        return new(manifest.Id, result);
    }

    private BackupManifest Load(string executable, string id)
    {
        CheckRoots(Path.GetDirectoryName(executable)!);
        var folder = SnapshotFolder(executable, id);
        var path = Path.Combine(folder, "snapshot.json");
        var m = ReadJson<BackupManifest>(path);
        if (m.SchemaVersion != 1 || m.Id != id || m.Files is null || m.Files.Count is < 1 or > MaximumFiles
            || !string.Equals(m.ExecutablePath, executable, PathComparison) || !ValidHash(m.ExecutableSha256))
            throw new InvalidDataException("Backup non valido o appartenente a un altro percorso del gioco.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var f in m.Files)
        {
            if (f is null || !names.Add(ValidateRelative(f.RelativePath)) || f.Length < 0 || f.Length > MaximumFileBytes
                || (f.Existed ? !ValidHash(f.Sha256) : f.Sha256 is not null || f.Length != 0))
                throw new InvalidDataException("Elenco file del backup non valido.");
            total = checked(total + f.Length);
        }
        if (total > MaximumSnapshotBytes || !m.Files.Any(f => f.Existed
            && string.Equals(f.RelativePath, Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase)
            && f.Sha256 == m.ExecutableSha256)) throw new InvalidDataException("Identità dell'eseguibile nel backup non valida.");
        return m;
    }

    private static RepairJournal? ReadJournal(string path, string snapshotId)
    {
        if (AttributesOrMissing(path) is null) return null;
        var j = ReadJson<RepairJournal>(path);
        if (j.SchemaVersion != 1 || j.SnapshotId != snapshotId || !ValidId(j.OperationId)
            || j.State is not ("Pending" or "Complete") || j.CompletedFiles is null
            || j.CompletedFiles.Count > MaximumFiles) throw new InvalidDataException("Journal di ripristino non valido.");
        foreach (var p in j.CompletedFiles) ValidateRelative(p);
        return j;
    }

    private static void CheckAbsentTargets(string root, BackupManifest manifest)
    {
        foreach (var f in manifest.Files.Where(f => !f.Existed))
            if (AttributesOrMissing(GameFile(root, f.RelativePath)) is not null)
                throw new InvalidDataException($"È comparso un file non presente nel backup: {f.RelativePath}. Ripristino sospeso.");
    }

    private static void CleanupTemporaryFiles(string root, BackupManifest manifest, string operationId)
    {
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            if (!manifest.Files[i].Existed) continue;
            var destination = GameFile(root, manifest.Files[i].RelativePath);
            var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".gamepanel-{operationId}-{i:D4}.tmp");
            GuardPath(temporary);
            if (AttributesOrMissing(temporary) is not null) File.Delete(temporary);
        }
    }

    private sealed class BaselineLease : IDisposable
    {
        private readonly List<FileStream> _streams = [];
        public BaselineLease(string root, BackupManifest manifest, RestorePlan plan, CancellationToken token)
        {
            try
            {
                for (var i = 0; i < manifest.Files.Count; i++)
                    if (plan.Files[i].Disposition == RestoreDisposition.Identical)
                        Add(GameFile(root, manifest.Files[i].RelativePath), manifest.Files[i].Sha256!, token);
            }
            catch { Dispose(); throw; }
        }
        public void Add(string path, string expectedHash, CancellationToken token)
        {
            GuardPath(path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _streams.Add(stream);
            if (Digest(stream, null, token, MaximumFileBytes).Hash != expectedHash)
                throw new InvalidDataException("Un file presente non corrisponde al backup: ripristino sospeso.");
        }
        public void Dispose() { foreach (var stream in _streams) stream.Dispose(); }
    }

    private void CheckRoots(string gameRoot)
    {
        GuardPath(gameRoot); GuardPath(_store);
        if (Inside(_store, gameRoot) || Inside(gameRoot, _store))
            throw new InvalidDataException("La cartella dei backup deve essere separata dalla cartella del gioco.");
    }
    private string GameStore(string executable)
    {
        var key = OperatingSystem.IsWindows() ? executable.ToUpperInvariant() : executable;
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_store, hash);
    }
    private string SnapshotFolder(string executable, string id)
    {
        if (!ValidId(id)) throw new InvalidDataException("Identificativo backup non valido.");
        var folder = Path.Combine(GameStore(executable), id); GuardPath(folder); return folder;
    }
    private static string FullExecutable(string path) => Path.GetFullPath(ExecutablePath.Normalize(path));
    private static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    internal static bool Inside(string path, string root) => string.Equals(Canonical(path), Canonical(root), PathComparison)
        || Path.GetRelativePath(root, path) is var relative && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    internal static bool ValidId(string? id) => id is { Length: 32 } && id.All(Uri.IsHexDigit);
    internal static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    internal static string ValidateRelative(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 240 || Path.IsPathRooted(relative)
            || relative.Contains(':') || relative.Contains('\0')) throw new InvalidDataException("Percorso nel backup non valido.");
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')
            || p.Any(c => c < 32 || "<>\"|?*".Contains(c)))) throw new InvalidDataException("Percorso nel backup non valido.");
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9'))
                throw new InvalidDataException("Nome dispositivo non consentito nel backup.");
        }
        return string.Join('/', parts);
    }
    internal static string GameFile(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, ValidateRelative(relative).Replace('/', Path.DirectorySeparatorChar)));
        if (!Inside(path, root)) throw new InvalidDataException("File fuori dalla cartella del gioco.");
        GuardPath(path); return path;
    }
    private static string Payload(string folder, int index)
    {
        var path = Path.Combine(folder, "files", $"{index:D4}.bin"); GuardPath(path); return path;
    }
    internal static FileAttributes? AttributesOrMissing(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    internal static void GuardPath(string path)
    {
        var full = Path.GetFullPath(path);
        while (true)
        {
            if (AttributesOrMissing(full)?.HasFlag(FileAttributes.ReparsePoint) == true)
                throw new InvalidDataException("Collegamento o junction rilevato: operazione bloccata.");
            var parent = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(parent) || parent == full) break;
            full = parent;
        }
    }
    private static FileStream Lock(string store)
    {
        var path = Path.Combine(store, ".operation.lock"); GuardPath(path);
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void EnsureStopped(string executable)
    {
        bool running;
        try { running = _gameIsRunning(executable); }
        catch (Exception e) { throw new InvalidOperationException("Impossibile verificare che il gioco sia chiuso. Operazione bloccata.", e); }
        if (running) throw new InvalidOperationException("Chiudi il gioco prima di creare o ripristinare il backup.");
    }
    internal static bool IsGameRunning(string executable)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    internal static (string Hash, long Length) CopyVerified(string source, string destination, CancellationToken token, long available)
    {
        GuardPath(source); GuardPath(destination);
        (string Hash, long Length) result;
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (input.Length > MaximumFileBytes || input.Length > available) throw new InvalidDataException("Dimensione massima del backup superata.");
            result = Digest(input, output, token, Math.Min(MaximumFileBytes, available));
            output.Flush(flushToDisk: true);
        }
        if (HashFile(destination, token) != result.Hash || new FileInfo(destination).Length != result.Length)
            throw new InvalidDataException("Verifica della copia di backup non riuscita.");
        return result;
    }
    internal static string HashFile(string path, CancellationToken token)
    {
        GuardPath(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Digest(input, null, token, MaximumFileBytes).Hash;
    }
    private static (string Hash, long Length) Digest(Stream input, Stream? output, CancellationToken token, long limit)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024]; long total = 0; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested(); total = checked(total + count);
            if (total > limit) throw new InvalidDataException("Dimensione massima del backup superata.");
            digest.AppendData(buffer, 0, count); output?.Write(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return (Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), total);
    }
    internal static T ReadJson<T>(string path)
    {
        GuardPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 128 * 1024) throw new InvalidDataException("Metadati backup troppo grandi.");
        try { return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException("Metadati backup mancanti."); }
        catch (JsonException e) { throw new InvalidDataException("Metadati backup non leggibili.", e); }
    }
    internal static void WriteJson<T>(string path, T value)
    {
        GuardPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(output, value, JsonOptions); output.Flush(flushToDisk: true); }
        GuardPath(path); File.Move(temporary, path, overwrite: true);
    }
}
