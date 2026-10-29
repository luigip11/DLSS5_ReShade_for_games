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
