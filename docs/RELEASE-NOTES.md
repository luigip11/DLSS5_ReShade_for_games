# Candidato alpha.12 — prova Remix su copia separata

In Texture e modelli → Da installare, **Prepara copia di prova** analizza il gioco e il 7z locale; **Crea copia e applica Remix** crea una nuova cartella, senza scrivere nel gioco originale né avviarlo. Il flusso verifica spazio libero, file sorgenti e archivio, estrae i contenuti nella propria cartella temporanea e pubblica la copia solo dopo i controlli finali. Interruzioni e conflitti non pubblicano una copia incompleta.

La prova accetta solo l'EXE CD funzionante identificato e il Widescreen Fix già gestito. Mantiene l'EXE, il loader e il fix presenti; omette ReShade, Texture Toolkit, TexWizard e gli script del pacchetto autore. AntiCull è escluso perché il suo controllo di entry point rifiuta questo EXE. La camera libera viene disattivata; il preset RTX dell'autore viene conservato.

**Non è una compatibilità Remix confermata.** Senza AntiCull possono mancare oggetti o parti della città; modelli Smol già presenti possono non corrispondere agli hash dell'autore. Non promette un nuovo HUD, personaggi moderni, DLSS attivo o un remaster completo. Salvataggi e profilo in Documenti/registro possono essere condivisi. Restano i requisiti del disco originale. [Istruzioni](REMIX-TRIAL.md), [risultati dell'audit statico](REMIX-COMPONENT-REVIEW.md).

# Candidato alpha.11 — libreria, tab e conferme

L'azione della libreria diventa **Analizza** e continua a rianalizzare il gioco selezionato aprendone la Panoramica. La pagina Texture e modelli ha due tab: **Da installare** per le operazioni disponibili e **I tuoi pacchetti** per la gestione, le anteprime di rimozione/disattivazione e le tre FAQ. Gli strumenti manuali sono raccolti in un gruppo avanzato, con il recupero delle operazioni interrotte sempre visibile.

Texture installate e attive e correzioni auto installate hanno conferme verdi con titolo evidente anche all'inizio della pagina. Risultati parziali, interruzioni ed errori non vengono mostrati come riusciti; una nuova operazione azzera il precedente stato di conferma.

Il report Remix 0.4 identifica il client `d3d9.dll` dentro la cartella dell'autore, insieme a runtime e DLL DLSS. Il flag storico della sola radice archivio resta compatibile, e l'analisi aggiunge la radice di installazione riconosciuta. Il nuovo esportatore crea uno ZIP di soli 44 componenti/configurazioni per l'audit statico, senza installazione o esecuzione. L'esportazione dal pacchetto reale da 2 GB resta da provare sul PC dell'utente.

# Candidato alpha.10 — texture automatiche e Smol parziale

Due pulsanti preparano e installano le catture texture in blocco, senza selezionare ogni hash. L'elaborazione interpola 2× le immagini piccole, conserva contenuto e alpha e genera mipmap compressi completi. Non aggiunge dettagli AI o nuovi modelli. Quantità, esclusioni, limiti e annullamenti sono espliciti; le associazioni sono verificate e recuperabili. [Guida](AUTOMATIC-TEXTURES.md).

Il RAR Smol dell'autore è stato verificato e letto con SharpCompress 0.50.7 incluso nell'app. Si prepara localmente un pack per Golf, IS300 e Sentra, mantenendo texture e backup originali. Sono esclusi database, altri modelli, ruote e componenti opzionali. Non è il pacchetto Smol completo né un remaster high-poly. [Dettagli](SMOL-MODELS.md).

RTX Remix rimane un progetto separato: nessuna installazione automatica di archivi non esaminati o sostituzione del renderer ReShade attuale. La build è un candidato di sviluppo senza firma Windows e non è pubblicata nel canale aggiornamenti.

# Candidato alpha.9 — texture DirectX sperimentali

Per le copie UG2 che non corrispondono agli indirizzi di TexWizard, il nuovo percorso usa Texture Toolkit 1.2.1 x86, fissato su sorgente, dimensione e SHA-256. Non cambia l'EXE. Prima si prepara e conferma il componente, poi si identifica una singola superficie nel gioco. Solo dopo si associa una DDS e si conferma l'attivazione delle sostituzioni.

Catture e texture personali restano in `%LOCALAPPDATA%\GamePanel`, anche quando il gioco è in Program Files o il componente viene rimosso. Le anteprime mostrano la cattura originale e il materiale scelto; un materiale procedurale asfalto 2048×2048 con mipmap completi è incluso. Journal e copie prima/dopo permettono il recupero; file sconosciuti o modificati dall'esterno bloccano le scritture.

Il componente resta **sperimentale e da collaudare sulla singola copia**. Non converte le mesh: questa versione non installa nuove geometrie della città o auto high-poly. La pagina elenca fonti degli autori e requisiti verificati, senza offrire download di pack non esaminati. [Guida DirectX](DIRECTX-TEXTURES.md), [auto e città](CAR-CITY-SOURCES.md).

Questo è un candidato di sviluppo, non una nuova release pubblicata tramite il canale aggiornamenti. I dati della libreria e i componenti scaricati restano nei dati utente. La build rimane senza firma Authenticode; non offre una nuova garanzia contro il blocco di Smart App Control.

# Alpha.8

Aggiunta la pagina **Texture e modelli**, con importazione ZIP inventariata, anteprima esplicita, installazione, disattivazione/rimozione e recupero dopo interruzioni. Le sostituzioni dei modelli conservano copie verificate degli originali; i pacchetti locali non possono introdurre installer o plugin arbitrari.

Primo pack originale **GamePanel Asphalt HD 2K**, scaricabile dalla nuova pagina: tre superfici stradali procedurali, 2048×2048 con mipmap, licenza MIT. È sperimentale e non ricopre tutta la città; geometrie e altri materiali restano originali. Il contenitore è verificato, la resa nel gioco reale resta da collaudare.

Le texture usano TexWizard Underground 2 1.3, scaricato separatamente dal repository dell'autore con hash fissati. Servono il Widescreen Fix già gestito e una build dell'EXE che superi controlli statici precisi; GamePanel verifica tutti gli 82 punti di chiamata e blocca layout diversi. Nessun ASI viene eseguito contro le fixture sintetiche di test.

**Da alpha.7:** aprire **Controlla aggiornamenti → Sì**. In alternativa estrarre tutto `GamePanel-win-x64.zip` in una cartella nuova e dedicata. Libreria, download, backup e impostazioni restano nei dati utente. `GamePanel-AsphaltHD.zip` è un asset separato da importare tramite GamePanel, non un programma da avviare.

La release non include un pacchetto di geometrie già verificato. RAR/TPF/script Binary non vengono convertiti o eseguiti automaticamente. Nessuna conversione HDR, DLSS o ricostruzione completa della città.

La build GamePanel rimane senza firma Authenticode e Smart App Control può bloccarla. Hash e audit statici non attestano l'assenza assoluta di malware o la fiducia Windows.

[Guida alpha.8](https://github.com/luigip11/DLSS5_ReShade_for_games/blob/main/docs/ALPHA8.md) · [Formato asset](https://github.com/luigip11/DLSS5_ReShade_for_games/blob/main/docs/ASSET-PACKS.md) · [Catalogo e provenienza](https://github.com/luigip11/DLSS5_ReShade_for_games/blob/main/docs/TEXWIZARD-CATALOG.md)
