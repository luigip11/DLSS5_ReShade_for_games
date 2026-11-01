using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly DirectXTextureProfileService _directX = new(ReShadeProfileService.DefaultStore);
    private readonly DirectXTextureSource _directXSource = new();
    private readonly DirectXTextureReplacementService _directXReplacements = new(ReShadeProfileService.DefaultStore);
    private DirectXTextureStatus? _directXState;
    private DirectXTexturePlan? _directXPlan;
    private IReadOnlyList<DirectXTextureCapture> _directXCaptures = [];
    private IReadOnlyList<DirectXTextureReplacement> _directXInstalled = [];
    private string _directXMessage = "Prepara il componente, poi verifica una texture nel gioco.";
    private string _directXSelectedHash = "";
    private string _directXReplacementPath = "";
    private bool _directXReadFailed;
    private string _directXCaptureNote = "";
    private bool _directXReplacementPending;

    private bool CanPrepareDirectX => !_busy && _report is not null && !_directXReadFailed
        && !_directXReplacementPending && _directXState is not null && _directXState.Pending != true && _assetState?.Pending != true
        && _fxState?.Pending != true && _screenState?.Pending != true;

    private void ResetDirectXTextures()
    {
        _directXState = null; _directXPlan = null; _directXCaptures = []; _directXInstalled = [];
        _directXSelectedHash = ""; _directXReplacementPath = ""; _directXReadFailed = false; _directXCaptureNote = ""; _directXReplacementPending = false;
        _directXMessage = "Prepara il componente, poi verifica una texture nel gioco.";
    }

    private async Task RefreshDirectXTexturesAsync(string executablePath)
    {
        var report = _report;
        try
        {
            var snapshot = await Task.Run(() => ReadDirectXUiSnapshot(executablePath, report));
            if (_closing) return;
            ApplyDirectXUiSnapshot(snapshot);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _directXReadFailed = true; _directXCaptures = []; _directXInstalled = [];
            _directXMessage = "Stato DirectX da verificare: " + e.Message;
        }
    }

    private sealed record DirectXUiSnapshot(DirectXTextureStatus State, DirectXTextureCaptureScan Scan, IReadOnlyList<DirectXTextureReplacement> Installed, bool ReplacementPending);

    private DirectXUiSnapshot ReadDirectXUiSnapshot(string executablePath, GameReport? report, CancellationToken token = default)
    {
        var state = _directX.Status(executablePath);
        var scan = state.Installed && !state.Pending ? DirectXTextureData.InspectCaptures(state.ResourceRoot, token) : new([], [], false);
        var replacementPending = state.Installed && !state.Pending && report is not null
            && _directXReplacements.HasPending(report, state.ResourceRoot);
        IReadOnlyList<DirectXTextureReplacement> installed = state.Installed && !state.Pending && !replacementPending && report is not null
            && string.Equals(report.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)
            ? _directXReplacements.ListInstalled(report, state.ResourceRoot, token) : [];
        return new(state, scan, installed, replacementPending);
    }

    private void ApplyDirectXUiSnapshot(DirectXUiSnapshot snapshot)
    {
        _directXState = snapshot.State; _directXCaptures = snapshot.Scan.Captures; _directXInstalled = snapshot.Installed;
        _directXReadFailed = false; _directXMessage = snapshot.State.Message; _directXReplacementPending = snapshot.ReplacementPending;
        _directXCaptureNote = (snapshot.Scan.Warnings.Count > 0 ? $"{snapshot.Scan.Warnings.Count} file esclusi perché non verificabili. " : "")
            + (snapshot.Scan.WasLimited ? "Elenco manuale limitato. La preparazione automatica esegue una scansione separata e mostra gli eventuali limiti nel riepilogo." : "");
        if (!_directXCaptures.Any(c => c.Hash == _directXSelectedHash)) _directXSelectedHash = "";
    }

    private void RenderDirectXTextures()
    {
        var body = new StackPanel();
        body.Children.Add(DesignSystem.Chip("DIRECTX 9 · SPERIMENTALE", Accent));
        var title = Text("Diagnosi e sostituzioni manuali", 24, Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(title);
        body.Children.Add(Text("Texture Toolkit intercetta le texture tramite DirectX. Il gioco mantiene il suo eseguibile; il componente va ancora verificato nella tua copia di Underground 2.", 13, Muted));
        var message = Text(_directXMessage, 13, Ink);
        message.Margin = new Thickness(0, 12, 0, 12);
        System.Windows.Automation.AutomationProperties.SetAutomationId(message, "DirectXTextureStatus");
        body.Children.Add(message);
        if (_directXReadFailed)
        {
            body.Children.Add(FxButton("Rileggi stato DirectX", "RereadDirectXTextures", async (_, _) => await RefreshDirectXCaptureListAsync(), !_busy && _report is not null));
            _content.Children.Add(AssetPanel(body, 20)); return;
        }


        if (_directXState?.Pending == true)
        {
            body.Children.Add(Text("Operazione interrotta. Chiudi il gioco, poi riprendi o recupera i file precedenti.", 13, Muted));
            var recovery = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            recovery.Children.Add(FxButton("Riprendi operazione DirectX", "ResumeDirectXTextures", async (_, _) => await RecoverDirectXTexturesAsync(false), !_busy && _report is not null));
            recovery.Children.Add(FxButton("Annulla e recupera i file", "RollbackDirectXTextures", async (_, _) => await RecoverDirectXTexturesAsync(true), !_busy && _report is not null));
            body.Children.Add(recovery); _content.Children.Add(AssetPanel(body, 20)); return;
        }

        if (_directXReplacementPending)
        {
            body.Children.Add(Text("Una sostituzione DDS è rimasta interrotta. Chiudi il gioco e recupera l'operazione prima di applicare altre modifiche.", 13, Muted));
            var recovery = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            recovery.Children.Add(FxButton("Riprendi sostituzione DDS", "ResumeDirectXReplacement", async (_, _) => await RecoverDirectXReplacementAsync(false), !_busy && _report is not null));
            recovery.Children.Add(FxButton("Annulla e recupera DDS", "RollbackDirectXReplacement", async (_, _) => await RecoverDirectXReplacementAsync(true), !_busy && _report is not null));
            body.Children.Add(recovery); _content.Children.Add(AssetPanel(body, 20)); return;
        }

        var reason = _report is null ? "Seleziona e analizza Underground 2 dalla libreria." : DirectXTextureCatalog.CheckGame(_report);
        if (reason is null && _screenState?.State?.Installed != true)
            reason = "Applica prima il Widescreen Fix gestito nella pagina Schermo: viene usato il suo ASI loader.";
        if (reason is null && _assetState?.State?.Packs.Any(p => p.Manifest.Kind == "Textures") == true)
            reason = "Rimuovi prima i pacchetti texture TexWizard dalla sezione I tuoi pacchetti.";
        if (_directXState?.Installed != true)
        {
            var prepare = FxButton("Prepara texture DirectX", "PrepareDirectXTextures", async (_, _) => await PrepareDirectXTexturesAsync(), CanPrepareDirectX && reason is null);
            prepare.Background = Accent; prepare.Foreground = BackgroundBrush; prepare.BorderBrush = Accent;
            body.Children.Add(prepare);
            if (reason is not null) { var hint = Text(reason, 12, Muted); hint.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(hint); }
            body.Children.Add(Text("Il download prepara un'anteprima. L'installazione richiede una conferma; nessuna texture viene sostituita al primo avvio.", 12, Muted));
        }
        else
        {
            body.Children.Add(DesignSystem.Chip(_directXState.InjectionEnabled == true ? "SOSTITUZIONI ATTIVE" : "PRIMA PROVA · ACQUISIZIONE", Accent));
            var steps = Text("Hai già usato Dump all textures? Usa «Prepara tutte le texture» in alto.\nPer una sostituzione manuale: premi INSERT nel gioco, riconosci una superficie con Blink e salva la DDS. Chiudi il gioco, aggiorna le catture e associa il tuo DDS alla superficie scelta.", 13, Muted);
            steps.LineHeight = 22; steps.Margin = new Thickness(0, 12, 0, 12); body.Children.Add(steps);
            var actions = new WrapPanel();
            actions.Children.Add(FxButton("Aggiorna catture", "RefreshDirectXCaptures", async (_, _) => await RefreshDirectXCaptureListAsync(), CanPrepareDirectX));
            actions.Children.Add(FxButton("Apri cartella texture", "OpenDirectXTextureFolder", (_, _) => OpenDirectXTextureFolder(), !_busy && !_directXReadFailed));
            actions.Children.Add(FxButton("Prepara rimozione componente", "PrepareRemoveDirectXTextures", async (_, _) => await PrepareDirectXRemovalAsync(), CanPrepareDirectX));
            body.Children.Add(actions);
            if (_directXState.InjectionEnabled == true)
                body.Children.Add(FxButton("Prepara disattivazione sostituzioni", "DisableDirectXInjection", async (_, _) => await PrepareDirectXSettingsAsync(false), CanPrepareDirectX));
            else
                body.Children.Add(FxButton("Prepara attivazione sostituzioni", "EnableDirectXInjection", async (_, _) => await PrepareDirectXSettingsAsync(true), CanPrepareDirectX && _directXInstalled.Count > 0));
            var counts = Text($"{_directXCaptures.Count} catture verificate · {_directXInstalled.Count} sostituzioni gestite", 12, Muted);
            counts.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(counts);
            if (_directXCaptureNote.Length > 0) { var note = Text(_directXCaptureNote, 12, Muted); note.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(note); }

        }
        _content.Children.Add(AssetPanel(body, 20));
        RenderDirectXPlan();
        if (_directXState?.Installed == true && !_directXReadFailed) RenderDirectXCaptureSelection();
    }

    private void RenderDirectXPlan()
    {
        if (_directXPlan is not { } plan) return;
        var body = new StackPanel(); body.Children.Add(DesignSystem.Chip("ANTEPRIMA DIRECTX", Accent));
        var title = Text(plan.Action, 19, Ink, FontWeights.SemiBold); title.Margin = new Thickness(0, 10, 0, 8); body.Children.Add(title);
        body.Children.Add(Text(plan.Changes.Count == 0 ? "I file sono già nello stato richiesto." : string.Join("\n", plan.Changes.Select(c =>
            $"{(c.AfterSha256 is null ? "Rimuovi" : c.BeforeSha256 is null ? "Aggiungi" : "Aggiorna")} · {c.Path}")), 12, Muted));
        var hint = Text("Le catture e le texture personali restano nella cartella dati di GamePanel. Chiudi il gioco prima di confermare.", 12, Muted);
        hint.Margin = new Thickness(0, 12, 0, 12); body.Children.Add(hint);
        var actions = new WrapPanel();
        var apply = FxButton("Conferma modifiche DirectX", "ApplyDirectXTexturePlan", async (_, _) => await ApplyDirectXPlanAsync(), CanPrepareDirectX);
        apply.Background = Accent; apply.Foreground = BackgroundBrush; apply.BorderBrush = Accent; actions.Children.Add(apply);
        actions.Children.Add(FxButton("Scarta anteprima", "DiscardDirectXTexturePlan", (_, _) => { _directXPlan = null; _directXMessage = "Anteprima DirectX scartata."; Render(); }, !_busy));
        body.Children.Add(actions); _content.Children.Add(AssetPanel(body, 20));
    }

    private void RenderDirectXCaptureSelection()
    {
        if (_directXCaptures.Count == 0)
        {
            Card("Catture per la diagnosi manuale", "Non ci sono ancora catture DDS verificate. Salvale dal pannello INSERT durante il gioco. La preparazione automatica usa tutte le catture compatibili senza richiedere una selezione individuale.");
            return;
        }
        var body = new StackPanel(); body.Children.Add(Text("Una superficie alla volta", 20, Ink, FontWeights.SemiBold));
        var description = Text("Scegli la cattura che hai riconosciuto nel gioco. La sostituzione sarà legata al suo contenuto, anche se l'eseguibile usa un layout diverso da TexWizard.", 13, Muted);
        description.Margin = new Thickness(0, 8, 0, 12); body.Children.Add(description);
        var picker = new ComboBox { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(10), MinHeight = 40,
            Background = DesignSystem.Panel, Foreground = Ink, BorderBrush = DesignSystem.Line, IsEnabled = CanPrepareDirectX };
        System.Windows.Automation.AutomationProperties.SetAutomationId(picker, "DirectXCapturedTexture");
        System.Windows.Automation.AutomationProperties.SetName(picker, "Texture acquisita da sostituire");
        foreach (var capture in _directXCaptures)
            picker.Items.Add(new ComboBoxItem { Tag = capture.Hash, Content = $"{capture.Hash} · {capture.Info.Width} × {capture.Info.Height} · {capture.Info.Format}", Foreground = Ink, Background = DesignSystem.Panel });
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, _directXSelectedHash, StringComparison.Ordinal));
        body.Children.Add(picker);
        var selected = Text(_directXSelectedHash.Length == 0 ? "Seleziona esplicitamente una cattura prima di associare il DDS." : "Texture scelta: " + _directXSelectedHash, 12, Muted);
        selected.Margin = new Thickness(0, 0, 0, 10); body.Children.Add(selected);
        var replacement = Text(_directXReplacementPath.Length == 0 ? "Nessun DDS sostitutivo selezionato." : "DDS selezionato: " + Path.GetFileName(_directXReplacementPath), 12, Muted);
        body.Children.Add(replacement);
        var previews = CreateDirectXPreviewPair(out var originalPreview, out var replacementPreview);
        previews.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(previews);
        _ = PopulateDirectXPreviewsAsync(originalPreview, replacementPreview, _directXSelectedHash, _directXReplacementPath);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var choose = FxButton("Scegli DDS sostitutivo", "ChooseDirectXReplacement", (_, _) => SelectDirectXReplacement(), CanPrepareDirectX);
        var install = FxButton("Associa e salva sostituzione", "InstallDirectXReplacement", async (_, _) => await InstallDirectXReplacementAsync(), CanPrepareDirectX && _directXSelectedHash.Length > 0 && _directXReplacementPath.Length > 0);
        install.Background = Accent; install.Foreground = BackgroundBrush;
        var remove = FxButton("Rimuovi sostituzione scelta", "RemoveDirectXReplacement", async (_, _) => await RemoveDirectXReplacementAsync(), CanPrepareDirectX && _directXInstalled.Any(r => r.Hash == _directXSelectedHash));
        picker.SelectionChanged += (_, _) =>
        {
            _directXSelectedHash = (picker.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            selected.Text = _directXSelectedHash.Length == 0 ? "Seleziona esplicitamente una cattura prima di associare il DDS." : "Texture scelta: " + _directXSelectedHash;
            install.IsEnabled = CanPrepareDirectX && _directXSelectedHash.Length > 0 && _directXReplacementPath.Length > 0;
            remove.IsEnabled = CanPrepareDirectX && _directXInstalled.Any(r => r.Hash == _directXSelectedHash);
            _ = PopulateDirectXPreviewsAsync(originalPreview, replacementPreview, _directXSelectedHash, _directXReplacementPath);
        };
        actions.Children.Add(choose);
        actions.Children.Add(FxButton("Usa asfalto GamePanel 2K", "ChooseDirectXAsphaltSample", async (_, _) => await SelectDirectXAsphaltSampleAsync(), CanPrepareDirectX));
        actions.Children.Add(install); actions.Children.Add(remove); body.Children.Add(actions);
        var hint = Text("Salva una sola associazione per la prima prova. Il DDS viene copiato nei dati di GamePanel; l'attivazione si prepara dal riquadro DirectX. Questa funzione modifica le texture, mentre le geometrie richiedono modelli dedicati.", 12, Muted);
        hint.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(hint); _content.Children.Add(AssetPanel(body, 20));
    }

    private async Task PrepareDirectXTexturesAsync()
    {
        var report = _report; if (!CanPrepareDirectX || report is null) return;
        ClearAssetAndDirectXPlans();
        await RunFxTaskAsync(async (token, progress) =>
        {
            var component = await _directXSource.PrepareAsync(token, progress);
            return _directX.PrepareInstall(report, component, token);
        }, plan => { _directXPlan = plan; _directXMessage = "Componente verificato in cache. Controlla l'anteprima prima dell'installazione sperimentale."; }, "Preparazione texture DirectX · nessuna modifica al gioco");
    }

    private async Task PrepareDirectXRemovalAsync()
    {
        var report = _report; if (!CanPrepareDirectX || report is null) return;
        ClearAssetAndDirectXPlans();
        await RunFxTaskAsync((token, _) => Task.FromResult(_directX.PrepareRemove(report, token)), plan =>
        { _directXPlan = plan; _directXMessage = "Rimozione pronta. Catture e sostituzioni personali saranno conservate."; }, "Preparazione rimozione componente DirectX");
    }

    private async Task PrepareDirectXSettingsAsync(bool enabled)
    {
        var report = _report; if (!CanPrepareDirectX || report is null) return;
        if (enabled && _directXInstalled.Count == 0) return;
        ClearAssetAndDirectXPlans();
        await RunFxTaskAsync((token, _) => Task.FromResult(_directX.PrepareSettings(report, enabled, token)), plan =>
        { _directXPlan = plan; _directXMessage = enabled ? "Attivazione delle sostituzioni pronta. Conferma e poi avvia il gioco per la prova." : "Disattivazione pronta. Conferma per tornare alla sola acquisizione."; }, "Preparazione parametri DirectX");
    }

    private async Task ApplyDirectXPlanAsync()
    {
        var plan = _directXPlan; var report = _report; if (!CanPrepareDirectX || plan is null || report is null) return;
        _directXPlan = null;
        await RunFxTaskAsync((token, progress) =>
        {
            if (plan.Changes.Count > 0) _backups.Create(report, token, progress);
            _directX.Execute(plan, token, progress);
            return Task.FromResult(_directX.Status(report.ExecutablePath));
        }, state => { _directXState = state; _directXMessage = state.Installed ? "Componente DirectX pronto. Avvia normalmente il gioco per verificare il pannello INSERT e una singola superficie." : "Componente DirectX rimosso. Catture e sostituzioni personali conservate."; }, "Applicazione delle modifiche DirectX approvate");
    }

    private async Task RecoverDirectXTexturesAsync(bool rollback)
    {
        var report = _report; if (_busy || report is null) return;
        _directXPlan = null;
        await RunFxTaskAsync((token, progress) =>
        {
            _directX.Recover(report.ExecutablePath, rollback, token, progress);
            return Task.FromResult(_directX.Status(report.ExecutablePath));
        }, state => { _directXState = state; _directXReadFailed = false; _directXMessage = rollback ? "Stato DirectX precedente recuperato." : "Operazione DirectX completata."; }, "Recupero operazione DirectX");
    }

    private async Task RefreshDirectXCaptureListAsync()
    {
        var report = _report;
        if (_busy || report is null || _assetState?.Pending == true || _fxState?.Pending == true || _screenState?.Pending == true) return;
        _directXPlan = null;
        await RunFxTaskAsync((token, _) => Task.FromResult(ReadDirectXUiSnapshot(report.ExecutablePath, report, token)), snapshot =>
        {
            ApplyDirectXUiSnapshot(snapshot);
            if (snapshot.State.Installed)
                _directXMessage = snapshot.Scan.Captures.Count == 0 ? "Nessuna cattura verificata: salva le DDS dal pannello INSERT durante una gara." : $"{snapshot.Scan.Captures.Count} catture verificate. Usa la preparazione automatica in alto oppure scegli una superficie per una sostituzione manuale.";
        }, "Lettura catture DirectX · nessuna modifica al gioco");
    }

    private void OpenDirectXTextureFolder()
    {
        if (_busy || _directXReadFailed || _directXState?.Installed != true) return;
        try
        {
            var root = _directXState.ResourceRoot;
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("La cartella texture non esiste ancora. Avvia il gioco dopo l'installazione del componente.");
            Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        { _directXMessage = e.Message; Render(); }
    }

    private void SelectDirectXReplacement()
    {
        if (!CanPrepareDirectX) return;
        var picker = new OpenFileDialog { Title = "Seleziona una texture DDS sostitutiva", Filter = "Texture DDS (*.dds)|*.dds", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        _directXReplacementPath = picker.FileName; _directXPlan = null; Render();
    }

    private async Task SelectDirectXAsphaltSampleAsync()
    {
        if (!CanPrepareDirectX) return;
        await RunFxTaskAsync((_, _) => Task.FromResult(DirectXAsphaltSample.GetVerifiedPath()), path =>
        { _directXReplacementPath = path; _directXPlan = null; _directXMessage = "Asfalto GamePanel 2K selezionato. Scegli una cattura riconosciuta dell'asfalto prima di associare la texture."; }, "Verifica texture asfalto GamePanel 2K");
    }

    private async Task RecoverDirectXReplacementAsync(bool rollback)
    {
        var report = _report; var state = _directXState;
        if (_busy || report is null || state?.Installed != true) return;
        _directXPlan = null;
        await RunFxTaskAsync((token, _) =>
        {
            _directXReplacements.Recover(report, state.ResourceRoot, rollback, token);
            return Task.FromResult(ReadDirectXUiSnapshot(report.ExecutablePath, report, token));
        }, snapshot => { ApplyDirectXUiSnapshot(snapshot); _directXMessage = rollback ? "Sostituzione DDS annullata e stato precedente recuperato." : "Sostituzione DDS completata."; }, "Recupero sostituzione DDS");
    }

    private async Task InstallDirectXReplacementAsync()
    {
        var report = _report; var state = _directXState; var hash = _directXSelectedHash; var path = _directXReplacementPath;
        if (!CanPrepareDirectX || report is null || state?.Installed != true || hash.Length == 0 || path.Length == 0) return;
        var prompt = "Associare «" + Path.GetFileName(path) + "» alla texture " + hash + "?\n\nLa copia verrà salvata nei dati di GamePanel. Chiudi il gioco prima di continuare.";
        if (MessageBox.Show(this, prompt, "Conferma associazione texture", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        ClearAssetAndDirectXPlans();
        await RunFxTaskAsync((token, _) =>
        {
            _directXReplacements.Install(report, state.ResourceRoot, hash, path, token);
            return Task.FromResult(_directXReplacements.ListInstalled(report, state.ResourceRoot, token));
        }, installed => { _directXInstalled = installed; _directXReplacementPath = ""; _directXMessage = "Sostituzione salvata. Prepara l'attivazione dal riquadro DirectX, poi verifica una gara."; }, "Salvataggio sostituzione DDS nei dati di GamePanel");
    }

    private async Task RemoveDirectXReplacementAsync()
    {
        var report = _report; var state = _directXState; var hash = _directXSelectedHash;
        if (!CanPrepareDirectX || report is null || state?.Installed != true || hash.Length == 0) return;
        if (MessageBox.Show(this, "Rimuovere la sostituzione gestita per " + hash + "? La cattura originale viene conservata.", "Conferma rimozione texture", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        ClearAssetAndDirectXPlans();
        await RunFxTaskAsync((token, _) =>
        {
            _directXReplacements.Remove(report, state.ResourceRoot, hash, token);
            return Task.FromResult(_directXReplacements.ListInstalled(report, state.ResourceRoot, token));
        }, installed => { _directXInstalled = installed; _directXMessage = "Sostituzione rimossa. La cattura originale è conservata."; }, "Rimozione sostituzione DDS gestita");
    }

    private void ClearAssetAndDirectXPlans()
    { _directXPlan = null; _assetPlan = null; _assetPackage = null; }
}
