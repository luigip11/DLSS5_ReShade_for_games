using System.Buffers.Binary;
using System.Security.Cryptography;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record RemixArchiveFile(string RelativePath, long Bytes, bool IsDirectory);
public sealed record RemixArchiveReport(int SchemaVersion, string SourceVersion, long ArchiveBytes,
    string Sha256, string Md5, bool MatchesPublishedSource, IReadOnlyList<RemixArchiveFile> Files,
    bool HasRootD3d9, IReadOnlyList<string> Warnings);

/// <summary>Reads one identified author archive. Never extracts, installs or executes its contents.</summary>
public sealed class RemixArchiveAudit
{
    public const string SourceVersion = "0.4";
    public const string SourceUrl = "https://www.moddb.com/mods/need-for-speed-underground-2-remix/downloads/underground-2-remix-04";
    public const long PublishedArchiveBytes = 2117971904;
    public const string PublishedMd5 = "e5b57ad2ea50d4ff22dbdd408c20a322";
    public const long MaximumArchiveBytes = 3L * 1024 * 1024 * 1024;
    public const long MaximumDeclaredBytes = 12L * 1024 * 1024 * 1024;
    public const long MaximumEntryBytes = 2L * 1024 * 1024 * 1024;
    public const int MaximumEntries = 30000;
    internal sealed record Pin(long Bytes, string Md5);
    private readonly Pin _pin;
    public RemixArchiveAudit() : this(new(PublishedArchiveBytes, PublishedMd5)) { }
    internal RemixArchiveAudit(Pin pin) { _pin = pin; }

    public RemixArchiveReport Analyze(string archivePath, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        archivePath = Path.GetFullPath(archivePath); GuardPath(archivePath);
        if (AttributesOrMissing(archivePath) is not FileAttributes attributes
            || (attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw Invalid("Seleziona un archivio Remix regolare.");
        using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        var length = input.Length; var modified = File.GetLastWriteTimeUtc(archivePath);
        if (length is < 32 or > MaximumArchiveBytes || length != _pin.Bytes)
            throw Invalid("Dimensione diversa dall'archivio Underground 2 Remix 0.4 pubblicato dall'autore.");
        Span<byte> header = stackalloc byte[32]; input.ReadExactly(header);
        if (!header[..6].SequenceEqual(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }) || header[6] != 0)
            throw Invalid("Formato 7z non riconosciuto: archivi SFX e volumi separati non sono ammessi.");
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(header[12..20]);
        var tableBytes = BinaryPrimitives.ReadUInt64LittleEndian(header[20..28]);
        if (tableBytes is 0 or > 16 * 1024 * 1024 || offset > (ulong)length - 32
            || tableBytes > (ulong)length - 32 - offset)
            throw Invalid("Tabella 7z fuori limite o archivio incompleto.");
        input.Position = 0;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[1024 * 1024]; long read = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested(); var count = input.Read(buffer);
            if (count == 0) break;
            read = checked(read + count); if (read > length) throw Invalid("Archivio cambiato durante la lettura.");
            sha.AppendData(buffer, 0, count); md5.AppendData(buffer, 0, count);
            progress?.Report(new("Calcolo hash archivio Remix", (int)(read * 1000 / length), 1000));
        }
        token.ThrowIfCancellationRequested();
        var sha256 = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        var md5sum = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
        Unchanged();
        var warnings = new List<string>
        {
            "Analisi della sola tabella: i contenuti dei file, i loro CRC e le licenze non sono stati verificati; nessun file è stato estratto o eseguito.",
            "Il confronto con l'MD5 pubblicato identifica la sorgente dichiarata, non certifica sicurezza o compatibilità con l'EXE, ReShade o Texture Toolkit. SHA-256 calcolato localmente, senza confronto con un pin indipendente."
        };
        if (!string.Equals(md5sum, _pin.Md5, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("MD5 diverso da quello pubblicato: archivio non identificato; lettura della tabella rifiutata.");
            return new(1, SourceVersion, length, sha256, md5sum, false, [], false, warnings.AsReadOnly());
        }
        input.Position = 0;
        var files = new List<RemixArchiveFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var bounded = new CancelableStream(input, token);
            using var archive = (SevenZipArchive)SevenZipArchive.OpenArchive(bounded, new ReaderOptions { LeaveStreamOpen = true, LookForHeader = false });
            if (archive.IsEncrypted || !archive.IsComplete || archive.Volumes.Count != 1) throw Invalid("Archivio 7z cifrato, incompleto o multivolume.");
            long declared = 0;
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (files.Count >= MaximumEntries) throw Invalid("Troppe voci nella tabella Remix.");
                var name = (entry.Key ?? "").Replace('\\', '/');
                if (entry.IsDirectory && name.EndsWith('/')) name = name[..^1];
                if (name.Length == 0 || name.Length > 512 || ValidateRelative(name) != name || !names.Add(name))
                    throw Invalid("Percorso 7z non relativo, ambiguo o duplicato.");
                var attr = unchecked((uint)(entry.Attrib ?? 0)); var extended = unchecked((uint)(entry.ExtendedAttrib ?? 0));
                var unixKind = (attr >> 28) & 15; var extendedKind = (extended >> 28) & 15;
                if (entry.LinkTarget is not null || entry.IsEncrypted || entry.IsSplitAfter
                    || !entry.IsComplete || entry.IsAnti || (attr & 0x400) != 0
                    || unixKind is not (0 or 4 or 8) || extendedKind is not (0 or 4 or 8))
                    throw Invalid("Link, file speciali, cancellazioni, cifratura o volumi 7z non ammessi.");
                if (entry.Size < 0 || entry.Size > MaximumEntryBytes || entry.IsDirectory && entry.Size != 0)
                    throw Invalid("Dimensione di una voce 7z fuori limite.");
                declared = checked(declared + entry.Size);
                if (declared > MaximumDeclaredBytes) throw Invalid("Dimensione complessiva dichiarata fuori limite.");
                files.Add(new(name, entry.Size, entry.IsDirectory));
            }
            if (files.Count == 0) throw Invalid("Archivio Remix vuoto.");
            var regular = files.Where(f => !f.IsDirectory).Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                for (var slash = file.RelativePath.IndexOf('/'); slash >= 0; slash = file.RelativePath.IndexOf('/', slash + 1))
                    if (regular.Contains(file.RelativePath[..slash])) throw Invalid("Conflitto tra file e cartella nell'archivio 7z.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or InvalidOperationException or OverflowException or NotSupportedException
            || e.GetType().Assembly == typeof(SevenZipArchive).Assembly)
        { throw Invalid("Tabella 7z non valida o non supportata: nessun contenuto è stato installato."); }
        Unchanged(); token.ThrowIfCancellationRequested();
        files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
        // An author wrapper folder is retained in the report, never silently stripped from paths.
        var rootD3d9 = files.Any(f => !f.IsDirectory && f.RelativePath.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase));
        if (rootD3d9) warnings.Add("d3d9.dll nella radice dell'archivio: richiede un profilo renderer separato da ReShade.");
        warnings.Add("Nessuna installazione autorizzata da questo report: occorrono audit del contenuto e un test reversibile sul gioco esistente.");
        return new(1, SourceVersion, length, sha256, md5sum, true, files.AsReadOnly(), rootD3d9, warnings.AsReadOnly());

        void Unchanged()
        {
            if (read != length || input.Length != length || new FileInfo(archivePath).Length != length || File.GetLastWriteTimeUtc(archivePath) != modified)
                throw Invalid("Archivio cambiato durante l'analisi: riprovare con una copia locale stabile.");
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);
    private sealed class CancelableStream(Stream inner, CancellationToken token) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { token.ThrowIfCancellationRequested(); inner.Position = value; } }
        public override int Read(byte[] buffer, int offset, int count) { token.ThrowIfCancellationRequested(); return inner.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { token.ThrowIfCancellationRequested(); return inner.Read(buffer); }
        public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return inner.Seek(offset, origin); }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
