param(
    [string]$PythonVersion = "3.13.11",
    [string]$DestinationPath
)

$ErrorActionPreference = "Stop"
$pythonExe = Join-Path $DestinationPath "python.exe"
$zipUrl = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-win32.zip"
$zipPath = Join-Path $DestinationPath "python_temp.zip"

if (Test-Path $pythonExe) {
    Write-Host "[Python-Build] Python $PythonVersion already installed at $DestinationPath"
} else {
    Write-Host "[Python-Build] Python not found. Installing to $DestinationPath..."
    
    if (-not (Test-Path $DestinationPath)) {
        New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null
    }

    try {
        Write-Host "[Python-Build] Downloading from $zipUrl..."
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
        
        Write-Host "[Python-Build] Extracting..."
        Expand-Archive -Path $zipPath -DestinationPath $DestinationPath -Force
        
        Write-Host "[Python-Build] Extraction complete."
    } catch {
        Write-Warning "[Python-Build] Failed to download or extract Python runtime: $($_.Exception.Message)"
        Write-Warning "[Python-Build] The build will continue, but Python features may be unavailable."
    } finally {
        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }
    }
}

# Post-install configuration
if (Test-Path $pythonExe) {
    $pthFiles = Get-ChildItem (Join-Path $DestinationPath "*._pth")
    foreach ($pth in $pthFiles) {
        Write-Host "[Python-Build] Enabling site-packages in $($pth.Name)..."
        (Get-Content $pth.FullName) -replace '#import site', 'import site' | Set-Content $pth.FullName
    }
}

# Always exit with 0 to allow the build to proceed even if the download failed
exit 0
