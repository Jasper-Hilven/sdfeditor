# 44 — Body animations

Part of **Minigun Monsters** (41-50). The soldier body (43) moves with BattleRoyale's Mixamo clips through its Animator: locomotion,
jump, fire, hit, deaths and dances. Missing clips come from Mixamo.

Depends on: 43. Blocks: the Play-mode look of 45 and 46.

## Source (`../BattleRoyale` at `80df274`)

| From | What |
|---|---|
| `unity/BattleRoyale/Assets/Game/Soldier/Animations/` (with `.meta`, GUIDs kept) | The 37 clips from medievalminiguns: locomotion, combat, deaths, emotes. Also `SoldierAnimationPostprocessor`, `AnimationImportFixer`, `SoldierClips`, `SoldierClipMap` and the `Mixamo/` drop pipeline |
| `Assets/Game/Soldier/Animator/`, `Avatar/UpperBody.mask` | `SoldierControllerBuilder`, `CombatLayerBuilder`, `DeathLayerBuilder`, `CombatClipMirror` and its mirrored bakes |
| `Assets/Game/Soldier/Runtime/` | `SoldierAnimator` (with `.Combat`), `LocomotionMath`, `LocomotionClips`, `LocomotionTuning`, `CombatTuning`, `CombatRules` |
| `docs/animations.md` | The clip table and the Mixamo steps ("Missing clips") |

Licence: Mixamo, royalty-free, and the Synty skeleton's bone names only (no Synty mesh in this repo). Note it in the credits and
legal notes if the project has them.

## Work

- **Copy and import**: the clips into `Assets/Battle/Body/Animations/`, the controller builders into its `Editor/`, and `SoldierAnimator`
  into `Runtime/`. Rename the namespaces, and drop BR-only layers (parachute, crouch, loot).
- **Wire to the game**: the player's and bots' controllers feed `SoldierAnimator` with:
  - the velocity in the body's frame (the locomotion tree)
  - grounded or jumping (`soldier_jump`)
  - fire (automatic for the minigun, flamethrower and electro-gun; single for the sniper and pistol)
  - hit direction, and the death (a ragdoll hook, not a clip: 51)
  - the carried flag (the upper body stays in the rifle pose)
- **Death and the head**: changed by 51. On death the body ragdolls (no directional death clip) and the head bursts at the same
  moment (51, 46). The headless corpse lies for the existing `Corpse` time. The body never puffs, bursts or bleeds, at any gore level.
- **Speeds**: the playback rate is matched to this game's move speeds (BR's `LocomotionClipSpeeds`), including the flag carrier's 10%
  slower speed and Frenzy's speed (49).
- **First person** stays as it is: the camera's floating hands and weapon (`WeaponView`). Your own body is seen only by the death
  camera (`DeathCamera`) and in previews. The floating hands take the team's glove colour.

## Missing clips (Mixamo, onto the Synty skeleton)

The Mixamo download needs the user's login. The steps are in BR's `docs/animations.md`, "Missing clips": upload medievalminiguns'
`FantasyKingdom_Characters.fbx`, download FBX for Unity, Without Skin, 30 fps, In Place, into the `Mixamo/` drop folder, and the
import validates it. List:

| Save as | Mixamo search | Loops | Why | Priority |
|---|---|---|---|---|
| `body_pistol_idle` | "Pistol Idle" | yes | pistol and dual pistols | needed |
| `body_pistol_run_f/b/l/r` | "Pistol Run" / "Pistol Walk Backward" / "Pistol Strafe" | yes | pistol locomotion | needed |
| `body_jump_up`, `body_fall_loop`, `body_land` | "Jump" (split), "Falling Idle", "Falling To Landing" | loop: fall only | the game jumps a lot (the kitchen edge, the toy box) | needed |
| `body_heavy_idle`, `body_heavy_run_f` | "Rifle Idle" with a heavy weapon / "Heavy Weapon Run" | yes | minigun and flamethrower | optional (the rifle set stands in) |
| `body_roar` | "Mutant Roaring" | no | using a monster power (49) | optional |
| `body_victory` | "Victory" / "Fist Pump" | no | the winners on the end card | optional (the dances stand in) |

Until a clip is in, its stand-in comes from `SoldierClipMap` (the rifle set), so nothing waits on the downloads.

## Done when

- The clips import with zero unresolved bindings, and the ported EditMode tests (clip catalogue, controller, combat rules) pass.
- In Play mode, 7 bots and you run, strafe, jump, fire, carry the flag and die in the right direction, with no foot sliding at the
  game's speeds. Capture strips go in `Media/` or the ticket.
- The missing-clip list, with stand-ins, is in this ticket's status.

## Status

Done in code and tested; left for a human: the Play-mode look, and the Mixamo clips below. Ported from BattleRoyale `80df274`
(`80df27498e87bb919a25049b4c1c6d1435023b61`), the clips with their `.meta` GUIDs.

What was ported (all under `Unity/Assets/Battle/Body/`, assemblies `SdfBody`, `SdfBody.Editor`, `SdfBody.Tests`):

- **Clips** (`Animations/`, 20 of BR's 37): 13 locomotion (walk and run, eight ways, aim idle), `soldier_fire_rifle`, the burst, reload,
  hit, `soldier_jump` (the rifle jump), the two dances. Dropped: the six death clips and their `_alt` copies, the two `_alt` runs, the
  grenade and the two turns (nothing here uses them). `SoldierClips` (the catalogue and the gap table), `SoldierClipMap`, and the
  editor side: `AnimationImportFixer`, `SoldierAnimationPostprocessor`, `MixamoDrop`, `ClipMapBuilder`, `SyntySkeletonPaths`.
- **Animator** (`Animator/`, `Avatar/UpperBody.mask`, `Editor/`): `SoldierControllerBuilder` (the base layer: three locomotion trees blended
  by `Stance` (rifle, pistol, heavy), the air (JumpUp, Fall, Land), the dances), `CombatLayerBuilder` (the `UpperBody` layer: fire,
  burst, reload, hit), `CombatClipMirror` (the mirrored flinch), `CombatSkeleton`, `LocomotionClipSpeeds` (re-measured on the soldier
  prefab, not BR's stick). Menu `Sdf/Body/Build All` or `tools/unity-batch.sh body -executeMethod SdfBody.Editor.BodyAssets.BuildAllFromCommandLine`.
- **Runtime** (`Runtime/`): `SoldierAnimator`, `LocomotionMath`, `LocomotionClips`, `LocomotionTuning`, `CombatTuning`, `CombatRules`.
  Cut from BR: the crouch, sprint and parachute layers and clips, the loot clips, the tier culling (`SimTier`, BR's `Seams`), the
  directional death layer and its measures. **`DeathLayerBuilder` was not tangled with the controller builder (it is a separate layer
  builder found by type), so it was not ported at all.**
- **Death**: `SoldierAnimator.Died(hitDirection)` is the hook for 51's ragdoll: it stops the animation, holds the last pose (the Animator is
  turned off) and raises `WasKilled(animator, flatPushDirection)`. Done by 51: `Gore` hands the frozen pose to a `SoldierRagdoll`; before that the body simply stood frozen where it was shot, until the
  player appears again (`Revive`). Falls and rule removals hide the body as before.
- **Speeds**: the tree's run speed is the game's walk speed (4.4 m/s); the flag carrier (0.9) and Frenzy (1.6) land on the tree by their
  real velocity, with the sprint ring out to 1.65; the playback is matched per frame (`LocomotionMath.Playback`), so no foot sliding is
  expected at any of the three. A test checks the feet against the ground at those speeds for all three stances.

How the game is wired (Assembly-CSharp, `Battle/Runtime/`):

- `SoldierPuppet` (new) is made in `Player.Bind` for you and every bot, from `SoldierModels.Prefab` (red or blue skin by team); its
  hit capsules are removed (the player's capsule is what is hit, as with the scout). It feeds `SoldierAnimator` each frame from the
  `CharacterController`: velocity in the body's frame, jump (leaving the ground at more than 2 m/s up), airborne (off the ground for 0.1 s),
  and the stance of the weapon in hand. `Arsenal` reports shots (`BodyAnimationRules.AutomaticFire`: minigun, flamethrower, electro-gun
  loop the burst; sniper, pistols, poison-gun and blob-gun restart the single shot; the flamethrower fires every frame it is held),
  `Combat` the hits (a flinch to the side it came from, not for burning) and the death.
- Your own body is hidden in first person and shown by `DeathCamera` while you watch your death; bots' bodies are drawn.
- `WeaponView` puts a bot's weapon at the soldier's `grip` socket (dual pistols also the `support` socket), full size; `Spores`' Thick Hide
  swells the body root; `Gore` no longer builds a scout corpse over a soldier (blood and the puff stay; the head and the ragdoll are 45
  and 51).
- The mushroom scout body (`ScoutBody`) is now only the fallback when the soldier models were not imported (a fresh clone before `sdf unity`
  and `SdfRig.Editor.SoldierSkins.ImportAll`); with them imported nothing draws it. The carried flag stays at its carry offset on the
  player (not on the `back` socket yet), and the upper body keeps the weapon's stance while carrying.
- Not used yet: `Reload` (the game has no reload), the `back` and `head_top` sockets, the hit capsules (45).

The missing clips (Mixamo, the user's login). Steps: upload `~/Desktop/gdev/bitbucket/medievalmagicmadness/medievalminiguns/Assets/Models/FantasyKingdom_Characters.fbx`
to mixamo.com (Upload Character, so the clips key the Synty bone names), search each name, tick In Place, download FBX for Unity,
Skin: Without Skin, 30 fps, Keyframe Reduction none, save as the file name below into
`Unity/Assets/Battle/Body/Animations/Mixamo/` (nothing else in that folder). The import validates a file by itself (see the console), then
run `Sdf/Body/Build All` (the controller), and `Sdf/Body/Measure Locomotion Clip Speeds` after the pistol and heavy runs, then the
EditMode tests (a red `MixamoDropTests` case names the file and the problem).

| Save as | Mixamo search | Needed | Plays until it exists |
|---|---|---|---|
| `body_pistol_idle.fbx` | "Pistol Idle" | needed | the rifle aim idle |
| `body_pistol_run_f.fbx` | "Pistol Run" | needed | the rifle run |
| `body_pistol_run_b.fbx` | "Pistol Walk Backward" | needed | the rifle run back |
| `body_pistol_run_l.fbx`, `body_pistol_run_r.fbx` | "Pistol Strafe" (left, right) | needed | the rifle run left, right |
| `body_jump_up.fbx` | "Jump" (the take-off half) | needed | the rifle jump |
| `body_fall_loop.fbx` | "Falling Idle" | needed | the rifle jump held at its fall pose |
| `body_land.fbx` | "Falling To Landing" | needed | the rifle jump held at its fall pose |
| `body_heavy_idle.fbx` | "Rifle Idle" with a heavy weapon | optional | the rifle aim idle |
| `body_heavy_run_f.fbx` | "Heavy Weapon Run" | optional | the rifle run |
| `body_roar.fbx` | "Mutant Roaring" | optional | nothing plays (no state until it exists) |
| `body_victory.fbx` | "Victory" or "Fist Pump" | optional | the first dance |

(12 files: 8 needed, 4 optional.)

Checks: EditMode 615 passed, 0 failed (147 of them this ticket's: clip catalogue 26, Mixamo drop 3, locomotion maths 30, game speeds 15,
clip speeds 18, combat rules 5, controller, clip bindings on the soldier and animator 31, weapon fire modes and stances 19). Capture:
`Media/body-44/body-poses.png` (red above blue: idle, run, run with the heavy set, the fire burst, the jump's fall; the heavy and the
rifle poses are the same clips until the Mixamo ones are in).

A Play-mode smoke test (`SdfBody.PlayTests`, `tools/unity-batch.sh play44 -runTests -testPlatform PlayMode`; it unpauses the Meadow, which starts paused behind its menu) passed: 8 soldiers, 7 drawn and yours hidden, 5 of 8 running with trees near MoveY 1, no errors logged.

Left for a Play-mode look by eye: that 7 bots and you run, strafe, jump, fire and carry the flag with no foot sliding at the game's speeds;
that the held weapons sit right in the soldier's hands (the sockets, the pitch of the aim); the frozen body on death; the web frame time
with 8 animated bodies on the Kitchen (43's open item).
