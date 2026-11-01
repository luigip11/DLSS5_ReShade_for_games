using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly DirectXTextureBatchService _automaticTextures = new(ReShadeProfileService.DefaultStore);
    private readonly SmolModelSource _smolSource = new();
    private readonly SmolModelPackBuilder _smolBuilder = new();
    private PreparedSmolModels? _preparedSmol;
    private AssetPlan? _smolPlan;
    private string _smolMessage = "Smol parziale: correzioni ai modelli originali di Golf, IS300 e Sentra.";
    private PreparedDirectXBatch? _preparedTextures;
    private bool _acceptLimitedTextures;
    private string _automaticTextureMessage = "Le catture già salvate possono essere preparate tutte insieme, senza scegliere ogni hash.";

    private void ResetAutomaticAssets()
    {
        _preparedTextures = null; _acceptLimitedTextures = false;
        _automaticTextureMessage = "Le catture già salvate possono essere preparate tutte insieme, senza scegliere ogni hash.";
        ResetSmolModels();
    }

    private void RenderAutomaticTextures()
    {
        var body = new StackPanel();
        body.Children.Add(DesignSystem.Chip("PREPARA · CONTROLLA · INSTALLA", Accent));
        var title = Text("Texture automatiche dalle catture", 24, Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(title);
        body.Children.Add(Text("Ingrandimento 2× delle DDS catturate fino a 512 × 512, con uscita fino a 1024 × 1024. Conserva contenuto e trasparenza; è interpolazione, non AI, nuovi materiali o geometrie. Ogni hash viene associato alla propria cattura.", 13, Muted));
        var status = Text(_automaticTextureMessage, 13, Ink); status.Margin = new Thickness(0, 12, 0, 12);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "AutomaticTextureStatus"); body.Children.Add(status);
        var installed = _directXState?.Installed == true;
        if (!installed) body.Children.Add(Text("Installa prima il componente con «Prepara texture DirectX» nel riquadro seguente. Le catture già presenti restano disponibili.", 12, Muted));
        if (_preparedTextures is { } prepared)
        {
            var summary = Text($"{prepared.FilesCount} texture pronte · {prepared.OutputBytes / (1024d * 1024d):0.0} MiB · {prepared.SkippedCount} escluse" + (prepared.WasLimited ? " · PREPARAZIONE PARZIALE" : ""), 14, Ink, FontWeights.SemiBold);
            summary.Margin = new Thickness(0, 8, 0, 8); body.Children.Add(summary);
            System.Windows.Automation.AutomationProperties.SetAutomationId(summary, "AutomaticTextureSummary");
            if (prepared.Warnings.Count > 0)
            {
                var warnings = Text(string.Join("\n", prepared.Warnings), 12, Muted);
                body.Children.Add(new ScrollViewer { Content = warnings, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            }
            if (prepared.WasLimited)
            {
                var accept = new CheckBox { Content = "Accetto il lotto parziale; le sostituzioni non verranno attivate automaticamente.", IsChecked = _acceptLimitedTextures, Foreground = Ink, Margin = new Thickness(0, 10, 0, 10), IsEnabled = !_busy };
                System.Windows.Automation.AutomationProperties.SetAutomationId(accept, "AcceptLimitedAutomaticTextures");
                accept.Checked += (_, _) => { _acceptLimitedTextures = true; Render(); };
                accept.Unchecked += (_, _) => { _acceptLimitedTextures = false; Render(); };
                body.Children.Add(accept);
            }
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(FxButton("Prepara tutte le texture", "PrepareAllDirectXTextures", async (_, _) => await PrepareAllDirectXTexturesAsync(), CanPrepareDirectX && installed));
        var install = FxButton("Installa texture preparate", "InstallAllDirectXTextures", async (_, _) => await InstallAllDirectXTexturesAsync(), CanPrepareDirectX && installed && _preparedTextures is { FilesCount: > 0 } && (!_preparedTextures.WasLimited || _acceptLimitedTextures));
        install.Background = Accent; install.Foreground = BackgroundBrush; install.BorderBrush = Accent; actions.Children.Add(install); body.Children.Add(actions);
        var hint = Text("La preparazione usa la cache di GamePanel e non modifica il gioco. Controlla quantità, spazio ed esclusioni, poi chiudi il gioco e installa. Un lotto completo viene anche attivato; l'annullamento conserva le texture già installate e permette di riprendere.", 12, Muted);
        hint.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(hint);
        _content.Children.Add(AssetPanel(body, 20));
    }

    private async Task PrepareAllDirectXTexturesAsync()
    {
        var report = _report; var state = _directXState;
        if (!CanPrepareDirectX || report is null || state?.Installed != true) return;
        ClearAssetAndDirectXPlans(); _preparedTextures = null; _acceptLimitedTextures = false;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_automaticTextures.Prepare(report, state.ResourceRoot, token, progress)), prepared =>
        {
            _preparedTextures = prepared;
            _automaticTextureMessage = prepared.FilesCount == 0 ? "Nessuna cattura compatibile da preparare. Controlla le esclusioni e la cartella dump."
                : "Texture pronte in cache. Controlla il riepilogo prima di installarle tutte.";
        }, "Preparazione di tutte le texture catturate · nessuna modifica al gioco");
    }

    private async Task InstallAllDirectXTexturesAsync()
    {
        var report = _report; var state = _directXState; var prepared = _preparedTextures;
        if (!CanPrepareDirectX || report is null || state?.Installed != true || prepared is null || prepared.FilesCount == 0 || (prepared.WasLimited && !_acceptLimitedTextures)) return;
        ClearAssetAndDirectXPlans();
        await RunFxTaskAsync((token, progress) =>
        {
            var result = _automaticTextures.Install(report, state.ResourceRoot, prepared, token, progress);
            var activated = false;
            if (!result.Cancelled && !token.IsCancellationRequested && !prepared.WasLimited && result.InstalledCount + result.AlreadyInstalledCount == result.TotalCount)
            {
                var plan = _directX.PrepareSettings(report, true, token);
                if (plan.Changes.Count > 0) _backups.Create(report, token, progress);
                _directX.Execute(plan, token, progress); activated = true;
            }
            return Task.FromResult((Result: result, Activated: activated, Snapshot: ReadDirectXUiSnapshot(report.ExecutablePath, report)));
        }, completed =>
        {
            ApplyDirectXUiSnapshot(completed.Snapshot);
            _automaticTextureMessage = completed.Result.Cancelled
                ? $"Operazione interrotta: {completed.Result.InstalledCount} installate, {completed.Result.AlreadyInstalledCount} già presenti. Premi Installa per riprendere; nessuna attivazione automatica."
                : $"{completed.Result.InstalledCount} texture installate · {completed.Result.AlreadyInstalledCount} già presenti. " + (completed.Activated ? "Sostituzioni attive: avvia il gioco per verificarle." : prepared.WasLimited ? "Lotto parziale salvato; attivazione automatica non eseguita." : "Attivazione non completata; controlla lo stato DirectX prima di riprendere.");
        }, "Installazione di tutte le texture preparate · chiudi il gioco");
    }

    private void ResetSmolModels()
    {
        _preparedSmol = null; _smolPlan = null;
        _smolMessage = "Smol parziale: correzioni ai modelli originali di Golf, IS300 e Sentra.";
    }

    private void RenderSmolModels()
    {
        var body = new StackPanel(); body.Children.Add(DesignSystem.Chip("SMOL PARZIALE · TRE AUTO", Accent));
        var title = Text("Correzioni ai modelli auto", 23, Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(title);
        body.Children.Add(Text("Golf · Lexus IS300 · Nissan Sentra. Correzioni mirate alle geometrie originali, con le texture locali conservate. Nessun pacchetto high poly, città o personaggi. Gli altri contenuti dell'archivio Smol non vengono installati.", 13, Muted));
        var status = Text(_smolMessage, 13, Ink); status.Margin = new Thickness(0, 12, 0, 12);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "SmolModelStatus"); body.Children.Add(status);
        if (_smolPlan is { } plan)
        {
            var summary = Text($"Anteprima · {plan.Changes.Count(c => c.BeforeSha256 != c.AfterSha256)} modifiche · tre auto", 14, Ink, FontWeights.SemiBold);
            summary.Margin = new Thickness(0, 0, 0, 8); body.Children.Add(summary);
            var files = Text(string.Join("\n", plan.Changes.Select(c => c.Path)), 12, Muted);
            body.Children.Add(new ScrollViewer { Content = files, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        var actions = new WrapPanel();
        actions.Children.Add(FxButton("Scarica e prepara correzioni auto", "PrepareSmolModels", async (_, _) => await PrepareSmolModelsAsync(null), CanPrepareAssets));
        actions.Children.Add(FxButton("Scegli archivio Smol", "SelectSmolArchive", async (_, _) => await SelectSmolArchiveAsync(), CanPrepareAssets));
        var install = FxButton("Installa correzioni auto", "InstallSmolModels", async (_, _) => await InstallSmolModelsAsync(), CanPrepareAssets && _preparedSmol is not null && _smolPlan is not null);
        install.Background = Accent; install.Foreground = BackgroundBrush; install.BorderBrush = Accent; actions.Children.Add(install); body.Children.Add(actions);
        body.Children.Add(FxButton("Fonte autore · Smol Model Fix", "OpenSmolModelSource", (_, _) => OpenAssetSource(SmolModelSource.SourceUrl), !_busy));
        var hint = Text("Se il download non è disponibile, seleziona il RAR originale già scaricato: dimensione e SHA-256 devono coincidere con la versione verificata. La preparazione non modifica il gioco. Dopo aver controllato l'anteprima, chiudi il gioco e installa; gli originali vengono salvati per il ripristino.", 12, Muted);
        hint.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(hint); _content.Children.Add(AssetPanel(body, 20));
    }

    private async Task SelectSmolArchiveAsync()
    {
        if (!CanPrepareAssets) return;
        var picker = new OpenFileDialog { Title = "Seleziona il RAR originale Smol Model Fix", Filter = "Archivio Smol (*.rar)|*.rar", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) == true) await PrepareSmolModelsAsync(picker.FileName);
    }

    private async Task PrepareSmolModelsAsync(string? archivePath)
    {
        var report = _report; if (!CanPrepareAssets || report is null) return;
        ClearAssetAndDirectXPlans(); _preparedSmol = null; _smolPlan = null;
        await RunFxTaskAsync(async (token, progress) =>
        {
            var archive = archivePath ?? await _smolSource.PrepareAsync(token, progress);
            var prepared = _smolBuilder.Prepare(report, archive, token, progress);
            SmolModelPackBuilder.VerifyForInstall(report, prepared, token);
            var plan = _assets.PrepareInstall(report, prepared.Package, token: token);
            return (Prepared: prepared, Plan: plan);
        }, result =>
        {
            _preparedSmol = result.Prepared; _smolPlan = result.Plan;
            _smolMessage = "Archivio e originali verificati. Controlla i file delle tre auto prima di installare le correzioni.";
        }, "Preparazione correzioni Smol · nessuna modifica al gioco");
    }

    private async Task InstallSmolModelsAsync()
    {
        var report = _report; var prepared = _preparedSmol; var plan = _smolPlan;
        if (!CanPrepareAssets || report is null || prepared is null || plan is null) return;
        ClearAssetAndDirectXPlans();
        await RunFxTaskAsync((token, progress) =>
        {
            SmolModelPackBuilder.VerifyForInstall(report, prepared, token);
            if (plan.Changes.Count > 0) _backups.Create(report, token, progress);
            SmolModelPackBuilder.VerifyForInstall(report, prepared, token);
            return Task.FromResult(_assets.Execute(plan, token, progress));
        }, state =>
        {
            _assetState = new(state, false, ""); _preparedSmol = null; _smolPlan = null;
            _smolMessage = "Correzioni Smol installate e verificate per Golf, IS300 e Sentra. Avvia il gioco per controllarle; il pacchetto è gestibile qui sotto.";
            _assetMessage = _smolMessage;
        }, "Installazione correzioni delle tre auto · copie degli originali verificate");
    }
}
