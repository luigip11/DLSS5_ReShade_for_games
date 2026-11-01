using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly SemaphoreSlim _directXPreviewGate = new(1, 1);
    private int _directXPreviewRevision;

    private static Grid CreateDirectXPreviewPair(out Border original, out Border replacement)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        Border Panel(string caption, int column)
        {
            var body = new StackPanel();
            var label = Text(caption, 12, Muted); label.Margin = new Thickness(0, 0, 0, 7); body.Children.Add(label);
            var frame = new Border { Background = DesignSystem.Panel, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), MinHeight = 160, Height = 190, Padding = new Thickness(8),
                Child = Text("Seleziona una texture per l'anteprima.", 12, Muted) };
            body.Children.Add(frame); Grid.SetColumn(body, column); body.Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 0);
            grid.Children.Add(body); return frame;
        }
        original = Panel("Cattura originale", 0); replacement = Panel("DDS sostitutivo · anteprima file", 1); return grid;
    }

    private async Task PopulateDirectXPreviewsAsync(Border originalFrame, Border replacementFrame, string hash, string replacementPath)
    {
        var revision = Interlocked.Increment(ref _directXPreviewRevision);
        var capture = _directXCaptures.FirstOrDefault(c => c.Hash == hash);
        originalFrame.Child = Text(capture is null ? "Scegli una cattura verificata." : "Caricamento anteprima…", 12, Muted);
        replacementFrame.Child = Text(replacementPath.Length == 0 ? "Scegli un DDS o usa l'asfalto GamePanel 2K." : "Caricamento anteprima…", 12, Muted);
        await _directXPreviewGate.WaitAsync();
        try
        {
            if (_closing || revision != Volatile.Read(ref _directXPreviewRevision)) return;
            var previews = await Task.Run(() => (Original: TryDirectXPreview(capture?.FilePath), Replacement: TryDirectXPreview(replacementPath)));
            if (_closing || revision != Volatile.Read(ref _directXPreviewRevision)) return;
            if (capture is not null) SetDirectXPreview(originalFrame, previews.Original);
            if (replacementPath.Length > 0) SetDirectXPreview(replacementFrame, previews.Replacement);
        }
        finally { _directXPreviewGate.Release(); }
    }

    private static (BitmapSource? Image, string? Error) TryDirectXPreview(string? path)
    {
        if (string.IsNullOrEmpty(path)) return (null, null);
        try { return (ReadDirectXPreview(path), null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        { return (null, "Anteprima non disponibile: " + e.Message); }
    }

    private static void SetDirectXPreview(Border frame, (BitmapSource? Image, string? Error) preview)
    {
        if (preview.Image is not null)
        {
            var image = new Image { Source = preview.Image, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); frame.Child = image;
        }
        else frame.Child = Text(preview.Error ?? "Anteprima non disponibile per questo DDS.", 12, Muted);
    }

    // Sample mip 0 into at most 256 x 256 pixels. Only one compressed/uncompressed source row
    // is buffered; an 8192 x 8192 capture never allocates a full decoded image or a native codec.
    private static BitmapSource ReadDirectXPreview(string path)
    {
        var info = DirectXTextureData.ReadDds(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != info.Bytes || !Convert.ToHexString(SHA256.HashData(stream)).Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DDS cambiato durante la lettura dell'anteprima.");
        stream.Position = 0; var header = new byte[128]; stream.ReadExactly(header);
        var dx10 = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84, 4)) == 0x30315844;
        var payload = dx10 ? 148 : 128;
        var bc1 = info.Format.StartsWith("DXT1", StringComparison.Ordinal);
        var bc2 = info.Format.StartsWith("DXT3", StringComparison.Ordinal);
        var bc3 = info.Format.StartsWith("DXT5", StringComparison.Ordinal);
        var compressed = bc1 || bc2 || bc3;
        var bgra = info.Format.StartsWith("BGRA8", StringComparison.Ordinal);
        var rgba = info.Format.StartsWith("RGBA8", StringComparison.Ordinal);
        if (!compressed && !bgra && !rgba) throw new InvalidDataException("Formato non supportato dall'anteprima.");
        var blockBytes = bc1 ? 8 : 16;
        var rowBytes = compressed ? checked(Math.Max(1, (info.Width + 3) / 4) * blockBytes) : checked(info.Width * 4);
        var row = new byte[rowBytes]; var lastSourceRow = -1;
        var scale = Math.Min(1d, 256d / Math.Max(info.Width, info.Height));
        var width = Math.Max(1, (int)Math.Round(info.Width * scale));
        var height = Math.Max(1, (int)Math.Round(info.Height * scale));
        var pixels = new byte[checked(width * height * 4)];
        var forceOpaque = !dx10 && !compressed && BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(104, 4)) == 0;
        for (var y = 0; y < height; y++)
        {
            var sourceY = checked(y * info.Height / height); var sourceRow = compressed ? sourceY / 4 : sourceY;
            if (sourceRow != lastSourceRow)
            {
                stream.Position = checked(payload + (long)sourceRow * rowBytes); stream.ReadExactly(row); lastSourceRow = sourceRow;
            }
            for (var x = 0; x < width; x++)
            {
                var sourceX = checked(x * info.Width / width); var dest = (y * width + x) * 4;
                if (compressed)
                {
                    var block = row.AsSpan(sourceX / 4 * blockBytes, blockBytes);
                    DecodeDirectXBlockPixel(block, sourceX & 3, sourceY & 3, bc1, bc2, pixels.AsSpan(dest, 4));
                }
                else
                {
                    var src = sourceX * 4;
                    pixels[dest] = row[src + (bgra ? 0 : 2)]; pixels[dest + 1] = row[src + 1];
                    pixels[dest + 2] = row[src + (bgra ? 2 : 0)]; pixels[dest + 3] = forceOpaque ? (byte)255 : row[src + 3];
                }
            }
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, checked(width * 4));
        result.Freeze(); return result;
    }

    private static void DecodeDirectXBlockPixel(ReadOnlySpan<byte> block, int x, int y, bool bc1, bool bc2, Span<byte> bgra)
    {
        var color = bc1 ? block : block[8..]; var pixel = y * 4 + x;
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(color); var c1 = BinaryPrimitives.ReadUInt16LittleEndian(color[2..]);
        var selector = (int)((BinaryPrimitives.ReadUInt32LittleEndian(color[4..]) >> (pixel * 2)) & 3);
        byte Channel(ushort c, int shift, int mask) => (byte)((((c >> shift) & mask) * 255 + mask / 2) / mask);
        var r0 = Channel(c0, 11, 31); var g0 = Channel(c0, 5, 63); var b0 = Channel(c0, 0, 31);
        var r1 = Channel(c1, 11, 31); var g1 = Channel(c1, 5, 63); var b1 = Channel(c1, 0, 31);
        byte Mix(byte a, byte b) => selector switch
        {
            0 => a, 1 => b,
            2 when !bc1 || c0 > c1 => (byte)((2 * a + b) / 3),
            3 when !bc1 || c0 > c1 => (byte)((a + 2 * b) / 3),
            2 => (byte)((a + b) / 2), _ => 0
        };
        bgra[0] = Mix(b0, b1); bgra[1] = Mix(g0, g1); bgra[2] = Mix(r0, r1);
        byte alpha = bc1 && c0 <= c1 && selector == 3 ? (byte)0 : (byte)255;
        if (bc2) alpha = (byte)(((BinaryPrimitives.ReadUInt64LittleEndian(block) >> (pixel * 4)) & 15) * 17);
        else if (!bc1)
        {
            ulong alphaBits = 0;
            for (var i = 0; i < 6; i++) alphaBits |= (ulong)block[i + 2] << (i * 8);
            var index = (int)((alphaBits >> (pixel * 3)) & 7); var a0 = block[0]; var a1 = block[1];
            alpha = index switch
            {
                0 => a0, 1 => a1,
                _ when a0 > a1 => (byte)(((8 - index) * a0 + (index - 1) * a1) / 7),
                6 => 0, 7 => 255,
                _ => (byte)(((6 - index) * a0 + (index - 1) * a1) / 5)
            };
        }
        bgra[3] = alpha;
    }
}
