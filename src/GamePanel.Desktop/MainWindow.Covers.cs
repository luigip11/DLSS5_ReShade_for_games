using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly Dictionary<string, (DateTime Stamp, long Bytes, BitmapSource? Image)> _coverImages = new();
    private BitmapSource? CoverImage(string id)
    {
        try
        {
            var path = GameCoverStore.PathFor(id); var file = new FileInfo(path);
            if (!file.Exists) { _coverImages.Remove(id); return null; }
            if (_coverImages.TryGetValue(id, out var entry) && entry.Stamp == file.LastWriteTimeUtc && entry.Bytes == file.Length) return entry.Image;
            var image = GameCoverStore.Read(id); _coverImages[id] = (file.LastWriteTimeUtc, file.Length, image); return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    private void AddLibraryCover(Grid artwork, LibraryGame game)
    {
        var image = CoverImage(game.Id);
        if (image is not null)
        {
            artwork.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill, Opacity = .2 });
            artwork.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(8) });
        }
        var cover = Button("Copertina", (_, _) => { }); cover.Padding = new Thickness(9, 6, 9, 6); cover.FontSize = 10;
        var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(DesignSystem.Icon("image", Accent, 14));
        var label = Text("Copertina", 10, Ink, FontWeights.SemiBold); label.Margin = new Thickness(6, 0, 0, 0); content.Children.Add(label); cover.Content = content;
        System.Windows.Automation.AutomationProperties.SetName(cover, "Copertina del gioco");
        System.Windows.Automation.AutomationProperties.SetAutomationId(cover, "LibraryCover-" + game.Id);
        cover.IsEnabled = !_busy; cover.HorizontalAlignment = HorizontalAlignment.Right; cover.VerticalAlignment = VerticalAlignment.Bottom;
        cover.Margin = new Thickness(10); cover.Background = DesignSystem.Color("#14251F"); cover.ToolTip = "Scegli un’immagine locale oppure cerca online per titolo.";
        var menu = new ContextMenu();
        var online = new MenuItem { Header = "Cerca automaticamente online…" }; online.Click += (_, _) => SearchLibraryCover(game); menu.Items.Add(online);
        var attribution = GameCoverStore.Source(game.Id);
        if (attribution is not null)
        {
            var source = new MenuItem { Header = "Fonte: Wikipedia" };
            source.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(attribution.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { MessageBox.Show(this, e.Message, "Fonte non aperta"); }
            }; menu.Items.Add(source);
        }
        var choose = new MenuItem { Header = "Scegli immagine locale…" };
        choose.Click += (_, _) => ChooseLibraryCover(game); menu.Items.Add(choose);
        var remove = new MenuItem { Header = "Rimuovi copertina", IsEnabled = image is not null };
        remove.Click += (_, _) =>
        {
            try { GameCoverStore.Remove(game.Id); _coverImages.Remove(game.Id); _libraryMessage = "Copertina rimossa. I file del gioco restano conservati."; Render(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { _libraryMessage = e.Message; Render(); }
        };
        menu.Items.Add(remove); cover.ContextMenu = menu;
        cover.Click += (_, _) => { menu.PlacementTarget = cover; menu.IsOpen = true; }; artwork.Children.Add(cover);
    }
    private void ChooseLibraryCover(LibraryGame game)
    {
        var picker = new OpenFileDialog { Title = "Copertina per " + game.Title, Filter = "Immagini (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            GameCoverStore.Import(game.Id, picker.FileName); _coverImages.Remove(game.Id);
            _libraryMessage = "Copertina salvata localmente. La stessa immagine è condivisa da Originale e Remix."; Render();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.IO.FileFormatException)
        { MessageBox.Show(this, e.Message, "Copertina non importata", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
