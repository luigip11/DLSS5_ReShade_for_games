param([string]$OutputDirectory = './artifacts/reshade-inspection')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$version = '6.8.0'
$url = "https://reshade.me/downloads/ReShade_Setup_$version.exe"
# Published by reshade.me on the download page. A changed certificate requires review.
$publisher = '589690208A5E52FB96980C4A6698F50ACD47C49F'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('GamePanel-ReShade-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
function Check-Publisher([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    Write-Host "Signature: $($signature.Status); $($signature.StatusMessage)"
    Write-Host "Certificate: $($signature.SignerCertificate.Subject); expiry $($signature.SignerCertificate.NotAfter.ToString('o')); timestamp $($signature.TimeStamperCertificate.Subject)"
    Write-Host "Downloaded SHA256: $((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash)"
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $publisher) {
        throw "ReShade publisher verification failed: $($signature.Status), $($signature.SignerCertificate.Thumbprint)"
    }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($info.FileMajorPart -ne 6 -or $info.FileMinorPart -ne 8 -or $info.FileBuildPart -ne 0) { throw 'Unexpected ReShade file version.' }
}
try {
    $setup = Join-Path $temporary 'ReShade-setup.exe'
    Invoke-WebRequest -Uri $url -OutFile $setup -MaximumRedirection 0
    if ((Get-Item -LiteralPath $setup).Length -gt 32MB) { throw 'ReShade setup exceeds the size limit.' }
    Check-Publisher $setup
    $data = [IO.File]::ReadAllBytes($setup)
    $offset = -1
    for ($i = 0; $i -lt $data.Length - 30; $i += 512) {
        if ($data[$i] -eq 0x50 -and $data[$i + 1] -eq 0x4b -and $data[$i + 2] -eq 3 -and $data[$i + 3] -eq 4) { $offset = $i; break }
    }
    if ($offset -lt 0) { throw 'Embedded ReShade archive not found.' }
    $stream = [IO.MemoryStream]::new($data, $offset, $data.Length - $offset, $false)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'ReShade32.dll' })
        if ($entries.Count -ne 1 -or $entries[0].Length -gt 32MB) { throw 'Invalid ReShade32 entry.' }
        $dll = Join-Path $temporary 'ReShade32.dll'
        $source = $entries[0].Open(); $target = [IO.File]::Create($dll)
        try { $source.CopyTo($target) } finally { $target.Dispose(); $source.Dispose() }
    } finally { $archive.Dispose(); $stream.Dispose() }
    Check-Publisher $dll
    $catalog = [ordered]@{
        SchemaVersion = 1; Version = $version; SetupUrl = $url; SignerThumbprint = $publisher
        SetupSha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        SetupBytes = (Get-Item -LiteralPath $setup).Length
        DllSha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
        DllBytes = (Get-Item -LiteralPath $dll).Length
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
    $json = $catalog | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'catalog.json'), $json, [Text.UTF8Encoding]::new($false))
    Write-Host $json
    Write-Host 'PASS official setup and x86 DLL: trusted publisher, pinned certificate and version. Neither file was executed or published.'
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
