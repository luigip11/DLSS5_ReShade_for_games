# Primo collaudo Windows — M0

Nessuna modifica grafica in questa alpha. Non è richiesto installare ReShade, DLL, wrapper o driver per il collaudo dell'analisi.

1. Estrarre il pacchetto completo in una cartella dell'utente e avviare GamePanel.exe senza elevazione.
2. Verificare leggibilità e navigazione a scala schermo 100%, 125% e 150%; ridimensionare la finestra.
3. Selezionare l'EXE di Underground 1, poi di Underground 2. Registrare architettura e API dichiarata. Controllare che il gioco non parta durante la selezione.
4. Selezionare l'EXE di un gioco moderno. Se le API non vengono determinate, annotare se è un launcher anziché l'eseguibile del renderer.
5. Aprire DLSS/HDR e verificare che nessun controllo dichiari una mod installata o una funzione attiva.
6. Aprire Diagnosi e controllare componenti individuati contro la cartella reale. Assenza di DLL rilevate non prova assenza di DLSS.
7. Esportare JSON in Documenti. Verificare che contenga il percorso selezionato, hash e note; controllarlo prima di condividerlo.
8. Provare un file di testo rinominato .exe: deve comparire un errore, senza chiusura inattesa.
9. Provare annullamento analisi e chiusura finestra durante l'analisi.
10. Solo se si intende eseguire il gioco: usare Avvia senza modifiche. I titoli che richiedono argomenti/launcher dedicati potrebbero richiedere ancora l'avvio consueto.

Raccogliere: versione di Windows, driver NVIDIA, adattatori rilevati, GPU usata dal gioco, display interno/esterno e supporto HDR indicato dalle impostazioni Windows; risultato per ciascun punto. Per il prossimo blocco servono anche due titoli moderni specifici con versione/build.

Da completare prima di dichiarare M0 collaudato: avvio effettivo su Windows 11, DPI, drag/drop, dialoghi, enumerazione GPU e report su giochi reali. I controlli sintetici del core non sostituiscono queste prove.
