# GamePanel 0.1.0-alpha.7

La prima installazione di alpha.7 è manuale: scaricare `GamePanel-win-x64.zip` dalla release e **estrarre tutto in una cartella nuova e scrivibile**, fuori dai giochi. Avviare GamePanel.exe. La libreria esistente è già nei dati utente e viene ritrovata, purché si usi lo stesso account Windows e la stessa cartella dati.

## Aggiornamenti

In basso nella sidebar premere **Controlla aggiornamenti**. Il controllo avviene in background senza aprire browser o console; non parte da solo all'avvio. La risposta corrente è **Software già aggiornato**. Una versione maggiore disponibile apre la domanda **Sì / No**, con No predefinito. Solo Sì scarica e prepara l'aggiornamento. Alla fine GamePanel si chiude, un helper separato sostituisce i file propri dell'app e la riapre.

Il controllo usa le release pubblicate di `luigip11/DLSS5_ReShade_for_games`, incluse le prerelease quando l'app è alpha. Non considera gli artifact Actions e non propone downgrade. Le versioni stabili ricevono solo release stabili. Rete assente, limite GitHub, nessuna release e pacchetto incompleto hanno messaggi distinti: non sono presentati come software aggiornato.

Libreria, preferenza schermo, componenti scaricati e backup sono fuori dalla cartella app, normalmente in `%LOCALAPPDATA%\GamePanel`. L'updater sostituisce/rimuove solo file elencati nel vecchio manifest e aggiunge quelli nel nuovo. Conserva file extra; se un file proprio è stato alterato o collide con un file personale, si ferma. Rifiuta una cartella dati sovrapposta all'app. Non cancella cartelle dei giochi.

Prima della chiusura verifica hash e possibilità di scrivere nella cartella dell'app. Ogni download verifica dimensioni, digest GitHub e SHA-256 dichiarato nei metadata. Lo ZIP ammette solo file inventariati e percorsi relativi, senza collegamenti o voci duplicate. L'helper conserva copie precedenti e journal in `Updates`, può riprendere un aggiornamento pendente con la stessa richiesta o tenta il rollback se fallisce. Una modifica esterna non prevista blocca anche il rollback: le copie restano disponibili. Non interrompere volontariamente l'helper.

Se l'app è in una cartella protetta, aggiornare quella copia può richiedere l'avvio come amministratore. È più semplice tenerla in una cartella scrivibile dedicata. La firma GamePanel rimane assente: questi controlli di integrità non risolvono Smart App Control e non certificano un editore. Nessuna protezione Windows viene disattivata.

## Schermo

La pagina **Schermo** legge da Windows gli schermi attivi e la risoluzione attuale in pixel fisici. Quando disponibile legge anche la modalità preferita del monitor. Il valore non è più fissato a 3440×1440.

Automatico sceglie lo schermo che contiene la finestra di GamePanel; si può scegliere esplicitamente il Dell esterno nel menu e salvare la preferenza. **Rileva schermi** aggiorna l'elenco dopo aver collegato/spostato uno schermo. Se quello salvato è assente, l'app lo segnala e usa un altro schermo attivo. In clonazione più pannelli possono condividere la stessa sorgente Windows: il rilevamento rappresenta una sorgente attiva.

La lettura non cambia Windows e non forza il gioco a trasferirsi sul monitor. Per Underground 2 il Widescreen Fix rimane separato: scegliere nel menu video del gioco la risoluzione mostrata. Il log ReShade può confermare l'ultima risoluzione utilizzata, non una misurazione in tempo reale. Non viene rilevata o attivata una conversione HDR.

## Altri giochi

| Funzione | Disponibilità |
| --- | --- |
| Libreria, analisi, avvio e backup | Altri EXE importati; renderer/launcher non sempre identificabili |
| ReShade SDR Originale/Bilanciato/Cinema | Sperimentale per `speed2.exe`, `speed.exe` (UG1/MW2005), `nfsc.exe`, `gta_sa.exe`, soltanto x86 con import diretto D3D9 |
| Widescreen Fix installabile da GamePanel | Solo Underground 2 |
| Texture/modelli HD, RTX Remix, DLSS/HDR | Non installati da questa versione |

Il nome del file non certifica l'identità del gioco. Restano i controlli su API, architettura, anti-cheat, file preesistenti e hash dell'EXE. Non sono accettati automaticamente giochi DX8, wrapper o renderer dinamici. Most Wanted indica **2005**, San Andreas la versione **classica**, non Definitive Edition. Gli altri giochi non sono stati eseguiti nei test automatici: sono usate fixture DX9 sintetiche.

Il profilo cambia colori, nitidezza, contrasto, bloom e vignettatura. Non aumenta la geometria e non sostituisce le texture delle strade. Per il passaggio successivo consultare [texture e modelli](GRAPHICS-ASSETS.md). [Analisi dell'archivio SweetFX](LEGACY-SWEETFX.md).

## Verifiche

Test del core: selezione SemVer/canali, metadati e download, ZIP ostili, file alterati, dati preservati, interruzione/ripresa/rollback e journal grandi. Il workflow Windows compila entrambi gli eseguibili, prova l'helper su un pacchetto reale isolato, verifica la UI, legge uno schermo CI e ripete il ciclo ReShade/Widescreen con fixture sintetica. Non sostituisce il collaudo sul monitor Dell o sui giochi reali. Gli esiti effettivi sono nei log della specifica build.

In caso di arresto del PC durante la sostituzione, conservare `Updates` e chiudere tutte le finestre GamePanel. Nella sottocartella dell'operazione ci sono `request.json`, `install-journal.json`, `before` e `ready`. Il comando `& '.\ready\GamePanel.Updater.exe' --request '.\request.json'`, eseguito da PowerShell in quella sottocartella, riprende la stessa operazione; aggiungere `--rollback` per recuperare la versione precedente. Non usare richieste o helper ricevuti da fonti diverse dal proprio aggiornamento verificato. Un journal già Complete o RolledBack non viene riapplicato.
