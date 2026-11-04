using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GamePanel.Core;

internal static class OnlineCoverChecks
{
    public static void Register(Action<string, Action> check)
    {
        check("Cover online: ricerca per titolo, fonte e immagine HTTPS consentite", () =>
        {
            using var handler = new Handler(_ => Json("{\"query\":{\"pages\":[{\"pageid\":42,\"title\":\"Test Game\",\"thumbnail\":{\"source\":\"https://upload.wikimedia.org/test.png\"}}]}}"));
            using var source = new OnlineCoverSource(handler); var found = source.SearchAsync("Test Game").GetAwaiter().GetResult();
            Require(found.Count == 1 && found[0].Article.AbsoluteUri == "https://en.wikipedia.org/?curid=42", "Fonte non canonica.");
            Require(handler.Requests.Single().Host == "en.wikipedia.org" && handler.Requests[0].Query.Contains("Test%20Game", StringComparison.Ordinal), "Query non limitata al titolo.");
        });
        check("Cover online: URL privati, credenziali, porte e strutture JSON estranee ignorati", () =>
        {
            foreach (var uri in new[] { "http://upload.wikimedia.org/test.png", "https://127.0.0.1/test.png", "https://upload.wikimedia.org.evil.invalid/test.png", "https://user@upload.wikimedia.org/test.png", "https://upload.wikimedia.org:8443/test.png" })
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new { query = new { pages = new[] { new { pageid = 42, title = "Game", thumbnail = new { source = uri } } } } });
                using var handler = new Handler(_ => Json(json)); using var source = new OnlineCoverSource(handler);
                Require(source.SearchAsync("Test Game").GetAwaiter().GetResult().Count == 0, "URL estraneo accettato.");
                Refused(() => source.DownloadAsync(new(42, "Game", new Uri(uri), new Uri("https://en.wikipedia.org/?curid=42"))).GetAwaiter().GetResult());
                Require(handler.Requests.Count == 1, "Download estraneo ha effettuato una richiesta.");
            }
            foreach (var json in new[] { "[]", "{\"query\":[]}", "{\"query\":{\"pages\":[null,42,{}]}}" })
            {
                using var handler = new Handler(_ => Json(json)); using var source = new OnlineCoverSource(handler);
                Require(source.SearchAsync("Test Game").GetAwaiter().GetResult().Count == 0, "JSON senza cover trattato come valido.");
            }
        });
        check("Cover online: MIME, firma, dimensione e redirect controllati prima della decodifica", () =>
        {
            var candidate = new OnlineCover(42, "Game", new Uri("https://upload.wikimedia.org/test.png"), new Uri("https://en.wikipedia.org/?curid=42"));
            foreach (var kind in new[] { "png", "wrong-mime", "wrong-signature", "too-big", "redirect" })
            {
                using var handler = new Handler(_ =>
                {
                    var response = new HttpResponseMessage(kind == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK);
                    response.Content = new ByteArrayContent(kind == "wrong-signature" ? Encoding.UTF8.GetBytes("not an image") : new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue(kind == "wrong-mime" ? "text/html" : "image/png");
                    if (kind == "too-big") response.Content.Headers.ContentLength = 20 * 1024 * 1024;
                    response.Headers.Location = new Uri("https://private.invalid/image.png"); return response;
                });
                using var source = new OnlineCoverSource(handler);
                if (kind == "png") Require(source.DownloadAsync(candidate).GetAwaiter().GetResult().Extension == ".png", "PNG non restituito.");
                else Refused(() => source.DownloadAsync(candidate).GetAwaiter().GetResult());
                Require(handler.Requests.Count == 1, "Redirect seguito o richiesta duplicata.");
            }
        });
        check("Cover online: percorsi locali, titoli invalidi e annullamento non inviano dati", () =>
        {
            using var handler = new Handler(_ => Json("{}")); using var source = new OnlineCoverSource(handler);
            foreach (var query in new[] { "", "X", "C:\\Games\\Speed.exe", "/home/user/game", "Game\nsecret", new string('a', 141) }) Refused(() => source.SearchAsync(query).GetAwaiter().GetResult());
            Require(handler.Requests.Count == 0, "Query locale inviata.");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { source.SearchAsync("Test Game", cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Annullamento ignorato."); }
            catch (OperationCanceledException) { }
        });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!); return Task.FromResult(response(request)); }
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static void Require(bool value, string text) { if (!value) throw new Exception(text); }
    private static void Refused(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or HttpRequestException) { return; } throw new Exception("Input non valido accettato."); }
}
