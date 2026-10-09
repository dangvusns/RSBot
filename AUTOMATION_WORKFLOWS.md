# Automation workflow changes

Existing profiles retain their shared pickup rules and town services. New pickup rules and skipping town services are opt-in. Inventory organization is an explicit action. No dependencies, project files, Designer files, or SDUI files were changed.

## Controls

- **Items → Pet and character:** enable separate categories, choose pet/character categories, pause only character pickup, configure fallback when the pet is unavailable, and set pet radius. Radius 0 uses the existing training-area range; explicit values are limited to 100. When both collectors allow an item, the active pet has priority within its range. Existing character-only item filters still take precedence.
- **Inventory → Organize:** stop the bot, tracing and other inventory activity first. Organization merges stacks and groups supplies, equipment, quest items and pets. It uses the client's normal inventory boundary (13 or 17). Interrupted organization retains already-confirmed moves; it does not roll them back.
- **Training → Back to training → Skip town NPCs:** skips purchases, storage and repairs. The existing stop-on-return setting retains priority. Reverse return is attempted when enabled, with normal walkback used if unavailable or refused.

## Automatic behavior

- Confirmed obstacle reports trigger up to three movement attempts over six seconds, followed by a cooldown. Escape candidates must be inside the training area and pass resolved movement raycasts. Healing, resurrection and casting retain priority. Slow damage alone does not trigger recovery.
- Generated navigation still prefers the existing graph. A local outdoor NavMesh search supplies a route when the graph has none, or detours around a resolved blocked segment. Local searches are bounded by 4,096 nodes, two seconds and 1,000 units between endpoints. Recorded routes and graph teleports retain their existing execution path. Missing NavMesh data does not manufacture a local route.
- Recovery, inventory organization, town services and walkback report progress or interruption through the existing status/log UI. Failed route generation and incomplete walkback wait five seconds before another loop attempt.

## Validation

Added xUnit coverage for recovery timing/cooldown and tick wrap, pickup categories, organization categories and inventory changes, client inventory boundaries, and local route obstacles/cancellation/search limits. These tests require Windows .NET 8 and the existing MSBuild test project.

This Linux session has no Windows PowerShell/MSBuild runtime. Static review and `git diff --check` passed; compilation, test execution, DPI rendering and live server behavior remain unverified.

Build from Windows PowerShell in this repository:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug -DoNotStart
```

The build script terminates RSBot, Manager and sro_client processes before building. Run it when those sessions can be stopped. Do not use `-Clean` for this validation.

Then run the existing test project through Visual Studio Test Explorer, or MSBuild:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" .\Tests\RSBot.Core.Tests\RSBot.Core.Tests.csproj /restore /t:VSTest /p:Configuration=Debug /p:Platform=x86
```

Check these live scenarios before unattended use:

1. Keep a legacy profile unchanged: shared pickup, town purchases/storage/repair and normal stack sorting should behave as before.
2. Block a target behind an obstacle: recovery is bounded, stays inside the area, and yields to healing/resurrection. Repeat with a slow fight to verify there is no damage-time trigger.
3. Enable separate pickup: give the pet equipment and the character quest items; overlap categories; remove the pet; toggle fallback and character-only pause. Stop, disconnect and reconnect during pickup.
4. Organize partial stacks and a full inventory on slot-13 and slot-17 clients. Equipment/relic slots must remain untouched. Try organization while tracing, casting or exchanging; start the bot or change inventory while it runs. It must refuse or stop without issuing the next stale move.
5. Exercise a valid graph route, a blocked segment, a missing/malformed graph, unavailable NavMesh data, a teleport route and a recorded route. Stop during local search. A failed search must report failure and avoid a partial route.
6. Enable skip-town-NPCs with stop-on-return both on and off, with and without a usable reverse scroll. Verify that skipped services really perform no buying, storing or repairing.
7. Inspect the new controls at 100%, 125% and 175% DPI, in English and Vietnamese, and resize the window. Verify keyboard access and scrolling.
