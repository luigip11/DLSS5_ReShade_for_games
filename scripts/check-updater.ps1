param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PackageDirectory).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GamePanel-Update-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $fixture 'application'
$data = Join-Path $fixture 'data'
$work = Join-Path $data ('Updates/' + [Guid]::NewGuid().ToString('N'))
$ready = Join-Path $work 'ready'
$helper = $null
try {
    [IO.Directory]::CreateDirectory($app) | Out-Null
    [IO.Directory]::CreateDirectory($ready) | Out-Null
    Copy-Item "$source/*" $app -Recurse
    Copy-Item "$source/*" $ready -Recurse
    [IO.File]::WriteAllText((Join-Path $app 'LEGGIMI.md'), 'Previous-version test content.')
    [IO.File]::WriteAllText((Join-Path $app 'obsolete-test.txt'), 'Old owned file.')
    $manifest = Get-ChildItem $app -File -Recurse | Where-Object { $_.Name -ne 'FILES-SHA256SUMS.txt' } | Sort-Object FullName | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($app.Length + 1).Replace('\', '/')
    }
    [IO.File]::WriteAllLines((Join-Path $app 'FILES-SHA256SUMS.txt'), [string[]]$manifest, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $app 'personal.txt'), 'Preserve extra app file.')
    foreach ($relative in @('Library/library.json', 'Components/cache.bin', 'Backups/copy.bin', 'Settings/display.json')) {
        $path = Join-Path $data $relative
        [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
        [IO.File]::WriteAllText($path, 'User data sentinel.')
    }
    [xml]$props = Get-Content (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw
    $version = [string]$props.Project.PropertyGroup.Version
    $request = @{ SchemaVersion = 1; ApplicationDirectory = $app; ReadyDirectory = $ready; DataRoot = $data; ParentPid = [int]::MaxValue; ParentStartedUtcTicks = 0; TargetVersion = $version; Restart = $false }
    $requestPath = Join-Path $work 'request.json'
    [IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $helper = Start-Process -FilePath (Join-Path $ready 'GamePanel.Updater.exe') -ArgumentList @('--request', ('"' + $requestPath + '"')) -WorkingDirectory $ready -PassThru
    $retainedHandle = $helper.Handle
    if (-not $helper.WaitForExit(60000)) { throw 'Native updater timeout.' }
    if ($helper.ExitCode -ne 0) { throw "Native updater failed: $($helper.ExitCode)" }
    $journal = Get-Content (Join-Path $work 'install-journal.json') -Raw | ConvertFrom-Json
    if ($journal.Status -ne 'Complete') { throw 'Native journal is incomplete.' }
    foreach ($line in Get-Content (Join-Path $ready 'FILES-SHA256SUMS.txt')) {
        if (-not $line) { continue }
        $hash = $line.Substring(0, 64)
        $name = $line.Substring(66)
        if ((Get-FileHash -LiteralPath (Join-Path $app $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Installed hash mismatch: $name" }
    }
    if (Test-Path (Join-Path $app 'obsolete-test.txt')) { throw 'Owned obsolete file not removed.' }
    if ((Get-Content (Join-Path $app 'personal.txt') -Raw) -ne 'Preserve extra app file.') { throw 'Extra file lost.' }
    foreach ($relative in @('Library/library.json', 'Components/cache.bin', 'Backups/copy.bin', 'Settings/display.json')) {
        if ((Get-Content (Join-Path $data $relative) -Raw) -ne 'User data sentinel.') { throw "User data changed: $relative" }
    }
    Write-Host 'PASS native updater, full package hashes, obsolete-file removal and preservation of library/cache/backups/settings/extra files'
}
finally {
    if ($null -ne $helper) { if (-not $helper.HasExited) { Stop-Process -Id $helper.Id -Force }; $helper.Dispose() }
    if (Test-Path $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
