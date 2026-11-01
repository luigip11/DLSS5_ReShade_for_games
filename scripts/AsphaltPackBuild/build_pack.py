"""MIT. Reproducible original 2K asphalt asset builder; no game install required.
Run: python build_pack.py --output /path/to/work --dotnet /path/to/dotnet
Only pinned MIT source archives are downloaded. No downloaded executable runs.
"""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,sys,urllib.request,zipfile
import numpy as np
from generate_asphalt import generate

PINNED=[
 ('nlgxzef/Darius','059016553a4d7ecf0b642e346c527cba18a5c326','29632d0b7be7f91ab0ea3b2b96e7bdccdbb6d990580b7458426b727d75607fff'),
 ('nlgxzef/CoreExtensions','4c9d4062b3c5b0ab7b5cddaf5f9b87d739f3c510','6986e381acfce12481fca2bda56f2fc5612101e13e6992603889a8a42baa293a')]

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True);parser.add_argument('--dotnet',default='dotnet');parser.add_argument('--offline',action='store_true')
    args=parser.parse_args();root=args.output.resolve();script=Path(__file__).resolve().parent
    if sys.version_info[:3] not in ((3,12,10),(3,12,14)):raise RuntimeError('Verified release builders require CPython 3.12.10 or 3.12.14')
    if np.__version__!='2.3.5':raise RuntimeError('Reproducible release builds require NumPy 2.3.5')
    dotnet=shutil.which(args.dotnet) or args.dotnet
    sdk=subprocess.check_output([dotnet,'--version'],text=True).strip()
    if sdk!='10.0.403':raise RuntimeError('Pinned .NET SDK 10.0.403 required')
    (root/'source').mkdir(parents=True,exist_ok=True)
    for repo,commit,expected in PINNED:
        name=repo.split('/')[1];archive=root/(name+'-source.zip')
        if not archive.exists():
            if args.offline:raise RuntimeError('Missing cached source '+str(archive))
            request=urllib.request.Request(f'https://codeload.github.com/{repo}/zip/{commit}',headers={'User-Agent':'GamePanel-asset-builder'})
            with urllib.request.urlopen(request,timeout=90) as response:
                maximum=32*1024*1024
                payload=response.read(maximum+1)
                if len(payload)>maximum:raise RuntimeError('Source archive exceeds 32 MiB: '+name)
                archive.write_bytes(payload)
        if hashlib.sha256(archive.read_bytes()).hexdigest()!=expected:raise RuntimeError('Source archive SHA256 mismatch: '+name)
        expected_source=root/'source'/(name+'-'+commit)
        if expected_source.exists():shutil.rmtree(expected_source)
        with zipfile.ZipFile(archive) as z:
            # ZIP members must remain under the expected source directory.
            for item in z.infolist():
                if not (root/'source'/item.filename).resolve().is_relative_to(root/'source'):raise RuntimeError('Unsafe source archive member')
            z.extractall(root/'source')
    for name,src in [('Core',root/'source'/('CoreExtensions-'+PINNED[1][1])/'CoreExtensions'),('Nikki',root/'source'/('Darius-'+PINNED[0][1])/'Nikki')]:
        target=root/'build'/name;target.mkdir(parents=True,exist_ok=True)
        reference='<ProjectReference Include="../Core/Core.csproj" />' if name=='Nikki' else ''
        # Native ASM builder is unrelated to TPK and has an obsolete byte overload
        # that became ambiguous when .NET added System.Half. No source files change.
        excluded=f'{src}/obj/**;{src}/bin/**'+(f';{src}/Native/ASMBuilder.cs' if name=='Core' else '')
        project=f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><EnableDefaultCompileItems>false</EnableDefaultCompileItems><EnableNETAnalyzers>false</EnableNETAnalyzers><NoWarn>1591;SYSLIB0051;SYSLIB0050;CS0672;CS0618</NoWarn></PropertyGroup><ItemGroup><Compile Include="{src}/**/*.cs" Exclude="{excluded}" />{reference}</ItemGroup></Project>'
        (target/(name+'.csproj')).write_text(project,encoding='utf-8')
    target=root/'build/Pack';target.mkdir(parents=True,exist_ok=True)
    (target/'Pack.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup><ItemGroup><ProjectReference Include="../Nikki/Nikki.csproj" /></ItemGroup></Project>',encoding='utf-8')
    shutil.copyfile(script/'Program.cs',target/'Program.cs')
    if (root/'pack').exists():shutil.rmtree(root/'pack')
    generate(root)
    subprocess.run([dotnet,'build',str(target/'Pack.csproj'),'-t:Rebuild','--nologo','-v:q','-m:1','-p:NuGetAudit=false','-p:ImportDirectoryBuildProps=false'],check=True)
    subprocess.run([dotnet,str(target/'bin/Debug/net10.0/Pack.dll'),str(root)],check=True)
    subprocess.run([sys.executable,str(script/'finalize_pack.py'),str(root)],check=True)
    pins={'python':sys.version.split()[0],'numpy':np.__version__,'dotnet':sdk,'sources':[{'repository':repo,'commit':commit,'sha256':sha} for repo,commit,sha in PINNED]}
    (root/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':main()
