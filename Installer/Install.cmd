@echo off
rem Checks and installs what RSBot needs, then creates the RSBot Manager desktop shortcut.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
