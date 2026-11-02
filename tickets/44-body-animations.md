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
  - hit and death direction (directional deaths)
  - the carried flag (the upper body stays in the rifle pose)
- **Death and the head**: on death the body plays its directional death and the head explodes at the same moment (46). The
  headless corpse lies for the existing `Corpse` time. With gore Off, the whole body goes in the cartoon puff instead.
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

Not started.
