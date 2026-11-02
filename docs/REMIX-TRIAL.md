# Prova locale Underground 2 Remix 0.4 — alpha.12

Questa funzione prepara una copia del gioco con il renderer e gli asset dell'archivio dell'utente. Non modifica il gioco originale, non cambia l'EXE e non avvia il gioco. Non è una conferma che Remix funzioni con l'EXE CD: AntiCull è incompatibile e viene escluso, quindi possono mancare oggetti o parti della città.

## Sul PC Windows

1. Chiudere il gioco. Estrarre tutta la nuova build GamePanel in una cartella dedicata; libreria e download restano nei dati utente.
2. In Libreria scegliere **Analizza** sul gioco originale funzionante. Il Widescreen Fix deve risultare già installato e gestito in Schermo, senza operazioni da recuperare. Non sostituire l'EXE.
3. Aprire **Texture e modelli → Da installare → Prova RTX Remix su una copia → Prepara copia di prova**. Scegliere il proprio `Underground_2_Remix_0.4.7z`, poi creare/selezionare una cartella completamente vuota, per esempio `C:\Games\UG2-Remix-Test`. Deve essere fuori dal gioco originale, Program Files, cartelle dell'app e dati profilo. L'archivio originale da circa 1,97 GiB è necessario; lo ZIP di componenti da pochi MiB non contiene tutti gli asset.
4. Controllare destinazione, spazio richiesto e avvisi nell'anteprima. Scegliere **Crea copia e applica Remix** e attendere. L'operazione legge diversi GB e può richiedere tempo. Interrompi annulla la creazione; la destinazione finale viene pubblicata solo al termine.
5. Dopo **Copia creata, da provare**, scegliere **Seleziona EXE copia**. La copia viene analizzata e può essere avviata dalla sua Panoramica. Rimangono necessari CD/requisiti dell'originale. Nessun avvio automatico.

La copia conserva il Widescreen Fix e la sua configurazione corrente: sul profilo dell'utente l'obiettivo resta **3440×1440**. Il builder non forza la risoluzione di Windows e non cambia le opzioni video globali del gioco. Verificare la modalità effettiva sul Dell all'avvio.

Usare **Interrompi operazione** e attendere il completamento dell'annullamento prima di chiudere GamePanel. Se Windows termina il processo improvvisamente, può restare una cartella temporanea `.gamepanel-remix-<identificativo>` accanto alla destinazione scelta. Non è una copia completata. A GamePanel e gioco chiusi si può eliminare quella cartella temporanea, identificata dal file `.gamepanel-remix-owner` e dal percorso scelto; il gioco originale non richiede ripristino.

I salvataggi in Documenti e le impostazioni nel registro possono essere condivisi. La separazione riguarda la cartella del gioco, non l'intero profilo Windows: fare una copia dei propri salvataggi prima del collaudo se si vuole conservarne lo stato.

## Che cosa viene copiato

- Dati del gioco e lo stesso `speed2.exe`, verificati prima e dopo la copia. Eventuali modelli Smol già applicati vengono conservati: gli hash delle mesh potrebbero non coincidere con il profilo dell'autore, da verificare visivamente.
- Loader `dinput8.dll`, ASI, DAT e INI del Widescreen Fix già gestito. I binari devono corrispondere al catalogo GamePanel; l'INI corrente viene conservato.
- Runtime, materiali, scene e licenze del 7z locale, mantenendo la struttura interna dopo la cartella esterna dell'autore. Si conserva `rtx.conf`; si imposta la singola chiave attiva `rtx.useFreeCamera = false` in `dxvk.conf`.

Non si copiano ReShade, Texture Toolkit, TexWizard e TexturePacks dal gioco. Dall'archivio Remix si escludono il loader e tutti gli `scripts`, incluso AntiCull: non vengono rinominati o attivati gli `.asii`. Plugin originali sconosciuti e proxy in conflitto bloccano la preparazione. Il builder rifiuta una sorgente già Remix.

`GamePanel-RemixTrial.json` nella copia registra sorgente, archivio, hash dei file estratti e finali e avvisi. Le vecchie operazioni GamePanel restano associate alla cartella originale: la copia è una prova separata, non una seconda installazione gestita di ReShade o dei pack precedenti. Per tornare al gioco precedente basta selezionare l'originale in libreria. Dopo averla chiusa si può eliminare solo la cartella della prova.

## Limiti effettivi

La prova richiede il CD EXE identificato SHA-256 `3233ef990fe5ac6a554ec65b821e7a5231c955c3419fe7e910e11d336cce1471` e il 7z SHA-256 `dc17cc4a462935c129e08b84ee13251a4405abd91350363127f79287d36cc37f`. Questi hash identificano i file esaminati; non sono una firma dell'editore.

Il builder è verificato su archivi e giochi sintetici. Qui non sono stati avviati il gioco, AntiCull o i runtime caricati dall'utente; il 7z completo resta sul suo PC. La prova reale deve confermare avvio, menu, HUD, gara, auto, oggetti e stabilità. Nel preset Frame Generation è disabilitata; la presenza di DLL DLSS e opzioni RR non dimostra che siano attivi. Non è incluso un nuovo pack completo HUD/menu o personaggi moderni.

Nessun file del gioco o del pacchetto Remix viene redistribuito con GamePanel. La build alpha resta senza firma Authenticode: questa funzione non risolve il blocco di Smart App Control. [Audit statico](REMIX-COMPONENT-REVIEW.md).
