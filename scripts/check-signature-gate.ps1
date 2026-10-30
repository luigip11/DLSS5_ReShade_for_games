param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$rejected = $false
try {
    & (Join-Path $PSScriptRoot 'verify-signed-package.ps1') -UnsignedDirectory $PackageDirectory -SignedDirectory $PackageDirectory -ExpectedPublisher 'CN=Not a production signing identity'
}
catch {
    if ($_.Exception.Message -notlike 'Publisher signature is not valid:*') { throw }
    $rejected = $true
    Write-Host 'PASS signing gate rejects the current unsigned alpha; no signed-release claim is made'
}
if (-not $rejected) { throw 'Unsigned package was incorrectly accepted by the signing gate.' }
