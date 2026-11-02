using GamePanel.Core;

internal static class CapabilityChecks
{
    public static void Register(Action<string, Action> check, string root, Func<string, bool, bool, string, string> makeExe)
    {
        check("Disponibilità: nome speed2 da solo non propone pacchetti Underground 2", () =>
        {
            var game = Fixture("unknown-speed2", "speed2.exe");
            var result = GameCapabilities.From(game);
            Require(!result.IsUnderground2 && !result.SupportsWidescreen && !result.SupportsTexWizard
                && !result.SupportsDirectXTextures && !result.HasRemixTrial, "Nome del file scambiato per compatibilità del gioco.");
        });
        check("Disponibilità: indizi UG2 e vincoli API restano separati dalla compatibilità di scrittura", () =>
        {
            var game = Fixture("ug2-shaped", "speed2.exe"); var folder = Path.GetDirectoryName(game.ExecutablePath)!;
            foreach (var child in new[] { "GLOBAL", "TRACKS", "CARS" }) Directory.CreateDirectory(Path.Combine(folder, child));
            var result = GameCapabilities.From(game);
            Require(result.IsUnderground2 && result.SupportsDirectXTextures && !result.SupportsTexWizard && !result.HasRemixTrial,
                "Disponibilità advisory non separata dal layout TexWizard e Remix.");
            var limited = GameCapabilities.From(game with { ScanWasLimited = true });
            Require(limited.IsUnderground2 && !limited.SupportsDirectXTextures && !limited.SupportsWidescreen, "Scansione incompleta non blocca gli strumenti.");
            var differentApi = GameCapabilities.From(game with { GraphicsApis = ["Direct3D 12"] });
            Require(!differentApi.IsUnderground2 && !differentApi.SupportsReShade, "Import/API di un altro renderer ignorato.");
        });
        check("Disponibilità: Carbon, San Andreas e giochi generici non ereditano correzioni UG2", () =>
        {
            foreach (var name in new[] { "nfsc.exe", "gta_sa.exe", "speed.exe", "ModernGame.exe" })
            {
                var result = GameCapabilities.From(Fixture("other-" + name, name));
                Require(!result.IsUnderground2 && !result.SupportsWidescreen && !result.SupportsDirectXTextures && !result.SupportsTexWizard,
                    "Pacchetti UG2 esposti per " + name);
                if (name != "ModernGame.exe") Require(result.SupportsReShade, "Profilo SDR esistente perso per " + name);
            }
            Require(!GameCapabilities.From(null).SupportsReShade, "Nessun gioco produce funzioni attive.");
        });
        check("Disponibilità: nomi di file Remix e metadati inventati non abilitano il renderer", () =>
        {
            var game = Fixture("false-remix", "speed2.exe"); var folder = Path.GetDirectoryName(game.ExecutablePath)!;
            File.WriteAllText(Path.Combine(folder, "d3d9.dll"), "not a renderer"); Directory.CreateDirectory(Path.Combine(folder, ".trex"));
            File.WriteAllText(Path.Combine(folder, "GamePanel-RemixTrial.json"), "{}");
            var result = GameCapabilities.From(game);
            Require(!result.HasRemixRenderer && !result.HasRemixTrial, "Nomi o JSON vuoto scambiati per runtime verificato.");
        });
        check("Varianti libreria: solo relazioni registrate raggruppano, senza perdere nomi o percorsi mancanti", () =>
        {
            var original = Entry("variant-original", "Mio Underground 2"); var copy = Entry("variant-copy", "Copia personale");
            var other = Entry("variant-other", "Altro gioco");
            var snapshot = new LibrarySnapshot(1, copy.Id, [original, copy, other]);
            Require(GameLibraryVariants.Group(snapshot, []).Count == 3, "Hash e nomi simili raggruppati senza registrazione.");
            var result = GameLibraryVariants.Group(snapshot, [(original.ExecutablePath, copy.ExecutablePath)]);
            Require(result.Count == 2 && result.Sum(g => g.Variants.Count) == 3, "Gruppo perde o duplica giochi.");
            var group = result.Single(g => g.Original.Id == original.Id);
            Require(group.Original.Title == original.Title && group.Preferred(snapshot.SelectedId).Id == copy.Id, "Nome o selezione della variante perso.");
            File.Delete(original.ExecutablePath);
            Require(GameLibraryVariants.Group(snapshot, [(original.ExecutablePath, copy.ExecutablePath)]).Sum(g => g.Variants.Count) == 3,
                "Sorgente mancante eliminata dalla libreria.");
            var missing = GameLibraryVariants.Group(snapshot, [(original.ExecutablePath, Path.Combine(root, "not-imported.exe"))]);
            Require(missing.Count == 3, "Relazione incompleta nasconde un gioco.");
            var conflicting = GameLibraryVariants.Group(snapshot,
                [(original.ExecutablePath, copy.ExecutablePath), (other.ExecutablePath, copy.ExecutablePath)]);
            Require(conflicting.Count == 3, "Relazioni ambigue non ritornano a schede separate.");
        });
        check("Varianti libreria: selezione persistente senza rianalisi, avvio o cambi ai file del gioco", () =>
        {
            var a = Fixture("select-original", "speed2.exe"); var b = Fixture("select-copy", "speed2.exe");
            var folder = Path.Combine(root, "capabilities-select-metadata"); var store = new GameLibrary(folder);
            var first = store.Remember(a).SelectedId!; var before = store.Remember(b);
            var aBytes = File.ReadAllBytes(a.ExecutablePath); var bBytes = File.ReadAllBytes(b.ExecutablePath);
            var selected = store.Select(first); var reopened = new GameLibrary(folder).Load();
            Require(selected.SelectedId == first && reopened.SelectedId == first && reopened.Games.Count == before.Games.Count,
                "Scelta della versione non conservata dopo riapertura.");
            Require(reopened.Games.All(g => before.Games.Any(old => old.Id == g.Id && old.Sha256 == g.Sha256
                && old.Title == g.Title && old.LastInspectedAt == g.LastInspectedAt && old.LastPlayedAt == g.LastPlayedAt))
                && File.ReadAllBytes(a.ExecutablePath).SequenceEqual(aBytes) && File.ReadAllBytes(b.ExecutablePath).SequenceEqual(bBytes),
                "La scelta modifica analisi, avvio, nomi o dati del gioco.");
            try { store.Select(new string('f', 32)); throw new Exception("ID inesistente accettato."); }
            catch (InvalidDataException) { }
            Require(store.Load().SelectedId == first, "Selezione persa dopo ID non valido.");
        });

        GameReport Fixture(string name, string file)
        {
            var folder = Path.Combine(root, "capabilities-" + name); Directory.CreateDirectory(folder);
            var path = makeExe(Path.Combine(folder, file), false, false, "d3d9.dll");
            return new GameInspector().Inspect(path);
        }
        LibraryGame Entry(string name, string title)
        {
            var report = Fixture(name, "speed2.exe");
            return new(name, title, report.ExecutablePath, report.Sha256, report.Architecture, report.GraphicsApis,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, true);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
