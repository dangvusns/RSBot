# iBot → RSBot improvements plan

Branch: `iBot_improvements`. Source studied: `iBot/iBot_ISRO_Source` (VB6 bot for iSRO, ~47k lines).
Goal: port the useful features iBot has that RSBot doesn't, step by step (tier 1 → 3).

## Working rules for this branch

- Build on Windows with `powershell.exe -ExecutionPolicy Bypass .\build.ps1` (no MSBuild on Linux, never `dotnet build`).
- The user approved editing `.Designer.cs` files for this work.
- New checkboxes: set `AutoSize = false` and an explicit `Size(w, 30)`. At 175% DPI, auto-sized SDUI checkboxes
  grow wider than their text and cover their neighbours. Add right-column controls to `Controls` first (drawn on top).
- Checkboxes inside `groupBoxAdvanced` (Training) and `groupBackTown` (Protection) are saved automatically as
  `RSBot.Training.<name>` / `RSBot.Protection.<name>`.
- Language lookups have no English fallback: add new keys to every file in `Build/Data/Languages/<plugin>/`.
- Commit each step and push so the user can test on Windows.

## Done

- [x] Bug: "Store items from pet" was saved from the sell checkbox (`Plugins/RSBot.Items/Views/Main.cs`).
- [x] Bug: quest reward lines all showed the gold amount (`Plugins/RSBot.Quest/Views/Main.cs`).
- [x] 1. Don't steal kills: skip mobs other players are fighting; switch target if others attack it
      (`Botbases/RSBot.Training/Bundle/Target/TargetBundle.cs`).
- [x] 2. Back-town triggers: unique nearby, pet died, transport died, quest completed
      (`Plugins/RSBot.Protection/Components/Town/{UniqueNearby,CosDied,QuestCompleted}Handler.cs`,
      core event `OnQuestObjectivesCompleted` in `QuestUpdateResponse.cs`).
- [x] 3. Pet assist: pet attacks my target (`Bundle/Pet/PetBundle.cs`, `Cos.Attack`); defend my attack pet (TargetBundle).
- [x] 4. Opener skills and strong-monster-only buffs. Skills tab, right-click the attack skill list / buff list.
      Config `RSBot.Skills.Openers_<monster type index>` and `RSBot.Skills.StrongTargetBuffs`;
      logic in `SkillManager.GetNextSkill` / `IsBuffAllowedNow`, used by `BuffBundle`.
- [x] 5. Area swap: next saved area when no target for N seconds (`Bundle/AreaSwap/AreaSwapBundle.cs`, option in the
      training areas dialog). Walks with an auto path when > 80m; back to the primary area when the town script runs
      or the bot stops. Config `RSBot.Training.checkSwapArea`, `RSBot.Training.numSwapAreaSeconds`.
- [x] Create training area dialog: X / Y / Region can be typed (`CreateTrainingAreaDialog`).
- [x] Fix: Skills tab crash in `BuffTimer_Tick` — skill lists are now reloaded on the UI thread.
- [x] 7. Remote commands: private/party messages work without the sender near (`Commander` record in
      `CommandsBundle`); new invite/inviteme, leave/leavept, status, logout (no relogin via
      `ReloginGuard.SuppressUntilNextLogin`), help. "join party" skipped (needs a party number).
- [x] 8. Pick pet → inventory transfer (`Plugins/RSBot.Protection/Components/Pet/PetTransferHandler.cs`, Protection
      tab pet box): supplies right away, everything when the pet is full; the "full pet inventory" town trip waits
      while the inventory has room. Config `RSBot.Protection.checkPetTransferSupplies` / `checkPetTransferWhenFull`.
- [x] Fix: Inventory tab "Move to pet" had an inverted null check and never moved anything.

## Tier 1 (remaining)

- [-] 6. Sound alarms: skipped, the user doesn't use alarms much.

## Tier 2

- [ ] 9. Quest automation: take Nth quest from NPC, claim reward, then town / "quest done" script (iBot `QuestParser.bas`).
- [ ] 10. Forgotten World automation (Dimension Hole item, train inside, leave when cleared) (iBot `ForgottenWorld.bas`).
- [ ] 11. Auto-vending stall: prices, open, sale log, close when sold out (iBot `Staller.bas:214-483`).
- [ ] 12. Consignment auto-buy at or below a price (iBot `Staller.bas:510-655`, opcodes 0x750A / 0x750C).
- [ ] 13. Dismantle items (iBot `Staller.bas:666-726`, opcode 0x7538; RSBot `AlchemyManager.cs:13` has a to-do).
- [ ] 14. Extra script commands: set area, pick on/off, buff on/off, clear area, rewind, stop, inject packet.

## Tier 3

- [ ] Check Attendance button (packet 0x74DD).
- [ ] Party-matching join filter: accept only names on a list (`PartyMatchingInviteRequest.cs:14`).
- [ ] Fuse statistics: success/fail per +level, pattern log (iBot `frmFuse.frm:452-660`).
- [ ] Chat log to file.
- [ ] Log category filters.
- [ ] Login / disconnect counters.
- [ ] Lag detector (no server packets for 5 s while botting).
- [ ] Always-on-top toggle.
- [ ] Mini mode window.
- [ ] Level several masteries in turn (iBot `frmMastery.frm`).
- [ ] Different imbue per mob type.
- [ ] Mob rules by name (traps, event mobs).
- [ ] Set respawn point.
- [ ] Party "taxi" timers (kick when paid time runs out).

## Not worth porting

Cleverbot chat replies, skill-cancel exploit, fake GM summon, Hackshield / opcode remapping, on-level-up script (stub in iBot).
