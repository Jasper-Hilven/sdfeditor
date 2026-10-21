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
scouts: west amanita red, east chanterelle gold.

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
- `content/models/pickups/flag-red.sdf` and `flag-gold.sdf` (or one flag model and two materials, whichever the pipeline does
  better): a pole of about 2 m with a cloth; separate from the stand so a player can carry it later.
- `Assets/Pickups/Runtime/FlagStand.cs`: holds the side id and a reference to its `Flag` child (the home position). No CTF rules yet;
  just the data a later CTF mode needs.
- The builder writes `flag-stand-west.prefab` and `flag-stand-east.prefab` (stand + flag of that colour + `FlagStand`).
- Tests: bounds and the walkable base in `SDFPipeline.Tests/Models/`.

**Status 17.2: done**, except what only Play mode shows. Built:

- `flag-stand.sdf`: stone base (`pod_stone`), foot radius 1.05 m, flat top 1.5 m across at 0.25 m, the pod's bevel (about 40
  degrees); a `pod_glow` ring inlaid at 0.42 to 0.52 m; a brass socket collar (`flag_brass`) up to 0.3 m (the pod's height) with
  the pole's hole down to 0.15 m. `collider convex`. Meshed at 256 with 3 LODs: 3.6k, 0.9k, 0.4k triangles.
- `flag-red.sdf` / `flag-gold.sdf`: two models (the toon colour comes per vertex, so one model cannot take two materials), the same
  shape: a 2 m wooden pole (`flag_pole`) with a brass knob, foot on the origin; a 0.9 × 0.6 m cloth flying towards +x with two soft
  waves (two `Arc2D`s extruded), a swallowtail and a sleeve. `collider none`. 1.3k, 0.3k, 0.1k triangles. Materials
  `materials/pickups/flag_red` (amanita scarlet, white flecks), `flag_gold` (chanterelle gold), `flag_pole`, `flag_brass`.
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
`flag-red.glb`, `flag-gold.glb`), then open Unity or run `LevelBuilder.BuildAllInBatch` (mesh assets, the three model prefabs are in
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

## Open

- The chessboard's theme is board games, and the pods are stone with a teal glow. Pods may count as gameplay (like the players) or
  may need a board-game look there; decide on seeing it.

## Status

17.1 and 17.2 done (see their status above); 17.3 not started.
