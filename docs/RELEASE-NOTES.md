Aggiunta la pagina **Texture e modelli**, con importazione ZIP inventariata, anteprima esplicita, installazione, disattivazione/rimozione e recupero dopo interruzioni. Le sostituzioni dei modelli conservano copie verificate degli originali; i pacchetti locali non possono introdurre installer o plugin arbitrari.

Primo pack originale **GamePanel Asphalt HD 2K**, scaricabile dalla nuova pagina: tre superfici stradali procedurali, 2048×2048 con mipmap, licenza MIT. È sperimentale e non ricopre tutta la città; geometrie e altri materiali restano originali. Il contenitore è verificato, la resa nel gioco reale resta da collaudare.

Le texture usano TexWizard Underground 2 1.3, scaricato separatamente dal repository dell'autore con hash fissati. Servono il Widescreen Fix già gestito e una build dell'EXE che superi controlli statici precisi; GamePanel verifica tutti gli 82 punti di chiamata e blocca layout diversi. Nessun ASI viene eseguito contro le fixture sintetiche di test.

**Da alpha.7:** aprire **Controlla aggiornamenti → Sì**. In alternativa estrarre tutto `GamePanel-win-x64.zip` in una cartella nuova e dedicata. Libreria, download, backup e impostazioni restano nei dati utente. `GamePanel-AsphaltHD.zip` è un asset separato da importare tramite GamePanel, non un programma da avviare.

La release non include un pacchetto di geometrie già verificato. RAR/TPF/script Binary non vengono convertiti o eseguiti automaticamente. Nessuna conversione HDR, DLSS o ricostruzione completa della città.

La build GamePanel rimane senza firma Authenticode e Smart App Control può bloccarla. Hash e audit statici non attestano l'assenza assoluta di malware o la fiducia Windows.

[Guida alpha.8](https://github.com/luigip11/DLSS5_ReShade_for_games/blob/main/docs/ALPHA8.md) · [Formato asset](https://github.com/luigip11/DLSS5_ReShade_for_games/blob/main/docs/ASSET-PACKS.md) · [Catalogo e provenienza](https://github.com/luigip11/DLSS5_ReShade_for_games/blob/main/docs/TEXWIZARD-CATALOG.md)
