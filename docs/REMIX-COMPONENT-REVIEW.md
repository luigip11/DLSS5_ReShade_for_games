# Audit statico componenti Remix 0.4 — 2 novembre 2026

Lo ZIP esportato dall'utente contiene 44 payload, 6.227.713 byte, più il manifest. Dimensioni e SHA-256 di tutti i 44 file corrispondono al manifest. I file sono stati letti come dati: nessun EXE, DLL o ASI è stato caricato o eseguito. Questo audit verifica identità e alcuni comportamenti statici, non certifica sicurezza o compatibilità nel gioco.

L'archivio completo locale dichiarato ha 2.117.971.904 byte, MD5 `e5b57ad2ea50d4ff22dbdd408c20a322`, SHA-256 `dc17cc4a462935c129e08b84ee13251a4405abd91350363127f79287d36cc37f`. Lo SHA è stato ricavato dalla copia locale dell'utente; non è una firma indipendente del distributore. Il 7z completo non è stato trasferito né estratto qui.

## Incompatibilità AntiCull

| Elemento | Risultato statico |
|---|---|
| `scripts/NFSU2AntiCull.asi` | PE32 x86, 84.992 byte, versione incorporata 1.0.0.100 |
| SHA-256 AntiCull | `32868a9f7ebcb826175c82ab8477965f788f4521061fa97bb17e1e89eed4c1d5` |
| Entry point richiesto nel gioco | RVA `0x35BCC7` |
| EXE CD funzionante | 5.987.982 byte, SHA-256 `3233ef990fe5ac6a554ec65b821e7a5231c955c3419fe7e910e11d336cce1471` |
| Entry point dell'EXE CD | RVA `0x53309E` |

In DllMain, l'istruzione a VA preferita `0x100012EE` confronta direttamente l'AddressOfEntryPoint del modulo principale con `0x35BCC7`. In caso diverso mostra il messaggio relativo all'EXE NTSC e restituisce FALSE prima di applicare le modifiche. Il messaggio cita 4.800.512 byte, ma il controllo osservato è sull'entry point, non sulla dimensione.

Il ramo ammesso modifica in memoria due byte agli indirizzi fissi `0x5BC408` e `0x5BC419`, senza verificare in quel percorso i byte originali. Non è corretto trasferire questi indirizzi all'EXE CD o aggirare il controllo. Il codice nel [tag v1.0.0.1337 dell'autore](https://github.com/nlgxzef/NFSU2AntiCull/tree/v1.0.0.1337), in `dllmain.cpp` e `AntiCullStuff.h`, corrisponde a gate e indirizzi osservati. Questo non costituisce una verifica byte per byte dell'asset di release.

Decisione: mantenere l'EXE funzionante; **escludere AntiCull dalla prova** e dichiarare la possibile visibilità incompleta degli oggetti. Non si considera risolto il problema dell'altro EXE che non partiva sul PC.

## Configurazioni e dipendenze

- Il Widescreen Fix dell'archivio imposta 1920×1080. La prova conserva invece il fix e l'INI già funzionanti nel gioco. Le correzioni HUD/FOV non sono nuove texture dell'interfaccia.
- `dxvk.conf` contiene una voce attiva `rtx.useFreeCamera = true`; la copia la disattiva. Il resto della configurazione non viene reinterpretato.
- `rtx.conf` imposta `rtx.dlfg.enable = False`. Preferenze DLSS/RR e DLL presenti non provano che SR o RR siano attivi, né la compatibilità della versione runtime con RTX 5080.
- I 28 sublayer diretti di `mod.usda` compaiono nell'inventario completo. Alcuni riferimenti testuali non risolti dall'elenco possono usare percorsi di ricerca MDL o appartenere a layer non attivi: non vengono dichiarati automaticamente file mancanti.
- `adamplayer.usda` contiene luci del mondo: il nome non dimostra un nuovo personaggio. Non è stato individuato un pack completo di nuove immagini HUD/menu/font.
- `NFSU_XtendedInput.asii` non viene rinominato o attivato. I binari grandi del runtime non sono inclusi nei 44 payload: la prova reale resta necessaria.

Il client root x86 `d3d9.dll` ha 854.016 byte e SHA-256 `79448dd4dbe82ae4df13839323cd51e860fab681833b004e30d204c299c40b95`. Il bridge x64 esportato ha SHA-256 `4a9acb0191a78a48afb92879de372c807887294e40503ddb3d9530605142a911`. Le risorse versione legacy dei binari non bastano per attribuire una versione precisa del runtime.

## Distribuzione e collaudo

Le licenze MIT alla radice riguardano componenti specifici, non ogni asset: sono presenti anche condizioni NVIDIA/MDL e altre licenze. GamePanel non pubblica questo archivio o i suoi binari e conserva le licenze nella copia locale creata dall'utente.

La [prova alpha.12](REMIX-TRIAL.md) usa una cartella distinta, senza sovrascrivere il renderer del gioco originale e senza avvio automatico. Restano da verificare sul PC avvio, resa delle scene, culling, salvataggi condivisi, associazioni dei modelli e tecniche di ricostruzione attive. Un inventario e controlli di copia non sostituiscono questa prova.
