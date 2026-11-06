# Pacchetti locali RTX Remix

## Percorso disponibile in alpha.16

1. Importa e analizza la copia originale compatibile di UG1 o UG2.
2. In Grafica > Configura, apri la preparazione Remix. Seleziona una cartella della mod gia estratta oppure uno ZIP prodotto dall'esportazione locale GamePanel.
3. Scegli una cartella vuota, esterna al gioco e al pacchetto. UG2 propone separatamente Smol per Golf, IS300 e Sentra: disabilitarlo non rimuove modelli gia presenti nell'originale.
4. Prepara l'anteprima: vengono verificati EXE, componenti, file originali e spazio. I file dell'originale restano conservati. Chiudi i giochi durante l'operazione.
5. Crea la copia. Una cancellazione o un conflitto prima del completamento non pubblica una copia parziale. La cartella temporanea posseduta dall'operazione viene rimossa.
6. Continua con la copia per analizzarla, collegarla all'originale e preparare la registrazione del profilo. Conferma la registrazione nella pagina DLSS, poi scegli risoluzione e impostazioni supportate. L'avvio usa il percorso della copia.

Il nuovo importatore accetta le versioni locali identificate negli inventari del 6 novembre 2026, non qualsiasi runtime o archivio con lo stesso nome. Le configurazioni di testo personalizzate entro il limite vengono riconosciute come personalizzate e conservate; gli altri componenti devono corrispondere agli hash del catalogo. Non e una firma del produttore o una certificazione del risultato nel gioco.

Gli ZIP devono avere i componenti in radice, senza cartella wrapper e senza file estranei. Un archivio completo dell'autore puo includere file ulteriori: estrailo separatamente e seleziona la cartella della mod; la procedura copia soltanto i file previsti dal catalogo. Nessun EXE del gioco viene fornito dal pacchetto. Cartelle di sistema, percorsi annidati, collegamenti, traversal e duplicati non vengono accettati.

## Dati base e componenti

UG2 mantiene FRONTEND/FrontB.lzc dell'utente: la differenza esaminata era nella lobby online PC_OL_Lobby.fng, non nei restanti blocchi del file. Il comportamento dell'installazione risultante richiede una prova in gioco. La base collaudata conteneva gia le tre geometrie Smol; non e una prova di compatibilita su una copia vanilla senza quel componente.

UG1 comprende le 11 modifiche Global/FrontEnd identificate. L'audio lossless opzionale non e incluso nel catalogo di preparazione. Configurazioni e runtime UG1/UG2 restano distinti. La registrazione del profilo non forza i valori del preset UG2 su UG1.

Cache, journal, stato GamePanel e loader ReShade/texture della copia originale vengono esclusi dal percorso rapido; i dati del gioco e i modelli gia presenti sono riutilizzati. I salvataggi esterni e il registro possono essere condivisi dalle due copie: non vengono isolati.

## Esportazione e distribuzione

Da Avanzate > Esporta il pacchetto locale puoi generare uno ZIP verificato dei soli componenti catalogati. Non include EXE del gioco e non effettua upload. La presenza di un file nel catalogo tecnico non concede diritti di distribuzione. Non pubblicare l'archivio come pacchetto GamePanel finche non sono completati controllo di provenienza, licenze dei binari e asset, hosting e collaudo della copia generata.

Il progetto UG2 Ekozmaster e MIT; conservare licenza e crediti per le parti coperte, senza estenderli automaticamente ai componenti proprietari. Smol richiede README inalterato e crediti AJ_Lethal. Per gli asset UG1 il permesso di ripacchettamento non e stato identificato nei materiali ricevuti. Il pulsante di download resta quindi disabilitato, con collegamento alla fonte e importazione locale funzionante.

## Hosting su GitHub Releases

Gli archivi possono essere allegati a una release dedicata di luigip11/GamePanel, fuori dalla cronologia Git e dal pacchetto dell'app. GitHub richiede ogni allegato inferiore a 2 GiB: https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases.

Dopo "Esporta ZIP locale verificato", "Prepara file per GitHub Releases" crea una nuova cartella accanto allo ZIP: parti di massimo 2.000.000.000 byte e manifest con dimensioni e SHA-256 di ogni parte e dello ZIP completo. Lo ZIP originale resta disponibile. Non effettua upload. Il manifest nasce con DistributionReviewed=false: gli hash attestano l'integrita, non i permessi di redistribuzione.

Dopo la revisione dei diritti e un test della copia generata, si potra creare una release dedicata ai pacchetti, caricare tutte le parti e il manifest, quindi collegare gli asset pubblicati al catalogo download dell'app. Non caricare cartelle complete del gioco, EXE, cache o salvataggi. UG1 attende ancora il permesso dell'autore; per UG2 completare i termini di tutti i componenti, oltre alla licenza MIT del progetto.

Chi riceve le parti deve metterle insieme al manifest nella stessa cartella e usare "Ricostruisci ZIP dalle parti scaricate" in GamePanel. La ricostruzione verifica tutti gli hash e rifiuta file incompleti; dopo serve comunque "Verifica pacchetto e prepara anteprima", che controlla i singoli componenti prima della creazione della copia. In questa versione il download automatico pubblico resta indisponibile: non esistono ancora release asset pubblicati e revisionati.
