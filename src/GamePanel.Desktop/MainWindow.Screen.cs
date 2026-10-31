using System.IO;
using System.Windows;
using System.Windows.Controls;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private readonly ReShadeProfileService _widescreen = ReShadeProfileService.ForWidescreen(ReShadeProfileService.DefaultStore);
    private readonly WidescreenPackageSource _wideSource = new(ReShadePackageSource.DefaultCache);
    private ProfileStatus? _screenState;
    private ProfilePlan? _screenPlan;
    private string _screenMessage = "Analizza Underground 2 per preparare il Widescreen Fix.";
    private RuntimeEvidence? _runtimeEvidence;
    private Button? _screenApplyButton;
    private void ResetScreen()
    { _screenState = null; _screenPlan = null; _runtimeEvidence = null; _screenMessage = "Analizza Underground 2 per preparare il Widescreen Fix."; }
    private async Task RefreshScreenAsync(string path)
    {
        try { var state = await Task.Run(() => _widescreen.Status(path)); if (!_closing) { _screenState = state; _screenMessage = state.Message; } }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { _screenState = null; _screenMessage = e.Message; }
    }
    private void RenderScreen()
    {
        _screenApplyButton = null;
        Heading("Più spazio. Più definizione.", "Underground 2 · Widescreen Fix di ThirteenAG · integrazione sperimentale.");
        var target = new StackPanel(); target.Children.Add(DesignSystem.Chip("IL TUO DELL ULTRAWIDE", Accent));
        var resolution = Text("3440 × 1440", 36, Ink, FontWeights.Bold); resolution.Margin = new Thickness(0, 12, 0, 6); target.Children.Add(resolution);
        target.Children.Add(Text("Obiettivo del profilo: risoluzione nativa, proporzioni corrette, HUD e campo visivo adattati.", 13, Muted));
        _content.Children.Add(new Border { Background = Surface, BorderBrush = DesignSystem.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 18), Child = target });
        var status = Text(_screenMessage, 13, Muted); status.Margin = new Thickness(0, 0, 0, 18);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "ScreenStatus"); _content.Children.Add(status);
        if (_screenState?.Pending == true)
        {
            _content.Children.Add(FxButton("Riprendi operazione schermo", "ResumeScreen", async (_, _) => await RecoverScreenAsync(false), !_busy));
            _content.Children.Add(FxButton("Annulla operazione schermo", "RollbackScreen", async (_, _) => await RecoverScreenAsync(true), !_busy));
        }
        else
        {
            var reason = _report is null ? "Seleziona un gioco dalla libreria." : ReShadeProfileService.Eligibility(_report);
            if (reason is not null) Card("Disponibilità", reason);
            var pendingPeer = _fxState?.Pending == true;
            var prepare = FxButton(_screenState?.State?.Installed == true ? "Prepara rimozione Widescreen Fix" : "Scarica fix e prepara anteprima", "PrepareScreen",
                async (_, _) => await PrepareScreenAsync(_screenState?.State?.Installed == true), !_busy && reason is null && !pendingPeer);
            prepare.Background = Accent; prepare.Foreground = BackgroundBrush; _content.Children.Add(prepare);
            if (pendingPeer) Card("Prima recupera il profilo immagine", "Un'operazione ReShade è interrotta. Apri HDR / FX e riprendila o annullala.");
            if (_screenPlan is not null)
            {
                Card(_screenPlan.Removing ? "Rimozione pronta" : "Installazione pronta", string.Join("\n", _screenPlan.Changes.Select(c => $"{(c.AfterSha256 is null ? "Rimuovi" : "Aggiungi")} · {c.Path}"))
                    + (_screenPlan.PreservedFiles.Count == 0 ? "" : "\nConservati perché modificati: " + string.Join(", ", _screenPlan.PreservedFiles)));
                var apply = FxButton(_screenPlan.Removing ? "Conferma rimozione fix" : "Applica Widescreen Fix", "ApplyScreen", async (_, _) => await ApplyScreenAsync(), !_busy);
                _screenApplyButton = apply;
                apply.Background = Accent; apply.Foreground = BackgroundBrush; _content.Children.Add(apply);
            }
        }
        Card("Dopo l'applicazione", "Avvia il gioco sul Dell e apri le sue opzioni video. Seleziona 3440 × 1440 e conferma. Questo fix aggiunge le modalità del monitor al menu: la sola installazione non conferma la risoluzione usata. Non cambiamo la risoluzione o lo schermo principale di Windows.");
        var read = FxButton("Leggi risoluzione dell'ultimo avvio", "ReadRuntimeEvidence", (_, _) => ReadRuntimeEvidence(), !_busy && _report is not null); _content.Children.Add(read);
        if (_runtimeEvidence is not null)
        {
            var e = _runtimeEvidence;
            Card("Ultimo log ReShade · " + e.LogWrittenAt.LocalDateTime.ToString("dd MMM · HH:mm"),
                (e.Width is int w && e.Height is int h ? $"Risoluzione dichiarata: {w} × {h}." : "Risoluzione non determinata nel log.")
                + (e.Width == 3440 && e.Height == 1440 ? " Obiettivo presente nel log." : " Verifica 3440 × 1440 nel menu video del gioco.")
                + (e.ShaderCompiled ? "\nShader GamePanel compilato." : "\nCompilazione shader non confermata dal log."));
        }
        Details("Compatibilità e file esistenti", "Il pacchetto originale proviene dal repository ufficiale ThirteenAG, con dimensione e SHA-256 fissati. Il fix usa un ASI loader separato da ReShade. DLL dinput8/version/winmm/dsound preesistenti, cartelle scripts con mod o wrapper sconosciuti bloccano l'installazione. La build reale di speed2.exe va collaudata: il fix modifica il gioco in memoria e non riscrive l'EXE. La combinazione ReShade + fix non è ancora collaudata sul tuo PC.");
        Details("Cosa cambia e cosa resta originale", "Si installano quattro file: dinput8.dll, scripts/NFSUnderground2.WidescreenFix.asi, il relativo .dat e .ini. La configurazione abilita correzioni HUD/FOV e nasconde il bordo di 1 pixel legato a MSAA. Le opzioni per lettore ottico, salvataggi/registro, camera e controller sono disabilitate. Non sono inclusi texture pack, modelli nuovi, ray tracing, DLSS o HDR. La firma di GamePanel e la fiducia Windows dei componenti rimangono da risolvere.");
    }
    private async Task PrepareScreenAsync(bool remove)
    {
        if (_report is null) return; var report = _report; _screenPlan = null;
        await RunFxTaskAsync(async (token, progress) =>
        {
            if (remove) return _widescreen.PrepareRemove(report, token);
            var package = await _wideSource.PrepareAsync(token, progress); return _widescreen.PrepareWidescreen(report, package, token);
        }, plan => { _screenPlan = plan; _screenMessage = "Anteprima pronta. Nessun file del gioco modificato."; }, "Preparazione Widescreen Fix");
    }
    private async Task ApplyScreenAsync()
    {
        var plan = _screenPlan; var report = _report; if (plan is null || report is null) return;
        await RunFxTaskAsync((token, progress) =>
        {
            if (!plan.Removing) _backups.Create(report, token, progress);
            return Task.FromResult(_widescreen.Execute(plan, token, progress));
        }, state =>
        {
            _screenPlan = null; _screenState = new(state, false, "");
            _screenMessage = state.Installed ? "Fix installato. Ora scegli 3440 × 1440 nelle opzioni video del gioco."
                : "Widescreen Fix rimosso." + (state.PreservedFiles.Count > 0 ? " INI modificato conservato." : "");
        }, "Applicazione Widescreen Fix");
    }
    private async Task RecoverScreenAsync(bool rollback)
    {
        if (_report is null) return; var path = _report.ExecutablePath;
        await RunFxTaskAsync((token, progress) => Task.FromResult(_widescreen.Recover(path, rollback, token, progress)), state =>
        { _screenState = new(state, false, ""); _screenPlan = null; _screenMessage = rollback ? "Operazione schermo annullata." : "Operazione schermo completata."; }, "Recupero profilo schermo");
    }
    private void ReadRuntimeEvidence()
    {
        if (_report is null) return;
        try { _runtimeEvidence = RuntimeEvidence.Read(_report.ExecutablePath); _screenMessage = "Letto il log locale dell'ultimo avvio; non è una misura in tempo reale."; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { _runtimeEvidence = null; _screenMessage = "Log non verificabile: " + e.Message; }
        Render();
    }
}
