# Run after build.ps1. Exercises the real updater against disposable installations.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('RSBot updater tests ' + [guid]::NewGuid().ToString('N'))
$runner = Join-Path $testRoot runner
New-Item -ItemType Directory $runner -Force | Out-Null
foreach ($name in @('RSBot.Updater.exe', 'RSBot.Updater.dll', 'RSBot.Updater.deps.json', 'RSBot.Updater.runtimeconfig.json')) {
    Copy-Item (Join-Path Build $name) $runner
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Assert-Content($Path, $Expected) {
    if ((Get-Content $Path -Raw).Trim() -ne $Expected) { throw "Unexpected contents: $Path" }
}
function Invoke-Case($Name, $Entries, $ExpectedExit, [switch]$BlockCopy) {
    $install = Join-Path $testRoot $Name
    New-Item -ItemType Directory (Join-Path $install update_temp), (Join-Path $install User), (Join-Path $install Data) -Force | Out-Null
    'old exe' | Set-Content (Join-Path $install RSBot.exe)
    'old dll' | Set-Content (Join-Path $install RSBot.dll)
    'my profile' | Set-Content (Join-Path $install User/profile.rs)
    'custom plugin' | Set-Content (Join-Path $install Data/custom.dll)
    if ($BlockCopy) { New-Item -ItemType Directory (Join-Path $install Data/blocked.dll) | Out-Null }
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $install update_temp/update.zip), [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Entries.GetEnumerator()) {
            $item = $zip.CreateEntry($entry.Key)
            $writer = [IO.StreamWriter]::new($item.Open())
            try { $writer.Write($entry.Value) } finally { $writer.Dispose() }
        }
    } finally { $zip.Dispose() }
    $process = Start-Process -FilePath (Join-Path $runner RSBot.Updater.exe) -ArgumentList @("`"$install`"", '0', '--no-restart') -Wait -PassThru
    if ($process.ExitCode -ne $ExpectedExit) { throw "$Name exited $($process.ExitCode), expected $ExpectedExit. See $install/updater_error.log" }
    Assert-Content (Join-Path $install User/profile.rs) 'my profile'
    Assert-Content (Join-Path $install Data/custom.dll) 'custom plugin'
    if ($ExpectedExit -eq 0) {
        Assert-Content (Join-Path $install RSBot.exe) 'new exe'
        Assert-Content (Join-Path $install RSBot.Updater.exe) 'new updater'
        $backup = @(Get-ChildItem (Join-Path $install update_temp) -Directory -Filter 'backup-*')
        if ($backup.Count -ne 1) { throw 'Expected one backup directory' }
        Assert-Content (Join-Path $backup[0].FullName RSBot.exe) 'old exe'
        if (Test-Path (Join-Path $install update_temp/update.zip)) { throw 'Successful update retained ZIP' }
    } else {
        Assert-Content (Join-Path $install RSBot.exe) 'old exe'
        Assert-Content (Join-Path $install RSBot.dll) 'old dll'
        if (Test-Path (Join-Path $install RSBot.runtimeconfig.json)) { throw 'Failed update left a new application file' }
        if (-not (Test-Path (Join-Path $install updater_error.log))) { throw 'Failure did not write a log' }
    }
    Write-Host "PASS: $Name"
}
try {
    $valid = [ordered]@{
        'RSBot.exe' = 'new exe'
        'RSBot.dll' = 'new dll'
        'RSBot.runtimeconfig.json' = '{}'
        'RSBot.Updater.exe' = 'new updater'
        'User/profile.rs' = 'overwrite attempt'
        'Logs/test.log' = 'packaged log'
    }
    Invoke-Case 'successful update' $valid 0
    if (Test-Path (Join-Path $testRoot 'successful update/Logs/test.log')) { throw 'Packaged logs were installed' }
    Invoke-Case 'invalid package' @{ 'unrelated.txt' = 'bad package' } 1
    $traversal = [ordered]@{}
    foreach ($key in $valid.Keys) { $traversal[$key] = $valid[$key] }
    $traversal['../../escaped.txt'] = 'escape attempt'
    Invoke-Case 'archive traversal' $traversal 1
    if (Test-Path (Join-Path $testRoot 'archive traversal/escaped.txt')) { throw 'Archive escaped staging' }
    $blocked = [ordered]@{}
    foreach ($key in $valid.Keys) { $blocked[$key] = $valid[$key] }
    $blocked['Data/blocked.dll'] = 'copy failure'
    Invoke-Case 'rollback on copy failure' $blocked 1 -BlockCopy
    Write-Host 'All updater checks passed.'
} finally {
    Remove-Item $testRoot -Recurse -Force
}
