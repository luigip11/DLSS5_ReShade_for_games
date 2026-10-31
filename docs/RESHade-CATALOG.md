# Catalogo ReShade alpha.5

Verificato il 31 ottobre 2026. Il codice incorpora URL, versione, dimensioni e hash; nessun file di configurazione scaricato può sostituirli.

| Campo | Valore approvato |
|---|---|
| Versione | 6.8.0, x86 Direct3D 9 |
| Origine | https://reshade.me/downloads/ReShade_Setup_6.8.0.exe |
| Setup, byte | 4075064 |
| Setup, SHA-256 | `207aea16205fbf952bc8fe1879966672454cf04002e7ad34237c7990a5b3c0b4` |
| `ReShade32.dll`, byte | 4136216 |
| DLL, SHA-256 | `538998f66c0197adcdeadfe8e5ea19dd6ca3253e40399476d291f385ce518b88` |
| Impronta certificato pubblicata sul sito | `589690208A5E52FB96980C4A6698F50ACD47C49F` |
| Authenticode osservato su Windows | `UnknownError`: catena terminata in radice non attendibile |

[Ispezione della fonte ufficiale](https://github.com/luigip11/DLSS5_ReShade_for_games/actions/runs/41186369559): download HTTPS, hash del pacchetto fissato dopo revisione, lettura statica dell'archivio incorporato, estrazione della sola `ReShade32.dll`, versione/fingerprint e hash risultanti. Nessun setup o DLL è stato eseguito o allegato agli artifact durante questa ispezione. Il successivo test di rendering usa una fixture DX9 scritta nel repository, mai un gioco dell'utente.

L'impronta confronta il certificato presente nei byte approvati; non stabilisce fiducia Windows. Il controllo decisivo di identità è SHA-256 di setup e DLL. Una versione nuova richiede revisione del catalogo e nuova build. Non sono ammessi mirror, URL liberi, redirect o versioni arbitrarie importate dall'utente.
