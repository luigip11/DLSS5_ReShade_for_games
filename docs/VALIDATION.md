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
