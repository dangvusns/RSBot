# Hyperbot-inspired batch 1: validation handoff

Source changes implement callback terminal states/cleanup, confirmed purchase and sale results,
bounded shopping progress, independent buyback snapshots, and accepted per-encounter openers.
ManagerLink reports pending callback count and shopping outcome/error/reconciliation status.
Existing void shopping APIs remain available to plugins.

No Windows build, test execution, gameplay run or measured performance result is available from
the Linux workspace. `git diff --check` is a source hygiene check, not compilation.

## Windows build and automated checks

Run in a Windows copy of this checkout with Visual Studio 2022 and the existing dependencies:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug -DoNotStart
```

The existing build script stops RSBot, its manager and game clients. Save any running session
before invoking it. Review `build.log` and run the four new test classes in Visual Studio Test
Explorer: AwaitCallbackTests, ShopOperationTests, BuybackItemsTests, EncounterOpenersTests.
The tests use the repository's existing xUnit project and require no project-file edits.

## Live checks before release

- Buy and sell player items and transport goods. Confirm actual inventory quantities match
  town/trade progress, including partial sales and packaged purchases.
- Refuse a purchase (insufficient gold/full inventory), disconnect during a purchase, and Stop
  during a pending operation. Verify the workflow terminates and pending callbacks disappear.
- While shopping waits, manually move an item. The automatic operation must cancel; a later
  response may update inventory but must not trigger another automatic purchase.
- Confirm full buyback and server-defined index shifting; inspect partial buyback captures
  before relying on partial withdrawals on a particular server variant. Unknown slots and
  excessive quantities must leave known buyback state intact.
- Refuse an opener then retry after backoff. Accept an opener on a giant, interrupt to fight a
  smaller attacker, and return: the accepted opener should not repeat. Check death/despawn,
  teleport and reconnect clear the history.

Inventory error packets do not identify the request. After a timeout, cancellation or
unconfirmed response, automatic shopping pauses until reconnect; there is no automatic retry
or claim that the server rejected the operation. Manual inventory remains authoritative.
Repair, storage, pet transfer and full combat coordination are later batches.

## Resource baseline

After a Release build, warm up for five minutes, then sample identical 1/4/8-bot scenarios:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\measure-resources.ps1 -Scenario clientless-training-4 -Seconds 900 -Output training-4.csv
```

Repeat with idle, visible map/log UI, party training and townloop/reconnect. Record commit,
machine, client version, plugins, log level and packet capture settings alongside the CSV.
CPU percentage uses the whole machine as 100%; the first sample has no CPU percentage.
Private bytes and working set are separate measurements; summed working sets include shared
pages. `Responding` is a coarse OS check, not click latency or a UI heartbeat measurement.
Compare against the preceding commit using the same workload. The collector does not measure
GC allocations, packet latency or kills/hour; no performance gain is asserted for this batch.

Next release gate: pass Windows build/tests and these live checks before migrating additional
operation types or changing UI scheduling, combat coordination and navigation.
