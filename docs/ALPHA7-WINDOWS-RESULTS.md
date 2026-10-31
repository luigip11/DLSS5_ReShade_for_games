# Collaudo alpha.7 — 31 ottobre 2026

Release: [v0.1.0-alpha.7](https://github.com/luigip11/DLSS5_ReShade_for_games/releases/tag/v0.1.0-alpha.7).
Sorgente del pacchetto: `a29780bc6322c16204e8e2217941ed0374d26f69`.

- [Build main e collaudo Windows riusciti](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/runs/41213004474).
- [Build, collaudo e pubblicazione release riusciti](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/runs/41213014153).
- 108/108 controlli superati su Windows, inclusi download ufficiali ReShade/Widescreen, SemVer, digest, ZIP ostili, file alterati, ripresa/rollback e journal oltre 128 KB.
- Helper Windows eseguito su un pacchetto completo isolato: hash finali corretti, file obsoleto rimosso, extra personali e sentinelle libreria/cache/backup/impostazioni conservati.
- GUI: avvio, percorso quotato, backup/ripristino, download e applicazione/rimozione dei profili, libreria persistente dopo riavvio e avvio dalla scheda.
- Pulsante aggiornamenti interrogato contro GitHub: risposta corretta di assenza release prima della pubblicazione. Il runner espone i pulsanti nativi del dialogo come Pane non invocabili; il processo isolato di questo controllo di sola lettura viene terminato e riaperto prima dei test di modifica. Non è una verifica automatica del clic Sì/No o dell'aggiornamento della copia dell'utente.
- Rilevamento effettivo dello schermo CI: HyperVMonitor, 1024 × 768 attuale e preferita. Nessun valore Dell fissato nel codice. La selezione nativa sul Dell fisico rimane da provare.
- Renderer sintetico DX9 x86: shader compilato; Cinema RGB 55/131/206 contro Originale 64/128/192, differenza 26 sul pixel campione. Nessun gioco commerciale eseguito; ASI Widescreen installato/rimosso staticamente, mai eseguito nella fixture.
- Screenshot delle pagine Schermo/Libreria controllati nell'artifact `GamePanel-interface-preview` della build main.

## Pacchetto realmente pubblicato

`GamePanel-win-x64.zip`: 65.410.338 byte.
SHA-256: `3ead4680ce86d4ccc894f3f7f4c4cda4b6f6e24c39c5c2ad90b6cff4230918b8`.

Dopo la pubblicazione è stato scaricato lo ZIP reale tramite il codice SoftwareUpdates; metadata/digest, estrazione e hash di tutti i 413 file inventariati verificati. I metadati pubblici delle release sono stati ottenuti attraverso il connettore GitHub perché il proxy del workspace non consente api.github.com; il download degli asset ha usato HTTPS reale su GitHub/CDN. Lo stesso parser ha confermato che alpha.7 è corrente e che una versione precedente vede l'aggiornamento. Il trasporto API diretto era stato provato dalla GUI Windows prima della pubblicazione.

L'app è ancora senza firma Authenticode GamePanel. Il controllo di firma rifiuta correttamente il pacchetto non firmato. Nessuna attestazione di compatibilità SAC, assenza assoluta di malware o compatibilità verificata con tutti i giochi.
