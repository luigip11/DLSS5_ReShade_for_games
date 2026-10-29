using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.Run(new MainWindow());
    }
}

internal sealed class MainWindow : Window
{
    private static readonly Brush BackgroundBrush = Brush("#11151D");
    private static readonly Brush Surface = Brush("#1B2230");
    private static readonly Brush Ink = Brush("#ECF1FA");
    private static readonly Brush Muted = Brush("#AAB7CA");
    private static readonly Brush Accent = Brush("#8ADAC3");
    private readonly TextBlock _path = Text("Seleziona l'eseguibile del gioco per iniziare.", 15, Muted);
    private readonly TextBlock _status = Text("Pronto · analisi locale", 13, Muted);
    private readonly StackPanel _content = new() { Margin = new Thickness(32) };
    private readonly Button _select;
    private readonly Button _export;
    private readonly Button _cancel;
    private readonly List<Button> _navigation = [];
    private GameReport? _report;
    private CancellationTokenSource? _cancellation;
    private int _page;
    private bool _busy;
    private bool _closing;

    public MainWindow()
    {
        Title = "GamePanel · Analisi iniziale";
        Width = 1160; Height = 800; MinWidth = 860; MinHeight = 620;
        Background = BackgroundBrush; Foreground = Ink;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowDrop = true;
        Resources.Add(typeof(Button), ButtonStyle());

        var frame = new Grid();
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
        frame.ColumnDefinitions.Add(new ColumnDefinition());
        Content = frame;

        var sidebar = new DockPanel { Background = Brush("#161C27"), LastChildFill = true };
        frame.Children.Add(sidebar);
        var bottom = new StackPanel { Margin = new Thickness(22) };
        bottom.Children.Add(Text("0.1 · ALPHA", 12, Accent));
        bottom.Children.Add(Text("Selezione e analisi disponibili.\nMod DLSS/HDR da integrare.", 12, Muted));
        DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom);
        var menu = new StackPanel { Margin = new Thickness(16, 28, 16, 0) };
        menu.Children.Add(Text("GAMEPANEL", 22, Ink, FontWeights.SemiBold));
        menu.Children.Add(Text("Il tuo gioco, sotto controllo", 12, Muted));
        menu.Children.Add(new Border { Height = 28 });
        var names = new[] { "01   Gioco", "02   DLSS", "03   HDR e immagine", "04   Diagnosi" };
        for (var i = 0; i < names.Length; i++)
        {
            var page = i;
            var button = Button(names[i], (_, _) => { _page = page; Render(); });
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            _navigation.Add(button); menu.Children.Add(button);
        }
        sidebar.Children.Add(menu);

        var main = new DockPanel(); Grid.SetColumn(main, 1); frame.Children.Add(main);
        var footer = new Border { BorderBrush = Brush("#2A3445"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(32, 14, 32, 14), Child = _status };
        DockPanel.SetDock(footer, Dock.Bottom); main.Children.Add(footer);
        var header = new StackPanel { Margin = new Thickness(32, 28, 32, 16) };
        header.Children.Add(Text("Scegli da dove iniziare.", 28, Ink, FontWeights.SemiBold));
        _path.Margin = new Thickness(0, 8, 0, 16); header.Children.Add(_path);
        var actions = new WrapPanel();
        _select = Button("Seleziona gioco .exe", async (_, _) => await SelectAsync());
        _select.Background = Accent; _select.Foreground = BackgroundBrush;
        _export = Button("Esporta analisi", (_, _) => Export()); _export.IsEnabled = false;
        _cancel = Button("Annulla analisi", (_, _) => _cancellation?.Cancel()); _cancel.Visibility = Visibility.Collapsed;
        actions.Children.Add(_select); actions.Children.Add(_export); actions.Children.Add(_cancel);
        header.Children.Add(actions);
        header.Children.Add(Text("Oppure trascina qui l'EXE · Nessun account richiesto", 12, Muted));
        DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        main.Children.Add(new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        DragOver += (_, e) => { e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) =>
        {
            if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths)
                await InspectAsync(paths[0]);
        };
        Closing += (_, _) => { _closing = true; _cancellation?.Cancel(); };
        Render();
    }

    private async Task SelectAsync()
    {
        var picker = new OpenFileDialog { Title = "Seleziona l'eseguibile reale del gioco", Filter = "Eseguibili Windows (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) == true) await InspectAsync(picker.FileName);
    }

    private async Task InspectAsync(string path)
    {
        if (_busy) return;
        _busy = true; _select.IsEnabled = false; _export.IsEnabled = false;
        _cancel.Visibility = Visibility.Visible;
        _report = null; _page = 0;
        _path.Text = path;
        _status.Text = "Analisi in corso · Nessuna esecuzione del gioco";
        _cancellation = new CancellationTokenSource();
        Render();
        try
        {
            var token = _cancellation.Token;
            var report = await Task.Run(() => new GameInspector().Inspect(path, token), token);
            if (_closing) return;
            _report = report; _export.IsEnabled = true;
            _status.Text = "Analisi completata · Nessuna mod applicata";
        }
        catch (OperationCanceledException) { if (!_closing) _status.Text = "Analisi annullata"; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or InvalidOperationException)
        {
            if (!_closing)
            {
                _status.Text = "Analisi non completata";
                MessageBox.Show(this, e.Message, "Impossibile analizzare il file", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            _cancellation.Dispose(); _cancellation = null; _busy = false;
            if (!_closing) { _select.IsEnabled = true; _cancel.Visibility = Visibility.Collapsed; Render(); }
        }
    }

    private void Render()
    {
        _content.Children.Clear();
        for (var i = 0; i < _navigation.Count; i++)
        {
            _navigation[i].Background = i == _page ? Brush("#293D3D") : Brushes.Transparent;
            _navigation[i].Foreground = i == _page ? Accent : Muted;
        }
        switch (_page)
        {
            case 0: RenderGame(); break;
            case 1: RenderDlss(); break;
            case 2: RenderHdr(); break;
            default: RenderDiagnostics(); break;
        }
    }

    private void RenderGame()
    {
        Heading("Gioco", "Partiamo dai file reali, anche fuori dalle librerie degli store.");
        if (_report is null)
        {
            Card(_busy ? "Lettura dell'eseguibile…" : "Un EXE per iniziare", _busy
                ? "Controllo intestazioni, import grafici e componenti nelle cartelle vicine."
                : "Seleziona o trascina l'EXE. L'analisi legge i file senza caricarli come codice e senza applicare mod.");
            Card("Prima versione di collaudo", "Questa alpha prepara la diagnosi. Installazione, backup/ripristino, parametri DLSS e HDR arriveranno nei prossimi blocchi; in questa versione non sono attivi.");
        }
        else
        {
            Card(System.IO.Path.GetFileName(_report.ExecutablePath),
                $"Architettura: {_report.Architecture}\nAPI dichiarate: {(_report.GraphicsApis.Count > 0 ? string.Join(", ", _report.GraphicsApis) : "Non determinate")}\nComponenti individuati: {_report.Components.Count}");
            Card("Come leggere il risultato", "Gli import statici forniscono indizi sul renderer. I nomi delle DLL indicano componenti possibili, non ne verificano autenticità o compatibilità.");
            if (_report.HasAntiCheatIndicators)
                Card("Da verificare prima di modificare", "La scansione ha trovato possibili indicatori anti-cheat. Questa alpha non applica mod.");
            var launch = Button("Avvia senza modifiche", (_, _) => Launch());
            _content.Children.Add(launch);
        }
        Card("Schermo e GPU", Hardware.DisplayDevices() + "\nSupporto HDR del display: rilevamento da integrare. La GPU da sola non conferma il supporto HDR dello schermo.");
    }

    private void RenderDlss()
    {
        Heading("DLSS", "Super Resolution, Frame Generation e Neural Rendering saranno gestiti separatamente.");
        var found = _report?.Components.Where(c => c.Kind.StartsWith("DLSS", StringComparison.Ordinal)).ToArray() ?? [];
        Card("Componenti presenti", found.Length == 0 ? "Nessuna DLL DLSS riconosciuta nell'analisi attuale. Questo non esclude caricamenti dal driver o da percorsi non scansionati."
            : string.Join("\n", found.Select(c => $"{c.Kind} · {c.Version}\n{c.RelativePath}")));
        Card("Gestione da integrare", "La prossima fase introdurrà versioni approvate, anteprima delle modifiche e backup verificati. Nessun download, swap o controllo DLSS viene effettuato da questa alpha.");
        Card("Giochi vecchi", "I profili per Underground 1 e 2 richiederanno un collaudo specifico. L'eventuale percorso neurale con helper x64 resterà sperimentale finché non verificato.");
    }

    private void RenderHdr()
    {
        Heading("HDR e immagine", "Un solo metodo HDR attivo per profilo, quando compatibile.");
        Card("Calibrazione da integrare", "Il pannello finale proporrà luminosità di riferimento, picco luminoso e regolazioni immagine supportate dal componente selezionato. Questa alpha non cambia HDR di Windows, driver o impostazioni dei giochi.");
        Card("Prima il display", "Il supporto HDR andrà rilevato per il monitor usato dal gioco. Fino a quel controllo non viene dichiarata compatibilità HDR.");
        Card("ReShade e componenti esistenti", _report is null ? "Seleziona un gioco per leggere i componenti presenti."
            : string.Join("\n", _report.Components.Where(c => c.Kind.Contains("ReShade", StringComparison.Ordinal) || c.Kind.Contains("proxy", StringComparison.Ordinal) || c.Kind.Contains("Add-on", StringComparison.Ordinal))
                .Select(c => $"{c.RelativePath} · {c.Kind}").DefaultIfEmpty("Nessun componente riconosciuto nella scansione limitata.")));
    }

    private void RenderDiagnostics()
    {
        Heading("Diagnosi", "Report locale, esportato soltanto su tua richiesta.");
        if (_report is null) { Card("Nessuna analisi disponibile", "Seleziona un EXE dalla pagina Gioco."); return; }
        Card("Controlli e limiti", string.Join("\n\n", _report.Notes));
        Card("SHA-256 dell'eseguibile", _report.Sha256 + "\nIdentifica i byte letti; non è un certificato di sicurezza.");
        Card("Componenti riconosciuti", string.Join("\n", _report.Components.Select(c => $"{c.RelativePath}\n{c.Kind} · {c.Version}").DefaultIfEmpty("Nessuno nella scansione limitata.")));
        Card("Privacy", "Il JSON esportato contiene il percorso locale dell'EXE e i nomi dei componenti. Controllalo prima di condividerlo. L'app non invia report e non effettua connessioni di rete.");
    }

    private void Export()
    {
        if (_report is null) return;
        var dialog = new SaveFileDialog { Title = "Esporta analisi locale", Filter = "Report JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true, FileName = "GamePanel-analysis.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (!string.Equals(System.IO.Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Il report deve avere estensione .json.");
            var gameFolder = System.IO.Path.GetDirectoryName(_report.ExecutablePath)! + System.IO.Path.DirectorySeparatorChar;
            if (System.IO.Path.GetFullPath(dialog.FileName).StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Salva il report fuori dalla cartella del gioco, per esempio in Documenti.");
            System.IO.File.WriteAllText(dialog.FileName, GameInspector.ToJson(_report));
            _status.Text = "Report esportato · Contiene percorsi locali";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { MessageBox.Show(this, e.Message, "Esportazione non completata", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void Launch()
    {
        if (_report is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_report.ExecutablePath)
            { WorkingDirectory = System.IO.Path.GetDirectoryName(_report.ExecutablePath), UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { MessageBox.Show(this, e.Message, "Avvio non completato", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void Heading(string title, string description)
    { _content.Children.Add(Text(title, 25, Ink, FontWeights.SemiBold)); var t = Text(description, 14, Muted); t.Margin = new Thickness(0, 6, 0, 24); _content.Children.Add(t); }

    private void Card(string title, string description)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(title, 17, Ink, FontWeights.SemiBold));
        var body = Text(description, 14, Muted); body.Margin = new Thickness(0, 10, 0, 0); body.LineHeight = 23; stack.Children.Add(body);
        _content.Children.Add(new Border { Background = Surface, CornerRadius = new CornerRadius(10), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 16), Child = stack });
    }

    private static TextBlock Text(string value, double size, Brush color, FontWeight? weight = null) => new()
    { Text = value, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(16, 12, 16, 12), Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += handler; return button;
    }
    private static Style ButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#273246")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, .45)); style.Triggers.Add(disabled);
        return style;
    }
}

internal static class Hardware
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);

    public static string DisplayDevices()
    {
        var names = new HashSet<string>();
        for (uint i = 0; i < 32; i++)
        {
            var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>(), DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
            if (!EnumDisplayDevices(null, i, ref display, 0)) break;
            if (!string.IsNullOrWhiteSpace(display.DeviceString) && (display.StateFlags & 8) == 0) names.Add(display.DeviceString);
        }
        return names.Count > 0 ? string.Join("\n", names) : "Adattatori video non determinati.";
    }
}
