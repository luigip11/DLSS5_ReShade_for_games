# Confrontare le prestazioni di UG1 Remix

Il caso osservato è 60–80 FPS nel contatore con Frame Generation 4×, su monitor a 180 Hz. Il contatore include fotogrammi generati: non dimostra 60–80 FPS di rendering e non basta dividerlo per quattro per ricavare una misura affidabile. La frequenza dello schermo non obbliga il gioco a raggiungere 180 FPS.

Conserva prima la configurazione che si vede correttamente e il nuovo avvio tramite Esplora file. Per il confronto usa lo stesso percorso, auto, meteo e una durata simile; annota minimo, media e scatti, dopo il caricamento iniziale. Non cambiare impostazioni di illuminazione o contenuti insieme ai controlli di prestazione.

1. In **Alt+X → General**, disattiva temporaneamente Frame Generation e misura gli FPS effettivamente renderizzati. Conserva Ray Reconstruction Transformer, Reflex Enabled e la risoluzione personale.
2. Confronta **DLSS Bilanciato** e **Performance**, scegliendoli esplicitamente invece di Auto. Con Performance l'immagine parte da meno pixel; controlla leggibilità, pioggia e dettagli mentre guidi.
3. In **Rendering**, prova **Volumetric Lighting disattivato**. Confronta prima di fare altre modifiche.
4. Se serve ulteriore margine, prova separatamente qualità **Particle Light** e **Neural Radiance Cache** inferiori, usando le opzioni offerte dal tuo runtime. Mantieni inizialmente i rimbalzi 0–4: ridurli può cambiare l'illuminazione.
5. Motion Blur, aberrazione cromatica e vignettatura possono restare disattivati per un'immagine più leggibile; non sono una promessa di un grande aumento degli FPS.
6. Riattiva Frame Generation dopo aver migliorato la base e confronta fluidità, risposta ai comandi e artefatti. Un limite FPS va scelto in base al minimo sostenibile e verificato con FG attivo: alcuni limitatori agiscono sul rendering e altri sul conteggio finale.

Un carico GPU vicino al 100% è normale in una scena limitata dalla GPU senza un limite FPS. L'obiettivo è ridurre gli scatti e mantenere margine nelle scene più pesanti, non ottenere un numero di utilizzo più basso a qualsiasi costo. Le prove non cambiano automaticamente la configurazione da GamePanel.
