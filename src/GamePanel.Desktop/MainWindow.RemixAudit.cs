using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using GamePanel.Core;
using Microsoft.Win32;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private string _remixAuditMessage = "L'analisi legge l'archivio locale ed esporta l'inventario. Non installa il renderer o le mod.";

    private async Task ExportRemixComponentsAsync()
    {
        if (_busy) return;
        var source = new OpenFileDialog
        {
            Title = "Seleziona lo stesso archivio Underground 2 Remix 0.4",
            Filter = "Archivio Remix (*.7z)|*.7z", CheckFileExists = true, Multiselect = false
        };
        if (source.ShowDialog(this) != true) return;
        var destination = new SaveFileDialog
        {
            Title = "Salva i componenti selezionati per la verifica",
            Filter = "Pacchetto di verifica (*.zip)|*.zip", FileName = "GamePanel-RemixComponents.zip",
            DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true
        };
        if (destination.ShowDialog(this) != true) return;
        if (!Path.GetExtension(destination.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        { _remixAuditMessage = "Scegli un nuovo file ZIP per i componenti di verifica."; Render(); return; }
        if (_report is not null)
        {
            var relative = Path.GetRelativePath(Path.GetDirectoryName(_report.ExecutablePath)!, destination.FileName);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            { _remixAuditMessage = "Salva il pacchetto fuori dalla cartella del gioco, per esempio sul Desktop."; Render(); return; }
        }
        await RunFxTaskAsync((token, progress) => Task.FromResult(new RemixComponentBundle().Prepare(source.FileName, destination.FileName, token, progress)), result =>
        {
            _remixAuditMessage = $"Pacchetto di verifica creato: {result.FilesCount} file, {result.Bytes / 1048576d:0.0} MiB. "
                + "Allega questo ZIP alla chat. Nessun componente è stato installato o eseguito.";
            _assetMessage = _remixAuditMessage;
        }, "Preparazione componenti Remix per verifica · nessuna modifica al gioco");
    }

    private async Task AnalyzeRemixArchiveAsync()
    {
        if (_busy) return;
        var picker = new OpenFileDialog
        {
            Title = "Seleziona Underground_2_Remix_0.4.7z scaricato dall'autore",
            Filter = "Archivio RTX Remix (*.7z)|*.7z", CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        var source = picker.FileName;
        await RunFxTaskAsync((token, progress) => Task.FromResult(new RemixArchiveAudit().Analyze(source, token, progress)), report =>
        {
            _remixAuditMessage = $"Archivio letto: {report.Files.Count} voci. "
                + (report.MatchesPublishedSource ? "Dimensione e MD5 corrispondono alla pubblicazione 0.4; compatibilità ancora da verificare." : "Archivio diverso dalla pubblicazione attesa: nessuna installazione.");
            var save = new SaveFileDialog
            {
                Title = "Esporta inventario Remix per la verifica", Filter = "Report JSON (*.json)|*.json",
                FileName = "GamePanel-RemixAudit.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true
            };
            if (save.ShowDialog(this) != true) return;
            if (!Path.GetExtension(save.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Seleziona un file JSON per il report Remix.");
            File.WriteAllText(save.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            _remixAuditMessage += " Inventario esportato: puoi allegare il JSON senza trasferire l'archivio completo.";
        }, "Analisi locale dell'archivio Remix · nessuna estrazione o installazione");
    }
}
