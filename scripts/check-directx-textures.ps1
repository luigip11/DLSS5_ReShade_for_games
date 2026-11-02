param(
    [string]$OutputDirectory = './artifacts/directx-texture-probe',
    [string]$ReShadeDllPath = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$root = Join-Path $output ('run-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$summaryPath = Join-Path $output 'runtime-verification.json'
$summary = [ordered]@{
    SchemaVersion = 1
    Status = 'not-run'
    SyntheticFixtureOnly = $true
    RealGameVerified = $false
    TextureToolkitVersion = '1.2.1'
    TextureToolkitSourceCommit = 'd600d7bdc8293dd1e372f4565a970af99e0acffd'
    TextureToolkitSha256 = '4c7763ae8b54f5077365729b7360f427109bfb73f9500e2d48e6082930d065e4'
    ReShadeVersion = '6.8.0'
    ReShadeSha256 = '538998f66c0197adcdeadfe8e5ea19dd6ca3253e40399476d291f385ce518b88'
    Phases = @()
    RunDirectory = $root
}
function Write-Summary {
    [IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
function Assert-Pinned([string]$Path, [long]$Bytes, [string]$Sha256) {
    if ((Get-Item -LiteralPath $Path).Length -ne $Bytes -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Pinned component differs: $([IO.Path]::GetFileName($Path))"
    }
}
function Get-PinnedDownload([string]$Url, [string]$Destination, [long]$Bytes, [string]$Sha256, [bool]$GitHubRedirects) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(45)
    $uri = [Uri]$Url
    try {
        for ($redirect = 0; ; $redirect++) {
            if ($uri.Scheme -ne 'https' -or -not $uri.IsDefaultPort -or $uri.UserInfo -or $uri.Fragment) { throw 'Component redirect is not plain HTTPS.' }
            if ($uri.AbsoluteUri -ne $Url -and (-not $GitHubRedirects -or $uri.Host -ne 'release-assets.githubusercontent.com')) { throw 'Component redirect host is not approved.' }
            $response = $client.GetAsync($uri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            try {
                if ([int]$response.StatusCode -ge 300 -and [int]$response.StatusCode -le 399) {
                    if ($redirect -ge 3 -or -not $response.Headers.Location) { throw 'Component redirect limit exceeded.' }
                    $uri = [Uri]::new($uri, $response.Headers.Location)
                    continue
                }
                if ($response.StatusCode -ne [Net.HttpStatusCode]::OK) { throw "Component download failed: $($response.StatusCode)" }
                if ($null -ne $response.Content.Headers.ContentLength -and $response.Content.Headers.ContentLength -ne $Bytes) { throw 'Component declared size differs.' }
                $input = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                $target = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try {
                    $buffer = [byte[]]::new(65536)
                    $total = [long]0
                    while (($count = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $total += $count
                        if ($total -gt $Bytes) { throw 'Component body exceeds reviewed size.' }
                        $target.Write($buffer, 0, $count)
                    }
                    $target.Flush($true)
                } finally { $target.Dispose(); $input.Dispose() }
                break
            } finally { $response.Dispose() }
        }
    } finally { $client.Dispose() }
    Assert-Pinned $Destination $Bytes $Sha256
}
function Invoke-Fixture([string]$Name, [bool]$Injection, [bool]$WithReShade, [string]$ReplacementFile, [string]$OriginalHash) {
    $directory = Join-Path $root $Name
    $resources = Join-Path $directory 'resources'
    [IO.Directory]::CreateDirectory((Join-Path $resources 'inject')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $resources 'dump')) | Out-Null
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $directory 'DirectXTextureFixture.exe')
    Copy-Item -LiteralPath $toolkit -Destination (Join-Path $directory 'TextureToolkit-x86.asi')
    if ($ReplacementFile) { Copy-Item -LiteralPath $ReplacementFile -Destination (Join-Path $resources "inject/$OriginalHash.dds") }
    $ini = "[TextureToolkit]`r`nHotKey=0x2D`r`nResourceRoot=$resources`r`nEnableInjection=$([int]$Injection)`r`nAutoDump=1`r`nFilterSmallTextures=1`r`nShowCurrentFrameOnly=1`r`nAcceptSpecialKNames=0`r`nHighlightSelected=0`r`nShowOSDBanner=0`r`nVerbose=1`r`n"
    [IO.File]::WriteAllText((Join-Path $directory 'TextureToolkit.ini'), $ini, [Text.Encoding]::Unicode)
    if ($WithReShade) {
        Copy-Item -LiteralPath $reshade -Destination (Join-Path $directory 'd3d9.dll')
        [IO.Directory]::CreateDirectory((Join-Path $directory 'Shaders')) | Out-Null
        $shader = @'
texture2D ProbeColor : COLOR;
sampler2D ProbeSampler { Texture = ProbeColor; AddressU = CLAMP; AddressV = CLAMP; };
void ProbeVS(uint id : SV_VertexID, out float4 position : SV_Position, out float2 uv : TEXCOORD)
{
    uv.x = (id == 2) ? 2.0 : 0.0;
    uv.y = (id == 1) ? 2.0 : 0.0;
    position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}
float4 ProbePS(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target { return tex2D(ProbeSampler, uv); }
technique GamePanelDirectXProbe { pass { VertexShader = ProbeVS; PixelShader = ProbePS; } }
'@
        [IO.File]::WriteAllText((Join-Path $directory 'Shaders/GamePanelDirectXProbe.fx'), $shader, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $directory 'ProbePreset.ini'), "Techniques=GamePanelDirectXProbe@GamePanelDirectXProbe.fx`r`nTechniqueSorting=GamePanelDirectXProbe@GamePanelDirectXProbe.fx`r`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $directory 'ReShade.ini'), "[GENERAL]`r`nEffectSearchPaths=.\Shaders`r`nPresetPath=.\ProbePreset.ini`r`nPerformanceMode=1`r`n[OVERLAY]`r`nTutorialProgress=4`r`n", [Text.UTF8Encoding]::new($false))
    }
    $process = $null
    try {
        $mode = if ($Injection) { 'replacement' } else { 'original' }
        $process = Start-Process -FilePath (Join-Path $directory 'DirectXTextureFixture.exe') -ArgumentList $mode -WorkingDirectory $directory -PassThru
        if (-not $process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); throw "Synthetic phase $Name timed out." }
        $code = $process.ExitCode
    } finally { if ($process) { $process.Dispose() } }
    $path = Join-Path $directory 'fixture-result.json'
    if (-not (Test-Path -LiteralPath $path)) { throw "Synthetic phase $Name produced no result (exit $code)." }
    $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $summary.Phases += [ordered]@{ Name = $Name; ExitCode = $code; Result = $result; Directory = $directory }
    Write-Summary
    if ($code -eq 77 -and $result.status -eq 'unverified-no-d3d9-adapter') { return [pscustomobject]@{ Available = $false; Directory = $directory; Result = $result } }
    if ($code -ne 0 -or $result.status -ne 'verified-rendered-pixel' -or -not $result.toolkitLoaded) { throw "Synthetic phase $Name failed; see $path." }
    if ($WithReShade) {
        $log = Join-Path $directory 'ReShade.log'
        if (-not $result.reshadeLoaded -or -not (Test-Path -LiteralPath $log)) { throw 'Synthetic ReShade coexistence was not observed.' }
        $text = Get-Content -LiteralPath $log -Raw
        if ($text -notmatch "Successfully compiled '[^']*GamePanelDirectXProbe\.fx'" -or $text -match "Failed to compile '[^']*GamePanelDirectXProbe\.fx'") { throw 'ReShade probe shader did not compile.' }
    }
    if ($Injection) {
        $log = Join-Path $directory 'TextureToolkit.log'
        if (-not (Test-Path -LiteralPath $log) -or (Get-Content -LiteralPath $log -Raw) -notmatch 'Loaded high-res DX9 replacement[^\r\n]*128x128') { throw 'Toolkit did not report the higher-resolution DX9 replacement.' }
    }
    return [pscustomobject]@{ Available = $true; Directory = $directory; Result = $result }
}
try {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'MSVC x86 tools are required for the synthetic DirectX texture probe.' }
    $developer = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
    $source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'DirectXTextureFixture.cpp'))
    $fixture = Join-Path $root 'DirectXTextureFixture.exe'
    $command = '"{0}" -no_logo -arch=x86 -host_arch=x64 && cl /nologo /O2 /MT /W4 /WX /EHsc /std:c++17 /DUNICODE /D_UNICODE "{1}" /link /OUT:"{2}" user32.lib gdi32.lib d3d9.lib' -f $developer, $source, $fixture
    Push-Location $root
    try { & cmd /d /s /c $command; if ($LASTEXITCODE -ne 0) { throw 'Synthetic DirectX texture fixture compilation failed.' } }
    finally { Pop-Location }
    $toolkit = Join-Path $root 'TextureToolkit-x86.asi'
    Get-PinnedDownload 'https://github.com/BadassBaboon/Texture-Toolkit/releases/download/v1.2.1/TextureToolkit-x86.asi' $toolkit 1122304 $summary.TextureToolkitSha256 $true
    if ($ReShadeDllPath) {
        $reshade = (Resolve-Path -LiteralPath $ReShadeDllPath).Path
        Assert-Pinned $reshade 4136216 $summary.ReShadeSha256
    } else {
        $setup = Join-Path $root 'ReShade_Setup_6.8.0.exe'
        Get-PinnedDownload 'https://reshade.me/downloads/ReShade_Setup_6.8.0.exe' $setup 4075064 '207aea16205fbf952bc8fe1879966672454cf04002e7ad34237c7990a5b3c0b4' $false
        $data = [IO.File]::ReadAllBytes($setup)
        $offset = -1
        for ($i = 0; $i -lt $data.Length - 30; $i += 512) {
            if ($data[$i] -eq 0x50 -and $data[$i + 1] -eq 0x4b -and $data[$i + 2] -eq 3 -and $data[$i + 3] -eq 4) { $offset = $i; break }
        }
        if ($offset -lt 0) { throw 'Reviewed ReShade embedded archive was not found.' }
        $stream = [IO.MemoryStream]::new($data, $offset, $data.Length - $offset, $false)
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
        $reshade = Join-Path $root 'ReShade32.dll'
        try {
            $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'ReShade32.dll' })
            if ($entries.Count -ne 1 -or $entries[0].Length -ne 4136216) { throw 'Reviewed ReShade x86 archive entry differs.' }
            $input = $entries[0].Open(); $target = [IO.File]::Open($reshade, [IO.FileMode]::CreateNew)
            try { $input.CopyTo($target) } finally { $target.Dispose(); $input.Dispose() }
        } finally { $archive.Dispose(); $stream.Dispose() }
        Assert-Pinned $reshade 4136216 $summary.ReShadeSha256
    }
    $capture = Invoke-Fixture '01-capture' $false $false '' ''
    if (-not $capture.Available) {
        $summary.Status = 'unverified-no-d3d9-adapter'
        Write-Summary
        Write-Warning "UNVERIFIED: this host has no usable Direct3D 9 HAL adapter. No texture capture, replacement or ReShade coexistence PASS is claimed. See $summaryPath"
        return
    }
    $hash = $capture.Result.originalTextureHash
    if ($hash -cnotmatch '^[A-F0-9]{16}$') { throw 'Synthetic original hash is invalid.' }
    $dump = Join-Path $capture.Directory "resources/dump/$hash.dds"
    if (-not (Test-Path -LiteralPath $dump)) { throw 'Toolkit did not capture our known 64x64 original texture.' }
    $dds = [IO.File]::ReadAllBytes($dump)
    if ($dds.Length -ne (148 + 64 * 64 * 4) -or [BitConverter]::ToUInt32($dds, 0) -ne 0x20534444 -or [BitConverter]::ToUInt32($dds, 12) -ne 64 -or [BitConverter]::ToUInt32($dds, 16) -ne 64 -or [BitConverter]::ToUInt32($dds, 84) -ne 0x30315844 -or [BitConverter]::ToUInt32($dds, 128) -ne 87 -or [BitConverter]::ToUInt32($dds, 132) -ne 3 -or [BitConverter]::ToUInt32($dds, 140) -ne 1) { throw 'Captured synthetic DDS header or length differs from expected BGRA8.' }
    for ($i = 148; $i -lt $dds.Length; $i += 4) {
        if ($dds[$i] -ne 192 -or $dds[$i + 1] -ne 128 -or $dds[$i + 2] -ne 64 -or $dds[$i + 3] -ne 255) { throw 'Captured DDS differs from the original uploaded pixels.' }
    }
    $replacement = [byte[]]::new(148 + 128 * 128 * 4)
    [Array]::Copy($dds, 0, $replacement, 0, 148)
    foreach ($field in @(@(12,128), @(16,128), @(20,512), @(28,1))) { [Array]::Copy([BitConverter]::GetBytes([uint32]$field[1]), 0, $replacement, [int]$field[0], 4) }
    for ($i = 148; $i -lt $replacement.Length; $i += 4) { $replacement[$i] = 64; $replacement[$i + 1] = 224; $replacement[$i + 2] = 32; $replacement[$i + 3] = 255 }
    $replacementPath = Join-Path $root 'synthetic-green-128.dds'
    [IO.File]::WriteAllBytes($replacementPath, $replacement)
    $injected = Invoke-Fixture '02-replace' $true $false $replacementPath $hash
    if (-not $injected.Available) { throw 'Direct3D 9 adapter became unavailable after capture.' }
    $coexist = Invoke-Fixture '03-reshade-coexistence' $true $true $replacementPath $hash
    if (-not $coexist.Available) { throw 'Direct3D 9 adapter became unavailable during ReShade coexistence.' }
    $summary.Status = 'verified-synthetic-capture-replacement-reshade'
    $summary.OriginalTextureHash = $hash
    $summary.CaptureSha256 = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash.ToLowerInvariant()
    $summary.ReplacementSha256 = (Get-FileHash -LiteralPath $replacementPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Summary
    Write-Host 'PASS synthetic x86 DX9: exact original DDS capture, 64-to-128 texture replacement with changed rendered pixel, and ReShade 6.8 coexistence + shader compile. Real-game compatibility remains unverified.'
} catch {
    $summary.Status = 'failed'
    $summary.Error = $_.Exception.Message
    Write-Summary
    throw
}
