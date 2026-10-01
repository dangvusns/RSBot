# Prepares this PC to run RSBot and RSBot.Manager from this folder:
# - installs the .NET 8 Desktop Runtime (x86) and the Visual C++ Redistributable (x86) when they are missing
# - unblocks the files of the downloaded zip, so Windows lets RSBot load its plugins
# - creates a desktop shortcut to RSBot.Manager.exe
#
# Usage: double-click Install.cmd, or run
#   powershell.exe -ExecutionPolicy Bypass -File .\install.ps1 [-NoShortcut]

param(
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"
$folder = Split-Path -Parent $MyInvocation.MyCommand.Path
$rebootNeeded = $false

$dependencies = @(
    @{
        Name = ".NET 8 Desktop Runtime (x86)"
        Url  = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe"
        File = "windowsdesktop-runtime-8-win-x86.exe"
        Test = {
            # RSBot is 32-bit, so it needs the x86 runtime. On 64-bit Windows it lives in "Program Files (x86)"
            $root = if ([Environment]::Is64BitOperatingSystem) { ${env:ProgramFiles(x86)} } else { $env:ProgramFiles }
            $shared = Join-Path $root "dotnet\shared\Microsoft.WindowsDesktop.App"
            (Test-Path $shared) -and @(Get-ChildItem $shared -Directory | Where-Object Name -like "8.*").Count -gt 0
        }
    },
    @{
        Name = "Visual C++ Redistributable 2015-2022 (x86)"
        Url  = "https://aka.ms/vs/17/release/vc_redist.x86.exe"
        File = "vc_redist.x86.exe"
        Test = {
            $keys = @(
                "HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X86",
                "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X86"
            )
            @($keys | Where-Object { (Get-ItemProperty $_ -ErrorAction SilentlyContinue).Installed -eq 1 }).Count -gt 0
        }
    }
)

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Install-Dependency($dependency) {
    $installer = Join-Path $env:TEMP $dependency.File

    Write-Host "Downloading $($dependency.Name)..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = "SilentlyContinue" # the progress bar makes Invoke-WebRequest very slow
    Invoke-WebRequest -Uri $dependency.Url -OutFile $installer -UseBasicParsing

    Write-Host "Installing $($dependency.Name)..."
    $process = Start-Process -FilePath $installer -ArgumentList "/install", "/quiet", "/norestart" -Wait -PassThru
    Remove-Item $installer -ErrorAction SilentlyContinue

    # 1638: a newer version is already installed, 3010: installed, restart needed
    switch ($process.ExitCode) {
        0 { }
        1638 { }
        3010 { $script:rebootNeeded = $true }
        default { throw "Installing $($dependency.Name) failed with exit code $($process.ExitCode)." }
    }

    Write-Host "$($dependency.Name) installed." -ForegroundColor Green
}

try {
    Write-Host "Checking dependencies..."
    $missing = @($dependencies | Where-Object { -not (& $_.Test) })

    foreach ($dependency in $dependencies) {
        $state = if ($missing -contains $dependency) { "missing" } else { "OK" }
        Write-Host "  $($dependency.Name): $state"
    }

    if ($missing.Count -gt 0 -and -not (Test-Administrator)) {
        # Installing needs administrator rights: run this script again elevated and stop here
        Write-Host "Administrator rights are needed to install the missing dependencies, asking Windows..."
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
        if ($NoShortcut) { $arguments += " -NoShortcut" }

        Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait
        exit
    }

    foreach ($dependency in $missing) {
        Install-Dependency $dependency
    }

    Write-Host "Unblocking the files of this folder..."
    Get-ChildItem $folder -Recurse -File | Unblock-File

    if (-not $NoShortcut) {
        $target = Join-Path $folder "RSBot.Manager.exe"
        $shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "RSBot Manager.lnk"

        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $target
        $shortcut.WorkingDirectory = $folder
        $shortcut.IconLocation = "$target,0"
        $shortcut.Save()

        Write-Host "Created the desktop shortcut 'RSBot Manager'."
    }

    Write-Host ""
    Write-Host "Done. Start RSBot Manager from the desktop shortcut or RSBot.Manager.exe." -ForegroundColor Green
    if ($rebootNeeded) {
        Write-Host "Please restart Windows to finish installing the dependencies." -ForegroundColor Yellow
    }
}
catch {
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Check the internet connection, or install the dependency by hand from the links in README.txt."
}

Read-Host "Press Enter to close"
