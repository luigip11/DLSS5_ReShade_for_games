using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly ReShadeProfileService _profiles = new(ReShadeProfileService.DefaultStore);
    private readonly ReShadePackageSource _components = new(ReShadePackageSource.DefaultCache);
    private ProfileStatus? _fxState;
    private ProfilePlan? _fxPlan;
    private SdrSettings _fxSettings = SdrSettings.Balanced;
    private string _fxMessage = "Seleziona speed2.exe per preparare il profilo SDR sperimentale.";
    private void ResetEffects()
    { _fxState = null; _fxPlan = null; _fxSettings = SdrSettings.Balanced; _fxMessage = "Analizza speed2.exe per preparare il profilo SDR sperimentale."; }

    private async Task RefreshEffectsAsync(string path)
    {
        try
        {
            var state = await Task.Run(() => _profiles.Status(path));
            if (_closing) return;
            _fxState = state; _fxSettings = state.State?.Installed == true ? state.State.Settings : SdrSettings.Balanced;
            _fxMessage = state.Message;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { _fxState = null; _fxMessage = "Stato profilo non leggibile: " + e.Message; }
    }

    private void RenderEffects()
    {
        Heading("La tua immagine. Il tuo stile.", "Underground 2 · ReShade SDR · sperimentale, da collaudare nel gioco.");
        var state = _fxState;
        Card(state?.Pending == true ? "Operazione da recuperare" : state?.State?.Installed == true ? "Profilo installato" : "Prepara il profilo", _fxMessage);
        var statusCard = (Border)_content.Children[_content.Children.Count - 1];
        var stateLabel = ((StackPanel)statusCard.Child).Children.OfType<TextBlock>().Last();
        System.Windows.Automation.AutomationProperties.SetAutomationId(stateLabel, "EffectsStatus");
        var reason = _report is null ? "Seleziona e analizza prima l'eseguibile del gioco." : ReShadeProfileService.Eligibility(_report);
        if (reason is not null) Card("Disponibilità", reason);
        if (state?.Pending == true)
        {
            var recovery = new WrapPanel();
            recovery.Children.Add(FxButton("Riprendi operazione", "ResumeProfile", async (_, _) => await RecoverProfileAsync(false), !_busy));
            recovery.Children.Add(FxButton("Annulla e recupera stato precedente", "RollbackProfile", async (_, _) => await RecoverProfileAsync(true), !_busy));
            _content.Children.Add(recovery);
            Details("Prima di riprendere", "Chiudi il gioco. Il recupero verifica EXE, copie e file presenti. Un file cambiato da un altro strumento blocca ulteriori modifiche."); return;
        }
        var choices = new WrapPanel { Margin = new Thickness(0, 18, 0, 4) };
        var original = FxButton("Originale", "OriginalProfile", (_, _) => SelectSettings(SdrSettings.Original), !_busy);
        var balanced = FxButton("Bilanciato", "BalancedProfile", (_, _) => SelectSettings(SdrSettings.Balanced), !_busy);
        if (_fxSettings == SdrSettings.Original) original.BorderBrush = Accent;
        if (_fxSettings == SdrSettings.Balanced) balanced.BorderBrush = Accent;
        choices.Children.Add(original); choices.Children.Add(balanced); _content.Children.Add(choices);
        var enabled = new CheckBox { Content = "Effetti attivi nel preset", IsChecked = _fxSettings.Enabled, Foreground = Ink, Margin = new Thickness(0, 4, 0, 18), IsEnabled = !_busy };
        enabled.Checked += (_, _) => { _fxSettings = _fxSettings with { Enabled = true }; InvalidateFxPlan(); };
        enabled.Unchecked += (_, _) => { _fxSettings = _fxSettings with { Enabled = false }; InvalidateFxPlan(); };
        _content.Children.Add(enabled);
        AddFxSlider("Nitidezza", "Definizione leggera dei dettagli; riduci se compaiono aloni.", "Sharpness", _fxSettings.Sharpness, value => _fxSettings = _fxSettings with { Sharpness = value });
        AddFxSlider("Colore", "50 mantiene la saturazione originale.", "Color", _fxSettings.Color, value => _fxSettings = _fxSettings with { Color = value });
        AddFxSlider("Intensità", "Quanto mescolare il risultato con l'immagine originale.", "Intensity", _fxSettings.Intensity, value => _fxSettings = _fxSettings with { Intensity = value });
        var prepare = FxButton(state?.State?.Installed == true ? "Prepara aggiornamento" : "Scarica ReShade e prepara anteprima", "PrepareProfile", async (_, _) => await PrepareProfileAsync(false), !_busy && reason is null);
        prepare.Background = Accent; prepare.Foreground = BackgroundBrush; prepare.BorderBrush = Accent;
        _content.Children.Add(prepare);
        if (state?.State?.Installed == true)
            _content.Children.Add(FxButton("Anteprima rimozione ReShade", "PrepareRemoveProfile", async (_, _) => await PrepareProfileAsync(true), !_busy));
        var preview = new StackPanel();
        System.Windows.Automation.AutomationProperties.SetAutomationId(preview, "ProfilePreview"); _content.Children.Add(preview);
        if (_fxPlan is not null)
        {
            var title = _fxPlan.Removing ? "Rimozione pronta" : "Modifiche pronte";
            preview.Children.Add(Text(title, 18, Ink, FontWeights.SemiBold));
            preview.Children.Add(Text(_fxPlan.Changes.Count == 0 ? "I file sono già nello stato richiesto."
                : string.Join("\n", _fxPlan.Changes.Select(c => $"{(c.AfterSha256 is null ? "Rimuovi" : c.BeforeSha256 is null ? "Aggiungi" : "Aggiorna")} · {c.Path}")), 13, Muted));
            if (_fxPlan.PreservedFiles.Count > 0) preview.Children.Add(Text("Rimangono perché modificati: " + string.Join(", ", _fxPlan.PreservedFiles), 13, Muted));
            var apply = FxButton(_fxPlan.Removing ? "Conferma rimozione" : "Applica profilo", "ApplyProfile", async (_, _) => await ApplyProfileAsync(), !_busy);
            apply.Margin = new Thickness(0, 14, 0, 18); apply.Background = Accent; apply.Foreground = BackgroundBrush; preview.Children.Add(apply);
        }
        Details("Cosa viene scaricato e modificato", "Download esplicito ReShade 6.8.0 da reshade.me, verificato con SHA-256 fissati nella build. Il setup non viene eseguito. Si installano d3d9.dll x86, ReShade.ini, GamePanelPreset.ini e lo shader GamePanelFX/GamePanelSDR.fx. Nessun EXE del gioco viene modificato. Le DLL preesistenti bloccano l'installazione; gli ASI/widescreen fix restano da collaudare insieme al profilo.");
        Details("Uso e ritorno all'originale", "Applica con il gioco chiuso e poi avvialo dalla Panoramica. Dopo ogni modifica ai parametri riavvia il gioco. Originale disattiva l'effetto; Rimozione elimina il loader e i file gestiti rimasti identici. Configurazioni/preset modificati da ReShade o altri strumenti vengono conservati e segnalati. Home apre il pannello ReShade; Bloc Scorr attiva/disattiva gli effetti.");
        Details("HDR e Windows", "Questo profilo è solo SDR: non converte il gioco in HDR e non abilita DLSS. ReShade può contattare GitHub per controllare aggiornamenti quando il gioco parte. Il suo certificato pubblicato non costituisce una firma attendibile per Windows: Smart App Control può bloccare i componenti. GamePanel non cambia protezioni o certificati del PC.");
    }
    private Button FxButton(string label, string id, RoutedEventHandler handler, bool enabled)
    {
        var button = Button(label, handler); button.IsEnabled = enabled;
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, id); return button;
    }
    private void SelectSettings(SdrSettings settings) { _fxSettings = settings; _fxPlan = null; Render(); }
    private void InvalidateFxPlan()
    {
        _fxPlan = null;
        var container = _content.Children.OfType<StackPanel>().FirstOrDefault(p => System.Windows.Automation.AutomationProperties.GetAutomationId(p) == "ProfilePreview");
        container?.Children.Clear();
    }
    private void AddFxSlider(string name, string description, string id, int value, Action<int> update)
    {
        var row = new DockPanel(); var number = Text(value.ToString(), 14, Accent, FontWeights.SemiBold);
        DockPanel.SetDock(number, Dock.Right); row.Children.Add(number); row.Children.Add(Text(name, 14, Ink, FontWeights.SemiBold)); _content.Children.Add(row);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10,
            Margin = new Thickness(0, 7, 0, 4), IsEnabled = !_busy, Foreground = Accent };
        System.Windows.Automation.AutomationProperties.SetName(slider, name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(slider, id);
        slider.ValueChanged += (_, _) => { var v = (int)Math.Round(slider.Value); update(v); number.Text = v.ToString(); InvalidateFxPlan(); };
        _content.Children.Add(slider); var hint = Text(description, 12, Muted); hint.Margin = new Thickness(0, 0, 0, 18); _content.Children.Add(hint);
    }
    private async Task PrepareProfileAsync(bool remove)
    {
        var report = _report; if (report is null) return;
        var settings = _fxSettings; _fxPlan = null;
        await RunFxTaskAsync(async (token, progress) =>
        {
            if (remove) return _profiles.PrepareRemove(report, token);
            var component = await _components.PrepareAsync(token, progress);
            return _profiles.PrepareApply(report, component, settings, token);
        }, plan => { _fxPlan = plan; _fxMessage = "Anteprima pronta. Controlla l'elenco prima di confermare."; }, "Preparazione profilo · nessuna modifica al gioco");
    }
    private async Task ApplyProfileAsync()
    {
        var plan = _fxPlan; if (plan is null || _report is null) return;
        await RunFxTaskAsync((token, progress) =>
        {
            if (!plan.Removing && _fxState?.State?.Installed != true) _backups.Create(_report, token, progress);
            var state = _profiles.Execute(plan, token, progress);
            return Task.FromResult(state);
        }, state =>
        {
            _fxState = new(state, false, ""); _fxPlan = null;
            _fxMessage = state.Installed ? "Profilo applicato. Avvia o riavvia il gioco per provarlo."
                : "ReShade rimosso." + (state.PreservedFiles.Count == 0 ? "" : " Conservati file modificati: " + string.Join(", ", state.PreservedFiles));
        }, "Applicazione delle modifiche approvate");
    }
    private async Task RecoverProfileAsync(bool rollback)
    {
        if (_report is null) return; var path = _report.ExecutablePath;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_profiles.Recover(path, rollback, token, progress)), state =>
        {
            _fxState = new(state, false, ""); _fxSettings = state?.Settings ?? SdrSettings.Balanced; _fxPlan = null;
            _fxMessage = rollback ? "Operazione annullata: stato precedente recuperato." : "Operazione completata e verificata.";
        }, rollback ? "Recupero dello stato precedente" : "Ripresa operazione interrotta");
    }
    private async Task RunFxTaskAsync<T>(Func<CancellationToken, IProgress<BackupProgress>, Task<T>> work, Action<T> completed, string message)
    {
        if (_busy) return;
        _busy = true; _path.IsReadOnly = true; _select.IsEnabled = false; _analyze.IsEnabled = false; _export.IsEnabled = false;
        _cancel.Content = "Interrompi operazione"; _cancel.Visibility = Visibility.Visible; _status.Text = message;
        _cancellation = new CancellationTokenSource(); var operation = _cancellation; Render();
        var progress = new Progress<BackupProgress>(p => { if (!_closing && _busy && ReferenceEquals(_cancellation, operation)) _status.Text = $"{p.Message} · {p.Completed}/{p.Total}"; });
        try
        {
            var result = await Task.Run(() => work(operation.Token, progress), operation.Token);
            if (!_closing) { completed(result); _status.Text = "Operazione profilo completata"; }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or HttpRequestException or BadImageFormatException)
        {
            if (!_closing)
            {
                _fxPlan = null;
                if (_report is not null) await RefreshEffectsAsync(_report.ExecutablePath);
                _fxMessage = e is OperationCanceledException ? "Operazione interrotta. Se il journal è aperto, riprendi o annulla il recupero." : e.Message;
                _status.Text = "Operazione profilo non completata";
            }
        }
        finally
        {
            operation.Dispose(); _cancellation = null; _busy = false;
            if (!_closing)
            {
                _path.IsReadOnly = false; _select.IsEnabled = true; _analyze.IsEnabled = !string.IsNullOrWhiteSpace(_path.Text); _export.IsEnabled = _report is not null;
                _cancel.Content = "Annulla analisi"; _cancel.Visibility = Visibility.Collapsed; Render();
            }
        }
    }
}
