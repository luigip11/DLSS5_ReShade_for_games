using System.IO;
using System.Windows.Media.Imaging;
using System.Text.Json;
using GamePanel.Core;

namespace GamePanel.Desktop;

/// <summary>Optional local artwork, independent of game files and executable identity.</summary>
internal static class GameCoverStore
{
    private const int MaximumBytes = 12 * 1024 * 1024;
    public static string PathFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Identità della copertina non valida.");
        return Path.Combine(GamePanelPaths.Root, "Library", "Covers", id + ".png");
    }
    private static void Guard(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
        {
            try { if ((File.GetAttributes(part) & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) throw new IOException("Percorso copertina non regolare."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static BitmapImage Decode(string path, int size)
    {
        Guard(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > MaximumBytes) throw new InvalidDataException("Scegli un'immagine PNG o JPEG fino a 12 MB.");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if (decoder.Frames.Count != 1) throw new InvalidDataException("Scegli un'immagine statica.");
        var frame = decoder.Frames[0]; var width = frame.PixelWidth; var height = frame.PixelHeight;
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 32_000_000)
            throw new InvalidDataException("Dimensioni immagine oltre il limite di 8192 pixel e 32 megapixel.");
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height) image.DecodePixelWidth = Math.Min(width, size); else image.DecodePixelHeight = Math.Min(height, size);
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    public static string TemporaryPath(string extension)
    {
        if (extension is not (".png" or ".jpg")) throw new ArgumentException("Formato immagine non supportato.");
        var folder = Path.GetDirectoryName(PathFor(Guid.NewGuid().ToString("N")))!;
        Guard(folder); Directory.CreateDirectory(folder);
        return Path.Combine(folder, "preview-" + Guid.NewGuid().ToString("N") + extension);
    }
    public static void Import(string id, string source)
    {
        if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Formato supportato: PNG o JPEG.");
        var image = Decode(source, 960); var target = PathFor(id); var folder = Path.GetDirectoryName(target)!;
        Guard(folder); Directory.CreateDirectory(folder); Guard(target);
        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(output);
            File.Move(temporary, target, true);
            var sourceInfo = target + ".source.json"; Guard(sourceInfo); if (File.Exists(sourceInfo)) File.Delete(sourceInfo);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static BitmapImage? Read(string id)
    {
        try { var path = PathFor(id); return File.Exists(path) ? Decode(path, 384) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or System.Windows.Markup.XamlParseException or System.IO.FileFormatException or ArgumentException)
        { return null; }
    }
    public static void SetSource(string id, OnlineCover source)
    {
        var path = PathFor(id) + ".source.json"; Guard(path);
        File.WriteAllText(path, JsonSerializer.Serialize(new { source.Title, Url = source.Article.AbsoluteUri }));
    }
    public static Uri? Source(string id)
    {
        try
        {
            var path = PathFor(id) + ".source.json"; Guard(path); if (!File.Exists(path) || new FileInfo(path).Length > 32768) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("Url", out var value) && value.ValueKind == JsonValueKind.String
                && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.Host == "en.wikipedia.org" && uri.UserInfo.Length == 0 ? uri : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    public static void Remove(string id) { var path = PathFor(id); Guard(path); if (File.Exists(path)) File.Delete(path); var metadata = path + ".source.json"; Guard(metadata); if (File.Exists(metadata)) File.Delete(metadata); }
}
