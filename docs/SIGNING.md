# Smart App Control: blocco confermato e firma ancora da attivare

Stato al 29 ottobre 2026: alpha.2 è compilata e supera i test del core su Windows, ma i file GamePanel non hanno una firma Authenticode di un editore attendibile. È stato osservato un blocco di Controllo intelligente delle app per editore non verificabile. Questo impedisce il collaudo sul PC interessato prima ancora dell'analisi del gioco.

## Significato e limite attuale

Smart App Control può bloccare software sconosciuto senza firma valida. Il messaggio di editore non verificabile non equivale a un verdetto antivirus; non dimostra neppure che il file sia innocuo. Microsoft non offre un'eccezione per consentire una singola app con questa protezione attiva. Riferimento: [FAQ Microsoft](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions).

La correzione di distribuzione è una firma con certificato emesso da un provider attendibile. Una firma creata autonomamente per sviluppo non costituisce tale identità. La firma attesta provenienza e integrità, non garantisce assenza di vulnerabilità o malware. Riferimento: [firma per Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control).

## Percorsi concreti di attivazione

1. **Servizio di firma per open source:** valutare la candidatura del proprietario del progetto a [SignPath Foundation](https://signpath.org/terms). L'offerta può essere gratuita per progetti idonei; ammissione e tempi non sono garantiti. I vincoli sulle componenti proprietarie vanno riesaminati prima delle future integrazioni DLSS. Non è stata presentata una candidatura e non è disponibile una firma della Foundation.
2. **Identità pubblica tramite Azure Artifact Signing:** occorrono un account, la validazione dell'identità e un profilo di certificato. La configurazione può comportare costi e richiede il coinvolgimento del titolare dell'account. In GitHub Actions è preferibile autenticazione OIDC, senza copiare chiavi private nel repository. Non sono stati creati account o risorse a pagamento. [Prerequisiti Microsoft](https://learn.microsoft.com/en-us/azure/trusted-signing/how-to-signing-integrations), [azione ufficiale](https://github.com/Azure/artifact-signing-action).

Non è possibile trasformare questa build in una distribuzione con editore pubblicamente attendibile attraverso sole modifiche al codice: manca un'identità di firma autorizzata e configurata. Non caricare chiavi private, password o PFX in chat, issue o repository.

## Requisiti della futura pipeline firmata

- Compilare da un commit identificato e superare controlli e collaudo dell'avvio GUI su Windows.
- Firmare `GamePanel.exe`, `GamePanel.dll` e `GamePanel.Core.dll`, più eventuale altro codice del progetto distribuito; conservare le firme originali dei componenti Microsoft. Nuove DLL/add-on di terzi richiedono una valutazione propria di provenienza, licenza e fiducia.
- Usare timestamp e verificare che Authenticode risulti valido per ciascun file del progetto e che l'editore sia quello atteso.
- Bloccare la pubblicazione della distribuzione firmata se un file richiesto manca, la firma fallisce o non è valida. Non ripiegare silenziosamente su un pacchetto non firmato.
- Calcolare gli hash dopo la firma e archiviare esattamente quei byte. Il solo SHA-256 non sostituisce Authenticode.
- Provare il pacchetto scaricato e completamente estratto su Windows 11 con Smart App Control attivo prima di dichiararlo collaudato per tale configurazione.

Microsoft raccomanda di considerare tutti i binari distribuiti, inclusi DLL e componenti dei setup: [linee guida di distribuzione](https://learn.microsoft.com/en-us/windows/security/book/application-security-application-and-driver-control).

## Estrazione del pacchetto

L'artifact GitHub attuale contiene un secondo ZIP portabile. Devono essere estratti entrambi, fino a ottenere `GamePanel.exe` e tutte le DLL nella stessa cartella. Un percorso temporaneo di WinRAR indica un avvio dalla vista dell'archivio. L'estrazione corretta evita dipendenze mancanti, ma non aggiunge la firma che Smart App Control richiede in questo caso.

Non cambiare le impostazioni di protezione del PC per compensare questo limite di distribuzione.
