using System.IO;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private RemixPackService? _packService;
    private RemixPackPlan? _packPlan;
    private RemixPackResult? _packResult;
    private RemixPackExportResult? _packExport;
    private string _packGame = "", _packSource = "", _packDestination = "";
    private bool _packModels = true;
    private string _packMessage = "Scegli un pacchetto locale ZIP o una cartella Remix già estratta, poi una nuova cartella vuota per la copia.";
    private void ResetRemixPackage()
    { _packExport = null; _packService = null; _packPlan = null; _packResult = null; _packGame = ""; _packSource = ""; _packDestination = ""; _packModels = true; }
    private void RenderRemixPackage()
    {
        var report = _report; if (report is null || SelectedIsRemix || !(SelectedIsUnderground1 || SelectedIsUnderground2)) return;
        var id = SelectedIsUnderground1 ? "UG1" : "UG2";
        if (_packGame != id) { ResetRemixPackage(); _packGame = id; _packService = new(id); }
        var catalog = RemixPackDefinition.ForGame(id);
        var supported = catalog.ExecutableHashes.Contains(report.Sha256, StringComparer.OrdinalIgnoreCase);
        var body = new StackPanel(); body.Children.Add(DesignSystem.Chip(id + " · CONFIGURAZIONE RTX REMIX", Accent));
        var title = Text("Prepara la tua copia Remix", 24, Ink, FontWeights.SemiBold); title.Margin = new Thickness(0, 12, 0, 10); body.Children.Add(title);
        body.Children.Add(Text("1 · Verifica il gioco   →   2 · Pacchetto e copia   →   3 · Risoluzione, DLSS e avvio", 12, Ink, FontWeights.SemiBold));
        body.Children.Add(Text("Configurazioni derivate dalla copia collaudata su RTX 5080 Laptop. La compatibilità su altri PC va verificata. Il gioco originale e il suo EXE restano dell'utente; la copia viene creata separatamente.", 12, Muted));
        var distribution = Text(id == "UG1"
            ? "Download GamePanel non disponibile: redistribuzione degli asset UG1 ancora da chiarire. Puoi usare un pacchetto locale o la cartella della tua mod."
            : "Download GamePanel non ancora pubblicato: mancano l'archivio ospitato e la verifica finale della distribuzione dei componenti. L'importazione locale è disponibile.", 12, Muted);
        distribution.Margin = new Thickness(0, 12, 0, 10); body.Children.Add(distribution);
        var download = FxButton("Download non ancora disponibile", "RemixPackageDownload", (_, _) => { }, false); SmallAction(download, "Download non ancora disponibile", "download");
        var author = FxButton("Apri la fonte dell'autore", "RemixPackageAuthor", (_, _) => OpenAuthorPage(id), !_busy); SmallAction(author, "Apri la fonte dell'autore", "link");
        body.Children.Add(EqualActions(download, author));
        if (!supported) body.Children.Add(Text("La copia analizzata non corrisponde agli EXE verificati per questa preparazione. I file originali non vengono sostituiti per forzare la compatibilità.", 12, DesignSystem.Amber));
        body.Children.Add(Text("PACCHETTO LOCALE", 10, Muted, FontWeights.SemiBold));
        body.Children.Add(Text(_packSource.Length == 0 ? "Nessun pacchetto selezionato" : _packSource, 12, Muted));
        var folder = FxButton("Seleziona cartella Remix", "RemixChooseFolder", (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "Cartella con .trex, rtx-remix e configurazioni della mod", Multiselect = false };
            if (picker.ShowDialog(this) == true) { _packSource = picker.FolderName; _packPlan = null; _packResult = null; Render(); }
        }, !_busy && supported);
        var archive = FxButton("Seleziona pacchetto ZIP", "RemixChooseZip", (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Pacchetto locale GamePanel Remix · radice senza cartella wrapper", Filter = "Pacchetto ZIP (*.zip)|*.zip", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) { _packSource = picker.FileName; _packPlan = null; _packResult = null; Render(); }
        }, !_busy && supported);
        SmallAction(folder, "Seleziona cartella Remix", "folder"); SmallAction(archive, "Seleziona pacchetto ZIP", "layers"); body.Children.Add(EqualActions(folder, archive));
        body.Children.Add(FxButton("Ricostruisci ZIP dalle parti scaricate", "RemixJoinParts", async (_, _) =>
        {
            var manifest = new OpenFileDialog { Filter = "Manifest Remix (*.manifest.json)|*.manifest.json", CheckFileExists = true };
            if (manifest.ShowDialog(this) != true) return;
            var save = new SaveFileDialog { Filter = "Pacchetto ZIP (*.zip)|*.zip", FileName = id + "-GamePanel-local.zip", AddExtension = true };
            if (save.ShowDialog(this) != true) return; var service = _packService!;
            await RunFxTaskAsync((token, progress) => Task.FromResult(service.JoinReleaseParts(manifest.FileName, save.FileName, token, progress)), joined =>
            { _packSource = joined.Path; _packPlan = null; _packResult = null; _packMessage = "ZIP ricostruito e hash verificati. Ora verifica il pacchetto con il catalogo."; }, "Verifica e unione delle parti locali");
        }, !_busy && supported));
        body.Children.Add(Text("COPIA SEPARATA", 10, Muted, FontWeights.SemiBold)); body.Children.Add(Text(_packDestination.Length == 0 ? "Nessuna destinazione selezionata" : _packDestination, 12, Muted));
        body.Children.Add(FxButton("Scegli cartella vuota per la copia", "RemixChooseDestination", (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "Scegli o crea una cartella vuota esterna al gioco e al pacchetto" };
            if (picker.ShowDialog(this) == true) { _packDestination = picker.FolderName; _packPlan = null; _packResult = null; Render(); }
        }, !_busy && supported));
        if (id == "UG2")
        {
            var models = new CheckBox { Content = "Aggiungi le correzioni Smol verificate: Golf, IS300 e Sentra", IsChecked = _packModels, IsEnabled = !_busy, Foreground = Ink, Margin = new Thickness(0, 14, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(models, "RemixIncludeSmol");
            models.Checked += (_, _) => { _packModels = true; _packPlan = null; Render(); }; models.Unchecked += (_, _) => { _packModels = false; _packPlan = null; Render(); };
            body.Children.Add(models); body.Children.Add(Text("Componente separato presente nella configurazione collaudata. Deselezionarlo non rimuove eventuali modelli già presenti nel gioco originale.", 11, Muted));
        }
        var message = Text(_packMessage, 12, Muted); message.Margin = new Thickness(0, 12, 0, 12); body.Children.Add(message);
        System.Windows.Automation.AutomationProperties.SetAutomationId(message, "RemixPackageStatus");
        var prepare = FxButton("Verifica pacchetto e prepara anteprima", "RemixPackagePrepare", async (_, _) =>
        {
            var service = _packService!; var source = _packSource; var destination = _packDestination; var models = id == "UG2" && _packModels;
            _packPlan = null;
            await RunFxTaskAsync((token, progress) => Task.FromResult(service.Prepare(report, source, destination, models, token, progress)), plan =>
            { _packPlan = plan; _packMessage = "Anteprima pronta. Nessun file del gioco è stato modificato."; }, "Verifica pacchetto Remix e lettura dei file originali");
        }, CanPrepareAssets && supported && _packSource.Length > 0 && _packDestination.Length > 0);
        SmallAction(prepare, "Verifica pacchetto e prepara anteprima", "search"); body.Children.Add(prepare);
        if (_packPlan is { } plan)
        {
            var preview = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            preview.Children.Add(Text("Anteprima della copia", 19, Ink, FontWeights.SemiBold));
            preview.Children.Add(Text($"{plan.FileCount:N0} file · {RemixSize(plan.Bytes)} non compressi\nSpazio richiesto {RemixSize(plan.RequiredBytes)} · disponibile {RemixSize(plan.AvailableBytes)}\nDestinazione: {plan.Destination}", 12, Muted));
            preview.Children.Add(Text("Le configurazioni del pacchetto vengono conservate, senza applicare preset UG2 a UG1. "
                + (id == "UG2" ? "FrontB.lzc viene mantenuto dalla copia originale. " : "Le modifiche Global/FrontEnd identificate fanno parte della copia. ")
                + $"Configurazioni personalizzate rispetto all'inventario: {plan.PersonalizedConfigurations}. Dopo la copia verifica risoluzione e DLSS.", 12, Muted));
            preview.Children.Add(Text("La nuova copia può condividere salvataggi e impostazioni globali del gioco. Il pacchetto locale viene identificato tramite hash: non è una garanzia di stabilità o una firma del produttore.", 11, Muted));
            var apply = FxButton("Crea la copia Remix", "RemixPackageCreate", async (_, _) =>
            {
                var service = _packService!; _packPlan = null;
                await RunFxTaskAsync((token, progress) => Task.FromResult(service.Create(plan, token, progress)), result =>
                { _packResult = result; _packMessage = "Copia creata. Continua per registrarla e configurare risoluzione e DLSS."; }, "Creazione della copia Remix · gioco originale conservato");
            }, CanPrepareAssets); DecorateAction(apply, "Crea la copia Remix", "layers", true); preview.Children.Add(apply); body.Children.Add(preview);
        }
        if (_packResult is { } result)
        {
            body.Children.Add(Text("Copia pronta da configurare\n" + result.Folder, 16, Ink, FontWeights.SemiBold));
            body.Children.Add(FxButton("Continua con la copia Remix", "RemixPackageContinue", async (_, _) => await ContinueRemixPackageAsync(result), !_busy));
        }
        var export = new Expander { Header = "Avanzate · esporta il pacchetto locale", Foreground = Muted, Margin = new Thickness(0, 16, 0, 0) };
        var exportBody = new StackPanel(); exportBody.Children.Add(Text("Crea uno ZIP dai soli file del catalogo locale selezionato. EXE del gioco, cache e journal sono esclusi; gli asset modificati della mod restano soggetti ai propri permessi. L'esportazione non pubblica né carica l'archivio online.", 11, Muted));
        exportBody.Children.Add(FxButton("Esporta ZIP locale verificato", "RemixPackageExport", async (_, _) =>
        {
            var save = new SaveFileDialog { Title = "Nuovo pacchetto locale personale · non pubblicato", Filter = "Pacchetto ZIP (*.zip)|*.zip", FileName = id + "-GamePanel-local.zip", AddExtension = true };
            if (save.ShowDialog(this) != true) return; var service = _packService!; var source = _packSource; var models = id == "UG2" && _packModels;
            await RunFxTaskAsync((token, progress) => Task.FromResult(service.Export(source, save.FileName, models, token, progress)), result =>
            { _packExport = result; _packMessage = "ZIP locale creato: " + result.Path + "\nSHA-256: " + result.Sha256; }, "Esportazione dei soli componenti Remix selezionati");
        }, !_busy && _packSource.Length > 0));
        exportBody.Children.Add(Text("Hosting previsto: GitHub Releases. Dopo l'esportazione puoi preparare parti inferiori a 2 GiB e un manifest SHA-256. Il caricamento e la verifica dei permessi restano separati.", 11, Muted));
        exportBody.Children.Add(FxButton("Prepara file per GitHub Releases", "RemixExportRelease", async (_, _) =>
        {
            var exported = _packExport!; var service = _packService!;
            var destination = Path.Combine(Path.GetDirectoryName(exported.Path)!, Path.GetFileNameWithoutExtension(exported.Path) + "-GitHub-" + Guid.NewGuid().ToString("N")[..8]);
            await RunFxTaskAsync((token, progress) => Task.FromResult(service.ExportReleaseParts(exported, destination, token, progress)), release =>
            { _packMessage = $"Pronti {release.Parts} file in parti e il manifest: {release.Folder}\nNessun upload effettuato. Prima di pubblicare completa permessi, licenze e prova della copia generata."; }, "Preparazione degli allegati per GitHub Releases");
        }, !_busy && _packExport is not null)); export.Content = exportBody; StyleAdvanced(export); body.Children.Add(export);
        DesignSystem.SpaceActions(body); _content.Children.Add(AssetPanel(body, 22));
    }
    private static void OpenAuthorPage(string game)
    {
        var url = game == "UG1" ? "https://www.moddb.com/mods/need-for-speed-underground-rtx-remix/downloads/nfs-underground-rtx-v05" : RemixArchiveAudit.SourceUrl;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { MessageBox.Show(e.Message, "Fonte della mod"); }
    }
    private async Task ContinueRemixPackageAsync(RemixPackResult result)
    {
        var original = _library.Games.FirstOrDefault(g => string.Equals(g.ExecutablePath, result.OriginalExecutablePath, StringComparison.OrdinalIgnoreCase));
        _path.Text = result.ExecutablePath; await InspectAsync(result.ExecutablePath);
        if (_report is null || !string.Equals(_report.ExecutablePath, result.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var variant = _library.Games.Single(g => string.Equals(g.ExecutablePath, result.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            if (original is not null) _library = _libraryStore.LinkRemixVariant(original.Id, variant.Id);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { _assetMessage = "Copia analizzata; collegamento in libreria da completare: " + e.Message; }
        OpenGraphicsPage(1);
        await PrepareRemixManagedAsync("Adopt");
    }
}
