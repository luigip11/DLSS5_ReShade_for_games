# Componenti candidati — nessun binario integrato nell'alpha

Riferimenti consultati il 29 ottobre 2026. Ogni integrazione richiede una nuova verifica su release/commit fissato.

| Progetto | Uso possibile | Verifica ancora richiesta |
|---|---|---|
| [RHI](https://github.com/RankFTW/RHI) | Logiche DLSS/HDR, rilevamento, restore | Audit mirato e licenza GPL se si riusa codice; sostituire aggiornamenti dinamici con catalogo controllato |
| [DLSS5 Swapper](https://dlss5swapper.org/updates) | Idee di flusso e gestione profili | Il repository collegato risultava 404 durante l'analisi; sorgente corrente non verificato |
| [ReShade](https://reshade.me/) | Post-processing e add-on | Licenza del codice distinta dalle condizioni di distribuzione dei binari ufficiali; versione/firma/architettura |
| [RenoDX](https://github.com/clshortfuse/renodx) | HDR e consumer specifici | Scegliere esattamente quale add-on; quelli con nomi simili non sono intercambiabili |
| [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder) | Titoli senza DLSS, helper x64 per giochi x86 | Pipeline, provenienza consumer, compatibilità e costo GPU per ciascun gioco |
| [dgVoodoo2](https://www.dege.freeweb.hu/dgVoodoo2/ReadmeGeneral/) | Traduzione API legacy dove necessaria | Il testo consultato vieta l'inclusione in launcher/framework per uso generico; nessun bundling senza base autorizzativa |
| [DXVK](https://github.com/doitsujin/dxvk) | Percorsi legacy alternativi | Licenza, renderer effettivo, layer Vulkan e compatibilità per titolo |
| NVIDIA DLSS/Streamline | Runtime proprietari e/o SDK applicabili | Origine ufficiale quando disponibile, firme, diritti di distribuzione, coppie versione/driver supportate |

Un wrapper che traduce API non aggiunge automaticamente i dati del motore richiesti da tutte le funzioni DLSS. SR, FG, RR e Neural Rendering richiedono valutazioni separate. Nessun pacchetto da Discord o mirror va assunto sicuro solo perché citato da un progetto.
