# Formato pacchetti asset — schema 1

L'importazione locale è per ZIP preparati e revisionati nel formato GamePanel. Non basta rinominare un vecchio RAR/TPF in ZIP. I metadati dichiarati da un archivio non sono una firma dell'autore; i controlli di hash rilevano modifiche, non attestano l'assenza assoluta di malware.

Alla root dello ZIP serve `GamePanelAssetPack.json`, con proprietà PascalCase:

```json
{
  "SchemaVersion": 1,
  "Id": "example-pack",
  "Name": "Example pack",
  "Author": "Nome autore",
  "SourceUrl": "https://example.org/pagina-autore",
  "Kind": "Textures",
  "Files": [
    { "Path": "TexturePacks/GamePanel-example-pack/meta.json", "Sha256": "SHA256_ESATTO_DI_64_CARATTERI_HEX", "Bytes": 123 },
    { "Path": "TexturePacks/GamePanel-example-pack/textures.bin", "Sha256": "SHA256_ESATTO_DI_64_CARATTERI_HEX", "Bytes": 456 }
  ]
}
```

L'esempio contiene segnaposto, non è importabile così. Ogni file, comprese licenze e README, deve essere inventariato con dimensione e SHA-256 reali. Il manifest stesso non va elencato in Files. Id è uno slug minuscolo di 3–48 caratteri; nome/autore sono limitati e l'origine deve essere HTTPS senza credenziali. Il link viene mostrato, non eseguito.

## Texture

Sono ammessi soltanto `TexturePacks/GamePanel-<Id>/meta.json`, `textures.bin` e documenti `.txt`/`.md` sotto `GamePanelAssets/<Id>/`. Il metadata segue il formato TexWizard:

```json
{"textures":[["NOME_TEXTURE_ORIGINALE","NOME_TEXTURE_SOSTITUTIVA"]]}
```

Sono controllati limiti, duplicati e collisioni dei nomi/hash originali; il BIN deve essere un contenitore TPK con chunk nei limiti. Il runtime TexWizard viene scaricato separatamente dal catalogo fissato e non può essere introdotto da uno ZIP locale. `autoScan` resta disabilitato e la configurazione elenca soltanto i pack attivi gestiti.

## Modelli

Con `Kind: "Models"` sono ammesse esclusivamente coppie:

```text
CARS/<AUTO>/GEOMETRY.BIN
CARS/<AUTO>/TEXTURES.BIN
GamePanelAssets/<Id>/README.md
GamePanelAssets/<Id>/LICENSE.txt
```

`<AUTO>` contiene lettere maiuscole, numeri o underscore, massimo 32 caratteri. Entrambi i file originali dell'auto devono già esistere nella copia di gioco selezionata. Installare un modello può cambiare anche collisioni o punti di attacco: il controllo strutturale dei chunk non garantisce compatibilità della vettura. Non sono ammessi modifiche GLOBAL/frontend, nuove auto che richiedono database aggiornati, script Binary, DLL, ASI, EXE o installer. Non vengono redistribuiti file originali del gioco per completare coppie mancanti.

## Integrità e operazioni

Massimo 2048 file, 512 MiB per file e 1 GiB complessivo. ZIP con traversal, collegamenti, nomi dispositivo Windows, percorsi ambigui, proprietà JSON duplicate, file extra o hash/dimensioni diversi vengono rifiutati. L'importazione usa uno staging nuovo, controlla di nuovo lo ZIP sorgente e verifica sempre la cache esistente; una cache alterata non viene accettata o sovrascritta in silenzio.

L'anteprima è vincolata a EXE e revisione dello stato. Le scritture usano copie durabili prima/dopo, journal e file temporanei con sostituzione atomica. I file già cambiati da terzi bloccano l'operazione. Per i modelli restano copie originali anche dopo la rimozione. Per aggiornare un pack con lo stesso Id, rimuovere prima la versione precedente.

Nessun pacchetto nativo di terzi va considerato convertito automaticamente. In particolare [Smol Model Fix](https://nfsmods.xyz/mod/2721) richiede prima l'audit del RAR e delle istruzioni Binary dell'autore; la sua compatibilità, il suo inventario effettivo e un'eventuale conversione non sono attestati da questa release.
