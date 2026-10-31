param([Parameter(Mandatory = $true)][string]$ExecutablePath, [string]$PreviewDirectory = '', [string]$GraphicsFixturePath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if ($PreviewDirectory) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class GamePanelCapture {
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    public static void Save(IntPtr window, string path) {
        Rect rect;
        if (!GetWindowRect(window, out rect)) throw new InvalidOperationException("Window bounds unavailable.");
        SetForegroundWindow(window);
        using (var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top, PixelFormat.Format32bppArgb)) {
            using (var graphics = Graphics.FromImage(bitmap)) {
                var dc = graphics.GetHdc();
                try { if (!PrintWindow(window, dc, 2)) throw new InvalidOperationException("Window capture failed."); }
                finally { graphics.ReleaseHdc(dc); }
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
'@
}
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($executable) -ne 'GamePanel.exe') { throw 'Expected the newly built GamePanel.exe.' }
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('GamePanel-UI-' + [Guid]::NewGuid().ToString('N'))
$fixtureGame = [IO.Path]::GetFullPath((Join-Path $fixtureRoot 'Program Files (x86)/Test Game'))
$fixtureData = Join-Path $fixtureRoot 'data'
$previousDataRoot = [Environment]::GetEnvironmentVariable('GAMEPANEL_DATA_ROOT')
[Environment]::SetEnvironmentVariable('GAMEPANEL_DATA_ROOT', $fixtureData)
[IO.Directory]::CreateDirectory($fixtureGame) | Out-Null
$fixtureExe = [IO.Path]::GetFullPath((Join-Path $fixtureGame 'gamepanel-fixture.exe'))
# Copy the apphost only for static inspection. This copy is never executed.
Copy-Item -LiteralPath $executable -Destination $fixtureExe
$originalHash = (Get-FileHash -LiteralPath $fixtureExe -Algorithm SHA256).Hash
$sha = [Security.Cryptography.SHA256]::Create()
try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fixtureExe.ToUpperInvariant()))).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$fixtureStore = Join-Path $fixtureData "Backups/$key"
if (Test-Path -LiteralPath $fixtureStore) { throw 'Fixture backup path unexpectedly exists.' }
$process = $null
$renderProcess = $null
$profileBackupStore = $null
$profileStore = $null
function Wait-Until([scriptblock]$Condition, [string]$Description, [int]$TimeoutSeconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
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
    $signature = Get-AuthenticodeSignature -LiteralPath $executable
    Write-Host "Publisher signature status: $($signature.Status)"
    if ($PreviewDirectory) {
        Start-Sleep -Milliseconds 800
        [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '00-library.png')))
    }
    Invoke-Button 'OverviewPage'
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
    $picker = Find-Element 'BackupSnapshotPicker'
    $dropdown = $picker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $dropdown.Expand()
    Wait-Until { $dropdown.Current.ExpandCollapseState -eq [Windows.Automation.ExpandCollapseState]::Expanded } 'backup selector open'
    $dropdown.Collapse()
    Write-Host 'PASS GUI styled backup selector opens and closes'
    if ($PreviewDirectory) {
        Start-Sleep -Milliseconds 300
        [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '01-backup.png')))
    }
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
    if ($GraphicsFixturePath) {
        $fixtureBinary = (Resolve-Path -LiteralPath $GraphicsFixturePath).Path
        $profileExe = Join-Path $fixtureGame 'speed2.exe'
        Copy-Item -LiteralPath $fixtureBinary -Destination $profileExe
        $profileHash = (Get-FileHash -LiteralPath $profileExe -Algorithm SHA256).Hash
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $profileKey = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($profileExe.ToUpperInvariant()))).Replace('-', '').ToLowerInvariant()
            $folderKey = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fixtureGame.ToUpperInvariant()))).Replace('-', '').ToLowerInvariant()
        } finally { $sha.Dispose() }
        $profileBackupStore = Join-Path $fixtureData "Backups/$profileKey"
        $profileStore = Join-Path $fixtureData "Profiles/$folderKey"
        if ((Test-Path -LiteralPath $profileBackupStore) -or (Test-Path -LiteralPath $profileStore)) { throw 'Unexpected existing profile fixture state.' }
        $pathInput.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($profileExe)
        Invoke-Button 'AnalyzePath'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'x86 DX9 fixture analysis'
        Invoke-Button 'HdrPage'
        Invoke-Button 'PrepareProfile'
        Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'verified official download and preview' 120
        if (Test-Path -LiteralPath (Join-Path $fixtureGame 'd3d9.dll')) { throw 'Preview wrote a loader into the fixture game.' }
        if ($PreviewDirectory) {
            (Find-Element 'PageScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
            Start-Sleep -Milliseconds 300
            [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '02-effects.png')))
        }
        Invoke-Button 'ApplyProfile'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'profile application'
        $loader = Join-Path $fixtureGame 'd3d9.dll'
        if ((Get-FileHash -LiteralPath $loader -Algorithm SHA256).Hash -ne '538998f66c0197adcdeadfe8e5ea19dd6ca3253e40399476d291f385ce518b88') { throw 'Installed loader differs from the approved catalog.' }
        if ((Get-FileHash -LiteralPath $profileExe -Algorithm SHA256).Hash -ne $profileHash) { throw 'Profile changed the fixture executable.' }
        Write-Host 'PASS GUI official download, no-write preview, baseline backup and tracked profile application'
        (Find-Element 'Sharpness').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(60)
        Invoke-Button 'PrepareProfile'
        Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'parameter update preview'
        Invoke-Button 'ApplyProfile'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'parameter application'
        $preset = Join-Path $fixtureGame 'GamePanelPreset.ini'
        if ((Get-Content -LiteralPath $preset -Raw) -notmatch 'Sharpness=0\.6000') { throw 'Slider is not connected to the saved preset.' }
        Invoke-Button 'OriginalProfile'
        Invoke-Button 'PrepareProfile'
        Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'original profile preview'
        Invoke-Button 'ApplyProfile'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'original profile application'
        if ((Get-Content -LiteralPath $preset -Raw) -notmatch '^Techniques=\r?\n') { throw 'Original profile did not disable the technique.' }
        Invoke-Button 'BalancedProfile'
        Invoke-Button 'PrepareProfile'
        Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'balanced profile preview'
        Invoke-Button 'ApplyProfile'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'balanced profile application'
        Write-Host 'PASS GUI parameter slider, Original technique disabled, Balanced profile restored'
        Invoke-Button 'CinemaProfile'
        Invoke-Button 'PrepareProfile'
        Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'cinema profile preview'
        Invoke-Button 'ApplyProfile'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'cinema profile application'
        if ((Get-Content -LiteralPath $preset -Raw) -notmatch 'Contrast=0\.3500') { throw 'Cinema profile is not connected to the preset.' }
        if ($PreviewDirectory) {
            (Find-Element 'PageScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
            Start-Sleep -Milliseconds 300
            [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '03-cinema.png')))
        }

        # This is our compiled synthetic renderer. No user-selected game/crack is executed.
        $renderProcess = Start-Process -FilePath $profileExe -WorkingDirectory $fixtureGame -PassThru
        if (-not $renderProcess.WaitForExit(45000)) { throw 'Synthetic renderer did not exit.' }
        if ($renderProcess.ExitCode -eq 77) { Write-Host 'UNVERIFIED DX9 rendering: CI adapter unavailable; real-game rendering still needs testing.' }
        elseif ($renderProcess.ExitCode -ne 0) { throw "Synthetic renderer failed: $($renderProcess.ExitCode)" }
        else {
            $runtimeLog = Join-Path $fixtureGame 'ReShade.log'
            if (-not (Test-Path -LiteralPath $runtimeLog)) { throw 'ReShade runtime did not produce a log.' }
            $runtimeText = Get-Content -LiteralPath $runtimeLog -Raw
            if ($runtimeText -notmatch "Successfully compiled '[^']*GamePanelSDR\.fx'" -or $runtimeText -match "Failed to compile '[^']*GamePanelSDR\.fx'") {
                Write-Host $runtimeText
                throw 'Our SDR shader did not compile successfully in the actual x86 DX9 runtime.'
            }
            Write-Host 'PASS synthetic x86 DX9 renderer: official ReShade loaded and GamePanelSDR.fx compiled'
        }
        $renderAvailable = $renderProcess.ExitCode -eq 0
        $cinemaPixel = $null
        if ($renderAvailable) {
            $pixelPath = Join-Path $fixtureGame 'fixture-pixel.txt'
            if (-not (Test-Path -LiteralPath $pixelPath)) { throw 'Synthetic renderer did not record a rendered screen pixel.' }
            $cinemaPixel = @((Get-Content -LiteralPath $pixelPath -Raw).Trim().Split(' ') | ForEach-Object { [int]$_ })
        }
        $renderProcess.Dispose(); $renderProcess = $null
        # Runtime may rewrite its INI/preset. Re-analyze and verify that modified
        # configs are preserved during removal, never forcibly overwritten.
        Invoke-Button 'AnalyzePath'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'post-render analysis'
        Invoke-Button 'HdrPage'
        (Find-Element 'Sharpness').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(55)
        Invoke-Button 'PrepareProfile'
        Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'post-runtime config compatibility' 60
        Invoke-Button 'ApplyProfile'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'post-runtime parameter update'
        if ((Get-Content -LiteralPath $preset -Raw) -notmatch 'Sharpness=0\.5500') { throw 'Runtime-rewritten config prevented a valid parameter update.' }
        Write-Host 'PASS GUI settings update after ReShade runtime configuration rewrite'
        if ($renderAvailable) {
            Invoke-Button 'OriginalProfile'
            Invoke-Button 'PrepareProfile'
            Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'original image comparison preview'
            Invoke-Button 'ApplyProfile'
            Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'original image comparison application'
            Remove-Item -LiteralPath $pixelPath
            $renderProcess = Start-Process -FilePath $profileExe -WorkingDirectory $fixtureGame -PassThru
            if (-not $renderProcess.WaitForExit(45000) -or $renderProcess.ExitCode -ne 0) { throw 'Original image comparison failed.' }
            $renderProcess.Dispose(); $renderProcess = $null
            $originalPixel = @((Get-Content -LiteralPath $pixelPath -Raw).Trim().Split(' ') | ForEach-Object { [int]$_ })
            $difference = 0
            for ($channel = 0; $channel -lt 3; $channel++) { $difference += [Math]::Abs($cinemaPixel[$channel] - $originalPixel[$channel]) }
            if ($difference -lt 3) { throw "Cinema did not produce a different rendered pixel: $cinemaPixel vs $originalPixel" }
            Write-Host "PASS rendered pixel comparison: Cinema RGB=$cinemaPixel; Original RGB=$originalPixel; difference=$difference"
        }

        Invoke-Button 'PrepareRemoveProfile'
        Wait-Until { $null -ne (Find-Element 'ApplyProfile') -and (Find-Element 'ApplyProfile').Current.IsEnabled } 'profile removal preview'
        Invoke-Button 'ApplyProfile'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'profile removal'
        if (Test-Path -LiteralPath $loader) { throw 'Managed loader remains after removal.' }
        if ((Get-FileHash -LiteralPath $profileExe -Algorithm SHA256).Hash -ne $profileHash) { throw 'Removal changed the fixture executable.' }
        Write-Host 'PASS GUI tracked removal: loader removed and fixture EXE unchanged'
        Invoke-Button 'ScreenPage'
        Invoke-Button 'PrepareScreen'
        Wait-Until { $null -ne (Find-Element 'ApplyScreen') -and (Find-Element 'ApplyScreen').Current.IsEnabled } 'widescreen download and preview' 120
        if (Test-Path -LiteralPath (Join-Path $fixtureGame 'dinput8.dll')) { throw 'Widescreen preview wrote game files.' }
        Invoke-Button 'ApplyScreen'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'widescreen static install'
        if ((Get-FileHash -LiteralPath (Join-Path $fixtureGame 'dinput8.dll') -Algorithm SHA256).Hash -ne 'd5a059aa467a7a7127c8f6169f79fa63ff0f55986ee9eb2fd9a281bebf2aa2e6') { throw 'Widescreen loader hash mismatch.' }
        if ($PreviewDirectory) {
            (Find-Element 'PageScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
            Start-Sleep -Milliseconds 300
            [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '04-ultrawide.png')))
        }
        # Never execute the game-specific ASI against this synthetic renderer.
        Invoke-Button 'PrepareScreen'
        Wait-Until { $null -ne (Find-Element 'ApplyScreen') -and (Find-Element 'ApplyScreen').Current.IsEnabled } 'widescreen removal preview'
        Invoke-Button 'ApplyScreen'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'widescreen static removal'
        if (Test-Path -LiteralPath (Join-Path $fixtureGame 'dinput8.dll')) { throw 'Widescreen loader remains after removal.' }
        Write-Host 'PASS GUI verified widescreen download, static install and removal; game-specific ASI NOT executed'
        $libraryPath = Join-Path $fixtureData 'Library/library.json'
        $library = Get-Content -LiteralPath $libraryPath -Raw | ConvertFrom-Json
        $libraryGame = @($library.Games | Where-Object { $_.ExecutablePath -eq $profileExe })
        if ($libraryGame.Count -ne 1 -or @($library.Games).Count -ne 2) { throw 'Library entries were not saved or deduplicated.' }
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
        $process = Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -PassThru
        Wait-Until { $process.MainWindowHandle -ne [IntPtr]::Zero } 'library restart'
        $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        Wait-Until { $null -ne (Find-Element ('LibraryOpen-' + $libraryGame[0].Id)) } 'persisted library card'
        if ($PreviewDirectory) {
            Start-Sleep -Milliseconds 300
            [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '05-library-populated.png')))
        }
        Invoke-Button ('LibraryOpen-' + $libraryGame[0].Id)
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'open stored game without entering path'
        if ((Find-Element 'ExecutablePath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $profileExe) { throw 'Library opened another executable.' }
        Write-Host 'PASS GUI library persistence after restart and game selection without re-entering its path'
        Invoke-Button 'LibraryPage'
        Invoke-Button ('LibraryPlay-' + $libraryGame[0].Id)
        Wait-Until {
            $saved = Get-Content -LiteralPath $libraryPath -Raw | ConvertFrom-Json
            return $null -ne @($saved.Games | Where-Object { $_.Id -eq $libraryGame[0].Id })[0].LastPlayedAt
        } 'library launch and last-played timestamp'
        $launched = @([Diagnostics.Process]::GetProcessesByName('speed2') | Where-Object { $_.MainModule.FileName -eq $profileExe })
        if ($launched.Count -ne 1) { throw 'Library did not launch exactly our selected synthetic renderer.' }
        $renderProcess = $launched[0]
        if (-not $renderProcess.WaitForExit(45000) -or $renderProcess.ExitCode -ne 0) { throw 'Library-launched renderer failed.' }
        $renderProcess.Dispose(); $renderProcess = $null
        Write-Host 'PASS GUI launch directly from saved library card and persisted last-played timestamp'

    }
    Write-Host 'This checks the CI desktop; it does not certify SAC, antivirus safety or game compatibility.'
}
finally {
    [Environment]::SetEnvironmentVariable('GAMEPANEL_DATA_ROOT', $previousDataRoot)
    if ($null -ne $renderProcess) { if (-not $renderProcess.HasExited) { Stop-Process -Id $renderProcess.Id -Force }; $renderProcess.Dispose() }
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
