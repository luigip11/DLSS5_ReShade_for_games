using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly SoftwareUpdates _softwareUpdates = new();
    private Button _updatesButton = null!;
    private bool _checkingUpdates;
    private CancellationTokenSource? _checkUpdatesCancellation;
    private async Task CheckSoftwareUpdatesAsync()
    {
        if (_checkingUpdates || _busy) return;
        _checkingUpdates = true; _updatesButton.IsEnabled = false; _updatesButton.Content = "Controllo…";
        _checkUpdatesCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var result = await _softwareUpdates.CheckAsync(AppVersion.Current, _checkUpdatesCancellation.Token);
            if (_closing) return;
            if (result.Update is null)
            {
                MessageBox.Show(this, result.PublishedReleasesExist ? "Software già aggiornato." : "Non ci sono ancora release aggiornabili pubblicate su GitHub.", "GamePanel · aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this, $"È disponibile GamePanel {result.Update.Version}.\n\nVuoi scaricarlo e aggiornare questa copia? GamePanel si chiuderà e verrà riaperto. Libreria, backup e componenti scaricati restano nei dati utente.",
                "GamePanel · aggiornamento disponibile", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (_busy) { MessageBox.Show(this, "Termina l'operazione in corso e ripeti il controllo."); return; }
            await InstallSoftwareUpdateAsync(result.Update);
        }
        catch (OperationCanceledException) { if (!_closing) MessageBox.Show(this, "Controllo interrotto o connessione scaduta. Nessuna modifica all'app.", "GamePanel · aggiornamenti"); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or JsonException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException)
        { if (!_closing) MessageBox.Show(this, "Impossibile controllare gli aggiornamenti.\n\n" + e.Message, "GamePanel · aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally
        {
            _checkUpdatesCancellation?.Dispose(); _checkUpdatesCancellation = null; _checkingUpdates = false;
            if (!_closing) { _updatesButton.Content = "Controlla aggiornamenti"; _updatesButton.IsEnabled = !_busy; }
        }
    }
    private async Task InstallSoftwareUpdateAsync(SoftwareRelease release)
    {
        _busy = true; _path.IsReadOnly = true; _select.IsEnabled = false; _analyze.IsEnabled = false; _export.IsEnabled = false;
        _cancel.Content = "Annulla download"; _cancel.Visibility = Visibility.Visible; _status.Text = "Preparazione aggiornamento " + release.Version;
        var operation = new CancellationTokenSource(); _cancellation = operation; Render(); var handedOff = false;
        var progress = new Progress<BackupProgress>(p => { if (!_closing && ReferenceEquals(_cancellation, operation)) _status.Text = $"{p.Message} · {p.Completed}/{p.Total} KiB"; });
        try
        {
            var prepared = await _softwareUpdates.PrepareAsync(release, Path.Combine(GamePanelPaths.Root, "Updates"), operation.Token, progress);
            if (_closing) return;
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var install = new UpdateInstallation(directory, prepared.ReadyDirectory, GamePanelPaths.Root);
            await Task.Run(() => { install.Preview(operation.Token); install.ProbeWriteAccess(); }, operation.Token);
            var helper = Path.Combine(prepared.ReadyDirectory, "GamePanel.Updater.exe");
            using var self = Process.GetCurrentProcess();
            var request = new UpdateRequest(1, directory, prepared.ReadyDirectory, GamePanelPaths.Root, self.Id, self.StartTime.ToUniversalTime().Ticks, release.Version, true);
            var requestPath = Path.Combine(prepared.Folder, "request.json");
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), operation.Token);
            var start = new ProcessStartInfo(helper) { WorkingDirectory = prepared.ReadyDirectory, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--request"); start.ArgumentList.Add(requestPath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("L'updater non è stato avviato.");
            handedOff = true;
        }
        catch (OperationCanceledException) { if (!_closing) _status.Text = "Download annullato · app invariata"; }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or JsonException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!_closing)
            {
                _status.Text = "Aggiornamento non applicato";
                MessageBox.Show(this, e.Message + (e is UnauthorizedAccessException ? "\n\nSe la cartella di GamePanel è protetta, riaprilo come amministratore per aggiornare questa copia." : "")
                    + "\n\nL'app e i dati utente non sono stati sostituiti.", "GamePanel · aggiornamento", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            operation.Dispose(); _cancellation = null; _busy = false;
            if (handedOff) Application.Current.Shutdown();
            else if (!_closing)
            {
                _path.IsReadOnly = false; _select.IsEnabled = true; _analyze.IsEnabled = !string.IsNullOrWhiteSpace(_path.Text); _export.IsEnabled = _report is not null;
                _cancel.Content = "Annulla analisi"; _cancel.Visibility = Visibility.Collapsed; Render();
            }
        }
    }
}
