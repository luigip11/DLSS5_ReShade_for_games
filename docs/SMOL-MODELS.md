# Smol: preparazione e installazione locale

Il pulsante prepara il RAR originale di AJ_Lethal, verificato con SHA256
`385e5a706266b252618b4018cba8970c14084481a3d6700d76b7f26df044282e`
(18.424.342 byte). Se il sito rifiuta il download, si può selezionare lo stesso RAR
già scaricato. SharpCompress 0.50.7 legge i dati direttamente: nessun programma
esterno e nessuno script END viene eseguito.

Parser distribuito: [SharpCompress 0.50.7](https://www.nuget.org/packages/SharpCompress/0.50.7),
licenza MIT; copyright pacchetto © 2025 Adam Hathcock. Il repository del pacchetto
è fissato al commit `b4af8afe6683d019d504954a3b4e4f18b06e2cd6`.
Il [testo della licenza](SHARPCOMPRESS-LICENSE.txt) conserva l'avviso originale.

Si importano soltanto le geometrie di Golf, IS300 e Sentra. Il pacchetto Models
abbina le texture già presenti nel gioco, copiate senza modifiche nella cache
locale. I readme inglese e spagnolo sono conservati byte per byte come file di
testo; i crediti indicano AJ_Lethal. Il pacchetto contiene dati del gioco e deve
rimanere locale. Non include un remaster ad alta definizione.

La versione completa del mod richiede Binary >=2.5.0 e modifica database e
contenitori texture. A3, Mustang, RSX, ruote, modifiche GLOBALB, Unlimiter e suoni
sono esclusi da questa integrazione. Le correzioni installate coprono grille e
badge Golf, marcatore scarico IS300, mapping/normali/griglie e marcatori Sentra.
Non richiedono TexWizard o cambiamenti all'EXE; la compatibilità con il vecchio
layout TexWizard rimane una questione separata.

La lettura RAR è limitata a 32 MiB di archivio, 128 voci e 64 MiB di dati
decompressi. Si rifiutano link, traversal, nomi duplicati, file speciali, cifratura
e volumi multipli; il CRC di tutti i file viene controllato. Geometrie e readme
devono corrispondere agli hash dell'audit. L'archivio viene riletto e verificato
prima e dopo la lettura, anche riaprendo il percorso per rilevare sostituzioni.

La preparazione non scrive nel gioco. Prima dell'installazione si verificano
nuovamente gli hash dell'eseguibile e dei sei file auto originali. Il servizio
Models esistente gestisce piano, backup, attivazione/disattivazione e rimozione.
I test usano RAR4 STORE sintetici; la verifica facoltativa dell'archivio autore
usa `--verify-smol-archive <percorso.rar>`. Questi controlli verificano contenitori,
conservazione dei dati e rollback del servizio; non sostituiscono una prova
visiva nel gioco.

L'autore consente modifica/distribuzione con credito e readme originale
inalterato. Questa autorizzazione non concede diritti di ridistribuire le texture
originali del gioco. Nessun file del gioco o del mod è incluso nella release.
