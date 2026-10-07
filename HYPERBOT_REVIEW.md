# Hyperbot lessons for RSBot

Reviewed 2026-10-07. Source baselines: Hyperbot `6c7e188`, RSBot `5335bcd5`.
Static source inspection only; neither application was built or run. This review adds documentation only.

## Conclusion

Hyperbot is most useful as a reference for event-driven operations, skill lifecycle tracking, structured observations, and action diagnostics. The current entry point centers on multi-session reinforcement-learning PvP training, rather than a complete general-purpose farming product. Keep RSBot's C# core, plugin structure, manager process, client variants, and existing gameplay policies. Adapt individual patterns incrementally.

The first implementation should improve confirmed shopping operations, followed by buyback correctness and operation diagnostics. Those address concrete weaknesses visible in RSBot today. A full AI or navigation replacement would have much greater cost and weaker evidence of readiness.

## What the source actually contains

| Area | Evidence in Hyperbot | Implication |
|---|---|---|
| Multiple sessions | `Session` owns a proxy, packet processor and bot; `TrainingManager` owns a vector of sessions. `Hyperbot::run` shares game data, an event broker and world state. | Useful architecture reference for coordination; does not establish a production party coordinator. |
| Event-driven operations | Child state machines for casting, walking, item movement, purchasing and NPC interaction; cancellable delayed events. | Strong pattern for replacing blocking action waits. |
| Skill lifecycle | Cast-ID map records caster and reference skill; begin/end/failure events drive `CastSkill`. | Extend RSBot's current cast model rather than introducing another independent one. |
| Navigation | Navmesh triangulation and a Polyanya pathfinder call, agent radius, search timeout, waypoint conversion. | Substantial implementation exists, but normal navmesh loading is disabled. |
| RL | Observation builder, action space, JAX interface, replay buffer, rewards and checkpoints. | Research implementation; successful gameplay or model quality was not verified. |
| Statistics | Timestamped protobuf event schema; recorder implementation largely commented out. | Useful schema idea, not an active statistics feature to port. |
| Roadmap | `documents/bot-ideas.md` describes party coordination, academy farms, unique hunting, dungeon runs and marketplace ideas. | Do not count these notes as implemented capabilities. |

Primary architecture sources: [entry point](/home/dangvu/Documents/Hyperbot/bot/src/hyperbot.cpp), [session](/home/dangvu/Documents/Hyperbot/bot/src/session.hpp), [training manager](/home/dangvu/Documents/Hyperbot/bot/src/rl/trainingManager.hpp), [state machine](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/stateMachine.hpp).

## Ranked improvements

### 1. Confirm shopping operations by their meaning, not only their opcode — high priority

**Hyperbot lesson:** `BuyingItems` waits for inventory events before reducing the remaining shopping quantity. It explicitly tracks whether a buy or a stack move is pending. However, its TODOs admit that an incoming item is not conclusively correlated with the purchase, so this is a pattern to improve on.

**RSBot finding:** `ShoppingManager.PurchaseItem` and `SellItem` use `new AwaitCallback(null, 0xB034)`. Any matching opcode satisfies the callback, including an unrelated inventory operation or an error response. `SellItem` logs “Sold item” after waiting without checking success. The methods return `void`, preventing callers from distinguishing acceptance, rejection and timeout.

**Proposal:** introduce a result-bearing inventory operation service. Serialize ambiguous operations per inventory/transport; subscribe before sending; match the response result and operation type plus fields actually present in that client variant. Preserve the raw error code. Confirm quantities from the authoritative inventory update. If the protocol omits correlation fields, serialization and reconciliation must resolve ambiguity; a local request ID alone cannot identify a wire response. Cancel operations on stop/disconnect and close pending callbacks.

**Benefit:** fewer false successes, wrong purchase counts, and repeated or stalled town actions. This develops the “confirmed, cancellable operations” item already in `BOT_BACKLOG.md`.

Sources: [Hyperbot purchasing](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/buyingItems.cpp:22), [RSBot shopping](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Components/ShoppingManager.cs:290), [callback](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Network/AwaitCallback.cs).

### 2. Fix buyback ownership and partial quantities — high priority, small scope

**Hyperbot lesson:** on a partial sale, clone the sold portion and set its quantity separately from the remaining inventory stack. It also has an explicit queue abstraction and handles partial buyback.

**RSBot finding:** `ParseInventoryToNpc` puts `itemAtSlot` directly into `ShoppingManager.BuybackList`, then changes the inventory stack's amount on a partial sale. Both entries reference the same mutable object. `ParseBuybackToInventory` changes that object again and always removes the source buyback entry, regardless of whether a partial buyback leaves a remainder.

**Proposal:** use a separate item instance for the sold amount; preserve the inventory remainder. Handle partial withdrawal, unknown source slots and queue shifts according to captured server responses. Do not adopt Hyperbot's queue indexing blindly: RSBot already consumes a server-provided buyback slot.

**Benefit:** accurate inventory/buyback counts and a sound foundation for a “recent sales / buy back” UI. Buyback parsing already exists in RSBot; this is a correctness improvement.

Sources: [Hyperbot sale handling](/home/dangvu/Documents/Hyperbot/bot/src/packetProcessor.cpp:901), [buyback queue](/home/dangvu/Documents/Hyperbot/bot/src/storage/buybackQueue.cpp), [RSBot sale parsing](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Network/Handler/Agent/Inventory/InventoryOperationResponse.cs:419), [buyback parsing](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Network/Handler/Agent/Inventory/InventoryOperationResponse.cs:772).

### 3. Track the complete skill operation — high priority, medium scope

**Hyperbot lesson:** distinguish waiting for cast acceptance from waiting for the skill to finish; use cast IDs, root skill relationships, chain completion, interruptions and timeout events.

**RSBot already has:** `SkillCastResult`, server-confirmed cooldowns, refusal backoff, and a recent cast-ID-to-executor map. Do not redo these features. The current result enum describes acceptance of buff requests, and the recent map stores executor and tick rather than a complete skill operation.

**Proposal:** extend the existing model with request/target/skill/instance IDs, accepted/completed/interrupted/refused/timed-out/cancelled outcomes, and separate acceptance/completion deadlines. Centralize action ownership so buffs, healing, resurrection, combat and weapon swaps coordinate. Keep emergency priorities and lower-type attacker interruption intact. Clear state on death, disconnect and profile changes; reject stale local timeout events using an operation generation.

**Benefit:** clearer handling of chained skills, interrupted casts and competing support actions. Hyperbot's own two-value `Done/NotDone` status conflates success and failure; use richer outcomes in RSBot.

Sources: [Hyperbot casting](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/castSkill.cpp), [cast map](/home/dangvu/Documents/Hyperbot/bot/src/packetProcessor.cpp:1650), [RSBot action](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Objects/Action.cs:14), [cast results](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Objects/Skill/SkillCastResult.cs), [skill manager](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Components/SkillManager.cs:622).

### 4. Show what each bot is doing and why it is waiting — high value, medium scope

**Hyperbot lesson:** child state machines expose their active name; walking publishes the remaining path; Tracy instrumentation covers packet processing, event dispatch, locks and training.

**RSBot finding:** manager snapshots report broad states such as `Running`, `InGame` and `LoggingIn`, along with health, loot and position. They do not explain a pending NPC response, weapon swap, blocked movement or a cast timeout.

**Proposal:** add an operation snapshot containing action, phase, target, elapsed time, deadline, retry count and last refusal. Display concise user-facing reasons such as “Buying potions — waiting for server” and “Returning to area — movement rejected.” Record operation transitions next to packet-capture markers. Measure queue depth and processing latency before changing threading.

**Benefit:** makes an eight-bot run much easier to diagnose and turns logs into evidence of why a bot stopped progressing.

Sources: [active state](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/stateMachine.cpp), [walking](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/walking.cpp), [RSBot manager snapshot](/home/dangvu/Documents/RSBot/Plugins/RSBot.ManagerLink/Components/StatusTracker.cs:40), [packet recorder](/home/dangvu/Documents/RSBot/Plugins/RSBot.PacketAnalyzer/Components/PacketRecorder.cs).

### 5. Parse first, apply state second; add offline packet regressions — medium priority

**Hyperbot lesson:** parsers produce structured packets before `PacketProcessor` applies changes under the world-state lock. For example, skill-hit bytes are consumed without first looking up each target in the live spawn table.

**RSBot already has:** corrected hit-payload consumption, bit-flag handling and executor correlation in `Action`; packet capture; tracing tests. Treat those as existing work, not missing features.

**Proposal:** extract decoding for the busiest combat/inventory packets into data objects, preserve RSBot's client-specific layouts, then feed captured fixtures to decoders and operation state transitions offline. First fixtures: unknown AoE target before a known target, chained skill end, late acknowledgement, rejected purchase, partial sale/buyback and disconnect during a pending action. Use a fake clock and isolated state; replay must not send packets to a live server.

**Benefit:** reproduces rare failures without another live farming session. Hyperbot's RL replay buffer stores learning transitions; it is not a packet replay harness.

Sources: [skill-end decoder](/home/dangvu/Documents/Hyperbot/bot/src/packet/parsing/serverAgentSkillEnd.cpp), [hit decoder](/home/dangvu/Documents/Hyperbot/bot/src/packet/parsing/commonParsing.cpp:278), [RSBot action decoder](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Objects/Action.cs:94), [existing test harness](/home/dangvu/Documents/RSBot/Tests/RSBot.Core.Tests/TraceHarness.cs).

### 6. Order gameplay state events and bind their lifetime to the session — medium priority

**Hyperbot lesson:** events and delayed actions pass through a timer worker, with typed event objects and cancellable timer IDs.

**RSBot finding:** `EventManager.FireEvent` launches a separate `Task.Run` for each listener when called on the packet processor thread. Callback execution order and completion order are consequently not guaranteed. Subscription snapshots, locking, unsubscribe support and exception isolation already exist.

**Proposal:** pilot typed, ordered delivery for a small set of operation-state events. Use a per-session queue, disposable subscriptions and bounded work; marshal presentation updates separately. Keep blocking packet waits off the queue to avoid deadlock. Migrate legacy events incrementally.

**Benefit:** reduces races between begin/end, despawn and cancellation. Do not copy Hyperbot's broker wholesale: callback exceptions and callback-time unsubscription need careful handling, and its timer loop has shutdown/concurrency concerns visible in the source.

Sources: [Hyperbot event dispatch](/home/dangvu/Documents/Hyperbot/bot/src/broker/eventBroker.cpp), [timer worker](/home/dangvu/Documents/Hyperbot/bot/src/broker/timerManager.cpp), [RSBot events](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Event/EventManager.cs:66).

### 7. Extend manager coordination and historical statistics — later feature work

**Hyperbot lesson:** common world observations across sessions and timestamped event schemas provide useful building blocks for coordination and historical comparisons.

**Proposal:** retain RSBot's separate bot processes. Extend ManagerLink with fresh, versioned observations keyed by server/shard/session, then pilot expiring target reservations or buff requests. Define ownership, freshness, timeout and fallback rules before enabling coordination. Add a lightweight history of kills, loot, deaths, town time and operation failures; show group totals and trends in the manager.

**Benefit:** potential reduction in duplicate party work and better comparison of training areas. These are new RSBot features inspired by architectural pieces and design notes, not verified Hyperbot capabilities. RSBot's live statistics are already implemented; Hyperbot's `StatAggregator` subscriptions and handlers are commented out.

Sources: [shared world initialization](/home/dangvu/Documents/Hyperbot/bot/src/hyperbot.cpp:36), [event schema](/home/dangvu/Documents/Hyperbot/ui_proto/stats.proto), [disabled recorder](/home/dangvu/Documents/Hyperbot/bot/src/statAggregator.cpp:17), [RSBot statistics](/home/dangvu/Documents/RSBot/Plugins/RSBot.Statistics/Stats/CalculatorRegistry.cs).

## Defer these larger transfers

- **Navigation rewrite:** RSBot already has navmesh raycasts, graph-based automatic walkscript generation and smart tracing. Hyperbot offers agent-clearance and waypoint-conversion ideas, but `GameData::parseData` comments out navmesh parsing because of a noted Triangle memory leak. Its waypoint conversion also admits rounding/arc approximations may produce invalid paths, and its navmesh test file contains placeholders. Start with obstacle-aware local return-to-area routing using RSBot's existing data, movement raycasts and explicit waypoint confirmation; evaluate another pathfinder only against captured obstacles and region transitions. Sources: [disabled loading](/home/dangvu/Documents/Hyperbot/silkroad_lib/src/silkroad_lib/pk2/gameData.cpp:62), [path conversion](/home/dangvu/Documents/Hyperbot/bot/src/bot.cpp:720), [RSBot navigation](/home/dangvu/Documents/RSBot/Botbases/RSBot.Training/Bot/NavigationManager.cs), [RSBot collision checks](/home/dangvu/Documents/RSBot/Library/RSBot.Core/Objects/Position.cs:252).
- **RL combat:** first reuse the observation/action separation for deterministic decisions and offline evaluation. Hyperbot has fixed observation skill/item lists, commented-out MP observations, and an action that deliberately requests an absent item to elicit a server error. Its PvP setup includes GM warp, replenishment and item-spawn operations. This is a controlled training setup, not evidence of a deployable general combat policy. Sources: [observations](/home/dangvu/Documents/Hyperbot/bot/src/rl/observationBuilder.cpp), [item action](/home/dangvu/Documents/Hyperbot/bot/src/rl/action.cpp:75), [PvP setup](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/pvpManager.cpp:292).
- **Wholesale townloop port:** Hyperbot hardcodes town NPC locations, recognizes only Jangan/Constantinople in this implementation, and leaves dynamic NPC selection as a TODO. Keep RSBot's scripts and shopping configuration; improve their confirmed operation execution. Source: [townloop](/home/dangvu/Documents/Hyperbot/bot/src/state/machine/townlooping.cpp).

## Suggested implementation sequence and acceptance checks

1. **Inventory operation results:** an unrelated `0xB034` cannot complete a purchase; a rejection returns the code; quantities advance only after confirmation; timeout/disconnect stop cleanly; existing client branches remain supported.
2. **Buyback correctness:** sell part of a stack, verify independent sold/remaining quantities, buy back part and then the remainder where supported, and handle expired/unknown slots without corrupting inventory. Confirm packet layouts from captures.
3. **Operation visibility:** manager shows the pending action and reason; one operation identifier connects request, acknowledgement, timeout and recovery logs.
4. **Skill lifecycle and ordered events:** chained cast completion, knockback interruption, delayed end response, resurrection priority and buff/weapon-switch conflicts pass offline fixtures and live checks.
5. **Local navigation recovery:** reach the training area around an obstacle with bounded retries while preserving the current strict training-boundary policy.
6. **Optional group coordination/history:** begin with read-only observations; measure improvements before allowing shared decisions.

Build any later code changes on Windows with `build.ps1 -Configuration Debug -DoNotStart`, then validate the affected gameplay paths. No build is needed for this documentation-only review. No `.sln`, `.csproj`, `.Designer.cs` or generated files were changed.
