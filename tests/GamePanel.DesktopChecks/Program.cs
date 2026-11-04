using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

if (args.Length == 2 && args[0] == "--explorer-probe") return ExplorerLaunchChecks.Run(args[1]);

// Independent golden DDS samples exercise the actual WPF preview shipped by GamePanel.
// No game, ASI, downloaded executable, or native image codec is loaded.
var root = Path.Combine(Path.GetTempPath(), "GamePanel-preview-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var desktop = Assembly.Load("GamePanel");
    var window = desktop.GetType("GamePanel.Desktop.MainWindow", throwOnError: true)!;
    var read = window.GetMethod("ReadDirectXPreview", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Missing production preview entrypoint.");
    var count = 0;
    void Check(string name, byte[] dds, int width, int height, params (int X, int Y, byte R, byte G, byte B, byte A)[] samples)
    {
        var path = Path.Combine(root, name + ".dds"); File.WriteAllBytes(path, dds);
        var image = (BitmapSource)(read.Invoke(null, [path]) ?? throw new InvalidOperationException("Missing bitmap."));
        Require(image.IsFrozen, name + ": preview was not frozen for UI transfer.");
        Require(image.PixelWidth == width && image.PixelHeight == height, name + ": unexpected bounded preview size.");
        Require(width <= 256 && height <= 256, name + ": output exceeded the preview budget.");
        var pixels = new byte[checked(width * height * 4)]; image.CopyPixels(pixels, checked(width * 4), 0);
        foreach (var p in samples)
        {
            var at = (p.Y * width + p.X) * 4;
            Require(pixels[at] == p.B && pixels[at + 1] == p.G && pixels[at + 2] == p.R && pixels[at + 3] == p.A,
                $"{name}: wrong golden RGBA at {p.X},{p.Y}; got {pixels[at + 2]},{pixels[at + 1]},{pixels[at]},{pixels[at + 3]}.");
        }
        Console.WriteLine("PASS " + name); count++;
    }
    void Refused(string name, byte[] bytes)
    {
        var path = Path.Combine(root, name + ".dds"); File.WriteAllBytes(path, bytes);
        try { read.Invoke(null, [path]); throw new InvalidOperationException(name + ": invalid DDS accepted."); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { }
        Console.WriteLine("PASS " + name); count++;
    }

    var red = ColorBlock(0xf800, 0, 0);
    Check("BC1-red", Dds(4, 4, "DXT1", red), 4, 4, (0, 0, 255, 0, 0, 255), (3, 3, 255, 0, 0, 255));
    Check("BC1-transparent", Dds(4, 4, "DXT1", ColorBlock(0, 0xf800, uint.MaxValue)), 4, 4, (2, 1, 0, 0, 0, 0));
    Check("BC1-interpolation", Dds(4, 4, "DXT1", ColorBlock(0xf800, 0x001f, 0xaaaaaaaa)), 4, 4, (1, 2, 170, 0, 85, 255));
    var bc2 = new byte[16]; Array.Fill(bc2, (byte)0x88, 0, 8); red.CopyTo(bc2, 8);
    Check("BC2-alpha", Dds(4, 4, "DXT3", bc2), 4, 4, (0, 3, 255, 0, 0, 136));
    Check("BC3-alpha-interpolation", Dds(4, 4, "DXT5", AlphaBlock(200, 100, 2, ColorBlock(0x001f, 0, 0))), 4, 4, (3, 0, 0, 0, 255, 185));
    Check("BC3-alpha-zero", Dds(4, 4, "DXT5", AlphaBlock(10, 20, 6, ColorBlock(0x07e0, 0, 0))), 4, 4, (2, 2, 0, 255, 0, 0));
    Check("BGRA8", Dds(2, 1, "BGRA8", [30, 20, 10, 40, 70, 60, 50, 80]), 2, 1, (0, 0, 10, 20, 30, 40), (1, 0, 50, 60, 70, 80));
    Check("RGBA8", Dds(2, 1, "RGBA8", [10, 20, 30, 40, 50, 60, 70, 80]), 2, 1, (0, 0, 10, 20, 30, 40), (1, 0, 50, 60, 70, 80));
    var opaque = Dds(1, 1, "BGRA8", [30, 20, 10, 0]); U32(opaque, 104, 0);
    Check("BGRX8-opaque", opaque, 1, 1, (0, 0, 10, 20, 30, 255));
    var dx10 = ToDx10(Dds(4, 4, "DXT1", red), 71);
    Check("DX10-BC1", dx10, 4, 4, (1, 1, 255, 0, 0, 255));
    Check("DX10-RGBA8", ToDx10(Dds(1, 1, "RGBA8", [10, 20, 30, 40]), 28), 1, 1, (0, 0, 10, 20, 30, 40));
    var widePixels = new byte[8192 * 4];
    for (var x = 0; x < 8192; x++) { widePixels[x * 4] = (byte)(x & 255); widePixels[x * 4 + 3] = 255; }
    Check("8192-wide-sampling", Dds(8192, 1, "RGBA8", widePixels), 256, 1, (0, 0, 0, 0, 0, 255), (1, 0, 32, 0, 0, 255), (255, 0, 224, 0, 0, 255));
    var largeBlocks = new byte[8192 / 4 * (8192 / 4) * 8];
    for (var at = 0; at < largeBlocks.Length; at += 8) red.CopyTo(largeBlocks, at);
    Check("8192-square-bounded", Dds(8192, 8192, "DXT1", largeBlocks), 256, 256, (0, 0, 255, 0, 0, 255), (255, 255, 255, 0, 0, 255));
    Refused("truncated", Dds(4, 4, "DXT1", red)[..^1]);
    var oversized = Dds(4, 4, "DXT1", red); U32(oversized, 16, 8193); Refused("oversized-header", oversized);
    var wrongDx10 = ToDx10(Dds(4, 4, "DXT1", red), 98); Refused("unsupported-BC7", wrongDx10);
    Console.WriteLine($"PASS {count} independent DDS preview golden checks."); return 0;
}
catch (Exception e) { Console.Error.WriteLine(e); return 1; }
finally { Directory.Delete(root, true); }

static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void U32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at, 4), value);
static byte[] ColorBlock(ushort c0, ushort c1, uint selectors)
{
    var b = new byte[8]; BinaryPrimitives.WriteUInt16LittleEndian(b, c0); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), c1); U32(b, 4, selectors); return b;
}
static byte[] AlphaBlock(byte a0, byte a1, int selector, byte[] color)
{
    var b = new byte[16]; b[0] = a0; b[1] = a1; ulong bits = 0;
    for (var p = 0; p < 16; p++) bits |= (ulong)selector << (p * 3);
    for (var n = 0; n < 6; n++) b[n + 2] = (byte)(bits >> (n * 8)); color.CopyTo(b, 8); return b;
}
static byte[] Dds(int width, int height, string format, byte[] pixels)
{
    var b = new byte[128 + pixels.Length]; U32(b, 0, 0x20534444); U32(b, 4, 124); U32(b, 8, 0x1007);
    U32(b, 12, (uint)height); U32(b, 16, (uint)width); U32(b, 28, 1); U32(b, 76, 32); U32(b, 108, 0x1000);
    if (format.StartsWith("DXT", StringComparison.Ordinal))
    {
        U32(b, 80, 4); U32(b, 84, format switch { "DXT1" => 0x31545844, "DXT3" => 0x33545844, "DXT5" => 0x35545844, _ => throw new ArgumentException(format) });
    }
    else
    {
        U32(b, 80, 0x41); U32(b, 88, 32); U32(b, 92, format == "BGRA8" ? 0x00ff0000u : 0x000000ffu);
        U32(b, 96, 0x0000ff00); U32(b, 100, format == "BGRA8" ? 0x000000ffu : 0x00ff0000u); U32(b, 104, 0xff000000);
    }
    pixels.CopyTo(b, 128); return b;
}
static byte[] ToDx10(byte[] legacy, uint format)
{
    var b = new byte[legacy.Length + 20]; legacy.AsSpan(0, 128).CopyTo(b); legacy.AsSpan(128).CopyTo(b.AsSpan(148));
    U32(b, 80, 4); U32(b, 84, 0x30315844); U32(b, 128, format); U32(b, 132, 3); U32(b, 140, 1); return b;
}
