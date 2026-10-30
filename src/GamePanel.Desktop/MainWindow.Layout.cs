using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly TextBlock _sectionName = DesignSystem.Label("PANORAMICA", 11, Muted, true);
    private readonly TextBlock _gameContext = DesignSystem.Label("Nessun gioco selezionato", 13, Ink, true);
    private readonly TextBlock _inputHint = DesignSystem.Label("Incolla il percorso del gioco oppure scegli il suo EXE", 13, Muted);
    private static readonly string[] PageTitles = ["Panoramica", "DLSS", "HDR e immagine", "Backup", "Diagnosi"];
    private static readonly string[] PageIcons = ["game", "chip", "sun", "shield", "scan"];

    private void BuildShell()
    {
        Title = "GamePanel · 0.1.0-alpha.4";
        Width = Math.Min(1360, Math.Max(1000, SystemParameters.WorkArea.Width - 64));
        Height = Math.Min(900, Math.Max(680, SystemParameters.WorkArea.Height - 64));
        MinWidth = 1000; MinHeight = 680;
        Background = BackgroundBrush; Foreground = Ink;
        Icon = DesignSystem.AppIcon();
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; AllowDrop = true;
        Resources.Add(typeof(Button), DesignSystem.ButtonStyle());
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), DesignSystem.ScrollBarStyle());
        Resources.Add(typeof(ComboBox), DesignSystem.ComboBoxStyle());
        SourceInitialized += (_, _) => ApplyDarkTitlebar();

        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        shell.ColumnDefinitions.Add(new ColumnDefinition()); Content = shell;
        var sidebar = new DockPanel { Background = DesignSystem.Sidebar, LastChildFill = true };
        shell.Children.Add(sidebar);
        var bottom = new StackPanel { Margin = new Thickness(20, 12, 20, 22) };
        bottom.Children.Add(DesignSystem.Chip("ALPHA 04", Accent));
        var local = Text("Sul tuo PC. Sotto controllo.", 11, Muted); local.Margin = new Thickness(0, 12, 0, 0); bottom.Children.Add(local);
        DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom);
        var menu = new StackPanel { Margin = new Thickness(14, 28, 14, 0) };
        var brand = new StackPanel { Margin = new Thickness(10, 0, 0, 32) };
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal };
        brandRow.Children.Add(new Border { Width = 5, Height = 26, Background = Accent, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) });
        brandRow.Children.Add(new TextBlock { Text = "GAMEPANEL", FontSize = 24, Foreground = Ink, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Bahnschrift SemiCondensed") });
        brand.Children.Add(brandRow);
        brand.Children.Add(Text("IL TUO SETUP, UN SOLO POSTO", 9, Muted)); menu.Children.Add(brand);
        var libraryLabel = Text("WORKSPACE", 10, Muted, FontWeights.SemiBold); libraryLabel.Margin = new Thickness(12, 0, 0, 12); menu.Children.Add(libraryLabel);
        for (var i = 0; i < PageTitles.Length; i++)
        {
            var page = i;
            var nav = Button("", (_, _) => { _page = page; Render(); });
            nav.Content = NavigationContent(i, i == 0);
            nav.Margin = new Thickness(0, 0, 0, 7); nav.Padding = new Thickness(12, 14, 12, 14);
            nav.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (i == 3) System.Windows.Automation.AutomationProperties.SetAutomationId(nav, "BackupsPage");
            if (i == 0) System.Windows.Automation.AutomationProperties.SetAutomationId(nav, "OverviewPage");
            if (i == 1) System.Windows.Automation.AutomationProperties.SetAutomationId(nav, "DlssPage");
            if (i == 2) System.Windows.Automation.AutomationProperties.SetAutomationId(nav, "HdrPage");
            _navigation.Add(nav); menu.Children.Add(nav);
        }
        menu.Children.Add(new Border { Height = 1, Background = DesignSystem.Line, Margin = new Thickness(12, 22, 12, 20) });
        var note = Text("Analizza il gioco, verifica i file e crea una copia prima di intervenire.", 12, Muted); note.Margin = new Thickness(12, 0, 12, 0); note.LineHeight = 20; menu.Children.Add(note);
        sidebar.Children.Add(menu);

        var main = new DockPanel(); Grid.SetColumn(main, 1); shell.Children.Add(main);
        var footerGrid = new Grid(); footerGrid.ColumnDefinitions.Add(new ColumnDefinition()); footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { Background = Accent, CornerRadius = new CornerRadius(3), Width = 6, Height = 6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Left); statusRow.Children.Add(dot);
        _status.FontSize = 11; _status.TextWrapping = TextWrapping.NoWrap; _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = _status });
        statusRow.Children.Add(_status); footerGrid.Children.Add(statusRow);
        var version = Text("GAMEPANEL  /  0.1.0-alpha.4", 10, Muted); Grid.SetColumn(version, 1); footerGrid.Children.Add(version);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_status, "OperationStatus");
        var footer = new Border { BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(30, 14, 30, 14), Child = footerGrid };
        DockPanel.SetDock(footer, Dock.Bottom); main.Children.Add(footer);

        var header = new StackPanel { Margin = new Thickness(30, 24, 30, 0) };
        var context = new Grid(); context.ColumnDefinitions.Add(new ColumnDefinition()); context.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        context.Children.Add(_sectionName);
        var gameLabel = new StackPanel { Orientation = Orientation.Horizontal };
        _gameContext.MaxWidth = 280; _gameContext.TextWrapping = TextWrapping.NoWrap; _gameContext.TextTrimming = TextTrimming.CharacterEllipsis;
        _gameContext.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = _gameContext });
        gameLabel.Children.Add(DesignSystem.Icon("game", DesignSystem.Blue, 17)); _gameContext.Margin = new Thickness(9, 0, 0, 0); gameLabel.Children.Add(_gameContext);
        Grid.SetColumn(gameLabel, 1); context.Children.Add(gameLabel); header.Children.Add(context);
        var pathLabel = new Label { Content = "ESEGUIBILE DEL GIOCO", Target = _path, Foreground = Muted, FontSize = 10, FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), Margin = new Thickness(0, 22, 0, 8) };
        header.Children.Add(pathLabel);
        var pathRow = new Grid();
        pathRow.ColumnDefinitions.Add(new ColumnDefinition());
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var field = new Grid();
        _path.Margin = new Thickness(0); _path.BorderBrush = DesignSystem.Line; _path.Background = DesignSystem.Panel;
        _path.Padding = new Thickness(13, 12, 13, 12); _path.MinHeight = 46;
        _path.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _inputHint.Margin = new Thickness(14, 0, 8, 0); _inputHint.VerticalAlignment = VerticalAlignment.Center; _inputHint.IsHitTestVisible = false;
        field.Children.Add(_path); field.Children.Add(_inputHint); pathRow.Children.Add(field);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_path, "ExecutablePath");
        _select = Button("Scegli EXE", async (_, _) => await SelectAsync());
        _select.Margin = new Thickness(8, 0, 0, 0); _select.MinHeight = 46; _select.ToolTip = "Seleziona l'eseguibile del gioco";
        Grid.SetColumn(_select, 1); pathRow.Children.Add(_select);
        _analyze = Button("Analizza gioco  →", async (_, _) => await InspectAsync(_path.Text));
        _analyze.Margin = new Thickness(8, 0, 0, 0); _analyze.MinHeight = 46;
        _analyze.Background = Accent; _analyze.Foreground = BackgroundBrush; _analyze.BorderBrush = Accent; _analyze.IsEnabled = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_analyze, "AnalyzePath");
        Grid.SetColumn(_analyze, 2); pathRow.Children.Add(_analyze); header.Children.Add(pathRow);
        var tools = new Grid { Margin = new Thickness(0, 10, 0, 0) }; tools.ColumnDefinitions.Add(new ColumnDefinition()); tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tip = Text("Incolla un percorso, anche tra virgolette, oppure trascina l'EXE.", 11, Muted); tip.VerticalAlignment = VerticalAlignment.Center; tools.Children.Add(tip);
        var toolActions = new StackPanel { Orientation = Orientation.Horizontal };
        _export = Button("Esporta analisi", (_, _) => Export()); _export.IsEnabled = false; _export.FontSize = 11; _export.Padding = new Thickness(10, 6, 10, 6); _export.Margin = new Thickness(0); _export.Background = Brushes.Transparent;
        _cancel = Button("Annulla analisi", (_, _) => _cancellation?.Cancel()); _cancel.FontSize = 11; _cancel.Padding = new Thickness(10, 6, 10, 6); _cancel.Margin = new Thickness(8, 0, 0, 0); _cancel.Visibility = Visibility.Collapsed;
        toolActions.Children.Add(_export); toolActions.Children.Add(_cancel); Grid.SetColumn(toolActions, 1); tools.Children.Add(toolActions); header.Children.Add(tools);
        DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        _content.Margin = new Thickness(30, 22, 30, 20);
        main.Children.Add(new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    private static FrameworkElement NavigationContent(int page, bool active)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(DesignSystem.Icon(PageIcons[page], active ? Accent : Muted, 19));
        var label = Text(page == 2 ? "HDR / FX" : PageTitles[page], 13, active ? Ink : Muted, active ? FontWeights.SemiBold : FontWeights.Normal); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(label, 1); row.Children.Add(label);
        if (page is 1 or 2) { var badge = DesignSystem.Chip("PRESTO", Muted, Brushes.Transparent); Grid.SetColumn(badge, 2); row.Children.Add(badge); }
        return row;
    }

    private string GameName()
    {
        if (_report is null) return "Nessun gioco selezionato";
        var name = Path.GetFileName(Path.GetDirectoryName(_report.ExecutablePath));
        return string.IsNullOrWhiteSpace(name) || new[] { "bin", "win64", "win32", "binaries" }.Contains(name.ToLowerInvariant())
            ? Path.GetFileNameWithoutExtension(_report.ExecutablePath) : name;
    }

    private void RenderDashboard()
    {
        var hero = new Grid { Height = 235, ClipToBounds = true };
        hero.Children.Add(DesignSystem.HeroArtwork());
        var heroBody = new StackPanel { Margin = new Thickness(26, 24, 250, 26), VerticalAlignment = VerticalAlignment.Center };
        heroBody.Children.Add(DesignSystem.Chip(_report is null ? "IL TUO PROSSIMO SETUP PARTE QUI" : "GIOCO ANALIZZATO", Accent, DesignSystem.Color("#24372C")));
        var title = Text(_report is null ? "Il tuo gioco.\nIl tuo setup." : GameName(), _report is null ? 42 : 32, Ink, FontWeights.SemiBold);
        title.MaxHeight = 96; title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        if (_report is not null) title.ToolTip = GameName();
        title.FontFamily = new FontFamily("Bahnschrift SemiCondensed"); title.LineHeight = _report is null ? 45 : 38; title.Margin = new Thickness(0, 14, 0, 10); heroBody.Children.Add(title);
        var description = Text(_report is null ? "Seleziona un EXE per riconoscere i componenti e preparare un backup verificato."
            : "La tua copia è stata letta. Controlla i componenti rilevati e salva uno stato di riferimento.", 13, Muted); description.LineHeight = 21; heroBody.Children.Add(description);
        hero.Children.Add(heroBody);
        _content.Children.Add(new Border
        {
            Background = new LinearGradientBrush(DesignSystem.Color("#1B2D48").Color, DesignSystem.Color("#101B2B").Color, 20),
            BorderBrush = DesignSystem.Color("#2B4261"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = hero, ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 18)
        });
        var stats = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        for (var i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition());
        var architecture = _report?.Architecture.StartsWith("x86", StringComparison.Ordinal) == true ? "32 bit" : _report?.Architecture.StartsWith("x64", StringComparison.Ordinal) == true ? "64 bit" : _report?.Architecture ?? "Da rilevare";
        var api = _report?.GraphicsApis.FirstOrDefault()?.Replace("Direct3D", "DirectX", StringComparison.Ordinal) ?? "Da rilevare";
        var values = new[] { ("ARCHITETTURA", architecture, "chip"), ("API DICHIARATA", api, "scan"), ("COMPONENTI RILEVATI", _report?.Components.Count.ToString() ?? "—", "folder") };
        for (var i = 0; i < values.Length; i++)
        {
            var (label, value, icon) = values[i];
            var body = new StackPanel { Margin = new Thickness(18) };
            var caption = new DockPanel(); var mark = DesignSystem.Icon(icon, DesignSystem.Blue, 16); DockPanel.SetDock(mark, Dock.Right); caption.Children.Add(mark); caption.Children.Add(Text(label, 9, Muted, FontWeights.SemiBold)); body.Children.Add(caption);
            var number = Text(value, 23, Ink, FontWeights.SemiBold); number.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(number);
            var card = new Border { Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = body, Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 2 ? 0 : 6, 0) };
            Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        _content.Children.Add(stats);
        var section = new Grid { Margin = new Thickness(0, 0, 0, 12) }; section.ColumnDefinitions.Add(new ColumnDefinition()); section.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        section.Children.Add(Text("Prepara la prossima sessione", 20, Ink, FontWeights.SemiBold));
        var mode = Text("ANALISI + RECUPERO", 9, Muted); mode.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(mode, 1); section.Children.Add(mode); _content.Children.Add(section);
        var steps = new Grid(); steps.ColumnDefinitions.Add(new ColumnDefinition()); steps.ColumnDefinitions.Add(new ColumnDefinition());
        var backup = ActionTile("shield", "Prima metti al sicuro i file", "Una copia di riferimento verificata prima di qualsiasi futura modifica.", "Apri backup", (_, _) => { _page = 3; Render(); }, Accent);
        backup.Margin = new Thickness(0, 0, 7, 14); steps.Children.Add(backup);
        var diagnosis = ActionTile("scan", "Conosci il tuo gioco", "Architettura, librerie dichiarate e componenti già presenti, in un unico report.", "Apri diagnosi", (_, _) => { _page = 4; Render(); }, DesignSystem.Blue);
        diagnosis.Margin = new Thickness(7, 0, 0, 14); Grid.SetColumn(diagnosis, 1); steps.Children.Add(diagnosis); _content.Children.Add(steps);
        if (_report?.HasAntiCheatIndicators == true) Card("Da verificare", "Sono presenti possibili indicatori anti-cheat. Controlla la compatibilità prima delle future modifiche.");
        if (_report is not null)
        {
            var play = Button("Avvia il gioco senza modifiche  →", (_, _) => Launch()); play.IsEnabled = !_busy; _content.Children.Add(play);
        }
        var gpu = Hardware.DisplayDevices();
        Card("Il tuo hardware", gpu + "\nIl supporto HDR dello schermo verrà verificato nella fase di integrazione.");
    }

    private static Border ActionTile(string icon, string title, string description, string action, RoutedEventHandler click, Brush color)
    {
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(DesignSystem.Icon(icon, color, 23)); stack.Children[^1].SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        var heading = Text(title, 17, Ink, FontWeights.SemiBold); heading.Margin = new Thickness(0, 13, 0, 8); stack.Children.Add(heading);
        var body = Text(description, 12, Muted); body.LineHeight = 20; stack.Children.Add(body);
        var button = Button(action + "  →", click); button.Background = Brushes.Transparent; button.Foreground = color; button.BorderBrush = Brushes.Transparent; button.Padding = new Thickness(0, 14, 0, 0); button.Margin = new Thickness(0); button.HorizontalContentAlignment = HorizontalAlignment.Left; stack.Children.Add(button);
        return new Border { Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = stack };
    }

    private void FeatureCards(params (string Title, string Caption, string Icon)[] features)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        for (var i = 0; i < features.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var body = new StackPanel { Margin = new Thickness(18) };
            var icon = DesignSystem.Icon(features[i].Icon, DesignSystem.Blue, 22); icon.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(icon);
            var title = Text(features[i].Title, 19, Ink, FontWeights.SemiBold); title.Margin = new Thickness(0, 13, 0, 9); body.Children.Add(title);
            var caption = Text(features[i].Caption, 12, Muted); caption.LineHeight = 20; caption.Margin = new Thickness(0, 0, 0, 16); body.Children.Add(caption);
            body.Children.Add(DesignSystem.Chip("IN SVILUPPO", DesignSystem.Amber, DesignSystem.Color("#332A20")));
            var card = new Border { Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = body, Margin = new Thickness(i == 0 ? 0 : 6, 0, i == features.Length - 1 ? 0 : 6, 0) };
            Grid.SetColumn(card, i); grid.Children.Add(card);
        }
        _content.Children.Add(grid);
    }

    private void Details(string title, string body)
    {
        var text = Text(body, 12, Muted); text.LineHeight = 21; text.Margin = new Thickness(4, 14, 4, 4);
        _content.Children.Add(new Expander
        {
            Header = title, Content = text, IsExpanded = false, Foreground = Muted,
            Margin = new Thickness(0, 4, 0, 18), Padding = new Thickness(14),
            Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1)
        });
    }

    private void ApplyDarkTitlebar()
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
