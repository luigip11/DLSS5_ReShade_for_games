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
    & dotnet run --project tests/GamePanel.Checks/GamePanel.Checks.csproj -c Release --no-restore -- @checkArguments
    if ($LASTEXITCODE -ne 0) { throw 'Controlli non superati. Pubblicazione interrotta.' }
    & dotnet restore src/GamePanel.Desktop/GamePanel.Desktop.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore Windows non riuscito.' }
    $portable = Join-Path $repository 'artifacts/GamePanel-win-x64'
    # Only remove the generated package directory, never user/game folders.
    if (Test-Path $portable) { Remove-Item -LiteralPath $portable -Recurse -Force }
    & dotnet publish src/GamePanel.Desktop/GamePanel.Desktop.csproj -c Release --no-restore -o $portable -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione Windows non riuscita.' }
    & dotnet restore src/GamePanel.Updater/GamePanel.Updater.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore updater non riuscito.' }
    $updater = Join-Path $repository 'artifacts/GamePanel-updater-build'
    if (Test-Path $updater) { Remove-Item -LiteralPath $updater -Recurse -Force }
    & dotnet publish src/GamePanel.Updater/GamePanel.Updater.csproj -c Release --no-restore -o $updater -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione updater non riuscita.' }
    # Both executables use the same pinned runtime; copy the helper's own files only.
    foreach ($name in @('GamePanel.Updater.exe', 'GamePanel.Updater.dll', 'GamePanel.Updater.deps.json', 'GamePanel.Updater.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $updater $name) -Destination (Join-Path $portable $name)
    }
    Copy-Item README.md (Join-Path $portable 'LEGGIMI.md')
    Copy-Item LICENSE (Join-Path $portable 'LICENSE-GamePanel.txt')
    Copy-Item docs/WINDOWS-CHECK.md (Join-Path $portable 'COLLAUDO-WINDOWS.md')
    Copy-Item docs/BACKUP.md (Join-Path $portable 'BACKUP.md')
    Copy-Item docs/RESHade-SDR.md (Join-Path $portable 'RESHade-SDR.md')
    Copy-Item docs/RESHade-CATALOG.md (Join-Path $portable 'RESHade-CATALOG.md')
    Copy-Item docs/ALPHA7.md (Join-Path $portable 'ALPHA7.md')
    Copy-Item docs/WIDESCREEN-CATALOG.md (Join-Path $portable 'WIDESCREEN-CATALOG.md')
    Copy-Item docs/licenses/ThirteenAG-MIT.txt (Join-Path $portable 'LICENSE-WidescreenFix.txt')
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
