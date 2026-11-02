# Underground 2 Remix: analisi locale, profilo renderer da verificare

Aggiornamento alpha.12: questo documento descrive l’analisi preliminare. Sono ora disponibili [l’audit dei componenti](REMIX-COMPONENT-REVIEW.md) e una [prova locale su copia separata](REMIX-TRIAL.md), con AntiCull escluso e compatibilità da collaudare.

GamePanel può analizzare l'archivio originale **Underground 2 Remix 0.4** e
produrre un piccolo inventario JSON. L'analisi non installa il mod, non estrae
file, non modifica il gioco e non avvia DLL, ASI, script o eseguibili.

Scaricare personalmente il file dalla [pagina dell'autore su ModDB](https://www.moddb.com/mods/need-for-speed-underground-2-remix/downloads/underground-2-remix-04),
selezionarlo con **Analizza archivio Remix** e salvare il report JSON. Il report
si può condividere per proseguire l'audit senza caricare l'intero archivio.
Non contiene il percorso locale dell'archivio, il percorso del gioco o il nome
dell'utente del computer: elenca solo i percorsi relativi contenuti nel 7z.

## Identificazione della sorgente

La pagina dell'autore pubblica questi dati, consultati il 1 novembre 2026:

| Campo | Valore |
|---|---|
| Versione | 0.4, caricata il 18 luglio 2025 |
| File | `Underground_2_Remix_0.4.7z` |
| Dimensione | 2.117.971.904 byte |
| MD5 dichiarato | `e5b57ad2ea50d4ff22dbdd408c20a322` |

Questi byte non sono stati scaricati e verificati nell'ambiente di sviluppo.
L'analizzatore calcola SHA-256 e MD5 insieme, leggendo una volta tutto il file.
Una dimensione diversa viene rifiutata subito. Se l'MD5 non corrisponde,
`MatchesPublishedSource` è falso e l'inventario rimane vuoto: il parser 7z non
interpreta la tabella di un archivio sconosciuto. Il nome del file non è una
prova di provenienza.

`MatchesPublishedSource = true` significa esclusivamente corrispondenza con i
dati dichiarati sulla pagina. L'MD5 non è una firma digitale; lo SHA-256 locale
non è confrontato con un pin indipendente. Nessuno dei due risultati certifica
sicurezza, diritti di redistribuzione o compatibilità con un EXE.

## Contenuto del report e limiti

Schema 1: versione della sorgente, dimensione e hash dell'archivio, esito del
confronto, percorsi relativi, dimensioni dichiarate e cartelle, presenza di un
`d3d9.dll` esattamente alla radice del 7z e avvertenze. Un'eventuale cartella
contenitore dell'autore viene conservata: il report non decide come installarla.

Il parser legge la tabella, senza aprire i flussi dei singoli file. Non verifica
i CRC dei contenuti o gli hash dei singoli membri e non effettua un'analisi
antimalware. I limiti sono 3 GiB per l'archivio, 16 MiB per la tabella 7z
memorizzata, 30.000 voci, 2 GiB per membro e 12 GiB dichiarati complessivi.
Sono rifiutati percorsi assoluti, traversal, nomi ambigui o duplicati anche con
maiuscole diverse, collisioni tra file e cartelle, link e file speciali,
attributi reparse, voci di cancellazione, cifratura, archivi incompleti e volumi
separati. Non viene cercato o caricato automaticamente un volume adiacente.

Il report è il primo inventario per un successivo audit: non abilita alcun
pulsante di installazione e non è un manifest attendibile da eseguire.

## Cosa cambia il mod

Il [changelog 0.4 dell'autore](https://www.moddb.com/mods/need-for-speed-underground-2-remix)
descrive geometrie nuove per quasi tutto il traffico, segnali, cartelli,
barili, spartitraffico, vegetazione, palme, lampioni e vasi; aggiunge geometrie
dei fari delle auto iniziali, materiali, texture e illuminazione tramite RTX
Remix. Non documenta una sostituzione completa dei personaggi, dei corpi di
tutte le auto giocabili o dell'intera città.

## Cosa manca per un profilo installabile

Le istruzioni dell'autore richiedono una copia vanilla aggiornata fuori da
Program Files, la copia dei contenuti nella cartella del gioco e la
configurazione di WidescreenFix e delle opzioni grafiche illustrate. Non
indicano hash, regione o layout degli EXE supportati. Non prescrivono una
sostituzione dell'EXE. La compatibilità con l'EXE CD esistente rimane da provare;
l'esito dei controlli TexWizard non prova né esclude il supporto di Remix.

Il [Bridge di NVIDIA](https://github.com/NVIDIAGameWorks/bridge-remix/blob/main/README.md)
occupa normalmente `d3d9.dll` nella cartella del gioco e usa il runtime a 64 bit
sotto `.trex`. Il profilo GamePanel con ReShade usa già quel percorso. Serve
quindi un profilo renderer separato con ripristino transazionale e coordinamento
di Texture Toolkit, UAL e WidescreenFix; la sovrascrittura diretta non è
un'installazione gestita. Nessuna compatibilità simultanea è stata dimostrata.

La [README del repository fissata al commit ad48949](https://github.com/Ekozmaster/NFSU2-RTX-Remix/blob/ad48949c8ffa27258d08758fc90474636b0d56f3/README.md)
contiene istruzioni per Remix 0.2 e vecchi artefatti CI: non identifica i
componenti della distribuzione ModDB 0.4. Il repository MIT contiene inoltre
[materiali NVIDIA con intestazione EULA](https://github.com/Ekozmaster/NFSU2-RTX-Remix/blob/ad48949c8ffa27258d08758fc90474636b0d56f3/rtx-remix/mods/gameReadyAssets/02_materials/AperturePBR_Opacity.mdl).
La sola licenza MIT alla radice non dimostra che ogni dipendenza sia MIT.

Prima di automatizzare un download o un'installazione occorrono l'inventario
effettivo, l'audit dei componenti e delle licenze, pin SHA-256 verificati e un
test reversibile con l'EXE funzionante dell'utente. L'analisi locale permette di
ottenere il primo di questi elementi senza trasferire 2 GB nell'ambiente cloud.
