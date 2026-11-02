using System.Security.Cryptography;
using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record PreparedDirectXBatch(string Folder, int FilesCount, int SkippedCount, long OutputBytes,
    IReadOnlyList<string> Warnings, bool WasLimited, string ManifestSha256);
public sealed record BatchInstallResult(int InstalledCount, int AlreadyInstalledCount, int TotalCount, bool Cancelled);
public sealed record DirectXBatchFile(string Hash, string CaptureSha256, string OutputSha256, long OutputBytes);
internal sealed record DirectXBatchManifest(int SchemaVersion, string ExecutablePath, string ExecutableSha256,
    string ResourceRoot, IReadOnlyList<DirectXBatchFile> Files, int SkippedCount, long OutputBytes, bool WasLimited);

/// <summary>Bounded, content-preserving 2x preparation outside the game; installation is separately guarded.</summary>
public sealed class DirectXTextureBatchService
{
    public const int MaximumFiles = 2048;
    public const long MaximumSourceBytes = 1024L * 1024 * 1024, MaximumOutputBytes = 512L * 1024 * 1024;
    public const string StatusLabel = DdsAutomaticEnhancer.StatusLabel;
    private readonly DirectXTextureProfileService _profiles;
    private readonly string _preparedRoot;
    public DirectXTextureBatchService(string store, Func<string, bool>? running = null, string? preparedRoot = null)
        : this(new DirectXTextureProfileService(store, running), preparedRoot ?? Path.Combine(GamePanelPaths.Root, "PreparedTextures")) { }
    internal DirectXTextureBatchService(DirectXTextureProfileService profiles, string preparedRoot)
    { _profiles = profiles; _preparedRoot = Path.GetFullPath(preparedRoot); }

    public PreparedDirectXBatch Prepare(GameReport report, string resourceRoot, CancellationToken token = default,
        IProgress<BackupProgress>? progress = null)
    {
        CheckPreparationContext(report, resourceRoot, token);
        var game = Path.GetDirectoryName(Path.GetFullPath(report.ExecutablePath))!;
        if (Within(_preparedRoot, game) || Within(_preparedRoot, resourceRoot)) throw new InvalidDataException("La cache preparata deve essere esterna al gioco e alle catture.");
        GuardPath(_preparedRoot); Directory.CreateDirectory(_preparedRoot); GuardPath(_preparedRoot);
        var folder = Path.Combine(_preparedRoot, Guid.NewGuid().ToString("N")); GuardPath(folder); Directory.CreateDirectory(folder);
        var files = new List<DirectXBatchFile>(); var warnings = new List<string>(); long bytes = 0; var skipped = 0; var limited = false;
        try
        {
            var scan = Scan(resourceRoot, token); skipped = scan.Skipped; limited = scan.Limited; warnings.AddRange(scan.Warnings);
            foreach (var capture in scan.Captures)
            {
                token.ThrowIfCancellationRequested();
                if (capture.Info.Width > 512 || capture.Info.Height > 512) { skipped++; continue; }
                var data = ReadBound(capture.FilePath, DirectXTextureData.MaximumDdsBytes, token);
                if (DirectXTextureData.InspectDds(data).Sha256 != capture.Info.Sha256) throw new InvalidDataException("Cattura cambiata durante la preparazione.");
                var output = DdsAutomaticEnhancer.Enhance(data, token);
                if (bytes + output.LongLength > MaximumOutputBytes) { limited = true; warnings.Add("Raggiunto il limite di 512 MiB di uscita; le catture restanti non sono preparate."); break; }
                var info = DirectXTextureData.InspectDds(output); var path = Path.Combine(folder, capture.Hash + ".dds"); GuardPath(path);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(output); stream.Flush(true); }
                files.Add(new(capture.Hash, capture.Info.Sha256, info.Sha256, output.LongLength)); bytes += output.LongLength;
                progress?.Report(new(StatusLabel, files.Count, scan.Captures.Count));
            }
            CheckPreparationContext(report, resourceRoot, token);
            var manifest = new DirectXBatchManifest(1, Path.GetFullPath(report.ExecutablePath), report.Sha256,
                Path.GetFullPath(resourceRoot), files, skipped, bytes, limited);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            var manifestPath = Path.Combine(folder, "manifest.json"); GuardPath(manifestPath);
            using (var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(manifestBytes); stream.Flush(true); }
            if (files.Count == 0) warnings.Add("Nessuna cattura compatibile fino a 512 × 512 da preparare.");
            if (skipped > 0) warnings.Add($"{skipped} catture ignorate: dimensioni superiori a 512 × 512, formato non supportato o DDS non valido.");
            return new(folder, files.Count, skipped, bytes, warnings, limited, manifestHash);
        }
        catch { GuardPath(folder); Directory.Delete(folder, true); throw; }
    }

    public BatchInstallResult Install(GameReport report, string resourceRoot, PreparedDirectXBatch prepared,
        CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        var folder = Path.GetFullPath(prepared.Folder); GuardPath(folder);
        if (!Within(folder, _preparedRoot) || Same(folder, _preparedRoot)) throw new InvalidDataException("Cache preparata non appartenente a questo profilo.");
        var bytes = ReadBound(Path.Combine(folder, "manifest.json"), 2 * 1024 * 1024, token);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != prepared.ManifestSha256)
            throw new InvalidDataException("Manifest preparato cambiato: prepara e rivedi nuovamente il batch.");
        DirectXBatchManifest manifest;
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 12 }); RejectDuplicates(document.RootElement);
            manifest = document.RootElement.Deserialize<DirectXBatchManifest>() ?? throw new InvalidDataException("Manifest vuoto.");
        }
        catch (JsonException e) { throw new InvalidDataException("Manifest non valido.", e); }
        if (manifest.SchemaVersion != 1 || !Same(manifest.ExecutablePath, report.ExecutablePath) || manifest.ExecutableSha256 != report.Sha256
            || !Same(manifest.ResourceRoot, resourceRoot) || manifest.Files is null || manifest.Files.Count is < 1 or > MaximumFiles
            || manifest.OutputBytes is < 1 or > MaximumOutputBytes || manifest.Files.Any(f => f is null || f.OutputBytes is < 128 or > DirectXTextureData.MaximumDdsBytes)
            || manifest.Files.Sum(f => f.OutputBytes) != manifest.OutputBytes
            || manifest.Files.Count != prepared.FilesCount || manifest.OutputBytes != prepared.OutputBytes
            || manifest.WasLimited != prepared.WasLimited || manifest.SkippedCount != prepared.SkippedCount)
            throw new InvalidDataException("Manifest non associato a questo EXE/cartella texture o fuori limite.");
        return new DirectXTextureReplacementService(_profiles).InstallBatch(report, resourceRoot, folder, manifest.Files, token, progress);
    }

    private void CheckPreparationContext(GameReport report, string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var status = _profiles.Status(report.ExecutablePath);
        if (status.Pending || !status.Installed || status.State?.ExecutableSha256 != report.Sha256 || !Same(status.ResourceRoot, root)
            || HashFile(report.ExecutablePath, token) != report.Sha256) throw new InvalidDataException("Runtime DirectX verificato richiesto per preparare queste catture.");
    }

    internal sealed record CaptureScan(IReadOnlyList<DirectXTextureCapture> Captures, int Skipped, IReadOnlyList<string> Warnings, bool Limited);
    internal static CaptureScan Scan(string root, CancellationToken token)
    {
        root = Path.GetFullPath(root); GuardPath(root); var dump = Path.Combine(root, "dump"); GuardPath(dump);
        var result = new List<DirectXTextureCapture>(); var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>(); var skipped = 0; var count = 0; var entries = 0; long bytes = 0; var limited = false;
        if (AttributesOrMissing(dump) is null) return new(result, 0, warnings, false);
        if (AttributesOrMissing(dump) is not FileAttributes dir || !dir.HasFlag(FileAttributes.Directory)) throw new InvalidDataException("Cartella catture non valida.");
        foreach (var entry in new DirectoryInfo(dump).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (++entries > 16384) { limited = true; break; }
            if (!entry.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase)) continue;
            if (++count > MaximumFiles) { limited = true; break; }
            try
            {
                GuardPath(entry.FullName);
                if (entry is not FileInfo file || (file.Attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0
                    || file.Length is < 128 or > DirectXTextureData.MaximumDdsBytes) throw new InvalidDataException("DDS non regolare o fuori limite.");
                bytes = checked(bytes + file.Length); if (bytes > MaximumSourceBytes) { limited = true; break; }
                var hash = DirectXTextureData.NormalizeHash(Path.GetFileNameWithoutExtension(file.Name));
                if (!keys.Add(hash)) throw new InvalidDataException("Hash cattura duplicato.");
                var info = DirectXTextureData.ReadDds(file.FullName, token);
                if (info.TopMipHash != hash) throw new InvalidDataException("Hash nome diverso dai pixel catturati.");
                result.Add(new(hash, file.FullName, info));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
            { skipped++; if (warnings.Count < 64) warnings.Add(entry.Name + ": " + e.Message); }
        }
        if (limited) warnings.Add("Scansione parziale: limite 2048 catture, 1 GiB sorgente o 16384 voci raggiunto.");
        return new(result.OrderBy(c => c.Hash, StringComparer.Ordinal).ToArray(), skipped, warnings, limited);
    }
    private static byte[] ReadBound(string path, int maximum, CancellationToken token)
    {
        GuardPath(path); if (AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("File preparato non regolare.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum || stream.Length < 1) throw new InvalidDataException("File fuori limite.");
        var data = new byte[(int)stream.Length];
        for (var offset = 0; offset < data.Length;) { token.ThrowIfCancellationRequested(); var n = stream.Read(data, offset, Math.Min(65536, data.Length - offset)); if (n == 0) throw new InvalidDataException("File troncato."); offset += n; }
        if (stream.ReadByte() != -1) throw new InvalidDataException("File cresciuto durante la lettura."); return data;
    }
    private static void RejectDuplicates(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object) { var keys = new HashSet<string>(StringComparer.Ordinal); foreach (var p in item.EnumerateObject()) { if (!keys.Add(p.Name)) throw new InvalidDataException("Manifest con chiavi duplicate."); RejectDuplicates(p.Value); } }
        else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) RejectDuplicates(child);
    }
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Within(string path, string root) => Same(path, root) || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
