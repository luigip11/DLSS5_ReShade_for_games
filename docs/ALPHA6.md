# Alpha.6 — libreria, Cinema e obiettivo 3440 × 1440

## Primo avvio e libreria

Estrarre tutto il nuovo ZIP in una cartella nuova, conservando i dati in `%LocalAppData%\GamePanel`. Non copiare solo GamePanel.exe sopra una versione precedente. L'app si apre nella Libreria.

1. Premere **Importa gioco** e scegliere `speed2.exe` una volta. Ogni analisi riuscita salva automaticamente la voce; il gioco non viene eseguito durante l'importazione.
2. Ritornare a **Libreria** per trovare la scheda. **Apri scheda** rianalizza l'EXE salvato; **Avvia** lo controlla e poi lo lancia senza chiedere il percorso.
3. Nome, percorso, ultima analisi e ultimo avvio da GamePanel restano salvati tra le sessioni. I dati di architettura/API appartengono all'ultima analisi, non a una misura in tempo reale.
4. **Rinomina** cambia l'etichetta. **Ricollega EXE** aggiorna il percorso di un gioco spostato; non migra automaticamente i backup o i profili, che restano legati al percorso originario. **Rimuovi dalla libreria** elimina solo la voce, senza cancellare gioco, backup o mod.

Il database locale è `%LocalAppData%\GamePanel\Library\library.json`. Non sono cercati altri giochi sul disco, né scaricate copertine. I nomi iniziali sono suggeriti dal percorso, non identificazioni certe. La libreria non esegue automaticamente i percorsi salvati all'avvio. Un file mancante resta elencato e può essere ricollegato. Un database malformato viene segnalato e conservato; non è azzerato per nascondere l'errore.

L'alpha.5 non aveva una libreria: i giochi vanno importati una prima volta in alpha.6. I profili/backup alpha.5 vengono invece letti dagli archivi esistenti dello stesso account Windows. Usare le stesse credenziali Windows anche quando si approva l'elevazione; un altro account ha dati locali distinti.

## Profilo Cinema

1. Con il gioco chiuso, aprire la sua scheda e **HDR / FX**.
2. Selezionare **Cinema → Prepara aggiornamento → Applica profilo**. Per una prima installazione il pulsante scarica ReShade e prepara l'anteprima.
3. L'aggiornamento da alpha.5 sostituisce attraverso il journal lo shader di proprietà e il preset; non richiede di cancellare manualmente DLL o copie. Un file modificato esternamente continua a bloccare l'aggiornamento.
4. Riavviare il gioco. Cinema aggiunge una curva di contrasto, un bagliore delle luci e una vignettatura regolabili, oltre a nitidezza, saturazione e intensità. Ridurre il bagliore se il bloom originale del gioco è già forte. **Originale** disabilita la tecnica; **Bilanciato** conserva il look leggero precedente.

Questo resta post-processing SDR. Non sostituisce texture o modelli, non aggiunge illuminazione fisica, ray tracing, DLSS o HDR e non trasforma da solo Underground 2 in un gioco moderno. Non vengono scaricati pacchetti di texture non verificati. I tre nuovi parametri assenti dai metadata alpha.5 vengono letti come zero per evitare cambiamenti prima dell'applicazione esplicita.

## Dell ultrawide: 3440 × 1440

1. Dalla scheda di Underground 2 aprire **Schermo**.
2. Con il gioco chiuso, premere **Scarica fix e prepara anteprima**, controllare i quattro percorsi, poi **Applica Widescreen Fix**.
3. Avviare il gioco sul Dell. Nelle opzioni video del gioco scegliere **3440 × 1440** e confermare. La versione approvata del fix aggiunge le modalità disponibili al menu; non usa i vecchi parametri INI `ResX`/`ResY`. La sola installazione non dimostra che il gioco stia usando la risoluzione desiderata.
4. Dopo un avvio aprire **Schermo → Leggi risoluzione dell'ultimo avvio**. Il pannello mostra dimensioni dichiarate e data dell'ultimo `ReShade.log`, quando il log identifica lo stesso EXE. Non è una misura in tempo reale; un log vecchio non prova lo stato attuale.
5. Se 3440 × 1440 non compare, o il gioco si chiude dopo l'aggiunta del fix, raccogliere il log e usare la rimozione gestita. Non sostituire l'EXE o cancellare loader di provenienza diversa per forzare l'installazione.

Il pacchetto Widescreen Fix è scaricato direttamente dal repository ufficiale di ThirteenAG. Versione approvata: asset GitHub `558911953`, ZIP di 8832311 byte, SHA-256 `791fcf486db6916642b02a51dd6ed786da7bdbd6c4ad7e207c36ba1f0a5cca59`. I singoli file sono verificati; l'INI viene poi sostituito con la configurazione display GamePanel. [Catalogo](WIDESCREEN-CATALOG.md).

Si aggiungono solo `dinput8.dll`, `scripts/NFSUnderground2.WidescreenFix.asi`, `.dat` e `.ini`. La configurazione imposta HUD/FOV, nasconde le linee MSAA e tiene disabilitate le opzioni aggiuntive relative a lettore ottico, directory salvataggi, scrittura settings.ini, controller e camera. Il progetto upstream può contenere altre correzioni interne: non è una patch di sole dimensioni dello schermo. Non modifica l'EXE su disco; applica correzioni in memoria e la compatibilità della build reale va verificata.

Loader esistenti e cartelle `scripts` già popolate bloccano il primo deployment. Il fix non sovrascrive mod sconosciute e non crea catene di loader. Per la coesistenza è riconosciuto il ReShade originale già approvato. Rimozione e recupero dei due profili sono separati e serializzati; un'operazione interrotta blocca l'altro profilo finché non è recuperata. INI modificati vengono conservati durante la rimozione.

## Permessi e limiti

Il gioco dell'utente si trova sotto `Program Files (x86)`: per applicare/rimuovere file lì possono servire privilegi amministrativi. Si può aprire GamePanel con **Esegui come amministratore** per le sole modifiche, poi riaprirlo normalmente come libreria/launcher. Non vengono cambiati ACL, certificati, protezioni o configurazione degli schermi di Windows. Il primo collaudo va eseguito su una copia funzionante in una cartella scrivibile.

GamePanel resta senza firma attendibile; anche i componenti scaricati richiedono collaudo con Smart App Control. Hash e prove automatiche non sono una garanzia di assenza di malware o compatibilità. La libreria è disponibile per gli EXE analizzabili; i profili legacy qui descritti sono limitati a Underground 2 x86 DX9. Underground 1 e titoli moderni non ricevono automaticamente questo fix.

Per i test è disponibile `GAMEPANEL_DATA_ROOT`, un percorso assoluto opzionale: sposta tutti gli archivi/cache della sessione in una cartella separata. Non impostarlo nell'uso normale se si vogliono ritrovare i dati precedenti. I test GUI usano una root temporanea e non importano fixture nella libreria personale.
