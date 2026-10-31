# Sicurezza e confini dell'alpha

## Cosa fa il codice attuale

Legge PE, file versione e nomi dei componenti; calcola un hash; mostra i risultati. Non chiama LoadLibrary o Assembly.Load sui file selezionati, non carica add-on, non installa servizi, non scrive registro, non richiede elevazione, non modifica impostazioni HDR/driver. Da alpha.5 è implementato il download esplicito del pacchetto ReShade originale, con URL/versione/dimensioni/hash fissati nel codice, redirect rifiutati e nessuna esecuzione del setup. Il runtime ReShade caricato intenzionalmente dal gioco può contattare GitHub. L'esportazione del JSON è una scrittura richiesta dall'utente; il pulsante Avvia esegue intenzionalmente l'EXE selezionato.

Da alpha.3 **Crea backup** salva copie e metadati locali su richiesta. **Ripristina / riprendi** scrive soltanto file mancanti dopo verifica e anteprima. File presenti differenti causano un conflitto. Le temporanee dell'operazione e i journal consentono la ripresa. Questa pagina non installa mod; il profilo SDR alpha.5 si applica invece dalla pagina HDR / FX. Vedere [limiti e comportamento del backup](BACKUP.md).

Il framework, Windows, antivirus e il gioco possono compiere attività proprie non controllate dal gestore. Questo testo descrive il codice GamePanel, non garantisce il comportamento dell'intero PC.

## Limiti dell'analisi

- Import diretti soltanto: renderer in DLL engine, delay imports, percorsi non scansionati e caricamenti dinamici possono sfuggire.
- La scansione inventaria nomi noti entro limiti; non attribuisce firme o identità ai mod.
- Le versioni file sono dichiarazioni del file, non attestazioni.
- Indicatori anti-cheat limitati e non esaustivi. Nessun indicatore non significa assenza.
- L'hash identifica il contenuto letto, non prova assenza di malware o compatibilità.
- Non è uno scanner antivirus e non rende sicuro un EXE modificato o una DLL preesistente.
- L'analisi è soggetta a modifiche concorrenti dei file. Non usare il report per autorizzare una futura scrittura senza rianalisi e verifica degli hash al momento dell'operazione.

## Distribuzione

Il sorgente alpha non include binari grafici terzi. Il pacchetto self-contained include componenti runtime Microsoft. Build senza certificato Authenticode: conservare gli hash, verificare provenienza, non aggirare le protezioni. Non presentare una build non scansionata come "certificata priva di malware".

Non usare ancora con l'aspettativa di ottenere DLSS o HDR: le pagine relative dichiarano le integrazioni mancanti.

## Vulnerabilità

Non esiste ancora un canale pubblico del progetto. Segnalare nel canale di sviluppo privato i passi minimi, versione/commit e comportamento osservato. Non inserire token, eseguibili del gioco o dump contenenti dati personali nelle issue pubbliche.

## Modifiche alpha.5

Il profilo SDR può scrivere solo i quattro percorsi documentati in [RESHade-SDR](RESHade-SDR.md). Primo deployment solo su target assenti; aggiornamento solo del preset di proprietà con hash atteso. Registro e copie di entrambe le versioni precedono le scritture, con ripresa o annullamento. Percorsi canonici, reparse point, identità EXE, stato dell'anteprima e processi sono ricontrollati. Conflitti esterni sono bloccati; configurazioni/preset modificati sono conservati alla rimozione. Una sequenza multi-file può restare parziale dopo guasto: il journal serve a recuperarla. Non è una protezione contro manipolazioni intenzionali da amministratore locale.

Il download approvato via SHA-256 non equivale a un certificato Windows attendibile. Il certificato ReShade pubblicato termina in radice non attendibile sul runner; nessun certificato è aggiunto a Windows. Nessuna scansione antivirus completa o garanzia di assenza di malware viene dedotta da hash o test.

## Alpha.6

La libreria salva solo metadati locali, con file atomico, scrittore esclusivo e limiti di dimensione/voci; non esegue percorsi all’avvio. Ogni lancio da scheda passa da una nuova analisi. I dati corrotti non sono azzerati automaticamente. Il percorso dati ordinario resta LocalAppData; una root assoluta opzionale serve ai test isolati.

Widescreen Fix scaricato dall’asset GitHub originale e mai allegato alla build GamePanel: redirect HTTPS verso host/repository fissati, hash/dimensioni approvati per ZIP e singole voci, estrazione dei soli quattro percorsi. Il loader ASI viene caricato dal gioco dopo applicazione esplicita, non dall’analizzatore. I journal dei due profili sono separati ma condividono lock e blocco su operazioni pendenti. Configurazioni esterne conservate alla rimozione. Vedere [catalogo e limiti](WIDESCREEN-CATALOG.md). Non è stato eseguito il fix specifico su una copia reale del gioco nell’ambiente di sviluppo.
