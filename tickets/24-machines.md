# 24 — Machines: helicopters, tanks and turrets

Each side gets fungal-looking machines that fight for it on their own: **1 helicopter**, **2 tanks** and **4 turrets** per side,
14 in all. They only shoot, die and come back; they do not play the flag. First on the fungal level.

Depends on: 22 (health, damage, hit detection, kill feed), 23 (the navmesh, sight and aim from the bots), 18 (red vs blue).

## Decisions (from the user)

- **Fungal looking**: they belong to the scene's theme (README: one theme per scene), mushroom forms, not real machines.
- The same cartoonish **floating eyes** as the mushroom scouts, looking at what they aim at.
- Size: helicopter and tank at most **80 cm**; turrets about **2 m** high.
- Damage per hit: **half the pistol's** (6 against its 12; ticket 22's table).
- They die and **respawn after 30 s**: helicopters and tanks **behind their own castle**, turrets where they stood.
- **Kill feed**: a machine killing a player gives a kill message ("Blue tank [cannon] Red 2"); a machine being killed gives **none**.
- They **do not play the flag**: they never take, carry or return a flag, and the flag's triggers ignore them.
- Defaults the user accepted:
  - Health: tank 150, helicopter 100, turret 250.
  - Range: turret 30 m, tank 25 m, helicopter 35 m.
  - Targets: enemy **players only**, never machines, never their own team.
  - Fire rate about 1 shot per second, with the bots' reaction time and aim error (23), so they are a nuisance, not a wall.
  - Tanks have a solid collider; nobody stands on them (you slide off), they steer round players and do not push or get pushed.
  - Only the fungal level for now; the meadow and the chessboard (a closed room: a lower flight ceiling) later.

## 24.1 — Models

SDF models in `content/models/machines/`, team materials in `materials/machines/`, each meshed per colour (red, blue), toon look,
3 LODs, budget-minded (14 of them on screen at once, next to 8 players).

- **Tank** (≤ 80 cm long): a squat mushroom body on two rounded tread rolls; the turret is a mushroom **cap** that turns, with a short
  stem-like barrel; the eyes float over the cap. Parts: `Body`, `Turret` (turns about y), `Barrel` (tilts).
- **Helicopter** (≤ 80 cm): a round puffball body, the rotor a flat spinning **cap** (or gill-ring) on a stalk, a little tail with a
  tail rotor; eyes at the front. Parts: `Body`, `Rotor`, `TailRotor` (spun by script).
- **Turret** (~2 m): a thick fungal stem rooted in a mossy foot, a cap head that turns, the barrel from under the cap's rim; eyes on
  the cap's front. Parts: `Foot`, `Head` (turns), `Barrel` (tilts).
- The eyes: the scouts' floating eye part, so they look the same; two per machine, children of the turning part.
- Tests: bounds (tank and helicopter within 0.8 m, turret about 2 m), parts present.

## 24.2 — Placement in the level

- `fungal.level`: under each side in `sides`, `turret at=(x, z) yaw=..` ×4 round the castle (outside the walls, covering the
  approaches, clear of the gate, the pods, the fairy rings and the team mushrooms) and a `machines at=(x, z) radius=..` zone
  **behind** the castle where helicopters and tanks start and respawn (the castles are at x = ±62, the play area ends at ±75:
  check that it is reachable ground). Mirrored like the rest of the side.
- Parser rules and the bake as in 17 (exactly 4 turrets and one machine zone per side when the level has machines; levels without
  are allowed). `BattleSide` gains `turrets` and `machineSpawn`.
- Tests as in 17: mirrored, in the play area, no overlap with placed models or battle objects.

## 24.3 — Behaviour

- `Machine` base: team, health (22's `Health`), sight (range + line of sight, from 23), target choice (nearest visible enemy
  player), aim with reaction time and error, firing (hitscan, 6 per hit, ~1/s, a tracer from 22's effects), the eyes looking at
  the target (idle: looking about).
- **Turret**: stands; turns the head to the target, else sweeps slowly. Respawns in place after 30 s.
- **Tank**: drives to random points on the navmesh (ground only: no stairs, no wall-walks; a navmesh agent type for 0.8 m), slows
  to fire, turns the turret to the target. Steers round players. Respawns in the machine zone after 30 s.
- **Helicopter**: flies to random points over the play area at 6-12 m, smoothly, tilting into its flight; keeps above the castles'
  tops (about 20 m) where it crosses them or flies round them, never into the hills; rotors spin. Fires down at targets in range.
  Respawns in the machine zone after 30 s (rises from the ground).
- Death: a small burst (a variant of ticket 8's exploding mushroom pieces), the machine hidden until it respawns.
- Kill feed: a player killed by a machine is reported with the machine as the killer; a machine's death is not reported and gives
  no kill credit.
- The flag triggers (21) and the pods (16, 17) ignore machines.

## Done when

In Play mode on the fungal level, with ticket 23's bots: the 14 machines move, aim with their eyes, shoot enemy players for 6 per
hit, die and come back after 30 s in the right places, and appear in the kill feed only as killers. Frame time within the web
budget with everything on. EditMode tests for targeting (enemy players only), damage and the respawn timers.

## Status

**24.1 (models) done in the pipeline**; 24.2 and 24.3 not started. Left for 24.1: the prefabs in Unity (they build by themselves
when Unity picks up the meshes; else Tools > SDF Mushrooms > Rebuild All, then Tools > SDF Machines > Rebuild Machines) and a look.

### 24.1 — what was built

- **One model per moving piece**, `content/models/machines/<machine>-<team>-<piece>.sdf`, each with its **origin at its pivot**, so
  every piece is a prefab of its own with a sensible pivot (a part used once would be meshed in the model's space, at the model's
  origin, so named parts could not give pivots). 18 models: `tank-{red,blue}-{body,turret,barrel}`,
  `helicopter-{red,blue}-{body,rotor,tail-rotor}`, `turret-{red,blue}-{foot,head,barrel}`. Blue is red's file with the blue
  materials (`MachinesTests.Blue` keeps them in step; edit red and regenerate blue).
- **Machine prefabs** `Assets/Machines/Prefabs/{tank,helicopter,turret}-{red,blue}.prefab`, built by
  `Assets/Machines/Editor/MachineBuilder.cs` (like the flag stands: rebuilt when missing, stale or after `MushroomBuilder` builds; by
  hand Tools > SDF Machines > Rebuild Machines). Each piece is an empty **pivot object** at its hinge with the piece's prefab under it
  as `Model`, so turning the pivot's local rotation turns the piece about its hinge. `MachineRig` (`Assets/Machines/Runtime/`, data
  only) on the root points at `body`, `turn`, `tilt` and the two `eyes`.
- **Eyes**: `eye_machine` (`content/parts/machines/eye_machine.sdf`), the scouts' eye meshed coarser: the same 12 cm eyeball with
  the same painted rings (limbus, teal iris, amber ring, black pupil) and the same `materials/characters/eye`, under a root
  `Detail detail=0.26`, and with the rings cut to z > 0.05 instead of the front half (the cut at z = 0 broke the coarse mesh into
  thousands of split vertices on every other grid size). The scouts keep `content/parts/eye.sdf` as it was. Two per machine through
  `SymmetryX`, so the pipeline meshes it once and each eye is a child of the piece's model, `Model/eye_machine.0` and
  `Model/eye_machine.1`, at its own transform (one mirrored, negative x scale), looking along local +z at rest. Tank and helicopter
  at scale 0.55 (13 cm across), the turret at 1 (24 cm, the scouts' size). `MachineBuilder` (version 2) puts these two into `MachineRig.eyes`.
- **Materials** `materials/machines/`: `machine_{red,blue}_cap` (red a glossy fly agaric scarlet with cream warts, blue the
  chanterelle scout's cornflower blue with pale flecks), `machine_{red,blue}_flesh` (the scouts' rosy red / sky blue stem flesh),
  `machine_{red,blue}_gills` (pale apricot / ice blue), and the shared `machine_tread` (umber) and `machine_moss`.

**Pivots** (each piece's pivot object nests under its parent's, beside the parent's `Model`; Unity coordinates: the .sdf's x negated, as for every mesh; +z forward, +y up; each in its parent's space):

| Machine | Piece (prefab child) | Pivot | Moves |
|---|---|---|---|
| tank | `Body` | (0, 0, 0), on the ground under the middle | (the tank) |
| | `Body/Turret` | (0, 0.3, 0), the hull's top | turns about local y; carries the eyes |
| | `Body/Turret/Barrel` | (0, 0.05, 0.13) in the turret, under the cap's front rim | tilts about local x (negative x up in the .sdf; check the sign in Unity) |
| helicopter | `Body` | (0, 0, 0), the skids on the ground | (the helicopter); carries the eyes |
| | `Body/Rotor` | (0, 0.56, 0), the mast's top | spins about local y |
| | `Body/TailRotor` | (-0.025, 0.4, -0.35) (the .sdf's +x side) | spins about local x |
| turret | `Foot` | (0, 0, 0), on the ground at the stem | (stands) |
| | `Foot/Head` | (0, 1.5, 0), the stem's top | turns about local y; carries the eyes |
| | `Foot/Head/Barrel` | (0, 0, 0.3) in the head, under the cap's rim | tilts about local x |

**The models** (toon renders checked from all sides, posed with the turret/head turned and the barrel tilted):

- **Tank** (about 0.6 × 0.71 × 0.72 m, w × h × l, within 0.8 m turned any way; the barrel reaches 0.38 m from the turret's axis): a
  squat rosy hull between two umber tread rolls, each a row of five puffball segments on a core roll like a fat caterpillar; the
  turret a glossy team-coloured cap with warts and pale gills on a short collar; a short stem barrel from a knuckle under the cap's
  front rim with a gill band and a trumpet-flared muzzle; the eyes float over the cap.
- **Helicopter** (about 0.71 × 0.67 × 0.74 m): a round team-coloured puffball on two little stem-flesh skids, a short mast with a flat,
  wavy five-lobed cap as the rotor (0.7 m across, gills under it), a tapering stalk tail with a little four-lobed cap as the tail
  rotor; the eyes float in front.
- **Turret** (about 2.0 m high, 1.2 m wide): a thick fibrous stem swelling to a bulb in a mossy mound with roots, a pale ring
  skirt high on it; the head a big team cap (1.2 m across) on a collar; the barrel a long stem with two gill bands and a flared
  muzzle from under the cap's front rim (reach 0.9 m from the axis); full-size eyes float in front of the cap.
- `collider convex` on the tank's body and turret, the helicopter's body, the turret's foot and head (and so on their eyes); none on
  the barrels and rotors.

**Meshed** at 256 cells with 3 LODs, plain and `_256`, into `Unity/Assets/Mushrooms/Generated/machines/`. Triangles, LOD0 / LOD1 /
LOD2 of the pieces' own meshes (the eye mesh comes on top, two per machine: 582 / 168 / 90 per eye, from the scouts' eye's 4,254 /
1,318 / 716):

| Piece | LOD0 | LOD1 | LOD2 |
|---|---|---|---|
| tank body / turret / barrel | 6,612 / 2,964 / 2,422 | 1,858 / 686 / 584 | 800 / 284 / 308 |
| helicopter body / rotor / tail rotor | 4,034 / 1,874 / 1,270 | 962 / 272 / 218 | 396 / 114 / 104 |
| turret foot / head / barrel | 8,600 / 2,394 / 4,118 | 1,798 / 532 / 1,060 | 682 / 196 / 408 |

A whole machine with its eyes: tank 13.2k / 3.5k / 1.6k, helicopter 8.3k / 1.8k / 0.8k, turret 16.3k / 3.7k / 1.5k (before the
coarser eye: 20.5k / 5.8k / 2.8k, 15.7k / 4.1k / 2.0k, 23.6k / 6.0k / 2.7k). All 14 (4 tanks, 2 helicopters, 8 turrets) at LOD0
about 200k, at LOD1 (the usual distance) about 47k, at LOD2 about 20k. The eyes are now 7-14% of a machine (1,164 triangles a pair
at LOD0). They keep their look at the machines' sizes: white ball, teal iris with its dark limbus edge, amber ring and a round-ish
black pupil at LOD0, a pentagon pupil at LOD1; at LOD2 the pupil thins to a sliver, but that level is only drawn far away. The
eye's shape follows the grid, so check a render of the .glb before changing its `Detail`.

**Tests** `SDFPipeline.Tests/Models/MachinesTests.cs`: the machines put together at these pivots (the same table in the test) are
within 0.8 m with the moving pieces turned 0, 90 and 180 degrees, and on the ground; the turret 1.85-2.15 m high; every piece is a
model whose mesh split is its body plus, on the turning piece only, the `eye_machine` part with 2 instances and a detail below 1
(never the scouts' `eye`); `eye_machine` is the scouts' eye's shape with the same materials; each piece touches its parent
at its pivot; blue matches red.
