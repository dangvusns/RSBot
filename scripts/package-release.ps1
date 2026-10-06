param([Parameter(Mandatory = $true)][string]$Tag)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Expected a vMAJOR.MINOR.PATCH tag.' }
$required = @('RSBot.exe', 'RSBot.dll', 'RSBot.runtimeconfig.json',
    'RSBot.Updater.exe', 'RSBot.Updater.dll', 'RSBot.Updater.deps.json', 'RSBot.Updater.runtimeconfig.json', 'Data')
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path Build $name))) { throw "Build is missing $name" }
}
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Resolve-Path Build/RSBot.dll)).Version
if ($assemblyVersion -ne [version]($Tag.Substring(1) + '.0')) { throw "Build version $assemblyVersion does not match $Tag" }
$archive = Join-Path (Get-Location) "RSBot.$Tag.zip"
# Profiles, logs, previous update staging and debugging symbols never belong in a release.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    $root = (Resolve-Path Build).Path
    foreach ($file in (Get-ChildItem Build -Recurse -File)) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
        if ($relative -match '^(User|Logs|update_temp)([\\/]|$)' -or
            $relative -eq 'updater_error.log' -or $file.Extension -eq '.pdb') { continue }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName,
            $relative.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content "$archive.sha256" -Encoding ascii
Write-Host "Created $archive"
