# 17 — Battle sides

Every level is fought by two **battle sides**, one at each end. Each side has its own set of gameplay objects in the static scene,
and a `BattleSide` component that points to them, so game code finds a side's spawn, flag and pods without searching by name.
Besides the per-side sets, each level holds two **neutral pods**, one from each pool below, between the sides.

Depends on: 16 (the weapon pods, `WeaponPodBuilder`, `WeaponPickup`).

## Per side (both sides, every level)

| Object | What it is |
|---|---|
| Minigun pod | `minigun-pod.prefab` (ticket 16) |
| Pistol pod | `pistol-pod.prefab` (ticket 16) |
| Health pod | **new**, `health-pod.prefab`: the weapon pod with a floating health cross instead of a weapon (17.1) |
| Flag stand | **new**, `flag-stand.prefab`: a stand holding the side's flag, for capture the flag (17.2) |
| Spawn place | an empty marker (position and facing), drawn as a gizmo in the editor; no model |

## Neutral, one of each per level

- one of **sniper** or **electro-gun**;
- one of **flamethrower**, **poison-gun** or **blob-gun**.

| Level | Long-range pod | Short-range pod |
|---|---|---|
| meadow | sniper | flamethrower |
| fungal | electro-gun | poison-gun |
| chessboard | sniper | blob-gun |

(A default; the level file holds the choice, so it is a one-word change.)

## The sides

Sides are `west` (−x) and `east` (+x). On the meadow and the fungal level each side is its castle: the flag stand in the courtyard,
the spawn just inside, the pods near the gate. On the chessboard (no castles) each side is a back rank. Flag colours follow the two
scouts: west amanita red, east chanterelle blue (ticket 18).

---

## 17.1 — Health pod

- `content/models/pickups/health-cross.sdf`: a chunky rounded plus (about 0.5 m across, 0.15 m thick), white with a red border or red
  with a white rim, toon materials in `materials/pickups/`. Meshed like the weapons, LODs as props.
- `Assets/Pickups/Runtime/HealthPickup.cs`: like `WeaponPickup` (trigger, hide, respawn after `respawnSeconds`, 20 by default),
  with an `amount` (50). The game has no health system, so it raises `onPickedUp` (`UnityEvent<int>`, the amount) and a static
  `HealthPickup.PickedUp` (pod, player object), nothing more.
- `WeaponPodBuilder` (or a small shared helper it calls) also writes `Assets/Pickups/Pods/health-pod.prefab`: `Pod`, `Cross`
  (`SpinHover`, same heights as the weapons) with `Model`, the trigger capsule and `HealthPickup`. Same rebuild rules as the weapon
  pods; `BuilderVersion` bumped.
- Tests: the cross's bounds in `SDFPipeline.Tests/Models/`.

**Status 17.1: done**, except what only Play mode shows. `health-cross.sdf` is a white plus (arms 0.5 m, 17 cm wide, 13 cm thick)
with a red plus 3 cm inside it standing 1 cm proud on both faces (15 cm thick overall), each drawn flat (`SmoothUnion2D` of two
`Box2D`, `Round2D`), extruded and rounded; materials `health_white` and `health_red` (red subsurface glow). Meshed at 256 with 3 LODs:
5.5k, 1.2k and 0.5k triangles. `HealthPodBuilder` (its own file, called from `WeaponPodBuilder.BuildAll`/`BuildStale`, sharing its
stamp, pod height, trigger and `Measure`) writes `health-pod.prefab`; `BuilderVersion` 2. Built in the worktree's own `Unity/`
(Library and Generated cloned from the main tree): the pod at the origin, `Cross` at y = 1.302, the trigger capsule, `HealthPickup`
(amount 50, respawn 20 s). `HealthCrossTests` (bounds; solid arms, open corners, thickness) pass.
Generated files to make in the main checkout: `sdf unity --resolution 256 --lods 3` (writes `Generated/pickups/health-cross.glb`), then
open Unity or run `LevelBuilder.BuildAllInBatch`: it rebuilds `health-cross.prefab` and, with the version bump, every pod.

## 17.2 — Flag stand

- `content/models/pickups/flag-stand.sdf`: a low base the player can walk onto or around (no higher than the weapon pod, same bevel
  rule), with a socket in the middle for the flag pole.
- `content/models/pickups/flag-red.sdf` and `flag-blue.sdf` (or one flag model and two materials, whichever the pipeline does
  better): a pole of about 2 m with a cloth; separate from the stand so a player can carry it later.
- `Assets/Pickups/Runtime/FlagStand.cs`: holds the side id and a reference to its `Flag` child (the home position). No CTF rules yet;
  just the data a later CTF mode needs.
- The builder writes `flag-stand-west.prefab` and `flag-stand-east.prefab` (stand + flag of that colour + `FlagStand`).
- Tests: bounds and the walkable base in `SDFPipeline.Tests/Models/`.

**Status 17.2: done**, except what only Play mode shows. Built:

- `flag-stand.sdf`: stone base (`pod_stone`), foot radius 1.05 m, flat top 1.5 m across at 0.25 m, the pod's bevel (about 40
  degrees); a `pod_glow` ring inlaid at 0.42 to 0.52 m; a brass socket collar (`flag_brass`) up to 0.3 m (the pod's height) with
  the pole's hole down to 0.15 m. `collider convex`. Meshed at 256 with 3 LODs: 3.6k, 0.9k, 0.4k triangles.
- `flag-red.sdf` / `flag-blue.sdf`: two models (the toon colour comes per vertex, so one model cannot take two materials), the same
  shape: a 2 m wooden pole (`flag_pole`) with a brass knob, foot on the origin; a 0.9 × 0.6 m cloth flying towards +x with two soft
  waves (two `Arc2D`s extruded), a swallowtail and a sleeve. `collider none`. 1.3k, 0.3k, 0.1k triangles. Materials
  `materials/pickups/flag_red` (plain amanita scarlet), `flag_blue` (plain chanterelle blue, ticket 18), `flag_pole`,
  `flag_brass`.
- `Assets/Pickups/Runtime/FlagStand.cs`: `side` (`BattleSideId` West/East, in the same file, for 17.3 to reuse), `flag` (the `Flag`
  child), `homePosition`/`homeRotation`, `FlagAtHome`, `ReturnFlag()`.
- `Assets/Pickups/Editor/FlagStandBuilder.cs` writes `Assets/Pickups/Flags/flag-stand-west.prefab` and `flag-stand-east.prefab`:
  `Stand` (the flag-stand prefab), `Flag` (pivot at the socket's bottom, y = 0.15) with `Model` (the flag's prefab), `FlagStand` on
  the root. Same rebuild rules as the pods (own `BuilderVersion`, editor load, after `MushroomBuilder`, `LevelBuilder.BuildAllInBatch`;
  `Tools > SDF Pickups > Rebuild Flag Stands`; batch `SdfPickups.Editor.FlagStandBuilder.BuildAllInBatch`).
- `SDFPipeline.Tests/Models/FlagStandTests.cs`: stand bounds, flat top, nothing above 0.3 m, bevel under the slope limit, the socket;
  each flag's bounds, pole foot fits the socket, the cloth; both flags the same shape.

Left: a Play-mode look (walking onto the stand, the flag's size in the courtyard, how the thin cloth's LOD2 and ink line read far off).
Generated files to make in the main checkout: `sdf unity --resolution 256 --lods 3` (writes `Generated/pickups/flag-stand.glb`,
`flag-red.glb`, `flag-blue.glb`), then open Unity or run `LevelBuilder.BuildAllInBatch` (mesh assets, the three model prefabs are in
git, the two stands are rebuilt if stale).

## 17.3 — Sides in the level file and the scene

- **Level file**: a new `sides` block in `content/levels/*.level` (parsed in `SDFPipeline/Levels/LevelFile.cs`, written to the bake
  output Unity reads), for example:

  ```
  sides
      west
          spawn at=(-58, 0) yaw=90
          flag at=(-66, 0)
          pod minigun at=(-50, 6)
          pod pistol at=(-50, -6)
          pod health at=(-54, 10)
      east
          ...
  neutral
      pod sniper at=(0, 30)
      pod flamethrower at=(0, -30)
  ```

  Heights come from the ground (like `place`), or from the castle floor where an object stands inside a castle. The parser checks that
  each side has exactly one spawn, flag, minigun, pistol and health, and that `neutral` has exactly one of {sniper, electro-gun} and
  one of {flamethrower, poison-gun, blob-gun}.
- **Unity**: `LevelBuilder` puts the objects under a `Battle` root in the level scene: `Battle/West`, `Battle/East` (each with a
  `BattleSide` component: side id, `spawn` Transform, `flagStand`, `minigunPod`, `pistolPod`, `healthPod`) and `Battle/Neutral`
  (pods, referenced from a `BattleArena` component on `Battle` along with both sides).
- **Placements** in `meadow.level`, `fungal.level` and `chessboard.level`, mirrored between the sides, clear of rocks, pieces, the
  path's middle and castle walls; nothing in a spot the player cannot reach.
- **Tests**: `LevelFileTests` for the parser rules and that every level has complete, mirrored sides; a check that no pod or stand
  overlaps a placed model.
- Rebuild `Meadow.unity`, `Fungal.unity` and `Chessboard.unity`.

**Status 17.3: done in the pipeline and the builder; the scenes are to be rebuilt in Unity.** Built:

- Format: `sides` with `west` and `east` below it, each with `spawn`, `flag`, `pod minigun`, `pod pistol`, `pod health` (each `at=(x, z)`,
  optional `yaw`), and `neutral` with two `pod <pickup>` lines. The parser (`LevelFile.ReadSides`/`ReadNeutral`, records in
  `SDFPipeline/Levels/BattleLayout.cs`) checks: both sides, each exactly once and with exactly one of each object; side pods only
  minigun, pistol, health; neutral exactly one of {sniper, electro-gun} and one of {flamethrower, poison-gun, blob-gun}; `sides` and
  `neutral` together or not at all. The bake rejects an object not wholly inside the playable area, and writes level.json `battle`
  (`sides[]`: side, spawn, flag, pods[]; `neutral[]`; each position on the ground, yaw). Heights are the ground's: inside a castle that is
  the courtyard's floor (the castles have no floor of their own there).
- The scatter (grass, flowers, trees, rocks, giants) leaves out instances near a battle object (its radius + the layer's margin + 0.2 m,
  or 1.5 m for trees sections), after its random draws, so the rest of the scatter stays where it was.
- Placements, west mirrored to east (x negated, yaw negated). Meadow: flag (∓60.5, 0) in the courtyard before the keep, spawn
  (∓57, 0) just inside the gate facing out, minigun (∓48, 6) and pistol (∓48, −6) either side of the apron before the gate,
  health (∓51, 11); neutral on x = 0: sniper (0, 32) and flamethrower (0, −32).
  Fungal (moved after review): flag (∓61.5, 0) in the middle of the castle, half a metre towards the gate to clear the keep stairs'
  foot; spawn (∓57, 0); minigun (∓46, 38) and pistol (∓44, −22) each in a fairy ring near the castle (the east rings moved from
  (46, 36) and (44, −40) to mirror the west ones); health (∓28, −43) on the south flank; electro-gun (−8, 24) and poison-gun
  (18, −34) in the two fairy rings nearer the middle.
  Chessboard: flag (∓72, −4), spawn (∓66, −4) facing the other side, minigun (∓66, 6), pistol (∓66, −14), health (∓74, −14); sniper
  (0, 16), blob-gun (0, −12).
- Unity: `Assets/Battle/Runtime/BattleSide.cs` (side id, `spawn`, `flagStand`, `minigunPod`, `pistolPod`, `healthPod`; draws the spawn
  marker gizmo, a player-sized outline with an arrow, in the side's colour) and `BattleArena.cs` (`west`, `east`, `neutralPods`,
  `Current`). `LevelBuilder.AddBattle` (version 14) builds `Battle` at the scene root (not static), `Battle/West`, `Battle/East` (Spawn,
  Flag Stand, Minigun/Pistol/Health Pod) and `Battle/Neutral`, from the pod and flag-stand prefabs; SceneInfo's `battleSides` (team 0
  west, 1 east) get each side's objects, and `IMMScene.battle` points at the arena.
- Tests: `LevelFileTests` (reading the sections; 18 error cases), `BattleSidesTests` (every level: complete sides mirrored across x;
  in the play area, clear of each other and off the path's / trail's middle; no overlap with a placed model, checked against the model's
  shape from 0.35 m to 2.3 m up so a courtyard is open and a wall is not, with a check that it finds a stand moved into a wall and a pod
  onto a rock; the scatter clearing; the bake's level.json).

Left: rebuild the three scenes in Unity (Tools > SDF Level > Rebuild) and a Play-mode look (the open question below, the flag's size in
the courtyard).

## Open

- The chessboard's theme is board games, and the pods are stone with a teal glow. Pods may count as gameplay (like the players) or
  may need a board-game look there; decide on seeing it.

## Status

17.1, 17.2 and 17.3 done (see their status above); left: rebuilding the three scenes in Unity and a Play-mode look.
