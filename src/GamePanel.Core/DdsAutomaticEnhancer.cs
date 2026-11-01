using System.Buffers.Binary;

namespace GamePanel.Core;

/// <summary>Deterministic interpolation and block compression; does not synthesize new detail.</summary>
public static class DdsAutomaticEnhancer
{
    public const string StatusLabel = "2x textures catturate • interpolazione, non nuovi asset";

    public static byte[] Enhance(byte[] source, CancellationToken token = default)
    {
        var info = DirectXTextureData.InspectDds(source);
        if (info.Width > 512 || info.Height > 512)
            throw new InvalidDataException("Sono elaborate solo catture fino a 512 × 512 (uscita fino a 1024 × 1024).");
        var pixels = Decode(source, info, token);
        var transparent = false;
        for (var i = 3; i < pixels.Length; i += 4) transparent |= pixels[i] != 255;
        var kind = info.Format.StartsWith("DXT3", StringComparison.Ordinal) ? 2
            : info.Format.StartsWith("DXT5", StringComparison.Ordinal) || transparent ? 3 : 1;
        var width = info.Width * 2; var height = info.Height * 2;
        pixels = Resize(pixels, info.Width, info.Height, width, height, token);
        var srgb = info.Format.Contains("sRGB", StringComparison.Ordinal);
        var header = srgb ? 148 : 128; var blockBytes = kind == 1 ? 8 : 16;
        var mips = 1; long length = header;
        for (int w = width, h = height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
        {
            length += Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockBytes;
            if (w == 1 && h == 1) break; mips++;
        }
        var output = new byte[checked((int)length)];
        void U(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at, 4), value);
        U(0, 0x20534444); U(4, 124); U(8, 0xa1007); U(12, (uint)height); U(16, (uint)width);
        U(20, (uint)(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockBytes));
        U(28, (uint)mips); U(76, 32); U(80, 4); U(84, srgb ? 0x30315844 : kind == 1 ? 0x31545844u : kind == 2 ? 0x33545844u : 0x35545844u);
        U(108, 0x401008);
        if (srgb) { U(128, kind == 1 ? 72u : kind == 2 ? 75u : 78u); U(132, 3); U(140, 1); }
        var offset = header;
        for (int w = width, h = height; ;)
        {
            for (var by = 0; by < h; by += 4)
            {
                token.ThrowIfCancellationRequested();
                for (var bx = 0; bx < w; bx += 4)
                {
                    EncodeBlock(pixels, w, h, bx, by, kind, output.AsSpan(offset, blockBytes)); offset += blockBytes;
                }
            }
            if (w == 1 && h == 1) break;
            var nw = Math.Max(1, w / 2); var nh = Math.Max(1, h / 2);
            pixels = Resize(pixels, w, h, nw, nh, token); w = nw; h = nh;
        }
        _ = DirectXTextureData.InspectDds(output); return output;
    }

    internal static byte[] Decode(byte[] source, DdsTextureInfo info, CancellationToken token = default)
    {
        var dx10 = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(84)) == 0x30315844;
        var start = dx10 ? 148 : 128;
        var bc1 = info.Format.StartsWith("DXT1", StringComparison.Ordinal);
        var bc2 = info.Format.StartsWith("DXT3", StringComparison.Ordinal);
        var compressed = bc1 || bc2 || info.Format.StartsWith("DXT5", StringComparison.Ordinal);
        var bgra = info.Format.StartsWith("BGRA", StringComparison.Ordinal);
        var opaque = !dx10 && !compressed && BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(104)) == 0;
        var result = new byte[checked(info.Width * info.Height * 4)]; var size = bc1 ? 8 : 16;
        Span<byte> colors = stackalloc byte[16]; Span<byte> alpha = stackalloc byte[8];
        for (var y = 0; y < info.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < info.Width; x++)
            {
                var at = (y * info.Width + x) * 4;
                if (!compressed)
                {
                    var src = start + at; result[at] = source[src + (bgra ? 2 : 0)]; result[at + 1] = source[src + 1];
                    result[at + 2] = source[src + (bgra ? 0 : 2)]; result[at + 3] = opaque ? (byte)255 : source[src + 3]; continue;
                }
                var block = source.AsSpan(start + ((y / 4) * Math.Max(1, (info.Width + 3) / 4) + x / 4) * size, size);
                var color = bc1 ? block : block[8..];
                var c0 = BinaryPrimitives.ReadUInt16LittleEndian(color); var c1 = BinaryPrimitives.ReadUInt16LittleEndian(color[2..]);
                Palette(c0, c1, colors, bc1 && c0 <= c1);
                var p = (y % 4) * 4 + x % 4;
                var selector = (int)(BinaryPrimitives.ReadUInt32LittleEndian(color[4..]) >> (p * 2)) & 3;
                colors.Slice(selector * 4, 4).CopyTo(result.AsSpan(at, 4));
                if (bc2) result[at + 3] = (byte)((BinaryPrimitives.ReadUInt64LittleEndian(block) >> (p * 4) & 15) * 17);
                else if (!bc1)
                {
                    AlphaPalette(block[0], block[1], alpha); ulong bits = 0;
                    for (var i = 0; i < 6; i++) bits |= (ulong)block[i + 2] << (i * 8);
                    result[at + 3] = alpha[(int)(bits >> (p * 3) & 7)];
                }
            }
        }
        return result;
    }

    private static byte[] Resize(byte[] source, int sw, int sh, int dw, int dh, CancellationToken token)
    {
        var result = new byte[dw * dh * 4];
        for (var y = 0; y < dh; y++)
        {
            token.ThrowIfCancellationRequested();
            var fy = Math.Clamp((y + .5) * sh / dh - .5, 0, sh - 1); var y0 = (int)fy; var y1 = Math.Min(sh - 1, y0 + 1); fy -= y0;
            for (var x = 0; x < dw; x++)
            {
                var fx = Math.Clamp((x + .5) * sw / dw - .5, 0, sw - 1); var x0 = (int)fx; var x1 = Math.Min(sw - 1, x0 + 1); fx -= x0;
                var a = (y0 * sw + x0) * 4; var b = (y0 * sw + x1) * 4;
                var c = (y1 * sw + x0) * 4; var d = (y1 * sw + x1) * 4; var at = (y * dw + x) * 4;
                double Blend(double aa, double bb, double cc, double dd) => (aa * (1 - fx) + bb * fx) * (1 - fy) + (cc * (1 - fx) + dd * fx) * fy;
                var alpha = Blend(source[a + 3], source[b + 3], source[c + 3], source[d + 3]);
                result[at + 3] = (byte)Math.Clamp((int)Math.Round(alpha), 0, 255);
                for (var k = 0; k < 3; k++) result[at + k] = alpha < .0001 ? (byte)0 : (byte)Math.Clamp((int)Math.Round(Blend(source[a + k] * source[a + 3], source[b + k] * source[b + 3], source[c + k] * source[c + 3], source[d + k] * source[d + 3]) / alpha), 0, 255);
            }
        }
        return result;
    }

    private static void Palette(ushort c0, ushort c1, Span<byte> colors, bool transparent)
    {
        void Color(ushort c, Span<byte> target)
        { target[0] = (byte)(((c >> 11) * 255 + 15) / 31); target[1] = (byte)((((c >> 5) & 63) * 255 + 31) / 63); target[2] = (byte)(((c & 31) * 255 + 15) / 31); target[3] = 255; }
        Color(c0, colors); Color(c1, colors[4..]);
        for (var k = 0; k < 3; k++)
        {
            colors[8 + k] = (byte)(transparent ? (colors[k] + colors[4 + k]) / 2 : (2 * colors[k] + colors[4 + k]) / 3);
            colors[12 + k] = transparent ? (byte)0 : (byte)((colors[k] + 2 * colors[4 + k]) / 3);
        }
        colors[11] = 255; colors[15] = transparent ? (byte)0 : (byte)255;
    }

    private static void AlphaPalette(byte a, byte b, Span<byte> palette)
    {
        palette[0] = a; palette[1] = b;
        for (var i = 2; i < 8; i++) palette[i] = a > b ? (byte)(((8 - i) * a + (i - 1) * b) / 7)
            : i == 6 ? (byte)0 : i == 7 ? (byte)255 : (byte)(((6 - i) * a + (i - 1) * b) / 5);
    }

    private static void EncodeBlock(byte[] pixels, int w, int h, int bx, int by, int kind, Span<byte> output)
    {
        Span<byte> block = stackalloc byte[64]; Span<byte> colors = stackalloc byte[16]; Span<byte> alphas = stackalloc byte[8];
        var low = int.MaxValue; var high = int.MinValue; var lo = 0; var hi = 0; byte minA = 255, maxA = 0;
        for (var p = 0; p < 16; p++)
        {
            var src = (Math.Min(h - 1, by + p / 4) * w + Math.Min(w - 1, bx + p % 4)) * 4;
            pixels.AsSpan(src, 4).CopyTo(block.Slice(p * 4, 4));
            var luma = pixels[src] * 3 + pixels[src + 1] * 6 + pixels[src + 2];
            if (luma < low) { low = luma; lo = p * 4; } if (luma > high) { high = luma; hi = p * 4; }
            minA = Math.Min(minA, pixels[src + 3]); maxA = Math.Max(maxA, pixels[src + 3]);
        }
        static ushort Pack(ReadOnlySpan<byte> block, int at) => (ushort)(((block[at] * 31 + 127) / 255 << 11) | ((block[at + 1] * 63 + 127) / 255 << 5) | (block[at + 2] * 31 + 127) / 255);
        var c0 = Pack(block, hi); var c1 = Pack(block, lo);
        if (c0 < c1) (c0, c1) = (c1, c0);
        if (c0 == c1) { if (c0 < ushort.MaxValue) c0++; else c1--; }
        Palette(c0, c1, colors, false); uint selectors = 0;
        for (var p = 0; p < 16; p++)
        {
            var best = 0; var distance = int.MaxValue;
            for (var i = 0; i < 4; i++)
            {
                var error = 0; for (var k = 0; k < 3; k++) { var delta = block[p * 4 + k] - colors[i * 4 + k]; error += delta * delta; }
                if (error < distance) { distance = error; best = i; }
            }
            selectors |= (uint)best << (p * 2);
        }
        var color = kind == 1 ? output : output[8..];
        BinaryPrimitives.WriteUInt16LittleEndian(color, c0); BinaryPrimitives.WriteUInt16LittleEndian(color[2..], c1); BinaryPrimitives.WriteUInt32LittleEndian(color[4..], selectors);
        if (kind == 2)
        {
            ulong bits = 0; for (var p = 0; p < 16; p++) bits |= (ulong)((block[p * 4 + 3] * 15 + 127) / 255) << (p * 4);
            BinaryPrimitives.WriteUInt64LittleEndian(output, bits);
        }
        else if (kind == 3)
        {
            output[0] = maxA; output[1] = minA; AlphaPalette(maxA, minA, alphas); ulong bits = 0;
            for (var p = 0; p < 16; p++)
            {
                var best = 0; var distance = 256;
                for (var i = 0; i < 8; i++) { var error = Math.Abs(block[p * 4 + 3] - alphas[i]); if (error < distance) { distance = error; best = i; } }
                bits |= (ulong)best << (p * 3);
            }
            for (var i = 0; i < 6; i++) output[i + 2] = (byte)(bits >> (i * 8));
        }
    }
}
