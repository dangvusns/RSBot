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

## Tier 1 (remaining)

- [ ] 5. Swap to the next training area when no mobs are found for N seconds (iBot `AutoTrain.bas:300-305`).
      RSBot already stores several areas (`TrainingAreasDialog.cs`).
- [ ] 6. More sound alarms: GM nearby (name starts with `[GM]`), level up, died, disconnected, back in town,
      chat per channel, Hunter/Thief trade starting. RSBot only has unique alarms (`NotificationSounds.cs`).
- [ ] 7. Remote PM commands: accept commands from senders that aren't spawned nearby
      (`Plugins/RSBot.Party/.../ChatResponse.cs`); add invite-me, join-party, leave-party, logout, relog, help.
- [ ] 8. Pet-to-inventory transfer while training: pots/pills/arrows from pick pet to inventory, or empty the pet
      bag when full instead of returning to town (iBot `InventoryUpdate.bas:1408-1461`).

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
