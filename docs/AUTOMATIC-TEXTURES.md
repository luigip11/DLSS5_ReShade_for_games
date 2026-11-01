# Texture automatiche — candidato alpha.10

Il flusso principale usa due pulsanti in **Texture e modelli**:

1. **Prepara tutte le texture** legge le catture già salvate da Texture Toolkit e genera un lotto locale, senza modificare il gioco.
2. **Installa texture preparate** installa il lotto verificato e, se completo e non annullato, attiva le sostituzioni. Il gioco deve essere chiuso.

Non occorre scegliere ogni hash o associare manualmente l'asfalto. Il componente DirectX deve essere già preparato; le catture esistenti vengono riutilizzate. Il codec e il lettore archivi sono inclusi nell'app: nessun programma esterno da installare.

## Risultato effettivo

Il processo interpola a 2× le texture catturate fino a 512×512, con uscita massima 1024×1024, compressione BC, trasparenza conservata e mipmap completi. Mantiene il contenuto originale di ogni superficie. Non usa un modello AI, non inventa dettagli e non è un pack artistico di asfalto 2K. Non ricostruisce geometrie o personaggi. L'immagine può apparire più morbida: un maggior numero di pixel non dimostra un miglioramento visivo.

Texture più grandi o di formato non supportato restano originali e vengono contate nel riepilogo. Le texture mai catturate non vengono sostituite. L'associazione usa l'hash verificato dei pixel originali, non una stima del tipo di superficie.

## Limiti e conservazione

La scansione automatica è distinta dalla lista manuale: legge fino a 2048 catture e 1 GiB di sorgenti, preparando al massimo 512 MiB di DDS. Mostra quantità, spazio, esclusioni e limiti raggiunti. Un lotto limitato richiede consenso esplicito e non viene attivato automaticamente.

La cache è in `%LOCALAPPDATA%\GamePanel\PreparedTextures`; le sostituzioni sono nella cartella `DirectXTextures` del gioco selezionato. EXE, cartella, manifest, catture e uscite sono vincolati tramite hash. La presenza di DDS non gestite nella cartella di destinazione blocca l'installazione prima delle scritture.

Ogni sostituzione ha copie e journal recuperabili. Un annullamento può lasciare una parte del lotto installata: il conteggio lo indica, e premere nuovamente Installa riprende senza duplicare i file già identici. Non viene dichiarato un rollback atomico di tutto il lotto. Se un singolo journal è interrotto, usare prima i controlli di recupero DDS. Per tornare alle texture del gioco usare la disattivazione delle sostituzioni DirectX; le catture originali restano conservate.

I test sintetici verificano colori, trasparenza, mipmap, oltre 256 catture, limiti, annullamento/ripresa, file estranei e modifica del manifest. La resa e il consumo di memoria nella copia reale del gioco richiedono una prova dell'utente.
