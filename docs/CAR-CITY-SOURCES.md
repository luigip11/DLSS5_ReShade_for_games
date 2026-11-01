# Auto e geometrie cittadine: fonti e limiti verificati

Ricerca aggiornata al 1 novembre 2026. Nessun pack di modelli reale è stato incluso o dichiarato compatibile senza poterne esaminare i file. Texture Toolkit sostituisce texture; non converte o sostituisce mesh 3D.

| Fonte dell'autore | Modifica effettiva | Requisiti e decisione |
| --- | --- | --- |
| [The smol model fix, AJ_Lethal](https://nfsmods.xyz/mod/2721) | Correzioni mirate ai modelli delle auto originali: mapping, normali, ruote e collisioni. Non un remaster high-poly. | Richiede il gioco vanilla e Binary 2.5+. La pagina permette modifica/distribuzione con credito e readme conservato. Il RAR non è stato accessibile: inventario, script e dipendenze non sono stati verificati. Nessun pack pronto nell'app. |
| [Underground 2 Remix, UncleBurrito](https://www.moddb.com/mods/need-for-speed-underground-2-remix) | Nuovi modelli del traffico e di oggetti cittadini, fra cui segnali, lampioni, palme e vegetazione; renderer con path tracing. | La versione 0.4 richiede l'intero stack RTX Remix e una configurazione distinta da ReShade. L'archivio di circa 2,12 GB non è stato ispezionato; compatibilità e licenze dei singoli asset non sono attestate. Non applicabile come pack Texture Toolkit. |
| [Sorgenti NFSU2-RTX-Remix, Ekozmaster](https://github.com/Ekozmaster/NFSU2-RTX-Remix) | Scene e asset USD/DDS con vere nuove mesh di arredo urbano. | Repository MIT, commit esaminato `ad48949c8ffa27258d08758fc90474636b0d56f3`; molti file sono riferimenti Git LFS. Serve RTX Remix, non basta rinominare o copiare questi file come BIN. Eventuale riuso richiede inventario e controllo delle licenze dei componenti. |
| [NFSU2 ForceLOD2, Lucent-Mods](https://github.com/Lucent-Mods/NFSU2_ForceLOD2) | Mantiene visibili livelli di dettaglio originali più alti; non aggiunge nuove geometrie. | Sorgente MIT ma dipendente da indirizzi specifici del motore v1.2. Non validato per la build dell'utente: nessun pulsante di installazione. |

## Auto importabili oggi

GamePanel può installare con backup coppie `CARS/<AUTO>/GEOMETRY.BIN` e `TEXTURES.BIN` per vetture già presenti, quando un pacchetto revisionato è disponibile nel [formato GamePanel](ASSET-PACKS.md). Un archivio Binary/END non è un pacchetto già convertito: può richiedere operazioni sul database o altri file non supportati dall'importatore.

Per preparare il primo pack bisogna ottenere l'archivio originale dell'autore, leggere istruzioni e licenza, verificare i target, escludere le dipendenze incompatibili e collaudare il risultato. Non vengono redistribuiti file originali del gioco per completare coppie mancanti.

## Geometrie della città

Il progetto Remix è una fonte concreta da cui studiare o derivare un profilo futuro. L'integrazione richiede però un renderer separato, reversibile e collaudato, con risorse USD e materiali propri. La presenza di una GPU RTX non verifica automaticamente la compatibilità della copia di gioco e delle mod esistenti.

Questa versione consegna il percorso texture DirectX sperimentale e i collegamenti alle fonti; **non consegna un aggiornamento delle mesh cittadine o delle auto**. I download di mod non verificate non vengono attivati come se fossero pronti.
