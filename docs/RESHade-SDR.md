# ReShade SDR per Underground 2 — alpha.5

Prima integrazione sperimentale. Il nome `speed2.exe`, l'architettura x86 e l'import Direct3D 9 sono criteri di ammissione, non identificazione certa del gioco né certificazione della sua build. Non è DLSS, ray tracing o conversione HDR. Underground 1 e i titoli moderni non sono ancora ammessi da questo profilo.

## Uso

1. Chiudere il gioco. Per il primo test usare una copia funzionante in una cartella scrivibile, preservando l'installazione attuale. Sotto `Program Files (x86)` Windows può negare le scritture: GamePanel non cambia permessi né si eleva automaticamente.
2. Analizzare `speed2.exe` e aprire **HDR / FX**.
3. Selezionare **Bilanciato** oppure **Originale** e regolare Nitidezza, Colore e Intensità. Colore 50 è neutro; Originale disabilita la tecnica.
4. Premere **Scarica ReShade e prepara anteprima**. È una richiesta di rete esplicita verso il solo URL HTTPS fissato. Il setup ufficiale viene letto come archivio e mai eseguito. Le dimensioni e i due SHA-256 devono corrispondere al catalogo incorporato nel codice, non a un manifest scaricato.
5. Controllare i file e premere **Applica profilo**. Alla prima installazione viene creato anche un backup di riferimento. Avviare il gioco dalla Panoramica.
6. Provare menu, gara, filmati e Alt+Tab. Riavviare il gioco dopo ogni aggiornamento dei parametri. **Home** apre ReShade; **Bloc Scorr** commuta gli effetti.
7. Per togliere l'effetto scegliere Originale, preparare e applicare. Per eliminare anche il loader: **Anteprima rimozione ReShade → Conferma rimozione**.

Se è già presente un loader `d3d9.dll`, una configurazione ReShade o la cartella `GamePanelFX`, l'installazione si ferma senza sovrascriverli. Anche wrapper DXGI/DirectDraw e configurazioni DXVK/dgVoodoo bloccano questo percorso. Gli ASI/widescreen fix non sono certificati compatibili; GamePanel non li sostituisce o concatena automaticamente.

## File gestiti

| Percorso accanto all'EXE | Funzione |
|---|---|
| `d3d9.dll` | ReShade 6.8.0 x86 originale, scaricato dall'utente |
| `ReShade.ini` | Percorsi e impostazioni iniziali del runtime |
| `GamePanelPreset.ini` | Parametri del profilo e stato della tecnica |
| `GamePanelFX/GamePanelSDR.fx` | Shader originale MIT, senza include, texture o accesso alla profondità |

La regolazione applica una maschera di contrasto leggera, saturazione e miscelazione con l'immagine originale. Non stima FPS o miglioramenti prestazionali. Può coinvolgere anche HUD/filmati; qualità e costo GPU vanno giudicati nel gioco.

## Installazione e recupero

Registro persistente in `%LocalAppData%\GamePanel\Profiles`, separato dalle cartelle dei giochi. Ogni operazione salva prima entrambe le versioni dei file e un journal. I percorsi ammessi sono soltanto i quattro sopra. Sono controllati identità/hash dell'EXE, collegamenti, processi e stato visto nell'anteprima. Le sostituzioni riguardano soltanto file installati e riconosciuti da GamePanel.

Una scrittura di più file non è una transazione atomica unica. Dopo un'interruzione: rianalizzare lo stesso EXE, aprire HDR / FX e scegliere **Riprendi operazione** oppure **Annulla e recupera stato precedente**. Il recupero rifiuta hash sconosciuti e copie corrotte. Le copie precedenti restano conservate anche dopo la rimozione; non eliminare manualmente l'archivio durante un'operazione sospesa.

ReShade può riscrivere INI/preset. Durante la rimozione, configurazioni e preset modificati vengono conservati e indicati nell'anteprima, mentre il loader identico viene rimosso. Un loader o shader cambiato blocca la rimozione. Un preset modificato fuori da GamePanel blocca gli aggiornamenti dei parametri. Dopo una rimozione con INI/preset conservati, una nuova installazione richiede prima di verificare e spostare quei file: non sono sovrascritti automaticamente. Log e cache prodotti dal runtime non sono cancellati ricorsivamente.

Il controllo processi è conservativo per nome EXE e non protegge da un amministratore locale che manipola intenzionalmente file/metadata durante l'operazione. Non sono simulati guasti fisici al disco o perdita reale di alimentazione. Il backup non comprende salvataggi e asset completi.

## Origine, firma e rete

ReShade non è incorporato nello ZIP di GamePanel né ripubblicato nel repository. Il sito ufficiale chiede di non condividere i suoi binari: il download avviene direttamente da lì. Vedere [catalogo e verifica](RESHade-CATALOG.md).

La verifica sul runner Windows ha rilevato per setup e DLL il certificato pubblicato `CN=ReShade`, ma una radice non attendibile (`UnknownError`). Gli hash approvati identificano esattamente il componente letto dal sito ufficiale; non costituiscono firma di un editore attendibile per Windows, audit malware o collaudo di Smart App Control. Non vengono installati certificati o cambiate protezioni di Windows.

GamePanel scarica solo su richiesta e rifiuta redirect, dimensioni inattese e hash diversi. Riutilizza la cache verificata in `%LocalAppData%\GamePanel\Components`. Non esegue il setup, non scarica shader esterni e non aggiorna il catalogo online. ReShade, quando caricato nel gioco, può cercare aggiornamenti su GitHub: è comportamento del runtime upstream.

Fonti: [ReShade, download e condizioni](https://reshade.me/), [sorgente v6.8.0](https://github.com/crosire/reshade/tree/v6.8.0), [licenza BSD-3-Clause](https://github.com/crosire/reshade/blob/v6.8.0/LICENSE.md). Nessuna approvazione di GamePanel da parte degli autori di ReShade è implicita.
