param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$repository = 'luigip11/DLSS5_ReShade_for_games'
if ($env:GITHUB_REPOSITORY -cne $repository -or $env:GITHUB_SHA -notmatch '^[a-f0-9]{40}$') { throw 'Unexpected release repository or commit.' }
[xml]$props = Get-Content (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or $env:GITHUB_REF -cne "refs/heads/release/$version") { throw 'Release branch and version must agree.' }
if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { throw 'Release token unavailable.' }
$headers = @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$api = "https://api.github.com/repos/$repository"
$tag = "v$version"
try {
    $existing = Invoke-RestMethod -Uri "$api/releases/tags/$tag" -Headers $headers
    throw 'A release already exists for this version. Refusing to replace it.'
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
}
$folder = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (-not (Test-Path (Join-Path $folder 'GamePanel.Updater.exe'))) { throw 'Incomplete portable artifact.' }
$output = Join-Path ([IO.Path]::GetTempPath()) ('GamePanel-Release-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$zip = Join-Path $output 'GamePanel-win-x64.zip'
Compress-Archive -Path "$folder/*" -DestinationPath $zip -CompressionLevel Optimal
$packageHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$metadata = @{ SchemaVersion = 1; Version = $version; PackageFileName = 'GamePanel-win-x64.zip'; PackageBytes = (Get-Item $zip).Length; PackageSha256 = $packageHash; Commit = $env:GITHUB_SHA }
$metadataPath = Join-Path $output 'GamePanel-update.json'
[IO.File]::WriteAllText($metadataPath, ($metadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$notes = Get-Content (Join-Path $PSScriptRoot '../docs/RELEASE-NOTES.md') -Raw
$body = @{ tag_name = $tag; target_commitish = $env:GITHUB_SHA; name = "GamePanel $version"; body = $notes; draft = $true; prerelease = $version.Contains('-') } | ConvertTo-Json
# Publish only after both uploaded files have matching GitHub digests. Failures leave a draft.
$release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body))
$upload = $release.upload_url -replace '\{.*$', ''
if ($upload -cne "https://uploads.github.com/repos/$repository/releases/$($release.id)/assets") { throw 'Unexpected GitHub upload URL.' }
foreach ($path in @($zip, $metadataPath)) {
    $name = [IO.Path]::GetFileName($path)
    $contentType = if ($name.EndsWith('.zip')) { 'application/zip' } else { 'application/json' }
    $asset = Invoke-RestMethod -Method Post -Uri ($upload + '?name=' + [Uri]::EscapeDataString($name)) -Headers $headers -ContentType $contentType -InFile $path
    $expected = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.digest -cne $expected -or $asset.size -ne (Get-Item $path).Length -or $asset.name -cne $name) { throw "GitHub asset verification failed: $name" }
}
$published = Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers -ContentType 'application/json' -Body '{"draft":false}'
if ($published.draft) { throw 'Release remained in draft state.' }
Write-Host "Published $($published.html_url)"
