using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record UpdateFileChange(string Path, string? Before, string? After);
public sealed record UpdateJournal(int SchemaVersion, string Id, string ApplicationDirectory, string ReadyDirectory, string DataRoot,
    string Status, IReadOnlyList<UpdateFileChange> Changes);
public sealed record UpdateRequest(int SchemaVersion, string ApplicationDirectory, string ReadyDirectory, string DataRoot,
    int ParentPid, long ParentStartedUtcTicks, string TargetVersion, bool Restart);

/// <summary>Replaces only manifest-owned application files. User data and extra files
/// are never swept. Both versions and a journal precede every application write.</summary>
public sealed class UpdateInstallation
{
    private readonly string _application;
    private readonly string _ready;
    private readonly string _data;
    private readonly string _work;
    private readonly string _id;
    private const string JournalName = "install-journal.json";
    public UpdateInstallation(string applicationDirectory, string readyDirectory, string dataRoot)
    {
        _application = Full(applicationDirectory); _ready = Full(readyDirectory); _data = Full(dataRoot);
        _work = System.IO.Path.GetDirectoryName(_ready)!; _id = System.IO.Path.GetFileName(_work);
        GuardPath(_application); GuardPath(_ready); GuardPath(_data); GuardPath(_work);
        if (!ValidId(_id) || System.IO.Path.GetFileName(_ready) != "ready" || !Inside(_ready, _data)
            || Inside(_application, _data) || Inside(_data, _application))
            throw new InvalidDataException("App, staging e dati utente devono essere separati. Usa una cartella dedicata per GamePanel.");
    }
    public IReadOnlyList<UpdateFileChange> Preview(CancellationToken token = default)
    {
        var old = PackageInventory.Read(_application, requireUpdater: false); var next = PackageInventory.Read(_ready);
        PackageInventory.Verify(_application, old, token); PackageInventory.Verify(_ready, next, token);
        var names = old.Keys.Union(next.Keys, StringComparer.OrdinalIgnoreCase).Append(PackageInventory.ManifestName).ToArray();
        var result = new List<UpdateFileChange>();
        foreach (var name in names)
        {
            var target = GameFile(_application, name); var before = Live(target, token);
            var after = name == PackageInventory.ManifestName ? HashFile(GameFile(_ready, name), token) : next.GetValueOrDefault(name);
            if (name != PackageInventory.ManifestName && before is not null && !old.ContainsKey(name)) throw new InvalidDataException("Un file personale occupa il percorso dell'aggiornamento: " + name);
            if (before != after) result.Add(new(name, before, after));
        }
        return result;
    }
    public void ProbeWriteAccess()
    {
        var path = System.IO.Path.Combine(_application, ".gamepanel-write-" + Guid.NewGuid().ToString("N") + ".tmp"); GuardPath(path);
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
    }
    public void Apply(CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        using var lease = Acquire();
        var journalPath = System.IO.Path.Combine(_work, JournalName); GuardPath(journalPath);
        if (File.Exists(journalPath)) throw new InvalidOperationException("Questa installazione ha già un journal. Riprendila o recuperala.");
        var changes = Preview(token);
        Directory.CreateDirectory(System.IO.Path.Combine(_work, "before"));
        for (var i = 0; i < changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); var c = changes[i];
            if (c.Before is not null && CopyVerified(GameFile(_application, c.Path), Before(i), token, SoftwareUpdates.MaximumUnpackedBytes).Hash != c.Before)
                throw new InvalidDataException("File dell'app cambiato durante il backup: " + c.Path);
        }
        var journal = new UpdateJournal(1, _id, _application, _ready, _data, "Pending", changes);
        WriteJson(journalPath, journal);
        Transition(journal, rollback: false, token, progress);
    }
    public void Recover(bool rollback, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        using var lease = Acquire(); var journal = ReadJournal(System.IO.Path.Combine(_work, JournalName)); Validate(journal);
        if (journal.Status != "Pending") throw new InvalidOperationException("Nessun aggiornamento sospeso da recuperare.");
        Transition(journal, rollback, token, progress);
    }
    public bool HasPendingJournal
    {
        get { var path = System.IO.Path.Combine(_work, JournalName); GuardPath(path); if (!File.Exists(path)) return false; var j = ReadJournal(path); Validate(j); return j.Status == "Pending"; }
    }
    private void Transition(UpdateJournal journal, bool rollback, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        Validate(journal);
        for (var i = 0; i < journal.Changes.Count; i++)
        {
            var c = journal.Changes[i]; var live = Live(GameFile(_application, c.Path), token);
            if (live != c.Before && live != c.After) throw new InvalidDataException("File cambiato da un altro programma: " + c.Path);
            if (c.Before is not null && HashFile(Before(i), token) != c.Before || c.After is not null && HashFile(GameFile(_ready, c.Path), token) != c.After)
                throw new InvalidDataException("Copia dell'aggiornamento danneggiata.");
        }
        for (var i = 0; i < journal.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); var c = journal.Changes[i]; var expected = rollback ? c.After : c.Before; var wanted = rollback ? c.Before : c.After;
            var target = GameFile(_application, c.Path); var actual = Live(target, token);
            if (actual != wanted)
            {
                if (actual != expected) throw new InvalidDataException("Destinazione cambiata durante l'aggiornamento: " + c.Path);
                if (wanted is null) File.Delete(target);
                else
                {
                    var parent = System.IO.Path.GetDirectoryName(target)!; GuardPath(parent); Directory.CreateDirectory(parent); GuardPath(parent);
                    var temporary = Temp(target, i); GuardPath(temporary);
                    if (File.Exists(temporary)) File.Delete(temporary);
                    CopyVerified(rollback ? Before(i) : GameFile(_ready, c.Path), temporary, token, SoftwareUpdates.MaximumUnpackedBytes);
                    if (HashFile(temporary, token) != wanted) throw new InvalidDataException("Temporanea aggiornamento non valida.");
                    token.ThrowIfCancellationRequested(); GuardPath(target);
                    if (Live(target, token) != expected) throw new InvalidDataException("File cambiato prima della sostituzione: " + c.Path);
                    File.Move(temporary, target, overwrite: expected is not null);
                }
            }
            progress?.Report(new(rollback ? "Ripristino app" : "Aggiornamento app", i + 1, journal.Changes.Count));
        }
        foreach (var c in journal.Changes) if (Live(GameFile(_application, c.Path), token) != (rollback ? c.Before : c.After)) throw new InvalidDataException("Verifica finale dell'aggiornamento fallita.");
        for (var i = 0; i < journal.Changes.Count; i++) { var t = Temp(GameFile(_application, journal.Changes[i].Path), i); GuardPath(t); if (File.Exists(t)) File.Delete(t); }
        WriteJson(System.IO.Path.Combine(_work, JournalName), journal with { Status = rollback ? "RolledBack" : "Complete" });
    }
    private static UpdateJournal ReadJournal(string path)
    {
        GuardPath(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Journal aggiornamento troppo grande.");
        return JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Journal mancante.");
    }
    private void Validate(UpdateJournal j)
    {
        if (j.SchemaVersion != 1 || j.Id != _id || j.ApplicationDirectory != _application || j.ReadyDirectory != _ready || j.DataRoot != _data
            || j.Status is not ("Pending" or "Complete" or "RolledBack") || j.Changes is null || j.Changes.Count > PackageInventory.MaximumFiles * 2 + 1) throw new InvalidDataException("Journal aggiornamento non valido.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in j.Changes)
            if (c is null || !names.Add(PackageInventory.PathName(c.Path)) || c.Before == c.After
                || c.Before is not null && !ValidHash(c.Before) || c.After is not null && !ValidHash(c.After)) throw new InvalidDataException("File nel journal aggiornamento non valido.");
    }
    private FileStream Acquire()
    { var path = System.IO.Path.Combine(_application, ".gamepanel-software-update.lock"); GuardPath(path); return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private string Before(int index) { var path = System.IO.Path.Combine(_work, "before", $"{index:D4}.bin"); GuardPath(path); return path; }
    private string Temp(string target, int index) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!, $".gamepanel-update-{_id}-{index:D4}.tmp");
    private static string Full(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    private static string? Live(string path, CancellationToken token)
    { GuardPath(path); if (AttributesOrMissing(path) is null) return null; if (File.GetAttributes(path).HasFlag(FileAttributes.Directory)) throw new InvalidDataException("Cartella al posto di un file applicativo."); return HashFile(path, token); }
}
