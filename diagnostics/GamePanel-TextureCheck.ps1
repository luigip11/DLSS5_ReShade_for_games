# GamePanel contributors, MIT. Read-only NFS Underground 2 diagnostics.
# Start the game yourself and leave it at its main menu before running this text.
# No network, process start/stop, memory writes, injection or plugin loading.
param([string]$OutputDirectory = [Environment]::GetFolderPath('Desktop'))
$ErrorActionPreference = 'Stop'
$nativeSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class GamePanelTextureReadOnly {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr count, out UIntPtr read);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    public static byte[] Read(IntPtr process, long address, int count) {
        if (count < 1 || count > 512 || address < 0) throw new ArgumentException("Read outside diagnostic limits.");
        byte[] bytes = new byte[count]; UIntPtr read;
        if (!ReadProcessMemory(process, new IntPtr(address), bytes, new UIntPtr((uint)count), out read) || read.ToUInt64() != (ulong)count)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read the game's diagnostic bytes.");
        return bytes;
    }
    public static long CallTarget(byte[] bytes, long address) {
        if (bytes == null || bytes.Length != 5 || bytes[0] != 0xE8) return -1;
        return address + 5 + BitConverter.ToInt32(bytes, 1);
    }
}
'@
if (-not ('GamePanelTextureReadOnly' -as [type])) { Add-Type -TypeDefinition $nativeSource }
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
$games = @([Diagnostics.Process]::GetProcessesByName('speed2'))
if ($games.Count -ne 1) {
    foreach ($candidate in $games) { $candidate.Dispose() }
    throw 'Leave exactly one Underground 2 instance running at its main menu, then repeat.'
}
$gameProcess = $games[0]
$handle = [IntPtr]::Zero
try {
    $started = $gameProcess.StartTime.ToUniversalTime()
    $module = $gameProcess.MainModule
    $exePath = $module.FileName
    if ([IO.Path]::GetFileName($exePath) -ine 'speed2.exe') { throw 'Unexpected process image.' }
    $imageBase = $module.BaseAddress.ToInt64()
    $hashBefore = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    # PROCESS_QUERY_INFORMATION | PROCESS_VM_READ only. No write/operation rights.
    $handle = [GamePanelTextureReadOnly]::OpenProcess(0x0410, $false, $gameProcess.Id)
    if ($handle -eq [IntPtr]::Zero) { throw 'Cannot open the game for reading. Use the same Windows account and privilege level as the game.' }
    $dos = [GamePanelTextureReadOnly]::Read($handle, $imageBase, 64)
    if ([BitConverter]::ToUInt16($dos, 0) -ne 0x5A4D) { throw 'Unexpected in-memory DOS header.' }
    $peAt = [BitConverter]::ToUInt32($dos, 60)
    if ($peAt -lt 64 -or $peAt -gt 1048576) { throw 'Unexpected PE header offset.' }
    $header = [GamePanelTextureReadOnly]::Read($handle, $imageBase + $peAt, 112)
    if ([BitConverter]::ToUInt32($header, 0) -ne 0x4550 -or [BitConverter]::ToUInt16($header, 4) -ne 0x14C -or [BitConverter]::ToUInt16($header, 24) -ne 0x10B) { throw 'The game is not a recognized PE32 x86 image.' }
    $entry = [BitConverter]::ToUInt32($header, 40)
    $imageBytes = [BitConverter]::ToUInt32($header, 80)
    if ($imageBytes -lt 4096 -or $imageBytes -gt 33554432) { throw 'Unexpected in-memory image size.' }
    $sites = @(
        4430024,4489790,4489810,4489830,4784921,4784970,4784989,4785008,4785027,4785046,4785632,4786117,
        4786327,4786430,4788845,4791502,4808101,4858928,4859001,4871029,4914722,4914808,4916056,4924768,
        4924854,4936624,4973667,4973753,5007108,5007348,5037501,5037914,5204205,5289231,5290994,5294974,
        5295166,5310974,5311005,5311033,5311061,5357308,5466250,5466846,5467406,5487024,5766138,6002882,
        6002913,6002944,6003004,6003034,6003064,6003094,6003124,6003154,6014726,6015152,6015530,6051682,
        6058952,6061020,6077254,6092449,6107818,6342645,6369396,6369426,6412205,6412967,6413066,6413981,
        6414035,6414051,6424138,6522624,6522643,6522919,6522968,6522999,6523017,5762457
    )
    $rows = @()
    foreach ($site in $sites) {
        $rva = [long]$site - 0x400000
        $targetRva = 0x4901D0 - 0x400000
        if ($site -eq 0x57ED99) { $targetRva = 0x57FB40 - 0x400000 }
        $row = [ordered]@{ Rva = ('0x{0:X8}' -f $rva); Match = $false; Bytes = $null; Target = $null; Error = $null }
        try {
            if ($rva -lt 0 -or $rva + 5 -gt $imageBytes) { throw 'Callsite outside image.' }
            $bytes = [GamePanelTextureReadOnly]::Read($handle, $imageBase + $rva, 5)
            $actual = [GamePanelTextureReadOnly]::CallTarget($bytes, $imageBase + $rva)
            $row.Bytes = [BitConverter]::ToString($bytes).Replace('-', '')
            if ($actual -ge 0) { $row.Target = '0x{0:X8}' -f $actual }
            $row.Match = $actual -eq ($imageBase + $targetRva)
        }
        catch { $row.Error = $_.Exception.Message }
        $rows += [pscustomobject]$row
    }
    $headerAfter = [GamePanelTextureReadOnly]::Read($handle, $imageBase + $peAt, 112)
    $sameHeader = [Convert]::ToBase64String($headerAfter) -ceq [Convert]::ToBase64String($header)
    $hashAfter = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($gameProcess.HasExited -or $gameProcess.StartTime.ToUniversalTime() -ne $started -or $hashBefore -cne $hashAfter) { throw 'Game/process image changed during the check; repeat.' }
    $matches = @($rows | Where-Object { $_.Match }).Count
    $report = [ordered]@{
        SchemaVersion = 1; CapturedAt = [DateTimeOffset]::Now.ToString('o'); Mode = 'ReadOnlyRuntimeSnapshot'
        ExecutablePath = $exePath; ExecutableSha256 = $hashBefore; ExecutableBytes = (Get-Item -LiteralPath $exePath).Length
        ProcessId = $gameProcess.Id; ProcessStartedUtc = $started.ToString('o')
        ImageBase = ('0x{0:X8}' -f $imageBase); PreferredBaseMatches = ($imageBase -eq 0x400000)
        EntryPointRva = ('0x{0:X8}' -f $entry); EntryPointMatches = ($entry -eq 0x35BCC7)
        HeaderStableDuringCheck = $sameHeader; MatchingCalls = $matches; TotalCalls = $rows.Count; Calls = $rows
        Limitation = 'Main-menu snapshot only; not proof of state at ASI load, function ABI, safety or complete game compatibility.'
    }
    $desktop = [IO.Path]::GetFullPath($OutputDirectory)
    if ([string]::IsNullOrWhiteSpace($desktop) -or -not [IO.Directory]::Exists($desktop)) { throw 'Desktop folder is unavailable.' }
    $reportPath = Join-Path $desktop ('GamePanel-TextureCheck-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.json')
    $json = $report | ConvertTo-Json -Depth 6
    $encoded = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $output = [IO.File]::Open($reportPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $output.Write($encoded, 0, $encoded.Length); $output.Flush() } finally { $output.Dispose() }
    Write-Host ("Read-only check complete: {0}/{1} CALLs; runtime entry point 0x{2:X8}." -f $matches, $rows.Count, $entry)
    Write-Host ('Report saved on Desktop: ' + $reportPath)
    Write-Host 'Share only this JSON report. It contains the game path; review it before sharing.'
}
finally {
    if ($handle -ne [IntPtr]::Zero) { [GamePanelTextureReadOnly]::CloseHandle($handle) | Out-Null }
    $gameProcess.Dispose()
}
