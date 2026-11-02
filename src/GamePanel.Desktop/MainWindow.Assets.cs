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
    private readonly AssetProfileService _assets = new(ReShadeProfileService.DefaultStore);
    private readonly AssetPackageImporter _assetImporter = new(Path.Combine(GamePanelPaths.Root, "AssetPacks"));
    private readonly TextureRuntimeSource _assetSource = new();
    private AssetStatus? _assetState;
    private AssetPlan? _assetPlan;
    private PreparedAssetPack? _assetPackage;
    private string _assetMessage = "Seleziona un gioco dalla libreria per preparare texture e modelli.";
    private string _assetArchivePath = "";
    private string _assetPlanConfirmation = "Applica pacchetto";
    private Button? _assetApplyButton;
    private Border? _assetPreviewPanel;
    private TextBlock? _assetStatusText;
    private int _assetsTab;

    private void ResetAssets()
    {
        _assetsTab = 0; _assetsAdvancedExpanded = false;
        _assetState = null; _assetPlan = null; _assetPackage = null; _assetArchivePath = "";
        _assetMessage = "Analizza il gioco per preparare texture e modelli.";
        _assetPlanConfirmation = "Applica pacchetto";
        _assetsTab = 0;
        ResetAutomaticAssets();
        ResetDirectXTextures();
    }

    private async Task RefreshAssetsAsync(string executablePath)
    {
        try
        {
            var state = await Task.Run(() => _assets.Status(executablePath));
            if (!_closing) { _assetState = state; _assetMessage = state.Message; }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { _assetState = null; _assetMessage = "Stato asset non leggibile: " + e.Message; }
        await RefreshDirectXTexturesAsync(executablePath);
    }

    private bool CanPrepareAssets => !_busy && _report is not null && _assetState?.Pending != true
        && _fxState?.Pending != true && _screenState?.Pending != true && _directXState?.Pending != true && !_directXReplacementPending;

    private void RenderAssets()
    {
        _assetApplyButton = null;
        _assetPreviewPanel = null;
        Heading("Texture e modelli", "Underground 2 · preparazione automatica · installazione verificata.");
        var state = Text(_assetMessage, 13, Muted);
        _assetStatusText = state;
        System.Windows.Automation.AutomationProperties.SetAutomationId(state, "AssetStatus");
        if (_automaticTextureFeedback == AssetFeedback.Success && _assetMessage == _automaticTextureMessage)
            _content.Children.Add(AssetFeedbackCard(AssetFeedback.Success, _automaticTextureFeedbackTitle, state));
        else if (_smolFeedback == AssetFeedback.Success && _assetMessage == _smolMessage)
            _content.Children.Add(AssetFeedbackCard(AssetFeedback.Success, _smolFeedbackTitle, state));
        else _content.Children.Add(AssetPanel(state, 14));

        if (_assetState?.Pending == true)
        {
            Card("Operazione da recuperare", "Il journal conserva le modifiche interrotte. Chiudi il gioco e scegli se completarle o recuperare lo stato precedente.");
            var recovery = new WrapPanel();
            recovery.Children.Add(FxButton("Riprendi operazione asset", "ResumeAssets", async (_, _) => await RecoverAssetsAsync(false), !_busy && _report is not null));
            recovery.Children.Add(FxButton("Annulla e recupera i file", "RollbackAssets", async (_, _) => await RecoverAssetsAsync(true), !_busy && _report is not null));
            _content.Children.Add(recovery);
            Details("Recupero verificato", "Prima di scrivere, GamePanel ricontrolla l'eseguibile, le copie e i file presenti. Se un altro strumento ha modificato un file, l'operazione si ferma.");
            return;
        }

        if (_report is null) Card("Scegli il gioco", "Apri Libreria e seleziona la tua copia. L'anteprima sarà legata all'hash dell'eseguibile analizzato.");
        if (_fxState?.Pending == true || _screenState?.Pending == true)
            Card("Prima completa il recupero", "C'è un'operazione interrotta in HDR / FX o Schermo. Riprendila o annullala nella sua pagina prima di modificare gli asset.");

        if (_directXState?.Pending == true || _directXReplacementPending)
        {
            RenderDirectXTextures();
            return;
        }

        var available = CaptureAssetContent(RenderAvailableAssets);
        var installed = CaptureAssetContent(() =>
        {
            if (_assetsTab == 1) RenderAssetPreview();
            RenderInstalledAssets();
            RenderAssetHelp();
        });
        _content.Children.Add(CreateAssetTabs(available, installed));
    }

    private void RenderAvailableAssets()
    {
        RenderAutomaticTextures();
        RenderSmolModels();
        RenderModelSourceCards();
        var advancedContent = CaptureAssetContent(RenderAdvancedAssets);
        var legacyTextures = _assetState?.State?.Packs.Any(p => p.Manifest.Kind == "Textures") == true;
        var needsSetup = _directXState?.Installed != true && !legacyTextures;
        var advanced = new Expander
        {
            Header = "Strumenti avanzati · componenti e importazione manuale",
            Content = advancedContent, Foreground = Ink, BorderBrush = DesignSystem.Line,
            Margin = new Thickness(0, 4, 0, 16), Padding = new Thickness(12),
            IsExpanded = _assetsAdvancedExpanded || needsSetup || _directXPlan is not null || _assetPlan is not null
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(advanced, "AssetsAdvancedTools");
        advanced.Expanded += (_, _) => _assetsAdvancedExpanded = true;
        advanced.Collapsed += (_, _) => _assetsAdvancedExpanded = false;
        _content.Children.Add(advanced);
    }

    private bool _assetsAdvancedExpanded;

    private void RenderAdvancedAssets()
    {
        RenderDirectXTextures();

        var builtin = new StackPanel();
        builtin.Children.Add(DesignSystem.Chip("PERCORSO TEXWIZARD · BUILD SPECIFICHE", Accent));
        var title = Text("Asfalto 2K", 22, Ink, FontWeights.SemiBold); title.Margin = new Thickness(0, 12, 0, 8); builtin.Children.Add(title);
        builtin.Children.Add(Text("Tre superfici stradali originali a 2048 × 2048. Un intervento mirato sull'asfalto: gli altri materiali e la geometria conservano il dettaglio del gioco.", 13, Muted));
        var builtinReason = _report is null ? "Seleziona e analizza Underground 2."
            : TextureRuntimeCatalog.CheckGame(_report);
        if (_directXState?.Installed == true)
            builtinReason = "Il percorso DirectX è installato. Rimuovilo prima di usare il runtime TexWizard.";
        if (builtinReason is null && _screenState?.State?.Installed != true)
            builtinReason = "Applica prima il Widescreen Fix gestito nella pagina Schermo: le texture condividono il suo ASI loader.";
        var builtinInstalled = _assetState?.State?.Packs.Any(p => p.Manifest.Id == "asphalt-hd") == true;
        var download = FxButton(builtinInstalled ? "Asfalto 2K installato · gestiscilo in I tuoi pacchetti" : "Scarica asfalto 2K e prepara anteprima", "DownloadAsphaltPack", async (_, _) => await PrepareBuiltinAssetAsync(), CanPrepareAssets && builtinReason is null && !builtinInstalled);
        download.Margin = new Thickness(0, 14, 0, 0); download.Background = Accent; download.Foreground = BackgroundBrush; download.BorderBrush = Accent;
        builtin.Children.Add(download);
        if (builtinReason is not null) { var hint = Text(builtinReason, 12, Muted); hint.Margin = new Thickness(0, 10, 0, 0); builtin.Children.Add(hint); }
        _content.Children.Add(AssetPanel(builtin, 20));

        RenderAssetImport();
        if (_assetsTab == 0) RenderAssetPreview();
    }

    private void RenderAssetHelp()
    {
        Details("Texture, modelli e immagine SDR", "Le texture automatiche ingrandiscono le catture esistenti; Smol corregge solo tre modelli originali. Il profilo HDR / FX regola invece l'immagine SDR. Il pack asfalto non trasforma tutti gli elementi in 2K. Verifica in gioco ogni modifica dopo l'installazione.");
        Details("Compatibilità e formati", "Importazione manuale solo da ZIP con GamePanelAssetPack.json, elenco preciso dei file, dimensioni e SHA-256. Sono ammessi dati texture e coppie GEOMETRY.BIN / TEXTURES.BIN per auto esistenti. Il percorso Smol legge esclusivamente il RAR originale verificato; i suoi convertitori e script non vengono eseguiti. Altri RAR, TPF, END e installer non sono supportati.");
        Details("Origine, licenza e ritorno all'originale", "Autore e URL dei pacchetti importati sono dichiarazioni del manifest, non una verifica dell'identità o della licenza. Controlla i diritti d'uso e le istruzioni del pack. Ogni applicazione richiede una conferma dell'anteprima; le copie verificate permettono di disattivare o rimuovere un pack. File cambiati da altri strumenti bloccano le modifiche. Chiudi il gioco prima di applicare e riavvialo per verificare il risultato.");
    }

    private void RenderModelSourceCards()
    {
        var body = new StackPanel(); body.Children.Add(DesignSystem.Chip("GEOMETRIE CITTÀ · PERCORSO SEPARATO", Accent));
        var title = Text("Città RTX Remix", 21, Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(title);
        var city = Text("Underground 2 Remix richiede un profilo renderer RTX Remix separato e l'archivio 0.4 verificato. Non è installabile da questo riquadro. Nessun pacchetto personaggi documentato è disponibile.", 13, Muted);
        city.Margin = new Thickness(0, 12, 0, 6); body.Children.Add(city);
        var sources = new WrapPanel();
        sources.Children.Add(FxButton("Progetto autore · Underground 2 Remix", "OpenCityRemixSource", (_, _) => OpenAssetSource("https://www.moddb.com/mods/need-for-speed-underground-2-remix"), !_busy));
        sources.Children.Add(FxButton("Repository asset RTX Remix", "OpenCityRemixRepository", (_, _) => OpenAssetSource("https://github.com/Ekozmaster/NFSU2-RTX-Remix"), !_busy));
        sources.Children.Add(FxButton("Analizza archivio Remix 0.4", "AnalyzeRemixArchive", async (_, _) => await AnalyzeRemixArchiveAsync(), !_busy));
        sources.Children.Add(FxButton("Esporta componenti per verifica", "ExportRemixComponents", async (_, _) => await ExportRemixComponentsAsync(), !_busy));
        body.Children.Add(sources);
        body.Children.Add(Text(_remixAuditMessage, 12, Muted));
        var hint = Text("Le fonti si aprono nel browser; gli strumenti leggono il tuo archivio locale. Nessuna mod viene installata da questo riquadro.", 12, Muted);
        hint.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(hint); _content.Children.Add(AssetPanel(body, 20));
    }

    private void OpenAssetSource(string url)
    {
        if (_busy) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        { _assetMessage = "Impossibile aprire la fonte: " + e.Message; Render(); }
    }

    private void RenderAssetImport()
    {
        var body = new StackPanel();
        body.Children.Add(Text("Il tuo pacchetto", 18, Ink, FontWeights.SemiBold));
        var intro = Text("Importa uno ZIP di texture o modelli con manifest GamePanel. L'analisi prepara l'elenco delle modifiche; applicale solo dopo averlo controllato.", 13, Muted);
        intro.Margin = new Thickness(0, 8, 0, 12); body.Children.Add(intro);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var archive = new TextBox
        {
            Text = _assetArchivePath, Foreground = Ink, Background = DesignSystem.Panel, BorderBrush = DesignSystem.Line,
            Padding = new Thickness(11, 10, 11, 10), MinHeight = 42, IsReadOnly = _busy,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            ToolTip = "Percorso dello ZIP con GamePanelAssetPack.json"
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(archive, "AssetPackagePath");
        System.Windows.Automation.AutomationProperties.SetName(archive, "Percorso pacchetto ZIP");
        var analyze = FxButton("Analizza pacchetto", "ImportAssetPackage", async (_, _) => await ImportAssetAsync(), CanPrepareAssets && !string.IsNullOrWhiteSpace(_assetArchivePath));
        archive.TextChanged += (_, _) =>
        {
            _assetArchivePath = archive.Text;
            if (_assetPlan is not null)
            {
                _assetPlan = null; _assetPackage = null;
                if (_assetPreviewPanel is not null) _assetPreviewPanel.Visibility = Visibility.Collapsed;
                _assetMessage = "Percorso cambiato. Analizza il pacchetto per preparare una nuova anteprima.";
                if (_assetStatusText is not null) _assetStatusText.Text = _assetMessage;
            }
            analyze.IsEnabled = CanPrepareAssets && !string.IsNullOrWhiteSpace(_assetArchivePath);
        };
        row.Children.Add(archive);
        var select = FxButton("Scegli ZIP", "SelectAssetPackage", (_, _) => SelectAssetPackage(), !_busy);
        select.Margin = new Thickness(8, 0, 0, 0); select.MinHeight = 42; Grid.SetColumn(select, 1); row.Children.Add(select); body.Children.Add(row);
        analyze.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(analyze);
        var note = Text("Texture: Underground 2 compatibile e Widescreen Fix gestito richiesti. Modelli: solo sostituzioni di file auto esistenti, senza download del loader texture.", 12, Muted);
        note.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(note);
        _content.Children.Add(AssetPanel(body, 20));
    }

    private void RenderAssetPreview()
    {
        if (_assetPlan is null) return;
        var plan = _assetPlan;
        var body = new StackPanel();
        body.Children.Add(DesignSystem.Chip("ANTEPRIMA · NESSUN FILE DEL GIOCO MODIFICATO", Accent));
        var name = Text(plan.Action + " · " + plan.PackName, 21, Ink, FontWeights.SemiBold); name.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(name);
        if (_assetPackage is not null)
        {
            var manifest = _assetPackage.Manifest;
            body.Children.Add(Text((manifest.Kind == "Textures" ? "Texture" : "Modelli") + " · " + manifest.Files.Count + " file dichiarati", 13, Muted));
            var origin = Text("METADATI DICHIARATI · AUTORE NON VERIFICATO", 10, Muted, FontWeights.SemiBold); origin.Margin = new Thickness(0, 12, 0, 5); body.Children.Add(origin);
            body.Children.Add(Text("Autore: " + manifest.Author + "\nOrigine: " + manifest.SourceUrl, 12, Muted));
            var hash = Text("SHA-256 archivio: " + _assetPackage.ArchiveSha256, 11, Muted); hash.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(hash);
        }
        var additions = plan.Changes.Count(c => c.BeforeSha256 is null && c.AfterSha256 is not null);
        var replacements = plan.Changes.Count(c => c.BeforeSha256 is not null && c.AfterSha256 is not null);
        var removals = plan.Changes.Count(c => c.AfterSha256 is null);
        var summary = Text($"{additions} aggiunte · {replacements} sostituzioni · {removals} rimozioni", 13, Ink, FontWeights.SemiBold);
        summary.Margin = new Thickness(0, 14, 0, 8); body.Children.Add(summary);
        var changes = Text(plan.Changes.Count == 0 ? "I file sono già nello stato richiesto."
            : string.Join("\n", plan.Changes.Select(c => $"{(c.AfterSha256 is null ? "Rimuovi / ripristina" : c.BeforeSha256 is null ? "Aggiungi" : "Sostituisci")} · {c.Path}")), 12, Muted);
        changes.LineHeight = 20;
        var scroll = new ScrollViewer { Content = changes, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        System.Windows.Automation.AutomationProperties.SetAutomationId(scroll, "AssetPlanFiles"); body.Children.Add(scroll);
        var confirm = Text("Conferma solo le modifiche elencate. Le sostituzioni conservano copie verificate degli originali; l'eseguibile analizzato e i file verranno ricontrollati prima della scrittura.", 12, Muted);
        confirm.Margin = new Thickness(0, 12, 0, 12); body.Children.Add(confirm);
        var actions = new WrapPanel();
        var apply = FxButton(_assetPlanConfirmation, "ApplyAssetPlan", async (_, _) => await ApplyAssetPlanAsync(), CanPrepareAssets);
        apply.Content = new TextBlock { Text = _assetPlanConfirmation, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
        apply.Background = Accent; apply.Foreground = BackgroundBrush; apply.BorderBrush = Accent; _assetApplyButton = apply; actions.Children.Add(apply);
        actions.Children.Add(FxButton("Scarta anteprima", "DiscardAssetPlan", (_, _) => { _assetPlan = null; _assetPackage = null; _assetMessage = "Anteprima scartata. Nessun file del gioco modificato."; Render(); }, !_busy));
        body.Children.Add(actions); _assetPreviewPanel = AssetPanel(body, 20); _content.Children.Add(_assetPreviewPanel);
    }

    private void RenderInstalledAssets()
    {
        var packs = _assetState?.State?.Packs;
        if (packs is null || packs.Count == 0)
        {
            Card("Nessun pacchetto installato", "Prepara texture o correzioni dalla scheda Da installare. Qui potrai gestire i pacchetti applicati e ripristinare gli originali.");
            return;
        }
        var heading = Text("I tuoi pacchetti", 20, Ink, FontWeights.SemiBold); heading.Margin = new Thickness(0, 0, 0, 12); _content.Children.Add(heading);
        foreach (var pack in packs)
        {
            var body = new StackPanel();
            body.Children.Add(Text(pack.Manifest.Name, 17, Ink, FontWeights.SemiBold));
            var detail = Text((pack.Manifest.Kind == "Textures" ? "Texture" : "Modelli") + " · " + (pack.Enabled ? "Attivo" : "Disattivato") + " · " + pack.Manifest.Files.Count + " file", 12, Muted);
            detail.Margin = new Thickness(0, 7, 0, 8); body.Children.Add(detail);
            body.Children.Add(Text("Autore dichiarato: " + pack.Manifest.Author + "\nOrigine dichiarata: " + pack.Manifest.SourceUrl, 12, Muted));
            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var id = pack.Manifest.Id; var enabled = pack.Enabled; var name = pack.Manifest.Name;
            actions.Children.Add(FxButton(enabled ? "Prepara disattivazione" : "Prepara attivazione", "ToggleAsset-" + id,
                async (_, _) => await PrepareAssetToggleAsync(id, name, !enabled), CanPrepareAssets));
            actions.Children.Add(FxButton("Prepara rimozione", "RemoveAsset-" + id,
                async (_, _) => await PrepareAssetRemoveAsync(id, name), CanPrepareAssets));
            body.Children.Add(actions); _content.Children.Add(AssetPanel(body, 18));
        }
    }

    private static Border AssetPanel(UIElement child, double padding) => new()
    {
        Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 16), Child = child
    };

    private void SelectAssetPackage()
    {
        if (_busy) return;
        var picker = new OpenFileDialog { Title = "Seleziona un pacchetto asset GamePanel", Filter = "Pacchetti ZIP (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        _assetArchivePath = picker.FileName; _assetPlan = null; _assetPackage = null; Render();
    }

    private async Task ImportAssetAsync()
    {
        _directXPlan = null;
        var report = _report; var path = _assetArchivePath.Trim().Trim('"');
        if (!CanPrepareAssets || report is null || path.Length == 0) return;
        _assetPlan = null; _assetPackage = null;
        var widescreenInstalled = _screenState?.State?.Installed == true;
        await RunFxTaskAsync(async (token, progress) =>
        {
            var package = _assetImporter.Import(path, token, progress);
            var plan = await PrepareImportedAssetAsync(report, package, widescreenInstalled, token, progress);
            return (Package: package, Plan: plan);
        }, prepared =>
        {
            _assetPackage = prepared.Package; _assetPlan = prepared.Plan;
            _assetPlanConfirmation = "Conferma applicazione di «" + prepared.Plan.PackName + "»";
            _assetMessage = "Archivio verificato. Controlla origine e modifiche prima di applicare il pacchetto.";
        }, "Analisi del pacchetto · nessuna modifica al gioco");
    }

    private async Task PrepareBuiltinAssetAsync()
    {
        _directXPlan = null;
        var report = _report; if (!CanPrepareAssets || report is null) return;
        _assetPlan = null; _assetPackage = null;
        var widescreenInstalled = _screenState?.State?.Installed == true;
        await RunFxTaskAsync(async (token, progress) =>
        {
            var package = await _assetSource.PrepareBuiltinPackAsync(token, progress);
            var plan = await PrepareImportedAssetAsync(report, package, widescreenInstalled, token, progress);
            return (Package: package, Plan: plan);
        }, prepared =>
        {
            _assetPackage = prepared.Package; _assetPlan = prepared.Plan;
            _assetPlanConfirmation = "Conferma applicazione di «" + prepared.Plan.PackName + "»";
            _assetMessage = "Asfalto 2K pronto in cache. Controlla l'anteprima prima di applicarlo al gioco.";
        }, "Preparazione asfalto 2K · nessuna modifica al gioco");
    }

    private async Task<AssetPlan> PrepareImportedAssetAsync(GameReport report, PreparedAssetPack package, bool widescreenInstalled,
        CancellationToken token, IProgress<BackupProgress> progress)
    {
        string? runtime = null;
        if (package.Manifest.Kind == "Textures")
        {
            if (_directXState?.Installed == true) throw new InvalidOperationException("Il percorso DirectX è installato. Rimuovilo prima di usare i pacchetti TexWizard.");
            var reason = TextureRuntimeCatalog.CheckGame(report);
            if (reason is not null) throw new InvalidOperationException(reason);
            if (!widescreenInstalled) throw new InvalidOperationException("Applica prima il Widescreen Fix gestito dalla pagina Schermo: le texture richiedono il suo ASI loader.");
            runtime = await _assetSource.PrepareAsync(token, progress);
        }
        return _assets.PrepareInstall(report, package, runtime, token);
    }

    private async Task PrepareAssetToggleAsync(string id, string name, bool enabled)
    {
        _directXPlan = null;
        var report = _report; if (!CanPrepareAssets || report is null) return;
        _assetPlan = null; _assetPackage = null;
        await RunFxTaskAsync((token, _) => Task.FromResult(_assets.PrepareToggle(report, id, enabled, token)), plan =>
        {
            _assetPlan = plan; _assetPlanConfirmation = "Conferma " + (enabled ? "attivazione" : "disattivazione") + " di «" + name + "»";
            _assetMessage = "Anteprima " + (enabled ? "attivazione" : "disattivazione") + " pronta. Controlla i file prima di confermare.";
        }, "Preparazione modifica pacchetto · nessuna scrittura al gioco");
    }

    private async Task PrepareAssetRemoveAsync(string id, string name)
    {
        _directXPlan = null;
        var report = _report; if (!CanPrepareAssets || report is null) return;
        _assetPlan = null; _assetPackage = null;
        await RunFxTaskAsync((token, _) => Task.FromResult(_assets.PrepareRemove(report, id, token)), plan =>
        {
            _assetPlan = plan; _assetPlanConfirmation = "Conferma rimozione di «" + name + "»";
            _assetMessage = "Anteprima rimozione pronta. I file originali gestiti saranno ripristinati dove previsto.";
        }, "Preparazione rimozione pacchetto · nessuna scrittura al gioco");
    }

    private async Task ApplyAssetPlanAsync()
    {
        var plan = _assetPlan; var report = _report;
        if (!CanPrepareAssets || plan is null || report is null) return;
        await RunFxTaskAsync((token, progress) =>
        {
            if (plan.Changes.Count > 0) _backups.Create(report, token, progress);
            return Task.FromResult(_assets.Execute(plan, token, progress));
        }, state =>
        {
            _assetState = new(state, false, ""); _assetPlan = null; _assetPackage = null;
            _assetMessage = "Modifiche applicate e verificate. Avvia o riavvia il gioco per controllare gli asset.";
        }, "Applicazione delle modifiche asset confermate");
    }

    private async Task RecoverAssetsAsync(bool rollback)
    {
        var report = _report; if (_busy || report is null) return;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_assets.Recover(report.ExecutablePath, rollback, token, progress)), state =>
        {
            _assetState = new(state, false, ""); _assetPlan = null; _assetPackage = null;
            _assetMessage = rollback ? "Operazione annullata: stato asset precedente recuperato." : "Operazione asset completata e verificata.";
        }, rollback ? "Recupero degli asset precedenti" : "Ripresa operazione asset");
    }
}
