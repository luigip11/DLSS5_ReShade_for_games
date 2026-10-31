# Verifiche prima della prima pubblicazione

Data: 29 ottobre 2026. Ambiente di sviluppo Linux; nessuna GPU Windows disponibile per eseguire l'app.

- SDK .NET 10.0.403 scaricato da builds.dotnet.microsoft.com; SHA-512 confrontato con i metadata Microsoft prima dell'estrazione.
- Core: 11/11 controlli eseguiti e superati, 0 saltati, 0 falliti. Coprono PE x86/x64, import grafici, conservazione dei file analizzati, hash/report, rifiuto di input non validi, cancellazione, indicatori anti-cheat e collegamenti di directory.
- Pubblicazione WPF win-x64 self-contained con runtime 10.0.14: riuscita, nessun warning o errore riportato.
- Restore in modalità locked del progetto Windows: riuscito.
- XML dei progetti e manifest: analizzato correttamente.
- GitHub Actions principali fissate a SHA letti dai rispettivi repository ufficiali.

Ancora da verificare: esecuzione della pipeline su Windows e dello script PowerShell, UI/DPI/drag-drop, avvio, dialoghi, adattatori GPU, analisi dei giochi reali, supporto HDR del display, scansione antivirus del pacchetto e firma Authenticode. Il workflow fornisce evidenza separata sulla propria pagina; la sua riuscita non prova il funzionamento nei giochi.

Nessuna integrazione grafica è installata in questa alpha. Non esistono ancora test reali di DLSS/HDR o backup/restore perché tali funzioni non sono implementate.

## Sviluppo alpha.3 — 30 ottobre 2026

I dati sopra descrivono la prima pubblicazione, non lo stato di alpha.3. Per alpha.3 sono implementati backup mirati e recupero dei file mancanti. Prima della pubblicazione sono passati 44/44 controlli su Linux, inclusi copie persistenti, hash originali recuperati, conflitti, corruzione, annullamento, ripresa da temporanee parziali, errore filesystem, journal, limiti di dimensione e percorsi non ammessi.

La pipeline Windows esegue gli stessi controlli, compila l'app e usa UI Automation per incollare un percorso quotato, creare un backup e recuperare un EXE di prova eliminato. L'EXE di prova è una copia dell'apphost compilato e non viene eseguito. Viene eseguito soltanto GamePanel. Lo script verifica l'hash del file recuperato e pulisce soltanto le proprie cartelle di prova. L'esito effettivo è nei log della build.

Restano separati: collaudo del backup sul gioco reale, permessi della sua cartella Program Files, DPI e interazioni manuali, Smart App Control e firma, antivirus, tutti i percorsi grafici DLSS/HDR/ReShade. Non sono stati simulati un disco fisicamente pieno o una reale perdita di alimentazione.

## Alpha.4 — revisione dell'interfaccia e preparazione della firma

Il motore del core non cambia. La pipeline mantiene i 44 controlli e il ciclo GUI di analisi, backup e recupero; aggiunge screenshot della vera finestra e un controllo negativo che impedisce di considerare firmata l'alpha ordinaria. La compilazione locale del restyling WPF è stata eseguita; gli esiti Windows effettivi e le immagini sono negli artifact della build.

Il workflow di firma di produzione è solo predisposto. Non viene avviato dai push e, senza le variabili e il secret di un provider approvato, si ferma prima di contattarlo. La verifica positiva di un pacchetto realmente firmato e il collaudo con SAC attivo restano bloccati dalla mancanza del servizio di firma. Il successo dell'avvio sul runner Windows non dimostra che il file sia accettato da SAC sul PC dell'utente.

### Esito verificato su Windows

La [build alpha.4 del commit b9a582b](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/runs/41090217812) ha completato 44/44 controlli, avvio GUI, analisi del percorso quotato, creazione della copia, apertura/chiusura del selettore ridisegnato e recupero con SHA-256 originale. Le schermate native sono state esaminate per correggere il titolo iniziale e l'etichetta del selettore dei backup. Stato della firma registrato dal runner: `NotSigned`; il controllo di firma respinge correttamente il pacchetto ordinario. Nessun esito positivo di SAC o dei giochi reali viene dedotto da questi test.

## Alpha.5 — test del profilo SDR

71/71 controlli locali superati su Linux, inclusi installazione/rimozione dei quattro file, EXE invariato, rifiuto di loader e wrapper preesistenti, nuovo anti-cheat, report obsoleto, parametri e Originale, configurazione/preset cambiati, interruzione a ciascuno dei quattro passi con ripresa/annullamento da nuova istanza, rollback di update/rimozione, payload corrotto, traversal, processi, symlink, archivio dentro gioco, redirect, dimensioni dichiarate/effettive ingannevoli, SHA-256 errato e annullamento prima del download. Le fixture unitarie non caricano DLL né eseguono EXE.

Il controllo Windows aggiunge il download reale approvato e la cache (72 controlli), poi GUI completa con una fixture DX9 x86 compilata dal codice del repository. Dopo installazione gestita avvia soltanto quel renderer sintetico e cerca nel log ReShade la compilazione riuscita di GamePanelSDR.fx; se il dispositivo DX9 non è disponibile, il rendering viene esplicitamente indicato non verificato. Gli esiti effettivi sono nei log della build, non dedotti dalla compilazione locale. Il test continua con rimozione tracciata ed EXE invariato.

Restano necessari sul PC reale: lancio della propria build Underground 2, gara/menu/filmati/Alt+Tab, qualità visiva e costo GPU, DPI, mod/ASI già presenti, permessi Program Files e SAC. Le prove non sono una scansione antivirus o una certificazione di compatibilità.

### Build alpha.5 verificata

La [build del commit bd8f080](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/runs/41188200644) ha superato 72/72 controlli su Windows, il download reale e l'intero ciclo GUI: anteprima senza scritture, backup iniziale, installazione, modifica del cursore nel preset, Originale disabilitato/Bilanciato ripristinato, aggiornamento dopo riscrittura INI da parte di ReShade e rimozione con EXE invariato. La fixture x86 Direct3D 9 è stata effettivamente eseguita: il runtime ufficiale ha caricato e compilato `GamePanelSDR.fx`; il rendering non è stato saltato. Non sono stati eseguiti giochi reali.

La build registra `NotSigned` per GamePanel e il controllo di produzione rifiuta correttamente il pacchetto non firmato. Le schermate del pannello sono state esaminate. Test e immagini non dimostrano SAC o compatibilità con una build reale del gioco.

## Alpha.6 — verifiche in sviluppo

92/92 controlli locali passati, inclusa ispezione statica dell’archivio widescreen originale già scaricato. Nuove verifiche: persistenza/deduplica/nomi/ricollegamento/rimozione solo metadata, corruzione e lock del database, schema e symlink, lettura del log vincolata all’EXE, schema alpha.5 e migrazione byte-esatta di shader/preset, journal widescreen con interruzione a ogni passo, blocco del profilo peer e conservazione INI esterni, URL/redirect e archivio non approvati. Su Windows il download reale di entrambi i componenti aggiunge un controllo: 93 previsti, esito effettivo nei log della build.

La prova GUI usa una root dati temporanea, non la libreria personale. Verifica riapertura e selezione di giochi salvati, Cinema/Originale e pixel di una superficie DX9 sintetica, installazione/rimozione widescreen statiche. L’ASI specifico del gioco non viene eseguito nella fixture generica. Il confronto di un pixel dimostra un effetto sul renderer di prova, non un miglioramento estetico certificato, un FPS reale del gioco o la compatibilità ultrawide dell’EXE dell’utente.

### Alpha.6 — esito Windows verificato

[Build f6bc813](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/runs/41194571942): **93/93 controlli superati, 0 saltati**. GUI completa, download approvati, backup/recupero, Cinema e aggiornamento dopo riscrittura della configurazione, rimozioni, riapertura con libreria persistente, scelta e avvio dell'EXE dalla scheda con timestamp salvato. Il controllo di avvio usa una sonda nel nostro EXE sintetico, indipendente dal dispositivo grafico; il suo handle è mantenuto aperto per leggere correttamente il codice di uscita su Windows PowerShell.

Nel test grafico dedicato, il runtime x86 DX9 ha caricato e compilato lo shader. Pixel renderizzato misurato nello stesso punto della fixture: Cinema **55/131/206**, Originale **64/128/192**, differenza assoluta complessiva 26. Il test non è stato saltato. Il Widescreen Fix è stato scaricato, verificato, installato e rimosso senza eseguire l'ASI specifico del gioco. La compatibilità sul vero Underground 2 e la modalità 3440×1440 rimangono da verificare sul Dell dell'utente. GamePanel risulta ancora `NotSigned` e il gate di produzione respinge la build non firmata.
