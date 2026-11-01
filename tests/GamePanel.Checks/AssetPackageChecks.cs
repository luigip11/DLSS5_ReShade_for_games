using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamePanel.Core;

internal static class AssetPackageChecks
{
    public static void Register(Action<string, Action> check, string root)
    {
        check("Asset: texture importate e cache riutilizzata senza toccare gli input", () =>
        {
            using var f = new Fixture(root); var zip = f.Pack(); var before = File.ReadAllBytes(zip);
            var result = f.Importer.Import(zip);
            Require(result.ArchiveSha256 == Hash(before), "Identità archivio errata.");
            Require(result.Manifest.Kind == "Textures", "Tipo pack perso.");
            Require(File.ReadAllBytes(Path.Combine(result.Folder, "files", f.TexturePath)).SequenceEqual(f.Payloads[f.TexturePath]), "Payload modificato.");
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(result.Folder, "files", f.TexturePath));
            Require(f.Importer.Import(zip).Folder == result.Folder, "Cache non riutilizzata.");
            Require(File.GetLastWriteTimeUtc(Path.Combine(result.Folder, "files", f.TexturePath)) == stamp, "Cache riscritta.");
            Require(File.ReadAllBytes(zip).SequenceEqual(before), "Archivio sorgente modificato.");
        });
        check("Asset: modelli richiedono la coppia geometria e texture per auto", () =>
        {
            using var f = new Fixture(root); f.Models();
            Require(f.Importer.Import(f.Pack()).Manifest.Files.Count == 2, "Modello valido rifiutato.");
            var geometry = f.Payloads["CARS/COROLLA/GEOMETRY.BIN"];
            f.Payloads["CARS/COROLLA/GEOMETRY.BIN"] = [0, 0, 0x13, 0x80, 0xff, 0xff, 0xff, 0x7f];
            Refused(() => f.Importer.Import(f.Pack()));
            f.Payloads["CARS/COROLLA/GEOMETRY.BIN"] = geometry;
            f.Payloads.Remove("CARS/COROLLA/TEXTURES.BIN");
            Refused(() => f.Importer.Import(f.Pack())); f.NoPending();
        });
        check("Asset: nessun loader o originale GLOBAL/frontend è ammesso", () =>
        {
            foreach (var path in new[] { "d3d9.dll", "GLOBAL/textures.bin", "FRONTEND/frontend.bin", "TexturePacks/GamePanel-sample-pack/loader.asi", "CARS/COROLLA/evil.exe", "GamePanelAssets/sample-pack/readme.ps1" })
            {
                using var f = new Fixture(root); f.Payloads[path] = [1, 2, 3, 4, 5, 6, 7, 8];
                Refused(() => f.Importer.Import(f.Pack())); f.NoPending();
            }
        });
        check("Asset: traversal, device names e percorsi ambigui sono rifiutati", () =>
        {
            foreach (var path in new[] { "../outside.bin", "/outside.bin", "C:/outside.bin", "GamePanelAssets/sample-pack/CON.txt", "GamePanelAssets/sample-pack/NUL.md", "GamePanelAssets/sample-pack/folder./readme.txt", "GamePanelAssets/sample-pack/a:stream.txt", "GamePanelAssets\\sample-pack\\readme.txt" })
            {
                using var f = new Fixture(root); var zip = f.Pack(extras: [(path, new byte[] { 1 })]);
                Refused(() => f.Importer.Import(zip)); f.NoPending();
                Require(!File.Exists(Path.Combine(f.Root, "outside.bin")), "Traversal ha scritto fuori cache.");
            }
        });
        check("Asset: inventario ZIP esatto, duplicati e conflitti file/cartella", () =>
        {
            using var f = new Fixture(root);
            foreach (var extras in new (string, byte[])[][]
            {
                [("unlisted.txt", [1])], [(f.TexturePath, [1])], [(f.TexturePath.ToUpperInvariant(), [1])],
                [("TexturePacks", [1])], [("unused/", [])]
            }) Refused(() => f.Importer.Import(f.Pack(extras: extras)));
            var missing = f.Manifest() with { Files = f.Manifest().Files.Append(new("GamePanelAssets/sample-pack/missing.txt", Hash([1]), 1)).ToArray() };
            Refused(() => f.Importer.Import(f.Pack(missing))); f.NoPending();
        });
        check("Asset: link e file speciali nello ZIP sono rifiutati", () =>
        {
            using var f = new Fixture(root);
            foreach (var attributes in new[] { unchecked((int)0xa1ff0000), unchecked((int)0x11ff0000), (int)FileAttributes.ReparsePoint })
            {
                var zip = f.Pack();
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) archive.GetEntry(f.TexturePath)!.ExternalAttributes = attributes;
                Refused(() => f.Importer.Import(zip));
            }
            f.NoPending();
        });
        check("Asset: hash, dimensioni e manifest duplicati non sono ignorati", () =>
        {
            using var f = new Fixture(root); var manifest = f.Manifest();
            foreach (var file in new[] { manifest.Files[0] with { Sha256 = new string('0', 64) }, manifest.Files[0] with { Bytes = 1 }, manifest.Files[0] with { Bytes = AssetPackageImporter.MaximumFileBytes + 1 } })
                Refused(() => f.Importer.Import(f.Pack(manifest with { Files = [file, manifest.Files[1]] })));
            var zip = f.Pack();
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            {
                archive.GetEntry(AssetPackageImporter.ManifestName)!.Delete();
                using var writer = new StreamWriter(archive.CreateEntry(AssetPackageImporter.ManifestName).Open());
                writer.Write(JsonSerializer.Serialize(manifest).Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2,\"SchemaVersion\":1", StringComparison.Ordinal));
            }
            Refused(() => f.Importer.Import(zip)); f.NoPending();
        });
        check("Asset: identità, fonte HTTPS e schema sono convalidati", () =>
        {
            using var f = new Fixture(root); var m = f.Manifest();
            foreach (var invalid in new[] { m with { SchemaVersion = 2 }, m with { Id = "../bad" }, m with { Id = "BAD" }, m with { Name = "" }, m with { Author = new string('a', 101) }, m with { SourceUrl = "http://example.org/pack" }, m with { SourceUrl = "https://user:secret@example.org/pack" }, m with { Kind = "DLL" }, m with { Files = [] } })
                Refused(() => f.Importer.Import(f.Pack(invalid)));
            f.NoPending();
        });
        check("Asset: corruzione cache non viene sovrascritta o riparata", () =>
        {
            using var f = new Fixture(root); var zip = f.Pack(); var result = f.Importer.Import(zip);
            var payload = Path.Combine(result.Folder, "files", f.TexturePath); File.AppendAllText(payload, "corrotto"); var bad = File.ReadAllBytes(payload);
            Refused(() => f.Importer.Import(zip));
            Require(File.ReadAllBytes(payload).SequenceEqual(bad), "Cache corrotta sovrascritta."); f.NoPending();
        });
        check("Asset: record preparati costruiti dal chiamante sono riconvalidati", () =>
        {
            using var f = new Fixture(root); var prepared = f.Importer.Import(f.Pack());
            AssetPackageImporter.VerifyPrepared(prepared);
            Refused(() => AssetPackageImporter.VerifyPrepared(prepared with { ArchiveSha256 = "invalid" }));
            Refused(() => AssetPackageImporter.VerifyPrepared(prepared with { ArchiveSha256 = new string('0', 64) }));
            Refused(() => AssetPackageImporter.VerifyPrepared(prepared with { Manifest = prepared.Manifest with { Files = prepared.Manifest.Files.Append(new("speed2.exe", Hash([1]), 1)).ToArray() } }));
            // Even mutually consistent forged hashes and a rewritten cache manifest cannot authorize executable data.
            var malicious = new byte[] { (byte)'M', (byte)'Z', 0, 0, 0, 0, 0, 0 };
            File.WriteAllBytes(Path.Combine(prepared.Folder, "files", f.TexturePath), malicious);
            var files = prepared.Manifest.Files.Select(p => p.Path == f.TexturePath ? p with { Sha256 = Hash(malicious), Bytes = malicious.Length } : p).ToArray();
            var forged = prepared with { Manifest = prepared.Manifest with { Files = files } };
            File.WriteAllBytes(Path.Combine(prepared.Folder, AssetPackageImporter.ManifestName), JsonSerializer.SerializeToUtf8Bytes(forged.Manifest));
            Refused(() => AssetPackageImporter.VerifyPrepared(forged));
        });
        check("Asset: cache con manifest alterato, file aggiunti o mancanti è rifiutata", () =>
        {
            foreach (var mutation in new Action<Fixture, PreparedAssetPack>[]
            {
                (_, p) => File.AppendAllText(Path.Combine(p.Folder, AssetPackageImporter.ManifestName), "invalid"),
                (_, p) => File.WriteAllText(Path.Combine(p.Folder, "extra.txt"), "extra"),
                (f, p) => File.Delete(Path.Combine(p.Folder, "files", f.TexturePath)),
                (_, p) => Directory.CreateDirectory(Path.Combine(p.Folder, "unknown"))
            })
            { using var f = new Fixture(root); var zip = f.Pack(); var prepared = f.Importer.Import(zip); mutation(f, prepared); Refused(() => f.Importer.Import(zip)); }
        });
        check("Asset: collegamenti cache e archivio non vengono seguiti", () =>
        {
            using var f = new Fixture(root); var zip = f.Pack(); var prepared = f.Importer.Import(zip);
            var outside = Path.Combine(f.Root, "outside.bin"); File.WriteAllText(outside, "preserva");
            var payload = Path.Combine(prepared.Folder, "files", f.TexturePath); File.Delete(payload);
            try { File.CreateSymbolicLink(payload, outside); }
            catch (Exception e) when (OperatingSystem.IsWindows() && e is IOException or UnauthorizedAccessException)
            { throw new SkipCheckException("Creazione symlink non disponibile per questo utente Windows."); }
            Refused(() => f.Importer.Import(zip));
            var link = Path.Combine(f.Root, "linked.zip"); File.CreateSymbolicLink(link, zip); Refused(() => f.Importer.Import(link));
            Require(File.ReadAllText(outside) == "preserva", "Target link modificato.");
        });
        check("Asset: annullamento pulisce solo lo staging dell'operazione", () =>
        {
            using var f = new Fixture(root); Directory.CreateDirectory(f.Cache);
            var foreign = Path.Combine(f.Cache, ".pending-other-operation"); Directory.CreateDirectory(foreign); File.WriteAllText(Path.Combine(foreign, "preserve"), "x");
            using var cancellation = new CancellationTokenSource();
            Cancelled(() => f.Importer.Import(f.Pack(), cancellation.Token, new InlineProgress(_ => cancellation.Cancel())));
            Require(Directory.GetDirectories(f.Cache).SequenceEqual([foreign]), "Cache o staging altrui eliminato.");
            Cancelled(() => f.Importer.Import(f.Pack(), cancellation.Token));
        });
        check("Asset: modifica o sostituzione dello ZIP durante import impedisce il commit", () =>
        {
            using var f = new Fixture(root); var zip = f.Pack();
            Refused(() => f.Importer.Import(zip, progress: new InlineProgress(_ =>
            {
                // Windows prevents this write through FileShare.Read; Unix must detect it before committing.
                using var output = new FileStream(zip, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                output.Position = 0; output.WriteByte(0);
            })));
            f.NoPending(); Require(!Directory.Exists(f.Cache) || Directory.GetDirectories(f.Cache).Length == 0, "Archivio mutato pubblicato.");
        });
        check("Asset: contenuti BIN eseguibili o troncati sono rifiutati", () =>
        {
            foreach (var data in new byte[][] { [(byte)'M', (byte)'Z', 0, 0, 0, 0, 0, 0], [1, 2, 3] })
            { using var f = new Fixture(root); f.Payloads[f.TexturePath] = data; Refused(() => f.Importer.Import(f.Pack())); f.NoPending(); }
        });
        check("Asset: chunk texture rispettano confini, firma e padding", () =>
        {
            foreach (var data in new byte[][]
            {
                [0, 0, 0x30, 0xb3, 0xff, 0xff, 0xff, 0x7f], // declared outside file
                [0, 0, 0x30, 0xb3, 0xff, 0xff, 0xff, 0xff], // negative size
                [0, 0, 0x30, 0xb3, 0, 0, 0, 0], // empty texture pack
                [0, 0, 0x31, 0xb3, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // wrong top-level chunk
                [0, 0, 0, 0, 1, 0, 0, 0, 1] // hidden padding content
            })
            { using var f = new Fixture(root); f.Payloads[f.TexturePath] = data; Refused(() => f.Importer.Import(f.Pack())); f.NoPending(); }
            using var valid = new Fixture(root); var first = valid.Payloads[valid.TexturePath];
            valid.Payloads[valid.TexturePath] = first.Concat(new byte[] { 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0 }).Concat(first).ToArray();
            valid.Importer.Import(valid.Pack());
            valid.Payloads[valid.TexturePath] = first.Concat(new byte[] { 1 }).ToArray();
            Refused(() => valid.Importer.Import(valid.Pack())); valid.NoPending();
        });
        check("Asset: limiti conteggio, totale dichiarato e ZIP sono applicati", () =>
        {
            using var f = new Fixture(root); var m = f.Manifest();
            var tooMany = Enumerable.Range(0, AssetPackageImporter.MaximumFiles + 1).Select(i => new AssetPackFile($"GamePanelAssets/sample-pack/{i}.txt", Hash([1]), 1)).ToArray();
            Refused(() => f.Importer.Import(f.Pack(m with { Files = tooMany })));
            var tooLarge = Enumerable.Range(0, 3).Select(i => new AssetPackFile($"GamePanelAssets/sample-pack/{i}.txt", Hash([1]), 400L * 1024 * 1024)).ToArray();
            Refused(() => f.Importer.Import(f.Pack(m with { Files = tooLarge })));
            var zip = f.Pack(); using (var stream = new FileStream(zip, FileMode.Open, FileAccess.Write)) stream.SetLength(AssetPackageImporter.MaximumArchiveBytes + 1);
            Refused(() => f.Importer.Import(zip)); f.NoPending();
        });
        check("Asset: metadati TexWizard sono limitati e privi di percorsi o duplicati", () =>
        {
            AssetPackageImporter.ValidateTextureMetadata(Encoding.UTF8.GetBytes("{\"textures\":[[\"ROAD_OLD\",\"ROAD_NEW\"]]}"));
            foreach (var invalidRoot in new[] { "[[\"old\",\"new\"]]", "{\"textures\":[[\"old\",\"new\"]],\"unused\":true}", "{\"textures\":[[\"old\",\"new\"]],\"textures\":[[\"old\",\"new\"]]}" })
                Refused(() => AssetPackageImporter.ValidateTextureMetadata(Encoding.UTF8.GetBytes(invalidRoot)));
            AssetPackageImporter.ValidateTextureMetadata(Encoding.UTF8.GetBytes("{\"textures\":[[\"a\",\"b\"],[\"A\",\"c\"]]}"));
            foreach (var metadata in new[] { "{}", "[]", "[[\"a\"]]", "[[\"a\",1]]", "[[\"../a\",\"b\"]]", "[[\"a\",\"C:/b\"]]", "[[\"a\",\"b\"],[\"a\",\"c\"]]", "[[\"a\",\"b\"],[\"0x40\",\"c\"]]", "[[\"0xINVALID\",\"b\"]]", "[[\"a\\u0000\",\"b\"]]", "[[\"à\",\"b\"]]" })
                Refused(() => AssetPackageImporter.ValidateTextureMetadata(Encoding.UTF8.GetBytes("{\"textures\":" + metadata + "}")));
            Refused(() => AssetPackageImporter.ValidateTextureMetadata(new byte[AssetPackageImporter.MaximumMetadataBytes + 1]));
            Refused(() => AssetPackageImporter.ValidateTextureMetadata(JsonSerializer.SerializeToUtf8Bytes(new { textures = Enumerable.Range(0, 10001).Select(i => new[] { "T" + i, "N" + i }) })));
            using var f = new Fixture(root); f.Payloads[f.MetadataPath] = Encoding.UTF8.GetBytes("{\"textures\":[[\"../bad\",\"new\"]]}");
            Refused(() => f.Importer.Import(f.Pack())); f.NoPending();
        });
        check("Asset: README e licenza sono dati confinati al namespace del pack", () =>
        {
            using var f = new Fixture(root); f.Payloads["GamePanelAssets/sample-pack/README.md"] = Encoding.UTF8.GetBytes("# Read me");
            f.Payloads["GamePanelAssets/sample-pack/LICENSE.txt"] = Encoding.UTF8.GetBytes("License fixture");
            Require(f.Importer.Import(f.Pack()).Manifest.Files.Count == 4, "Documenti consentiti rifiutati.");
        });
    }

    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refused(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException) { return; }
        throw new Exception("Pacchetto non valido accettato.");
    }
    private static void Cancelled(Action action)
    { try { action(); } catch (OperationCanceledException) { return; } throw new Exception("Cancellazione ignorata."); }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private sealed class InlineProgress(Action<BackupProgress> action) : IProgress<BackupProgress>
    { public void Report(BackupProgress value) => action(value); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Cache => Path.Combine(Root, "cache");
        public string TexturePath => "TexturePacks/GamePanel-sample-pack/textures.bin";
        public string MetadataPath => "TexturePacks/GamePanel-sample-pack/meta.json";
        public Dictionary<string, byte[]> Payloads { get; } = new(StringComparer.Ordinal);
        public AssetPackageImporter Importer => new(Cache);
        private string _kind = "Textures";
        public Fixture(string root)
        {
            Root = Path.Combine(root, "assets-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root);
            Payloads[MetadataPath] = Encoding.UTF8.GetBytes("{\"textures\":[[\"OLD_TEXTURE\",\"NEW_TEXTURE\"]]}");
            Payloads[TexturePath] = TextureContainer();
        }
        public void Models()
        {
            _kind = "Models"; Payloads.Clear();
            Payloads["CARS/COROLLA/GEOMETRY.BIN"] = [0, 0x40, 0x13, 0x80, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            Payloads["CARS/COROLLA/TEXTURES.BIN"] = TextureContainer();
        }
        public AssetPackManifest Manifest() => new(1, "sample-pack", "Sample pack", "Fixture author", "https://example.org/assets", _kind,
            Payloads.Select(p => new AssetPackFile(p.Key, Hash(p.Value), p.Value.LongLength)).ToArray());
        public string Pack(AssetPackManifest? manifest = null, (string Path, byte[] Data)[]? extras = null)
        {
            var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            Add(AssetPackageImporter.ManifestName, JsonSerializer.SerializeToUtf8Bytes(manifest ?? Manifest()));
            foreach (var p in Payloads) Add(p.Key, p.Value);
            foreach (var p in extras ?? []) Add(p.Path, p.Data);
            return path;
            void Add(string name, byte[] data)
            { using var stream = zip.CreateEntry(name).Open(); stream.Write(data); }
        }
        public void NoPending() => Require(!Directory.Exists(Cache) || !Directory.EnumerateDirectories(Cache, ".pending-*").Any(), "Staging incompleto conservato.");
        private static byte[] TextureContainer() => [0, 0, 0x30, 0xb3, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
