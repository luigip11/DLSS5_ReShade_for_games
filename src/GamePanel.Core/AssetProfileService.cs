using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record InstalledAssetPack(AssetPackManifest Manifest, string ArchiveSha256, bool Enabled);
public sealed record AssetState(int SchemaVersion, string Revision, string ExecutablePath, string ExecutableSha256,
    IReadOnlyList<InstalledAssetPack> Packs, IReadOnlyList<AssetPackFile> ManagedFiles);
public sealed record AssetChange(string Path, string? BeforeSha256, string? AfterSha256);
public sealed record AssetStatus(AssetState? State, bool Pending, string Message);
public sealed class AssetPlan
{
    internal AssetPlan(GameReport report, AssetState? previous, AssetState next, IReadOnlyList<AssetChange> changes,
        Dictionary<string, AssetSource> sources, PreparedAssetPack? package, string name, string action)
    { Report = report; Previous = previous; Next = next; Changes = changes; Sources = sources; Package = package; PackName = name; Action = action; }
    internal GameReport Report { get; }
    internal AssetState? Previous { get; }
    internal AssetState Next { get; }
    internal Dictionary<string, AssetSource> Sources { get; }
    internal PreparedAssetPack? Package { get; }
    public IReadOnlyList<AssetChange> Changes { get; }
    public string PackName { get; }
    public string Action { get; }
}
internal sealed record AssetSource(string? File, byte[]? Metadata = null);

/// <summary>Data-only, streamed deployment with durable original and installed copies outside the game.</summary>
public sealed class AssetProfileService
{
    public const string TextureLoaderSha256 = "63b464597f60d7e2f29dc65bd48919d9336896d1f54c9be8247157c14ebfe25b";
    private const string AsiLoaderSha256 = "d5a059aa467a7a7127c8f6169f79fa63ff0f55986ee9eb2fd9a281bebf2aa2e6";
    private const string Loader = "scripts/TexWizard.asi", Config = "scripts/TexWizard.json";
    private readonly string _store, _loaderHash, _asiLoaderHash;
    private readonly Func<string, bool> _running;
    private readonly Func<GameReport, string?> _textureCheck;
    private sealed record Baseline(AssetPackManifest Manifest, string ArchiveSha256, IReadOnlyList<AssetPackFile> Originals);
    private sealed record Operation(int SchemaVersion, string Id, string ExecutablePath, string ExecutableSha256,
        string Status, AssetState? Previous, AssetState Next, IReadOnlyList<AssetChange> Changes);
    public AssetProfileService(string store, Func<string, bool>? running = null)
        : this(store, running, TextureRuntimeCatalog.CheckGame, TextureLoaderSha256) { }
    internal AssetProfileService(string store, Func<string, bool>? running, Func<GameReport, string?> textureCheck, string textureLoaderSha256, string? asiLoaderSha256 = null)
    { _store = Path.GetFullPath(store); _running = running ?? IsGameRunning; _textureCheck = textureCheck; _loaderHash = textureLoaderSha256; _asiLoaderHash = asiLoaderSha256 ?? AsiLoaderSha256; }

    public AssetStatus Status(string executablePath)
    {
        var exe = Full(executablePath); var folder = Folder(exe);
        var state = LoadState(folder, exe); var op = LoadOperation(folder, exe);
        return new(state, op?.Status == "Pending", op?.Status == "Pending" ? "Operazione asset interrotta: riprendi o annulla."
            : state?.Packs.Count > 0 ? $"{state.Packs.Count} pack gestiti da GamePanel." : "Nessun pack asset installato.");
    }
    public AssetPlan PrepareInstall(GameReport report, PreparedAssetPack package, string? verifiedLoaderPath = null, CancellationToken token = default)
    {
        var (folder, previous) = Begin(report, token); AssetPackageImporter.VerifyPrepared(package, token); ValidateManifest(package.Manifest);
        if (!ValidHash(package.ArchiveSha256)) throw Invalid("Identità archivio non valida.");
        var m = Normalize(package.Manifest); var root = Path.GetDirectoryName(Full(report.ExecutablePath))!;
        if (previous?.Packs.Any(p => p.Manifest.Id == m.Id) == true) throw Invalid("Pack già installato: rimuovilo prima di aggiornarlo.");
        if (m.Files.Any(f => previous?.Packs.Any(p => p.Manifest.Files.Any(x => x.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase))) == true))
            throw Invalid("Un altro pack gestisce già questo percorso.");
        var sources = new Dictionary<string, AssetSource>(StringComparer.Ordinal);
        foreach (var f in m.Files)
        {
            var path = GameFile(Path.Combine(Path.GetFullPath(package.Folder), "files"), f.Path);
            VerifyFile(path, f, token); sources.Add(f.Path, new(path));
            var existing = Live(root, f.Path, token);
            if (IsCar(f.Path))
            {
                if (existing is null || !Directory.Exists(Path.GetDirectoryName(GameFile(root, f.Path)))) throw Invalid("L'auto originale deve essere già presente: " + f.Path);
            }
            else if (existing is not null) throw Invalid("Il pack non sostituisce file preesistenti: " + f.Path);
        }
        if (m.Kind == "Textures")
        {
            CheckTexture(report, token);
            if (previous?.Packs.Any(p => p.Manifest.Kind == "Textures") != true)
            {
                if (verifiedLoaderPath is null || HashFile(verifiedLoaderPath, token) != _loaderHash) throw Invalid("TexWizard verificato mancante o cambiato.");
                if (Live(root, Loader, token) is not null || Live(root, Config, token) is not null) throw Invalid("TexWizard o configurazione preesistenti: nessuna sostituzione.");
                sources.Add(Loader, new(Path.GetFullPath(verifiedLoaderPath)));
            }
        }
        var packs = (previous?.Packs ?? []).Append(new InstalledAssetPack(m, package.ArchiveSha256.ToLowerInvariant(), true)).ToArray();
        return Build(report, folder, previous, packs, sources, package, m.Name, "Installa", token);
    }
    public AssetPlan PrepareToggle(GameReport report, string packId, bool enabled, CancellationToken token = default)
    {
        var (folder, previous) = Begin(report, token); var pack = Find(previous, packId);
        if (pack.Enabled == enabled) throw Invalid("Il pack è già nello stato richiesto.");
        if (pack.Manifest.Kind == "Textures") CheckTexture(report, token);
        return Build(report, folder, previous, previous!.Packs.Select(p => p.Manifest.Id == packId ? p with { Enabled = enabled } : p).ToArray(),
            new(StringComparer.Ordinal), null, pack.Manifest.Name, enabled ? "Attiva" : "Disattiva", token);
    }
    public AssetPlan PrepareRemove(GameReport report, string packId, CancellationToken token = default)
    {
        var (folder, previous) = Begin(report, token); var pack = Find(previous, packId);
        return Build(report, folder, previous, previous!.Packs.Where(p => p.Manifest.Id != packId).ToArray(), new(StringComparer.Ordinal), null, pack.Manifest.Name, "Rimuovi", token);
    }
    private AssetPlan Build(GameReport report, string folder, AssetState? previous, IReadOnlyList<InstalledAssetPack> packs,
        Dictionary<string, AssetSource> sources, PreparedAssetPack? package, string name, string action, CancellationToken token)
    {
        VerifyTextureMappings(folder, packs, package, token);
        var root = Path.GetDirectoryName(Full(report.ExecutablePath))!; var desired = new Dictionary<string, AssetPackFile>(StringComparer.Ordinal);
        foreach (var pack in packs)
        {
            var baseline = package?.Manifest.Id == pack.Manifest.Id ? null : LoadBaseline(folder, pack);
            for (var i = 0; i < pack.Manifest.Files.Count; i++)
            {
                var f = pack.Manifest.Files[i]; var original = baseline?.Originals.SingleOrDefault(x => x.Path == f.Path);
                if (pack.Manifest.Kind == "Models" && !pack.Enabled && IsCar(f.Path))
                {
                    if (original is null) throw Invalid("Backup originale mancante.");
                    desired.Add(f.Path, original); sources[f.Path] = new(CacheFile(folder, pack, i, false));
                }
                else
                {
                    desired.Add(f.Path, f);
                    if (!sources.ContainsKey(f.Path)) sources[f.Path] = new(CacheFile(folder, pack, i, true));
                }
            }
        }
        if (packs.Any(p => p.Manifest.Kind == "Textures"))
        {
            var bytes = Configuration(packs);
            desired[Config] = new(Config, Hash(bytes), bytes.Length); sources[Config] = new(null, bytes);
            var old = previous?.ManagedFiles.SingleOrDefault(f => f.Path == Loader);
            var length = old?.Bytes ?? new FileInfo(sources[Loader].File!).Length;
            desired[Loader] = new(Loader, _loaderHash, length);
        }
        // Removing a model restores the original even though the path is no longer managed.
        var restored = new Dictionary<string, AssetPackFile>(StringComparer.Ordinal);
        foreach (var removed in (previous?.Packs ?? []).Where(p => packs.All(n => n.Manifest.Id != p.Manifest.Id)))
        {
            var baseline = LoadBaseline(folder, removed);
            foreach (var f in baseline.Originals)
            {
                var index = removed.Manifest.Files.ToList().FindIndex(x => x.Path == f.Path);
                restored.Add(f.Path, f); sources[f.Path] = new(CacheFile(folder, removed, index, false));
            }
        }
        var paths = (previous?.ManagedFiles.Select(f => f.Path) ?? []).Concat(desired.Keys).Concat(restored.Keys).Distinct(StringComparer.Ordinal);
        var changes = new List<AssetChange>();
        foreach (var path in paths)
        {
            var before = Live(root, path, token); var after = desired.GetValueOrDefault(path)?.Sha256 ?? restored.GetValueOrDefault(path)?.Sha256;
            if (before != after) changes.Add(new(path, before, after));
        }
        var next = new AssetState(1, Guid.NewGuid().ToString("N"), Full(report.ExecutablePath), report.Sha256, packs, desired.Values.ToArray());
        return new(report, previous, next, changes, sources, package, name, action);
    }
    public AssetState Execute(AssetPlan plan, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        CheckReport(plan.Report, token); var exe = Full(plan.Report.ExecutablePath); var root = Path.GetDirectoryName(exe)!; var folder = Folder(exe);
        Directory.CreateDirectory(folder); GuardPath(folder); using var lease = Acquire(folder); CheckNoPending(folder);
        var previous = LoadState(folder, exe);
        if (previous?.Revision != plan.Previous?.Revision) throw Invalid("Stato asset cambiato: ripeti l'anteprima.");
        VerifyManaged(root, previous, token);
        VerifyTextureMappings(folder, plan.Next.Packs, plan.Package, token);
        if (plan.Next.Packs.Any(p => p.Manifest.Kind == "Textures")) CheckTexture(plan.Report, token);
        foreach (var change in plan.Changes) if (Live(root, change.Path, token) != change.BeforeSha256) throw Invalid("File cambiato dopo l'anteprima: " + change.Path);
        if (plan.Package is not null)
        {
            AssetPackageImporter.VerifyPrepared(plan.Package, token);
            if (plan.Package.Manifest.Kind == "Textures") CheckTexture(plan.Report, token);
            SavePack(folder, root, plan.Package, token);
        }
        var id = Guid.NewGuid().ToString("N"); var payload = Path.Combine(folder, id); Directory.CreateDirectory(payload); GuardPath(payload);
        for (var i = 0; i < plan.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); var c = plan.Changes[i];
            if (c.BeforeSha256 is not null) CopyExpected(GameFile(root, c.Path), Payload(payload, i, false), c.BeforeSha256, token);
            if (c.AfterSha256 is not null)
            {
                if (!plan.Sources.TryGetValue(c.Path, out var source)) throw Invalid("Sorgente asset mancante.");
                if (source.Metadata is not null) DurableBytes(Payload(payload, i, true), source.Metadata);
                else CopyExpected(source.File!, Payload(payload, i, true), c.AfterSha256, token);
                if (HashFile(Payload(payload, i, true), token) != c.AfterSha256) throw Invalid("Payload cambiato.");
            }
        }
        CheckReport(plan.Report, token); VerifyManaged(root, previous, token);
        foreach (var c in plan.Changes) if (Live(root, c.Path, token) != c.BeforeSha256) throw Invalid("Destinazione cambiata durante il backup.");
        var op = new Operation(1, id, exe, plan.Report.Sha256, "Pending", previous, plan.Next, plan.Changes);
        ValidateOperation(op, exe);
        if (JsonSerializer.SerializeToUtf8Bytes(op).Length > 4 * 1024 * 1024) throw Invalid("Journal asset oltre il limite di 4 MiB.");
        WriteJson(Path.Combine(folder, "assets-operation.json"), op);
        return Transition(folder, op, false, token, progress);
    }
    public AssetState Recover(string executablePath, bool rollback, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var exe = Full(executablePath); var folder = Folder(exe); using var lease = Acquire(folder);
        CheckPeers(folder); var op = LoadOperation(folder, exe);
        if (op?.Status != "Pending") throw new InvalidOperationException("Nessuna operazione asset interrotta.");
        return Transition(folder, op, rollback, token, progress);
    }
    private AssetState Transition(string folder, Operation op, bool rollback, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var exe = op.ExecutablePath; var root = Path.GetDirectoryName(exe)!; CheckIdentity(exe, op.ExecutableSha256, token);
        if (!rollback && op.Next.Packs.Any(p => p.Manifest.Kind == "Textures"))
        { CheckTexture(new GameInspector().Inspect(exe, token), token); VerifyTextureMappings(folder, op.Next.Packs, null, token); }
        var state = LoadState(folder, exe);
        if (state?.Revision != op.Previous?.Revision && state?.Revision != op.Next.Revision) throw Invalid("Stato estraneo al journal asset.");
        var payload = Path.Combine(folder, op.Id); GuardPath(payload);
        for (var i = 0; i < op.Changes.Count; i++)
        {
            var c = op.Changes[i];
            if (c.BeforeSha256 is not null && HashFile(Payload(payload, i, false), token) != c.BeforeSha256
                || c.AfterSha256 is not null && HashFile(Payload(payload, i, true), token) != c.AfterSha256) throw Invalid("Backup asset danneggiato: recupero bloccato.");
            var actual = Live(root, c.Path, token);
            if (actual != c.BeforeSha256 && actual != c.AfterSha256) throw Invalid("File modificato esternamente: " + c.Path);
        }
        // Unchanged managed files are also protected during recovery.
        foreach (var f in (op.Previous?.ManagedFiles ?? []).Concat(op.Next.ManagedFiles).Where(f => op.Changes.All(c => c.Path != f.Path)).DistinctBy(f => f.Path))
            if (Live(root, f.Path, token) != f.Sha256) throw Invalid("Componente asset modificato: " + f.Path);
        for (var i = 0; i < op.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested(); CheckIdentity(exe, op.ExecutableSha256, token);
            var c = op.Changes[i]; var wanted = rollback ? c.BeforeSha256 : c.AfterSha256; var other = rollback ? c.AfterSha256 : c.BeforeSha256;
            var path = GameFile(root, c.Path); var current = Live(root, c.Path, token);
            if (current != wanted)
            {
                if (current != other) throw Invalid("Destinazione cambiata: " + c.Path);
                if (wanted is null) { GuardPath(path); File.Delete(path); }
                else
                {
                    var parent = Path.GetDirectoryName(path)!; GuardPath(parent); Directory.CreateDirectory(parent); GuardPath(parent);
                    var temp = Path.Combine(parent, $".gamepanel-assets-{op.Id}-{i:D4}.tmp"); GuardPath(temp);
                    if (File.Exists(temp) && HashFile(temp, token) != wanted) File.Delete(temp);
                    if (!File.Exists(temp)) CopyExpected(Payload(payload, i, !rollback), temp, wanted, token);
                    if (HashFile(temp, token) != wanted) throw Invalid("Copia temporanea danneggiata.");
                    token.ThrowIfCancellationRequested(); CheckIdentity(exe, op.ExecutableSha256, token);
                    if (Live(root, c.Path, token) != other) throw Invalid("Destinazione cambiata durante la copia.");
                    File.Move(temp, path, overwrite: other is not null);
                }
            }
            progress?.Report(new("Asset: " + c.Path, i + 1, op.Changes.Count));
        }
        foreach (var c in op.Changes) if (Live(root, c.Path, token) != (rollback ? c.BeforeSha256 : c.AfterSha256)) throw Invalid("Verifica finale asset fallita.");
        token.ThrowIfCancellationRequested(); CheckIdentity(exe, op.ExecutableSha256, token);
        var result = rollback ? op.Previous : op.Next; VerifyManaged(root, result, token);
        for (var i = 0; i < op.Changes.Count; i++)
        {
            var parent = Path.GetDirectoryName(GameFile(root, op.Changes[i].Path))!;
            var temp = Path.Combine(parent, $".gamepanel-assets-{op.Id}-{i:D4}.tmp"); GuardPath(temp);
            if (File.Exists(temp)) File.Delete(temp);
        }
        var statePath = Path.Combine(folder, "assets-state.json"); GuardPath(statePath);
        if (result is null) { if (File.Exists(statePath)) File.Delete(statePath); }
        else WriteJson(statePath, result);
        WriteJson(Path.Combine(folder, "assets-operation.json"), op with { Status = rollback ? "RolledBack" : "Complete" });
        return result ?? new(1, Guid.NewGuid().ToString("N"), exe, op.ExecutableSha256, [], []);
    }
    private (string Folder, AssetState? State) Begin(GameReport report, CancellationToken token)
    {
        CheckReport(report, token); var exe = Full(report.ExecutablePath); var folder = Folder(exe); CheckNoPending(folder);
        var state = LoadState(folder, exe);
        if (state is not null && state.ExecutableSha256 != report.Sha256) throw Invalid("EXE cambiato dopo la gestione degli asset.");
        VerifyManaged(Path.GetDirectoryName(exe)!, state, token); return (folder, state);
    }
    private void CheckReport(GameReport report, CancellationToken token)
    {
        CheckIdentity(Full(report.ExecutablePath), report.Sha256, token);
        var fresh = new GameInspector().Inspect(report.ExecutablePath, token);
        if (ReShadeProfileService.Eligibility(fresh, true) is { } reason) throw Invalid(reason);
        if (fresh.Sha256 != report.Sha256) throw Invalid("EXE cambiato: ripeti l'analisi.");
    }
    private void CheckTexture(GameReport report, CancellationToken token)
    {
        if (_textureCheck(report) is { } reason) throw Invalid(reason);
        var root = Path.GetDirectoryName(Full(report.ExecutablePath))!;
        if (AttributesOrMissing(GameFile(root, DirectXTextureCatalog.LoaderRelativePath)) is not null)
            throw Invalid("Rimuovi prima il componente texture DirectX: TexWizard usa un percorso alternativo.");
        var wide = ReShadeProfileService.ForWidescreen(_store, _running).Status(report.ExecutablePath);
        if (wide.Pending || wide.State?.Installed != true || wide.State.ExecutableSha256 != report.Sha256 || Live(root, "dinput8.dll", token) != _asiLoaderHash)
            throw Invalid("Installa prima il Widescreen Fix GamePanel con ASI loader verificato.");
        foreach (var file in wide.State.Files.Where(f => f.Path != WidescreenCatalog.Ini))
            if (Live(root, file.Path, token) != file.Sha256) throw Invalid("Componente Widescreen modificato o mancante: " + file.Path);
        foreach (var name in new[] { "dinput8.ini", "global.ini", "UltimateASILoader.ini", "scripts/dinput8.ini", "scripts/global.ini", "scripts/UltimateASILoader.ini" })
            if (AttributesOrMissing(GameFile(root, name)) is not null) throw Invalid("Configurazione ASI loader preesistente non verificata: " + name);
        var scripts = GameFile(root, "scripts"); var entries = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(scripts))
        {
            token.ThrowIfCancellationRequested(); GuardPath(path);
            if (++entries > 128) throw Invalid("Troppi componenti nella cartella scripts: verifica manuale necessaria.");
            if (!Path.GetExtension(path).Equals(".asi", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = "scripts/" + Path.GetFileName(path);
            if (relative.Equals(WidescreenCatalog.Plugin, StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.Equals(Loader, StringComparison.OrdinalIgnoreCase) && Live(root, relative, token) == _loaderHash) continue;
            throw Invalid("Plugin ASI preesistente non verificato: " + relative);
        }
    }
    private static void VerifyTextureMappings(string folder, IReadOnlyList<InstalledAssetPack> packs, PreparedAssetPack? package, CancellationToken token)
    {
        var owners = new Dictionary<uint, string>();
        foreach (var pack in packs.Where(p => p.Manifest.Kind == "Textures"))
        {
            var metadataPath = $"TexturePacks/GamePanel-{pack.Manifest.Id}/meta.json";
            var index = pack.Manifest.Files.ToList().FindIndex(f => f.Path == metadataPath); var file = pack.Manifest.Files[index];
            var path = package?.Manifest.Id == pack.Manifest.Id ? GameFile(Path.Combine(package.Folder, "files"), metadataPath) : CacheFile(folder, pack, index, true);
            VerifyFile(path, file, token);
            if (file.Bytes > AssetPackageImporter.MaximumMetadataBytes) throw Invalid("Metadati texture oltre il limite.");
            token.ThrowIfCancellationRequested(); byte[] bytes;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length != file.Bytes || input.Length > AssetPackageImporter.MaximumMetadataBytes) throw Invalid("Metadati texture cambiati durante la lettura.");
                bytes = new byte[(int)input.Length]; input.ReadExactly(bytes);
                if (input.ReadByte() != -1) throw Invalid("Metadati texture cresciuti durante la lettura.");
            }
            token.ThrowIfCancellationRequested();
            if (bytes.Length != file.Bytes || Hash(bytes) != file.Sha256) throw Invalid("Metadati texture cambiati durante la lettura.");
            AssetPackageImporter.ValidateTextureMetadata(bytes); using var doc = JsonDocument.Parse(bytes);
            foreach (var pair in doc.RootElement.GetProperty("textures").EnumerateArray())
            {
                var name = pair[0].GetString()!; uint hash;
                if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hash = uint.Parse(name.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                else { hash = uint.MaxValue; foreach (var c in name) hash = unchecked(hash * 33 + c); }
                if (owners.TryGetValue(hash, out var owner)) throw Invalid($"I pack {owner} e {pack.Manifest.Name} sostituiscono la stessa texture originale. Rimuovi il pack in conflitto, anche se disattivato.");
                owners.Add(hash, pack.Manifest.Name);
            }
        }
    }
    private void CheckIdentity(string exe, string hash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); _ = Folder(exe);
        if (_running(exe)) throw new InvalidOperationException("Chiudi il gioco prima di gestire gli asset.");
        if (!ValidHash(hash) || HashFile(exe, token) != hash) throw Invalid("EXE cambiato: operazione asset sospesa.");
    }
    private static InstalledAssetPack Find(AssetState? state, string id) => state?.Packs.SingleOrDefault(p => p.Manifest.Id == id) ?? throw Invalid("Pack non installato.");
    private static void VerifyManaged(string root, AssetState? state, CancellationToken token)
    { foreach (var f in state?.ManagedFiles ?? []) if (Live(root, f.Path, token) != f.Sha256) throw Invalid("File gestito modificato o mancante: " + f.Path); }
    private void SavePack(string folder, string root, PreparedAssetPack package, CancellationToken token)
    {
        var pack = new InstalledAssetPack(Normalize(package.Manifest), package.ArchiveSha256.ToLowerInvariant(), true); var dir = PackFolder(folder, pack);
        if (File.Exists(Path.Combine(dir, "baseline.json")))
        {
            var old = LoadBaseline(folder, pack);
            for (var i = 0; i < pack.Manifest.Files.Count; i++) VerifyFile(CacheFile(folder, pack, i, true), pack.Manifest.Files[i], token);
            foreach (var original in old.Originals)
            {
                var index = pack.Manifest.Files.ToList().FindIndex(f => f.Path == original.Path);
                VerifyFile(CacheFile(folder, pack, index, false), original, token);
                if (Live(root, original.Path, token) != original.Sha256) throw Invalid("Originale diverso dalla baseline conservata.");
            }
            return;
        }
        // A failed pre-journal attempt keeps its copies, but never authorizes their use without hashes.
        Directory.CreateDirectory(dir); GuardPath(dir); var originals = new List<AssetPackFile>();
        for (var i = 0; i < pack.Manifest.Files.Count; i++)
        {
            var f = pack.Manifest.Files[i]; var after = CacheFile(folder, pack, i, true);
            CacheCopy(GameFile(Path.Combine(package.Folder, "files"), f.Path), after, f.Sha256, token);
            if (IsCar(f.Path))
            {
                var source = GameFile(root, f.Path); var hash = HashFile(source, token); var bytes = new FileInfo(source).Length;
                CacheCopy(source, CacheFile(folder, pack, i, false), hash, token); originals.Add(new(f.Path, hash, bytes));
            }
        }
        WriteJson(Path.Combine(dir, "baseline.json"), new Baseline(pack.Manifest, pack.ArchiveSha256, originals));
    }
    private Baseline LoadBaseline(string folder, InstalledAssetPack pack)
    {
        var b = ReadAssetJson<Baseline>(Path.Combine(PackFolder(folder, pack), "baseline.json"));
        ValidateManifest(b.Manifest);
        if (JsonSerializer.Serialize(b.Manifest) != JsonSerializer.Serialize(pack.Manifest) || b.ArchiveSha256 != pack.ArchiveSha256 || b.Originals is null
            || b.Originals.Count != pack.Manifest.Files.Count(f => IsCar(f.Path)) || b.Originals.Select(f => f.Path).Distinct().Count() != b.Originals.Count
            || b.Originals.Any(f => !IsCar(f.Path) || pack.Manifest.Files.All(x => x.Path != f.Path) || !ValidHash(f.Sha256) || f.Bytes is < 1 or > MaximumFileBytes))
            throw Invalid("Baseline asset non coerente.");
        return b;
    }
    private void ValidateBaselines(string folder, AssetState? state)
    {
        if (state is null) return;
        foreach (var p in state.Packs)
        {
            var baseline = LoadBaseline(folder, p);
            if (p.Manifest.Kind == "Models" && !p.Enabled)
                foreach (var original in baseline.Originals)
                    if (state.ManagedFiles.Single(f => f.Path == original.Path) != original) throw Invalid("Stato disattivato diverso dalla baseline.");
        }
        ValidateRuntimePin(state);
    }
    private void ValidateRuntimePin(AssetState? state)
    {
        if (state?.Packs.Any(p => p.Manifest.Kind == "Textures") == true && state.ManagedFiles.Single(f => f.Path == Loader).Sha256 != _loaderHash)
            throw Invalid("Identità TexWizard nello stato non valida.");
    }
    private AssetState? LoadState(string folder, string exe)
    {
        var path = Path.Combine(folder, "assets-state.json"); GuardPath(path);
        if (!File.Exists(path)) return null; var state = ReadAssetJson<AssetState>(path); ValidateState(state, exe); ValidateBaselines(folder, state); return state;
    }
    private static void ValidateState(AssetState state, string exe)
    {
        if (state.SchemaVersion != 1 || !ValidId(state.Revision) || !SamePath(state.ExecutablePath, exe) || !ValidHash(state.ExecutableSha256)
            || state.Packs is null || state.ManagedFiles is null || state.Packs.Count > 128) throw Invalid("Stato asset non valido.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in state.Packs)
        {
            if (p is null || !ValidHash(p.ArchiveSha256)) throw Invalid("Identità pack non valida."); ValidateManifest(p.Manifest);
            if (!ids.Add(p.Manifest.Id)) throw Invalid("Pack duplicato.");
            foreach (var f in p.Manifest.Files) if (!files.Add(f.Path)) throw Invalid("Percorsi pack sovrapposti.");
        }
        if (state.Packs.Any(p => p.Manifest.Kind == "Textures")) { files.Add(Loader); files.Add(Config); }
        if (state.ManagedFiles.Any(f => f is null) || state.ManagedFiles.Count != files.Count || state.ManagedFiles.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count
            || state.ManagedFiles.Any(f => f is null || !files.Contains(f.Path) || !ValidHash(f.Sha256) || f.Bytes is < 1 or > MaximumFileBytes)) throw Invalid("File gestiti asset non validi.");
        if (state.Packs.Any(p => p.Manifest.Kind == "Textures"))
        {
            var bytes = Configuration(state.Packs);
            if (state.ManagedFiles.Single(f => f.Path == Config) != new AssetPackFile(Config, Hash(bytes), bytes.Length))
                throw Invalid("Configurazione TexWizard diversa dai soli pack attivi gestiti.");
        }
        foreach (var p in state.Packs)
            foreach (var f in p.Manifest.Files.Where(f => p.Enabled || !IsCar(f.Path)))
                if (state.ManagedFiles.Single(x => x.Path == f.Path) != f) throw Invalid("Stato asset diverso dal manifest.");
    }
    private Operation? LoadOperation(string folder, string exe)
    {
        var path = Path.Combine(folder, "assets-operation.json"); GuardPath(path);
        if (!File.Exists(path)) return null; var op = ReadAssetJson<Operation>(path); ValidateOperation(op, exe); ValidateBaselines(folder, op.Previous); ValidateBaselines(folder, op.Next);
        var baselines = (op.Previous?.Packs ?? []).Concat(op.Next.Packs).DistinctBy(p => p.Manifest.Id).Select(p => LoadBaseline(folder, p)).SelectMany(b => b.Originals).ToDictionary(f => f.Path);
        foreach (var target in baselines.Keys)
        {
            var before = op.Previous?.ManagedFiles.SingleOrDefault(f => f.Path == target)?.Sha256 ?? baselines[target].Sha256;
            var after = op.Next.ManagedFiles.SingleOrDefault(f => f.Path == target)?.Sha256 ?? baselines[target].Sha256;
            var change = op.Changes.SingleOrDefault(c => c.Path == target);
            if (before != after && change is null || change is not null && (change.BeforeSha256 != before || change.AfterSha256 != after))
                throw Invalid("Journal modello diverso dalla baseline o incompleto.");
        }
        return op;
    }
    private void ValidateOperation(Operation op, string exe)
    {
        if (op.SchemaVersion != 1 || !ValidId(op.Id) || !SamePath(op.ExecutablePath, exe) || !ValidHash(op.ExecutableSha256)
            || op.Status is not ("Pending" or "Complete" or "RolledBack") || op.Next is null || op.Changes is null) throw Invalid("Journal asset non valido.");
        ValidateState(op.Next, exe); if (op.Previous is not null) ValidateState(op.Previous, exe);
        ValidateRuntimePin(op.Previous); ValidateRuntimePin(op.Next);
        if (op.Next.ExecutableSha256 != op.ExecutableSha256 || op.Previous is not null && op.Previous.ExecutableSha256 != op.ExecutableSha256) throw Invalid("Identità journal non coerente.");
        var allowed = (op.Previous?.Packs ?? []).Concat(op.Next.Packs).SelectMany(p => p.Manifest.Files).Select(f => f.Path).Append(Loader).Append(Config).ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in op.Changes)
        {
            if (c is null || !allowed.Contains(c.Path) || !names.Add(c.Path) || c.BeforeSha256 == c.AfterSha256
                || c.BeforeSha256 is not null && !ValidHash(c.BeforeSha256) || c.AfterSha256 is not null && !ValidHash(c.AfterSha256)) throw Invalid("Percorso/hash journal non valido.");
            var old = op.Previous?.ManagedFiles.SingleOrDefault(f => f.Path == c.Path)?.Sha256;
            var next = op.Next.ManagedFiles.SingleOrDefault(f => f.Path == c.Path)?.Sha256;
            if ((!IsCar(c.Path) || old is not null) && old != c.BeforeSha256 || (!IsCar(c.Path) || next is not null) && next != c.AfterSha256)
                throw Invalid("Journal non coerente con lo stato.");
        }
        foreach (var path in (op.Previous?.ManagedFiles.Select(f => f.Path) ?? []).Concat(op.Next.ManagedFiles.Select(f => f.Path)).Distinct())
        {
            var before = op.Previous?.ManagedFiles.SingleOrDefault(f => f.Path == path)?.Sha256;
            var after = op.Next.ManagedFiles.SingleOrDefault(f => f.Path == path)?.Sha256;
            if (before != after && !names.Contains(path) && !IsCar(path)) throw Invalid("Journal incompleto.");
        }
    }
    private static void ValidateManifest(AssetPackManifest m)
    {
        if (m is null || m.SchemaVersion != 1 || m.Id is null || !Regex.IsMatch(m.Id, "^[a-z0-9-]{3,48}$") || m.Kind is not ("Textures" or "Models")
            || !DisplayText(m.Name) || !DisplayText(m.Author) || !Uri.TryCreate(m.SourceUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length != 0
            || m.Files is null || m.Files.Count is < 1 or > 2048) throw Invalid("Manifest asset non valido.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long bytes = 0;
        foreach (var f in m.Files)
        {
            if (f is null || ValidateRelative(f.Path) != f.Path || !names.Add(f.Path) || !ValidHash(f.Sha256) || f.Bytes is < 1 or > MaximumFileBytes) throw Invalid("File asset non valido.");
            bytes = checked(bytes + f.Bytes); if (bytes > 1024L * 1024 * 1024) throw Invalid("Pack oltre 1 GiB.");
            if (m.Kind == "Textures" && (f.Path == $"TexturePacks/GamePanel-{m.Id}/meta.json" || f.Path == $"TexturePacks/GamePanel-{m.Id}/textures.bin")) continue;
            if (m.Kind == "Models" && IsCar(f.Path)) continue;
            if (f.Path.StartsWith($"GamePanelAssets/{m.Id}/", StringComparison.Ordinal) && (f.Path.EndsWith(".txt", StringComparison.Ordinal) || f.Path.EndsWith(".md", StringComparison.Ordinal))) continue;
            throw Invalid("Percorso asset non consentito: " + f.Path);
        }
        foreach (var path in names)
            for (var at = path.LastIndexOf('/'); at > 0; at = path.LastIndexOf('/', at - 1))
                if (names.Contains(path[..at])) throw Invalid("Conflitto file/cartella nel manifest asset.");
        if (m.Kind == "Textures" && (!names.Contains($"TexturePacks/GamePanel-{m.Id}/meta.json") || !names.Contains($"TexturePacks/GamePanel-{m.Id}/textures.bin"))) throw Invalid("Pack texture incompleto.");
        var cars = m.Files.Where(f => IsCar(f.Path)).GroupBy(f => f.Path.Split('/')[1]);
        if (m.Kind == "Models" && (!cars.Any() || cars.Any(g => g.Count() != 2))) throw Invalid("Coppia modello incompleta.");
    }
    private void CheckNoPending(string folder)
    {
        CheckPeers(folder); var path = Path.Combine(folder, "assets-operation.json"); GuardPath(path);
        if (File.Exists(path)) { using var doc = ReadDocument(path); if (doc.RootElement.GetProperty("Status").GetString() == "Pending") throw new InvalidOperationException("Riprendi o annulla l'operazione asset interrotta."); }
    }
    private static void CheckPeers(string folder)
    {
        foreach (var file in new[] { "operation.json", "widescreen-operation.json", "directx-operation.json", "directx-replacements-operation.json", "remix-managed-operation.json" })
        { var path = Path.Combine(folder, file); GuardPath(path); if (File.Exists(path)) { using var doc = ReadDocument(path); if (doc.RootElement.GetProperty("Status").GetString() == "Pending") throw new InvalidOperationException("Operazione immagine/schermo interrotta: recuperala prima degli asset."); } }
    }
    private static T ReadAssetJson<T>(string path)
    { using var doc = ReadDocument(path); try { return doc.RootElement.Deserialize<T>() ?? throw Invalid("Metadati asset mancanti."); } catch (JsonException e) { throw new InvalidDataException("Metadati asset non validi.", e); } }
    private static JsonDocument ReadDocument(string path)
    {
        GuardPath(path); using var stream = File.OpenRead(path); if (stream.Length > 4 * 1024 * 1024) throw Invalid("Metadati asset troppo grandi.");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(stream, new() { MaxDepth = 24 }); }
        catch (JsonException e) { throw new InvalidDataException("JSON asset non valido.", e); }
        try { Check(doc.RootElement); return doc; } catch { doc.Dispose(); throw; }
        static void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw Invalid("Proprietà JSON asset duplicata."); Check(p.Value); }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Check(item);
        }
    }
    private string Folder(string exe)
    {
        var root = Path.GetDirectoryName(exe)!; GuardPath(root); GuardPath(_store);
        if (Inside(root, _store) || Inside(_store, root)) throw Invalid("Archivio asset e gioco devono essere separati.");
        var key = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var folder = Path.Combine(_store, Hash(Encoding.UTF8.GetBytes(key))); GuardPath(folder); return folder;
    }
    private static string Full(string path) => Path.GetFullPath(ExecutablePath.Normalize(path));
    private static bool SamePath(string a, string b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static byte[] Configuration(IReadOnlyList<InstalledAssetPack> packs) => JsonSerializer.SerializeToUtf8Bytes(new { packs = packs.Where(p => p.Manifest.Kind == "Textures" && p.Enabled).Select(p => "TexturePacks\\GamePanel-" + p.Manifest.Id).ToArray(), autoScan = false });
    private static AssetPackManifest Normalize(AssetPackManifest manifest) => manifest with { Files = manifest.Files.Select(f => f with { Sha256 = f.Sha256.ToLowerInvariant() }).ToArray() };
    private static bool DisplayText(string? s) => s is { Length: >= 1 and <= 100 } && !string.IsNullOrWhiteSpace(s) && !s.Any(char.IsControl);
    private static bool IsCar(string path) => Regex.IsMatch(path, "^CARS/[A-Z0-9_]{1,32}/(GEOMETRY|TEXTURES)\\.BIN$", RegexOptions.CultureInvariant);
    private static string? Live(string root, string relative, CancellationToken token)
    { var path = GameFile(root, relative); return AttributesOrMissing(path) is null ? null : HashFile(path, token); }
    private static string PackFolder(string folder, InstalledAssetPack pack)
    { if (!Regex.IsMatch(pack.Manifest.Id, "^[a-z0-9-]{3,48}$") || !ValidHash(pack.ArchiveSha256)) throw Invalid("Identità pack non valida."); var path = Path.Combine(folder, "asset-packs", pack.Manifest.Id, pack.ArchiveSha256); GuardPath(path); return path; }
    private static string CacheFile(string folder, InstalledAssetPack pack, int index, bool after) => Payload(PackFolder(folder, pack), index, after);
    private static string Payload(string folder, int index, bool after)
    { var path = Path.Combine(folder, $"{index:D4}-{(after ? "after" : "before")}.bin"); GuardPath(path); return path; }
    private static FileStream Acquire(string folder)
    { GuardPath(folder); var path = Path.Combine(folder, ".operation.lock"); GuardPath(path); return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private static void VerifyFile(string path, AssetPackFile file, CancellationToken token)
    { GuardPath(path); if (new FileInfo(path).Length != file.Bytes || HashFile(path, token) != file.Sha256) throw Invalid("Cache asset modificata: " + file.Path); }
    private static void CopyExpected(string source, string destination, string hash, CancellationToken token)
    { if (CopyVerified(source, destination, token, MaximumFileBytes).Hash != hash) throw Invalid("Hash della copia asset non corrispondente."); }
    private static void CacheCopy(string source, string destination, string hash, CancellationToken token)
    { if (File.Exists(destination)) { if (HashFile(destination, token) != hash) throw Invalid("Copia conservata danneggiata."); } else CopyExpected(source, destination, hash, token); }
    private static void DurableBytes(string path, byte[] bytes)
    { GuardPath(path); using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(bytes); stream.Flush(true); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static InvalidDataException Invalid(string message) => new(message);
}
