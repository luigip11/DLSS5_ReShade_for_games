param([string]$GameDirectory)
$ErrorActionPreference = 'Stop'
if (-not $GameDirectory) { $GameDirectory = Read-Host 'Cartella del gioco UG1 che avvii (contiene Speed.exe)' }
$root = (Resolve-Path -LiteralPath $GameDirectory.Trim().Trim('"')).Path
if (-not (Test-Path -LiteralPath (Join-Path $root 'Speed.exe') -PathType Leaf)) { throw 'Speed.exe non trovato nella cartella indicata.' }
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('UG1 - diagnosi sola lettura: nessuna modifica a gioco o registro')
$lines.Add('Cartella: ' + $root)
foreach ($relative in @('Speed.exe', 'dinput8.dll', 'd3d9.dll', 'scripts/NFSUnderground.WidescreenFix.asi', 'scripts/RTXRemix.asi', 'LANGUAGES/ITALIAN.BIN', 'sound/strmBank/STRM_Italian_Speech_MB.abk', 'SPEECHSTREAMS/STRM_Italian_Speech_MB.ast')) {
    $path = Join-Path $root $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $file = Get-Item -LiteralPath $path
        $lines.Add(('{0} | {1} byte | SHA256 {2}' -f $relative, $file.Length, (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash))
    } else { $lines.Add('ASSENTE: ' + $relative) }
}
$ini = Join-Path $root 'scripts/NFSUnderground.WidescreenFix.ini'
if (Test-Path -LiteralPath $ini -PathType Leaf) {
    $lines.Add('INI della cartella selezionata:')
    foreach ($line in Get-Content -LiteralPath $ini) {
        if ($line -match '^\s*(\[.*\]|ShowLangSelect\s*=|SkipIntro\s*=|ResX\s*=|ResY\s*=|CustomUserFilesDirectoryInGameDir\s*=)') { $lines.Add($line) }
    }
}
$keys = @('SOFTWARE\EA Games\Need For Speed Underground', 'SOFTWARE\EA Games\NFS Underground',
    'SOFTWARE\Classes\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\EA Games\Need For Speed Underground',
    'SOFTWARE\Classes\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\EA Games\NFS Underground')
foreach ($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryHive]::CurrentUser)) {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        try {
            foreach ($name in $keys) {
                $key = $base.OpenSubKey($name, $false)
                if ($null -eq $key) { continue }
                try {
                    $lines.Add("Registro $hive / $view / $name")
                    foreach ($value in @('Language', 'Locale', 'Install Dir')) {
                        $data = $key.GetValue($value, '(assente)')
                        $lines.Add("  $value = $data")
                    }
                } finally { $key.Dispose() }
            }
        } finally { $base.Dispose() }
    }
}
$desktop = [Environment]::GetFolderPath('Desktop')
if (-not $desktop) { $desktop = $PSScriptRoot }
$output = Join-Path $desktop 'GamePanel-UG1-diagnosi.txt'
[IO.File]::WriteAllLines($output, $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Report creato: $output"
Write-Host 'Contiene percorsi locali: puoi oscurare il nome utente prima di condividerlo.'
