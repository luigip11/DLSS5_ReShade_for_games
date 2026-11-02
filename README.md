# GamePanel — DLSS5, ReShade for games

Gestore personale Windows per DLSS e HDR/ReShade. Target iniziale: MSI Vector 16 AI HX, RTX 5080 Laptop, Windows 11, selezione manuale dell'EXE, giochi moderni e profili legacy per NFS Underground 1 e 2.

## Stato reale: candidato 0.1.0-alpha.13, profili per gioco e gestione DLSS Remix

**Blocco noto su Windows 11 con Smart App Control attivo:** le build alpha attuali non hanno una firma Authenticode attendibile e possono essere bloccate per editore non verificabile. Non sono ancora una distribuzione pronta per questa configurazione. La firma di produzione non è configurata. Vedere [stato e percorso di firma](docs/SIGNING.md).

Alpha.13 organizza GamePanel intorno al gioco selezionato: **Libreria, Gioco, Grafica e DLSS**. Gli strumenti specifici UG2 compaiono solo nel loro contesto; quelli tecnici sono in Avanzate. La pagina DLSS gestisce le copie Remix riconosciute con registrazione, cattura della configurazione, anteprima dei parametri, applicazione e ripristino. Sugli altri giochi espone componenti e disponibilità senza offrire modifiche non integrate.

La copia Remix alpha.12 già collaudata può essere registrata senza riestrarre l'archivio. Le varianti registrate Originale/Remix condividono una scheda della libreria. Il profilo consigliato per RTX 5080 Laptop e 3440×1440 deriva dal file finale dell'utente e dalle scelte confermate nel pannello; la sua fluidità con Frame Generation non equivale agli FPS di rendering. [Guida ai profili](docs/GAME-PROFILES.md).

Restano il builder di una copia Remix separata, i percorsi ReShade e texture per i giochi compatibili e le procedure di recupero. Nessun gioco, asset o runtime Remix è incluso nello ZIP. La build è un candidato di sviluppo fuori dal canale aggiornamenti.

Disponibile:

- Libreria nativa con importazione persistente, ricerca, rinomina, ricollegamento e avvio da scheda.
- Prova texture DirectX x86 UG2: componente su richiesta, prima prova senza sostituzioni, catture DDS verificate e anteprime, materiale asfalto originale completo di mipmap, sostituzioni personali reversibili. Richiede collaudo nel gioco reale.
- Texture e modelli: pacchetti ZIP con inventario SHA-256, anteprima, copie degli originali e ripristino. Primo pack originale di tre superfici asfalto 2K, sperimentale, per il layout UG2 compatibile.
- Pagine Panoramica, DLSS, HDR / FX, Schermo, Backup e Diagnosi.
- Selezione, trascinamento o inserimento del percorso di un singolo EXE, indipendente dagli store. Accetta anche il percorso tra virgolette copiato da Esplora file.
- Analisi statica PE x86/x64; indizi del renderer dagli import diretti.
- Inventario limitato dei componenti DLSS, ReShade, proxy e add-on nelle cartelle vicine.
- Calcolo SHA-256; avvisi su possibili indicatori anti-cheat e scansione parziale.
- Esportazione JSON locale su richiesta, fuori dalla cartella del gioco.
- Avvio esplicito del gioco con i file e il profilo attualmente applicati, usando la sua cartella come working directory.
- Lettura degli adattatori video esposti da Windows.
- Panoramica gaming con dati reali, navigazione a icone e backup più diretto; screenshot nell'artifact `GamePanel-interface-preview`.
- ReShade SDR per classici NFS/San Andreas x86 DX9 riconoscibili (sperimentale): Originale, Bilanciato e Cinema, sei parametri reali e aggiornamento dello shader alpha.5 tracciato.
- Widescreen Fix sperimentale: pacchetto ufficiale verificato, correzioni HUD/FOV e risoluzioni native nel menu del gioco; rimozione separata.
- Lettura della risoluzione dichiarata nell’ultimo log ReShade, con data e identità EXE.
- Copie di riferimento dell'EXE e dei componenti riconosciuti, con hash verificati e archivio persistente nei dati utente.
- Anteprima di recupero, ripristino dei soli file mancanti e ripresa delle operazioni interrotte. I file presenti ma cambiati bloccano il recupero.

Non ancora disponibile:

- Gestione generale dei mod e swap DLSS; pacchetti di geometrie collaudati e remaster completo della città.
- Sovrascrittura di file modificati da altri strumenti; concatenamento di loader preesistenti.
- Regolazione DLSS/HDR e conversione HDR del gioco.
- Identificazione certa del gioco, compatibilità certificata, rilevamento HDR del display.
- Integrazione con launcher che richiedono URL/argomenti specifici.
- Neural Rendering collaudato su Underground 1/2 o sui giochi moderni del proprietario.

I nomi dei file e le risorse versione sono indizi, non verifiche di firma o sicurezza. Non vengono caricati come codice gli EXE/DLL analizzati. L'app non contiene telemetria, servizi o controlli di aggiornamento all'avvio. Il pulsante aggiornamenti interroga GitHub soltanto su richiesta e scarica una nuova versione soltanto dopo Sì. Le preparazioni esplicite scaricano ReShade da reshade.me, Widescreen Fix e TexWizard dai repository ufficiali GitHub/CDN e il pack originale dalla release GamePanel con cataloghi fissati; dimensioni e SHA-256 sono verificati. Il setup ReShade non viene eseguito. ReShade può cercare aggiornamenti quando caricato dal gioco. I binari ReShade non sono redistribuiti nello ZIP.

## Uso sul PC Windows

Per il primo test consultare [la guida alpha.8](docs/ALPHA8.md), usando una copia funzionante del gioco in una cartella scrivibile. Alla prima apertura importare gli EXE una volta: dopo la riapertura restano nella libreria.

Il progetto è pubblicato in [luigip11/DLSS5_ReShade_for_games](https://github.com/luigip11/DLSS5_ReShade_for_games). Scaricare **GamePanel-win-x64.zip** da [Releases](https://github.com/luigip11/DLSS5_ReShade_for_games/releases). Per le build di sviluppo rimangono gli artifact delle compilazioni riuscite in [Actions](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/workflows/windows-build.yml), accessibili dopo login. Da alpha.7 si può usare Controlla aggiornamenti; le versioni precedenti richiedono il download manuale. Usare lo stesso account Windows per ritrovare la libreria.

1. Estrarre **tutto** l'artifact scaricato in una cartella scrivibile, fuori dalle cartelle dei giochi. `GamePanel.exe` deve avere le sue DLL accanto; non avviarlo dalla vista di WinRAR. Per alpha.1/2 era necessario estrarre anche lo ZIP interno.
2. Avviare `GamePanel.exe` e premere **Importa gioco** nella libreria. Non serve installare .NET separatamente. La consultazione/analisi non richiede elevazione; scrivere in Program Files può richiederla.
3. Incollare il percorso completo nel campo **Eseguibile del gioco** e premere **Analizza gioco** o **Invio**. In alternativa usare **Scegli EXE** o trascinare il file. Le virgolette esterne di "Copia come percorso" sono accettate. Se non emerge un'API grafica, il file potrebbe essere un launcher o caricare il renderer dinamicamente.
4. Aprire Diagnosi. Esportare il JSON in Documenti se si vuole conservarlo.
5. Con il gioco chiuso, aprire **Backup → Crea backup → Verifica e mostra anteprima**. Una verifica senza file mancanti non modifica il gioco. Vedere [cosa salva e come recuperare](docs/BACKUP.md).

Il pacchetto alpha non è firmato con certificato Authenticode. La pipeline comprende un controllo dell'avvio della finestra e del ciclo backup/ripristino su un file di prova Windows; l'esito effettivo è nei log della specifica build. Questo non sostituisce il collaudo sui giochi reali, con SAC, una scansione antivirus o un audit completo. In caso di blocco di sicurezza, verificare l'esatto messaggio e lo stato della firma.

## Compilazione per sviluppatori

Richiede SDK .NET 10.0.403 (runtime 10.0.14 incluso nel pacchetto utente); nessuna dipendenza NuGet di terze parti esplicita. WPF e runtime sono Microsoft.

Su Windows, dalla root del repository:

```powershell
dotnet run --project tests/GamePanel.Checks -c Release
powershell -NoProfile -File scripts/publish.ps1
```

Lo script produce lo ZIP e SHA256SUMS in `artifacts/`. Non installa componenti grafici e non esegue il gioco. La prima compilazione scarica SDK reference/runtime packs Microsoft; il semplice utilizzo del pacchetto portabile non lo richiede.

Il core e i suoi controlli si eseguono anche su Linux. La compilazione WPF si effettua tramite targeting Windows; l'esecuzione richiede Windows.

## Organizzazione

```text
src/GamePanel.Core       Lettura PE, inventario, copie di riferimento e recupero dei mancanti
src/GamePanel.Desktop    Interfaccia WPF, schermi, libreria e aggiornamenti
src/GamePanel.Updater    Sostituzione e recupero dei file dell'app a finestra chiusa
tests/GamePanel.Checks   Controlli PE e scenari backup/ripristino su file di prova
scripts                 Pubblicazione Windows portabile
docs                    Specifica, percorso di sviluppo, sicurezza, collaudo
.github/workflows       Compilazione Windows, controllo GUI e artifact portabile
```

## Fonti e licenza

L'implementazione iniziale è nuova: nessun sorgente RHI/Swapper né DLL grafica di terzi sono incorporati. Licenza MIT per questo codice. Un futuro riuso di codice GPL di RHI richiederà la revisione della licenza della derivazione e degli obblighi di distribuzione prima del merge.

I link ai progetti in `docs/COMPONENTS.md` sono riferimenti di analisi; non costituiscono approvazione dei binari né una promessa di integrazione. Ogni componente va verificato per versione, provenienza, licenza e compatibilità.

Vedere [ROADMAP](docs/ROADMAP.md), [BACKUP](docs/BACKUP.md), [SECURITY](docs/SECURITY.md) e [WINDOWS-CHECK](docs/WINDOWS-CHECK.md).
