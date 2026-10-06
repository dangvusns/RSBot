# Usage:
# `build.ps1
# -Clean[False, optional]
# -DoNotStart[False, optional]
# -Configuration[Debug, optional]
# -Version[major.minor.patch, optional]`

param(
    [string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$DoNotStart,
    [string]$Version
)

Set-Location $PSScriptRoot

if (-not (Test-Path ".\SDUI\SDUI\SDUI.csproj")) {
    Write-Output "SDUI submodule is missing. Initializing and updating submodules..."
    git submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw "Could not initialize SDUI." }
}

taskkill /F /IM RSBot.exe
taskkill /F /IM RSBot.Manager.exe
taskkill /F /IM sro_client.exe

if ($Clean) {
    Write-Output "Performing a clean build..."
    New-Item  -ItemType Directory ".\temp" -ErrorAction SilentlyContinue > $null
    Move-Item ".\Build\User" ".\temp" -ErrorAction SilentlyContinue > $null
    Remove-Item -Recurse -Force ".\Build" -ErrorAction SilentlyContinue > $null
}

Write-Output "Building with '$Configuration' configuration..."
$vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
$msBuildPath = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
# /restore: new projects get their NuGet assets (otherwise MSBuild fails with NETSDK1004)
$buildArguments = @('/restore', "/p:Configuration=$Configuration", '/p:Platform=x86', 'RSBot.sln')
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must be major.minor.patch."
    }
    $buildArguments += "/p:AssemblyVersion=$Version"
    $buildArguments += "/p:FileVersion=$Version"
    $buildArguments += "/p:InformationalVersion=$Version"
}
& $msBuildPath @buildArguments > build.log
$buildExitCode = $LASTEXITCODE
Write-Output "NOTE: This is a truncated view of the build logs. For the full log, refer to .\build.log"
Get-Content -Path "build.log" -Tail 100

if ($Clean) {
    Move-Item ".\temp\User" ".\Build\User" -ErrorAction SilentlyContinue > $null
    Remove-Item -Recurse -Force ".\temp" -ErrorAction SilentlyContinue > $null
}

if ($buildExitCode -ne 0) {
    throw "MSBuild failed with exit code $buildExitCode. See build.log."
}

if (!$DoNotStart) {
    Write-Output "Starting RSBot..."
    & ".\Build\RSBot.exe"
}