# Batch 2: cast acceptance and cancellation

This is the next increment of Stage C, not a complete combat coordinator.

- All SkillManager cast entry points share nonblocking local ownership before weapon checks.
  Competing buff calls return Busy; competing attack calls return false; position APIs retain
  their existing void signatures. Cancellation/dispel packets may still bypass the guard.
- Buff acceptance uses the same B070 header decoder as the gameplay handler and checks
  skill, captured caster and explicit recipient. No-target casts match skill/caster only.
- B074 can satisfy a buff's existing action-state wait only after matching acceptance.
  Accepted means the server started the cast, not that its effect appeared or its chain ended.
- Stop, death, teleport and disconnect directly cancel owned callbacks and waits. The legacy
  attack duration wait is cancellable, and position casts release unused callback registrations.
  A competing outgoing manual action cancels the owned request without clearing opener history.
- Cast executor lookup checks expiry even below the pruning threshold. Session resets clear
  executor history and refusal counters; ordinary target interruption preserves opener history.
- ManagerLink adds casting (local API ownership) and lastBuffOutcome. Existing enum numeric
  values are preserved, with Cancelled and Busy appended.

Anonymous B070 errors produce Unconfirmed rather than claiming an identifiable refusal.
The existing refusal/cooldown backoff handler still runs. Serialization cannot distinguish a
late reply for the same skill/recipient after an earlier request timed out. Full wire-level
reconciliation, effect tracking, chain completion and movement/inventory coordination remain
later work requiring captures; this batch does not claim to solve them.

## Windows validation

No Windows build or test execution was possible in the Linux workspace. Source hygiene alone
does not establish compilation or correctness.

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug -DoNotStart
```

The existing script stops RSBot, manager and game client processes. Review build.log, then
run CastLifecycleTests and the previous batch's four test classes in Visual Studio Test Explorer.
Tests cover fixed synthetic client layouts, wrong skill/caster/recipient, anonymous errors,
ownership reentry, cancellation, exception cleanup, expired attribution and session reset.
Synthetic payloads preserve the existing parser branches; they are not captured protocol proof.

Live checks before release:

1. Exercise self buff, party buff, healing, resurrection and imbue with required weapon swaps.
   Check that Busy/Cancelled/Unconfirmed results do not increment accepted party-buff attempts.
2. Trigger a competing Python/script cast while a buff awaits acknowledgement. It must not
   change equipment or send another cast through SkillManager until the owner releases.
3. Stop during cast acceptance, action-state wait and legacy duration wait. Check prompt local
   cancellation, callback cleanup and no subsequent shield/cast send from that operation.
4. Teleport, die and reconnect during those waits. Verify old cast history is gone and new
   session casts work; compare pending callback count before/after repeated cycles.
5. Manually cast/cancel during a buff wait. Manual actions remain possible and invalidate the
   automated wait. Interrupt a giant encounter, then resume: its accepted opener stays used.
6. Check ordinary attacks, lower-type interruption, area teleport casts, buffs during walkback,
   and existing resurrection priority. Inspect delayed/reordered B070/B074 capture behavior.

Use the batch-1 resource sampler with identical workloads. No CPU/RAM or gameplay-rate
improvement is asserted until Windows measurements and these checks pass.
