# GamePanel Asphalt HD 2K builder

This builds a small experimental NFS Underground 2 texture pack containing **three original asphalt surfaces**, not a complete city remaster. It requires no game installation and contains no game artwork. No downloaded EXE, ASI, native library, or UI tool is executed.

Install CPython **3.12.14**, NumPy **2.3.5** (`python -m pip install -r requirements.txt`), and .NET SDK **10.0.403**. Run:

```text
python build_pack.py --output ./asset-build --dotnet dotnet
```

Output: `asset-build/GamePanel-AsphaltHD.zip`, `.zip.sha256`, `pack-verification.json`, and `source-pins.json`. The ZIP has the importer manifest at its root and exactly the five declared files. `--offline` uses verified cached source archives named `Darius-source.zip` and `CoreExtensions-source.zip` in the output directory. The output `pack` staging directory and the two exact pinned source directories are rebuilt from scratch.

The builder downloads only the two MIT source archives pinned by full commit and SHA-256 in `build_pack.py`. It compiles the TPK writer as .NET 10 source and invokes a small local C# program. The imported TPK and DDS code is unchanged. The unrelated `CoreExtensions/Native/ASMBuilder.cs` is excluded because its byte overload became ambiguous with System.Half in newer .NET. No native functions are used on this path. MIT third-party notices accompany the produced asset.

Artwork, mip generation, and BC1 encoding use only integer operations; there are no FFT, floating-point, image encoder, platform hash, or randomized GUID dependencies. Coordinate hashing uses fixed unsigned arithmetic and seeds 22011–22013. DDS records explicitly use little endian. The archive uses sorted names, ZIP_STORED (no compression library variation), a fixed 1980 timestamp, explicit file modes, UTF-8 text and LF newlines. Rebuilding should produce the same ZIP bytes on Windows and Linux. Two independent Linux source builds have been compared; Windows equivalence still requires CI validation.

The TPK is decoded again with Nikki, checking all three texture hashes, dimensions (2048x2048), compression (DXT1), nine mip levels (2048 to 8), data lengths, and complete container consumption. A production integration should compare the final ZIP hash against a reviewed expected value before publishing.

Targets are listed in [NFSRaider's Underground2 texture list](https://github.com/Felipe379/NFSRaider/blob/492bf005c624c537b4fd15ae61b76813b43ef949/NFSRaider/Keys/MainKeys/Tracks/Textures/Underground2.txt): `RDP_INTERSECTMIDTILE`, `RDP_LANEC_NL_AA_KT`, and `RDP_PARKING_NL_AA_KT`. New names are unique uppercase `GP_A8_ASPHALT_*` strings. Material settings follow TexWizard's general opaque surface guidance (SRCCOPY, NONE alpha usage, ALL_SIDES tiling, rendering order 0); original material flags and dimensions were not copied from a user's game. **Appearance, UV scale, affected locations, and compatibility remain unverified in a running game.**

The TexWizard runtime engine is downloaded separately. It is GPLv3 and is not included in the pack or this builder. Its entry-point test alone is insufficient to verify a modified EXE; GamePanel must validate the expected PE layout and original callsite targets before enabling it.
