# Compatibilita texture UG2: verifica di sola lettura

Questo strumento legge soltanto l'header PE e cinque byte per ognuno degli 82 punti richiesti da TexWizard nel processo speed2 gia avviato dall'utente. Non avvia il gioco, non scrive nella sua memoria, non inietta/carica codice, non cambia file del gioco e non contatta Internet. Crea unicamente un report JSON nuovo sul Desktop.

## Sul PC Windows

1. Avvia Underground 2 nel modo che gia funziona e lascialo nel menu principale. Tieni aperta una sola istanza.
2. Apri `GamePanel-TextureCheck.ps1` in questo ramo, usa il pulsante per copiare il contenuto e leggilo se vuoi controllarlo.
3. Apri **Windows PowerShell a 64 bit**, con lo stesso account e livello di privilegi del gioco, incolla il contenuto e premi Invio. Non serve cambiare la politica di esecuzione o le protezioni Windows. Se Windows impedisce Add-Type/lettura del processo, fermati e riporta l'errore.
4. Sul Desktop viene creato `GamePanel-TextureCheck-...json`. Contiene anche il percorso locale del gioco: controllalo prima di allegarlo alla chat.

Un campione nel menu non dimostra quale fosse l'header durante il caricamento ASI e non certifica ABI o sicurezza. Non abilita automaticamente TexWizard.

## Esito statico del file condiviso

Il file analizzato e di 5.987.982 byte, PE32 x86 con import DX9. Il punto d'ingresso RVA e `0x53309E`; il layout atteso da TexWizard 1.3 usa `0x35BCC7`. Nessuno degli 82 CALL attesi coincide nei byte su disco. Questi dati spiegano il blocco dell'alpha.8: togliere il solo vincolo di dimensione non risolve la verifica. Il codice ad alta entropia puo indicare una trasformazione/protezione, ma non dimostra comportamento in memoria o presenza/assenza di malware. Il file non e stato eseguito o pubblicato in questo repository.
