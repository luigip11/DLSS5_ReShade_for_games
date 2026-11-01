# GamePanel 0.1.0-alpha.8

Questa versione aggiunge **Texture e modelli**, un gestore di pacchetti con inventario, anteprima, copie degli originali e recupero. Include il primo pacchetto GamePanel di asfalto 2K, con tre superfici originali. Non è un remaster completo della città e non include un pacchetto di geometrie già collaudato.

## Aggiornamento da alpha.7

Premere **Controlla aggiornamenti → Sì**. Si aggiorna l'app; libreria, componenti, backup e impostazioni restano nei dati utente. Se si scarica lo ZIP manualmente, estrarre tutto in una cartella nuova e dedicata usando lo stesso account Windows. Il pacchetto rimane senza firma Authenticode: non vengono cambiate le protezioni Windows.

## Provare l'asfalto 2K su Underground 2

1. Chiudere il gioco. Aprire la sua scheda dalla libreria.
2. Il Widescreen Fix gestito da GamePanel deve essere già installato nella pagina **Schermo**: viene riutilizzato il suo ASI loader, senza installare un secondo proxy.
3. Aprire **Texture e modelli** e leggere il risultato della compatibilità. TexWizard 1.3 richiede un layout specifico di speed2.exe x86 DX9. GamePanel controlla dimensione, PE, entry point, sezioni e tutti gli 82 punti di chiamata attesi. Una build diversa viene bloccata; non ci sono forzature o modifiche all'EXE.
4. Premere **Scarica asfalto 2K e prepara anteprima**. Si scaricano il pack GamePanel e, se necessario, il componente TexWizard dal repository dell'autore. Hash e dimensioni vengono verificati. L'anteprima non scrive nella cartella del gioco.
5. Controllare l'elenco dei file, poi premere **Conferma applicazione**. Se la cartella del gioco è protetta, Windows può richiedere di riaprire GamePanel come amministratore con lo stesso account. Non cambiare ACL o disattivare protezioni per il test.
6. Avviare il gioco dalla libreria. Verificare diverse strade, parcheggi e intersezioni a 3440×1440. La posizione effettiva delle tre superfici e i loro materiali richiedono ancora questa prova reale; non tutta la città cambia aspetto.
7. Per confrontare, chiudere il gioco e preparare **Disattivazione → Conferma**, poi riavviarlo. Scroll Lock riguarda soltanto ReShade, non il pack texture. **Rimozione → Conferma** elimina i file del pack gestito e il runtime texture quando non serve più.

Non copiare DLL del vecchio SweetFX sopra questa configurazione. Per rimuovere Widescreen Fix occorre prima rimuovere i pack texture: dipendono dal suo loader.

## Cosa contiene il primo pack

Tre texture procedurali originali MIT, 2048×2048 DXT1 con nove livelli mip fino a 8×8:

- `RDP_INTERSECTMIDTILE` → `GP_A8_ASPHALT_INTERSECT`
- `RDP_LANEC_NL_AA_KT` → `GP_A8_ASPHALT_LANE`
- `RDP_PARKING_NL_AA_KT` → `GP_A8_ASPHALT_PARKING`

I nomi originali sono presenti nell'elenco Underground 2 di NFSRaider fissato nel catalogo. Il contenitore è stato costruito e riletto con Nikki dai sorgenti MIT. Non sono copiati asset EA o texture di altri modder. UV, proporzioni originali e attributi materiali specifici non sono ricavati dalla copia di gioco dell'utente; la resa nel gioco resta sperimentale.

Il pacchetto viene pubblicato come asset separato `GamePanel-AsphaltHD.zip`, non è un installer né un programma da avviare. GamePanel lo scarica solo quando richiesto; il semplice aggiornamento dell'app non lo applica al gioco.

## Modelli

L'importazione locale ammette pacchetti ZIP nel formato GamePanel con coppie `CARS/<AUTO>/GEOMETRY.BIN` e `TEXTURES.BIN` per auto già presenti. Conserva gli originali e consente disattivazione/rimozione con ripristino dei byte esatti, a condizione che nessun altro strumento abbia modificato i file gestiti.

Il controllo dei chunk BIN è strutturale: non certifica correttezza delle mesh, collisioni, kit carrozzeria o salvataggi. Il nome di un file non dimostra che sia una conversione compatibile. Questa release non include una geometria dichiarata collaudata. RAR, TPF, script END/Binary, EXE e ASI contenuti in un pacchetto locale non vengono eseguiti né convertiti automaticamente. Una conversione va prima analizzata. [Formato e limiti](ASSET-PACKS.md).

## Recupero

Se un'operazione è interrotta, la pagina propone **Riprendi operazione asset** e **Annulla e recupera i file**. Le copie prima/dopo e il journal restano in `%LOCALAPPDATA%\GamePanel\Profiles`, separati dal gioco. I payload importati sono in `AssetPacks`; i download in `Components`. Non cancellare queste cartelle durante un recupero. Se un file è stato modificato da un altro programma o una copia è corrotta, l'operazione si ferma.

I profili immagine, schermo e asset condividono un lock e bloccano operazioni nuove mentre un journal è pendente. I salvataggi, l'EXE, la risoluzione Windows e gli archivi originali della città non vengono riscritti dal pack asfalto.
