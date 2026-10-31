# Candidatura SignPath Foundation — bozza pronta, non inviata

Questo documento prepara il passaggio che manca per una distribuzione con editore attendibile. Non indica che il progetto sia ammesso al programma o già firmato. Il proprietario del repository deve presentare la candidatura al servizio e accettarne le condizioni. Non sono stati creati account o attivati servizi a pagamento.

## Dati del progetto

- Nome: GamePanel.
- Repository: https://github.com/luigip11/DLSS5_ReShade_for_games
- Licenza del codice attuale: MIT.
- Maintainer e referente: proprietario del repository `luigip11`; contatto da inserire personalmente nel modulo del servizio.
- Piattaforma: Windows 11 x64, WPF .NET self-contained.
- Funzioni distribuite: analisi statica, report locale, backup mirati e recupero; da alpha.5 profilo SDR sperimentale per Underground 2 con download esplicito di ReShade e installazione/rimozione tracciata.
- Funzioni grafiche: shader SDR originale MIT e parametri reali. ReShade originale scaricato direttamente dal sito ufficiale su richiesta, non redistribuito nello ZIP. Il setup non è eseguito. Nessun controllo DLSS/HDR attivo; nessun file di giochi o crack incluso.
- Dipendenze distribuite: runtime Windows/.NET Microsoft; nessun binario grafico proprietario di terzi incorporato nella build attuale. Idoneità delle System Libraries e future integrazioni da verificare con il servizio.
- Comunicazioni: nessuna telemetria; report e copie locali. Un solo download esplicito HTTPS, URL/dimensioni/hash incorporati. ReShade può controllare aggiornamenti da dentro il gioco. Avvio gioco solo su richiesta esplicita.
- Build: GitHub Actions Windows, SDK fissato, controlli del core e prova GUI di analisi/backup/recupero/profilo; risultati nei log dei workflow.

## Descrizione per la candidatura

GamePanel is an MIT-licensed Windows desktop utility for inspecting a selected game executable, local baseline backup and missing-file recovery. Alpha.5 adds an experimental SDR post-processing profile for Underground 2 x86 Direct3D 9. On explicit user request it downloads exactly one pinned official ReShade setup over HTTPS, verifies compiled-in SHA-256 hashes and size limits, and statically extracts the x86 DLL without executing setup. ReShade binaries are not redistributed with GamePanel. Our original MIT shader and preset are managed through fixed-path install/update/removal operations with a durable two-version recovery journal. Existing loaders are not overwritten; externally changed configuration/presets are preserved at removal. No game binaries, cracks or commercial game assets are distributed. The application does not alter DRM, install services or change Windows security settings, and collects no telemetry. ReShade itself may check upstream updates when loaded by the game. Production signing is requested for our own binaries built from the public repository through GitHub Actions; the separately downloaded component and future graphics integrations must be disclosed and reviewed for provider eligibility. A GamePanel signature will not confer Windows trust on the external ReShade component.

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
