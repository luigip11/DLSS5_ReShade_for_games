# Profili per gioco e gestione DLSS — candidato alpha.13

La libreria resta il punto di ingresso. La navigazione principale mostra **Libreria, Gioco, Grafica e DLSS**. Immagine/ReShade, Schermo, Ripristino e Diagnosi sono in **Avanzate**. Il campo EXE si espande quando serve importare o cambiare percorso, senza occupare ogni scheda dopo l'analisi.

## Funzioni contestuali

Grafica mostra **Configura** e **I tuoi componenti**. Il rilevamento serve a scegliere l'interfaccia: ogni servizio ricontrolla separatamente i file prima di scrivere. Un nome come speed2.exe, da solo, non abilita i contenuti Underground 2. Gli altri giochi non mostrano Smol, Asfalto 2K o la preparazione Remix specifica UG2.

Per Underground 2 originale, la preparazione Remix mantiene il flusso anteprima → copia separata. Gli strumenti texture manuali, gli altri pacchetti e l'audit delle fonti stanno in Avanzate. Su una copia Remix il percorso rapido apre **Profilo e DLSS**; ReShade e i loader delle texture restano fuori da quella procedura.

## Adottare la copia alpha.12 già presente

1. Analizzare dalla libreria l'EXE della **copia Remix funzionante**.
2. Aprire DLSS e scegliere **Prepara registrazione della copia**.
3. Leggere l'anteprima e confermare. La registrazione salva rtx.conf e l'eventuale user.conf nei dati GamePanel senza riscrivere quei file e senza estrarre di nuovo l'archivio.
4. Per conservare successive regolazioni fatte nel gioco, chiudere il gioco e usare **Salva configurazione attuale**, con anteprima. Questa scelta aggiorna il punto di ripristino; non promette una cronologia illimitata delle configurazioni.

Quando originale e copia sono entrambi importati e la relazione è registrata, la libreria li raccoglie in una scheda con selettore **Originale / Remix**. La selezione resta salvata senza avviare o analizzare automaticamente un gioco. Relazioni incomplete o non valide non fanno sparire le voci: restano schede separate.

## Profilo consigliato e controlli DLSS

Il catalogo include il profilo **UG2 Remix · RTX 5080 Laptop · 3440×1440**. Il pulsante di applicazione è disponibile soltanto quando GamePanel rileva questo adattatore e il monitor selezionato corrisponde al target. Il profilo combina il file finale dell'utente, SHA-256 `564be5aa6b1051000345da401e4cf14dffa8310aa4a0d125b46c341a608d598e`, con le scelte confermate nelle fotografie: DLSS Auto, RR Transformer, Frame Generation 4×, Reflex attivo, rimbalzi 1–4 e NRC Ultra. I valori di ombre/alte luci ripristinano i default documentati rimossi dall'esportazione, evitando di ereditare quelli del vecchio preset dell'archivio. È un profilo derivato, non una copia byte per byte dell'intero stato effettivo del runtime. Le liste hash dei materiali e le altre chiavi presenti vengono conservate.

Il profilo è stato provato dall'utente sul suo PC; non garantisce identici FPS, compatibilità driver o qualità su altre installazioni. I controlli in **Avanzate · parametri DLSS** gestiscono Super Resolution, RR/modello, Frame Generation e Reflex nella copia riconosciuta. «Lascia invariato» non cancella un override già presente. Vengono applicati solo i parametri scelti, con differenze chiave per chiave nell'anteprima.

Se user.conf esiste, le chiavi modificate vengono aggiornate anche lì per evitare override locali incoerenti. Chiavi estranee e commenti restano conservati; un user.conf assente non viene creato. Valori presenti nei file non dimostrano l'opzione attiva in memoria: verificare nel pannello Remix dopo il riavvio. Una modifica fatta dal gioco viene segnalata e richiede una nuova cattura esplicita prima di ulteriori applicazioni o ripristini.

## Protezione dei dati e limiti

### Cambio all’EXE NoCD identificato

La gestione della configurazione riconosce anche l’EXE NoCD esaminato, SHA-256 `5a4be6a2d072cf27f1aedf3119b75fd685a4ae5ecdf567cae7db26278c596e97`. Il riconoscimento non certifica la patch 1.2 né il funzionamento di RTX Remix nel gioco. La creazione iniziale della copia continua a richiedere l’EXE CD già collaudato.

Su una copia esistente con manifest originale valido, originale e copia possono usare uno dei due EXE identificati. Il cambio viene segnalato e richiede **Salva configurazione attuale**, anteprima con i due hash e conferma prima di nuove applicazioni o ripristini. Questa operazione registra l’identità attuale e acquisisce le configurazioni senza riscrivere EXE, manifest o file del gioco. Un’operazione precedente ancora Pending deve essere recuperata con la sua identità originale prima della migrazione; i journal conclusi restano leggibili. EXE sconosciuti, runtime o manifest modificati continuano a essere rifiutati.

I percorsi equivalenti per maiuscole/minuscole vengono trattati come lo stesso percorso su Windows. Un nome o una cartella realmente differente non viene considerato equivalente.

Sono gestite solo le copie alpha.12 identificate, con uno degli EXE riconosciuti, manifest locale, client e bridge identificati e renderer server corrispondente al manifest. Le modifiche riguardano solo rtx.conf/user.conf e i dati del profilo, con lock per gioco, journal, verifica prima/dopo e ripresa o annullamento. Il gioco deve essere chiuso. L'originale associato non viene modificato. I salvataggi esterni possono restare condivisi, come già nella prova alpha.12.

Per altri giochi la pagina DLSS è aperta in lettura: mostra le librerie rilevate e la disponibilità della gestione. Non sostituisce DLL né aggiunge automaticamente DLSS a giochi non integrati. I contatori con Frame Generation possono includere fotogrammi generati; GamePanel non inventa gli FPS di rendering e non li ricava dividendo il contatore per il moltiplicatore.

Questa build non cambia lo stato della firma Windows e non redistribuisce l'archivio Remix o i file del gioco.
