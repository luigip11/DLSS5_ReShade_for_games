param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageDirectory).Path
$allowed = @('GamePanel.exe', 'GamePanel.dll', 'GamePanel.deps.json', 'GamePanel.runtimeconfig.json', 'GamePanel.Core.dll', 'GamePanel.Updater.exe', 'GamePanel.Updater.dll', 'GamePanel.Updater.deps.json', 'GamePanel.Updater.runtimeconfig.json', 'LEGGIMI.txt')
foreach ($name in $allowed) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) { throw "Portable entry file missing: $name" }
}
$unexpected = @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Name -notin $allowed })
if ($unexpected.Count -gt 0) { throw ('Unexpected loose root files: ' + ($unexpected.Name -join ', ')) }
foreach ($name in @('Docs/RESOLUTION-PROFILES.md', 'Licenses/LICENSE-GamePanel.txt', 'Assets/DirectXAsphalt/Asphalt.dds')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) { throw "Portable content missing: $name" }
}
if ((Get-Item (Join-Path $root 'GamePanel.exe')).Length -lt 10MB -or (Get-Item (Join-Path $root 'GamePanel.Updater.exe')).Length -lt 10MB) { throw 'Expected bundled self-contained executables.' }
Add-Type -AssemblyName System.Drawing
foreach ($name in @('GamePanel.exe', 'GamePanel.Updater.exe')) {
    $icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $root $name))
    if ($null -eq $icon) { throw "Embedded application icon missing: $name" }
    $bitmap = $icon.ToBitmap()
    try {
        $lime = 0; $blue = 0
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                if ($pixel.G -gt 220 -and $pixel.R -gt 140 -and $pixel.B -lt 160) { $lime++ }
                if ($pixel.B -gt 210 -and $pixel.G -gt 120 -and $pixel.R -lt 170) { $blue++ }
            }
        }
        if ($lime -lt 2 -or $blue -lt 2) { throw "Embedded icon does not match GamePanel colors: $name" }
    } finally { $bitmap.Dispose(); $icon.Dispose() }
}
Write-Host 'PASS portable layout: bundled runtimes, organized Docs/Licenses/Assets, legacy updater entry files and embedded brand icons'

$syntaxTokens = $null; $syntaxErrors = $null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Docs/Diagnosi-UG1.ps1'), [ref]$syntaxTokens, [ref]$syntaxErrors) | Out-Null
if ($syntaxErrors.Count -gt 0) { throw ('UG1 diagnostic script syntax: ' + ($syntaxErrors.Message -join '; ')) }
