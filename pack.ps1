# Builds RSBot and RSBot.Manager and packs them into a zip that can be installed on another PC.
#
# Usage:
# `pack.ps1
# -Configuration[Release, optional]
# -Output[.\RSBot-Manager.zip, optional]
# -SkipBuild[False, optional] (pack the current .\Build folder as it is)`
#
# The zip leaves out .\Build\User (profiles, accounts and passwords) and contains Install.cmd,
# which installs the missing dependencies on the target PC.

param(
    [string]$Configuration = "Release",
    [string]$Output = ".\RSBot-Manager.zip",
    [switch]$SkipBuild
)

Set-Location $PSScriptRoot

if (!$SkipBuild) {
    & .\build.ps1 -Clean -Configuration $Configuration -DoNotStart

    if (!(Select-String -Path ".\build.log" -Pattern "Build succeeded" -Quiet)) {
        Write-Output "The build failed, see .\build.log. Nothing was packed."
        exit 1
    }

    # The languages and town scripts are kept in git under Build\Data. The clean build deletes the
    # whole Build folder and nothing copies them back, so restore them from git
    git checkout -- Build/Data
}

# RSBot does not start without them ("Language list file missing")
foreach ($required in @(".\Build\Data\Languages\langs.rsl", ".\Build\Data\Scripts")) {
    if (!(Test-Path $required)) {
        Write-Output "$required is missing. Restore it with: git checkout -- Build/Data"
        exit 1
    }
}

# Set after the build: build.ps1 runs taskkill, which reports an error when nothing is running
$ErrorActionPreference = "Stop"

if (!(Test-Path ".\Build\RSBot.Manager.exe")) {
    Write-Output "Build\RSBot.Manager.exe is missing. Build first or run without -SkipBuild."
    exit 1
}

$staging = Join-Path $env:TEMP "RSBot-pack"
Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
New-Item -ItemType Directory $staging > $null

Write-Output "Copying the build without the User folder..."
Get-ChildItem ".\Build" -Exclude "User" | Copy-Item -Destination $staging -Recurse
Copy-Item ".\Installer\*" -Destination $staging

Write-Output "Creating $Output..."
Remove-Item $Output -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $Output

Remove-Item -Recurse -Force $staging

Write-Output "Done: $((Resolve-Path $Output).Path)"
Write-Output "On the other PC: unzip it and double-click Install.cmd."
