# HideOut prototype — Unity 6000.3.23f1

Open the existing `HideOutSystemScene.unity` in this folder and press Play. The `HideOut Prototype` root references `Data/HideOutPrototype.asset`. All scene UI is built at runtime, with the existing Pretendard Korean font. No edit-mode auto-generation or scene-wide serialization is used.

## Implemented

- Clinic: capped healing, 600-second cooldown only after successful healing, level-based recovery amount. `HideOutPlayerBridge.RecoveryRateMultiplier` is the natural-recovery integration point; there is no new global regeneration system.
- Bed: full prototype player/facility snapshot save, +10 maximum HP per level without healing current HP.
- Dummy: existing `PlayerMeleeAttack.TryAttackTarget` damage/cooldown, hit counter and damage feedback. Equipped weapon is required. Bonus is a player modifier applied to whichever weapon is equipped, never an edit to item assets.
- Storage: paired bag/storage UI, paged slots, equipment swap, partial transfers, name sort, deposit only types present in storage at button press; +2 player slots and +5 storage slots per level.
- Potion shelf: locked until Selena recruitment, all-or-nothing delivery, durable cooldown; full inventory consumes neither reward nor opportunity.
- Gym: maximum MP bonus only. Dining and workshop are disabled placeholders.
- Upgrade: validate total cost first, aggregate duplicate recipe entries, mutate a candidate snapshot, atomically save then apply. Same-frame reentrant/double commands are rejected. Separate intentional clicks can purchase successive levels if fully affordable.
- Appearance: tiers at 0/2/4/6, last tier retained above 6. `HideOutAppearance.onUpgrade` is an animation hook and is not invoked on load.

## Temporary values and policies

Edit `Data/HideOutPrototype.asset` in the Inspector, including the per-facility `upgrades` arrays.

| Setting | Prototype value |
| --- | --- |
| Maximum level | 10, determined by each upgrades array, independent of appearance |
| Cost for next level N | 2N `hideout_scrap`, 10N coins, 1 action point |
| Healing | 5 × (1 + level × 1%), rounded up; never above maximum HP |
| Recovery-rate bonus | +1% of baseline per clinic level; integration hook only |
| Bed | +10 maximum HP per level; current HP unchanged |
| Dummy | +1 damage per level while a weapon is equipped |
| Damage | existing melee base + existing personal Attack + equipped weapon Attack modifier + dummy bonus |
| Storage | 10 base slots, +5 per level; bag retains base/personal-stat growth, adds +2 |
| MP | adapter base 100; +1% of baseline per gym level, rounded down |
| Potions | existing `health_potion`, 2 units, 900 seconds minus 30/level, minimum 60 seconds |
| Cooldowns | UTC real time including offline time by default; `offlineCooldown=false` uses saved active session time |
| In-progress potion cooldown | keeps its existing deadline; upgrade affects the next successful claim |

Do not switch clock policy for an existing save without migrating its timestamps.

## Existing systems vs adapters

Reuses PlayerInventory/Slot, stable ItemId, MaxStack, equipment definitions, PlayerStats, PlayerHealth, PlayerMeleeAttack, Interactable2D, Input System UI and PersistentPlayer2D.

No MP, wallet, recruitment or game-save service was found. Those are prototype fields on `HideOutPlayerBridge.State`, not duplicated global managers. The existing available stat points are NOT treated as action points. `IHideOutRecruitmentSource` on the player supplies Selena's state; `SetSelenaRecruited()` is the external recruitment callback. `IHideOutSaveParticipant` lets future quest/world save providers contribute keyed state with validation to the same save envelope. Existing equipment has no durability or per-instance affixes; each equipment unit occupies one slot, following the current inventory rules.

The save includes inventory configuration, normal/quest slots, equipment, quick slots, personal stats, HP/base health configuration, adapter MP/currency/recruitment, facility levels/storage/deadlines, return scene/position and participant records. It is not yet a general save system for unrelated quest/world components; those have no existing persistence interface. Unknown participant records are retained, not discarded. Live origin-scene quest/NPC state survives visits because the scene stays loaded.

## Save isolation and failure handling

`Application.persistentDataPath/HideOutPrototype/sandbox-v1.json` is for direct-scene tests; `player-v1.json` is for entrance visits. Both use temporary-file flush + atomic replacement and `.bak` backup. Existing save files elsewhere are untouched. Unreadable/unsupported save files block writes instead of being reset. Item IDs missing from the configured catalog reject transactions rather than losing items. Add all real game item definitions to `Items` before integration.

Direct tests start with no currency and Selena locked. Editor/development-only controls grant test resources, equip a practice weapon and reduce HP, recruit Selena, or clear cooldowns. They work only in the sandbox and never change the production rules asset.

## Connect a teammate scene

1. Place `Prefabs/HideOutEntrance.prefab` at the approved entry location; no GDD/LPD location has been chosen here.
2. Set Return Point to a safe standing point at the entrance (the prefab transform is the default). Its trigger and Interactable2D UnityEvent are already wired to `Enter`.
3. The existing player must have PersistentPlayer2D, PlayerInventory and PlayerHealth. PlayerMeleeAttack is needed for weapon tests. Do not add a second inventory/player manager.
4. The player's existing interaction controller uses F. The entrance attaches the HideOut adapter to that player, snapshots return scene/path/coordinates and saves before travel.
5. HideOut loads additively, suspends active origin roots and player controls/physics, then restores those exact roots and return coordinates on Exit. This preserves live quests and NPCs instead of rebuilding the origin scene through the existing single-scene fade loader. The player remains owned by PersistentPlayer2D.
6. Editor play can load the scene by its asset path without Build Settings changes. A standalone build requires the team to add this scene to its Scene List; this shared setting was deliberately not changed. Failure is reported without leaving the original scene.

Direct HideOut execution has no origin scene. Exit displays an explanation instead of inventing a return destination. Loading a save restores player/facility state, not arbitrary scene travel. A live entrance visit keeps the current player/inventory and loads facility progression; the explicit Load button restores the full saved snapshot.

## Shared-code changes

- `PlayerHealth.cs`: source-keyed maximum-HP modifiers, capped healing, save restore API. Damage still uses the existing defense/invincibility rules.
- `PlayerInventory.cs`: source-keyed capacity bonuses, no truncation of occupied slots, equipment/quick-slot restore and change notification for validated transactions.
- `PlayerMeleeAttack.cs`: damage-modifier/receiver interfaces and a targeted training entry point using the same attack cooldown. No modifier means the existing attack damage is unchanged.

No other play scene, existing prefab, package, input settings, render pipeline or Unity version is changed. Existing GUIDs are retained. These shared API additions still need team review; merge conflicts cannot be ruled out.

## Validation / continuation

Run `Tests/RunModelChecks.ps1` from PowerShell with the installed .NET 10 SDK. It runs the pure model checks without Unity, package installs or changes to game settings. The model tests do not substitute for actual Unity play tests.

Manual integration checklist: entry/exit with a real player, quest/NPC preservation, equipment continuity, returning at the configured safe point, save/restart, repeated upgrades and reload, actual Korean rendering/layout at team target resolutions, play exit/re-entry, and game-wide quest save participants when that system exists.

`Tools > HideOut > Run isolated play checks` (Ctrl+Shift+F9 while playing this scene) exercises real Unity components and the entrance prefab in an unsaved temporary origin scene. It uses uniquely named saves under project `Temp/HideOutValidation`, checks runtime errors/EventSystem warnings, and exits Play mode to restore the original editor scene. Results are written to `Temp/HideOutValidation/play-checks.txt`. It never writes the production or sandbox save.
