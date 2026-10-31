# Widescreen Fix — catalogo alpha.6

Origine: [release ufficiale nfsu2](https://github.com/ThirteenAG/WidescreenFixesPack/releases/tag/nfsu2). Asset `558911953`, aggiornato il 12 settembre 2026, esaminato il 31 ottobre 2026. Il tag e l'URL upstream possono essere aggiornati: la build accetta soltanto questi byte, non qualsiasi pacchetto futuro con lo stesso nome.

URL: `https://github.com/ThirteenAG/WidescreenFixesPack/releases/download/nfsu2/NFSUnderground2.WidescreenFix.zip`

ZIP: 8832311 byte; SHA-256 `791fcf486db6916642b02a51dd6ed786da7bdbd6c4ad7e207c36ba1f0a5cca59`.

| Voce originale | Byte | SHA-256 |
|---|---:|---|
| `dinput8.dll` | 5418336 | `d5a059aa467a7a7127c8f6169f79fa63ff0f55986ee9eb2fd9a281bebf2aa2e6` |
| `scripts/NFSUnderground2.WidescreenFix.asi` | 6695040 | `e7bdceeca0343fa394ac202aea0e48492a8a707cffa694e5a07aa98ba4a40f19` |
| `scripts/NFSUnderground2.WidescreenFix.dat` | 5736 | `2b8a08a7d99d4f3f28dd7adbaa0a49136688b948558fa5f3357723e0213b03f2` |
| `scripts/NFSUnderground2.WidescreenFix.ini` | 4508 | `cf5d7e58230dd92d0bde5d1a0d7ae5dd7f0b4173fce578c17251d6acb6bea430` |

Le due immagini PE vengono controllate come DLL x86, mai caricate dall'analizzatore. L'INI approvato è verificato come parte dell'archivio e poi sostituito con quello esplicito di GamePanel per ridurre funzioni non richieste. Il relativo hash finale viene registrato nel manifest dell'installazione, distinto dall'hash del file originale nella tabella.

Download esplicito, massimo tre redirect HTTPS: solo il repository `ThirteenAG/WidescreenFixesPack` su github.com e il CDN release-assets.githubusercontent.com. Dimensioni reali/dichiarate, ZIP e singole voci devono corrispondere; nessun URL configurabile o manifest remoto. I binari non vengono inseriti nel repository o nello ZIP GamePanel. Nessuna affermazione di firma Windows attendibile è dedotta dalla provenienza GitHub.

Licenza del repository: [MIT, ThirteenAG](https://github.com/ThirteenAG/WidescreenFixesPack/blob/master/license). Il sorgente visibile è stato consultato per capire opzioni e flusso; il tag è mobile e non è dimostrato che l'asset sia compilato dall'attuale commit del tag. Non si dichiara una ricompilazione riproducibile o un audit completo del binario. La combinazione con la build reale di Underground 2 e con ReShade rimane sperimentale.
