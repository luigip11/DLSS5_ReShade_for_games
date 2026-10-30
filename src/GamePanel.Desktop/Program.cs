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
    private readonly TextBox _path = new()
    {
        FontSize = 14, Foreground = Ink, Background = Surface, CaretBrush = Accent,
        BorderBrush = Brush("#52637B"), BorderThickness = new Thickness(1),
        Padding = new Thickness(12), AcceptsReturn = false, AcceptsTab = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Center,
        ToolTip = "Incolla il percorso completo dell'EXE, anche con le virgolette di Windows."
    };
    private readonly TextBlock _status = Text("Pronto · analisi locale", 13, Muted);
    private readonly StackPanel _content = new() { Margin = new Thickness(32) };
    private readonly Button _select;
    private readonly Button _analyze;
    private readonly Button _export;
    private readonly Button _cancel;
    private readonly List<Button> _navigation = [];
    private GameReport? _report;
    private CancellationTokenSource? _cancellation;
    private int _page;
    private bool _busy;
    private bool _closing;
    private readonly BackupService _backups = new(BackupService.DefaultStore);
    private IReadOnlyList<BackupSummary> _snapshots = [];
    private string? _selectedBackup;
    private string? _backupForPath;
    private RestorePlan? _restorePlan;
    private string _backupMessage = "Crea una copia di riferimento prima delle future modifiche grafiche.";

    public MainWindow()
    {
        Title = "GamePanel · 0.1.0-alpha.3";
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
        bottom.Children.Add(Text("0.1 · ALPHA 3", 12, Accent));
        bottom.Children.Add(Text("Analisi e backup locali.\nMod DLSS/HDR da integrare.", 12, Muted));
        DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom);
        var menu = new StackPanel { Margin = new Thickness(16, 28, 16, 0) };
        menu.Children.Add(Text("GAMEPANEL", 22, Ink, FontWeights.SemiBold));
        menu.Children.Add(Text("Il tuo gioco, sotto controllo", 12, Muted));
        menu.Children.Add(new Border { Height = 28 });
        var names = new[] { "01   Gioco", "02   DLSS", "03   HDR e immagine", "04   Backup", "05   Diagnosi" };
        for (var i = 0; i < names.Length; i++)
        {
            var page = i;
            var button = Button(names[i], (_, _) => { _page = page; Render(); });
            if (i == 3) System.Windows.Automation.AutomationProperties.SetAutomationId(button, "BackupsPage");
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            _navigation.Add(button); menu.Children.Add(button);
        }
        sidebar.Children.Add(menu);

        var main = new DockPanel(); Grid.SetColumn(main, 1); frame.Children.Add(main);
        var footer = new Border { BorderBrush = Brush("#2A3445"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(32, 14, 32, 14), Child = _status };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_status, "OperationStatus");
        DockPanel.SetDock(footer, Dock.Bottom); main.Children.Add(footer);
        var header = new StackPanel { Margin = new Thickness(32, 28, 32, 16) };
        header.Children.Add(Text("Scegli da dove iniziare.", 28, Ink, FontWeights.SemiBold));
        var pathLabel = new Label { Content = "Percorso completo del gioco (.exe)", Target = _path, Foreground = Muted, Padding = new Thickness(0), Margin = new Thickness(0, 12, 0, 6) };
        header.Children.Add(pathLabel);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_path, "ExecutablePath");
        _path.Margin = new Thickness(0, 0, 0, 12); header.Children.Add(_path);
        var actions = new WrapPanel();
        _select = Button("Seleziona gioco .exe", async (_, _) => await SelectAsync());
        _analyze = Button("Analizza percorso", async (_, _) => await InspectAsync(_path.Text));
        _analyze.Background = Accent; _analyze.Foreground = BackgroundBrush;
        _analyze.IsEnabled = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_analyze, "AnalyzePath");
        _export = Button("Esporta analisi", (_, _) => Export()); _export.IsEnabled = false;
        _cancel = Button("Annulla analisi", (_, _) => _cancellation?.Cancel()); _cancel.Visibility = Visibility.Collapsed;
        actions.Children.Add(_analyze); actions.Children.Add(_select); actions.Children.Add(_export); actions.Children.Add(_cancel);
        header.Children.Add(actions);
        header.Children.Add(Text("Incolla il percorso e premi Invio, scegli il file oppure trascina qui l'EXE.", 12, Muted));
        DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        main.Children.Add(new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _path.TextChanged += (_, _) =>
        {
            _analyze.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(_path.Text);
            if (!_busy) ResetBackups();
            if (!_busy && _report is not null)
            {
                _report = null; _export.IsEnabled = false;
                _status.Text = "Percorso modificato · Premi Analizza percorso";
                Render();
            }
            else if (!_busy && _page == 3) Render();
        };
        _path.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && _analyze.IsEnabled)
            { e.Handled = true; await InspectAsync(_path.Text); }
        };
        PreviewDragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = !_busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 }
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewDrop += async (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Handled = true;
            if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths)
                await InspectAsync(paths[0]);
        };
        Closing += (_, _) => { _closing = true; _cancellation?.Cancel(); };
        Loaded += (_, _) => _path.Focus();
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
        _busy = true; _select.IsEnabled = false; _analyze.IsEnabled = false;
        _path.IsReadOnly = true; _export.IsEnabled = false;
        _cancel.Visibility = Visibility.Visible;
        _report = null; _page = 0;
        ResetBackups();
        _path.Text = path;
        _status.Text = "Analisi in corso · Nessuna esecuzione del gioco";
        _cancellation = new CancellationTokenSource();
        Render();
        try
        {
            var token = _cancellation.Token;
            var report = await Task.Run(() => new GameInspector().Inspect(path, token), token);
            if (_closing) return;
            _path.Text = report.ExecutablePath;
            _report = report; _export.IsEnabled = true;
            _status.Text = "Analisi completata · Nessuna mod applicata";
        }
        catch (OperationCanceledException) { if (!_closing) _status.Text = "Analisi annullata"; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or InvalidOperationException)
        {
            if (!_closing)
            {
                var title = e switch
                {
                    FileNotFoundException or DirectoryNotFoundException => "File non trovato",
                    UnauthorizedAccessException => "Accesso al file negato",
                    BadImageFormatException => "File trovato, formato non leggibile",
                    _ => "Analisi non completata"
                };
                var detail = e switch
                {
                    FileNotFoundException or DirectoryNotFoundException => "Controlla che il percorso indichi il file reale del gioco. Puoi sceglierlo con Seleziona gioco .exe.",
                    UnauthorizedAccessException => "Windows ha negato la lettura. Controlla i permessi del file selezionato.",
                    _ => e.Message
                };
                _status.Text = title;
                MessageBox.Show(this, detail + "\n\nPercorso inserito:\n" + path, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            _cancellation.Dispose(); _cancellation = null; _busy = false;
            if (!_closing)
            {
                _select.IsEnabled = true; _path.IsReadOnly = false;
                _analyze.IsEnabled = !string.IsNullOrWhiteSpace(_path.Text);
                _cancel.Visibility = Visibility.Collapsed; Render();
            }
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
            case 3: RenderBackups(); break;
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
            Card("Prima di applicare mod", "Analizza l'EXE e apri Backup per salvare una copia dei file gestiti. Installazione e parametri DLSS/HDR saranno introdotti dopo il collaudo del recupero.");
        }
        else
        {
            Card(System.IO.Path.GetFileName(_report.ExecutablePath),
                $"Architettura: {_report.Architecture}\nAPI dichiarate: {(_report.GraphicsApis.Count > 0 ? string.Join(", ", _report.GraphicsApis) : "Non determinate")}\nComponenti individuati: {_report.Components.Count}");
            Card("Come leggere il risultato", "Gli import statici forniscono indizi sul renderer. I nomi delle DLL indicano componenti possibili, non ne verificano autenticità o compatibilità.");
            if (_report.HasAntiCheatIndicators)
                Card("Da verificare prima di modificare", "La scansione ha trovato possibili indicatori anti-cheat. Questa alpha non applica mod.");
            var launch = Button("Avvia senza modifiche", (_, _) => Launch());
            launch.IsEnabled = !_busy;
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

    private void ResetBackups()
    {
        _snapshots = []; _selectedBackup = null; _backupForPath = null; _restorePlan = null;
        _backupMessage = "Crea una copia di riferimento prima delle future modifiche grafiche.";
    }

    private void RenderBackups()
    {
        Heading("Backup e ripristino", "Copie locali verificate, conservate anche dopo la chiusura dell'app.");
        Card("Stato", _backupMessage);
        Card("Cosa viene salvato", "EXE selezionato e componenti grafici riconosciuti dall'analisi. Sono registrati anche i nomi dei futuri loader e file di configurazione. Salvataggi e asset del gioco non fanno parte di questo backup mirato.");
        var actions = new WrapPanel();
        var create = Button("Crea backup", async (_, _) => await CreateBackupAsync());
        System.Windows.Automation.AutomationProperties.SetAutomationId(create, "CreateBackup");
        create.IsEnabled = !_busy && _report is not null;
        var load = Button("Carica backup del percorso", async (_, _) => await LoadBackupsAsync());
        load.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(_path.Text);
        actions.Children.Add(create); actions.Children.Add(load); _content.Children.Add(actions);
        if (_snapshots.Count > 0)
        {
            var chooser = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 16), Padding = new Thickness(12),
                ItemsSource = _snapshots.Select(s => new BackupChoice(s.Id,
                    $"{s.CreatedAt.LocalDateTime:g} · {s.FileCount} file · {s.Bytes / (1024d * 1024):F1} MB · {s.Id[..8]}")).ToArray(),
                DisplayMemberPath = nameof(BackupChoice.Label), SelectedValuePath = nameof(BackupChoice.Id),
                SelectedValue = _selectedBackup, IsEnabled = !_busy
            };
            chooser.SelectionChanged += (_, _) =>
            {
                if (chooser.SelectedValue is string id && id != _selectedBackup)
                { _selectedBackup = id; _restorePlan = null; Render(); }
            };
            _content.Children.Add(chooser);
            System.Windows.Automation.AutomationProperties.SetAutomationId(chooser, "BackupSnapshotPicker");
            var preview = Button("Verifica e mostra anteprima", async (_, _) => await PreviewBackupAsync());
            System.Windows.Automation.AutomationProperties.SetAutomationId(preview, "VerifyBackup");
            preview.IsEnabled = !_busy && _selectedBackup is not null;
            _content.Children.Add(preview);
        }
        if (_restorePlan is not null)
        {
            var conflicts = _restorePlan.Files.Where(f => f.Disposition == RestoreDisposition.Conflict).Select(f => f.RelativePath).ToArray();
            var missing = _restorePlan.Files.Where(f => f.Disposition == RestoreDisposition.Missing).Select(f => f.RelativePath).ToArray();
            if (conflicts.Length > 0)
                Card("Ripristino bloccato", "Questi file esistono e differiscono dalla copia di riferimento, oppure sono comparsi dopo il backup. Vanno verificati prima di procedere:\n\n" + string.Join("\n", conflicts));
            else if (missing.Length == 0)
            {
                Card("Verifica completata", "Le copie salvate hanno hash validi e i file previsti sono già presenti e identici. Nessun ripristino necessario.");
                if (_restorePlan.HasPendingRepair)
                {
                    var finish = Button("Concludi ripristino interrotto", async (_, _) => await RestoreBackupAsync());
                    finish.IsEnabled = !_busy; _content.Children.Add(finish);
                }
            }
            else
            {
                Card($"File mancanti da recuperare: {missing.Length}", string.Join("\n", missing));
                var restore = Button($"Ripristina / riprendi ({missing.Length} file)", async (_, _) => await RestoreBackupAsync());
                System.Windows.Automation.AutomationProperties.SetAutomationId(restore, "RestoreMissing");
                restore.IsEnabled = !_busy; _content.Children.Add(restore);
            }
        }
        Card("Protezione dei file presenti", "Questa versione recupera soltanto i file mancanti. Se un file presente è cambiato, il ripristino viene bloccato. Un recupero interrotto si può riprendere dopo aver risolto il problema; i file già recuperati restano conservati.");
        Card("Dove sono conservate le copie", _backups.StorePath + "\nNon eliminare questa cartella: contiene i dati necessari al recupero. Le copie non sono caricate su Internet.");
        Card("Cartelle protette", "La creazione del backup richiede lettura dei file. Il recupero richiede anche scrittura nella cartella del gioco; Windows può negarla per Program Files. In quel caso l'app segnala il problema e conserva il backup.");
    }

    private sealed record BackupChoice(string Id, string Label);

    private async Task CreateBackupAsync()
    {
        var report = _report;
        if (report is null) return;
        await RunBackupTaskAsync((token, progress) =>
        {
            var snapshot = _backups.Create(report, token, progress);
            return (Snapshot: snapshot, List: _backups.List(report.ExecutablePath));
        }, result =>
        {
            _snapshots = result.List; _selectedBackup = result.Snapshot.Id; _backupForPath = report.ExecutablePath;
            _restorePlan = null;
            _backupMessage = $"Backup creato e verificato: {result.Snapshot.Files.Count(f => f.Existed)} file. Il gioco non è stato modificato.";
        }, "Creazione del backup in corso");
    }

    private async Task LoadBackupsAsync()
    {
        var raw = _path.Text;
        await RunBackupTaskAsync((_, _) =>
        {
            var path = Path.GetFullPath(ExecutablePath.Normalize(raw));
            return (Path: path, List: _backups.List(path));
        }, result =>
        {
            _backupForPath = result.Path; _snapshots = result.List; _selectedBackup = _snapshots.FirstOrDefault()?.Id;
            _restorePlan = null; _backupMessage = $"Backup disponibili per questo percorso: {_snapshots.Count}.";
        }, "Caricamento backup");
    }

    private async Task PreviewBackupAsync()
    {
        var path = _backupForPath; var id = _selectedBackup;
        if (path is null || id is null) return;
        await RunBackupTaskAsync((token, _) => _backups.Preview(path, id, token), plan =>
        {
            _restorePlan = plan;
            _backupMessage = plan.HasConflicts ? "Conflitti rilevati: nessuna scrittura eseguita." : "Integrità delle copie verificata. Controlla l'anteprima.";
        }, "Verifica delle copie e dei file del gioco");
    }

    private async Task RestoreBackupAsync()
    {
        var path = _backupForPath; var id = _selectedBackup;
        if (path is null || id is null || _restorePlan is null || _restorePlan.HasConflicts) return;
        await RunBackupTaskAsync((token, progress) =>
        {
            var result = _backups.Restore(path, id, token, progress);
            return (Result: result, Plan: _backups.Preview(path, id, token));
        }, result =>
        {
            _restorePlan = result.Plan; _report = null; _export.IsEnabled = false;
            _backupMessage = $"Ripristino completato: {result.Result.Restored} file recuperati, {result.Result.Verified} file verificati. Rianalizza il gioco per aggiornare il report.";
        }, "Recupero dei file mancanti");
    }

    private async Task RunBackupTaskAsync<T>(Func<CancellationToken, IProgress<BackupProgress>, T> work,
        Action<T> completed, string message)
    {
        if (_busy) return;
        _busy = true; _path.IsReadOnly = true; _select.IsEnabled = false; _analyze.IsEnabled = false; _export.IsEnabled = false;
        _cancel.Content = "Interrompi operazione";
        _cancel.Visibility = Visibility.Visible; _status.Text = message; _restorePlan = null;
        _cancellation = new CancellationTokenSource(); Render();
        var operation = _cancellation;
        var progress = new Progress<BackupProgress>(p => { if (!_closing && _busy && ReferenceEquals(_cancellation, operation)) _status.Text = $"{p.Message} · {p.Completed}/{p.Total}"; });
        try
        {
            var token = _cancellation.Token;
            var result = await Task.Run(() => work(token, progress), token);
            if (!_closing) { completed(result); _status.Text = "Operazione backup completata"; }
        }
        catch (OperationCanceledException)
        {
            if (!_closing) { _backupMessage = "Operazione interrotta. Ricarica e verifica il backup prima di riprendere. Le copie incomplete non sono offerte per il ripristino."; _status.Text = "Operazione interrotta"; }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (!_closing) { _backupMessage = e.Message; _status.Text = "Operazione backup non completata"; }
        }
        finally
        {
            _cancellation.Dispose(); _cancellation = null; _busy = false;
            if (!_closing)
            {
                _path.IsReadOnly = false; _select.IsEnabled = true;
                _analyze.IsEnabled = !string.IsNullOrWhiteSpace(_path.Text); _export.IsEnabled = _report is not null;
                _cancel.Content = "Annulla analisi"; _cancel.Visibility = Visibility.Collapsed; Render();
            }
        }
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
        if (_busy || _report is null) return;
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
