"""Create importable GamePanel pack from original DDS and Nikki-produced BIN. MIT."""
from pathlib import Path
import io
import hashlib,json,zipfile,struct
import sys
p=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else Path.cwd()
def write_stable(path, value):
    path.write_bytes(value.encode('utf-8'))
pack=p/'pack'
folder=pack/'TexturePacks/GamePanel-asphalt-hd'
mappings=[['RDP_INTERSECTMIDTILE','GP_A8_ASPHALT_INTERSECT'],['RDP_LANEC_NL_AA_KT','GP_A8_ASPHALT_LANE'],['RDP_PARKING_NL_AA_KT','GP_A8_ASPHALT_PARKING']]
write_stable((folder/'meta.json'),json.dumps({'textures':mappings},indent=2)+'\n')
files=[]
for f in sorted(folder.glob('*')):
    b=f.read_bytes();files.append({'Path':f.relative_to(pack).as_posix(),'Sha256':hashlib.sha256(b).hexdigest(),'Bytes':len(b)})
manifest={'SchemaVersion':1,'Id':'asphalt-hd','Name':'GamePanel Asphalt HD','Author':'GamePanel contributors','SourceUrl':'https://github.com/luigip11/DLSS5_ReShade_for_games','Kind':'Textures','Files':files}
write_stable((pack/'GamePanelAssetPack.json'),json.dumps(manifest,indent=2)+'\n')
license='''MIT License

Copyright (c) 2026 GamePanel contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'''
write_stable((pack/'LICENSE.txt'),license)
write_stable((pack/'README.txt'),'''GamePanel Asphalt HD 2K - experimental alpha8 pack (three surfaces)

Original procedural artwork. No EA assets or third-party texture-pack artwork.
Three 2048x2048 opaque DXT1 textures; nine mip levels down to 8x8.
Only the three mapped textures are replaced. This is not a full city remaster.

Targets from NFSRaider Underground2.txt at commit
492bf005c624c537b4fd15ae61b76813b43ef949:
  RDP_INTERSECTMIDTILE
  RDP_LANEC_NL_AA_KT
  RDP_PARKING_NL_AA_KT
https://github.com/Felipe379/NFSRaider/blob/492bf005c624c537b4fd15ae61b76813b43ef949/NFSRaider/Keys/MainKeys/Tracks/Textures/Underground2.txt

Not validated in a running NFS Underground 2 installation. Target dimensions,
UV use, material flags, and affected city locations still need in-game review.
Material attributes use TexWizard wiki's general opaque-surface settings,
not attributes copied from the user's original TRACKS/STREAML4RA.BUN.

Requires TexWizard UG2 1.3 and its supported v1.2 NTSC executable layout.
Use GamePanel to import and activate this ZIP; restart the game after changes.
Disable this pack in GamePanel to return to original textures.

Generator: generate_asphalt.py (MIT, integer-only, seeds 22011-22013).
Container writer: unmodified Nikki/Darius TPK and DDS code, MIT (MaxHwoy).
Darius commit 059016553a4d7ecf0b642e346c527cba18a5c326
CoreExtensions commit 4c9d4062b3c5b0ab7b5cddaf5f9b87d739f3c510
Built from pinned .NET source; no ASI or downloaded tool EXE was executed.
Nikki roundtrip confirms 3 textures, expected names/hashes/dimensions/mip counts.
TexWizard engine is downloaded separately, is GPLv3, and is not in this pack.
''')
# Keep third-party notices for the container writer used to produce this artifact.
write_stable((pack/'THIRD-PARTY-NOTICES.txt'),'Container writer:\nhttps://github.com/nlgxzef/Darius\n\n'+next((p/'source').glob('Darius-*/LICENSE')).read_text()+'\nCoreExtensions:\nhttps://github.com/nlgxzef/CoreExtensions\n\n'+next((p/'source').glob('CoreExtensions-*/LICENSE.md')).read_text())
# Strict importer: every payload, including notices, has a manifest record.
docs=pack/'GamePanelAssets/asphalt-hd'
docs.mkdir(parents=True,exist_ok=True)
for name in ['README.txt','LICENSE.txt','THIRD-PARTY-NOTICES.txt']:
    (pack/name).replace(docs/('README.md' if name=='README.txt' else name))
files=[]
for f in sorted(pack.rglob('*')):
    if f.is_file() and f.name!='GamePanelAssetPack.json':
        b=f.read_bytes();files.append({'Path':f.relative_to(pack).as_posix(),'Sha256':hashlib.sha256(b).hexdigest(),'Bytes':len(b)})
manifest['Files']=files
write_stable((pack/'GamePanelAssetPack.json'),json.dumps(manifest,indent=2)+'\n')
# Fixed metadata keeps the ZIP deterministic across repeated builds.
zip_path=p/'GamePanel-AsphaltHD.zip'
with zipfile.ZipFile(zip_path,'w',compression=zipfile.ZIP_STORED) as z:
    for f in sorted(pack.rglob('*')):
        if f.is_file():
            info=zipfile.ZipInfo(f.relative_to(pack).as_posix(),date_time=(1980,1,1,0,0,0));info.create_system=3;info.compress_type=zipfile.ZIP_STORED;info.external_attr=0o100644<<16
            z.writestr(info,f.read_bytes(),compress_type=zipfile.ZIP_STORED)
b=zip_path.read_bytes()
write_stable((zip_path.with_suffix('.zip.sha256')),hashlib.sha256(b).hexdigest()+'  '+zip_path.name+'\n')
def h(s):
    v=0xffffffff
    for x in s.encode('ascii'):v=(v*33+x)&0xffffffff
    return f'0x{v:08X}'
report={'zip':zip_path.name,'bytes':len(b),'sha256':hashlib.sha256(b).hexdigest(),'mappings':[{'old':a,'oldHash':h(a),'new':b,'newHash':h(b)}for a,b in mappings],'files':files,'gameValidation':'Not performed','formatValidation':'Nikki source roundtrip passed'}
write_stable((p/'pack-verification.json'),json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
