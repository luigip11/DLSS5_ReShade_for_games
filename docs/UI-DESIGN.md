# Interfaccia gaming — alpha.4

## Direzione visiva

Workspace scuro con superfici blu carbone, accenti lime e blu elettrico, illustrazione vettoriale originale e tipografia Segoe UI/Bahnschrift disponibile su Windows. Nessun font web, wallpaper remoto, logo di terzi o immagine di gioco scaricata automaticamente.

La panoramica dà priorità a selezione EXE, analisi, dati rilevati e accesso al backup. La scheda del gioco usa il nome della cartella o dell'EXE come etichetta; non identifica con certezza il titolo dal solo percorso.

## Decisioni di UX

- Un'azione primaria per analizzare l'EXE; scelta file e incolla sempre visibili, con supporto Invio.
- Navigazione con icone e stato selezionato; DLSS/HDR esplicitamente in sviluppo.
- Metriche solo dal report: architettura, prima API dichiarata e numero di componenti. Nessun FPS, carico GPU o guadagno prestazionale simulato.
- Backup con azioni e anteprima prima delle spiegazioni secondarie; dettagli di recupero e permessi espandibili.
- Focus, pressione e disabilitazione riconoscibili; icone accompagnate da testo; stato lungo abbreviato con contenuto completo nel tooltip.
- Titolo Windows scuro solo per la finestra dell'app: non cambia tema globale, registro o protezioni.
- Icona vettoriale originale e grafica statica per limitare il lavoro grafico durante i giochi.

## Verifica

La pipeline prova avvio, percorso quotato e ciclo backup/ripristino con UI Automation. Produce le immagini reali `00-overview.png` e `01-backup.png` nell'artifact **GamePanel-interface-preview**. Esito e screenshot appartengono alla specifica build; non sono mockup.

Restano da collaudare sul PC reale le diverse scale DPI, il display e Smart App Control. Il restyling non attribuisce fiducia Windows: alpha.4 resta non firmata finché non è completato il percorso in SIGNING.md.
