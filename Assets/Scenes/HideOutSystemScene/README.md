# HideOut prototype — Unity 6000.3.23f1

Open the existing `HideOutSystemScene.unity` in this folder and press Play. The `HideOut Prototype` root references `Data/HideOutPrototype.asset`. All scene UI is built at runtime, with the existing Pretendard Korean font. No edit-mode auto-generation or scene-wide serialization is used.

## Implemented

- Clinic: capped healing, 600-second cooldown only after successful healing, level-based recovery amount. `HideOutPlayerBridge.RecoveryRateMultiplier` is the natural-recovery integration point; there is no new global regeneration system.
- Bed: full prototype player/facility snapshot save, +10 maximum HP per level without healing current HP.
- Dummy: compact bottom panel, test mode, existing `PlayerMeleeAttack.TryAttackTarget` damage/cooldown, hit counter and floating damage feedback. Equipped weapon is required. Bonus records are keyed by the equipped weapon `ItemId`; item assets are never edited.
- Storage: paired bag/storage UI, paged slots, equipment swap, partial transfers, name sort, deposit only types present in storage at button press; +2 player slots and +5 storage slots per level.
- Potion shelf: locked until Selena recruitment, all-or-nothing delivery, durable cooldown; full inventory consumes neither reward nor opportunity.
- Gym: maximum MP bonus only. Dining and workshop are disabled placeholders.
- Upgrade: validate total cost first, aggregate duplicate recipe entries, mutate a candidate snapshot, atomically save then apply. Same-frame reentrant/double commands are rejected. Separate intentional clicks can purchase successive levels if fully affordable.
- Appearance: tiers at 0/2/4/6, last tier retained above 6. `HideOutAppearance.onUpgrade` is an animation hook and is not invoked on load.
- Top view: a procedural 3D isometric bunker replaces the two-room carousel. Vertex-colored cuboids form open-front rooms, tiled floors, walls and props without changing the URP 2D renderer. Facility icons project 3D anchors into the screen-space HUD; selection focuses the orthographic camera and opens the existing panel.

## Temporary values and policies

Edit `Data/HideOutPrototype.asset` in the Inspector, including the per-facility `upgrades` arrays.

| Setting | Prototype value |
| --- | --- |
| Maximum level | 10, determined by each upgrades array, independent of appearance |
| Cost for next level N | 2N `hideout_scrap`, 10N coins, 1 action point |
| Healing | 5 × (1 + level × 1%), rounded up; never above maximum HP |
| Recovery-rate bonus | +1% of baseline per clinic level; integration hook only |
| Bed | +10 maximum HP per level; current HP unchanged |
| Dummy | +1 permanent damage to the weapon `ItemId` equipped at each upgrade |
| Damage | existing melee base + existing personal Attack + equipped weapon Attack modifier + dummy bonus |
| Storage | 10 base slots, +5 per level; bag retains base/personal-stat growth, adds +2 |
| MP | adapter base 100; +1% of baseline per gym level, rounded down |
| Potions | existing `health_potion`, 2 units, 900 seconds minus 30/level, minimum 60 seconds |
| Cooldowns | UTC real time including offline time by default; `offlineCooldown=false` uses saved active session time |
| In-progress potion cooldown | keeps its existing deadline; upgrade affects the next successful claim |
| Overview camera | position (12, 15, -16), rotation (37, -37, 0), orthographic size 10.5 |
| Facility focus | clinic 4.8, storage 5.2, dummy 4.8 orthographic size; 0.7-second move |

Do not switch clock policy for an existing save without migrating its timestamps.

Camera position, angle, move duration and facility focus values are temporary presentation settings under `HideOutPrototype.asset > Top View`. Runtime camera anchors are named `OverviewCameraAnchor`, `TreatmentBoothCameraAnchor`, `WarehouseCameraAnchor` and `TrainingDummyCameraAnchor`. Procedural room art is isolated in `HideOutIsometricEnvironment`, so final meshes or room prefabs can replace it without changing facility logic.

## Free camera controls

- Empty-space left drag pans across the XZ ground plane after a 10-pixel threshold; short clicks do not move the camera.
- Dragging from UI never pans. A facility-button drag suppresses its click, while a short click still focuses the facility.
- The mouse wheel smoothly zooms between orthographic sizes 3.8 and 10.5. Zoom currently uses the camera center.
- Left or middle mouse drag pans; Alt rotation has been removed. Wheel scrolling zooms independently of middle-button dragging.
- Left drag uses 40% of the middle-button sensitivity and 25% release inertia for small adjustments. `Left Drag Multiplier` adjusts this independently.
- When the overview is larger than the map on an axis, `Overview Pan Allowance` permits a small 1.5-unit pan either side of its center instead of locking that axis. Set zero to keep it locked. This is a fixed boundary with no elastic overscroll.
- `Home` or the small `전체 보기` button closes facility UI and restores overview position, zoom and roll.
- The environment calculates bounds from generated MeshRenderers. Viewport ground intersections, aspect ratio and zoom are included when clamping free movement.

Drag sensitivity, maximum speed, inertia damping, zoom and bounds padding are Inspector values under `HideOutPrototype.asset > Top View`. Drag position is applied directly with no elastic overscroll; only release inertia and zoom are smoothed. Bounds clamp both actual and target positions and remove outward velocity on each blocked axis. Camera footprint scales around the ground intersection at screen center rather than around the eye position.

## Editor preview and six facilities

Opening `HideOutSystemScene.unity`, reloading editor assemblies, or exiting Play automatically restores one temporary Scene/Game preview when `Editor Auto Preview` on the saved HideOut Prototype is enabled. Generation frames Scene view in the isometric angle; Game view uses a preview camera and screen-space HUD. There is no continuous regeneration. Use `하이드아웃 미리보기 생성 / 갱신` after changing art/settings. Choose `미리 볼 시설` and the detail-preview button for sample panels; `상세 미리보기 끄기` keeps the environment visible. The full cleanup button removes only temporary preview objects. Preview uses DontSave and is cleared before Play/domain reload. Prefab stages are excluded. No real inventory or HP component is created, no save is read, and edit-mode transactions are rejected.

All eight facilities use the same world/bar click toggle: first click focuses and opens its panel; clicking the selected facility returns to overview. Input is locked during movement. Other facilities stay locked during dummy testing, while clicking the selected dummy icon exits to overview. Resource HUD uses generated replaceable sprite silhouettes (heart, drop, coin, action marker) with live HP/MP gauges and coin/AP counts. Visual tiers still change at 2/4/6, with stronger floor frames/supports/light rails. Paid upgrades flash a separate ground highlight for 0.55 seconds; loads never flash it.

`Tools > HideOut > Verify preview and play` runs preview-regeneration checks and the isolated Play checks in the existing 6000.3.23f1 editor. Reports and actual Game screenshots are written under `Temp/HideOutValidation`; production saves are not used. An explicit `request-ui-checks.flag` in that folder requests the same command for automation; it is consumed once after refresh/compilation settle.

The bottom horizontal ScrollRect uses `Top View > Shortcut Order`: storage, treatment booth, dummy, bed, potion shelf, gym. World and bar buttons call the same selection entry point. Potion locks and levels refresh from bridge state. FacilityFocus blocks navigation; dummy tests also block bar switching. Panel layouts reserve the bottom 106 reference pixels for this bar.

Bed uses the existing complete HideOut snapshot/participant save envelope and source-keyed +10 max HP per upgrade, preserving current HP. This is not a whole-game save service: unregistered quest/world systems still need IHideOutSaveParticipant. Potions reuse health_potion x2, 900-second UTC deadline, -30/level, 60-second minimum, with atomic full-inventory checks. Missing item definitions block claiming. IHideOutRecruitmentSource is polled for Selena state; sandbox development controls include a recruitment test. Gym uses baseline adapter MP 100, +1% per upgrade, floors the combined bonus once. Existing recipes remain 2N scrap, 10N coins, 1 AP at next level N, max level 10 through upgrade arrays.

Presentation records retain facility IDs independently of save enum IDs. To replace art assign `Visual Prefab` or `Tier Visual Prefabs` (0/2/4/6) in `Prototype Facilities`; the generated room meshes are hidden while independent icon anchors, camera anchors, dummy receiver and UI events remain intact. Prefabs use the environment's world coordinate origin. `Icon` supplies both world and shortcut artwork; camera positions remain under Top View. The new expansion rooms also expose named Visual Roots in their generated hierarchy. Refresh editor preview after changing art/settings.

## Existing systems vs adapters

### Exhibition rooms and shared visual progression

Dining and Workshop are exhibition-only, identified by the existing saved enum IDs 6 and 7. No morale, NPC-count unlock, buff, recipe, crafting, delivery or paid upgrade is executed. Their dedicated panel says `추후 구현` and separates future design descriptions from active effects. Old levels in saves remain untouched; exhibition artwork always uses its initial tier. Both world icons and the bottom scroll bar reuse SelectFacility and the existing focus/overview flow. Both rooms appear in the same explicit editor preview.

`Data/HideOutPrototype.asset > Prototype Facilities` controls the room description, icon, exhibition room position/size, icon offset and optional Visual Prefab/Tier Visual Prefabs. The room art is isolated below Procedural Visual Root; hit receivers and icon/camera anchors remain independent. Prefab Position/Rotation/Scale are adjustable through visualPosition/visualEuler/visualScale. Missing tier prefabs reuse the nearest earlier configured tier, then the base prefab; clearing the overrides restores procedural art. Tier slots represent 0–1 / 2–3 / 4–5 / 6+ levels, independent of max level. The six usable facilities all have procedural tier changes. Exhibition rooms stay at initial appearance regardless of legacy levels.

On the saved HideOut Prototype root, connect `On Facility Upgraded (int facilityId, int completedLevel)` to a future animation/audio receiver. The bridge emits UpgradeSucceeded only after the atomic save/apply succeeds. The view restores the latest art before invoking the animation hook. Scene entry, editor preview, repeated refresh and load only restore art and never emit this event. No separate upgrade or appearance room exists. Existing 10-level recipes and effects are unchanged (next level N: scrap 2N, coins 10N, AP 1), with save publication after total-cost validation.

The art direction uses warm timber/enamel communal dining on the west passage and cool steel/blueprint servicing with hazard stripes on the east passage. Plates, mugs, lamps and barrels are procedural cylinders; decorative weapons have no item/pickup/attack components. No external assets or packages are needed.

Reuses PlayerInventory/Slot, stable ItemId, MaxStack, equipment definitions, PlayerStats, PlayerHealth, PlayerMeleeAttack, Interactable2D, Input System UI and PersistentPlayer2D.

No MP, wallet, recruitment or game-save service was found. Those are prototype fields on `HideOutPlayerBridge.State`, not duplicated global managers. The existing available stat points are NOT treated as action points. `IHideOutRecruitmentSource` on the player supplies Selena's state; `SetSelenaRecruited()` is the external recruitment callback. `IHideOutSaveParticipant` lets future quest/world save providers contribute keyed state with validation to the same save envelope. Existing equipment has no durability, per-instance affixes or per-copy instance ID; each equipment unit occupies one slot, following the current inventory rules. The dummy prototype therefore distinguishes weapons by stable definition `ItemId`. Two copies with the same `ItemId` share the same bonus until the inventory gains instance IDs.

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

Enter Play mode, then run `Tools > HideOut > Run isolated play checks` (Ctrl+Shift+F9). The check supports the project's enforced Forest startup scene, exercises real Unity components and the entrance prefab in an unsaved temporary origin scene, and exits Play mode to restore the original editor scene. It uses uniquely named saves under project `Temp/HideOutValidation`, checks runtime errors/EventSystem warnings, and writes results to `Temp/HideOutValidation/play-checks.txt`. It never writes the production or sandbox save.
