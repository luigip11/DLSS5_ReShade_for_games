using System.IO;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly RemixTrialService _remixTrial = new();
    private RemixTrialPlan? _remixTrialPlan;
    private RemixTrialResult? _remixTrialResult;
    private string _remixTrialOriginalExe = "";
    private string _remixTrialOriginalHash = "";
    private string _remixTrialMessage = "Scegli l'archivio locale 0.4 e una nuova cartella vuota fuori dal gioco. Prima vedrai l'anteprima.";

    private void ResetRemixTrial()
    {
        _remixTrialPlan = null; _remixTrialResult = null;
        _remixTrialOriginalExe = ""; _remixTrialOriginalHash = "";
        _remixTrialMessage = "Scegli l'archivio locale 0.4 e una nuova cartella vuota fuori dal gioco. Prima vedrai l'anteprima.";
    }

    private void RenderRemixTrial()
    {
        var body = new StackPanel();
        body.Children.Add(DesignSystem.Chip("SPERIMENTALE · ANTI CULL ESCLUSO", DesignSystem.Amber));
        var title = Text("Prova RTX Remix su una copia", 22, Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(title);
        body.Children.Add(Text("AntiCull escluso • gioco da collaudare. Copia il gioco e applica l'archivio Remix 0.4 nella destinazione scelta, conservando l'EXE originale. La cartella di partenza resta intatta.", 13, Muted));
        body.Children.Add(Text("Prima di preparare: il Widescreen Fix deve risultare installato e gestito nella pagina Schermo, sul gioco originale funzionante.", 12, Muted));
        var risk = Text("Senza AntiCull possono mancare oggetti o parti della città. Mondo, personaggi e DLSS attivo non sono verificati. ReShade, Texture Toolkit e TexWizard non vengono copiati. Il Widescreen Fix presente viene conservato nella copia, da collaudare con Remix.", 12, Muted);
        risk.Margin = new Thickness(0, 10, 0, 8); body.Children.Add(risk);
        body.Children.Add(Text("I salvataggi e il profilo in Documenti possono essere condivisi con l'originale: questa copia non garantisce progressi separati. Servono diversi GB liberi per il gioco e l'archivio estratto; nessun avvio automatico.", 12, Muted));
        var status = Text(_remixTrialMessage, 12, Muted); status.Margin = new Thickness(0, 12, 0, 8);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "RemixTrialStatus"); body.Children.Add(status);
        body.Children.Add(FxButton("Prepara copia di prova", "PrepareRemixTrial", async (_, _) => await PrepareRemixTrialAsync(), CanPrepareAssets));

        if (_remixTrialPlan is { } plan)
        {
            var preview = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(preview, "RemixTrialPreview");
            preview.Children.Add(Text("Anteprima · nessun file copiato", 17, Ink, FontWeights.SemiBold));
            preview.Children.Add(Text("EXE originale: " + _remixTrialOriginalExe + "\nSHA-256: " + _remixTrialOriginalHash
                + "\nArchivio: " + plan.ArchivePath + "\nDestinazione: " + plan.Destination, 12, Muted));
            preview.Children.Add(Text($"{plan.FileCount:N0} file previsti · gioco {RemixSize(plan.OriginalBytes)} · Remix estratto {RemixSize(plan.ArchiveBytes)}\nSpazio richiesto con margine: {RemixSize(plan.RequiredBytes)} · disponibile: {RemixSize(plan.AvailableBytes)}", 12, Muted));
            if (plan.Warnings.Count > 0) preview.Children.Add(Text(string.Join("\n", plan.Warnings), 12, Muted));
            var create = FxButton("Crea copia e applica Remix", "CreateRemixTrial", async (_, _) => await CreateRemixTrialAsync(), CanPrepareAssets);
            create.Margin = new Thickness(0, 12, 0, 0); create.Background = Accent; create.Foreground = BackgroundBrush;
            preview.Children.Add(create); body.Children.Add(preview);
        }
        if (_remixTrialResult is { } result)
        {
            var complete = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            complete.Children.Add(Text("Copia creata, da provare", 17, Ink, FontWeights.SemiBold));
            complete.Children.Add(Text($"{result.Folder}\n{result.FileCount:N0} file · {RemixSize(result.Bytes)}\nEXE copia: {result.ExecutablePath}", 12, Muted));
            if (result.Warnings.Count > 0) complete.Children.Add(Text(string.Join("\n", result.Warnings), 12, Muted));
            complete.Children.Add(Text("La creazione verifica i file, non il risultato grafico. Seleziona la copia per analizzarla e aggiungerla alla libreria; avviala solo quando vuoi collaudarla.", 12, Muted));
            complete.Children.Add(FxButton("Seleziona EXE copia", "SelectRemixTrialExe", async (_, _) =>
            {
                _path.Text = result.ExecutablePath;
                await InspectAsync(result.ExecutablePath);
            }, !_busy));
            body.Children.Add(complete);
        }
        _content.Children.Add(AssetPanel(body, 20));
    }

    private static string RemixSize(long bytes) => $"{bytes / 1073741824d:0.00} GiB";

    private async Task PrepareRemixTrialAsync()
    {
        var report = _report; if (!CanPrepareAssets || report is null) return;
        var archive = new OpenFileDialog
        {
            Title = "Seleziona Underground_2_Remix_0.4.7z locale",
            Filter = "Archivio Remix (*.7z)|*.7z", CheckFileExists = true, Multiselect = false
        };
        if (archive.ShowDialog(this) != true) return;
        var destination = new OpenFolderDialog
        {
            Title = "Scegli o crea una cartella vuota per la copia, fuori dal gioco",
            Multiselect = false
        };
        if (destination.ShowDialog(this) != true) return;
        _remixTrialPlan = null; _remixTrialResult = null;
        var relative = Path.GetRelativePath(Path.GetDirectoryName(report.ExecutablePath)!, destination.FolderName);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            _remixTrialMessage = "Scegli una cartella vuota fuori dalla cartella del gioco originale.";
            Render(); return;
        }
        await RunFxTaskAsync((token, progress) => Task.FromResult(_remixTrial.Prepare(report, archive.FileName, destination.FolderName, token, progress)), plan =>
        {
            _remixTrialPlan = plan; _remixTrialOriginalExe = report.ExecutablePath; _remixTrialOriginalHash = report.Sha256;
            _remixTrialMessage = "Anteprima pronta. Controlla destinazione, spazio e avvisi, poi scegli Crea copia e applica Remix.";
        }, "Preparazione copia Remix · lettura del gioco e dell'archivio locale");
    }

    private async Task CreateRemixTrialAsync()
    {
        var plan = _remixTrialPlan; if (!CanPrepareAssets || plan is null) return;
        _remixTrialPlan = null;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_remixTrial.Create(plan, token, progress)), result =>
        {
            _remixTrialResult = result;
            _remixTrialMessage = "Copia creata, da provare. AntiCull escluso; compatibilità e risultato grafico restano da collaudare.";
        }, "Creazione della copia e applicazione Remix · nessun avvio del gioco");
    }
}
