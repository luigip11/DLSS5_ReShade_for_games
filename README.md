# GamePanel — DLSS5, ReShade for games

Gestore personale Windows per DLSS e HDR/ReShade. Target iniziale: MSI Vector 16 AI HX, RTX 5080 Laptop, Windows 11, selezione manuale dell'EXE, giochi moderni e profili legacy per NFS Underground 1 e 2.

## Stato reale: 0.1.0-alpha.3, analisi e backup locali

**Blocco noto su Windows 11 con Smart App Control attivo:** le build alpha attuali non hanno una firma Authenticode attendibile e possono essere bloccate per editore non verificabile. Non sono ancora una distribuzione pronta per questa configurazione. La firma di produzione non è configurata. Vedere [stato e percorso di firma](docs/SIGNING.md).

Questa prima consegna **non migliora ancora la grafica dei giochi**. Serve a collaudare selezione, rilevamento e interfaccia prima di introdurre modifiche ai file.

Disponibile:

- Interfaccia nativa italiana con pagine Gioco, DLSS, HDR, Backup e Diagnosi.
- Selezione, trascinamento o inserimento del percorso di un singolo EXE, indipendente dagli store. Accetta anche il percorso tra virgolette copiato da Esplora file.
- Analisi statica PE x86/x64; indizi del renderer dagli import diretti.
- Inventario limitato dei componenti DLSS, ReShade, proxy e add-on nelle cartelle vicine.
- Calcolo SHA-256; avvisi su possibili indicatori anti-cheat e scansione parziale.
- Esportazione JSON locale su richiesta, fuori dalla cartella del gioco.
- Avvio esplicito del gioco senza modifiche, con la sua cartella come working directory.
- Lettura degli adattatori video esposti da Windows.
- Copie di riferimento dell'EXE e dei componenti riconosciuti, con hash verificati e archivio persistente nei dati utente.
- Anteprima di recupero, ripristino dei soli file mancanti e ripresa delle operazioni interrotte. I file presenti ma cambiati bloccano il recupero.

Non ancora disponibile:

- Download, installazione, swap di DLL e aggiornamenti dei mod.
- Ripristino sopra file modificati da altri strumenti; tracciamento delle future installazioni grafiche di GamePanel.
- Regolazione di parametri DLSS/HDR o pannelli in-game.
- Identificazione certa del gioco, compatibilità certificata, rilevamento HDR del display.
- Integrazione con launcher che richiedono URL/argomenti specifici.
- Neural Rendering collaudato su Underground 1/2 o sui giochi moderni del proprietario.

I nomi dei file e le risorse versione sono indizi, non verifiche di firma o sicurezza. Non vengono caricati come codice gli EXE/DLL analizzati. L'app non contiene client HTTP, telemetria, servizi o aggiornamenti automatici; le normali attività di Windows e del gioco avviato restano indipendenti dall'app.

## Uso sul PC Windows

Il progetto è pubblicato in [luigip11/DLSS5_ReShade_for_games](https://github.com/luigip11/DLSS5_ReShade_for_games). Per scaricare una build, aprire [Actions](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/workflows/windows-build.yml), scegliere una compilazione riuscita e scaricare l'artifact `GamePanel-win-x64-alpha` dopo l'accesso a GitHub. Da alpha.3 l'artifact contiene direttamente i file portabili e `FILES-SHA256SUMS.txt`, senza un secondo ZIP interno. Se il workflow non è ancora terminato, non è ancora disponibile una build verificata da Actions.

1. Estrarre **tutto** l'artifact scaricato in una cartella scrivibile, fuori dalle cartelle dei giochi. `GamePanel.exe` deve avere le sue DLL accanto; non avviarlo dalla vista di WinRAR. Per alpha.1/2 era necessario estrarre anche lo ZIP interno.
2. Avviare `GamePanel.exe`. Non richiede installazione separata di .NET né privilegi amministrativi.
3. Incollare il percorso completo nel campo **Percorso completo del gioco (.exe)** e premere **Analizza percorso** o **Invio**. In alternativa usare **Seleziona gioco .exe** o trascinare il file. Le virgolette esterne di "Copia come percorso" sono accettate. Se non emerge un'API grafica, il file potrebbe essere un launcher o caricare il renderer dinamicamente.
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
src/GamePanel.Desktop    Interfaccia WPF, dialoghi file, avvio scelto dall'utente
tests/GamePanel.Checks   Controlli PE e scenari backup/ripristino su file di prova
scripts                 Pubblicazione Windows portabile
docs                    Specifica, percorso di sviluppo, sicurezza, collaudo
.github/workflows       Compilazione Windows, controllo GUI e artifact portabile
```

## Fonti e licenza

L'implementazione iniziale è nuova: nessun sorgente RHI/Swapper né DLL grafica di terzi sono incorporati. Licenza MIT per questo codice. Un futuro riuso di codice GPL di RHI richiederà la revisione della licenza della derivazione e degli obblighi di distribuzione prima del merge.

I link ai progetti in `docs/COMPONENTS.md` sono riferimenti di analisi; non costituiscono approvazione dei binari né una promessa di integrazione. Ogni componente va verificato per versione, provenienza, licenza e compatibilità.

Vedere [ROADMAP](docs/ROADMAP.md), [BACKUP](docs/BACKUP.md), [SECURITY](docs/SECURITY.md) e [WINDOWS-CHECK](docs/WINDOWS-CHECK.md).

## Correzione alpha.2

La prima alpha mostrava il percorso in un'etichetta non modificabile. Ora il campo accetta l'incolla diretto, anche per percorsi con spazi e parentesi come quelli sotto `Program Files (x86)`. I messaggi distinguono file non trovato, accesso negato e formato PE non leggibile. Modificare il percorso invalida l'analisi precedente: non si può esportare o avviare per errore il gioco analizzato prima.

Questa modifica risolve l'inserimento del percorso. Non dimostra ancora la lettura dell'EXE reale di NFS Underground 2 presente sul PC dell'utente.

## Novità alpha.3

Prima base di recupero: backup persistenti, copie verificate, anteprima dei conflitti, recupero dei file mancanti e journal per la ripresa. Il report condiviso dall'utente ha confermato l'analisi di `speed2.exe` x86 con import Direct3D 9/DirectDraw. Il recupero sul suo gioco reale resta da collaudare; non sono ancora installati componenti ReShade o DLSS.
