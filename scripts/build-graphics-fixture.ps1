param([string]$OutputDirectory = './artifacts/graphics-fixture')
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'MSVC x86 tools not available for the graphics fixture.' }
$developer = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/GraphicsFixture.cpp'))
$command = '"{0}" -no_logo -arch=x86 -host_arch=x64 && cl /nologo /O2 /MT /W4 /WX /EHsc /DUNICODE /D_UNICODE "{1}" /link /OUT:"{2}" user32.lib d3d9.lib' -f $developer, $source, (Join-Path $output 'speed2.exe')
Push-Location $output
try { & cmd /d /s /c $command; if ($LASTEXITCODE -ne 0) { throw 'Graphics fixture compilation failed.' } }
finally { Pop-Location }
Write-Host 'PASS compiled synthetic Direct3D 9 x86 fixture; no real game is included.'
