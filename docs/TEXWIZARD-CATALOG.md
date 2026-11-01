# Catalogo texture e provenienza

## TexWizard Underground 2 1.3

Fonte: [R-033/texwizard](https://github.com/R-033/texwizard), tag 1.3, commit `51e7d29c070aa8f9b7c3b44b78fadfda299777a3`. Licenza GPL-3.0 upstream; GamePanel non incorpora né collega questo codice nel proprio eseguibile MIT. Scarica il componente indipendente dall'autore soltanto su richiesta esplicita.

- ZIP: `https://github.com/R-033/texwizard/releases/download/1.3/TexWizard_UG2.zip`
- ZIP byte: 169134; SHA-256 `2d0988ab482219cf97a0d3781139c3a5ef6388d3a50030fedc8803c7de9d8604`.
- Solo ASI estratto: `scripts/TexWizard.asi`, 335872 byte; SHA-256 `63b464597f60d7e2f29dc65bd48919d9336896d1f54c9be8247157c14ebfe25b`.
- Il setup, gli esempi e altri file della release non vengono eseguiti o installati.
- [Sorgente corrispondente e licenza](https://github.com/R-033/texwizard/tree/51e7d29c070aa8f9b7c3b44b78fadfda299777a3).

Preflight: speed2.exe x86 DX9 di 4.800.512 byte, PE32 base 0x400000, entry point RVA 0x35BCC7, ASLR assente. Sono verificati staticamente tutti gli 81 CALL di GetTextureInfo verso 0x4901D0 e il CALL globale 0x57ED99 verso 0x57FB40; gli offset sono fatti derivati da UG2_Address.h della versione fissata. Il solo filename non autorizza l'installazione. I controlli non certificano l'intera compatibilità runtime.

Configurazione posseduta da GamePanel: `scripts/TexWizard.json`, packs limitato ai namespace gestiti e `autoScan: false`. Il percorso è `TexturePacks\GamePanel-<Id>`, senza prefisso `..\`: TexWizard aggiunge quel prefisso solo per aprire il metadata durante DllMain. Il loader ThirteenAG pinned riporta v9.7.4, sorgente `6b440669144c4a0bef5718ab155df160d231cd42`: imposta CWD su scripts mentre carica l'ASI e poi ripristina la root per la richiesta del BIN. Un loader/configurazione diversa richiede un collaudo separato.

Nessuna firma Authenticode GamePanel viene conferita all'ASI. Hash e fonte non equivalgono a fiducia Windows o a una garanzia assoluta contro malware. Non si aggiungono esclusioni antivirus.

## GamePanel Asphalt HD 2K

Opera procedurale originale MIT: tre superfici 2048×2048 DXT1, nove mip fino a 8×8. BIN costruito e riletto dai sorgenti MIT Nikki/Darius, senza eseguire tool grafici o ASI scaricati.

- Asset: `GamePanel-AsphaltHD.zip`, release GamePanel v0.1.0-alpha.8.
- Byte: 8397130; SHA-256 `45fe214ba832782c2f46ed32265f5428bf34e82ac0c7bc3f1b5c4d9c11e3d67a`.
- BIN: 8389888 byte; SHA-256 `3990fe97b7dd5b72446adf71c5fd508d78b4afa75919c6f8319442c2e3522bb6`.
- Generatore e builder in `scripts/AsphaltPackBuild`; Python 3.12.14, NumPy 2.3.5, SDK .NET 10.0.403. Algoritmi interi, timestamp ZIP fissi, ZIP STORE e piattaforma ZIP esplicita. La pipeline verifica il digest atteso prima della pubblicazione.
- Darius commit `059016553a4d7ecf0b642e346c527cba18a5c326`, ZIP SHA-256 `29632d0b7be7f91ab0ea3b2b96e7bdccdbb6d990580b7458426b727d75607fff`.
- CoreExtensions commit `4c9d4062b3c5b0ab7b5cddaf5f9b87d739f3c510`, ZIP SHA-256 `6986e381acfce12481fca2bda56f2fc5612101e13e6992603889a8a42baa293a`.
- Nomi originali verificati nell'elenco [NFSRaider Underground2.txt](https://github.com/Felipe379/NFSRaider/blob/492bf005c624c537b4fd15ae61b76813b43ef949/NFSRaider/Keys/MainKeys/Tracks/Textures/Underground2.txt), commit `492bf005c624c537b4fd15ae61b76813b43ef949`.

Il pack non include texture EA, artwork di altri mod o un pacchetto di geometrie. La validazione del contenitore e dei mapping non prova la resa nel gioco: UV, attributi materiali e collocazione delle superfici richiedono il collaudo di Luigi. Disattivazione e rimozione riportano alla selezione delle texture originali.
