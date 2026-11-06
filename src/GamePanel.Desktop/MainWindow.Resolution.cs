using System.IO;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly GameResolutionProfiles _resolutionProfiles = new(Path.Combine(GamePanelPaths.Root, "Settings", "GameResolutions"));
    private GameResolutionState? _resolutionState;
    private GameResolutionPlan? _resolutionPlan;
    private GameResolutionOptions? _resolutionDraft;
    private ResolutionMethod? _resolutionMethod;
    private string? _resolutionGame, _resolutionDisplay, _resolutionReadError;
    private bool _resolutionPending;
    private Border? _resolutionPreview;
    private Button? _resolutionApply;
    private string _resolutionMessage = "Scegli una risoluzione del monitor e il metodo adatto al gioco.";
    private sealed record ResolutionChoice(ResolutionMethod? Method, string Label)
    {
        public override string ToString() => Label;
    }

    private void ResetResolution()
    {
        _resolutionState = null; _resolutionPlan = null; _resolutionDraft = null; _resolutionMethod = null;
        _resolutionGame = null; _resolutionDisplay = null; _resolutionReadError = null; _resolutionPending = false;
        _resolutionPreview = null; _resolutionApply = null;
        _resolutionMessage = "Scegli una risoluzione del monitor e il metodo adatto al gioco.";
    }
    private async Task RefreshResolutionAsync(string executable)
    {
        try
        {
            var data = await Task.Run(() => (State: _resolutionProfiles.Load(executable), Pending: _resolutionProfiles.HasPending(executable)));
            if (_closing || _report?.ExecutablePath != executable) return; _resolutionState = data.State; _resolutionPending = data.Pending; _resolutionReadError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { _resolutionReadError = e.Message; }
    }
    private void InvalidateResolution()
    {
        _resolutionPlan = null;
        if (_resolutionApply is not null) _resolutionApply.IsEnabled = false;
        if (_resolutionPreview is not null) _resolutionPreview.Visibility = Visibility.Collapsed;
    }
    private void RenderResolutionProfile(DetectedDisplay? display)
    {
        _resolutionPreview = null; _resolutionApply = null;
        if (_report is null) { Card("Risoluzione per gioco", "Analizza un gioco per salvare una richiesta di risoluzione o applicarla con un metodo compatibile."); return; }
        if (_resolutionReadError is not null) { Card("Profilo risoluzione da verificare", _resolutionReadError); return; }
        if (_resolutionPending)
        {
            Card("Operazione risoluzione interrotta", "Chiudi il gioco, poi completa l'applicazione oppure ripristina i file precedenti.");
            _content.Children.Add(FxButton("Completa risoluzione", "ResumeResolution", async (_, _) => await RecoverResolutionAsync(false), !_busy));
            _content.Children.Add(FxButton("Annulla e ripristina", "RollbackResolution", async (_, _) => await RecoverResolutionAsync(true), !_busy));
            return;
        }
        var body = new StackPanel();
        body.Children.Add(Text("Metodo di applicazione", 22, Ink, FontWeights.SemiBold));
        body.Children.Add(Text("Le impostazioni seguono il monitor selezionato. Il metodo applica impostazioni o parametri del gioco; non cambia la risoluzione di Windows e non sposta automaticamente la finestra su un altro monitor.", 12, Muted));
        if (display is null) { body.Children.Add(Text("Rileva prima uno schermo attivo.", 13, Muted)); _content.Children.Add(AssetPanel(body, 20)); return; }
        var modes = DisplayCatalog.Modes(display);
        if (_resolutionGame != _report.ExecutablePath)
        {
            _resolutionGame = _report.ExecutablePath; _resolutionDisplay = display.Id;
            _resolutionDraft = _resolutionState?.Options ?? new(ResolutionMethod.Ini, display.TargetWidth, display.TargetHeight, display.Id);
            if (_resolutionDraft.DisplayId != display.Id) _resolutionDraft = _resolutionDraft with { Width = display.TargetWidth, Height = display.TargetHeight, DisplayId = display.Id };
            _resolutionMethod = _resolutionState?.Options.Method;
            if (_resolutionState is null && _report.Sha256 == Underground1DisplayFix.ExecutableSha256)
            {
                if (SelectedIsRemix)
                {
                    _resolutionMethod = ResolutionMethod.Ini;
                    _resolutionDraft = _resolutionDraft with { Method = ResolutionMethod.Ini, ConfigPath = Path.Combine(Path.GetDirectoryName(_report.ExecutablePath)!, "scripts", "NFSUnderground.WidescreenFix.ini"), Section = "MAIN", WidthKey = "ResX", HeightKey = "ResY" };
                }
                else _resolutionMethod = ResolutionMethod.Underground1;
            }
            var root = Path.GetDirectoryName(_report.ExecutablePath)!;
            if (_resolutionState is null && _resolutionMethod is null && File.Exists(Path.Combine(root, "UnityPlayer.dll"))
                && Directory.Exists(Path.Combine(root, Path.GetFileNameWithoutExtension(_report.ExecutablePath) + "_Data"))) _resolutionMethod = ResolutionMethod.Unity;
        }
        if (_resolutionDisplay != display.Id)
        {
            _resolutionDisplay = display.Id;
            _resolutionDraft = _resolutionDraft! with { Width = display.TargetWidth, Height = display.TargetHeight, DisplayId = display.Id };
            InvalidateResolution();
        }
        var selected = modes.FirstOrDefault(m => m.Width == _resolutionDraft!.Width && m.Height == _resolutionDraft.Height)
            ?? modes.FirstOrDefault(m => m.Width == display.TargetWidth && m.Height == display.TargetHeight) ?? modes[0];
        _resolutionDraft = _resolutionDraft! with { Width = selected.Width, Height = selected.Height, DisplayId = display.Id };
        _screenResolutionHost!.Children.Clear();
        _screenResolutionHost.Children.Add(Text("RISOLUZIONE DEL GIOCO", 10, Muted, FontWeights.SemiBold));
        var mode = new ComboBox { ItemsSource = modes, SelectedItem = selected, IsEnabled = !_busy, Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(mode, "GameResolutionSize");
        UpdateMonitorResolution(selected.Width, selected.Height);
        mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is DisplayCatalog.ResolutionSize value) { _resolutionDraft = _resolutionDraft! with { Width = value.Width, Height = value.Height }; UpdateMonitorResolution(value.Width, value.Height); InvalidateResolution(); } };
        _screenResolutionHost.Children.Add(mode);
        var choices = new List<ResolutionChoice> { new(null, "Scegli il metodo compatibile") };
        if (_report.Sha256 == Underground1DisplayFix.ExecutableSha256 && !SelectedIsRemix) choices.Add(new(ResolutionMethod.Underground1, "Underground 1 · fix verificato"));
        choices.AddRange([new(ResolutionMethod.Unity, "Unity · parametri di avvio"), new(ResolutionMethod.Unreal, "Unreal · parametri di avvio"), new(ResolutionMethod.Ini, "Configurazione INI/CFG · personalizzata")]);
        var methodHost = new StackPanel(); methodHost.Children.Add(Text("METODO DI APPLICAZIONE", 10, Muted, FontWeights.SemiBold));
        var methodRow = new Grid { Margin = new Thickness(0, 16, 0, 0) }; methodRow.ColumnDefinitions.Add(new ColumnDefinition());
        methodRow.Children.Add(methodHost); body.Children.Add(methodRow);
        var method = new ComboBox { ItemsSource = choices, DisplayMemberPath = nameof(ResolutionChoice.Label), SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Method == _resolutionMethod)), IsEnabled = !_busy, Margin = new Thickness(0, 8, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(method, "GameResolutionMethod");
        method.SelectionChanged += (_, _) => { if (method.SelectedItem is ResolutionChoice value) { _resolutionMethod = value.Method; InvalidateResolution(); Render(); } };
        methodHost.Children.Add(method);
        if (SelectedIsUnderground1 && SelectedIsRemix) body.Children.Add(Text("UG1 Remix usa il proprio Widescreen Fix: vengono aggiornate soltanto ResX e ResY nell'INI esistente. Non viene installato il fix del gioco originale. Per conservare una risoluzione già funzionante, non applicare un nuovo profilo.", 12, Muted));
        if (_resolutionMethod == ResolutionMethod.Underground1)
            body.Children.Add(Text("Installa il Widescreen Fix UG1 verificato e salva le dimensioni in RES.cfg2. EXE, ReShade, lingua e salvataggi restano separati. La combinazione sul tuo PC richiede una prova nel gioco.", 12, Muted));
        else if (_resolutionMethod is ResolutionMethod.Unity or ResolutionMethod.Unreal)
            body.Children.Add(Text("Il motore e il gioco devono accettare questi parametri. La richiesta viene usata solo quando avvii questo EXE da GamePanel; il gioco può ignorarla o limitarla.", 12, Muted));
        else if (_resolutionMethod == ResolutionMethod.Ini)
        {
            var advanced = body;
            methodRow.ColumnDefinitions.Add(new ColumnDefinition()); methodHost.Margin = new Thickness(0, 0, 8, 0);
            var configHost = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(configHost, 1); methodRow.Children.Add(configHost);
            configHost.Children.Add(Text("FILE DI CONFIGURAZIONE", 10, Muted, FontWeights.SemiBold));
            var chooseConfig = FxButton("Seleziona configurazione", "ChooseResolutionIni", (_, _) =>
            {
                var picker = new OpenFileDialog { Title = "Configurazione di risoluzione del gioco", Filter = "Configurazioni (*.ini;*.cfg)|*.ini;*.cfg", CheckFileExists = true };
                if (picker.ShowDialog(this) == true) { _resolutionDraft = _resolutionDraft with { ConfigPath = picker.FileName }; InvalidateResolution(); Render(); }
            }, !_busy);
            SmallAction(chooseConfig, _resolutionDraft.ConfigPath is null ? "Seleziona configurazione" : Path.GetFileName(_resolutionDraft.ConfigPath), "folder");
            chooseConfig.ToolTip = _resolutionDraft.ConfigPath ?? "Scegli un file INI o CFG già presente"; chooseConfig.Margin = new Thickness(0, 8, 0, 14); configHost.Children.Add(chooseConfig);
            advanced.Children.Add(Text("Scegli il file che contiene già le chiavi della risoluzione. I nomi variano tra giochi; la sezione può restare vuota per le chiavi senza sezione.", 12, Muted));
            var fields = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-6, 8, -6, 0) }; advanced.Children.Add(fields);
            void Field(string caption, string value, string id, Action<string> change)
            {
                var field = new StackPanel { Margin = new Thickness(6, 0, 6, 0) }; fields.Children.Add(field);
                var text = Text(caption, 11, Muted); text.Margin = new Thickness(0, 10, 0, 6); field.Children.Add(text);
                var input = new TextBox { Text = value, Foreground = Ink, CaretBrush = Accent, Background = DesignSystem.Raised, BorderBrush = DesignSystem.Line, IsEnabled = !_busy, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6) };
                System.Windows.Automation.AutomationProperties.SetAutomationId(input, id);
                input.TextChanged += (_, _) => { change(input.Text.Trim()); InvalidateResolution(); }; field.Children.Add(input);
            }
            Field("Sezione", _resolutionDraft.Section, "ResolutionIniSection", v => _resolutionDraft = _resolutionDraft with { Section = v });
            Field("Chiave larghezza", _resolutionDraft.WidthKey, "ResolutionIniWidth", v => _resolutionDraft = _resolutionDraft with { WidthKey = v });
            Field("Chiave altezza", _resolutionDraft.HeightKey, "ResolutionIniHeight", v => _resolutionDraft = _resolutionDraft with { HeightKey = v });
        }
        var status = Text(_resolutionMessage, 12, Muted); status.Margin = new Thickness(0, 12, 0, 12); body.Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "GameResolutionStatus");
        if (_resolutionState is not null)
            body.Children.Add(Text($"Profilo salvato: {_resolutionState.Options.Width} × {_resolutionState.Options.Height}. È un'impostazione richiesta, non una misura del rendering in esecuzione.", 12, Ink));
        var peerPending = _fxState?.Pending == true || _screenState?.Pending == true || _assetState?.Pending == true;
        if (peerPending) body.Children.Add(Text("Completa o annulla l'operazione grafica precedente prima di applicare la risoluzione.", 12, Muted));
        var actions = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var prepareLabel = _resolutionMethod == ResolutionMethod.Underground1 ? "Prepara risoluzione e fix UG1" : "Prepara configurazione";
        var prepare = FxButton(prepareLabel, "PrepareGameResolution", async (_, _) => await PrepareResolutionAsync(false), !_busy && !peerPending && _resolutionMethod is not null);
        SmallAction(prepare, prepareLabel, "settings"); prepare.HorizontalAlignment = HorizontalAlignment.Stretch; prepare.Margin = new Thickness(0);
        actions.Children.Add(prepare);
        if (_resolutionMethod == ResolutionMethod.Underground1)
            actions.Children.Add(FxButton("Scegli ZIP del fix UG1", "ChooseUg1DisplayZip", async (_, _) => await PrepareResolutionAsync(false, true), !_busy && !peerPending));
        if (_resolutionState is not null) actions.Children.Add(FxButton("Prepara rimozione profilo", "RemoveGameResolution", async (_, _) => await PrepareResolutionAsync(true), !_busy && !peerPending));
        body.Children.Add(actions);
        var advancedPanel = AssetPanel(body, 22); advancedPanel.Margin = new Thickness(0);
        var advancedSettings = new Expander { Header = "Avanzate · configurazione del gioco", Content = advancedPanel, IsExpanded = true, Foreground = Ink, Margin = new Thickness(0, 0, 0, 22) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(advancedSettings, "ScreenAdvancedSettings"); StyleAdvanced(advancedSettings, roomy: true); _content.Children.Add(advancedSettings);
        if (_resolutionPlan is { } plan)
        {
            var preview = new StackPanel(); preview.Children.Add(Text(plan.Removing ? "Ripristino della risoluzione" : $"Anteprima · {plan.Options.Width} × {plan.Options.Height}", 20, Ink, FontWeights.SemiBold));
            preview.Children.Add(Text(plan.Changes.Count == 0 ? "Verrà salvato solo il profilo di avvio di questo gioco. Nessun file del gioco viene modificato."
                : string.Join("\n", plan.Changes.Select(c => (c.After is null ? "Rimuovi / ripristina · " : "Configura · ") + c.Path)), 12, Muted));
            if (plan.PreservedFiles.Count > 0) preview.Children.Add(Text("File cambiati fuori da GamePanel, che resteranno conservati:\n" + string.Join("\n", plan.PreservedFiles), 12, Muted));
            _resolutionApply = FxButton(plan.Removing ? "Conferma rimozione e ripristino" : "Applica al gioco", "ApplyGameResolution", async (_, _) => await ApplyResolutionAsync(), !_busy);
            preview.Children.Add(_resolutionApply); _resolutionPreview = AssetPanel(preview, 20); _content.Children.Add(_resolutionPreview);
        }
    }
    private async Task PrepareResolutionAsync(bool remove, bool local = false)
    {
        if (_report is null || _resolutionDraft is null || _resolutionMethod is null) return;
        var report = _report; var options = _resolutionDraft with { Method = _resolutionMethod.Value }; string? archive = null;
        if (local)
        {
            var picker = new OpenFileDialog { Title = "Archivio ufficiale NFSUnderground.WidescreenFix.zip", Filter = "Archivio ZIP (*.zip)|*.zip", CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return; archive = picker.FileName;
        }
        InvalidateResolution(); _screenPlan = null;
        await RunFxTaskAsync(async (token, _) =>
        {
            if (remove) return _resolutionProfiles.PrepareRemove(report);
            Underground1DisplayFix? package = null;
            if (options.Method == ResolutionMethod.Underground1) package = archive is null
                ? await Underground1DisplayFix.DownloadAsync(ReShadePackageSource.DefaultCache, token) : Underground1DisplayFix.Read(archive);
            token.ThrowIfCancellationRequested(); return _resolutionProfiles.Prepare(report, options, package);
        }, plan => { _resolutionPlan = plan; _resolutionMessage = "Anteprima pronta. Controlla le modifiche prima di applicare."; }, "Preparazione risoluzione del gioco");
    }
    private async Task ApplyResolutionAsync()
    {
        var plan = _resolutionPlan; var report = _report; if (plan is null || report is null) return;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_resolutionProfiles.Execute(plan, token, progress)), state =>
        {
            _resolutionState = state; _resolutionPlan = null;
            _resolutionMessage = state is null ? "Profilo rimosso. I file cambiati fuori da GamePanel sono stati conservati."
                : "Profilo applicato. Avvia il gioco e verifica la risoluzione nelle sue opzioni video.";
        }, "Applicazione risoluzione");
        await RefreshResolutionAsync(report.ExecutablePath); if (!_closing) Render();
    }
    private async Task RecoverResolutionAsync(bool rollback)
    {
        var report = _report; if (report is null) return;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_resolutionProfiles.Recover(report, rollback, token, progress)), state =>
        { _resolutionState = state; _resolutionPlan = null; _resolutionMessage = rollback ? "Impostazioni precedenti ripristinate." : "Profilo risoluzione completato."; }, "Recupero risoluzione");
        await RefreshResolutionAsync(report.ExecutablePath); if (!_closing) Render();
    }
}
