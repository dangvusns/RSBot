# RSBot implementation plan from the Hyperbot comparison

Date: 2026-10-07. RSBot baseline: `f112f775`; Hyperbot reference: `6c7e188`.
Planning and static source review only. No application code, project files or generated files changed by this task. No build, performance benchmark or gameplay validation was performed.

## Direction and priorities

Keep RSBot's current gameplay rules and modular architecture. Improve how actions are confirmed, coordinated, cancelled and observed. Then add better local navigation, richer rules and optional coordination through the existing manager.

Hyperbot provides useful operation composition, pending-command tracking, skill lifecycle and observation patterns. It is not a proven replacement: its normal navmesh loading is disabled, much of its statistics recorder is commented out, and the current entry point initializes an RL training system. Its UI also has blocking request acknowledgements. Copying its architecture does not establish lower CPU/RAM or higher kill rates.

| Stage | Priority | Result | Depends on | Relative scope |
|---|---|---|---|---|
| A | P0 | Measured baseline and reproducible failures | Current source | Small–medium |
| B | P0 | Confirmed, cancellable inventory operations and correct buyback | A's initial fixtures | Medium |
| C | P0 | Complete cast lifecycle and encounter state | Small operation core from B | Large; split by caller |
| D | P1 | Async UI workflows and explanations in manager | B/C snapshots; UI audit can start during A | Medium |
| E | P1 | Measured reductions in CPU and memory | A metrics; B/C for event migration | Medium–large |
| F | P1 | Obstacle-aware return-to-area and bounded movement recovery | Operation lifecycle and navmesh spike | Large |
| G | P2 | Monster rules, buff policy, supply rules and better scripts | B/C; F where movement is needed | Medium per feature |
| H | P2 | Historical statistics and party coordination | Stable D status protocol; B/C | Medium–large |
| I | P3 | Offline combat-policy experiments | Validated captures and observations | Research; optional |

Scope labels compare work size, not calendar commitments. Treat each stage as several reviewable changes with an independent release gate. Correctness fixes in B should not wait for the full benchmark matrix in A; collect the baseline before measuring optimization gains.

## What already exists

Do not schedule these as net-new implementations:

- Lower-type attacker interruption/resumption, leader assist, anti-KS filtering, obstacle blacklists and strict training-area recovery.
- Monster-type skill lists, opening skills, ordered rotation, knockdown-specific attacks and range-based selection.
- Cast acceptance results, cooldown/refusal backoff and recent cast-instance-to-executor correlation.
- Independent buff application copies, conditional instant party skills, healing thresholds and resurrection priority.
- Manager/ManagerLink, clientless mode, smart tracing, automatic graph-based walkscripts, navmesh raycasts and packet recording.
- The latest performance commit: copy-on-write event lookup, background batched file logging, bounded log display and packet recording, map/navmesh cache limits and several refresh/allocation reductions.

These are present in source. Previous backlog text includes older implementation/build status; it is not proof that current Windows builds and gameplay checks passed. Validate what exists before changing its behavior.

## Stage A — establish a usable baseline

### A1. Benchmark the current version

Use the normal Windows build script and a release build for resource comparisons. Keep a debug build for diagnosis. Record commit, configuration, client version, plugins, machine, logging level and capture settings.

Benchmark 1, 4 and 8 bots in these scenarios:

1. Logged in and idle, bot windows minimized.
2. Clientless training with ordinary mobs.
3. Clientless party training with healing, buffs and resurrection.
4. Visible bot UI with map/log/skills activity.
5. Townloop, reconnect and map-region changes.

Run game-client mode as a separate matrix. Report manager + bot processes separately from game clients, then show the combined cost. Keep accounts, routes, plugin settings and capture volume comparable. Warm up for about five minutes, collect 15–30 minutes per scenario, and repeat key scenarios three times. Add a two-hour soak for the selected configuration, with reconnects and repeated region/UI changes.

Collect:

- CPU seconds and average/peak usage for the process group; report logical processor count and whether 100% means one core or the whole machine.
- Private bytes, working set, managed allocations/GC activity, thread count, handles and GDI objects. Private bytes indicates private committed memory; working set includes resident/shared pages and cannot be interpreted as unique memory simply by summing processes.
- Packet rate, pending callback count, queued gameplay work, queue high-water marks and dropped log/capture records.
- Bot decision latency, packet-processing latency, cast acceptance/completion time, movement acknowledgement time and Stop latency.
- UI heartbeat delay and click-to-feedback latency; measure p50/p95/p99 and the worst stall.
- Kills/hour, deaths, confirmed buff coverage, failed operations and town time. Live game rates vary with monsters, other players and server conditions; use offline fixtures for deterministic correctness comparisons.

Do not promise a percentage saving before measuring. Proposed initial usability targets: ordinary button feedback within 100 ms at p95; no unexplained UI freeze over 500 ms; Stop cancels local pending work within 250 ms at p95. These are engineering targets to calibrate on the user's machine, not current results or promises of server acknowledgement timing.

### A2. Make failures reproducible

Reuse PacketAnalyzer captures and the existing test infrastructure. Add a small fixture runner with a fake monotonic clock, isolated session state and an output that records requested actions without sending them to a server. Extract only combat/inventory decoders needed for the first changes; do not rewrite every handler.

Initial fixtures: unrelated inventory response during purchase; rejection/timeout/late acknowledgement; partial sale and buyback; unknown AoE target before known target; chained skill completion; knockback; despawn during target selection; Stop/disconnect during a pending action. Preserve client-specific parsing branches. Store only the relevant packet payloads and remove authentication material from reusable fixtures.

**Gate:** a repeatable benchmark record exists, the first failing cases can be reproduced, and the instrumentation has measurable overhead. Default diagnostics must be lightweight; heavy packet capture is opt-in.

## Stage B — confirmed inventory operations

### B1. A small operation core

Create a shared operation context/result, then migrate shopping first. Do not add a second bot engine. A context records session generation, local operation ID, actor/inventory, operation kind, expected item/slots/quantity, send time, deadline, attempt count and raw server error.

Lifecycle: requested → sent → confirmed / rejected / timed out / cancelled. Completion changes exactly once. Cancellation, timeout and acknowledgement compete through one atomic completion path. Unsubscribe/remove callbacks at completion even if no more packets arrive. The existing async callback method catches cancellation without closing the callback state; this needs explicit lifecycle handling rather than assuming async conversion alone solves cancellation.

Subscribe before sending; report failure to send rather than silently waiting. Decode a response without consuming the shared reader state used by another handler. Match only fields actually provided by that client. A local operation ID does not appear on the wire and cannot correlate a response by itself.

Serialize ambiguous inventory mutations, including shopping, stacking, storage, pet transfers and weapon swaps that share the same response channel. Use finer concurrency only after protocol fixtures prove it is safe. Manual client moves remain legitimate authoritative changes: detect conflicts, invalidate/reconcile the planned step and report the reason rather than blindly blocking manual control.

### B2. Migrate shopping and bounded town workflows

- Add result-bearing purchase/sell/move/repair operations and update callers incrementally.
- Replace opcode-only success with operation/result matching and authoritative inventory confirmation.
- Preserve existing trade quantity correction; extend it with explicit rejection and timeout outcomes.
- Retry only when the failure is known and retryable. An ambiguous timeout may mean the operation succeeded; reconcile before resending to avoid duplicate purchases/sales.
- Propagate cancellation through NPC selection, buying, merging, storage, repair and script waits. Close an NPC dialog when appropriate, with a bounded attempt.
- Do not report a successful sale until the server confirms it. Emit one useful failure reason and bounded retries.

### B3. Correct buyback and expose recent sales later

Separate the sold item/quantity from the retained stack. InventoryItem currently offers a shallow clone; ensure mutable nested fields are not incorrectly shared when copying. Handle partial withdrawals only for variants where captures establish support. Preserve server-defined slot/index/queue behavior. Unknown or expired buyback slots must fail safely and leave inventory intact.

After the model is correct, add a Recent Sales view with quantities and availability, then an explicit user buyback action using the operation service. Do not automatically reverse intentional configured sales.

**Gate:** unrelated `0xB034` cannot complete a purchase; rejection retains its raw code; cancelled callbacks are removed; late responses update authoritative state without completing a new operation incorrectly; partial sale/buyback counts are correct; town scripts terminate cleanly after failure/Stop. Build and check ordinary player/transport shopping on Windows.

Touchpoints: `AwaitCallback`, `PacketManager`, `ShoppingManager`, `InventoryOperationResponse`, trade purchase command, script commands and pet-transfer code.

## Stage C — coordinated skills, buffs and combat

### C1. Complete the cast lifecycle

Extend the current cast tracking with skill reference/root, instance ID, actor, target, acceptance deadline, completion deadline and outcome. Keep learned skill/cooldown data separate from active buff applications and in-flight requests.

Distinguish server acceptance from full completion, effect appearance, refusal, interruption and silence. Use skill-instance correlation and validated chain semantics; skill-end packets can contain intermediate hits and must not automatically mean the complete chain ended. Clear session-owned state on disconnect/death/profile replacement. Bound and expire tracking tables using monotonic time.

Where a refusal lacks identifying fields, only attribute it under proven ordering rules with one outstanding ambiguous action. A local generation rejects stale timers but cannot, alone, identify late wire acknowledgements. After cancellation/timeout, reconcile the actor's state before giving a new action ownership of that channel.

### C2. One coordinator for conflicting actions

Bundles retain their gameplay decisions but submit intents to a small coordinator. Model action dependencies such as confirmed weapon swap → required imbue → accepted cast → completion/effect. Avoid holding locks across awaits or sending from a state-event worker that must process the acknowledgement itself.

Define resource ownership explicitly: movement, selected target, casting/equipment and inventory mutations. Compatible background observation and UI work can proceed concurrently. Do not serialize everything behind one slow action.

Preserve this initial policy: Stop/death/disconnect wins; strict boundary recovery rejects out-of-area intents; emergency self-protection takes precedence; existing resurrection-before-routine-buff behavior remains; configured healing precedes routine buffs/combat as today. Lower-type attacker interruption still overrides continuing a stronger encounter at a supported cancellation boundary. Starvation prevention lets ordinary attacks proceed when no higher-priority need remains. Any change to these gameplay priorities needs a clear before/after example and live validation.

Adopt scoped cancellation/subscription ownership from Hyperbot's child-state pattern, with explicit results instead of Hyperbot's `Done/NotDone`. UI shows proposed user-facing action names, not C# class names.

### C3. Fix opener bookkeeping and strengthen eligibility

Current `GetOpener` marks an opener used while selecting it. Instead, mark it after a matching accepted cast. Keep bounded per-encounter state through target interruption/resumption; clear it on death/despawn/session reset. Confirmed opener does not repeat when returning to the same giant; refused opener remains eligible subject to backoff.

Centralize resource/state checks: supported absolute/percentage MP and HP costs, death/stun/knockdown, required equipment, target type, cooldown, pending cast and buff conflicts. Derive game-specific cost reductions and overlap rules from RSBot reference data plus captures; do not transfer Hyperbot formulas unverified. Offer eligibility reasons to diagnostics.

### C4. Buff scheduling and imbue timing

- Suppress duplicate pending casts for the same recipient and buff family.
- Confirm effect application separately from cast acceptance; count attempts only when the accepted request was expected to produce an observable effect.
- Match replacement/removal by application identity, preserving newer tokens.
- Add known buff-family conflicts and speed-drug compatibility to the existing policy; avoid treating every unknown overlap value as a conflict.
- Refresh imbue slightly before attack arrival only when its duration/cooldown support it. Use a bounded measured latency estimate, not Hyperbot's fixed 100 ms assumption.
- Consider grouping compatible buffs by required weapon to reduce swaps, but do not postpone urgent support or change correctness to save swaps.
- Keep conditional heal/MP logic, party health granularity and strict boundary checks. Unknown instant-skill semantics should be visibly identified, not silently treated as understood.

**Gate:** no competing weapon swaps or duplicate cast while awaiting confirmation; correct chains/interruptions; accepted opener runs once per encounter; late buff removal preserves replacement; resurrection priority and lower-type interruption/resume remain; support casting cannot chase outside the training area. Measure useful cast rate, swaps/minute, retries and buff coverage before/after.

Touchpoints: `SkillManager`, `SkillInfo`, `Action`, action response handlers, Attack/Buff/PartyBuffing/Healing/Resurrect/Target bundles.

## Stage D — responsive UI and actionable manager status

### D1. Async user workflows

Audit event handlers that can reach `AwaitResponse`, synchronous I/O or lengthy enumeration. Convert individual workflows to cancellable async operations; retire `Application.DoEvents()` where those callers migrate. Show immediate feedback and disable only the conflicting action. Do not replace a sync call with arbitrary concurrent `Task.Run` calls against shared mutable state.

Use immutable/small snapshots and `BeginInvoke` or the appropriate UI context for presentation. Heavy packet parsing, path searches and data preparation stay off the UI thread. Cache/diff controls only where profiling finds meaningful cost. The existing log batching and hidden-view refresh suppression are already present; validate rather than reimplement them. Measure the maximum work each UI flush does, since a bounded queue can still create a long frame.

### D2. Explain pending work

Extend ManagerLink with protocol version, session generation, snapshot sequence/time, current operation/phase, elapsed/deadline, target and retry/error reason. Distinguish “command received” from “game action completed.” Retain compatibility with older snapshots and optional fields.

Display “Buying potions — waiting for server”, “Casting resurrection — moving into range”, “Returning to area — path blocked” and stale/unresponsive state. Add lightweight process CPU/private memory samples and queue/drop counters; do not imply UI-visible elapsed timers are server progress.

**Gate:** UI remains interactive during a delayed/refused purchase, disconnect, Stop and map activity; manager command completion/failure is honest; stale snapshots are obvious; benchmark UI targets are met or remaining misses have measured causes.

## Stage E — performance phase 2, driven by profiling

### E1. Ordered state events without excessive task creation

Current event-name lookup is optimized, but network-thread listeners still receive individual `Task.Run` dispatch. Pilot typed ordered delivery for operation-state events only. Keep compatibility adapters for other plugins. Snapshot event data rather than handing consumers mutable objects that may change before processing.

Bound queue work and expose pressure. Required gameplay transitions must not be silently dropped; coalesce only presentation/latest-value telemetry. A stalled consumer must not indefinitely block packet forwarding. Callbacks must be short and never block waiting for an event delivered on the same worker.

### E2. Reduce repeated world/target work

Profile `Kernel.ComponentUpdaterAsync`, `SpawnManager.Update`, targeting filters, raycasts and allocations. Separate authoritative packet updates, local motion estimation, decision evaluation and UI refresh. Use dirty signals/cache invalidation for repeated derived work, with explicit stale deadlines and a fallback heartbeat. Consider distance buckets/spatial indexes only if entity scans are a measured bottleneck. Avoid globally slowing the 10 ms loop before validating movement estimation and tracing accuracy.

### E3. Memory and capture budgets

Audit image ownership/disposal, navmesh caches, trace/encounter history and static subscriptions across repeated sessions. Existing cache limits may clear entire caches; replace with incremental eviction only if measured churn warrants it and objects in active use stay valid.

Packet recording is bounded by entry count, but entries have different payload sizes. Add/measure a byte budget and expose dropped bytes/records where needed, including shutdown flush behavior. Cap diagnostic history by age and size. Verify private memory plateaus across repeated comparable cycles rather than judging growth while caches are warming.

### E4. Reference-data sharing only if it wins

First measure how much of eight-process RAM is duplicated immutable reference data. Explore lazy data loading or a versioned disk cache for startup; note that deserializing one disk cache into eight managed graphs still duplicates RAM. Only then prototype a read-only memory-mapped representation or local data service keyed by client archive fingerprint, client variant and schema version. Preserve standalone bots and fallback loading. This is optional larger work, not a prerequisite for reliability fixes.

**Gate:** repeated comparable runs show a benefit larger than benchmark variability without regressing packet latency, tracing, cast rate or UI responsiveness. Report absolute values and percentages. No silent gameplay-event loss, unbounded queues or session-cycle memory growth. Release each measured change separately.

## Stage F — navigation recovery using RSBot data

First implement a local-path feasibility spike over the existing navmesh cells/edges. RSBot raycasts tell whether a segment is traversable; they do not themselves supply a route. Validate walkability, terrain/object transitions, region coordinates and any dungeon-specific representation before choosing an algorithm.

Build a bounded local planner, then integrate waypoint confirmation, timeouts and replan limits with the operation service. Validate every segment with movement raycasts and a supported clearance model. Keep graph-generated town-to-training walkscripts for long-distance/teleport routing. Add a map overlay for planned waypoints and failure points.

Preserve the strict training-area rule: normal movement destinations must remain inside; recovery from an already-outside position gets only a validated route back toward the area, without allowing a new chase outside. If no route respects the policy, stop the recovery attempt with a clear reason rather than repeatedly issuing a blocked center move. Following/party assistance must obey the same predicate.

Hyperbot's path conversion and agent-radius concepts are references, not a library drop-in; its navmesh load is disabled and its rounding/arc conversions have known TODOs.

**Gate:** return around an obstacle; target moves while approaching; crossing a region boundary; blocked waypoint; knockback; server rejection; disconnected actor; unreachable center. Retries are bounded, the UI stays responsive and tracing/boundary behavior is preserved. Start in supported outdoor regions; add dungeons after their own fixtures.

## Stage G — practical feature additions

Deliver individually with documented defaults and profile migration:

| Feature | Proposed behavior | Acceptance check |
|---|---|---|
| Monster rules | Stable reference ID/CodeName overrides: ignore for ordinary acquisition, normal attack only, skill profile and optional imbue override. | Rules survive localization; define interaction with self-defense/leader assist explicitly; existing default targeting unchanged. |
| Buff profiles | Named training/travel profiles and combat conditions built on existing buffs. | Profile switch avoids duplicate/conflicting applications and unnecessary weapon swaps. |
| Supply rules | Per-category carry minimum/maximum, reserves and confirmed pet transfers/stacking. | Keep healing/ammo reserves during sell/store; no repeated transfer after ambiguous timeout; correct town decision when full. |
| Smarter townloop | Skip an optional NPC step only when its configured work is already satisfied; stage-level progress/reason. | Repair/restock/storage steps are not skipped incorrectly; Stop terminates any stage. |
| Recent sales | Show confirmed sales and explicit buyback where supported. | Partial counts, expired queue and insufficient gold handled correctly. |
| Script extensions | Typed area, pickup, buff, wait and stop conditions with deadlines and restoration of temporary state. | Error identifies script/line; failure restores temporary settings; no silent line skip. |

Avoid hidden autonomous configuration changes. New policies start opt-in where they alter established gameplay; correctness fixes are enabled normally.

## Stage H — manager history and party coordination

### H1. Durable history and group view

Add a bounded, versioned local history store fed by actual gameplay/operation events. Schema choice (simple append records versus an embedded database) follows query needs and available dependencies. Record kills, exp/SP, picked gold, expenses, loot, deaths, town time, uptime, cast failures and movement recovery. Keep picked gold, wallet change and net income separate. Identify session/reset boundaries and unknown data.

Show group totals and training-area comparisons, and a timeline around failures. Do not poll live statistic getters just to build history: some calculators mutate sampling state when read. Define one sampling owner and a read-only snapshot consumed by both Statistics and ManagerLink. Group totals must not double-count the same shared party event.

### H2. Coordination in three steps

1. **Read-only:** share fresh observations through the existing manager. Key by server/shard/instance/session and entity ID, with sequence and expiry. Unknown instance identity disables entity-level coordination. Preserve each bot's authoritative local view.
2. **Buff requests:** a selected support bot services explicit member needs using confirmed cast results and local range/boundary checks. Leases expire after stop/disconnect/failure.
3. **Target reservations:** optional temporary reservations reduce duplicate acquisition. Release on death, abandonment, timeout or disconnect. Defensive interruption and configured leader assist override ordinary reservations; document how designated attackers may share a target.

Every bot falls back to standalone behavior if manager state is missing/stale. A coordinator does not need all bots moved into one process. Confirm benefit with coverage, duplicate casts, target conflicts and useful throughput before expanding to lurer demand or supply delivery.

**Gate:** stale/mismatched observations cannot command a bot; manager restart/disconnect does not strand combat; support requests respect boundaries; reservations cannot prevent self-defense; group metrics have clear provenance.

## Stage I — optional learning and advanced automation

Use the validated observation/action interface for deterministic alternative policies and offline scoring first. Consider a separate AI experiment only after the baseline is stable and data is sufficient. Evaluate legal-action filtering, resource/target constraints and results on held-out scenarios. A training model is not a production policy just because it learns in a controlled PvP setup.

Do not make RL, academy farming, full dungeon automation, marketplace automation or a C++/Qt rewrite prerequisites. Hyperbot's roadmap is not evidence that these features work. Any future PvP training tool needs a distinct controlled-server scope and measurable outcomes.

## Proposed delivery batches

| Batch | Reviewable change | Main validation |
|---|---|---|
| 1 | Baseline document, lightweight counters and initial packet fixtures | Instrumentation overhead; reproducible failures |
| 2 | Atomic callback completion, cancellation cleanup and send failure | Cancellation/timeout/late-response races |
| 3 | Result-bearing shopping calls, migrated player/transport callers | Rejection/unrelated response; ordinary townloop |
| 4 | Buyback ownership and supported partial operations | Sale/buyback fixtures and captured server behavior |
| 5 | Opener confirmation and bounded encounter state | Refused opener; interrupt and resume same target |
| 6 | Skill lifecycle and ordered operation events | Chain/interruption; no worker deadlock |
| 7 | Coordinator for one migrated skill workflow, then support/combat | Existing priority and boundary regressions |
| 8 | Async UI callers and manager operation snapshots | Delayed server/UI latency/Stop checks |
| 9+ | Individual profiled optimizations, local navigation, then feature additions | Relevant benchmark and gameplay gates |

Batch 5 requires the cast-acceptance correlation from the small operation core; it may proceed before the full coordinator. This gives useful fixes early without coupling every stage to a large framework rewrite.

## Validation and delivery rules

- Use `build.ps1 -Configuration Debug -DoNotStart` for correctness checks and `build.ps1 -Configuration Release -DoNotStart` for benchmark artifacts. Never use `dotnet build`.
- Run existing relevant tests plus meaningful new operation/decoder/encounter tests. Select the existing Windows test execution path supported by the repository; do not assume `build.ps1` executes tests. No tests are needed for this planning document itself.
- If a build cannot be taken, ask the user to build and inspect `build.log` after confirmation. Do not label source inspection as a passed build.
- Run affected live paths first, then a controlled multi-bot session and soak. Repeat broad checks only when changes or failures justify it.
- Do not edit `.sln`, `.csproj`, `.Designer.cs` or generated files without the repository's required explicit confirmation. Plan UI additions in ordinary code where appropriate; identify any necessary project/generated changes before implementation.
- Preserve existing profiles, translations, client variants and plugin entry points. Migrate callers through compatibility wrappers, then remove legacy waits only after all relevant consumers move.
- Keep each release independently reviewable. New algorithms/coordination policies should have a fallback; monitor concrete failure counts and resource changes after rollout.

## Source anchors

- [RSBot operation callbacks](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Network/AwaitCallback.cs), [packet dispatch](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Network/PacketManager.cs), [shopping](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Components/ShoppingManager.cs).
- [Inventory/buyback updates](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Network/Handler/Agent/Inventory/InventoryOperationResponse.cs), [trade quantity correction](/home/dangvu/Documents/RSBot/Botbases/RSBot.Trade/Components/Scripting/BuyGoodsScriptCommand.cs).
- [Skill decisions/openers](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Components/SkillManager.cs), [cast correlation](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Objects/Action.cs), [party buffs](/home/dangvu/Documents/RSBot/Botbases/RSBot.Training/Bundle/PartyBuffing/PartyBuffingBundle.cs).
- [World update loop](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Kernel.cs), [events](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Event/EventManager.cs), [log writer](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Components/LogFileWriter.cs), [log display](/home/dangvu/Documents/RSBot/Plugins/RSBot.Log/Views/Main.cs).
- [Manager snapshot](/home/dangvu/Documents/RSBot/Plugins/RSBot.ManagerLink/Components/StatusTracker.cs), [live statistic sampling](/home/dangvu/Documents/RSBot/Plugins/RSBot.Statistics/Stats/Calculators/Live/KillsPerHour.cs), [packet recorder](/home/dangvu/Documents/RSBot/Plugins/RSBot.PacketAnalyzer/Components/PacketRecorder.cs).
- [Navigation data](/home/dangvu/Documents/RSBot/Library/RSBot.NavMeshApi/NavMeshManager.cs), [boundary recovery](/home/dangvu/Documents/RSBot/Botbases/RSBot.Training/Bundle/Movement/MovementBundle.cs), [long-route graph](/home/dangvu/Documents/RSBot/Botbases/RSBot.Training/Bot/NavigationManager.cs).
- [Hyperbot cast sequencing](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/castSkill.cpp), [move confirmation/retries](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/moveItem.cpp), [pending commands](/home/dangvu/Documents/Hyperbot/bot/src/state/skillEngine.cpp), [state composition](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/sequentialStateMachines.cpp).
- [Hyperbot blocking UI acknowledgement](/home/dangvu/Documents/Hyperbot/rl_ui/hyperbot.cpp:240), [disabled navmesh loading](/home/dangvu/Documents/Hyperbot/silkroad_lib/src/silkroad_lib/pk2/gameData.cpp:62), [earlier comparison](/home/dangvu/Documents/RSBot/HYPERBOT_REVIEW.md).
