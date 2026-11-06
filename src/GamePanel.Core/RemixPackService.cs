using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record RemixPackFile(string Path, long Bytes, string Sha256, string Kind);
public sealed record RemixPackDefinition(string Id, string Game, string ExecutableName, IReadOnlyList<string> ExecutableHashes, IReadOnlyList<RemixPackFile> Files)
{
    public static RemixPackDefinition ForGame(string game)
    {
        if (game is not ("UG1" or "UG2")) throw new ArgumentException("Gioco Remix non disponibile.");
        using var stream = typeof(RemixPackDefinition).Assembly.GetManifestResourceStream("GamePanel.Core.Catalogs." + game.ToLowerInvariant() + "-local-remix.json")
            ?? throw new InvalidDataException("Catalogo Remix non disponibile.");
        return JsonSerializer.Deserialize<RemixPackDefinition>(stream) ?? throw new InvalidDataException("Catalogo Remix vuoto.");
    }
}
public sealed class RemixPackPlan
{
    internal RemixPackPlan(string owner, GameReport report, string package, string destination, IReadOnlyList<RemixPackFile> source,
        IReadOnlyList<RemixPackFile> payload, bool models, long available)
    { Owner = owner; Report = report; Package = package; Destination = destination; Source = source; Payload = payload; Models = models; AvailableBytes = available; }
    internal string Owner { get; }
    internal GameReport Report { get; }
    internal IReadOnlyList<RemixPackFile> Source { get; }
    internal IReadOnlyList<RemixPackFile> Payload { get; }
    public string Package { get; }
    public string Destination { get; }
    public bool Models { get; }
    public int FileCount => Source.Count + Payload.Count;
    public long Bytes => checked(Source.Sum(f => f.Bytes) + Payload.Sum(f => f.Bytes));
    public long RequiredBytes => checked(Bytes + 512L * 1024 * 1024 + FileCount * 4096L);
    public long AvailableBytes { get; }
    public int PersonalizedConfigurations => Payload.Count(f => f.Kind == "personal-config");
}
public sealed record RemixPackResult(string ExecutablePath, string OriginalExecutablePath, string Folder, long Bytes, int Files);
public sealed record RemixPackExportResult(string Path, long Bytes, string Sha256, int Files);

/// <summary>Explicit local-only package deployment to a separate staging directory.
/// Catalog fingerprints identify reviewed files, not a security signature or redistribution grant.</summary>
public sealed partial class RemixPackService
{
    public const string Marker = "GamePanel-RemixPackage.json";
    private const string Ownership = ".gamepanel-package-owner";
    private readonly RemixPackDefinition _catalog;
    private readonly Func<string, bool> _running;
    private readonly string _owner = Guid.NewGuid().ToString("N");
    public RemixPackService(string game) : this(RemixPackDefinition.ForGame(game), IsGameRunning) { }
    internal RemixPackService(RemixPackDefinition catalog, Func<string, bool> running)
    {
        _catalog = catalog; _running = running;
        if (catalog.Files.Count is < 1 or > 20000 || catalog.Files.Sum(f => f.Bytes) > 64L * 1024 * 1024 * 1024
            || catalog.Files.Any(f => f.Bytes is < 0 or > 8L * 1024 * 1024 * 1024 || !ValidHash(f.Sha256)
                || f.Path.Equals(catalog.ExecutableName, StringComparison.OrdinalIgnoreCase))
            || catalog.Files.Select(f => ValidateRelative(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Files.Count)
            throw Invalid("Catalogo del pacchetto non valido.");
    }
    public RemixPackPlan Prepare(GameReport report, string package, string destination, bool models = false,
        CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(report); token.ThrowIfCancellationRequested(); CheckExecutable(report, token);
        package = Path.GetFullPath(package); destination = Path.GetFullPath(destination);
        var original = Path.GetDirectoryName(report.ExecutablePath)!; CheckDestination(original, package, destination);
        using var bundle = new Bundle(package, _catalog.Files);
        var selected = _catalog.Files.Where(f => f.Kind != "models" || models).ToArray();
        var payload = new List<RemixPackFile>();
        foreach (var file in selected)
        {
            token.ThrowIfCancellationRequested(); progress?.Report(new("Verifica pacchetto · " + file.Path, payload.Count, selected.Length));
            using var input = bundle.Open(file.Path);
            if (file.Kind == "config")
            {
                using var data = new MemoryStream(); var actual = Digest(input, file.Path, 1024 * 1024, token, data);
                _ = new UTF8Encoding(false, true).GetString(data.ToArray());
                payload.Add(actual with { Kind = actual.Sha256 == file.Sha256 ? "config" : "personal-config" });
            }
            else
            {
                var actual = Digest(input, file.Path, file.Bytes, token);
                if (actual.Bytes != file.Bytes || actual.Sha256 != file.Sha256) throw Invalid("Componente diverso dal catalogo: " + file.Path);
                payload.Add(actual with { Kind = file.Kind });
            }
        }
        var overrides = payload.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var source = ScanOriginal(original, overrides, token, progress);
        var bytes = checked(source.Sum(f => f.Bytes) + payload.Sum(f => f.Bytes) + 512L * 1024 * 1024 + (source.Count + payload.Count) * 4096L);
        var available = FreeSpace(destination); if (available < bytes) throw Invalid("Spazio insufficiente per la copia separata.");
        return new(_owner, report, package, destination, source.AsReadOnly(), payload.AsReadOnly(), models, available);
    }
    public RemixPackResult Create(RemixPackPlan plan, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        if (plan.Owner != _owner) throw Invalid("Riprepara l'anteprima in questa sessione.");
        token.ThrowIfCancellationRequested(); CheckExecutable(plan.Report, token);
        var original = Path.GetDirectoryName(plan.Report.ExecutablePath)!;
        CheckDestination(original, plan.Package, plan.Destination);
        if (FreeSpace(plan.Destination) < plan.RequiredBytes) throw Invalid("Spazio disponibile cambiato.");
        var staging = Path.Combine(Path.GetDirectoryName(plan.Destination)!, ".gamepanel-package-" + Guid.NewGuid().ToString("N"));
        var ownership = Guid.NewGuid().ToString("N"); var owned = false;
        try
        {
            GuardPath(staging); Directory.CreateDirectory(staging); File.WriteAllText(Path.Combine(staging, Ownership), ownership); owned = true;
            using var bundle = new Bundle(plan.Package, _catalog.Files); var completed = 0;
            foreach (var file in plan.Source)
            {
                token.ThrowIfCancellationRequested(); using var input = OpenRegular(GameFile(original, file.Path));
                Copy(input, staging, file, token); progress?.Report(new("Copia dati del gioco · " + file.Path, ++completed, plan.FileCount));
            }
            foreach (var file in plan.Payload)
            {
                token.ThrowIfCancellationRequested(); using var input = bundle.Open(file.Path);
                Copy(input, staging, file, token); progress?.Report(new("Installa pacchetto · " + file.Path, ++completed, plan.FileCount));
            }
            var targetExe = Path.Combine(staging, Path.GetFileName(plan.Report.ExecutablePath));
            using (var exe = OpenRegular(targetExe)) if (Digest(exe, "EXE", 64 * 1024 * 1024, token).Sha256 != plan.Report.Sha256) throw Invalid("EXE della copia non corrispondente.");
            CheckExecutable(plan.Report, token); token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(staging, Marker), JsonSerializer.Serialize(new
            {
                SchemaVersion = 1, PackageId = _catalog.Id, Game = _catalog.Game, SourceFolder = original,
                SourceExecutableSha256 = plan.Report.Sha256, ModelsIncluded = plan.Models,
                Purpose = "Local package copy; runtime test required", Files = plan.Payload
            }), new UTF8Encoding(false));
            CheckDestination(original, plan.Package, plan.Destination); GuardPath(staging);
            if (Directory.Exists(plan.Destination)) Directory.Delete(plan.Destination, recursive: false);
            token.ThrowIfCancellationRequested(); Directory.Move(staging, plan.Destination); owned = false;
            return new(Path.Combine(plan.Destination, Path.GetFileName(plan.Report.ExecutablePath)), plan.Report.ExecutablePath, plan.Destination, plan.Bytes, plan.FileCount);
        }
        finally
        {
            if (owned)
            {
                GuardPath(staging); var marker = Path.Combine(staging, Ownership); GuardPath(marker);
                if (File.Exists(marker) && File.ReadAllText(marker) == ownership) Directory.Delete(staging, recursive: true);
            }
        }
    }
    public RemixPackExportResult Export(string package, string destination, bool models = false, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        package = Path.GetFullPath(package); destination = Path.GetFullPath(destination); GuardPath(package); GuardPath(destination);
        if (!destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || Within(destination, package)
            || !Directory.Exists(Path.GetDirectoryName(destination)) || File.Exists(destination) || Directory.Exists(destination))
            throw Invalid("Scegli un nuovo ZIP esterno al pacchetto sorgente.");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var files = _catalog.Files.Where(f => f.Kind != "models" || models).ToArray();
        try
        {
            using (var bundle = new Bundle(package, _catalog.Files))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var done = 0;
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested(); using var input = bundle.Open(file.Path);
                    var entry = zip.CreateEntry(file.Path, CompressionLevel.Fastest); using var target = entry.Open();
                    if (file.Kind == "config")
                    {
                        using var buffer = new MemoryStream(); _ = Digest(input, file.Path, 1024 * 1024, token, buffer);
                        _ = new UTF8Encoding(false, true).GetString(buffer.ToArray()); buffer.Position = 0; buffer.CopyTo(target);
                    }
                    else
                    {
                        var actual = Digest(input, file.Path, file.Bytes, token, target);
                        if (actual.Bytes != file.Bytes || actual.Sha256 != file.Sha256) throw Invalid("Componente diverso dal catalogo: " + file.Path);
                    }
                    progress?.Report(new("Esporta pacchetto locale · " + file.Path, ++done, files.Length));
                }
            }
            token.ThrowIfCancellationRequested(); GuardPath(destination);
            var hash = HashFile(temporary, token); var bytes = new FileInfo(temporary).Length;
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination); return new(destination, bytes, hash, files.Length);
        }
        finally { GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string? ReadOrigin(string root, string executableHash)
    {
        try { return ReadOriginCore(root, executableHash); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Metadati del pacchetto non validi.", e); }
    }
    internal static bool HasMetadata(string root, string executableHash)
    {
        try { return ReadOriginCore(root, executableHash, verifyRuntime: false) is not null; }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException) { return false; }
    }
    private static string? ReadOriginCore(string root, string executableHash, bool verifyRuntime = true)
    {
        var path = GameFile(root, Marker); GuardPath(path); if (!File.Exists(path)) return null;
        using var input = OpenRegular(path); if (input.Length > 4 * 1024 * 1024) throw Invalid("Metadati del pacchetto oltre limite.");
        using var doc = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 16 }); var value = doc.RootElement;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() != 1)) throw Invalid("Campi duplicati nei metadati.");
        var game = value.GetProperty("Game").GetString() ?? throw Invalid("Gioco mancante nei metadati."); var catalog = RemixPackDefinition.ForGame(game);
        if (value.GetProperty("SchemaVersion").GetInt32() != 1 || value.GetProperty("PackageId").GetString() != catalog.Id
            || !catalog.ExecutableHashes.Contains(value.GetProperty("SourceExecutableSha256").GetString() ?? "", StringComparer.OrdinalIgnoreCase) || !catalog.ExecutableHashes.Contains(executableHash, StringComparer.OrdinalIgnoreCase))
            throw Invalid("Metadati del pacchetto non corrispondenti al gioco.");
        var original = value.GetProperty("SourceFolder").GetString() ?? throw Invalid("Origine mancante nei metadati.");
        if (original.StartsWith("\\\\", StringComparison.Ordinal) || original.StartsWith("//", StringComparison.Ordinal)) throw Invalid("L'origine deve essere una cartella locale.");
        GuardPath(original);
        if (!Path.IsPathFullyQualified(original) || Within(root, original) || Within(original, root)) throw Invalid("Origine del pacchetto non valida.");
        using (var originalExe = OpenRegular(Path.Combine(original, catalog.ExecutableName)))
            if (!catalog.ExecutableHashes.Contains(Digest(originalExe, "EXE originale", 64 * 1024 * 1024, CancellationToken.None).Sha256, StringComparer.OrdinalIgnoreCase)) throw Invalid("Originale associato non identificato.");
        if (!verifyRuntime) return Path.Combine(original, catalog.ExecutableName);
        var keys = new[] { "d3d9.dll", "dinput8.dll", ".trex/d3d9.dll", ".trex/NvRemixBridge.exe", ".trex/nvngx_dlss.dll", ".trex/nvngx_dlssd.dll", ".trex/nvngx_dlssg.dll",
            game == "UG1" ? "scripts/NFSUnderground.WidescreenFix.asi" : "scripts/NFSUnderground2.WidescreenFix.asi" };
        foreach (var key in keys)
        {
            var pin = catalog.Files.Single(f => f.Path == key); using var stream = OpenRegular(GameFile(root, key));
            if (Digest(stream, key, pin.Bytes, CancellationToken.None).Sha256 != pin.Sha256) throw Invalid("Runtime del pacchetto cambiato: " + key);
        }
        return Path.Combine(original, catalog.ExecutableName);
    }
    private void CheckExecutable(GameReport report, CancellationToken token)
    {
        if (report.HasAntiCheatIndicators || report.ScanWasLimited || !_catalog.ExecutableHashes.Contains(report.Sha256, StringComparer.OrdinalIgnoreCase)
            || !Path.GetFileName(report.ExecutablePath).Equals(_catalog.ExecutableName, StringComparison.OrdinalIgnoreCase)) throw Invalid("Questa copia del gioco non corrisponde agli EXE verificati.");
        if (_running(report.ExecutablePath)) throw Invalid("Chiudi il gioco prima di preparare la copia.");
        using var file = OpenRegular(report.ExecutablePath); if (Digest(file, "EXE", 64 * 1024 * 1024, token).Sha256 != report.Sha256) throw Invalid("EXE cambiato dall'analisi.");
        var pe = PeInspector.Read(report.ExecutablePath);
        if (pe.IsDll || pe.Architecture != "x86 (32 bit)" || !pe.Imports.Contains("d3d9.dll", StringComparer.OrdinalIgnoreCase)) throw Invalid("Serve un EXE x86 Direct3D 9 compatibile.");
    }
    private static List<RemixPackFile> ScanOriginal(string root, HashSet<string> overrides, CancellationToken token, IProgress<BackupProgress>? progress)
    {
        var result = new List<RemixPackFile>(); var pending = new Stack<string>(); pending.Push(root); long bytes = 0; var count = 0;
        while (pending.Count > 0)
        {
            var folder = pending.Pop(); GuardPath(folder);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                token.ThrowIfCancellationRequested(); if (++count > 50000) throw Invalid("Troppi file nella copia originale.");
                GuardPath(path); var relative = ValidateRelative(Path.GetRelativePath(root, path).Replace('\\', '/'));
                if (SkipOriginal(relative)) continue;
                if (Directory.Exists(path)) { pending.Push(path); continue; }
                if (overrides.Contains(relative)) continue;
                using var input = OpenRegular(path); var file = Digest(input, relative, 8L * 1024 * 1024 * 1024, token);
                bytes = checked(bytes + file.Bytes); if (bytes > 128L * 1024 * 1024 * 1024) throw Invalid("Copia originale oltre limite.");
                result.Add(file); progress?.Report(new("Lettura originale · " + relative, result.Count, 0));
            }
        }
        return result;
    }
    private static bool SkipOriginal(string path)
    {
        var first = path.Split('/')[0]; var name = Path.GetFileName(path);
        if (first.Equals("GamePanelAssets", StringComparison.OrdinalIgnoreCase))
            return !path.Equals("GamePanelAssets", StringComparison.OrdinalIgnoreCase) && !path.Equals("GamePanelAssets/smol-cars", StringComparison.OrdinalIgnoreCase)
                && (!path.StartsWith("GamePanelAssets/smol-cars/", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        return new[] { ".trex", "rtx-remix", "scripts", "GamePanelFX", "TexturePacks", "reshade-shaders" }.Contains(first, StringComparer.OrdinalIgnoreCase)
            || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dxvk-cache", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("GamePanel-", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".gamepanel-", StringComparison.OrdinalIgnoreCase)
            || new[] { "d3d9.dll", "dinput8.dll", "dxgi.dll", "d3d8.dll", "d3d11.dll", "d3d12.dll", "ddraw.dll", "dsound.dll", "winmm.dll", "version.dll", "ReShade.ini", "GamePanelPreset.ini", "nrc_session_log.txt", "metrics.txt" }.Contains(path, StringComparer.OrdinalIgnoreCase)
            || path.EndsWith(".asi", StringComparison.OrdinalIgnoreCase);
    }
    private static void CheckDestination(string original, string package, string destination)
    {
        GuardPath(original); GuardPath(package); GuardPath(destination);
        if (Within(destination, original) || Within(original, destination) || Within(destination, package) || Within(package, destination)
            || Within(package, original) || Within(original, package)) throw Invalid("Gioco, pacchetto e destinazione devono essere separati e non annidati.");
        foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        { var protectedRoot = Environment.GetFolderPath(special); if (protectedRoot.Length > 0 && Within(destination, protectedRoot)) throw Invalid("Scegli una cartella personale per la copia, fuori dalle cartelle di sistema."); }
        if (Path.GetPathRoot(destination) == destination || !Directory.Exists(Path.GetDirectoryName(destination))) throw Invalid("Scegli una destinazione con cartella superiore esistente.");
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw Invalid("La destinazione deve essere vuota.");
    }
    private static bool Within(string path, string root)
    { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
    private static long FreeSpace(string path) => new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace;
    private static FileStream OpenRegular(string path)
    { GuardPath(path); if (Directory.Exists(path)) throw Invalid("Era atteso un file: " + Path.GetFileName(path)); return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
    private static RemixPackFile Digest(Stream input, string path, long limit, CancellationToken token, Stream? output = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1024 * 1024]; long count = 0; int read;
        while ((read = input.Read(buffer)) != 0)
        { token.ThrowIfCancellationRequested(); count = checked(count + read); if (count > limit) throw Invalid("File oltre limite: " + path); hash.AppendData(buffer, 0, read); output?.Write(buffer, 0, read); }
        token.ThrowIfCancellationRequested(); return new(path, count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), "source");
    }
    private static void Copy(Stream input, string root, RemixPackFile expected, CancellationToken token)
    {
        var path = GameFile(root, expected.Path); GuardPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); GuardPath(path);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var actual = Digest(input, expected.Path, expected.Bytes, token, output); output.Flush(true);
        if (actual.Bytes != expected.Bytes || actual.Sha256 != expected.Sha256) throw Invalid("File cambiato dall'anteprima: " + expected.Path);
    }
    private sealed class Bundle : IDisposable
    {
        private readonly string _root;
        private readonly FileStream? _input;
        private readonly ZipArchive? _zip;
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        public Bundle(string path, IReadOnlyList<RemixPackFile> allowed)
        {
            _root = path; GuardPath(path); if (Directory.Exists(path)) return;
            try
            {
                _input = OpenRegular(path); _zip = new ZipArchive(_input, ZipArchiveMode.Read, leaveOpen: true);
                if (_zip.Entries.Count > 40000) throw Invalid("Troppi elementi nel pacchetto ZIP.");
                var lookup = allowed.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in _zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith('/')) { _ = ValidateRelative(name.TrimEnd('/')); continue; }
                    name = ValidateRelative(name);
                    if (!lookup.TryGetValue(name, out var expected) || !_entries.TryAdd(name, entry)
                        || entry.Length > (expected.Kind == "config" ? 1024 * 1024 : expected.Bytes)
                        || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw Invalid("File non previsto, duplicato o non valido nello ZIP: " + name);
                }
            }
            catch { _zip?.Dispose(); _input?.Dispose(); throw; }
        }
        public Stream Open(string relative)
        { if (_zip is null) return OpenRegular(GameFile(_root, relative)); return _entries.TryGetValue(relative, out var entry) ? entry.Open() : throw Invalid("File richiesto assente nel pacchetto: " + relative); }
        public void Dispose() { _zip?.Dispose(); _input?.Dispose(); }
    }
    private static InvalidDataException Invalid(string message) => new(message);
}
