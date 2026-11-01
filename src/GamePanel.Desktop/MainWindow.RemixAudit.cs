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
