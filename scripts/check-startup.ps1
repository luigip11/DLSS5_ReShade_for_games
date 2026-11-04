param([Parameter(Mandatory = $true)][string]$ExecutablePath, [string]$PreviewDirectory = '', [string]$GraphicsFixturePath = '', [string]$AssetFixturePath = '', [string]$AssetPackagePath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
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
    $processCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $diagnostic = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $processCondition)
    Write-Host ('GUI diagnostic: ' + (@($diagnostic | ForEach-Object { $_.Current.ControlType.ProgrammaticName + ':' + $_.Current.Name }) -join ' | '))
    throw "Timeout: $Description"
}
function Find-Element([string]$Id) {
    # Expand only known shell containers requested by a test. Probing for an
    # absent operation (for example after removal) must not change page state.
    $containerId = $null
    if ($Id -in @('HdrPage', 'ScreenPage', 'BackupsPage', 'DiagnosticsPage')) { $containerId = 'AdvancedNavigation' }
    elseif ($Id -in @('ExecutablePath', 'AnalyzePath')) { $containerId = 'GamePathExpander' }
    if ($containerId) {
        $containerCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $containerId)
        $container = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $containerCondition)
        if ($null -ne $container) {
            $pattern = $container.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
            if ($pattern.Current.ExpandCollapseState -eq [Windows.Automation.ExpandCollapseState]::Collapsed) {
                $pattern.Expand()
                Start-Sleep -Milliseconds 100
            }
        }
    }
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    return $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Button([string]$Id) {
    $button = Find-Element $Id
    if ($null -eq $button -or -not $button.Current.IsEnabled) { throw "Button unavailable: $Id" }
    $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Select-AssetsTab([string]$Id) {
    $tab = Find-Element $Id
    if ($null -eq $tab) { throw "Asset tab unavailable: $Id" }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Set-AssetAdvancedTools([bool]$Expanded) {
    $expander = Find-Element 'AssetsAdvancedTools'
    if ($null -eq $expander) { throw 'Asset advanced tools unavailable.' }
    $pattern = $expander.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($Expanded) { $pattern.Expand() } else { $pattern.Collapse() }
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
    Invoke-Button 'CheckSoftwareUpdates'
    $updateCondition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, ('GamePanel ' + [char]0x00b7 + ' aggiornamenti')))
    Wait-Until { $null -ne [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $updateCondition) } 'GitHub release check dialog' 40
    $updateDialog = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $updateCondition)
    # Native static/button controls can be exposed as Panes in the CI session.
    function Read-UpdateDialog {
        $parts = $updateDialog.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
        return (@($parts | ForEach-Object { $_.Current.Name }) -join ' ')
    }
    Wait-Until { (Read-UpdateDialog).Length -gt 20 } 'update dialog content'
    $updateMessage = Read-UpdateDialog
    if ($updateMessage -notlike ('*Software gi' + [char]0x00e0 + ' aggiornato.*') -and $updateMessage -notlike '*Non ci sono ancora release aggiornabili pubblicate su GitHub.*') { throw "Unexpected release check result: $updateMessage" }
    Write-Host "PASS live GitHub release check through GUI: $updateMessage"
    # The headless runner exposes native dialog buttons as non-invokable Panes.
    # End only this isolated, idle check process; use a fresh app for mutation tests.
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
    $process.Dispose()
    $process = Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -PassThru
    Wait-Until { $process.MainWindowHandle -ne [IntPtr]::Zero } 'restart after read-only update check'
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Invoke-Button 'OverviewPage'
    $pathInput = Find-Element 'ExecutablePath'
    if ($null -eq $pathInput) { throw 'Editable executable path field not found.' }
    $pathInput.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('"' + $fixtureExe + '"')
    Wait-Until { (Find-Element 'AnalyzePath').Current.IsEnabled } 'analyze button enabled'
    Invoke-Button 'AnalyzePath'
    Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'static executable analysis'
    Write-Host 'PASS GUI quoted path analysis'
    $versionCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, ([string]::Concat('Versione dichiarata nell', [char]0x2019, 'EXE')))
    $versionElement = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $versionCondition)
    if ($null -eq $versionElement) { throw 'Executable version section not found.' }
    $versionExpanded = $false
    for ($depth = 0; $depth -lt 5 -and $null -ne $versionElement; $depth++) {
        $versionPattern = $null
        if ($versionElement.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$versionPattern)) {
            $versionPattern.Expand()
            $versionExpanded = $true
            break
        }
        $versionElement = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($versionElement)
    }
    if (-not $versionExpanded) { throw 'Executable version section cannot be expanded.' }
    Write-Host 'PASS GUI declared executable version section available'
    if ($null -eq (Find-Element 'GamePlay') -or $null -eq (Find-Element 'WindowsSystemInfo')) { throw 'Game play/system settings actions missing.' }

    if ($PreviewDirectory) {
        Start-Sleep -Milliseconds 800
        [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '09-game-version.png')))
    }
    Invoke-Button 'AssetsPage'
    Wait-Until { Has-Heading 'Grafica del gioco' } 'generic graphics page'
    foreach ($specific in @('DownloadAsphaltPack', 'PrepareSmolModels', 'SelectSmolArchive', 'PrepareRemixTrial', 'PrepareAllDirectXTextures')) {
        if ($null -ne (Find-Element $specific)) { throw "UG2-specific action shown for an unrelated game: $specific" }
    }
    if ((Has-Heading 'Asfalto 2K') -or (Has-Heading 'Correzioni ai modelli auto')) { throw 'UG2-specific content shown for an unrelated game.' }
    Invoke-Button 'DlssPage'
    Wait-Until { $null -ne (Find-Element 'DlssStatus') } 'contextual DLSS page'
    Wait-Until { $null -ne (Find-Element 'DlssOpenScreen') -and $null -ne (Find-Element 'DlssGpuSettings') } 'DLSS display and GPU action row'
    $screenBounds = (Find-Element 'DlssOpenScreen').Current.BoundingRectangle
    $gpuBounds = (Find-Element 'DlssGpuSettings').Current.BoundingRectangle
    if ([Math]::Abs($screenBounds.Top - $gpuBounds.Top) -gt 3 -or $gpuBounds.Left -le $screenBounds.Left -or $screenBounds.Width -lt 100 -or $gpuBounds.Width -lt 100) { throw 'DLSS screen/GPU buttons are not adjacent and aligned.' }
    Write-Host 'PASS GUI DLSS hardware illustration and aligned screen/GPU action row'

    $dlssNavigation = Find-Element 'DlssPage'
    $dlssParts = $dlssNavigation.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    $dlssNavigationText = $dlssNavigation.Current.Name + ' ' + (@($dlssParts | ForEach-Object { $_.Current.Name }) -join ' ')
    if ($dlssNavigationText -notlike '*DLSS*' -or $dlssNavigationText -like '*PRESTO*') { throw 'DLSS navigation is not available as a contextual page.' }
    if ($PreviewDirectory) {
        $dlssScroll = (Find-Element 'PageScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        if ($dlssScroll.Current.VerticallyScrollable) { $dlssScroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0) }
        Start-Sleep -Milliseconds 300
        [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '08-dlss.png')))
    }
    if ((Find-Element 'DlssStatus').Current.Name -like '*Underground*') { throw 'Generic DLSS status exposes a game-specific prerequisite.' }
    Write-Host 'PASS GUI contextual graphics: unrelated game has no UG2 tools, DLSS page available without PRESTO badge'
    Invoke-Button 'AssetsPage'
    Invoke-Button 'GraphicsOpenScreen'
    Wait-Until { $null -ne (Find-Element 'GameResolutionMethod') } 'general game resolution controls'
    $resolutionMethod = Find-Element 'GameResolutionMethod'
    Write-Host ('Resolution method control: ' + $resolutionMethod.Current.ControlType.ProgrammaticName + '; expand available: ' + $resolutionMethod.GetCurrentPropertyValue([Windows.Automation.AutomationElement]::IsExpandCollapsePatternAvailableProperty))
    $resolutionMethod.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $unityChoice = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true))
    function Find-UnityResolutionChoice {
        $items = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $unityChoice)
        return @($items | Where-Object { $_.Current.Name -like '*Unity*parametri di avvio*' } | Select-Object -First 1)
    }
    Wait-Until { @(Find-UnityResolutionChoice).Count -eq 1 } 'Unity resolution choice'
    $choice = @(Find-UnityResolutionChoice)[0]
    $choice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Button 'PrepareGameResolution'
    Wait-Until { $null -ne (Find-Element 'ApplyGameResolution') -and (Find-Element 'ApplyGameResolution').Current.IsEnabled } 'resolution no-write preview'
    if (@(Get-ChildItem -LiteralPath (Join-Path $fixtureData 'Settings/GameResolutions') -Filter state.json -Recurse -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Resolution preview wrote a profile.' }
    Invoke-Button 'ApplyGameResolution'
    Wait-Until { (Find-Element 'GameResolutionStatus').Current.Name -like 'Profilo applicato*' } 'resolution profile saved'
    $resolutionStates = @(Get-ChildItem -LiteralPath (Join-Path $fixtureData 'Settings/GameResolutions') -Filter state.json -Recurse)
    if ($resolutionStates.Count -ne 1) { throw 'Expected one per-game resolution profile.' }
    $resolutionState = Get-Content -LiteralPath $resolutionStates[0].FullName -Raw | ConvertFrom-Json
    if ($resolutionState.Executable -cne $fixtureExe -or $resolutionState.Options.Method -ne 1 -or $resolutionState.Options.Width -lt 320 -or $resolutionState.Options.Height -lt 200 -or @($resolutionState.Files).Count -ne 0) { throw 'Resolution profile identity or launch mode mismatch.' }
    if ((Get-FileHash -LiteralPath $fixtureExe -Algorithm SHA256).Hash -ne $originalHash) { throw 'Resolution profile changed the executable.' }
    if ($PreviewDirectory) {
        $resolutionScroll = (Find-Element 'PageScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        if ($resolutionScroll.Current.VerticallyScrollable) { $resolutionScroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 45) }
        Start-Sleep -Milliseconds 300
        [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '10-game-resolution.png')))
    }
    Invoke-Button 'RemoveGameResolution'
    Wait-Until { $null -ne (Find-Element 'ApplyGameResolution') -and (Find-Element 'ApplyGameResolution').Current.IsEnabled } 'resolution removal preview'
    Invoke-Button 'ApplyGameResolution'
    Wait-Until { (Find-Element 'GameResolutionStatus').Current.Name -like 'Profilo rimosso*' } 'resolution profile removal'
    if (Test-Path -LiteralPath $resolutionStates[0].FullName) { throw 'Resolution profile remains after removal.' }
    Write-Host 'PASS GUI general resolution profile: monitor modes, no-write preview, apply and removal; fixture EXE never executed'
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
        foreach ($clue in @('GLOBAL', 'TRACKS', 'CARS')) { [IO.Directory]::CreateDirectory((Join-Path $fixtureGame $clue)) | Out-Null }
        $profileHash = (Get-FileHash -LiteralPath $profileExe -Algorithm SHA256).Hash
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $profileKey = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($profileExe.ToUpperInvariant()))).Replace('-', '').ToLowerInvariant()
            $folderKey = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fixtureGame.ToUpperInvariant()))).Replace('-', '').ToLowerInvariant()
        } finally { $sha.Dispose() }
        $profileBackupStore = Join-Path $fixtureData "Backups/$profileKey"
        $profileStore = Join-Path $fixtureData "Profiles/$folderKey"
        if ((Test-Path -LiteralPath $profileBackupStore) -or (Test-Path -LiteralPath $profileStore)) { throw 'Unexpected existing profile fixture state.' }
        (Find-Element 'ExecutablePath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($profileExe)
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
        Wait-Until { $null -ne (Find-Element 'TargetDisplay') } 'display selector'
        $displayResolution = (Find-Element 'DetectedDisplayResolution').Current.Name
        if ($displayResolution -notmatch '^\d+ \u00d7 \d+$') { throw "Active display resolution was not detected: $displayResolution" }
        if ($null -eq (Find-Element 'CheckSoftwareUpdates')) { throw 'Software update button missing.' }
        Invoke-Button 'RefreshDisplays'
        if ((Find-Element 'DetectedDisplayResolution').Current.Name -ne $displayResolution) { throw 'Display enumeration unstable.' }
        Write-Host "PASS Windows display enumeration, resolution $displayResolution, display selector and updater entry point"
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
        # Original test artwork, generated only for this disposable UI fixture.
        $coverFolder = Join-Path $fixtureData 'Library/Covers'
        [IO.Directory]::CreateDirectory($coverFolder) | Out-Null
        $coverBitmap = [Drawing.Bitmap]::new(480, 270)
        $coverGraphics = [Drawing.Graphics]::FromImage($coverBitmap)
        $coverFont = [Drawing.Font]::new('Segoe UI', 26, [Drawing.FontStyle]::Bold)
        $coverBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(188, 255, 105))
        try {
            $coverGraphics.Clear([Drawing.Color]::FromArgb(18, 29, 43))
            $coverGraphics.DrawString('TEST GAME', $coverFont, $coverBrush, 34, 34)
            $coverGraphics.FillRectangle($coverBrush, 34, 108, 260, 5)
            $coverGraphics.DrawString('LOCAL COVER', $coverFont, [Drawing.Brushes]::White, 34, 155)
            $coverBitmap.Save((Join-Path $coverFolder ($libraryGame[0].Id + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $coverBrush.Dispose(); $coverFont.Dispose(); $coverGraphics.Dispose(); $coverBitmap.Dispose() }
        $process = Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -PassThru
        Wait-Until { $process.MainWindowHandle -ne [IntPtr]::Zero } 'library restart'
        $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        Wait-Until { $null -ne (Find-Element ('LibraryOpen-' + $libraryGame[0].Id)) } 'persisted library card'
        if ((Find-Element ('LibraryOpen-' + $libraryGame[0].Id)).Current.Name -ne 'Analizza') { throw 'Library analysis action label is misleading.' }
        if ($PreviewDirectory) {
            Start-Sleep -Milliseconds 300
            [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '05-library-populated.png')))
        }
        Invoke-Button ('LibraryOpen-' + $libraryGame[0].Id)
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'open stored game without entering path'
        if ((Find-Element 'ExecutablePath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $profileExe) { throw 'Library opened another executable.' }
        Write-Host 'PASS GUI library persistence after restart and game selection without re-entering its path'
        [IO.File]::WriteAllText((Join-Path $fixtureGame 'GamePanelLibraryProbe.request'), 'Synthetic launcher-only probe; no rendering required.')
        Invoke-Button 'LibraryPage'
        Invoke-Button ('LibraryPlay-' + $libraryGame[0].Id)
        Wait-Until {
            $saved = Get-Content -LiteralPath $libraryPath -Raw | ConvertFrom-Json
            return $null -ne @($saved.Games | Where-Object { $_.Id -eq $libraryGame[0].Id })[0].LastPlayedAt
        } 'library launch and last-played timestamp'
        $launched = @([Diagnostics.Process]::GetProcessesByName('speed2') | Where-Object { $_.MainModule.FileName -eq $profileExe })
        if ($launched.Count -ne 1) { throw 'Library did not launch exactly our selected synthetic renderer.' }
        $renderProcess = $launched[0]
        # GetProcessesByName creates a detached Process object. Keep its handle
        # open before exit; otherwise Windows PowerShell may expose a null ExitCode.
        $libraryProbeHandle = $renderProcess.Handle
        if (-not $renderProcess.WaitForExit(45000) -or $renderProcess.ExitCode -ne 0) { throw "Library-launched probe failed: $($renderProcess.ExitCode)" }
        if ((Get-Content -LiteralPath (Join-Path $fixtureGame 'GamePanelLibraryProbe.result') -Raw) -ne 'GamePanel-library-launch-ok') { throw 'Selected library executable did not execute our launcher probe.' }
        $renderProcess.Dispose(); $renderProcess = $null
        $launchReportPath = Join-Path $fixtureData 'Diagnostics/last-launch.json'
        $launchReport = Get-Content -LiteralPath $launchReportPath -Raw | ConvertFrom-Json
        if ($launchReport.Executable -cne $profileExe -or $launchReport.WorkingDirectory -cne $fixtureGame -or @($launchReport.Arguments).Count -ne 0 -or $launchReport.Route -ne 'GamePanel' -or $launchReport.Result -ne 'Started') { throw 'Direct-launch diagnostic does not match the real probe.' }
        Remove-Item -LiteralPath (Join-Path $fixtureGame 'GamePanelLibraryProbe.result')
        Invoke-Button 'LaunchThroughExplorer'
        Wait-Until { @(Get-Process -Name speed2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $profileExe }).Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $fixtureGame 'GamePanelLibraryProbe.result')) } 'Explorer-launched GUI probe'
        $renderProcess = @(Get-Process -Name speed2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $profileExe })[0]
        $explorerProbeHandle = $renderProcess.Handle
        if (-not $renderProcess.WaitForExit(45000) -or $renderProcess.ExitCode -ne 0) { throw 'Explorer GUI probe did not complete.' }
        $renderProcess.Dispose(); $renderProcess = $null
        $launchReport = Get-Content -LiteralPath $launchReportPath -Raw | ConvertFrom-Json
        if ($launchReport.Route -ne 'ExplorerDesktop' -or $launchReport.Result -ne 'Requested' -or $launchReport.Executable -cne $profileExe -or $launchReport.WorkingDirectory -cne $fixtureGame -or @($launchReport.Arguments).Count -ne 0) { throw 'Explorer-launch diagnostic differs from the accepted request.' }
        if ((Get-Content -LiteralPath (Join-Path $fixtureGame 'GamePanelLibraryProbe.result') -Raw) -ne 'GamePanel-library-launch-ok') { throw 'Explorer GUI probe did not find its game-relative request.' }
        Write-Host 'PASS GUI Explorer desktop launch and diagnostics: same EXE and working directory, no added arguments'
        Write-Host 'PASS GUI launch directly from saved library card and persisted last-played timestamp'

    }
    if ($AssetFixturePath -and $AssetPackagePath) {
        $assetGame = Join-Path $fixtureRoot 'Static Asset Game'
        [IO.Directory]::CreateDirectory($assetGame) | Out-Null
        $assetExe = Join-Path $assetGame 'speed2.exe'
        Copy-Item -LiteralPath (Resolve-Path -LiteralPath $AssetFixturePath).Path -Destination $assetExe
        foreach ($clue in @('GLOBAL', 'TRACKS', 'CARS')) { [IO.Directory]::CreateDirectory((Join-Path $assetGame $clue)) | Out-Null }
        $assetExeHash = (Get-FileHash -LiteralPath $assetExe -Algorithm SHA256).Hash
        $assetZip = (Resolve-Path -LiteralPath $AssetPackagePath).Path
        $assetArchiveHash = (Get-FileHash -LiteralPath $assetZip -Algorithm SHA256).Hash
        Invoke-Button 'OverviewPage'
        (Find-Element 'ExecutablePath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($assetExe)
        Invoke-Button 'AnalyzePath'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'static asset game analysis'
        Invoke-Button 'ScreenPage'
        Invoke-Button 'PrepareScreen'
        Wait-Until { $null -ne (Find-Element 'ApplyScreen') -and (Find-Element 'ApplyScreen').Current.IsEnabled } 'asset prerequisite preview'
        Invoke-Button 'ApplyScreen'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'asset prerequisite install'
        Invoke-Button 'AssetsPage'
        Select-AssetsTab 'AssetsAvailableTab'
        Set-AssetAdvancedTools $true
        (Find-Element 'AssetPackagePath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($assetZip)
        Invoke-Button 'ImportAssetPackage'
        Wait-Until { $null -ne (Find-Element 'ApplyAssetPlan') -and (Find-Element 'ApplyAssetPlan').Current.IsEnabled } 'real asphalt pack import and preview' 60
        if (Test-Path -LiteralPath (Join-Path $assetGame 'TexturePacks/GamePanel-asphalt-hd/textures.bin')) { throw 'Asset preview wrote game data before confirmation.' }
        if ((Get-FileHash -LiteralPath $assetExe -Algorithm SHA256).Hash -ne $assetExeHash) { throw 'Asset preview changed the synthetic EXE.' }
        Invoke-Button 'ApplyAssetPlan'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'asset install completion before tab switch'
        Select-AssetsTab 'AssetsInstalledTab'
        Wait-Until { $null -ne (Find-Element 'ToggleAsset-asphalt-hd') -and (Find-Element 'ToggleAsset-asphalt-hd').Current.IsEnabled } 'real asphalt pack static install'
        $texturePath = Join-Path $assetGame 'TexturePacks/GamePanel-asphalt-hd/textures.bin'
        if ((Get-FileHash -LiteralPath $texturePath -Algorithm SHA256).Hash -ne '3990fe97b7dd5b72446adf71c5fd508d78b4afa75919c6f8319442c2e3522bb6') { throw 'Installed 2K BIN hash mismatch.' }
        if ((Get-FileHash -LiteralPath (Join-Path $assetGame 'scripts/TexWizard.asi') -Algorithm SHA256).Hash -ne '63b464597f60d7e2f29dc65bd48919d9336896d1f54c9be8247157c14ebfe25b') { throw 'Installed texture runtime hash mismatch.' }
        Invoke-Button 'ScreenPage'
        if ((Find-Element 'PrepareScreen').Current.IsEnabled) { throw 'Widescreen removal is not blocked while textures depend on its ASI loader.' }
        Invoke-Button 'AssetsPage'
        Select-AssetsTab 'AssetsInstalledTab'
        $texConfig = Join-Path $assetGame 'scripts/TexWizard.json'
        $enabledConfig = Get-Content -LiteralPath $texConfig -Raw | ConvertFrom-Json
        if ($enabledConfig.autoScan -or @($enabledConfig.packs).Count -ne 1 -or $enabledConfig.packs[0] -ne 'TexturePacks\GamePanel-asphalt-hd') { throw 'Texture configuration is not limited to the selected pack.' }
        if ($PreviewDirectory) {
            (Find-Element 'PageScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
            Start-Sleep -Milliseconds 300
            [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '06-textures.png')))
            Select-AssetsTab 'AssetsAvailableTab'
            Set-AssetAdvancedTools $false
            (Find-Element 'PageScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
            Start-Sleep -Milliseconds 300
            [GamePanelCapture]::Save($process.MainWindowHandle, [IO.Path]::GetFullPath((Join-Path $PreviewDirectory '07-assets-available.png')))
            Select-AssetsTab 'AssetsInstalledTab'
        }
        Invoke-Button 'ToggleAsset-asphalt-hd'
        Wait-Until { $null -ne (Find-Element 'ApplyAssetPlan') -and (Find-Element 'ApplyAssetPlan').Current.IsEnabled } 'texture disable preview'
        Invoke-Button 'ApplyAssetPlan'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'texture disable'
        $disabledConfig = Get-Content -LiteralPath $texConfig -Raw | ConvertFrom-Json
        if ($disabledConfig.autoScan -or @($disabledConfig.packs).Count -ne 0) { throw 'Disabled texture pack remains enabled.' }
        Invoke-Button 'ToggleAsset-asphalt-hd'
        Wait-Until { $null -ne (Find-Element 'ApplyAssetPlan') -and (Find-Element 'ApplyAssetPlan').Current.IsEnabled } 'texture enable preview'
        Invoke-Button 'ApplyAssetPlan'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'texture enable'
        Invoke-Button 'RemoveAsset-asphalt-hd'
        Wait-Until { $null -ne (Find-Element 'ApplyAssetPlan') -and (Find-Element 'ApplyAssetPlan').Current.IsEnabled } 'texture remove preview'
        Invoke-Button 'ApplyAssetPlan'
        Wait-Until { $null -eq (Find-Element 'ToggleAsset-asphalt-hd') -and (Find-Element 'OperationStatus').Current.Name -eq 'Operazione profilo completata' } 'texture static removal'
        if (Test-Path -LiteralPath $texturePath) { throw 'Removed texture BIN remains.' }
        if (Test-Path -LiteralPath (Join-Path $assetGame 'scripts/TexWizard.asi')) { throw 'Texture ASI remains after final pack removal.' }
        if ((Get-FileHash -LiteralPath $assetExe -Algorithm SHA256).Hash -ne $assetExeHash -or (Get-FileHash -LiteralPath $assetZip -Algorithm SHA256).Hash -ne $assetArchiveHash) { throw 'Asset lifecycle changed EXE/archive.' }
        Write-Host 'PASS GUI real 2K texture archive, exact runtime, no-write preview, install/toggle/remove; static synthetic EXE and both ASIs NEVER EXECUTED'
    }
    Invoke-Button 'LibraryPage'
    $savedBefore = Get-Content -LiteralPath (Join-Path $fixtureData 'Library/library.json') -Raw | ConvertFrom-Json
    $variantCopy = @($savedBefore.Games | Where-Object { $_.ExecutablePath -eq $fixtureExe })[0]
    $variantParent = @($savedBefore.Games | Where-Object { $_.Id -ne $variantCopy.Id })[0]
    if ($null -eq $variantCopy -or $null -eq $variantParent) { throw 'Expected two library entries for explicit variant checks.' }
    Invoke-Button ('LibraryLinkVariant-' + $variantCopy.Id)
    function Find-VariantDialogControl([string]$Id) {
        $condition = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
        return [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    }
    Wait-Until { $null -ne (Find-VariantDialogControl 'LibraryVariantParent') } 'explicit original-game picker'
    if ((Find-VariantDialogControl 'ConfirmLibraryVariantLink').Current.IsEnabled) { throw 'Variant parent was selected without user choice.' }
    (Find-VariantDialogControl 'LibraryVariantParent').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $parentName = $variantParent.Title + ' ' + [char]0x00b7 + ' ' + $variantParent.ExecutablePath
    $parentCondition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $parentName))
    Wait-Until { $null -ne [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $parentCondition) } 'variant original choice'
    $parentChoice = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $parentCondition)
    $parentChoice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Find-VariantDialogControl 'ConfirmLibraryVariantLink').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-Until { $null -ne (Find-Element ('LibraryVariant-' + $variantParent.Id)) } 'one library card with variant selector'
    $linked = Get-Content -LiteralPath (Join-Path $fixtureData 'Library/library.json') -Raw | ConvertFrom-Json
    if (@($linked.Variants).Count -ne 1 -or $linked.Variants[0].OriginalId -ne $variantParent.Id -or $linked.Variants[0].VariantId -ne $variantCopy.Id -or @($linked.Games).Count -ne @($savedBefore.Games).Count) { throw 'Explicit variant relationship or library entries mismatch.' }
    Invoke-Button ('LibraryOpen-' + $variantParent.Id)
    Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'group original analysis'
    Invoke-Button 'LibraryPage'
    $persisted = Get-Content -LiteralPath (Join-Path $fixtureData 'Library/library.json') -Raw | ConvertFrom-Json
    if (@($persisted.Variants).Count -ne 1) { throw 'Reanalysis dropped explicit variant relationship.' }
    foreach ($variantStep in @(@{ Label = 'Originale'; Game = $variantParent }, @{ Label = 'Remix'; Game = $variantCopy })) {
        $variantPicker = Find-Element ('LibraryVariant-' + $variantParent.Id)
        $variantPicker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $variantCondition = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $variantStep.Label))
        Wait-Until { $null -ne [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $variantCondition) } 'library variant choice'
        $variantChoice = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $variantCondition)
        $variantChoice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        try { $variantPicker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() }
        catch [Windows.Automation.ElementNotAvailableException] { }
        Wait-Until {
            $state = Get-Content -LiteralPath (Join-Path $fixtureData 'Library/library.json') -Raw | ConvertFrom-Json
            return $state.SelectedId -eq $variantStep.Game.Id -and $null -ne (Find-Element ('LibraryVariant-' + $variantParent.Id))
        } 'variant selection and analysis completed'
        Invoke-Button 'OverviewPage'
        Wait-Until { (Find-Element 'OperationStatus').Current.Name -like 'Analisi completata*' } 'active variant analysis'
        if ((Find-Element 'ExecutablePath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $variantStep.Game.ExecutablePath) { throw 'Variant selector retained another active executable in the game page.' }
        Invoke-Button 'LibraryPage'
    }
    Write-Host 'PASS GUI variant selection refreshes the analyzed executable and all game-specific pages'
    Write-Host 'PASS GUI explicit Remix association: user chooses original, one card with variant selector, entries preserved across reanalysis'
    Invoke-Button 'DeselectLibraryGame'
    Wait-Until { (Find-Element 'OperationStatus').Current.Name -eq 'Nessun gioco selezionato' } 'deselect analyzed game'
    $savedAfter = Get-Content -LiteralPath (Join-Path $fixtureData 'Library/library.json') -Raw | ConvertFrom-Json
    if ($null -ne $savedAfter.SelectedId -or @($savedAfter.Games).Count -ne @($savedBefore.Games).Count) { throw 'Deselect lost library entries or did not clear selection.' }
    Invoke-Button 'OverviewPage'
    if ((Find-Element 'ExecutablePath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '') { throw 'Deselect retained active executable path.' }
    Write-Host 'PASS GUI deselection clears active context and preserves library entries'
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
