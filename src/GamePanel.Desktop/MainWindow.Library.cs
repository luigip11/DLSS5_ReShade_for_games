using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly GameLibrary _libraryStore = new(GameLibrary.DefaultFolder);
    private LibrarySnapshot _library = new(1, null, []);
    private string _libraryMessage = "I giochi importati restano salvati su questo account Windows.";
    private string _libraryQuery = "";
    private string? _relinkId;
    private WrapPanel? _libraryCards;
    private void LoadLibrary()
    {
        try { _library = _libraryStore.Load(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { _libraryMessage = "Libreria non leggibile. Il file esistente è conservato: " + e.Message; }
    }
    private void RememberGame(GameReport report)
    {
        try
        {
            _library = _libraryStore.Remember(report, _relinkId);
            _libraryMessage = "Gioco salvato. Lo ritrovi qui anche dopo aver chiuso GamePanel.";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { _libraryMessage = "Analisi riuscita, ma la libreria non è stata aggiornata: " + e.Message; }
    }
    private void RenderLibrary()
    {
        var hero = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        hero.ColumnDefinitions.Add(new ColumnDefinition()); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var intro = new StackPanel(); intro.Children.Add(Text("La tua libreria.", 32, Ink, FontWeights.Bold));
        var subtitle = Text($"{_library.Games.Count} giochi · pronti da ritrovare, senza reinserire il percorso.", 13, Muted); subtitle.Margin = new Thickness(0, 6, 12, 0); intro.Children.Add(subtitle); hero.Children.Add(intro);
        var add = Button("+  Importa gioco", async (_, _) => await SelectAsync()); add.IsEnabled = !_busy;
        add.Background = Accent; add.Foreground = BackgroundBrush; add.Margin = new Thickness(0); add.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetAutomationId(add, "ImportLibraryGame"); Grid.SetColumn(add, 1); hero.Children.Add(add); _content.Children.Add(hero);
        var message = Text(_libraryMessage, 12, Muted); message.Margin = new Thickness(0, 0, 0, 18); _content.Children.Add(message);
        System.Windows.Automation.AutomationProperties.SetAutomationId(message, "LibraryStatus");
        var label = Text("CERCA NELLA LIBRERIA", 10, Muted, FontWeights.SemiBold); label.Margin = new Thickness(0, 0, 0, 8); _content.Children.Add(label);
        var search = new TextBox { Text = _libraryQuery, FontSize = 14, Foreground = Ink, CaretBrush = Accent, Background = Surface,
            BorderBrush = DesignSystem.Line, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 22), IsEnabled = !_busy };
        System.Windows.Automation.AutomationProperties.SetAutomationId(search, "LibrarySearch");
        System.Windows.Automation.AutomationProperties.SetName(search, "Cerca giochi per nome o percorso");
        search.TextChanged += (_, _) => { _libraryQuery = search.Text; RenderLibraryCards(); }; _content.Children.Add(search);
        _libraryCards = new WrapPanel { Orientation = Orientation.Horizontal };
        _content.Children.Add(_libraryCards); RenderLibraryCards();
        if (_library.Games.Count == 0)
        {
            Card("Il prossimo gioco parte da qui", "Importa un EXE con il pulsante in alto. L'analisi riuscita lo salva automaticamente; l'importazione non avvia il gioco e non installa mod.");
            Details("Percorso manuale e giochi spostati", "Per incollare un percorso apri Panoramica. Se un gioco viene spostato, usa Ricollega EXE dalla sua scheda nella libreria. Backup e profili restano legati ai percorsi originari e non sono spostati automaticamente.");
        }
    }
    private void RenderLibraryCards()
    {
        if (_libraryCards is null) return; _libraryCards.Children.Clear();
        var games = _library.Games.Where(g => string.IsNullOrWhiteSpace(_libraryQuery)
            || g.Title.Contains(_libraryQuery.Trim(), StringComparison.OrdinalIgnoreCase)
            || g.ExecutablePath.Contains(_libraryQuery.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.LastPlayedAt ?? g.LastInspectedAt).ThenBy(g => g.Title).ToArray();
        foreach (var game in games)
        {
            var available = File.Exists(game.ExecutablePath);
            var body = new StackPanel();
            var art = new Grid { Height = 100, ClipToBounds = true };
            art.Children.Add(DesignSystem.HeroArtwork());
            var badge = DesignSystem.Chip(available ? "GIOCO IMPORTATO" : "PERCORSO DA VERIFICARE", available ? Accent : DesignSystem.Amber);
            badge.Margin = new Thickness(16); badge.VerticalAlignment = VerticalAlignment.Top; art.Children.Add(badge); body.Children.Add(art);
            var details = new StackPanel { Margin = new Thickness(16) };
            var title = Text(game.Title, 20, Ink, FontWeights.SemiBold); title.MaxHeight = 58; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = game.Title; details.Children.Add(title);
            var api = string.Join(" · ", game.GraphicsApis.Take(2));
            var facts = Text(game.Architecture + (api.Length == 0 ? "" : " · " + api), 11, Muted); facts.Margin = new Thickness(0, 9, 0, 9); details.Children.Add(facts);
            var path = Text(game.ExecutablePath, 11, Muted); path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis; path.ToolTip = game.ExecutablePath; details.Children.Add(path);
            var recent = Text(game.LastPlayedAt is null ? "Mai avviato da GamePanel" : "Ultimo avvio · " + game.LastPlayedAt.Value.LocalDateTime.ToString("dd MMM · HH:mm"), 11, Muted);
            recent.Margin = new Thickness(0, 8, 0, 14); details.Children.Add(recent);
            var actions = new WrapPanel();
            var play = Button("Avvia  →", async (_, _) => await OpenLibraryGameAsync(game, true)); play.IsEnabled = !_busy && available;
            play.Background = Accent; play.Foreground = BackgroundBrush; System.Windows.Automation.AutomationProperties.SetAutomationId(play, "LibraryPlay-" + game.Id); actions.Children.Add(play);
            var open = Button("Apri scheda", async (_, _) => await OpenLibraryGameAsync(game, false)); open.IsEnabled = !_busy;
            System.Windows.Automation.AutomationProperties.SetAutomationId(open, "LibraryOpen-" + game.Id); actions.Children.Add(open); details.Children.Add(actions);
            var edit = new WrapPanel();
            var rename = Button("Rinomina", (_, _) => RenameLibraryGame(game)); rename.IsEnabled = !_busy;
            var relink = Button("Ricollega EXE", async (_, _) => await RelinkLibraryGameAsync(game)); relink.IsEnabled = !_busy;
            var remove = Button("Rimuovi dalla libreria", (_, _) => EditLibrary(() => _libraryStore.Remove(game.Id), "Voce rimossa. I file del gioco, i backup e i profili sono conservati.")); remove.IsEnabled = !_busy;
            foreach (var button in new[] { rename, relink, remove }) { button.FontSize = 11; button.Padding = new Thickness(8, 7, 8, 7); button.Background = Brushes.Transparent; edit.Children.Add(button); }
            details.Children.Add(edit); body.Children.Add(details);
            var card = new Border { Width = 313, Background = Surface, BorderBrush = game.Id == _library.SelectedId ? DesignSystem.Color("#526B3B") : DesignSystem.Line,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 18, 18), Child = body, ClipToBounds = true };
            System.Windows.Automation.AutomationProperties.SetAutomationId(card, "LibraryCard-" + game.Id); _libraryCards.Children.Add(card);
        }
        if (games.Length == 0 && _library.Games.Count > 0) _libraryCards.Children.Add(Text("Nessun gioco corrisponde alla ricerca.", 14, Muted));
    }
    private async Task OpenLibraryGameAsync(LibraryGame game, bool launch)
    {
        if (_busy) return;
        await InspectAsync(game.ExecutablePath);
        if (launch && _report is not null && string.Equals(_report.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase)) Launch();
    }
    private async Task RelinkLibraryGameAsync(LibraryGame game)
    {
        var picker = new OpenFileDialog { Title = "Ricollega " + game.Title + " al suo EXE", Filter = "Eseguibili Windows (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        _relinkId = game.Id;
        try { await InspectAsync(picker.FileName); }
        finally { _relinkId = null; }
    }
    private void RenameLibraryGame(LibraryGame game)
    {
        var input = new TextBox { Text = game.Title, MaxLength = 160, Padding = new Thickness(10), Foreground = Ink, Background = Surface, BorderBrush = DesignSystem.Line, Margin = new Thickness(0, 12, 0, 18) };
        var body = new StackPanel { Margin = new Thickness(22) }; body.Children.Add(Text("Nome nella libreria", 20, Ink, FontWeights.SemiBold)); body.Children.Add(input);
        var window = new Window { Owner = this, Title = "Rinomina gioco", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            Background = BackgroundBrush, Foreground = Ink, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body };
        window.Resources.Add(typeof(Button), DesignSystem.ButtonStyle());
        var actions = new WrapPanel(); var save = Button("Salva", (_, _) =>
        {
            try { _library = _libraryStore.Rename(game.Id, input.Text); window.DialogResult = true; }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { MessageBox.Show(window, e.Message, "Nome non salvato", MessageBoxButton.OK, MessageBoxImage.Information); }
        }); save.IsDefault = true; actions.Children.Add(save);
        var cancel = Button("Annulla", (_, _) => window.DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel); body.Children.Add(actions);
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        if (window.ShowDialog() == true) { _libraryMessage = "Nome aggiornato."; Render(); }
    }
    private void EditLibrary(Func<LibrarySnapshot> change, string message)
    {
        if (_busy) return;
        try { _library = change(); _libraryMessage = message; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { _libraryMessage = e.Message; }
        Render();
    }
}
