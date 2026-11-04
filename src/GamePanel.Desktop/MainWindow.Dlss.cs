using System.IO;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly RemixManagedService _remixManaged = new();
    private RemixManagedStatus? _remixManagedStatus;
    private RemixManagedPlan? _remixManagedPlan;
    private RemixDlssSettings _dlssSettings = new();
    private RemixDlssSettings _dlssEdits = new();
    private string _remixManagedMessage = "Seleziona un gioco per vedere il percorso DLSS disponibile.";
    private string? _graphicsAdapters;
    private bool SelectedIsUnderground2 => SelectedCapabilities.IsUnderground2;
    private bool SelectedIsRemix => SelectedCapabilities.HasRemixRenderer;
    private bool HasMultiFrameHardware => (_graphicsAdapters ??= Hardware.DisplayDevices()).Contains("RTX 50", StringComparison.OrdinalIgnoreCase);
    private bool MatchesRecommendedMachine
    {
        get
        {
            var gpu = _graphicsAdapters ??= Hardware.DisplayDevices(); var display = SelectedDisplay();
            return gpu.Contains("RTX 5080", StringComparison.OrdinalIgnoreCase) && gpu.Contains("Laptop", StringComparison.OrdinalIgnoreCase)
                && display?.TargetWidth == 3440 && display.TargetHeight == 1440;
        }
    }
    private void ResetRemixManaged()
    { _remixManagedStatus = null; _remixManagedPlan = null; _dlssSettings = new(); _dlssEdits = new(); _remixManagedMessage = "Seleziona un gioco per vedere il percorso DLSS disponibile."; }
    private async Task RefreshRemixManagedAsync(string executable)
    {
        var status = await Task.Run(() => _remixManaged.Status(executable));
        if (_closing) return;
        _remixManagedStatus = status; _dlssSettings = status.Settings; _dlssEdits = new(); _remixManagedMessage = status.Message;
    }
    private void RenderDlss()
    {
        Heading("DLSS e profilo del gioco", "Le funzioni disponibili seguono il renderer e la configurazione del gioco selezionato.");
        var status = _remixManagedStatus;
        var displayMessage = _report is not null && status?.Supported != true && !SelectedIsRemix
            ? "Analisi DLSS disponibile. Le regolazioni seguono le integrazioni supportate per questo gioco." : _remixManagedMessage;
        var message = Text(displayMessage, 13, Muted);
        System.Windows.Automation.AutomationProperties.SetAutomationId(message, "DlssStatus");
        _content.Children.Add(AssetPanel(message, 14));
        if (SelectedIsRemix && _report?.Sha256.Equals(RemixExecutableCatalog.NoCdSha256, StringComparison.OrdinalIgnoreCase) == true)
            Card("EXE NoCD identificato", "Questa build può gestirne la configurazione. Il funzionamento di RTX Remix con questo eseguibile è ancora da collaudare; il riconoscimento non certifica la versione 1.2.");
        if (_report is null) { Card("Scegli un gioco", "Apri la libreria e analizza il gioco che vuoi configurare."); return; }
        if (status?.Supported != true)
        {
            Card("Disponibilità per questo gioco", SelectedIsRemix
                ? "Il renderer Remix è presente, ma questa copia non soddisfa i requisiti del profilo gestito. I controlli non modificano configurazioni di origine sconosciuta."
                : "L'analisi DLSS è disponibile. La modifica dei parametri è attiva per le copie Remix riconosciute; gli altri giochi richiedono un'integrazione dedicata.");
            var components = _report.Components.Where(c => c.Kind.StartsWith("DLSS", StringComparison.Ordinal)).ToArray();
            Card("Componenti riconosciuti", components.Length == 0
                ? "Nessuna libreria DLSS rilevata nella scansione. Questo dato non esclude caricamenti dal driver o da altre cartelle."
                : string.Join("\n", components.Select(c => $"{c.Kind} · {c.Version}\n{c.RelativePath}")));
            Details("Cosa indica l'analisi", "La presenza di una DLL non conferma che DLSS sia attivo nel gioco. Le opzioni e il runtime di ciascun titolo devono essere verificati prima di proporre modifiche.");
            return;
        }
        if (status.Pending)
        {
            Card("Operazione da recuperare", "La configurazione è stata modificata solo in parte. Chiudi il gioco e riprendi oppure recupera lo stato precedente.");
            _content.Children.Add(FxButton("Riprendi configurazione", "ResumeRemixProfile", async (_, _) => await RecoverRemixManagedAsync(false), !_busy));
            _content.Children.Add(FxButton("Annulla e recupera", "RollbackRemixProfile", async (_, _) => await RecoverRemixManagedAsync(true), !_busy));
            return;
        }
        if (!status.Adopted)
        {
            Card("La copia esistente è pronta da registrare", "Conserva le impostazioni che hai già collaudato. Registrare questa copia salva i file di configurazione attuali e collega la variante all'originale in libreria. Non serve riestrarre Remix.");
            _content.Children.Add(FxButton("Prepara registrazione della copia", "AdoptRemixProfile", async (_, _) => await PrepareRemixManagedAsync("Adopt"), !_busy));
        }
        else
        {
            var profile = new StackPanel(); profile.Children.Add(DesignSystem.Chip("CONFIGURAZIONE GESTITA", Accent));
            profile.Children.Add(Text("Configurazione salvata", 22, Ink, FontWeights.SemiBold));
            profile.Children.Add(Text("Salva le regolazioni effettuate nel gioco per conservarle come punto di ripristino. Il salvataggio acquisisce i file presenti, senza riscriverli.", 13, Muted));
            profile.Children.Add(FxButton("Salva configurazione attuale", "CaptureRemixProfile", async (_, _) => await PrepareRemixManagedAsync("Capture"), !_busy));
            profile.Children.Add(FxButton("Ripristina configurazione salvata", "RestoreRemixProfile", async (_, _) => await PrepareRemixManagedAsync("Restore"), !_busy && !status.Changed));
            SpaceDlssPanel(profile);
            _content.Children.Add(AssetPanel(profile, 20));
            if (status.Changed) Card("Modifiche da registrare", "Salva la configurazione attuale per registrare le impostazioni presenti e un eventuale cambio a un EXE identificato. Questa scelta aggiorna il punto di ripristino, senza riscrivere i file del gioco.");
            var recommended = new StackPanel(); recommended.Children.Add(Text(RemixRecommendedProfile.Name, 17, Ink, FontWeights.SemiBold));
            recommended.Children.Add(Text("Profilo derivato dal collaudo dell'utente: DLSS Auto, Ray Reconstruction Transformer e Frame Generation 4×. Include regolazioni di immagine e luci; applicarlo sostituisce i corrispondenti parametri personalizzati.", 12, Muted));
            recommended.Children.Add(FxButton("Prepara profilo consigliato", "RecommendedRemixProfile", async (_, _) => await PrepareRemixManagedAsync("Recommended"), !_busy && !status.Changed && MatchesRecommendedMachine));
            if (!MatchesRecommendedMachine) recommended.Children.Add(Text("Questo profilo è riservato alla RTX 5080 Laptop con schermo selezionato 3440 × 1440. Puoi conservare la tua configurazione e usare i controlli disponibili.", 12, Muted));
            SpaceDlssPanel(recommended);
            _content.Children.Add(AssetPanel(recommended, 18));
            var controls = new StackPanel();
            controls.Children.Add(Text("Parametri DLSS", 20, Ink, FontWeights.SemiBold));
            controls.Children.Add(Text("Valori letti dai file locali, non misurazioni del renderer in esecuzione. I campi senza valore esplicito non confermano un valore attivo. «Lascia invariato» non modifica la relativa chiave.", 12, Muted));
            var settingsGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(-6, 16, -6, 14) };
            controls.Children.Add(settingsGrid);
            settingsGrid.Children.Add(DlssChoice("Super Resolution", "DlssQuality", _dlssSettings.DlssMode,
                [(null, "Lascia invariato"), (4, "Auto"), (3, "Qualità"), (2, "Bilanciato"), (1, "Performance"), (0, "Ultra Performance"), (5, "DLAA")], value => _dlssSettings = _dlssSettings with { DlssMode = value, Upscaler = value is null ? null : 1 }));
            settingsGrid.Children.Add(DlssChoice("Ray Reconstruction", "DlssRayReconstruction", _dlssSettings.RayReconstruction is bool rr ? rr ? 1 : 0 : null,
                [(null, "Lascia invariato"), (1, "Attivo"), (0, "Disattivo")], value => _dlssSettings = _dlssSettings with { RayReconstruction = value is null ? null : value == 1 }));
            settingsGrid.Children.Add(DlssChoice("Modello di ricostruzione", "DlssReconstructionModel", _dlssSettings.Transformer is bool transformer ? transformer ? 1 : 0 : null,
                [(null, "Lascia invariato"), (1, "Transformer"), (0, "CNN")], value => _dlssSettings = _dlssSettings with { Transformer = value is null ? null : value == 1 }));
            var frameChoices = HasMultiFrameHardware ? new (int?, string)[] { (null, "Lascia invariato"), (0, "Disattiva"), (1, "2×"), (2, "3×"), (3, "4×") }
                : [(null, "Lascia invariato"), (0, "Disattiva")];
            var fg = _dlssSettings.FrameGeneration == false ? 0 : _dlssSettings.FrameGeneration == true ? _dlssSettings.GeneratedFrames : null;
            settingsGrid.Children.Add(DlssChoice("Frame Generation", "DlssFrameGeneration", fg, frameChoices,
                value => _dlssSettings = _dlssSettings with { FrameGeneration = value is null ? null : value > 0, GeneratedFrames = value is > 0 ? value : null }));
            if (!HasMultiFrameHardware) controls.Children.Add(Text("L'attivazione della generazione multipla richiede hardware RTX 50 rilevato; la sua disponibilità effettiva dipende anche da runtime e driver.", 12, Muted));
            settingsGrid.Children.Add(DlssChoice("Reflex", "DlssReflex", _dlssSettings.Reflex,
                [(null, "Lascia invariato"), (0, "Disattivo"), (1, "Attivo"), (2, "Attivo + Boost")], value => _dlssSettings = _dlssSettings with { Reflex = value }));
            controls.Children.Add(FxButton("Prepara modifiche DLSS", "PrepareRemixDlss", async (_, _) => await PrepareRemixManagedAsync("Apply"), !_busy && !status.Changed));
            var advanced = new Expander { Header = "Avanzate · parametri DLSS", Content = controls, Foreground = Ink, Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 16) };
            advanced.Header = "Avanzate · personalizza DLSS e fluidità";
            advanced.IsExpanded = true; StyleAdvanced(advanced);
            System.Windows.Automation.AutomationProperties.SetAutomationId(advanced, "DlssAdvancedSettings");
            _content.Children.Add(advanced);
            Details("Fluidità e prestazioni", "Frame Generation aggiunge fotogrammi generati. Un contatore che li include non misura gli FPS di rendering. GamePanel non ricava un valore dividendo per il moltiplicatore e non promette lo stesso risultato su altri PC.");
        }
        if (_remixManagedPlan is { } plan)
        {
            var preview = new StackPanel(); preview.Children.Add(Text("Anteprima · " + RemixActionTitle(plan.Action), 20, Ink, FontWeights.SemiBold));
            preview.Children.Add(Text(plan.ExecutablePath, 12, Muted));
            if (plan.Action == "Capture") preview.Children.Add(Text("Il punto di ripristino viene aggiornato alla configurazione attuale. I file del gioco non vengono modificati.", 12, Muted));
            if (plan.PreviousExecutableSha256 is not null && plan.PreviousExecutableSha256 != plan.ExecutableSha256)
                preview.Children.Add(Text("Cambio EXE da registrare\nPrima: " + plan.PreviousExecutableSha256 + "\nOra: " + plan.ExecutableSha256
                    + "\nViene aggiornata solo l’associazione del profilo; GamePanel non sostituisce l’eseguibile e non certifica il funzionamento di Remix nel gioco.", 12, Muted));
            preview.Children.Add(Text(plan.Changes.Count == 0 ? "Nessuna scrittura nella cartella del gioco. Viene registrata la configurazione attuale."
                : string.Join("\n", plan.Changes.Select(c => c.Path + " · configurazione prima/dopo verificata")), 12, Muted));
            preview.Children.Add(Text(string.Join("\n", plan.Warnings), 12, Muted));
            if (plan.SettingChanges.Count > 0)
            {
                var changes = string.Join("\n", plan.SettingChanges.Select(c => $"{c.Path} · {c.Key}\n{c.Before ?? "(assente)"} → {c.After ?? "(assente)"}"));
                preview.Children.Add(new Expander { Header = $"Parametri modificati · {plan.SettingChanges.Count}", Foreground = Ink,
                    Content = Text(changes, 12, Muted), Margin = new Thickness(0, 12, 0, 12) });
            }
            SpaceDlssPanel(preview);
            preview.Children.Add(FxButton("Conferma " + RemixActionTitle(plan.Action).ToLowerInvariant(), "ApplyRemixProfile", async (_, _) => await ApplyRemixManagedAsync(), !_busy));
            _content.Children.Add(AssetPanel(preview, 20));
        }
        _content.Children.Add(FxButton("Gioca", "RemixProfilePlay", (_, _) => Launch(), !_busy));
    }
    private static string RemixActionTitle(string action) => action switch { "Adopt" => "Registrazione", "Capture" => "Salvataggio", "Restore" => "Ripristino", _ => "Applicazione" };
    private FrameworkElement DlssChoice(string label, string id, int? value, (int? Value, string Label)[] choices, Action<int?> change)
    {
        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 6) }; body.Children.Add(Text(label, 12, Muted));
        var combo = new ComboBox { IsEnabled = !_busy, Margin = new Thickness(0, 5, 0, 0), MinWidth = 130, HorizontalAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetAutomationId(combo, id);
        foreach (var choice in choices) combo.Items.Add(choice.Label);
        combo.SelectedIndex = Array.FindIndex(choices, item => item.Value == value);
        if (combo.SelectedIndex < 0) { combo.SelectedIndex = 0; body.Children.Add(Text("Valore nel file: " + value + " · lasciato invariato", 11, Muted)); }
        var illustration = new Canvas { Width = 76, Height = 52, Margin = new Thickness(12, 0, 0, 0), IsHitTestVisible = false };
        DrawDlssSketch(illustration, id, value);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < 0) return; var selected = choices[combo.SelectedIndex].Value; change(selected); TrackDlssEdit(id, selected); DrawDlssSketch(illustration, id, selected); _remixManagedPlan = null; };
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(combo); Grid.SetColumn(illustration, 1); row.Children.Add(illustration);
        body.Children.Add(row); return new Border { Child = body, Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(6) };
    }
    private void TrackDlssEdit(string id, int? value)
    {
        _dlssEdits = id switch
        {
            "DlssQuality" => _dlssEdits with { DlssMode = value, Upscaler = value is null ? null : 1 },
            "DlssRayReconstruction" => _dlssEdits with { RayReconstruction = value is null ? null : value == 1 },
            "DlssReconstructionModel" => _dlssEdits with { Transformer = value is null ? null : value == 1 },
            "DlssFrameGeneration" => _dlssEdits with { FrameGeneration = value is null ? null : value > 0, GeneratedFrames = value is > 0 ? value : null },
            "DlssReflex" => _dlssEdits with { Reflex = value },
            _ => _dlssEdits
        };
    }
    private async Task PrepareRemixManagedAsync(string action)
    {
        var report = _report; if (report is null) return;
        if (action == "Recommended" && !MatchesRecommendedMachine) return;
        var settings = _dlssEdits;
        await RunFxTaskAsync((token, _) =>
        {
            token.ThrowIfCancellationRequested(); var prepared = action switch
            {
            "Adopt" => _remixManaged.PrepareAdopt(report), "Capture" => _remixManaged.PrepareCapture(report),
            "Restore" => _remixManaged.PrepareRestore(report), "Recommended" => _remixManaged.PrepareApply(report, recommended: true),
                _ => _remixManaged.PrepareApply(report, settings)
            }; token.ThrowIfCancellationRequested(); return Task.FromResult(prepared);
        }, plan => { _remixManagedPlan = plan; _remixManagedMessage = "Anteprima pronta. Controlla le modifiche e conferma."; }, "Preparazione profilo del gioco");
    }
    private async Task ApplyRemixManagedAsync()
    {
        var plan = _remixManagedPlan; if (plan is null) return;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_remixManaged.Execute(plan, token, progress)), status =>
        {
            _remixManagedStatus = status; _dlssSettings = status.Settings; _dlssEdits = new(); _remixManagedPlan = null;
            _remixManagedMessage = plan.Changes.Count == 0 ? "Configurazione registrata. La copia è pronta nella libreria." : "Configurazione applicata e verificata. Avvia il gioco per provarla.";
        }, "Salvataggio configurazione con ripristino");
    }
    private async Task RecoverRemixManagedAsync(bool rollback)
    {
        if (_report is null) return; var executable = _report.ExecutablePath;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_remixManaged.Recover(executable, rollback, token, progress)), status =>
        { _remixManagedStatus = status; _dlssSettings = status.Settings; _dlssEdits = new(); _remixManagedPlan = null; _remixManagedMessage = status.Message; }, "Recupero configurazione Remix");
    }
}
