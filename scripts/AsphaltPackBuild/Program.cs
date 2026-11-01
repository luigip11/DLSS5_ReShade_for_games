// MIT, GamePanel contributors, 2026. Build with Nikki + CoreExtensions MIT source.
using Nikki.Support.Underground2.Class;
using Nikki.Utils;
using SharedTexture = Nikki.Support.Shared.Class.Texture;
using SharedTPK = Nikki.Support.Shared.Class.TPKBlock;

string root = args[0];
var names = new[] {"INTERSECT", "LANE", "PARKING"};
var tpk = new TPKBlock("GP_A8_ASPHALT", null) {CompressionType=SharedTPK.TPKCompressionType.RawDecompressed};
foreach (string suffix in names) {
    string name="GP_A8_ASPHALT_"+suffix;
    tpk.AddTexture(name,Path.Combine(root,"original-asphalt",name+".dds"));
    var texture=tpk.Textures.Last();
    texture.AlphaBlendType=SharedTexture.TextureAlphaBlendType.TEXBLEND_SRCCOPY;
    texture.AlphaUsageType=SharedTexture.TextureAlphaUsageType.TEXUSAGE_NONE;
    texture.TileableUV=SharedTexture.TextureTileableType.ALL_SIDES;
    texture.RenderingOrder=0;
    Console.WriteLine($"{name}: hash 0x{texture.BinKey:X8}, {texture.Width}x{texture.Height}, {texture.Mipmaps} mips, {texture.Compression}, data {texture.Data.Length}");
}
string destination=Path.Combine(root,"pack","TexturePacks","GamePanel-asphalt-hd","textures.bin");
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
using(var bw=new BinaryWriter(File.Create(destination))) tpk.Assemble(bw);
// Roundtrip both generated pack and upstream example with the source library.
foreach (string path in new[]{destination}) {
    using var reader=new BinaryReader(File.OpenRead(path));
    var decoded = new TPKBlock(reader,null);
    Console.WriteLine($"Roundtrip {Path.GetFileName(path)}: {decoded.CollectionName}, {decoded.TextureCount} textures, end {reader.BaseStream.Position}/{reader.BaseStream.Length}");
    if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new Exception("Trailing data");
    var expectedHashes=names.Select(s => ("GP_A8_ASPHALT_"+s).BinHash()).OrderBy(x=>x);
    if(decoded.TextureCount!=3 || !decoded.Textures.Select(t=>t.BinKey).OrderBy(x=>x).SequenceEqual(expectedHashes))throw new Exception("Roundtrip hash/count mismatch");
    foreach(var t in decoded.Textures) {
      Console.WriteLine($"Validated 0x{t.BinKey:X8}: {t.Width}x{t.Height}, {t.Mipmaps} mips, {t.Compression}, {t.Data.Length} bytes");
      if(path==destination && (t.Width!=2048||t.Height!=2048||t.Mipmaps!=9||t.Data.Length!=2796192||t.Compression.ToString()!="TEXCOMP_DXTC1"||!t.Data.SequenceEqual(tpk.Textures.Single(original=>original.BinKey==t.BinKey).Data)))throw new Exception("Roundtrip mismatch");
    }
}
