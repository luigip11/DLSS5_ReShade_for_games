using System.Security.Cryptography;
using System.Text.Json;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record RemixReleasePart(string Name, long Bytes, string Sha256);
public sealed record RemixReleaseManifest(int SchemaVersion, string PackageId, string Game, long Bytes, string Sha256,
    bool DistributionReviewed, IReadOnlyList<RemixReleasePart> Parts);
public sealed record RemixReleaseExport(string Folder, string ManifestPath, int Parts);

public sealed partial class RemixPackService
{
    // GitHub requires every release asset to be smaller than 2 GiB.
    public const long ReleasePartBytes = 2_000_000_000;
    public RemixReleaseExport ExportReleaseParts(RemixPackExportResult archive, string destination, CancellationToken token = default,
        IProgress<BackupProgress>? progress = null) => ExportReleaseParts(archive, destination, ReleasePartBytes, token, progress);

    internal RemixReleaseExport ExportReleaseParts(RemixPackExportResult archive, string destination, long partBytes,
        CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        if (partBytes is < 1 or > ReleasePartBytes || archive.Bytes <= 0 || !ValidHash(archive.Sha256)) throw Invalid("Archivio non valido.");
        destination = Path.GetFullPath(destination); GuardPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination) || !Directory.Exists(Path.GetDirectoryName(destination)))
            throw Invalid("Scegli una nuova cartella per le parti GitHub.");
        using var input = OpenRegular(archive.Path);
        if (input.Length != archive.Bytes || FreeSpace(destination) < archive.Bytes + 512L * 1024 * 1024) throw Invalid("Dimensione archivio cambiata o spazio insufficiente.");
        var total = checked((int)((archive.Bytes + partBytes - 1) / partBytes));
        if (total > 1000) throw Invalid("Numero di parti oltre limite.");
        var staging = Path.Combine(Path.GetDirectoryName(destination)!, ".gamepanel-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var parts = new List<RemixReleasePart>(); var buffer = new byte[1024 * 1024];
            using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var index = 1; index <= total; index++)
            {
                token.ThrowIfCancellationRequested(); var name = $"GamePanel-{_catalog.Game}-Remix.zip.part{index:D3}";
                using var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var part = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
                while (count < partBytes)
                {
                    token.ThrowIfCancellationRequested(); var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, partBytes - count));
                    if (read == 0) break;
                    output.Write(buffer, 0, read); part.AppendData(buffer, 0, read); whole.AppendData(buffer, 0, read); count += read;
                }
                parts.Add(new(name, count, Convert.ToHexString(part.GetHashAndReset()).ToLowerInvariant()));
                progress?.Report(new("Prepara parti GitHub", index, total));
            }
            if (input.Position != archive.Bytes || Convert.ToHexString(whole.GetHashAndReset()).ToLowerInvariant() != archive.Sha256)
                throw Invalid("Archivio modificato dopo l'esportazione.");
            var manifestName = $"GamePanel-{_catalog.Game}-Remix.manifest.json";
            var manifest = new RemixReleaseManifest(1, _catalog.Id, _catalog.Game, archive.Bytes, archive.Sha256, false, parts);
            File.WriteAllText(Path.Combine(staging, manifestName), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            token.ThrowIfCancellationRequested(); GuardPath(destination); Directory.Move(staging, destination);
            return new(destination, Path.Combine(destination, manifestName), parts.Count);
        }
        finally { GuardPath(staging); if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public RemixPackExportResult JoinReleaseParts(string manifestPath, string destination, CancellationToken token = default,
        IProgress<BackupProgress>? progress = null)
    {
        manifestPath = Path.GetFullPath(manifestPath); destination = Path.GetFullPath(destination); GuardPath(destination);
        using var manifestInput = OpenRegular(manifestPath);
        if (manifestInput.Length > 512 * 1024) throw Invalid("Manifest oltre limite.");
        RemixReleaseManifest manifest;
        try { manifest = JsonSerializer.Deserialize<RemixReleaseManifest>(manifestInput) ?? throw Invalid("Manifest vuoto."); }
        catch (JsonException e) { throw new InvalidDataException("Manifest non valido.", e); }
        if (manifest.SchemaVersion != 1 || manifest.PackageId != _catalog.Id || manifest.Game != _catalog.Game
            || manifest.Bytes is <= 0 or > 64L * 1024 * 1024 * 1024 || !ValidHash(manifest.Sha256)
            || manifest.Parts is null || manifest.Parts.Count is < 1 or > 1000
            || manifest.Parts.Any(p => p is null || p.Bytes is <= 0 or > ReleasePartBytes || !ValidHash(p.Sha256))
            || manifest.Parts.Sum(p => p.Bytes) != manifest.Bytes) throw Invalid("Manifest incompatibile o incompleto.");
        for (var i = 0; i < manifest.Parts.Count; i++)
            if (manifest.Parts[i].Name != $"GamePanel-{_catalog.Game}-Remix.zip.part{i + 1:D3}") throw Invalid("Nomi o ordine delle parti non validi.");
        if (!destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || File.Exists(destination) || Directory.Exists(destination)
            || !Directory.Exists(Path.GetDirectoryName(destination)) || FreeSpace(destination) < manifest.Bytes + 512L * 1024 * 1024)
            throw Invalid("Scegli un nuovo ZIP con spazio sufficiente.");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var done = 0;
                foreach (var part in manifest.Parts)
                {
                    using var input = OpenRegular(Path.Combine(Path.GetDirectoryName(manifestPath)!, part.Name));
                    var actual = Digest(input, part.Name, part.Bytes, token, output);
                    if (actual.Bytes != part.Bytes || actual.Sha256 != part.Sha256) throw Invalid("Parte incompleta o modificata: " + part.Name);
                    progress?.Report(new("Ricostruisci ZIP locale", ++done, manifest.Parts.Count));
                }
            }
            if (HashFile(temporary, token) != manifest.Sha256) throw Invalid("Hash dell'archivio non corrispondente.");
            token.ThrowIfCancellationRequested(); GuardPath(destination); File.Move(temporary, destination);
            // Reassembly verifies transport only; Prepare must still verify every catalog entry.
            return new(destination, manifest.Bytes, manifest.Sha256, 0);
        }
        finally { GuardPath(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
