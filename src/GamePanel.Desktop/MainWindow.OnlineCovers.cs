using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private void SearchLibraryCover(LibraryGame game)
    {
        var title = game.Sha256.Equals(Underground1DisplayFix.ExecutableSha256, StringComparison.OrdinalIgnoreCase) ? "Need for Speed Underground"
            : RemixExecutableCatalog.Contains(game.Sha256) ? "Need for Speed Underground 2" : game.Title;
        var body = new StackPanel { Margin = new Thickness(22) };
        body.Children.Add(Text("Trova la copertina", 24, Ink, FontWeights.SemiBold));
        body.Children.Add(Text("Ricerca su Wikipedia per titolo. Viene inviato soltanto il testo di ricerca; EXE e percorsi locali restano sul PC. La cover scelta verrà salvata in cache per l'uso offline.", 12, Muted));
        var query = new TextBox { Text = title, MaxLength = 140, Padding = new Thickness(10), Margin = new Thickness(0, 16, 0, 10), Foreground = Ink, Background = Surface, BorderBrush = DesignSystem.Line };
        body.Children.Add(query);
        var status = Text("", 12, Muted); status.Margin = new Thickness(0, 8, 0, 12);
        var results = new ListBox { Height = 160, Background = Surface, Foreground = Ink, BorderBrush = DesignSystem.Line, Margin = new Thickness(0, 0, 0, 14) };
        var preview = new Image { Height = 190, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 0, 14) };
        using var source = new OnlineCoverSource(); using var cancellation = new CancellationTokenSource(); var closed = false;
        var window = new Window { Owner = this, Title = "Copertina online", Width = 650, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 50), ResizeMode = ResizeMode.CanResize,
            Background = BackgroundBrush, Foreground = Ink, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.Resources.Add(typeof(Button), DesignSystem.ButtonStyle());
        var search = Button("Cerca", (_, _) => { }); var save = Button("Usa questa copertina", (_, _) => { });
        save.Background = Accent; save.Foreground = BackgroundBrush; save.IsEnabled = false;
        var cancel = Button("Chiudi", (_, _) => window.Close()); cancel.IsCancel = true;
        body.Children.Add(search); body.Children.Add(status); body.Children.Add(results); body.Children.Add(preview);
        var actions = new WrapPanel(); actions.Children.Add(save); actions.Children.Add(cancel); body.Children.Add(actions);
        OnlineCover? downloaded = null; string? temporary = null; var loading = false;
        search.Click += async (_, _) => await Search(); save.Click += async (_, _) => await Apply();
        bool Expected(Exception e) => e is HttpRequestException or IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.IO.FileFormatException or ObjectDisposedException;
        async Task Search()
        {
            if (loading || closed) return; loading = true; search.IsEnabled = false; save.IsEnabled = false; results.IsEnabled = false;
            preview.Source = null; downloaded = null; status.Text = "Ricerca in corso…";
            try
            {
                var found = await source.SearchAsync(query.Text, cancellation.Token); if (closed) return;
                results.Items.Clear(); foreach (var item in found) results.Items.Add(new ListBoxItem { Content = item.Title, Tag = item, Padding = new Thickness(10) });
                status.Text = found.Count == 0 ? "Nessuna immagine trovata. Prova il titolo originale del gioco oppure importa una cover locale." : "Scegli il gioco corretto per vedere la cover. Alcuni risultati possono essere loghi o immagini della voce.";
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "Ricerca interrotta o scaduta. Puoi usare un'immagine locale."; }
            catch (Exception e) when (Expected(e)) { if (!closed) status.Text = "Ricerca non disponibile: " + e.Message; }
            finally { loading = false; if (!closed) { search.IsEnabled = true; results.IsEnabled = true; } }
        }
        results.SelectionChanged += async (_, _) =>
        {
            if (loading || closed || results.SelectedItem is not ListBoxItem { Tag: OnlineCover item }) return;
            loading = true; save.IsEnabled = false; search.IsEnabled = false; results.IsEnabled = false; downloaded = null; preview.Source = null;
            status.Text = "Scaricamento anteprima…";
            try
            {
                var image = await source.DownloadAsync(item, cancellation.Token); if (closed) return;
                if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
                temporary = GameCoverStore.TemporaryPath(image.Extension);
                await File.WriteAllBytesAsync(temporary, image.Bytes, cancellation.Token); if (closed) return;
                // Reuse the bounded image importer/decoder without changing this game's current cover.
                var previewId = Guid.NewGuid().ToString("N");
                try { GameCoverStore.Import(previewId, temporary); preview.Source = GameCoverStore.Read(previewId); }
                finally { GameCoverStore.Remove(previewId); }
                downloaded = item; save.IsEnabled = preview.Source is not null;
                status.Text = "Fonte: Wikipedia · " + item.Title + ". Verifica l'immagine e consulta la pagina originale per le condizioni d'uso.";
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "Anteprima interrotta o scaduta."; }
            catch (Exception e) when (Expected(e)) { if (!closed) status.Text = "Immagine non disponibile: " + e.Message; }
            finally
            {
                loading = false;
                if (!closed) { search.IsEnabled = true; results.IsEnabled = true; }
                else if (temporary is not null) { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            }
        };
        Task Apply()
        {
            if (downloaded is null || temporary is null || loading) return Task.CompletedTask;
            try
            {
                GameCoverStore.Import(game.Id, temporary); GameCoverStore.SetSource(game.Id, downloaded); _coverImages.Remove(game.Id);
                _libraryMessage = "Copertina salvata in cache. Fonte e immagine sono disponibili dal menu Copertina."; window.DialogResult = true;
            }
            catch (Exception e) when (Expected(e)) { status.Text = "Copertina non salvata: " + e.Message; }
            return Task.CompletedTask;
        }
        window.Closed += (_, _) => { closed = true; cancellation.Cancel(); if (temporary is not null && File.Exists(temporary)) { try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } } };
        window.Loaded += async (_, _) => await Search();
        DesignSystem.SpaceActions(body);
        if (window.ShowDialog() == true) Render();
    }
}
