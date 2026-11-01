# Texture DirectX — diagnosi manuale

Da alpha.10 il percorso principale è [preparazione e installazione in blocco](AUTOMATIC-TEXTURES.md). Le istruzioni qui sotto restano per diagnosi e sostituzioni individuali.

Questa versione aggiunge una prova sperimentale per Underground 2 x86 Direct3D 9, mantenendo l'eseguibile del gioco. La verifica statica delle API non dimostra che il componente funzioni nella singola copia: il primo avvio serve a verificarlo. Il componente sostituisce pixel delle texture, non modelli 3D o geometrie della città.

## Componente e origine

- Autore: BadassBaboon, [Texture Toolkit](https://github.com/BadassBaboon/Texture-Toolkit).
- Versione fissata: [v1.2.1](https://github.com/BadassBaboon/Texture-Toolkit/releases/tag/v1.2.1), commit `d600d7bdc8293dd1e372f4565a970af99e0acffd`.
- File x86: `TextureToolkit-x86.asi`, 1.122.304 byte; SHA-256 `4c7763ae8b54f5077365729b7360f427109bfb73f9500e2d48e6082930d065e4`.
- Licenza MIT, inclusa nel pacchetto. Il confronto SHA-256 verifica l'identità dei byte fissati; non è una firma Windows né una certificazione antivirus.
- L'autore documenta la convivenza con ReShade su Bully; Underground 2 non è tra i giochi collaudati nella sua lista. Non estendere quel risultato al gioco dell'utente.

Il codice di questa release intercetta le API nel processo ospite. Non individua i punti di codice di TexWizard, non modifica l'EXE, non sostituisce il renderer e non interviene sulle protezioni del gioco.

## Prima prova

1. Avvia il gioco con la configurazione funzionante e il disco richiesto; poi chiudilo.
2. In GamePanel, analizza l'EXE corrente. Nella pagina Schermo deve risultare installato il Widescreen Fix gestito: fornisce l'ASI loader verificato. Rimuovi tramite GamePanel eventuali pack texture TexWizard prima di preparare il percorso alternativo.
3. In Texture e modelli premi **Prepara texture DirectX**, controlla l'anteprima e conferma. Solo questa conferma installa il componente. Le sostituzioni e il dump automatico sono inizialmente disattivati.
4. Avvia normalmente il gioco, entra in una gara e premi **INSERT**. Metti in pausa mentre usi il pannello; la release upstream può bloccare anche lo stato del controller mentre il pannello è aperto.
5. Nel pannello seleziona una superficie. **Blink in game** permette di riconoscerla; **Dump** salva la singola texture. Non serve attivare Auto-dump.
6. Chiudi il gioco, torna in GamePanel e premi **Aggiorna catture**. Seleziona la cattura guardando l'anteprima, poi scegli una DDS personale oppure **Usa asfalto GamePanel 2K**. Conferma l'associazione alla superficie scelta.
7. Prepara e conferma l'attivazione delle sostituzioni. Riavvia il gioco e confronta quella superficie. L'esistenza del file non dimostra che la sostituzione sia stata disegnata: questa è la verifica visiva necessaria nel gioco.

Se il componente impedisce l'avvio, chiudi il gioco e usa **Prepara rimozione componente**. L'anteprima rimuove soltanto i file runtime posseduti; catture, texture personali e copie per il recupero restano conservate. Un'operazione interrotta espone i pulsanti di ripresa e annullamento.

Se Windows nega la scrittura dei due file nella cartella del gioco, la modifica richiede GamePanel con i permessi necessari, nello stesso account. L'avvio normale del gioco e le catture usano la cartella dati scrivibile. Non sono previste modifiche alle ACL o eccezioni antivirus.

## File e dati conservati

Nel gioco vengono gestiti soltanto `scripts/TextureToolkit-x86.asi` e `scripts/TextureToolkit.ini`. La DLL ReShade e il loader Widescreen esistenti non vengono sostituiti.

`ResourceRoot` è un percorso assoluto sotto `%LOCALAPPDATA%\GamePanel\DirectXTextures\<chiave-cartella-gioco>`. Contiene `dump` per le catture e `inject` per le sostituzioni DDS. Questo evita di richiedere scritture nella cartella Program Files durante la cattura. Il log upstream resta accanto all'ASI e può non essere scrivibile in una cartella protetta.

Stato, journal e copie prima/dopo sono in `%LOCALAPPDATA%\GamePanel\Profiles`. Tutti i writer condividono il lock del gioco; un journal interrotto blocca le altre modifiche. DLL sconosciute, EXE cambiati, link filesystem, configurazioni reindirizzate o DDS gestite modificate dall'esterno causano un arresto dell'operazione.

Le catture sono identificate dall'hash a 64 bit dei pixel originali, calcolato come nel sorgente upstream fissato. GamePanel verifica che nome e contenuto corrispondano. Non usa il nome della superficie nel motore e non indovina a quale materiale applicare l'asfalto.

## Materiale incluso e limiti

Il campione originale GamePanel è una sola texture procedurale BC1 da 2048×2048 con 12 livelli mip, fino a 1×1. Non contiene risorse del gioco o asset di terzi. Il suo script MIT è `scripts/AsphaltPackBuild/build_directx_sample.py`; la build ricontrolla 2.796.344 byte e SHA-256 `7adaf63cc2234e6dc29ab87faa01fbc273523580ab14ef30e52babfe1a04e98f`.

La superficie viene scelta dall'utente. Una texture con segnaletica o più materiali può perdere tali dettagli se sostituita con semplice asfalto. Non applicare lo stesso materiale indiscriminatamente alle texture del gioco.

Sono accettate DDS 2D singole BC1/2/3, RGBA8 o BGRA8, con header legacy o DX10, entro 8192×8192 e 128 MiB. Le sostituzioni gestite richiedono mipmap completi. Cubemap, array, volumi e formati sconosciuti vengono rifiutati. L'elenco catture è limitato e segnala file saltati invece di presentarli come validi.

## Prove riproducibili

I controlli Core usano dati sintetici per download alterati, limiti DDS, hash confrontati con il C++ upstream, recupero dei journal e conflitti. La build Windows compila inoltre un piccolo renderer D3D9 originale: verifica cattura 64×64, sostituzione 128×128 e pixel effettivamente renderizzato, poi ripete con ReShade 6.8. Se il runner non dispone di un adattatore D3D9 registra **UNVERIFIED**, mai PASS. Anche una prova sintetica riuscita non certifica il gioco reale.
