# Primo collaudo Windows — M0

Nessuna modifica grafica in questa alpha. Non è richiesto installare ReShade, DLL, wrapper o driver per il collaudo. Da alpha.3 la pagina Backup conserva copie su richiesta e recupera file mancanti.

1. Estrarre il pacchetto completo in una cartella dell'utente e avviare GamePanel.exe senza elevazione.
2. Verificare leggibilità e navigazione a scala schermo 100%, 125% e 150%; ridimensionare la finestra.
3. Incollare il percorso completo dell'EXE di Underground 2 nel campo dell'app, prima senza virgolette e poi con le virgolette di "Copia come percorso". Provare sia **Analizza percorso** sia **Invio**; entrambi devono analizzare lo stesso file. Ripetere scegliendo l'EXE con **Seleziona gioco .exe**. Registrare architettura e API dichiarata. Il gioco non deve partire durante l'analisi. Ripetere con Underground 1.
4. Selezionare l'EXE di un gioco moderno. Se le API non vengono determinate, annotare se è un launcher anziché l'eseguibile del renderer.
5. Aprire DLSS/HDR e verificare che nessun controllo dichiari una mod installata o una funzione attiva.
6. Aprire Diagnosi e controllare componenti individuati contro la cartella reale. Assenza di DLL rilevate non prova assenza di DLSS.
7. Esportare JSON in Documenti. Verificare che contenga il percorso selezionato, hash e note; controllarlo prima di condividerlo.
8. Provare un file di testo rinominato .exe: deve comparire un errore, senza chiusura inattesa.
9. Provare annullamento analisi e chiusura finestra durante l'analisi.
10. Solo se si intende eseguire il gioco: usare Avvia senza modifiche. I titoli che richiedono argomenti/launcher dedicati potrebbero richiedere ancora l'avvio consueto.

Raccogliere: versione di Windows, driver NVIDIA, adattatori rilevati, GPU usata dal gioco, display interno/esterno e supporto HDR indicato dalle impostazioni Windows; risultato per ciascun punto. Per il prossimo blocco servono anche due titoli moderni specifici con versione/build.

Da completare prima di dichiarare M0 collaudato: avvio effettivo su Windows 11, DPI, drag/drop, dialoghi, enumerazione GPU e report su giochi reali. I controlli sintetici del core non sostituiscono queste prove.

Per alpha.2 verificare anche che modificare il campo dopo un'analisi disabiliti l'esportazione e rimuova il pulsante di avvio del risultato precedente. Provare il trascinamento dell'EXE sia nel campo percorso sia nel resto della finestra. Un percorso mancante deve produrre **File non trovato** e restare correggibile nel campo.

Per alpha.3, con il gioco chiuso, usare **Backup → Crea backup → Verifica e mostra anteprima**. Il primo collaudo sul gioco reale deve terminare con file già presenti e identici: non cancellare file del gioco per provarlo. Per testare il recupero usare una copia separata del gioco in una cartella scrivibile. I passaggi e i limiti sono in [BACKUP](BACKUP.md). Verificare anche che le copie restino elencate dopo la chiusura e riapertura dell'app, usando **Carica backup del percorso**.
