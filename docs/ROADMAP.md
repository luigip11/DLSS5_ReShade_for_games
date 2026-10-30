# Specifica concordata e avanzamento

## Prodotto richiesto

Portabile Windows 11 x64 per MSI Vector 16 AI HX con RTX 5080 Laptop. Selezione EXE anche fuori dagli store, gestione unificata DLSS/HDR/ReShade, parametri comprensibili, backup e ripristino. Supporto iniziale da collaudare su NFS Underground 1/2 e su due titoli moderni ancora da indicare. Il display HDR e la sua luminosità non sono ancora noti.

Il gestore non si occupa di acquisire giochi o modificare DRM. Un eseguibile modificato può essere analizzato come qualsiasi file; sicurezza e compatibilità dipendono dai byte effettivi, dalle DLL e dalla versione del gioco.

## M0 — Analisi portabile: implementato, Windows da collaudare

Interfaccia e core separati, analisi senza esecuzione, limiti espliciti, report locale. Import PE diretti, nessuna ricerca certa dei caricamenti dinamici o delay-load. La scansione si ferma entro tre livelli e 6000 elementi e non segue reparse point. Nessun risultato simula una feature attiva.

Criterio di uscita: build portabile verificata; analisi su EXE x86 e x64 reali in Windows; rapporto confrontato con gli effettivi file del gioco; interfaccia verificata a DPI diversi. La build Linux non prova questi requisiti.

## M1 — Modifiche recuperabili

**Alpha.3: implementata la prima parte**, con copie di riferimento persistenti, verifiche hash, anteprima, recupero dei mancanti e ripresa. Non sono ancora disponibili sovrascritture di file modificati o registri di installazioni grafiche: arriveranno prima del primo deployment reale. Il collaudo sul gioco dell'utente e guasti fisici come disco pieno/perdita di alimentazione restano da verificare. M1 non è dichiarato interamente concluso. Dettagli: [BACKUP](BACKUP.md).

- Manifest operazioni locale, vincolato a eseguibile, cartella canonica e hash.
- Archivio backup distinto dalla cartella d'installazione, preservato da uninstall/aggiornamenti.
- Journal scritto prima della modifica; copia su file temporaneo e sostituzione atomica dove applicabile.
- Controllo dei processi in esecuzione e dei file bloccati: in caso di dubbio, non scrivere.
- Identificazione DLL proxy, conflitti di loader e gestione dei reparse point anche durante le scritture.
- Restore rifiutato se un aggiornamento del gioco ha cambiato i file attesi; anteprima del conflitto.
- Test reali di guasti: disco pieno, interruzione, backup danneggiato, gioco aggiornato/spostato.

Criterio di uscita: round trip esatto degli hash in scenari sintetici e su una copia di gioco di prova; nessuna scrittura fuori dall'insieme approvato; crash recovery verificato. Nessun mod reale distribuito prima.

## M2 — Primo percorso reale DLSS + HDR/ReShade

- Catalogo locale versionato con origine, licenza, dimensione e hash approvato; download esplicito.
- Verifica identità/firma quando applicabile e hash prima di estrazione/installazione.
- Nessun URL/script arbitrario da manifest online; redirect limitati, estrazione protetta, limiti dimensione.
- Gestione DLSS nativa con versioni compatibili; SR/FG/RR/NR separati.
- Un solo percorso HDR attivo: HDR nativo, conversione scelta o profilo ReShade supportato.
- Lettura supporto HDR effettivo del display; scelta dell'adattatore e diagnosi GPU ibrida.
- Pannelli reali collegati a parametri documentati, riavvio segnalato quando necessario.
- Nessuna sovrascrittura silenziosa di mod già presenti.

Criterio di uscita: un titolo moderno e un titolo legacy verificati sul PC reale, con confronto prima/dopo, tempi frame, assenza di clipping HDR evidente e restore verificato. Questo non rende tutti i giochi supportati.

## M3 — Underground 1/2 e Neural Rendering legacy

Profili dedicati per build effettive dei due giochi. Base immagine/stabilità prima di helper neurali. Verifica che helper x64 e gioco x86 usino lo stesso adattatore NVIDIA. Pipeline con un solo consumer neurale compatibile, niente combinazioni presunte. dgVoodoo soltanto attraverso una modalità di acquisizione conforme ai diritti del produttore; non incorporarlo in un launcher generico senza autorizzazione.

Criterio di uscita: menu, gara, video, alt-tab, salvataggi e più sessioni stabili su ciascun titolo; parametri reali e ripristino documentati. Fino a quel momento: sperimentale.

## M4 — Distribuzione

ZIP portabile come distribuzione principale; installer opzionale solo quando utile, senza eliminare backup. Hash, build legata al commit, inventario dipendenze, scansioni e valutazione Authenticode. Gestione aggiornamenti manuale iniziale; nessun componente nuovo eseguito senza passare il catalogo approvato.

## Interfaccia finale

Gioco → profilo consigliato → anteprima modifiche → Applica e avvia. DLSS e HDR separati e collegati allo stesso profilo. Controlli avanzati chiusi, aiuto in una frase, reset al profilo consigliato, indicazione "riavvio necessario". Stato per gioco: verificato sulla build indicata / sperimentale / non determinato / incompatibile. Il semplice riconoscimento di una DLL non attribuisce lo stato "verificato".
