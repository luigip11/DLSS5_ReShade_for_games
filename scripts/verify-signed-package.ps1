param(
    [Parameter(Mandatory = $true)][string]$UnsignedDirectory,
    [Parameter(Mandatory = $true)][string]$SignedDirectory,
    [Parameter(Mandatory = $true)][string]$ExpectedPublisher
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ExpectedPublisher)) { throw 'Expected publisher is required.' }
$source = (Resolve-Path -LiteralPath $UnsignedDirectory).Path
$signed = (Resolve-Path -LiteralPath $SignedDirectory).Path
$owned = @('GamePanel.exe', 'GamePanel.dll', 'GamePanel.Core.dll')
foreach ($name in $owned) {
    $path = Join-Path $signed $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Signed file missing: $name" }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
        throw "Publisher signature is not valid: $name ($($signature.Status))"
    }
    if (-not [string]::Equals($signature.SignerCertificate.Subject, $ExpectedPublisher, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected publisher for $name"
    }
    if ($null -eq $signature.TimeStamperCertificate) { throw "Timestamp missing: $name" }
}
$sourceFiles = @(Get-ChildItem -LiteralPath $source -File -Recurse | ForEach-Object { $_.FullName.Substring($source.Length + 1) })
$signedFiles = @(Get-ChildItem -LiteralPath $signed -File -Recurse | ForEach-Object { $_.FullName.Substring($signed.Length + 1) })
if (@(Compare-Object $sourceFiles $signedFiles).Count -ne 0) { throw 'Signed package has unexpected or missing files.' }
foreach ($relative in $sourceFiles) {
    if ($owned -contains $relative) { continue }
    $before = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $source $relative)).Hash
    $after = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $signed $relative)).Hash
    if ($before -ne $after) { throw "Non-project file changed during signing: $relative" }
}
# Regenerate the manifest only after signature and unchanged-runtime verification.
$manifest = Get-ChildItem -LiteralPath $signed -File -Recurse | Where-Object { $_.Name -ne 'FILES-SHA256SUMS.txt' } | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($signed.Length + 1).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $relative
}
[IO.File]::WriteAllLines((Join-Path $signed 'FILES-SHA256SUMS.txt'), [string[]]$manifest, [Text.UTF8Encoding]::new($false))
Write-Host 'PASS trusted publisher, timestamp, package file set and unchanged third-party runtime'
