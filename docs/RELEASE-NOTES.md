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
