# Immagine SDR, ReShade e HDR

In Grafica > Configura, ReShade e HDR sono percorsi distinti. Le copie Remix usano il proprio renderer; ReShade non viene sovrapposto automaticamente.

## ReShade generale

Il percorso attuale richiede un EXE reale x86 con import Direct3D 9, scansione completa e nessun indicatore anti-cheat. Non e limitato al nome di quattro giochi. La compatibilita va provata sul titolo specifico; DX11/DX12 e x64 non ricevono questo loader x86.

Preset: Originale disabilita la tecnica; Nitido usa sola nitidezza moderata; Bilanciato regola nitidezza e colore; Cinema aggiunge contrasto, bloom e vignettatura. Lo shader SDR di GamePanel non legge la profondita. Ambient occlusion, profondita di campo e conversione HDR non vengono promesse dal preset generale.

Il componente viene scaricato esplicitamente dalla fonte ufficiale con versione, dimensioni e SHA-256 fissati. Il setup non viene eseguito. Anteprima e journal accompagnano l'installazione dei quattro percorsi previsti; DLL o configurazioni sconosciute preesistenti bloccano la sovrascrittura. Rimozione e ripristino conservano le modifiche esterne riconosciute.

## HDR

La pagina HDR e schermo legge da Windows lo stato del colore avanzato del monitor selezionato, quando disponibile. Questo dato non prova che il gioco stia producendo HDR. Il pulsante Windows apre Sistema > Schermo > HDR; GamePanel non modifica automaticamente la modalita colore, il driver o flag del registro.

HDR nativo va verificato nel gioco. Auto HDR resta subordinato a Windows, schermo e compatibilita; la sola analisi dell'EXE non ne dimostra l'attivazione. Saturazione e contrasto SDR non sono HDR.
