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

internal sealed partial class MainWindow : Window
{
    private static readonly Brush BackgroundBrush = DesignSystem.Background;
    private static readonly Brush Surface = DesignSystem.Panel;
    private static readonly Brush Ink = DesignSystem.Text;
    private static readonly Brush Muted = DesignSystem.Muted;
    private static readonly Brush Accent = DesignSystem.Lime;
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
    private Button _select = null!;
    private Button _analyze = null!;
    private Button _export = null!;
    private Button _cancel = null!;
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
        BuildShell();
        _path.TextChanged += (_, _) =>
        {
            _inputHint.Visibility = _path.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            _path.CaretIndex = _path.Text.Length;
            _report = report; _export.IsEnabled = true;
            await RefreshEffectsAsync(report.ExecutablePath);
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
        _sectionName.Text = PageTitles[_page].ToUpperInvariant();
        _gameContext.Text = GameName();
        _inputHint.Visibility = _path.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _content.Children.Clear();
        for (var i = 0; i < _navigation.Count; i++)
        {
            _navigation[i].Background = i == _page ? DesignSystem.Color("#203127") : Brushes.Transparent;
            _navigation[i].BorderBrush = i == _page ? DesignSystem.Color("#3C5536") : Brushes.Transparent;
            _navigation[i].Content = NavigationContent(i, i == _page);
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

    private void RenderGame() => RenderDashboard();

    private void RenderDlss()
    {
        Heading("Più controllo sul rendering.", "Le funzioni DLSS verranno proposte in base al gioco e all'hardware compatibile.");
        FeatureCards(("Super Resolution", "Qualità di ricostruzione e versione della libreria per gioco.", "chip"),
            ("Frame Generation", "Gestione separata della generazione dei fotogrammi.", "game"),
            ("Neural Rendering", "Percorso dedicato, da collaudare anche sui titoli legacy.", "scan"));
        var found = _report?.Components.Where(c => c.Kind.StartsWith("DLSS", StringComparison.Ordinal)).ToArray() ?? [];
        Card("Componenti presenti", found.Length == 0 ? "Nessuna DLL DLSS riconosciuta nell'analisi attuale. Questo non esclude caricamenti dal driver o da percorsi non scansionati."
            : string.Join("\n", found.Select(c => $"{c.Kind} · {c.Version}\n{c.RelativePath}")));
        Details("Disponibilità e giochi legacy", "Questa alpha riconosce i componenti ma non installa o regola DLSS. I profili per Underground 1 e 2 richiedono collaudo specifico; l'eventuale helper x64 resterà sperimentale finché non verificato.");
    }

    private void RenderHdr() => RenderEffects();

    private void RenderDiagnostics()
    {
        Heading("Diagnosi", "Report locale, esportato soltanto su tua richiesta.");
        if (_report is null) { Card("Nessuna analisi disponibile", "Seleziona un EXE dalla pagina Gioco."); return; }
        Card("Controlli e limiti", string.Join("\n\n", _report.Notes));
        Card("SHA-256 dell'eseguibile", _report.Sha256 + "\nIdentifica i byte letti; non è un certificato di sicurezza.");
        Card("Componenti riconosciuti", string.Join("\n", _report.Components.Select(c => $"{c.RelativePath}\n{c.Kind} · {c.Version}").DefaultIfEmpty("Nessuno nella scansione limitata.")));
        Card("Privacy", "Il JSON esportato contiene il percorso locale dell'EXE e i nomi dei componenti. Controllalo prima di condividerlo. L'app non invia report. Il download esplicito ReShade contatta soltanto reshade.me; ReShade può cercare aggiornamenti quando avvii il gioco.");
    }

    private void ResetBackups()
    {
        ResetEffects();
        _snapshots = []; _selectedBackup = null; _backupForPath = null; _restorePlan = null;
        _backupMessage = "Crea una copia di riferimento prima delle future modifiche grafiche.";
    }

    private void RenderBackups()
    {
        Heading("Backup e ripristino", "Copie locali verificate, conservate anche dopo la chiusura dell'app.");
        Card("La tua copia di riferimento", _backupMessage);
        var scope = Text("Salva l'EXE e i componenti riconosciuti. Salvataggi e asset del gioco sono esclusi.", 12, Muted);
        scope.Margin = new Thickness(0, 0, 0, 14); _content.Children.Add(scope);
        var actions = new WrapPanel();
        var create = Button("Crea backup", async (_, _) => await CreateBackupAsync());
        System.Windows.Automation.AutomationProperties.SetAutomationId(create, "CreateBackup");
        create.IsEnabled = !_busy && _report is not null;
        create.Background = Accent; create.Foreground = BackgroundBrush; create.BorderBrush = Accent;
        var load = Button("Carica backup del percorso", async (_, _) => await LoadBackupsAsync());
        load.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(_path.Text);
        actions.Children.Add(create); actions.Children.Add(load); _content.Children.Add(actions);
        if (_snapshots.Count > 0)
        {
            var chooser = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 16), Padding = new Thickness(12),
                ItemsSource = _snapshots.Select(s => new BackupChoice(s.Id,
                    $"{s.CreatedAt.LocalDateTime:dd MMM yyyy · HH:mm} · {s.FileCount} file · {s.Bytes / (1024d * 1024):F1} MB")).ToArray(),
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
                restore.Background = Accent; restore.Foreground = BackgroundBrush; restore.BorderBrush = Accent;
                restore.IsEnabled = !_busy; _content.Children.Add(restore);
            }
        }
        Details("Come funziona il recupero", "Si recuperano solo i file mancanti. Un file presente ma cambiato blocca l'operazione. Dopo un'interruzione, i file già recuperati restano e il recupero può essere ripreso. Il backup resta conservato.");
        Details("Dove si trovano le copie e quali permessi servono", _backups.StorePath + "\n\nLe copie sono locali: non eliminare questa cartella. La creazione richiede lettura dei file; il recupero richiede scrittura nella cartella del gioco. Windows può negarla per Program Files: in quel caso l'app segnala il problema e conserva il backup.");
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
            if (_profiles.Status(_report.ExecutablePath).Pending)
                throw new InvalidOperationException("Completa o annulla l'operazione del profilo in HDR / FX prima di avviare il gioco.");
            Process.Start(new ProcessStartInfo(_report.ExecutablePath)
            { WorkingDirectory = System.IO.Path.GetDirectoryName(_report.ExecutablePath), UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { MessageBox.Show(this, e.Message, "Avvio non completato", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void Heading(string title, string description)
    { _content.Children.Add(Text(title, 25, Ink, FontWeights.SemiBold)); var t = Text(description, 14, Muted); t.Margin = new Thickness(0, 6, 0, 24); _content.Children.Add(t); }

    private void Card(string title, string description)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(title, 17, Ink, FontWeights.SemiBold));
        var body = Text(description, 14, Muted); body.Margin = new Thickness(0, 10, 0, 0); body.LineHeight = 23; stack.Children.Add(body);
        _content.Children.Add(new Border { Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 16), Child = stack });
    }

    private static TextBlock Text(string value, double size, Brush color, FontWeight? weight = null) => new()
    { Text = value, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(16, 12, 16, 12), Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += handler; return button;
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
