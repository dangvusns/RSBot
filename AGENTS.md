# RSBot

This repository contains an automation software (bot) for the game "Silkroad Online".

## Build system

The project is built using `MSBuild`.

- Generic path: `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`.
- Debug flags: `/p:Configuration=Debug /p:Platform=x86`
- Release flags: `/p:Configuration=Release /p:Platform=x86`
- Solution to build: `.\RSBot.sln`

- Use `.\build.ps1` to take builds.
- For execution policy issues use with `powershell.exe -ExecutionPolicy Bypass .\build.ps1`

- There are arguments that can be passed to the build script, `-Clean` for a clean build, `-Configuration` for the intended build configuration and `-DoNotStart` for not starting the main application.
- If you fail to make a build ask the user to build it. Check the `.\build.log` for any errors after user confirms the build.

## Instructions

- Never edit `.sln`, `.csproj`, `.Designer.cs` and other auto-generated files without explicit confirmation from the user.
- Never use `dotnet` for builds. Only for package management purposes via `dotnet restore`, `dotnet add` etc.

## Project information

Project has a modular structure.

| Name             | Directory                        | Description                                                          |
| ---------------- | -------------------------------- | -------------------------------------------------------------------- |
| `RSBot`          | `.\Application\RSBot`            | Main application                                                     |
| `Manager`        | `.\Application\RSBot.Manager`    | Multi-account manager, controls bots via the `ManagerLink` pipe      |
| `Alchemy`        | `.\Botbases\RSBot.Alchemy`       | Handles in-game alchemy tasks                                        |
| `Lure`           | `.\Botbases\RSBot.Lure`          | Manages luring monsters                                              |
| `Trade`          | `.\Botbases\RSBot.Trade`         | Automates trading activities                                         |
| `Training`       | `.\Botbases\RSBot.Training`      | Default botbase, controls the character's training area and behavior |
| `Languages`      | `.\Build\Data\Languages`         | Translations                                                         |
| `Scripts`        | `.\Build\Data\Scripts`           | Contains the scripts used for townloops                              |
| `Docs`           | `.\docs`                         | Documentation for the bot that is hosted via `github.io`             |
| `Core`           | `.\Library\RSBot.Core`           | Core functionalities of the bot                                      |
| `FileSystem`     | `.\Library\RSBot.FileSystem`     | Manages file system operations                                       |
| `Loader.Library` | `.\Library\RSBot.Loader.Library` | A C++ library for loading the bot (x86)                              |
| `NavMeshApi`     | `.\Library\RSBot.NavMeshApi`     | Provides navigation mesh functionalities                             |
| `Chat`           | `.\Plugins\RSBot.Chat`           | Manages in-game chat                                                 |
| `CommandCenter`  | `.\Plugins\RSBot.CommandCenter`  | A central command center for the bot                                 |
| `General`        | `.\Plugins\RSBot.General`        | General purpose functionalities                                      |
| `Inventory`      | `.\Plugins\RSBot.Inventory`      | Manages the character's inventory                                    |
| `Items`          | `.\Plugins\RSBot.Items`          | Handles item-related operations                                      |
| `Log`            | `.\Plugins\RSBot.Log`            | Provides logging capabilities                                        |
| `ManagerLink`    | `.\Plugins\RSBot.ManagerLink`    | Named-pipe endpoint used by `RSBot.Manager`                          |
| `Map`            | `.\Plugins\RSBot.Map`            | Displays the in-game map and related information                     |
| `PacketAnalyzer` | `.\Plugins\RSBot.PacketAnalyzer` | Captures, filters, injects and records packets                       |
| `Party`          | `.\Plugins\RSBot.Party`          | Manages party-related activities                                     |
| `Protection`     | `.\Plugins\RSBot.Protection`     | Implements protection mechanisms                                     |
| `Python`         | `.\Plugins\RSBot.Python`         | Hosts Python plugin scripts (pythonnet, embedded Python)             |
| `Quest`          | `.\Plugins\RSBot.Quest`          | Automates questing                                                   |
| `ServerInfo`     | `.\Plugins\RSBot.ServerInfo`     | Retrieves and displays server information                            |
| `Skills`         | `.\Plugins\RSBot.Skills`         | Manages character skills                                             |
| `Social`         | `.\Plugins\RSBot.Social`         | Nearby players, guild members and automated exchanges                |
| `Statistics`     | `.\Plugins\RSBot.Statistics`     | Collects and displays statistics                                     |
| `Tests`          | `.\Tests\RSBot.Core.Tests`       | xUnit tests for `RSBot.Core` logic                                   |
| `SDUI`           | `.\SDUI`                         | User interface library                                               |

UI is handled by the submodule `.\SDUI`. If you encounter any issues where it doesn't exist, the build script should automatically resolve it.

### Build structure

| Name        | Directory           | Description                                                         |
| ----------- | ------------------- | ------------------------------------------------------------------- |
| `RSBot.exe` | `.\Build\RSBot.exe` | Main application executable                                         |
| `Data`      | `.\Build\Data`      | Where dependencies, botbases, plugins, townscripts etc. goes        |
| `User`      | `.\Build\User`      | Autologin info, user configs and available profiles                 |
| `Logs`      | `.\Build\User\Logs` | Logs for each character and environment stored in a ISO dated file. |
