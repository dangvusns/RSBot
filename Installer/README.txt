RSBot + RSBot Manager
=====================

Install
-------
1. Unzip this folder where you want to keep the bot, for example D:\SRO\RSBot.
2. Double-click Install.cmd. It:
   - checks for the .NET 8 Desktop Runtime (x86) and the Visual C++ Redistributable (x86)
     and downloads and installs the ones that are missing (Windows asks for administrator rights),
   - unblocks the unzipped files,
   - creates the "RSBot Manager" desktop shortcut.
3. Start RSBot Manager. If it does not find the bot folder, click "Chon thu muc bot" and pick this folder.

First use
---------
1. Open RSBot.exe once and set up a profile to use as a template: Silkroad folder, skills, items, training area.
2. In RSBot Manager, add each account with "+ Them" and pick that profile as "Profile mau".

Installing the dependencies by hand
-----------------------------------
If Install.cmd cannot download them (no internet, proxy):
- .NET 8 Desktop Runtime x86:  https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe
- Visual C++ Redistributable x86: https://aka.ms/vs/17/release/vc_redist.x86.exe
