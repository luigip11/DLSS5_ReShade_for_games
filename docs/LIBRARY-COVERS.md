# Copertine della libreria

Il menu **Copertina**, sopra il titolo del gioco, offre due metodi:

- **Scegli immagine locale**: importa un PNG o JPEG fino a 12 MB. Funziona senza internet; il file originale viene conservato e una copia ridimensionata viene salvata in `%LOCALAPPDATA%\GamePanel\Library\Covers`.
- **Cerca automaticamente online**: apre la ricerca Wikipedia con il titolo del gioco già compilato. Richiede internet per ricerca e download, senza account o chiave API. Scegli il risultato, controlla l'anteprima e premi **Usa questa copertina**. I risultati possono includere loghi o immagini della voce: il titolo è modificabile per correggere la ricerca. Viene inviato solo il testo della ricerca, non l'EXE o il suo percorso.

Dopo il salvataggio la cover viene letta dalla cache locale, anche offline. Per quelle online il menu conserva **Fonte: Wikipedia**, con il rimando alla pagina originale e alle relative condizioni d'uso. Le immagini scaricate dall'utente non sono incluse nella distribuzione di GamePanel.

Originale e Remix condividono la cover della scheda del gioco originale. Rinomina e rianalisi mantengono l'identità della scheda. **Rimuovi copertina** ripristina l'illustrazione standard, senza modificare i file del gioco.

La ricerca dipende dalla raggiungibilità di Wikipedia/Wikimedia e dalla presenza di un'immagine adatta. Errori di rete non bloccano la libreria; resta disponibile l'importazione locale.
