using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using static GamePanel.Core.BackupService;

namespace GamePanel.Core;

public sealed record DdsTextureInfo(int Width, int Height, int MipLevels, string Format, long Bytes,
    string TopMipHash, bool FullMipChain, string Sha256);
public sealed record DirectXTextureCapture(string Hash, string FilePath, DdsTextureInfo Info);
public sealed record DirectXTextureCaptureScan(IReadOnlyList<DirectXTextureCapture> Captures, IReadOnlyList<string> Warnings, bool WasLimited);

/// <summary>Bounded 2D DDS inspection. Never decodes through a native codec or executes a component.</summary>
public static class DirectXTextureData
{
    public const int MaximumDimension = 8192;
    public const int MaximumDdsBytes = 128 * 1024 * 1024;
    public const int MaximumCaptureFiles = 256;
    public const long MaximumCaptureScanBytes = 512 * 1024 * 1024;

    public static DdsTextureInfo ReadDds(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); GuardPath(path);
        if (!Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase)
            || AttributesOrMissing(path) is not FileAttributes a || (a & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Seleziona un file DDS regolare.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 128 or > MaximumDdsBytes) throw new InvalidDataException("Dimensione DDS fuori limite.");
        var data = new byte[checked((int)input.Length)];
        for (var offset = 0; offset < data.Length;)
        {
            token.ThrowIfCancellationRequested(); var n = input.Read(data, offset, Math.Min(65536, data.Length - offset));
            if (n == 0) throw new InvalidDataException("DDS troncato durante la lettura."); offset += n;
        }
        if (input.ReadByte() != -1 || input.Length != data.Length) throw new InvalidDataException("DDS cambiato durante la lettura.");
        return InspectDds(data);
    }

    public static DdsTextureInfo ValidateReplacement(string originalHash, string path, CancellationToken token = default)
    {
        _ = NormalizeHash(originalHash);
        // The filename key identifies ORIGINAL pixels, so replacement pixels must not be compared to it.
        return ReadDds(path, token);
    }

    public static IReadOnlyList<DirectXTextureCapture> ListCaptures(string resourceRoot, CancellationToken token = default)
        => InspectCaptures(resourceRoot, token).Captures;

    public static DirectXTextureCaptureScan InspectCaptures(string resourceRoot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); resourceRoot = Path.GetFullPath(resourceRoot); GuardPath(resourceRoot);
        var dump = Path.Combine(resourceRoot, "dump"); GuardPath(dump);
        var captures = new List<DirectXTextureCapture>(); var warnings = new List<string>(); var limited = false;
        if (AttributesOrMissing(dump) is null) return new(captures, warnings, false);
        if (AttributesOrMissing(dump) is not FileAttributes dir || !dir.HasFlag(FileAttributes.Directory))
            throw new InvalidDataException("La cartella delle catture non è una directory regolare.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0; var entries = 0; var files = 0;
        foreach (var entry in new DirectoryInfo(dump).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (++entries > 1024) { limited = true; break; }
            if (!entry.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase)) continue;
            if (++files > MaximumCaptureFiles) { limited = true; break; }
            try
            {
                GuardPath(entry.FullName);
                if (entry is not FileInfo file || (file.Attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
                    throw new InvalidDataException("Voce non regolare.");
                if (file.Length < 128 || file.Length > MaximumDdsBytes) throw new InvalidDataException("Dimensione DDS fuori limite.");
                total = checked(total + file.Length);
                if (total > MaximumCaptureScanBytes) { limited = true; break; }
                var key = NormalizeHash(Path.GetFileNameWithoutExtension(file.Name));
                if (!keys.Add(key)) throw new InvalidDataException("Nome hash duplicato o ambiguo.");
                var info = ReadDds(file.FullName, token);
                if (!info.TopMipHash.Equals(key, StringComparison.Ordinal))
                    throw new InvalidDataException("L'hash del nome non corrisponde ai pixel originali catturati.");
                captures.Add(new(key, file.FullName, info));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
            { warnings.Add(entry.Name + ": " + e.Message); }
        }
        return new(captures.OrderBy(c => c.Hash, StringComparer.Ordinal).ToArray(), warnings, limited);
    }

    public static string NormalizeHash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 16 || hash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("L'identità della texture deve contenere esattamente 16 cifre esadecimali.");
        return hash.ToUpperInvariant();
    }

    internal static DdsTextureInfo InspectDds(byte[] data)
    {
        if (data.Length is < 128 or > MaximumDdsBytes) throw new InvalidDataException("Dimensione DDS fuori limite.");
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        if (U32(0) != 0x20534444 || U32(4) != 124 || U32(76) != 32 || (U32(8) & 0x1007) != 0x1007 || (U32(108) & 0x1000) == 0)
            throw new InvalidDataException("Header DDS non valido.");
        var w = U32(16); var h = U32(12);
        if (w is < 1 or > MaximumDimension || h is < 1 or > MaximumDimension || U32(24) > 1 || U32(112) != 0)
            throw new InvalidDataException("Sono ammesse solo texture DDS 2D, fino a 8192 × 8192, senza cubemap o volumi.");
        var naturalMips = NaturalMips((int)w, (int)h); var mipCount = U32(28) == 0 ? 1 : U32(28);
        if (mipCount > naturalMips || mipCount > 1 && (U32(8) & 0x20000) == 0)
            throw new InvalidDataException("Catena mip DDS non valida.");
        var headerBytes = 128; int blockBytes; string format;
        if ((U32(80) & 4) != 0)
        {
            var cc = U32(84);
            if (cc == 0x30315844) // DX10; this is the form written by Texture Toolkit's dump function.
            {
                if (data.Length < 148 || U32(132) != 3 || U32(136) != 0 || U32(140) != 1)
                    throw new InvalidDataException("DDS DX10 richiede una sola texture 2D, senza array o cubemap.");
                headerBytes = 148;
                (format, blockBytes) = U32(128) switch
                {
                    71 => ("DXT1 / BC1", 8), 72 => ("DXT1 / BC1 sRGB", 8),
                    74 => ("DXT3 / BC2", 16), 75 => ("DXT3 / BC2 sRGB", 16),
                    77 => ("DXT5 / BC3", 16), 78 => ("DXT5 / BC3 sRGB", 16),
                    28 => ("RGBA8", 0), 29 => ("RGBA8 sRGB", 0),
                    87 => ("BGRA8", 0), 91 => ("BGRA8 sRGB", 0),
                    _ => throw new InvalidDataException("Formato DDS DX10 non supportato: usa BC1, BC2, BC3, RGBA8 o BGRA8.")
                };
            }
            else (format, blockBytes) = cc switch
            {
                0x31545844 => ("DXT1 / BC1", 8), 0x33545844 => ("DXT3 / BC2", 16), 0x35545844 => ("DXT5 / BC3", 16),
                _ => throw new InvalidDataException("Formato DDS non supportato: usa DXT1, DXT3, DXT5, RGBA8 o BGRA8.")
            };
        }
        else
        {
            if ((U32(80) & 0x40) == 0 || U32(88) != 32 || U32(96) != 0x0000ff00 || U32(104) is not (0 or 0xff000000))
                throw new InvalidDataException("Formato DDS a pixel non supportato: usa RGBA8 o BGRA8.");
            if (U32(92) == 0x00ff0000 && U32(100) == 0x000000ff) format = "BGRA8";
            else if (U32(92) == 0x000000ff && U32(100) == 0x00ff0000) format = "RGBA8";
            else throw new InvalidDataException("Maschere dei canali DDS non supportate.");
            blockBytes = 0;
        }
        long expected = headerBytes; var width = (int)w; var height = (int)h; var topBytes = LevelBytes(width, height, blockBytes);
        for (var mip = 0; mip < mipCount; mip++)
        {
            expected = checked(expected + LevelBytes(width, height, blockBytes)); width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        if (expected != data.Length) throw new InvalidDataException("Dati DDS troncati, extra o non coerenti con la catena mip dichiarata.");
        var hash = ComputeTopMipHash(data.AsSpan(headerBytes, checked((int)topBytes)));
        return new((int)w, (int)h, (int)mipCount, format, data.LongLength, hash, mipCount == naturalMips,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
    }

    private static long LevelBytes(int width, int height, int blockBytes) => blockBytes == 0
        ? checked((long)width * height * 4)
        : checked((long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockBytes);
    private static int NaturalMips(int width, int height)
    { var n = 1; while (width > 1 || height > 1) { width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); n++; } return n; }

    // Algorithm from Texture Toolkit v1.2.1 TextureHash.cpp/h (MIT, copyright BadassBaboon).
    // See docs/third-party/TextureToolkit-MIT.txt. Hash only mip 0's tightly packed rows.
    internal static string ComputeTopMipHash(ReadOnlySpan<byte> pixels)
    {
        unchecked
        {
            const ulong prime1 = 0x9e3779b185ebca87, prime2 = 0xc2b2ae3d27d4eb4f;
            var hash = 0x27d4eb2f165667c5UL; var offset = 0;
            ulong Mix(ulong value, ulong word) => BitOperations.RotateLeft(value ^ word * prime1, 31) * prime2;
            for (; offset + 8 <= pixels.Length; offset += 8)
                hash = Mix(hash, BinaryPrimitives.ReadUInt64LittleEndian(pixels.Slice(offset, 8)));
            if (offset < pixels.Length)
            {
                Span<byte> tail = stackalloc byte[8]; tail.Clear(); pixels[offset..].CopyTo(tail);
                hash = Mix(hash, BinaryPrimitives.ReadUInt64LittleEndian(tail));
            }
            hash ^= (ulong)pixels.Length;
            hash ^= hash >> 33; hash *= 0xff51afd7ed558ccd; hash ^= hash >> 33; hash *= 0xc4ceb9fe1a85ec53; hash ^= hash >> 33;
            return hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
