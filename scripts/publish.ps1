param([switch]$VerifyOfficialComponent)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Per compilare serve SDK .NET 10.0.403. Il pacchetto portabile compilato include già il runtime.'
    }
    & dotnet restore tests/GamePanel.Checks/GamePanel.Checks.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore controlli non riuscito.' }
    $checkArguments = @()
    if ($VerifyOfficialComponent) { $checkArguments += '--verify-official-reshade' }
    $asphalt = Join-Path $repository 'artifacts/asphalt-pack-build/GamePanel-AsphaltHD.zip'
    if (Test-Path -LiteralPath $asphalt) { $checkArguments += @('--asphalt-package', $asphalt) }
    & dotnet run --project tests/GamePanel.Checks/GamePanel.Checks.csproj -c Release --no-restore -- @checkArguments
    if ($LASTEXITCODE -ne 0) { throw 'Controlli non superati. Pubblicazione interrotta.' }
    & dotnet restore src/GamePanel.Desktop/GamePanel.Desktop.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore Windows non riuscito.' }
    $portable = Join-Path $repository 'artifacts/GamePanel-win-x64'
    # Only remove the generated package directory, never user/game folders.
    if (Test-Path $portable) { Remove-Item -LiteralPath $portable -Recurse -Force }
    & dotnet publish src/GamePanel.Desktop/GamePanel.Desktop.csproj -c Release --no-restore -o $portable -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione Windows non riuscita.' }
    # Preserve the small managed entry files required by alpha.7+ update clients.
    # Runtime/framework DLLs are bundled into the executable rather than spread through the root.
    $desktopBinaries = Join-Path $repository 'src/GamePanel.Desktop/bin/Release/net10.0-windows/win-x64'
    foreach ($name in @('GamePanel.dll', 'GamePanel.deps.json', 'GamePanel.runtimeconfig.json', 'GamePanel.Core.dll')) {
        Copy-Item -LiteralPath (Join-Path $desktopBinaries $name) -Destination (Join-Path $portable $name)
    }
    & dotnet restore src/GamePanel.Updater/GamePanel.Updater.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore updater non riuscito.' }
    $updater = Join-Path $repository 'artifacts/GamePanel-updater-build'
    if (Test-Path $updater) { Remove-Item -LiteralPath $updater -Recurse -Force }
    & dotnet publish src/GamePanel.Updater/GamePanel.Updater.csproj -c Release --no-restore -o $updater -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione updater non riuscita.' }
    Copy-Item -LiteralPath (Join-Path $updater 'GamePanel.Updater.exe') -Destination (Join-Path $portable 'GamePanel.Updater.exe')
    $updaterBinaries = Join-Path $repository 'src/GamePanel.Updater/bin/Release/net10.0-windows/win-x64'
    foreach ($name in @('GamePanel.Updater.dll', 'GamePanel.Updater.deps.json', 'GamePanel.Updater.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $updaterBinaries $name) -Destination (Join-Path $portable $name)
    }
    [xml]$props = Get-Content (Join-Path $repository 'Directory.Build.props') -Raw
    $quickStart = (Get-Content (Join-Path $repository 'docs/PORTABLE-START.txt') -Raw).Replace('@VERSION@', [string]$props.Project.PropertyGroup.Version)
    [IO.File]::WriteAllText((Join-Path $portable 'LEGGIMI.txt'), $quickStart, [Text.UTF8Encoding]::new($false))
    Copy-Item LICENSE (Join-Path $portable 'LICENSE-GamePanel.txt')
    Copy-Item docs/WINDOWS-CHECK.md (Join-Path $portable 'COLLAUDO-WINDOWS.md')
    Copy-Item docs/BACKUP.md (Join-Path $portable 'BACKUP.md')
    Copy-Item docs/RESHade-SDR.md (Join-Path $portable 'RESHade-SDR.md')
    Copy-Item docs/RESHade-CATALOG.md (Join-Path $portable 'RESHade-CATALOG.md')
    Copy-Item docs/ALPHA8.md (Join-Path $portable 'ALPHA8.md')
    Copy-Item docs/DIRECTX-TEXTURES.md (Join-Path $portable 'DIRECTX-TEXTURES.md')
    Copy-Item docs/AUTOMATIC-TEXTURES.md (Join-Path $portable 'AUTOMATIC-TEXTURES.md')
    Copy-Item docs/SMOL-MODELS.md (Join-Path $portable 'SMOL-MODELS.md')
    Copy-Item docs/REMIX-PROFILE.md (Join-Path $portable 'REMIX-PROFILE.md')
    Copy-Item docs/REMIX-0.4-INVENTORY.md (Join-Path $portable 'REMIX-0.4-INVENTORY.md')
    Copy-Item docs/GAME-PROFILES.md (Join-Path $portable 'GAME-PROFILES.md')
    Copy-Item docs/RESOLUTION-PROFILES.md (Join-Path $portable 'RESOLUTION-PROFILES.md')
    Copy-Item docs/UG1-REMIX.md (Join-Path $portable 'UG1-REMIX.md')
    Copy-Item docs/LIBRARY-COVERS.md (Join-Path $portable 'LIBRARY-COVERS.md')
    Copy-Item docs/REMIX-PACKAGES.md (Join-Path $portable 'REMIX-PACKAGES.md')
    Copy-Item docs/IMAGE-AND-HDR.md (Join-Path $portable 'IMAGE-AND-HDR.md')
    Copy-Item docs/REMIX-PERFORMANCE.md (Join-Path $portable 'REMIX-PERFORMANCE.md')
    Copy-Item docs/REMIX-TRIAL.md (Join-Path $portable 'REMIX-TRIAL.md')
    Copy-Item docs/REMIX-COMPONENT-REVIEW.md (Join-Path $portable 'REMIX-COMPONENT-REVIEW.md')
    Copy-Item docs/SHARPCOMPRESS-LICENSE.txt (Join-Path $portable 'LICENSE-SharpCompress.txt')
    Copy-Item docs/CAR-CITY-SOURCES.md (Join-Path $portable 'CAR-CITY-SOURCES.md')
    Copy-Item docs/third-party/TextureToolkit-MIT.txt (Join-Path $portable 'LICENSE-TextureToolkit.txt')
    $sample = Join-Path $portable 'Assets/DirectXAsphalt/Asphalt.dds'
    if (-not (Test-Path -LiteralPath $sample) -or (Get-Item $sample).Length -ne 2796344 -or (Get-FileHash $sample -Algorithm SHA256).Hash -ne '7adaf63cc2234e6dc29ab87faa01fbc273523580ab14ef30e52babfe1a04e98f') {
        throw 'Materiale DirectX mancante o diverso. Eseguire prima scripts/AsphaltPackBuild/build_directx_sample.py con NumPy fissato.'
    }
    Copy-Item docs/ASSET-PACKS.md (Join-Path $portable 'ASSET-PACKS.md')
    Copy-Item docs/TEXWIZARD-CATALOG.md (Join-Path $portable 'TEXWIZARD-CATALOG.md')
    Copy-Item docs/WIDESCREEN-CATALOG.md (Join-Path $portable 'WIDESCREEN-CATALOG.md')
    Copy-Item docs/licenses/ThirteenAG-MIT.txt (Join-Path $portable 'LICENSE-WidescreenFix.txt')
    $docs = Join-Path $portable 'Docs'
    $licenses = Join-Path $portable 'Licenses'
    [IO.Directory]::CreateDirectory($docs) | Out-Null
    [IO.Directory]::CreateDirectory($licenses) | Out-Null
    Get-ChildItem -LiteralPath $portable -File -Filter '*.md' | Move-Item -Destination $docs
    Get-ChildItem -LiteralPath $portable -File -Filter '*.txt' | Where-Object { $_.Name -ne 'LEGGIMI.txt' } | Move-Item -Destination $licenses
    Copy-Item (Join-Path $PSScriptRoot 'diagnose-ug1.ps1') (Join-Path $docs 'Diagnosi-UG1.ps1')
    & (Join-Path $PSScriptRoot 'check-package-layout.ps1') -PackageDirectory $portable
    $manifest = Get-ChildItem -LiteralPath $portable -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($portable.Length + 1).Replace('\', '/')
        '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $relative
    }
    [IO.File]::WriteAllLines((Join-Path $portable 'FILES-SHA256SUMS.txt'), [string[]]$manifest, [Text.UTF8Encoding]::new($false))
    $archive = Join-Path $repository 'artifacts/GamePanel-win-x64.zip'
    if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path "$portable/*" -DestinationPath $archive -CompressionLevel Optimal
    $archiveHash = '{0}  GamePanel-win-x64.zip' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $repository 'artifacts/SHA256SUMS.txt'), $archiveHash + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Host "Pacchetto: $archive"
}
finally { Pop-Location }
