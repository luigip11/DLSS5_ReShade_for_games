# Underground 2 Remix 0.4: inventario ricevuto

Aggiornamento alpha.12: questo documento descrive l’analisi preliminare. Sono ora disponibili [l’audit dei componenti](REMIX-COMPONENT-REVIEW.md) e una [prova locale su copia separata](REMIX-TRIAL.md), con AntiCull escluso e compatibilità da collaudare.

Analisi del solo `GamePanel-RemixAudit.json` (schema 1), senza archivio, estrazione,
installazione o esecuzione dei componenti. I percorsi sotto sono relativi alla
cartella contenitore `Underground 2 Remix 0.4/`, conservata nel report.

| Dato dichiarato dal report | Valore |
|---|---|
| Archivio | 2.117.971.904 byte |
| SHA-256 calcolato localmente | `dc17cc4a462935c129e08b84ee13251a4405abd91350363127f79287d36cc37f` |
| MD5 | `e5b57ad2ea50d4ff22dbdd408c20a322` |
| Confronto con la fonte pubblicata | `MatchesPublishedSource = true` |
| Voci / file / cartelle | 2.924 / 2.636 / 288 |
| Dimensione totale dichiarata dei file estratti | 5.208.169.777 byte |

Il JSON trasmette l'esito del calcolo effettuato sul PC; qui non è possibile
ricalcolare l'hash dell'archivio. Non contiene CRC verificati né SHA-256 dei
singoli membri. La corrispondenza MD5 con la pagina dell'autore identifica la
sorgente dichiarata; non certifica sicurezza, compatibilità o redistribuibilità.

## Risultati per le priorità del progetto

- **Interfaccia Full HD:** sono presenti WidescreenFix ASI, DAT e INI. Non emerge
  un pacchetto dedicato di texture HD per menu/HUD, né un'impostazione 1920×1080
  verificata. Il contenuto dell'INI deve essere letto prima di proporre valori.
- **Città e strade:** l'inventario contiene materiali per ambiente, strade bagnate,
  autostrade, tunnel, segnaletica e vegetazione, oltre a geometrie di oggetti
  stradali, ambiente e garage. La presenza dei nomi non dimostra una ricostruzione
  completa della città o che ogni layer sia attivo nella scena.
- **Auto:** sono presenti materiali, geometrie e layer di fari/luci; la cartella
  mesh include nomi delle auto giocabili e traffico. Questo non dimostra la
  sostituzione integrale di tutte le carrozzerie. Copertura e riferimenti
  effettivi vanno verificati nei layer USD e nel gioco.
- **Personaggi:** `adamplayer.usda` è solo un nome di layer. Il report non prova
  personaggi nuovi, qualità delle animazioni o sostituzioni complete.
- **DLSS:** i nomi `nvngx_dlss.dll`, `nvngx_dlssd.dll` e `nvngx_dlssg.dll` sono
  presenti nel runtime; non provano che DLSS, Ray Reconstruction o Frame
  Generation funzionino con l'EXE e la GPU dell'utente.

| Gruppo osservato | File | Byte dichiarati |
|---|---:|---:|
| `.trex/` | 66 | 389.399.630 |
| `rtx-remix/` | 2.555 | 4.814.917.638 |
| `scripts/` | 6 | 511.582 |
| `02_materials/01_environment/` sotto gameReadyAssets | 401 | 526.067.007 |
| `02_materials/02_cars/` | 287 | 973.428.537 |
| `03_meshes/01_props/` | 202 | 757.735.863 |
| `03_meshes/02_environment/` | 109 | 78.974.687 |
| `03_meshes/03_cars/` | 192 | 142.878.552 |

## Catena renderer e punti da verificare

`HasRootD3d9 = false` descrive la radice del 7z: il file esiste nella cartella
contenitore come `d3d9.dll` (854.016 byte). Sono inoltre presenti
`.trex/NvRemixBridge.exe` (1.104.384), `.trex/d3d9.dll` (40.285.696),
`NvRemixLauncher32.exe` (137.216) e `dinput8.dll` (2.170.368).
Questo layout è coerente con un client Bridge per gioco a 32 bit e un runtime a
64 bit; architettura e identità dei binari non sono state verificate dai byte.

La [documentazione NVIDIA del runtime](https://github.com/NVIDIAGameWorks/rtx-remix/blob/main/README.md)
distingue l'applicazione che prepara gli asset dal runtime che intercetta il
rendering e integra path tracing, DLSS e Reflex. La
[documentazione dei binari](https://github.com/NVIDIAGameWorks/dxvk-remix/blob/main/documentation/RemixRuntimeBinaries.md)
descrive il passaggio client `d3d9.dll` → server `.trex/NvRemixBridge.exe` →
runtime `.trex/d3d9.dll`. Queste fonti spiegano l'architettura corrente, senza
identificare la versione inclusa nel pacchetto 0.4. Texture e geometrie da sole
non aggiungono DLSS al renderer originale: occorrono runtime funzionante,
percorso di rendering supportato, GPU/driver adatti e verifica della funzione
in gioco. Il catalogo di asset Full HD e il profilo renderer Remix restano
quindi due attività da verificare separatamente.

Il client occuperebbe lo stesso percorso del `d3d9.dll` del profilo ReShade.
`dinput8.dll` va identificato e confrontato con UAL già gestito da GamePanel.
Non è dimostrata una catena simultanea con ReShade o Texture Toolkit.

`scripts/NFSU2AntiCull.asi` misura 84.992 byte. **Non ci sono sorgenti AntiCull
nell'inventario**: i `.cpp` e `.h` presenti sono template USD NVIDIA, non codice
della patch di gioco. Non si può stabilire da questo report se AntiCull
richieda NFSU2 1.2, quali indirizzi motore modifichi o se controlli il layout
prima di applicare patch. Occorrono audit statico del binario, sorgente upstream
identificata per quella build e confronto con l'EXE funzionante dell'utente.
Nessuna compatibilità va inferita dai controlli TexWizard precedenti.

## Prossimo audit limitato a meno di 8 MiB

Una selezione esatta di **44 file, 6.227.713 byte dichiarati (5,94 MiB)** permette
un primo audit di configurazione, caricamento e licenze:

1. Tutti i file regolari immediati nella cartella contenitore: client, loader,
   launcher, `dxvk.conf`, `rtx.conf`, `LICENSE.txt`, `license_widescreenfix.txt`,
   `ThirdPartyLicenses-bridge.txt`, `ThirdPartyLicenses-dxvk.txt`.
2. I sei file `scripts/*`, incluso il nome esatto
   `NFSU_XtendedInput.asii` (non rinominarlo in `.asi`).
3. `.trex/bridge.conf`, `.trex/NvRemixBridge.exe` e `.trex/XESS_LICENSE.txt`.
4. `rtx-remix/mods/gameReadyAssets/README.md`, tutti i suoi layer immediati
   `*.usda` (escluso `mod.usda.bak`) e i cinque file immediati
   `02_materials/*.mdl`, per leggere riferimenti e intestazioni di licenza.

L'insieme esclude i grandi runtime e le texture. Selezionare tutti i file con
estensione DLL/EXE/ASI/ASII richiederebbe 392.280.584 byte. La selezione proposta
non contiene sorgenti AntiCull e non basta a certificare il runtime completo.
Non si deduce una licenza unica per il pacchetto dalla sola `LICENSE.txt`.

Il comando **Esporta componenti Remix per audit** prepara uno ZIP con questa
allowlist di nomi e dimensioni esatte, più `GamePanel-RemixComponents.json` con
SHA-256 e dimensione effettiva di ogni membro. Occorre selezionare nuovamente
il 7z locale originale, scegliere un nuovo ZIP fuori dalla cartella del gioco
e allegarlo alla conversazione per proseguire l'audit statico di binari,
configurazioni e licenze. Nessun file viene caricato automaticamente.

L'esportatore riconferma dimensione, MD5 e SHA-256 della sorgente; il pin SHA-256
proviene dal report locale ricevuto, senza una certificazione indipendente.
Controlla la tabella, rifiuta link e percorsi ambigui, verifica lo stesso stream
sorgente prima e dopo la lettura dei componenti e limita i dati selezionati a
8 MiB (ZIP massimo 12 MiB). Scrive prima un file provvisorio adiacente alla
destinazione, lo pubblica solo a operazione completata e non sostituisce un
file esistente. L'annullamento elimina il provvisorio.

La lettura di un archivio solido può dover decomprimere anche dati non
selezionati: l'output rimane piccolo, ma l'operazione può richiedere tempo.
DLL, EXE e ASI restano esclusivamente dati nello ZIP: non sono caricati,
eseguiti o installati. Il bundle permette la prossima verifica; non rende
Remix compatibile con l'EXE e non costituisce un profilo installabile.
