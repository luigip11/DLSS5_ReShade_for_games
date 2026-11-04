# Underground 1 Remix: configurazione essenziale

GamePanel gestisce una mod **già installata**: non scarica il pacchetto v0.5, non distribuisce EXE del gioco e non certifica il nome/versione dell'archivio. Riconosce l'EXE UG1 già analizzato e una struttura locale con client x86, server/bridge x64, loader e plugin UG1 Remix. Registra gli hash locali dei componenti per impedire scritture se vengono sostituiti.

1. Conserva una copia originale funzionante. Installa la mod in una cartella separata seguendo le indicazioni del suo autore.
2. Importa lo `Speed.exe` di quella cartella in GamePanel. Apri **Grafica → Profilo e DLSS** e registra la configurazione esistente. L'adozione non cambia i file del gioco e non richiede un manifest creato da GamePanel per UG2.
   Per visualizzare originale e Remix nella stessa scheda, importa entrambi, poi sulla scheda della copia scegli **Collega come Remix**, seleziona il gioco originale e conferma. Il collegamento è esplicito e modifica soltanto la libreria; non dipende dall'adozione del profilo. La scheda mostrerà il selettore **Originale / Remix**. **Separa variante** ripristina due schede distinte.
3. La risoluzione esistente resta conservata. Per cambiarla apri **Schermo e risoluzione**, scegli le dimensioni del monitor e prepara l'anteprima. Per il Widescreen Fix della mod il metodo INI è già predisposto su `[MAIN] ResX/ResY`. Cambiano solo quei valori, con ripristino; non viene installato il fix nativo di alpha 15, che usa un formato differente.
4. Controlla le **GPU rilevate**. Per assegnare la scheda al gioco apri **Preferenze GPU di Windows** e scegli l'EXE di questa copia. L'elenco non misura quale GPU sta renderizzando. I valori `d3d9.customDeviceId`/`customVendorId` in `dxvk.conf` sono gli identificativi presentati al gioco e restano conservati.
5. Prima della prova controlla le opzioni video originali del gioco: il README v0.5 richiede tutti i dettagli su **High**. Questa indicazione riguarda il menu video di UG1, non il preset del renderer Remix. Conserva la risoluzione personale impostata nel fix.
6. Scegli i parametri DLSS, prepara l'anteprima e conferma. Vengono modificati solo `rtx.conf` e l'eventuale `user.conf` esistente. Dopo il riavvio verifica le opzioni effettive con Alt+X.

Super Resolution e Ray Reconstruction richiedono RTX rilevata e le rispettive DLL x64 nel runtime. Il controllo Transformer richiede anche il parametro dichiarato nella configurazione locale: non viene inventato per renderer precedenti. Frame Generation 2× richiede RTX 40/50 rilevata e il componente corrispondente. La compatibilità 3×/4× del runtime di questa mod non è stata verificata: eventuali valori già presenti restano conservati, ma GamePanel non ne propone l'attivazione. Presenza delle DLL e chiavi configurate non provano il funzionamento nel gioco.

Il preset grafico collaudato per UG2 non compare su UG1. Illuminazione, texture, modelli, DLL, ASI, `dxvk.conf`, lingua e salvataggi non vengono modificati dai controlli DLSS. Se cambi il runtime dopo la registrazione, GamePanel blocca le modifiche finché l'installazione non torna coerente; non eliminare journal o backup per aggirare il controllo.

## Italiano

Una modifica al registro o `ShowLangSelect=1` senza effetto non identifica da sola la causa. Prima di altre modifiche bisogna confrontare la cartella effettivamente avviata, l'INI attivo, il plugin e la vista del registro a 32 bit; un export a 64 bit può essere diverso. Non rinominare o sovrascrivere i file di lingua.

Nel pacchetto portabile trovi `Docs/Diagnosi-UG1.ps1`. Il comando seguente, da PowerShell, chiede la cartella contenente lo `Speed.exe` avviato e crea `GamePanel-UG1-diagnosi.txt` sul Desktop:

```powershell
& '.\Docs\Diagnosi-UG1.ps1'
```

Lo script legge file, hash, alcune opzioni INI e le chiavi EA di lingua/installazione nelle viste del registro a 32 e 64 bit. Non avvia EXE/DLL e non scrive nel gioco o nel registro. Il report contiene percorsi locali, da controllare prima della condivisione. Se Windows blocca lo script, non occorre cambiare le protezioni: si possono raccogliere manualmente gli stessi dati.

## Scena molto scura

Il file v0.5 esaminato imposta `ignoreGamePointLights`, `ignoreGameSpotLights` e `ignoreGameDirectionalLights` su `True`, con `fallbackLightMode=0`. Se i contenuti/luci sostitutivi sono disabilitati, può mancare l'illuminazione prevista dalla mod. Questo è un indizio di configurazione, non una diagnosi ricavabile dalla sola immagine.

Prima di cambiare esposizione o intensità: apri **Alt+X → Content**, abilita **Enable All Enhanced Assets** e verifica **Enable Enhanced Lights**, salva e riavvia completamente il gioco. Se le voci erano già attive oppure la scena resta scura, servono configurazioni effettive e log per verificare il caricamento dei contenuti. Dopo una prova riuscita, **Salva configurazione attuale** in GamePanel registra il nuovo punto di ripristino senza riscrivere il gioco.

## Confronto tra avvio diretto e GamePanel

Se lo stesso EXE funziona avviato da Esplora file, conserva quella configurazione. Confronta il percorso nella pagina Gioco e usa gli stessi privilegi: un GamePanel avviato come amministratore può avviare il gioco elevato, a differenza dell'avvio normale, con possibili differenze nella lettura di file e registro virtualizzati. Per il confronto chiudi entrambi e apri GamePanel normalmente. Il manifest dell'app usa già `asInvoker`; eventuali opzioni di compatibilità di Windows o del collegamento possono imporre l'elevazione.

Il selettore Originale/Remix aggiorna anche l'analisi attiva e i profili delle pagine Gioco/Grafica/DLSS. La cartella di lavoro del processo è quella dell'EXE; l'avvio non applica preset grafici o modifiche alle luci.

### Avvio tramite desktop di Esplora file

Per l'EXE UG1 identificato con `.trex` e `rtx.conf`, il pulsante Gioca richiede l'avvio alla shell desktop già esistente, indicando esplicitamente la cartella del gioco e nessun argomento aggiuntivo. In questo modo il processo non viene creato direttamente dal contesto di GamePanel. La richiesta non cambia luci, file di configurazione, preferenze GPU o compatibilità Windows. Se la shell non è disponibile, l'app segnala l'errore senza cambiare silenziosamente modalità. Per gli altri giochi la pagina Gioco offre anche **Avvia tramite Esplora file**; i profili con argomenti Unity/Unreal richiedono l'avvio standard.

L'ultimo tentativo viene registrato in `%LOCALAPPDATA%\GamePanel\Diagnostics\last-launch.json`: percorso EXE, cartella di lavoro, argomenti, privilegi del pannello, hash dei file di configurazione e una lista limitata di variabili del renderer del pannello. Non viene esportato l'intero ambiente. Per la shell il risultato `Requested` indica che la richiesta è stata accettata: non misura l'effettivo caricamento della mod né i privilegi del processo figlio. Il report contiene percorsi locali e va controllato prima della condivisione.
