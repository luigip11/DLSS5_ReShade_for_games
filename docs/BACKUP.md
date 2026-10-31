# Backup di riferimento e recupero — alpha.3

## Primo uso sul gioco reale

1. Chiudere il gioco e analizzare il suo EXE.
2. Aprire **Backup** e premere **Crea backup**.
3. Premere **Verifica e mostra anteprima**. Se tutti i file esistono e sono identici, l'app verifica soltanto: non modifica la cartella del gioco.
4. Conservare la cartella `%LocalAppData%\GamePanel\Backups`. La copia resta disponibile dopo la chiusura dell'app e non è conservata dentro il pacchetto portabile.

Per il primo collaudo non eliminare o rinominare file del gioco reale. Il ciclo completo è verificato dalla pipeline su un file di prova. Eventuali prove di perdita/recupero manuale vanno effettuate su una copia del gioco in una cartella scrivibile.

## Contenuto e confini

Vengono salvati i byte attuali dell'EXE selezionato e dei componenti individuati nel report. Sono controllati anche i loader `d3d9.dll`, `dxgi.dll`, `d3d11.dll`, `d3d12.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `opengl32.dll` e i file `ReShade.ini`, `ReShadePreset.ini`, `dgVoodoo.conf`, `dxvk.conf` accanto all'EXE. Un file assente viene registrato come assente.

La copia rappresenta lo stato attuale scelto dall'utente: può includere mod già presenti o un eseguibile modificato. Non ricostruisce una versione originale del produttore. Non comprende tutti gli asset, i salvataggi, il registro, le ACL o i timestamp del gioco e non sostituisce un backup completo. L'inventario iniziale resta limitato dalla scansione dell'analizzatore.

Limiti: 256 percorsi, 512 MiB per file, 2 GiB per snapshot. Le copie incomplete restano in directory `.pending`, non sono offerte per il recupero e possono occupare spazio dopo un annullamento o un guasto. Non sono eliminate automaticamente. Non importare archivi o metadati forniti da altri utenti nella cartella di backup.

## Ripristino disponibile ora

- **Presente e identico:** verificato, senza riscriverlo.
- **Mancante e salvato nel backup:** proposta di recupero con anteprima.
- **Presente ma diverso, oppure comparso dopo il backup:** conflitto; l'intero recupero viene bloccato prima di iniziare.
- **Assente sia all'origine sia ora:** nessuna azione.

Non viene forzata la sovrascrittura di aggiornamenti, crack, preset o mod esterni. Da alpha.5 i quattro file del profilo SDR hanno un registro separato e un journal con entrambe le versioni, per ripresa o annullamento delle sole operazioni gestite. Questo non estende il backup generale alla sovrascrittura di file esterni. Vedere [RESHade-SDR](RESHade-SDR.md).

Se manca anche l'EXE, incollare il suo percorso originario, aprire **Backup** e usare **Carica backup del percorso**: non serve riuscire a rianalizzarlo. Il percorso deve corrispondere allo snapshot. Uno spostamento o una reinstallazione in un'altra cartella non vengono adattati automaticamente.

## Recupero interrotto

Il journal viene salvato prima delle scritture. I file mancanti vengono preparati su temporanei con nomi dell'operazione e creati attraverso rename senza sovrascrittura. La ripresa controlla nuovamente le copie e i file presenti. Una copia temporanea parziale viene ricreata dal backup verificato; le temporanee dell'operazione sono pulite al termine.

Un recupero di più file non è una transazione atomica unica: se fallisce a metà, gli originali già recuperati restano e il journal permette di riprendere. Il pulsante **Concludi ripristino interrotto** finalizza un recupero in cui tutti i file sono già tornati al loro posto. Il backup non viene consumato dal recupero.

## Protezioni e limiti delle garanzie

Collegamenti/junction, percorsi fuori root, nomi dispositivo e metadati malformati sono rifiutati. Le operazioni sono bloccate se risulta attivo un processo con il nome dell'EXE, anche se potrebbe appartenere a un altro percorso; un errore nel controllo processi blocca l'operazione. Un lock dell'archivio serializza gli scrittori GamePanel per gioco. I file presenti sono mantenuti aperti senza condivisione di scrittura/eliminazione su Windows durante il recupero.

Questi controlli non proteggono da un attaccante locale privilegiato che altera intenzionalmente filesystem e metadati, e non costituiscono una certificazione contro ogni guasto hardware o perdita di alimentazione. I test simulano annullamenti, perdita dell'ultimo completamento del journal, corruzione e ostacoli filesystem; non riempiono fisicamente il disco del runner.

La lettura per il backup normalmente non richiede elevazione. Il recupero richiede permessi di scrittura sulla cartella del gioco: sotto Program Files Windows può negarli. L'app segnala l'errore e conserva le copie; non cambia ACL, non richiede automaticamente elevazione e non modifica le protezioni di Windows.
