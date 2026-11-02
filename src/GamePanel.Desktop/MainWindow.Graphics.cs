using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private void OpenGraphicsPage(int page)
    {
        if (_busy) return;
        _page = page; Render(); _pageScroll.ScrollToTop();
    }

    private void RenderGraphicsSetup()
    {
        var body = new StackPanel();
        if (_report is null)
        {
            body.Children.Add(DesignSystem.Chip("CONFIGURAZIONE PER GIOCO", Accent));
            var title = Text("Un gioco. Le sue possibilità.", 25, Ink, FontWeights.SemiBold);
            title.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(title);
            body.Children.Add(Text("Importa un gioco dalla libreria. Qui ritroverai i profili compatibili, i componenti presenti e gli strumenti disponibili per quel titolo.", 13, Muted));
            body.Children.Add(FxButton("Apri libreria", "GraphicsOpenLibrary", (_, _) => OpenGraphicsPage(5), !_busy));
            _content.Children.Add(AssetPanel(body, 20)); return;
        }
        body.Children.Add(DesignSystem.Chip(SelectedIsRemix ? "PERCORSO RTX REMIX" : "CONFIGURAZIONE PER GIOCO", Accent));
        var heading = Text(SelectedIsRemix ? "La tua copia. Il tuo profilo." : "Configura una volta. Poi gioca.", 25, Ink, FontWeights.SemiBold);
        heading.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(heading);
        body.Children.Add(Text(SelectedIsRemix
            ? "La copia Remix è già presente. Apri Profilo e DLSS per registrarla, conservare le impostazioni riuscite o applicare un profilo con anteprima e ripristino. Non serve estrarre di nuovo l'archivio."
            : "I percorsi disponibili seguono il gioco selezionato. Ogni modifica parte da un'anteprima; i profili restano associati alla sua cartella.", 13, Muted));
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var profile = FxButton(SelectedIsRemix ? "Profilo e DLSS" : "Verifica DLSS", "GraphicsOpenDlss", (_, _) => OpenGraphicsPage(1), !_busy);
        if (SelectedIsRemix) { profile.Background = Accent; profile.Foreground = BackgroundBrush; }
        actions.Children.Add(profile);
        if (!SelectedIsRemix && ReShadeProfileService.Eligibility(_report) is null)
            actions.Children.Add(FxButton("Immagine e filtri", "GraphicsOpenEffects", (_, _) => OpenGraphicsPage(2), !_busy));
        if (!SelectedIsRemix && ReShadeProfileService.Eligibility(_report, widescreen: true) is null)
            actions.Children.Add(FxButton("Configura schermo", "GraphicsOpenScreen", (_, _) => OpenGraphicsPage(6), !_busy));
        actions.Children.Add(FxButton("Gioca", "GraphicsPlay", (_, _) => Launch(), CanPrepareAssets));
        body.Children.Add(actions);
        _content.Children.Add(AssetPanel(body, 20));
        if (SelectedIsRemix)
        {
            Details("Percorso grafico della copia", "Questa copia usa RTX Remix. Le regolazioni DLSS e il profilo si gestiscono nella pagina dedicata. ReShade, TexWizard, catture DDS e correzioni ai modelli non fanno parte della procedura rapida di questa copia.");
            return;
        }
        if (SelectedIsUnderground2) RenderRemixTrial();
        else Card("Strumenti per questo gioco", "GamePanel mostra le funzioni compatibili con il titolo selezionato. I pacchetti specifici di altri giochi, come asfalto o modelli auto, non vengono proposti qui. La sezione DLSS indica i componenti rilevati e l'eventuale percorso di gestione disponibile.");
    }

    private void RenderGraphicsComponentSummary()
    {
        var lines = new List<string>();
        if (SelectedIsRemix) lines.Add("RTX Remix · percorso della copia rilevato. Profilo e ripristino sono nella sezione DLSS.");
        if (_fxState?.State?.Installed == true) lines.Add("Immagine e filtri · profilo ReShade gestito.");
        if (_screenState?.State?.Installed == true) lines.Add("Schermo · Widescreen Fix gestito.");
        if (_directXState?.Installed == true) lines.Add("Texture · componente DirectX gestito.");
        Card("Profili e componenti", lines.Count > 0 ? string.Join("\n", lines)
            : _report is null ? "Seleziona un gioco dalla libreria per leggerne lo stato."
            : "Nessun profilo grafico gestito in questa cartella. Apri Configura per vedere i percorsi disponibili; i componenti riconosciuti dall'analisi restano consultabili in Diagnosi.");
    }
}
