using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class CoverChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "GamePanel-cover-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GAMEPANEL_DATA_ROOT"); Environment.SetEnvironmentVariable("GAMEPANEL_DATA_ROOT", Path.Combine(root, "Data"));
        try
        {
            var store = Assembly.Load("GamePanel").GetType("GamePanel.Desktop.GameCoverStore", throwOnError: true)!;
            object? Invoke(string name, params object[] args) => store.GetMethod(name, BindingFlags.Static | BindingFlags.Public)!.Invoke(null, args);
            var id = Guid.NewGuid().ToString("N"); var path = Path.Combine(root, "cover.png");
            var pixels = new byte[1600 * 900 * 4]; for (var at = 0; at < pixels.Length; at += 4) { pixels[at + 1] = 210; pixels[at + 3] = 255; }
            var source = BitmapSource.Create(1600, 900, 96, 96, PixelFormats.Bgra32, null, pixels, 1600 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using (var file = File.Create(path)) encoder.Save(file);
            var original = File.ReadAllBytes(path); Invoke("Import", id, path);
            if (!original.SequenceEqual(File.ReadAllBytes(path))) throw new Exception("Artwork importer modified the user's original file.");
            File.Delete(path); var read = (BitmapSource?)Invoke("Read", id);
            if (read is null || read.PixelWidth > 384 || read.PixelHeight > 384) throw new Exception("Stored cover is missing or not bounded.");
            Invoke("Remove", id); if (Invoke("Read", id) is not null) throw new Exception("Cover removal did not restore the placeholder.");
            try { Invoke("PathFor", "../outside"); throw new Exception("Cover traversal accepted."); } catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { }
            var oversized = Path.Combine(root, "large.png"); using (var file = File.Create(oversized)) file.SetLength(13 * 1024 * 1024);
            try { Invoke("Import", id, oversized); throw new Exception("Oversized artwork accepted."); } catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { }
            Console.WriteLine("PASS local cover import: owned cache, bounded decoding, independent source, traversal/size rejection and removal.");
        }
        finally { Environment.SetEnvironmentVariable("GAMEPANEL_DATA_ROOT", old); Directory.Delete(root, true); }
    }
}
