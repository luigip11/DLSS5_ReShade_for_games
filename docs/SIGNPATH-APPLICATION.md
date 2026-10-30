# Candidatura SignPath Foundation — bozza pronta, non inviata

Questo documento prepara il passaggio che manca per una distribuzione con editore attendibile. Non indica che il progetto sia ammesso al programma o già firmato. Il proprietario del repository deve presentare la candidatura al servizio e accettarne le condizioni. Non sono stati creati account o attivati servizi a pagamento.

## Dati del progetto

- Nome: GamePanel.
- Repository: https://github.com/luigip11/DLSS5_ReShade_for_games
- Licenza del codice attuale: MIT.
- Maintainer e referente: proprietario del repository `luigip11`; contatto da inserire personalmente nel modulo del servizio.
- Piattaforma: Windows 11 x64, WPF .NET self-contained.
- Funzioni distribuite: analisi statica di EXE e componenti grafici, report locale, copie di riferimento e recupero di file mancanti con verifica hash e journal.
- Funzioni grafiche: nessun deployment o controllo DLSS/HDR/ReShade attivo nelle alpha correnti. I file di giochi e mod dell'utente non sono inclusi nelle release.
- Dipendenze distribuite: runtime Windows/.NET Microsoft; nessun binario grafico proprietario di terzi incorporato nella build attuale. Idoneità delle System Libraries e future integrazioni da verificare con il servizio.
- Comunicazioni: nessun client HTTP o telemetria implementati; dati e copie locali. Avvio del gioco solo su richiesta esplicita.
- Build: GitHub Actions Windows, SDK fissato, 44 controlli del core e prova GUI di analisi/backup/recupero; risultati nei log dei workflow.

## Descrizione per la candidatura

GamePanel is an MIT-licensed Windows desktop utility for inspecting a user-selected game executable and preparing local recovery before future graphics-mod configuration. Current releases statically read PE metadata, inventory nearby components, export a local report, and create verified baseline snapshots. Recovery recreates missing files and refuses to overwrite changed existing files. No game binaries, cracks, commercial game assets, or third-party graphics payloads are distributed. The application does not modify DRM, install services, alter Windows security settings, or collect telemetry. Production signing is requested for binaries built from the public repository through GitHub Actions. Future graphics integrations will be reviewed separately for licensing and signing eligibility before inclusion.

## Dopo l'eventuale approvazione

1. Verificare le condizioni aggiornate su https://signpath.org/terms e seguire il percorso del servizio. La gratuità dipende da idoneità e approvazione: accettazione e tempi non sono garantiti.
2. Configurare progetto e integrazione GitHub con il provider secondo le istruzioni ricevute.
3. Creare l'environment GitHub `signing`, limitarlo al ramo `main` e impostare un revisore richiesto. Inserire le variabili `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_POLICY_SLUG`, `SIGNPATH_CONFIGURATION_SLUG`, `SIGNPATH_EXPECTED_SUBJECT`.
4. Conservare `SIGNPATH_API_TOKEN` soltanto come secret dell'environment, mai in chat, issue o file.
5. Configurare il pacchetto per firmare `GamePanel.exe`, `GamePanel.dll` e `GamePanel.Core.dll`, mantenendo invariati i componenti Microsoft e gli altri file.
6. Avviare manualmente **Windows signed release**. Senza configurazione il workflow si ferma prima delle richieste al servizio e non produce un download dichiarato firmato.
7. Il controllo di produzione verifica Authenticode valido, editore atteso, timestamp, insieme dei file e runtime invariato, poi rigenera gli hash. Un errore impedisce la pubblicazione dell'artifact firmato.
8. Collaudare il pacchetto risultante su Windows 11 con Smart App Control attivo. La firma non certifica assenza di malware o compatibilità assoluta.

Riferimento: azione ufficiale https://github.com/SignPath/github-action-submit-signing-request, v2 fissata a commit nel workflow. Il percorso completo di firma non è stato eseguito: mancano account approvato, policy e credenziali.
