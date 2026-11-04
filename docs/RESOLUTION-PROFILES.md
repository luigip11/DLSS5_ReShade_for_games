# Risoluzione per gioco — alpha.15

Da **Grafica → Schermo e risoluzione** scegli il monitor, una delle modalità che Windows dichiara disponibili su quel monitor e il metodo di applicazione. La selezione del monitor da sola non modifica il gioco. **Prepara** genera l’anteprima; **Applica al gioco** salva il profilo o modifica i file elencati. Windows mantiene la propria modalità video. Il profilo non sposta automaticamente una finestra sul monitor selezionato.

## Metodi

| Metodo | Effetto | Limite |
| --- | --- | --- |
| Underground 1 | Scarica il fix ThirteenAG fissato per hash e installa loader, ASI, DAT, INI e TPK; salva larghezza/altezza nel formato nativo `RES.cfg2`. | Solo l’EXE esaminato SHA-256 `c0fad450912952f53809a54690d235aa8b4e25b9f46650693c7cbee1b4c074b7`, gioco originale completo e senza loader/plugin ASI precedenti. Prova sul PC ancora necessaria. |
| Unity | All’avvio da GamePanel passa `-screen-width`, `-screen-height`, `-screen-fullscreen 1` come argomenti separati. | Il gioco deve accettare questi argomenti; può ignorarli. |
| Unreal | All’avvio da GamePanel passa `-ResX=`, `-ResY=` e `-Fullscreen`. | Il gioco deve accettare questi argomenti; può ignorarli. |
| INI/CFG personalizzata | Aggiorna soltanto le due chiavi esistenti della sezione scelta in un file di testo UTF-8. | Serve conoscere file, sezione e nomi delle chiavi. JSON, XML, codifiche diverse e configurazioni ambigue richiedono un adattatore dedicato. |

Il riconoscimento Unity tramite UnityPlayer e cartella dati suggerisce il metodo; gli altri giochi possono essere configurati manualmente. Nessun flag universale o script arbitrario viene eseguito. Un gioco non riconosciuto non viene dichiarato compatibile automaticamente. La dimensione salvata è una richiesta: controllare il risultato nelle opzioni video o in un log del renderer. Non misura risoluzione interna, upscaling o FPS.

## Underground 1

Fonte: [release ThirteenAG nfsu](https://github.com/ThirteenAG/WidescreenFixesPack/releases/tag/nfsu), archivio SHA-256 `c4ea2a23caa5f34f52afd35a8a1e4da58a439eafe45dc605ceaa6685fc7e69b9`, 8.264.601 byte. Ogni membro installato ha un hash verificato; i binari non sono inclusi nel pacchetto GamePanel. È disponibile anche la selezione dello ZIP locale, con gli stessi controlli.

Formato risoluzione verificato nel [sorgente associato alla pubblicazione](https://github.com/ThirteenAG/WidescreenFixesPack/blob/dd0d6c9e52433647315ffee9c5aba6023178dc9a/source/NFSUnderground.WidescreenFix/Resolution.ixx): il valore scritto in `%ProgramData%\NFS Underground\RES.cfg2` impacchetta larghezza e altezza con il bit alto attivo. Il file RES.cfg2 è condiviso dalle eventuali copie di Underground 1 dello stesso utente. Non vengono scritte obsolete chiavi ResX/ResY nell’INI. Il fix sceglie le modalità che il runtime riesce a enumerare: verificare la combinazione di scheda video e monitor nel gioco.

Il profilo limita le modifiche a schermo/HUD: conserva il percorso dei salvataggi, il flusso iniziale e il comportamento di camera/controller. ReShade e lingua non vengono modificati. Le varianti Remix richiedono la propria integrazione. Il Widescreen Fix UG2 già presente nel pannello conserva il flusso esistente; questo nuovo adattatore nativo riguarda UG1.

## Ripristino e recupero

I profili sono separati per percorso EXE in `%LOCALAPPDATA%\GamePanel\Settings\GameResolutions`. Prima delle scritture vengono salvati i contenuti precedenti e un journal. Un’operazione Pending blocca l’avvio da GamePanel e offre completamento o annullamento nella pagina Schermo. Cambiamenti ai file dopo l’anteprima vengono rifiutati. Una nuova anteprima acquisisce eventuali modifiche alle configurazioni di testo come nuovo punto di ripristino, conservando le chiavi estranee; l’INI del fix UG1 già personalizzato viene mantenuto. I binari alterati restano rifiutati. Durante la rimozione, i file modificati fuori da GamePanel restano conservati e vengono indicati nell’anteprima.

Per passare a un altro metodo o file di configurazione rimuovere prima il profilo precedente. I giochi vanno chiusi prima di applicare o recuperare. Directory protette possono richiedere permessi di scrittura: il pannello non eleva automaticamente i privilegi né modifica le protezioni di Windows.
