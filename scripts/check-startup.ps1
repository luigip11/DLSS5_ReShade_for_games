param([Parameter(Mandatory = $true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($executable) -ne 'GamePanel.exe') { throw 'Expected the newly built GamePanel.exe.' }
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('GamePanel-UI-' + [Guid]::NewGuid().ToString('N'))
$fixtureGame = Join-Path $fixtureRoot 'Program Files (x86)/Test Game'
[IO.Directory]::CreateDirectory($fixtureGame) | Out-Null
$fixtureExe = [IO.Path]::GetFullPath((Join-Path $fixtureGame 'gamepanel-fixture.exe'))
# Copy the apphost only for static inspection. This copy is never executed.
Copy-Item -LiteralPath $executable -Destination $fixtureExe
$originalHash = (Get-FileHash -LiteralPath $fixtureExe -Algorithm SHA256).Hash
$sha = [Security.Cryptography.SHA256]::Create()
try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fixtureExe.ToUpperInvariant()))).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$fixtureStore = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "GamePanel/Backups/$key"
if (Test-Path -LiteralPath $fixtureStore) { throw 'Fixture backup path unexpectedly exists.' }
$process = $null
function Wait-Until([scriptblock]$Condition, [string]$Description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "GamePanel exited during $Description. Exit code: $($process.ExitCode)" }
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 200
    }
    throw "Timeout: $Description"
}
function Find-Element([string]$Id) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    return $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Button([string]$Id) {
    $button = Find-Element $Id
    if ($null -eq $button -or -not $button.Current.IsEnabled) { throw "Button unavailable: $Id" }
    $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Has-Heading([string]$Name) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
    return $null -ne $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
try {
    $process = Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -PassThru
    Wait-Until { $process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -like 'GamePanel*' } 'main window'
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Write-Host "PASS GUI startup: $($process.MainWindowTitle)"
    $pathInput = Find-Element 'ExecutablePath'
    if ($null -eq $pathInput) { throw 'Editable executable path field not found.' }
    $pathInput.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('"' + $fixtureExe + '"')
    Wait-Until { (Find-Element 'AnalyzePath').Current.IsEnabled } 'analyze button enabled'
    Invoke-Button 'AnalyzePath'
    Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'static executable analysis'
    Write-Host 'PASS GUI quoted path analysis'
    Invoke-Button 'BackupsPage'
    Wait-Until { $null -ne (Find-Element 'CreateBackup') } 'backup page'
    Invoke-Button 'CreateBackup'
    Wait-Until { $null -ne (Find-Element 'BackupSnapshotPicker') } 'backup creation'
    $snapshot = @(Get-ChildItem -LiteralPath $fixtureStore -Directory | Where-Object { $_.Name -match '^[a-fA-F0-9]{32}$' })
    if ($snapshot.Count -ne 1) { throw 'Expected one committed snapshot.' }
    Write-Host 'PASS GUI persistent backup creation'
    Invoke-Button 'VerifyBackup'
    Wait-Until { Has-Heading 'Verifica completata' } 'baseline verification'
    # Delete only the unique test apphost; the selected user game is never involved.
    [IO.File]::Delete($fixtureExe)
    Invoke-Button 'VerifyBackup'
    Wait-Until { $null -ne (Find-Element 'RestoreMissing') } 'missing file preview'
    Invoke-Button 'RestoreMissing'
    Wait-Until {
        if (-not (Has-Heading 'Verifica completata')) { return $false }
        $journal = Join-Path $snapshot[0].FullName 'repair.json'
        if (-not (Test-Path -LiteralPath $journal) -or -not (Test-Path -LiteralPath $fixtureExe)) { return $false }
        $state = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
        return $state.State -eq 'Complete'
    } 'restore through UI'
    if ((Get-FileHash -LiteralPath $fixtureExe -Algorithm SHA256).Hash -ne $originalHash) { throw 'Restored file differs from the original fixture.' }
    Write-Host 'PASS GUI backup/restore round trip: original SHA-256 recovered'
    Write-Host 'This checks the CI desktop; it does not certify SAC, antivirus safety or game compatibility.'
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    if (Test-Path -LiteralPath $fixtureStore) { Remove-Item -LiteralPath $fixtureStore -Recurse -Force }
}
