using System.Text.Json;

namespace GamePanel.Core;

public sealed record OnlineCover(long PageId, string Title, Uri Image, Uri Article);
public sealed record CoverDownload(byte[] Bytes, string Extension);

/// <summary>Optional, explicit title search. Never sends executable data or local paths.</summary>
public sealed class OnlineCoverSource : IDisposable
{
    private readonly HttpClient _http;
    public OnlineCoverSource() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    internal OnlineCoverSource(HttpMessageHandler handler)
    {
        _http = new(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GamePanel/0.1 (https://github.com/luigip11/GamePanel)");
    }
    public async Task<IReadOnlyList<OnlineCover>> SearchAsync(string query, CancellationToken token = default)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 140 || query.Any(char.IsControl) || query.Contains('\\') || query.Contains('/'))
            throw new ArgumentException("Inserisci il titolo del gioco, senza percorsi, tra 2 e 140 caratteri.");
        var url = "https://en.wikipedia.org/w/api.php?action=query&generator=search&gsrnamespace=0&gsrlimit=6&prop=pageimages&piprop=thumbnail&pilicense=any&pithumbsize=600&format=json&formatversion=2&gsrsearch=" + Uri.EscapeDataString(query + " video game");
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBounded(response.Content, 512 * 1024, token);
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 20 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("query", out var result) || result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array) return [];
        var covers = new List<OnlineCover>();
        foreach (var page in pages.EnumerateArray().Take(6))
        {
            if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("pageid", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number < 1
                || !page.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
                || !page.TryGetProperty("thumbnail", out var thumb) || thumb.ValueKind != JsonValueKind.Object || !thumb.TryGetProperty("source", out var source)
                || source.ValueKind != JsonValueKind.String || !Uri.TryCreate(source.GetString(), UriKind.Absolute, out var image)) continue;
            var caption = title.GetString()!;
            if (caption.Length > 240 || caption.Any(char.IsControl) || !AllowedImage(image)) continue;
            covers.Add(new(number, caption, image, new Uri("https://en.wikipedia.org/?curid=" + number.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        return covers.AsReadOnly();
    }
    public async Task<CoverDownload> DownloadAsync(OnlineCover cover, CancellationToken token = default)
    {
        if (!AllowedImage(cover.Image)) throw new InvalidDataException("Origine immagine non supportata.");
        using var response = await _http.GetAsync(cover.Image, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var media = response.Content.Headers.ContentType?.MediaType;
        if (media is not ("image/png" or "image/jpeg")) throw new InvalidDataException("La fonte non offre una cover PNG o JPEG.");
        var bytes = await ReadBounded(response.Content, 12 * 1024 * 1024, token);
        var png = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if (media == "image/png" && !png || media == "image/jpeg" && !jpeg) throw new InvalidDataException("Contenuto immagine diverso dal formato dichiarato.");
        return new(bytes, png ? ".png" : ".jpg");
    }
    private static bool AllowedImage(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.IdnHost.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase);
    private static async Task<byte[]> ReadBounded(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Risposta della fonte troppo grande.");
        using var input = await content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[16384];
        while (true)
        {
            var count = await input.ReadAsync(buffer, token); if (count == 0) break;
            if (output.Length + count > limit) throw new InvalidDataException("Risposta della fonte troppo grande.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public void Dispose() => _http.Dispose();
}
