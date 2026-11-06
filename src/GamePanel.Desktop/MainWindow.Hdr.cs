using System.Windows;
using System.Windows.Controls;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private void RenderImageRoutes()
    {
        if (_report is null) return;
        var rows = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        rows.ColumnDefinitions.Add(new ColumnDefinition()); rows.ColumnDefinitions.Add(new ColumnDefinition());
        var sdr = new StackPanel(); sdr.Children.Add(DesignSystem.Icon("settings", Accent, 27));
        var title = Text("ReShade e filtri SDR", 18, Ink, FontWeights.SemiBold); title.Margin = new Thickness(0, 12, 0, 8); sdr.Children.Add(title);
        sdr.Children.Add(Text(SelectedIsRemix ? "Usa il percorso della copia originale. ReShade non viene aggiunto automaticamente al renderer Remix."
            : SelectedCapabilities.SupportsReShade ? "Preset generali per nitidezza, colore e contrasto. Download verificato del componente ufficiale, anteprima e ripristino."
            : "Il profilo generale richiede un EXE x86 Direct3D 9. Per gli altri renderer serve un'integrazione dedicata.", 12, Muted));
        var configure = FxButton("Configura ReShade", "GraphicsReShade", (_, _) => OpenGraphicsPage(2), !_busy && SelectedCapabilities.SupportsReShade && !SelectedIsRemix);
        SmallAction(configure, "Configura ReShade", "settings"); sdr.Children.Add(configure);
        var sdrCard = AssetPanel(sdr, 20); sdrCard.Margin = new Thickness(0, 0, 7, 0); rows.Children.Add(sdrCard);
        var hdr = new StackPanel(); hdr.Children.Add(DesignSystem.Icon("sun", Accent, 27));
        var hdrTitle = Text("HDR e schermo", 18, Ink, FontWeights.SemiBold); hdrTitle.Margin = new Thickness(0, 12, 0, 8); hdr.Children.Add(hdrTitle);
        hdr.Children.Add(Text("HDR nativo, Auto HDR e filtri SDR sono percorsi distinti. Verifica il monitor selezionato e le opzioni disponibili in Windows e nel gioco.", 12, Muted));
        var open = FxButton("Verifica HDR e schermo", "GraphicsHdr", (_, _) => OpenGraphicsPage(8), !_busy); SmallAction(open, "Verifica HDR e schermo", "sun"); hdr.Children.Add(open);
        var hdrCard = AssetPanel(hdr, 20); hdrCard.Margin = new Thickness(7, 0, 0, 0); Grid.SetColumn(hdrCard, 1); rows.Children.Add(hdrCard); _content.Children.Add(rows);
    }
    private void RenderHdrOptions()
    {
        Heading("HDR e schermo", "Le possibilità del monitor, di Windows e del gioco restano distinte dai preset SDR.");
        RenderDisplaySelector();
        var display = SelectedDisplay();
        var state = display is null ? null : DisplayCatalog.AdvancedColor(display);
        var body = new StackPanel();
        body.Children.Add(DesignSystem.Chip("RILEVAMENTO WINDOWS · SOLA LETTURA", Accent));
        var title = Text(display?.Name ?? "Nessun monitor selezionato", 22, Ink, FontWeights.SemiBold); title.Margin = new Thickness(0, 12, 0, 12); body.Children.Add(title);
        var status = Text(state is null ? "Stato colore avanzato non disponibile per questo schermo. Verifica le impostazioni HDR di Windows."
            : "Colore avanzato: " + (state.Supported ? "supportato" : "non segnalato") + " · " + (state.Enabled ? "attivo" : "disattivato")
                + (state.BitsPerChannel > 0 ? $" · {state.BitsPerChannel} bit per canale" : ""), 13, Muted);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "HdrDisplayStatus"); body.Children.Add(status);
        body.Children.Add(Text("Il dato di colore avanzato non certifica HDR nel gioco. GamePanel non forza formati HDR, driver o flag non documentati.", 12, Muted));
        var windows = FxButton("Apri HDR nelle impostazioni Windows", "WindowsHdrSettings", (_, _) => OpenWindowsSettings("ms-settings:display-advancedcolor", "Apri Impostazioni → Sistema → Schermo → HDR."), !_busy);
        SmallAction(windows, "Apri HDR nelle impostazioni Windows", "sun"); body.Children.Add(windows); _content.Children.Add(AssetPanel(body, 20));
        Card("HDR nativo del gioco", "Quando il titolo offre un'opzione HDR, va abilitata e calibrata nelle sue impostazioni. La sola analisi dell'EXE non conferma che quel gioco la supporti.");
        Card("Auto HDR di Windows", "La disponibilità dipende da Windows, monitor e compatibilità del gioco. Nella pagina HDR di Windows puoi controllare l'opzione quando presente. GamePanel non ne dichiara l'attivazione per singolo gioco.");
        Card("ReShade e preset SDR", "Nitidezza, saturazione e contrasto regolano l'immagine SDR: non diventano HDR. Gli effetti basati sulla profondità richiedono un profilo dedicato e non vengono installati dal preset generale.");
        if (SelectedIsRemix) Card("La copia usa RTX Remix", "Conserva il profilo di illuminazione della mod. Regola il renderer dal suo pannello; sovrapporre ReShade o altri percorsi HDR richiede un collaudo dedicato.");
    }
}
