using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record DirectXTextureReplacement(string Hash, string DestinationPath, string Sha256, long Bytes);

/// <summary>Personal, data-only DDS replacements. Original game data and captures are never overwritten.</summary>
public sealed class DirectXTextureReplacementService
{
    private readonly DirectXTextureProfileService _profiles;
    private const string StateName = "directx-replacements.json", JournalName = "directx-replacements-operation.json";
    private sealed record State(int SchemaVersion, string ExecutablePath, string ExecutableSha256,
        string ResourceRoot, IReadOnlyList<DirectXTextureReplacement> Files);
    private sealed record Journal(int SchemaVersion, string Id, string Status, string Hash,
        State Before, State After, string? BeforeSha256, string? AfterSha256);

    public DirectXTextureReplacementService(string store, Func<string, bool>? running = null)
        : this(new DirectXTextureProfileService(store, running)) { }
    internal DirectXTextureReplacementService(DirectXTextureProfileService profiles) => _profiles = profiles;

    public IReadOnlyList<DirectXTextureReplacement> ListInstalled(GameReport report, string resourceRoot,
        CancellationToken token = default)
    {
        using var lease = _profiles.AcquireForReplacement(report, resourceRoot, token);
        var folder = _profiles.ProfileFolder(report.ExecutablePath);
        if (ReadJournal(folder, report, resourceRoot) is { Status: "Pending" })
            throw Invalid("Sostituzione DDS interrotta: recupera prima l'operazione.");
        var state = ReadState(folder, report, resourceRoot);
        VerifyLive(state, token);
        return state.Files;
    }

    public bool HasPending(GameReport report, string resourceRoot)
        => ReadJournal(_profiles.ProfileFolder(report.ExecutablePath), report, resourceRoot)?.Status == "Pending";

    public DirectXTextureReplacement Install(GameReport report, string resourceRoot, string originalHash,
        string replacementPath, CancellationToken token = default)
    {
        originalHash = Key(originalHash);
        using var lease = _profiles.AcquireForReplacement(report, resourceRoot, token);
        var folder = _profiles.ProfileFolder(report.ExecutablePath);
        CheckNoPending(folder, report, resourceRoot);
        // Only a real, validated capture can select the texture that will be replaced.
        var captures = DirectXTextureData.ListCaptures(resourceRoot);
        if (!captures.Any(c => c.Hash.Equals(originalHash, StringComparison.OrdinalIgnoreCase)))
            throw Invalid("Seleziona una cattura DDS verificata di questo gioco.");
        var source = Path.GetFullPath(replacementPath); GuardPath(source);
        var info = DirectXTextureData.ReadDds(source);
        if (!info.FullMipChain) throw Invalid("La texture sostitutiva deve includere tutti i mipmap fino a 1 × 1.");
        var before = ReadState(folder, report, resourceRoot); VerifyLive(before, token);
        var destination = Destination(resourceRoot, originalHash);
        var old = before.Files.SingleOrDefault(f => f.Hash == originalHash);
        if (old is null && AttributesOrMissing(destination) is not null)
            throw Invalid("Una DDS non gestita occupa già questa destinazione: nessuna sostituzione.");
        var next = new DirectXTextureReplacement(originalHash, destination, info.Sha256, info.Bytes);
        var after = before with { Files = before.Files.Where(f => f.Hash != originalHash).Append(next).OrderBy(f => f.Hash).ToArray() };
        Mutate(report, folder, before, after, originalHash, old?.Sha256, next.Sha256, source, token);
        return next;
    }

    public void Remove(GameReport report, string resourceRoot, string originalHash, CancellationToken token = default)
    {
        originalHash = Key(originalHash);
        using var lease = _profiles.AcquireForReplacement(report, resourceRoot, token);
        var folder = _profiles.ProfileFolder(report.ExecutablePath); CheckNoPending(folder, report, resourceRoot);
        var before = ReadState(folder, report, resourceRoot); VerifyLive(before, token);
        var old = before.Files.SingleOrDefault(f => f.Hash == originalHash) ?? throw Invalid("Sostituzione DDS non gestita.");
        var after = before with { Files = before.Files.Where(f => f.Hash != originalHash).ToArray() };
        Mutate(report, folder, before, after, originalHash, old.Sha256, null, null, token);
    }

    public void Recover(GameReport report, string resourceRoot, bool rollback, CancellationToken token = default)
    {
        using var lease = _profiles.AcquireForReplacement(report, resourceRoot, token);
        var folder = _profiles.ProfileFolder(report.ExecutablePath);
        var op = ReadJournal(folder, report, resourceRoot) ?? throw Invalid("Nessuna sostituzione DDS da recuperare.");
        if (op.Status != "Pending") throw Invalid("L'operazione DDS è già conclusa.");
        var liveState = ReadState(folder, report, resourceRoot);
        if (JsonSerializer.Serialize(liveState) != JsonSerializer.Serialize(op.Before)
            && JsonSerializer.Serialize(liveState) != JsonSerializer.Serialize(op.After))
            throw Invalid("Stato DDS diverso dal journal: recupero sospeso.");
        Finish(report, folder, op, rollback, token);
    }

    private void Mutate(GameReport report, string folder, State before, State after, string hash, string? previousHash,
        string? nextHash, string? source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N"); var payload = PayloadFolder(folder, id);
        GuardPath(payload); Directory.CreateDirectory(payload); GuardPath(payload);
        if (previousHash is not null) CopyExpected(Destination(before.ResourceRoot, hash), Path.Combine(payload, "before.bin"), previousHash, token);
        if (nextHash is not null) CopyExpected(source!, Path.Combine(payload, "after.bin"), nextHash, token);
        var op = new Journal(1, id, "Pending", hash, before, after, previousHash, nextHash);
        _profiles.CheckReplacementContext(report, before.ResourceRoot, token);
        Write(Path.Combine(folder, JournalName), op);
        Finish(report, folder, op, false, token);
    }

    private void Finish(GameReport report, string folder, Journal op, bool rollback, CancellationToken token)
    {
        var desired = rollback ? op.BeforeSha256 : op.AfterSha256;
        var target = Destination(op.Before.ResourceRoot, op.Hash);
        var live = ExistingHash(target, token);
        if (live != op.BeforeSha256 && live != op.AfterSha256)
            throw Invalid("La DDS è cambiata fuori da GamePanel: nessun file sovrascritto.");
        var payload = PayloadFolder(folder, op.Id);
        if (op.BeforeSha256 is not null) VerifyDdsPayload(Path.Combine(payload, "before.bin"), op.BeforeSha256, token);
        if (op.AfterSha256 is not null) VerifyDdsPayload(Path.Combine(payload, "after.bin"), op.AfterSha256, token);
        var result = rollback ? op.Before : op.After;
        // Check all other managed replacements before committing any data.
        VerifyLive(result with { Files = result.Files.Where(f => f.Hash != op.Hash).ToArray() }, token);
        token.ThrowIfCancellationRequested();
        _profiles.CheckReplacementContext(report, op.Before.ResourceRoot, token);
        if (live != desired)
        {
            if (desired is null) { GuardPath(target); File.Delete(target); }
            else
            {
                var parent = Path.GetDirectoryName(target)!; GuardPath(parent); Directory.CreateDirectory(parent); GuardPath(parent);
                var stage = Path.Combine(parent, ".gamepanel-dds-" + op.Id + ".tmp"); GuardPath(stage);
                if (AttributesOrMissing(stage) is not null)
                {
                    // The exact name was reserved in the durable journal before creating it.
                    if (AttributesOrMissing(stage) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                        throw Invalid("File temporaneo DDS non regolare.");
                    File.Delete(stage);
                }
                CopyExpected(Path.Combine(payload, rollback ? "before.bin" : "after.bin"), stage, desired, token);
                _profiles.CheckReplacementContext(report, op.Before.ResourceRoot, token);
                if (ExistingHash(target, token) != live) throw Invalid("La DDS è cambiata durante la preparazione.");
                GuardPath(target); File.Move(stage, target, true);
            }
        }
        VerifyLive(result, token);
        _profiles.CheckReplacementContext(report, op.Before.ResourceRoot, token);
        Write(Path.Combine(folder, StateName), result);
        Write(Path.Combine(folder, JournalName), op with { Status = rollback ? "RolledBack" : "Complete" });
    }

    private State ReadState(string folder, GameReport report, string resourceRoot)
    {
        var state = Read<State>(Path.Combine(folder, StateName))
            ?? new(1, Path.GetFullPath(report.ExecutablePath), report.Sha256, Path.GetFullPath(resourceRoot), []);
        Validate(state, report, resourceRoot); return state;
    }
    private Journal? ReadJournal(string folder, GameReport report, string resourceRoot)
    {
        var op = Read<Journal>(Path.Combine(folder, JournalName)); if (op is null) return null;
        if (op.SchemaVersion != 1 || !Regex.IsMatch(op.Id ?? "", "\\A[a-f0-9]{32}\\z")
            || op.Status is not ("Pending" or "Complete" or "RolledBack") || op.Before is null || op.After is null)
            throw Invalid("Journal DDS non valido.");
        _ = Key(op.Hash); Validate(op.Before, report, resourceRoot); Validate(op.After, report, resourceRoot);
        if (op.BeforeSha256 is not null && !Digest(op.BeforeSha256) || op.AfterSha256 is not null && !Digest(op.AfterSha256)
            || op.Before.Files.SingleOrDefault(f => f.Hash == op.Hash)?.Sha256 != op.BeforeSha256
            || op.After.Files.SingleOrDefault(f => f.Hash == op.Hash)?.Sha256 != op.AfterSha256
            || JsonSerializer.Serialize(op.Before.Files.Where(f => f.Hash != op.Hash)) != JsonSerializer.Serialize(op.After.Files.Where(f => f.Hash != op.Hash)))
            throw Invalid("Transizione DDS non valida.");
        return op;
    }
    private static void Validate(State state, GameReport report, string root)
    {
        if (state.SchemaVersion != 1 || !Same(state.ExecutablePath, report.ExecutablePath)
            || state.ExecutableSha256 != report.Sha256 || !Same(state.ResourceRoot, root)
            || state.Files is null || state.Files.Count > 256) throw Invalid("Stato DDS non valido o EXE cambiato.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in state.Files)
            if (f is null || !keys.Add(Key(f.Hash)) || f.Hash != f.Hash.ToUpperInvariant()
                || f.DestinationPath != Destination(root, f.Hash) || !Digest(f.Sha256) || f.Bytes is < 128 or > 128 * 1024 * 1024)
                throw Invalid("Elenco DDS non valido.");
    }
    private void CheckNoPending(string folder, GameReport report, string root)
    { if (ReadJournal(folder, report, root)?.Status == "Pending") throw Invalid("Recupera prima la sostituzione DDS interrotta."); }
    private static void VerifyLive(State state, CancellationToken token)
    { foreach (var f in state.Files) { token.ThrowIfCancellationRequested(); var path = Destination(state.ResourceRoot, f.Hash); if (ExistingHash(path, token) != f.Sha256 || new FileInfo(path).Length != f.Bytes) throw Invalid("DDS gestita modificata o mancante: " + f.Hash); } }
    private static string Destination(string root, string hash)
    { var path = Path.Combine(Path.GetFullPath(root), "inject", Key(hash) + ".dds"); GuardPath(path); return path; }
    private static string Key(string hash)
        => DirectXTextureData.NormalizeHash(hash);
    private static bool Digest(string? hash) => hash?.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static InvalidDataException Invalid(string message) => new(message);
    private static string PayloadFolder(string folder, string id) => Path.Combine(folder, "directx-dds-backups", id);
    private static string? ExistingHash(string path, CancellationToken token)
    {
        GuardPath(path); var a = AttributesOrMissing(path); if (a is null) return null;
        if ((a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0 || new FileInfo(path).Length > 128 * 1024 * 1024)
            throw Invalid("DDS o metadati non regolari o fuori limite.");
        return HashFile(path, token);
    }
    private static void Verify(string path, string expected, CancellationToken token)
    { if (ExistingHash(path, token) != expected) throw Invalid("Copia DDS non verificata."); }
    private static void VerifyDdsPayload(string path, string expected, CancellationToken token)
    {
        Verify(path, expected, token);
        token.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 128 or > DirectXTextureData.MaximumDdsBytes) throw Invalid("Payload DDS fuori limite.");
        var bytes = new byte[checked((int)input.Length)];
        for (var offset = 0; offset < bytes.Length;)
        {
            token.ThrowIfCancellationRequested();
            var count = input.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (count == 0) throw Invalid("Payload DDS troncato."); offset += count;
        }
        if (input.ReadByte() != -1) throw Invalid("Payload DDS cresciuto durante la lettura.");
        var info = DirectXTextureData.InspectDds(bytes);
        if (info.Sha256 != expected || !info.FullMipChain) throw Invalid("Payload DDS non valido o privo di mipmap.");
    }
    private static void CopyExpected(string source, string destination, string expected, CancellationToken token)
    {
        Verify(source, expected, token); GuardPath(destination);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[65536]; long count = 0; int read;
            while ((read = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); count += read; if (count > 128 * 1024 * 1024) throw Invalid("DDS fuori limite."); output.Write(buffer, 0, read); }
            output.Flush(true);
        }
        Verify(destination, expected, token); Verify(source, expected, token);
    }
    private static T? Read<T>(string path)
    {
        GuardPath(path); if (AttributesOrMissing(path) is null) return default;
        if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0 || new FileInfo(path).Length > 1024 * 1024)
            throw Invalid("Metadati DDS fuori limite.");
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 1024 * 1024) throw Invalid("Metadati DDS fuori limite.");
            var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
            if (input.ReadByte() != -1) throw Invalid("Metadati DDS cresciuti durante la lettura.");
            using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 24 });
            RejectDuplicateKeys(doc.RootElement);
            return doc.RootElement.Deserialize<T>() ?? throw Invalid("Metadati DDS vuoti.");
        }
        catch (JsonException) { throw Invalid("Metadati DDS non validi."); }
    }
    private static void RejectDuplicateKeys(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            { if (!names.Add(property.Name)) throw Invalid("Proprietà duplicate nei metadati DDS."); RejectDuplicateKeys(property.Value); }
        }
        else if (item.ValueKind == JsonValueKind.Array)
            foreach (var child in item.EnumerateArray()) RejectDuplicateKeys(child);
    }
    private static void Write<T>(string path, T value)
    {
        GuardPath(path); var temp = path + ".writing-" + Guid.NewGuid().ToString("N"); GuardPath(temp);
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(JsonSerializer.SerializeToUtf8Bytes(value)); output.Flush(true); }
            GuardPath(path); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
