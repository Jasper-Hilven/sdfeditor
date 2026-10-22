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
  - Health: tank 150, helicopter 100, turret 250; later lowered by the user to **tank 60, helicopter 60, turret 90**.
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

**Done in code: 24.1 (models) and 24.2 (placement) in the pipeline, the bake and the builder; 24.3 (behaviour) in Unity's C#** (rules
in `SdfBattle.Core` with EditMode tests, the machines made and driven at runtime by the match); left: the Unity side checked in the open
editor. Left for 24.1: the prefabs in Unity (they build by themselves when Unity picks up the meshes; else Tools > SDF Mushrooms >
Rebuild All, then Tools > SDF Machines > Rebuild Machines) and a look. Left for 24.2 and 24.3: the scene rebuild in Unity
(LevelBuilder version 19 rebuilds the three scenes by itself; else Tools > SDF Level > Rebuild), Tools > SDF Battle > Check Navmesh,
the EditMode tests (Window > General > Test Runner > EditMode > Run All) and the Play-mode check of "Done when" on `Fungal.unity`
(below), with the web frame time.

### 24.3 — what was built

- **Rules in plain C#** (`Assets/Battle/Core`, `SdfBattle.Core`): `MachineRules.cs` (`MachineKind` tank / helicopter / turret;
  `MachineStats` per kind: health 150 / 100 / 250, range 25 / 35 / 30 m, 6 a hit (half the pistol's 12), a shot about every second,
  back after 30 s, the kill feed's weapon `cannon`, turn speed, speed; `MachineRules`: 1 helicopter and 2 tanks per side, the three
  stats, the aim's `BotSkill` at 0.45; `NameOf` gives "Blue tank"), `MachineState.cs` (`MachineState`: health (22's `Health`), alive,
  the 30 s wait, `Hurt(damage, allowed)` with 22's `CombatState.CanHurt(Team, Team)`, `Died`/`Respawned`, `Reset` for a new round; a
  machine's death is never a `Death`, so no kill feed and no kill credit; `MachineTarget` and `MachineTargeting.Choose`: the nearest
  enemy **player** in range that the line of sight sees: never a machine, never its own team, never the dead or spawn-protected; the
  line of sight asked nearest first and only until one is seen; the current target keeps a 3 m lead; `MachineGun`: 23's `BotAim`
  (reaction time, then the shrinking error, the tracking lag) and the fire rate, each wait 0.85-1.15 s, firing only once reacted and
  with the gun turned onto the aim), `MachineMotion.cs` (`MachineAim`: yaw and barrel tilt; `MachineSteering.Avoid`: the tank's slowing
  and turning aside for what is ahead, a stop for one right in front; `HelicopterPilot`: the whole flight, below).
- **The barrel's tilt sign, checked**: the barrel meshes point along their pivot's +z (the .glb's POSITION bounds: tank barrel z 0 to
  0.251, turret barrel z 0 to 0.58, x and y about 0; glTFast negates x only), and Unity's `Quaternion.Euler(a, 0, 0)` turns +z to
  (0, -sin a, cos a), so **raising a barrel is a negative angle about local x**: `MachineAim.TiltPerElevation = -1`,
  `TiltFor(direction, lowest, highest)`, tested with the same formula (`BarrelForward`).
- **EditMode tests** (`Assets/Battle/Tests/MachineTests.cs`, 18): the user's numbers; targets enemy players only (own team, enemy
  machine, dead and protected passed over), the nearest seen within range, rays asked nearest first and stopping at the first seen,
  the current target kept unless another is clearly nearer; the reaction before the first shot and about one shot a second (52-70 in
  a minute, gaps 0.84-1.17 s), no shot until the gun is on its aim, the bots' error that never reaches 0; a machine's hit does 6, not to
  its own team, 17 hits kill a player with the machine as the killer ("Blue tank", blue, `cannon`) and no kill credit; a machine takes
  damage, dies at 0, gives no kill credit and no reported death, is not hurt by its own team or in a countdown; every kind back after 30
  s at full health (turrets in place, the others in the zone), a new round brings them all back; the barrel's sign; the tank's stop and
  steering; the helicopter rising straight up first, then 6-12 m up inside the play area for 5 minutes, clearing a 15 m block by its 6 m,
  slowing and turning its nose to a target. All 165 battle tests pass outside Unity (net8 + NUnit against Unity's CoreModule).
- **Unity** (`Assets/Battle/Runtime`): `Machines` (on Battle, made by `Match.Start` after combat and before the bots, only on a level
  whose sides have turrets or a machine zone): gives each side's turrets (`BattleSide.turrets`, the scene's objects) a `TurretMachine`,
  and makes its helicopter and two tanks from `redTank`, `blueTank`, `redHelicopter`, `blueHelicopter` (set by `LevelBuilder.AddMachines`,
  version 19; in the editor loaded from `Assets/Machines/Prefabs/<kind>-<team>.prefab` when a scene has none) under Battle/Machines at
  free spots of the side's machine zone (`SpawnPicker`, 1.6 m apart, on the ground); a new round (`RoundReset`) puts every machine back,
  alive. Shared queries: `Visible` and `Shoot` (rays that pass the machine's own colliders), `Ground` (the first surface down that is
  neither player nor machine), `Surface` (the highest surface under a point, for the helicopters), `Machines.Of(collider)`.
  `Machine` (base, an `IDamageable` registered with `Combat.Register`): a kinematic `Rigidbody` on the root over all its colliders (moved
  by script: it pushes nobody and nobody pushes it; triggers of pods and flags react to players only, so they ignore it); looks four times a second at a
  staggered moment (`MachineTargeting`, the ray from the muzzle or the eyes); aims at the chest off by the gun's error; fires a hitscan
  ray from the muzzle (along the barrel for tank and turret, at the aim point for the helicopter) through `Combat.Hurt(player,
  Damage.ByMachine(name, team, "cannon", 6, muzzle))` and a pistol-coloured `CombatFx.Tracer`; the eyes look at the target's head (at most
  70 degrees off rest), else glance about every 1-3 s; at 0 health the burst (ticket 8's exploding mushroom, small and without pieces:
  the flash, sparks, puffs in the team colour and cream and spores into `CombatFx.Particles`; the turning piece (tank turret, turret
  head, rotor) thrown up spinning and shrinking, the rest squashed flat and away in a second), colliders off, hidden; back after 30 s
  with a pop (0.2 → 1.08 → 1). `TurretMachine`: head to the target at 70 deg/s, barrel -40 to +25 degrees, else a slow 55-degree sweep
  either side of its rest. `TankMachine`: random points of the play area on the **Tank** navmesh (ticket 23's agent type; no castle tops or
  wall-walks), plain path queries, a new way every 6-10 s or when stuck; the hull turns at 80 deg/s and drives at 2.2 m/s (0.3 of it
  while fighting), kept on the navmesh (`NavMesh.Raycast`) and on the ground (a ray down, the hull eased to its slope); turret 100 deg/s,
  barrel -15 to +30 degrees; slows and turns aside for players and machines within 3 m ahead and stops for one right in front; a player
  standing on it slides off (moved off its top, away from its middle). `HelicopterMachine`: flown by `HelicopterPilot` (straight up from
  the zone after every (re)spawn; then random points over the play area 6 m inside its edge, 6-12 m over what is under it, at 5 m/s with
  2.5 m/s² of acceleration, 1.5 m/s while fighting; always 6 m or more over the highest surface under it and 3 s ahead, sampled five
  times a second, holding back while it climbs, so it clears the castles' tops, about 20 m; never outside the play area, so never into
  the hills; leaning into its flight up to 16 degrees; under a closed room's ceiling later), nose (and eyes) turned to its target, firing
  within 30 degrees of its nose; rotor and tail rotor spinning (spinning up after a respawn).
- **Bots** (23, `Bot.cs`, small): with no player to fight, a bot shoots at the nearest enemy machine within 25 m in its view and sight
  (aim with its reaction and error, straight shots only), never makes one a goal; hurt by a machine it turns to where the shot came from
  (as before, from the damage's origin).
- **Kill feed and death screen**: a machine's kill is in the feed as "Blue tank [no icon] Red 2" (22's `MatchHud`); the death screen now
  says "by Blue tank" for a machine instead of "The island got you" (`FakeMatch.killedByName`, `GuiRoundResult`, `MatchHud`). A machine's
  death is in neither.
- **Spectating**: in Tools > SDF Battle > Spectate Bots, **M** follows the next machine (Shift+M the one before; its state, health and
  respawn wait at the bottom), Tab goes back to the players.
- **Frame cost** (estimated, not profiled): 14 machines look four times a second each at staggered moments with one or two rays a look
  (nearest enemy in range first); a tank casts one ray down a frame and finds a path every 6-10 s; a helicopter four rays down five times
  a second; the rest is rotations and a few vector sums; no per-frame allocations in the loops. No threads, no compute: WebGL 2 safe.
- Shared files touched (small, additive): `Match.cs` (the `Machines.Create` call; `rules` starting without a countdown, the user's
  change), `Bot.cs`, `BotSpectator.cs`, `MatchHud.cs`, `CombatFx.cs` (`Particles`), `LevelBuilder.cs` (`AddMachines`, version 19),
  `FakeMatch.cs` and `GuiRoundResult.cs` (the death screen). Also, from the user during this ticket: weapon pods keep their weapon when
  taken (`WeaponPickup.staysWhenTaken`, see ticket 16) and no countdown at a round's start (see ticket 20).

**Play-mode check** (`Fungal.unity`, after the rebuild): 4 turrets per side round each castle; a helicopter and two tanks per side
rising / driving out of the zone off the castle's back north corner; the helicopters roam the field at 6-12 m and over the castles
higher, never into the hills, rotors spinning, leaning; tanks drive to points on the field (not up stairs or onto wall-walks), slow and
turn their turrets on enemies, go round you, you slide off their tops; eyes follow their target and glance about otherwise; walk into
range of an enemy turret: after a short reaction about one shot a second, 6 a hit, some missing; die to one and see "by Blue turret"
and the kill in the feed; shoot a machine down (pistol 21 hits on a turret): the burst, nothing in the feed, no kill counted, back after
30 s (turret in place, tank/helicopter in its zone, rising); pods and flags do nothing for a tank on them; bots shoot machines in their
way. Spectate (Tools > SDF Battle > Spectate Bots) and press M to ride along with each machine. Frame time with all 14 machines and
8 players in the web build. Tuning: `Machines.rules` on Battle (stats, aim skill), the speeds on each machine component.

### 24.2 — what was built

- **Format**: under each side in `sides`, on a level with machines, exactly four `turret at=(x, z) yaw=..` (where it stands and
  respawns, facing yaw at rest) and one `machines at=(x, z) yaw=.. radius=..` (the zone where the side's helicopter and tanks start and
  respawn, facing yaw; radius 1.5 to 8 m, 3 by default). Parsed in `LevelFile.ReadSide` into `BattleObject`s of the new kinds
  `BattleKind.Turret` (foot radius `BattleObject.TurretRadius` = 1 m: the 1.37 m foot, 1.27 m cap, barrel 0.9 m from the axis) and
  `BattleKind.Machines` (`ZoneRadius`, `IsZone` like the spawn); `BattleSide` (pipeline) gains `Turrets` and `MachineSpawn`, its
  `Objects` lists them after the pods, `BattleLayout.HasMachines`, `TurretsPerSide = 4`. Rules: a side with any turret or a zone has
  exactly 4 turrets and one zone; both sides have machines or neither; turret/machines only in a side, not in `neutral`; levels
  without machines (meadow, chessboard) read as before. As `Objects` holds them, the bake's play-area check and the scatter's
  clearing (ticket 17) cover them too.
- **Bake**: level.json's side gets `turrets` (`position` on the ground, `yaw`) and `machines` (`position`, `yaw`, `radius`), both left
  out on a level without machines.
- **Placements** in `fungal.level`, west (red) mirrored to east (blue), x and yaw negated: turrets before the castle's front either
  side of the gate at (∓47, ±10), yaw ±60 / ±120 (facing out over the field and a little outwards; 5 m out from the front wall, 10 m
  either side of the trail, past the gate's clumps), and off the large towers' outer sides at (∓62, ±20), yaw ±30 / ±150 (covering
  the north and south flanks). Machine zone at (∓70, 22.5), radius 3, yaw facing the field (±90).
  **No room right behind the castle**: the castle's back (the keep and small towers) reaches x = ∓72.3 (∓71 at the keep's middle),
  which leaves a strip 2.7-4 m deep to the play area's edge at ∓75 (the 20 m walls), cut by the two team clumps at (∓72.9, −3.9) and
  (∓72.7, 5); a zone there could be at most 1.3 m across and would need the tanks to drive out along a 2.7 m corridor. So the zone is
  on the open plateau off the castle's back north corner: 8 m behind the castle's middle, beyond the back of the large tower, level
  (3.6 cm over the zone), 6 m from the north turret and clear of the plateau clump at (∓69.5, 16.5); tanks drive out to the field round
  the tower's north side.
- **Unity**: `BattleSide` gains `turrets` (`MachineRig[]`), `machineSpawn` (Transform) and `machineSpawnRadius`, draws the zone's
  circle and arrow; `Objects` includes them (so SceneInfo's side elements do). `LevelBuilder.AddBattle` (version 18) instantiates per
  side Turret 1-4 from `Assets/Machines/Prefabs/turret-{red,blue}.prefab` (red under Battle/West, blue under Battle/East) at their
  spots, and an empty Machine Spawn at the zone's middle; helicopters and tanks are not instantiated (24.3 spawns them at runtime).
  `BuildAllInBatch` also builds stale machine prefabs first.
- **Navmesh**: Battle is still left out of the bake, but each turret gets a `NavMeshModifier` of its own (a modifier further down
  overrides Battle's: not ignored, area Not Walkable, applied to children), so its foot and head colliders go in as obstacles: bots
  and tanks path round them and nothing walks on a cap. `BattleNavmesh.MarkObstacle`. Tools > SDF Battle > Check Navmesh also
  reports, on a level with machines, how far the scouts' navmesh keeps from each turret's axis (a problem under 0.6 m) and whether
  each machine zone is on the Tank navmesh and a tank gets from the west zone to the east one.
- **Tests**: `LevelFileTests.Read_TakesEachSidesTurretsAndMachineZone_AndLevelsWithoutMachines` and `Read_ChecksTheMachines` (12
  error cases: 3 and 5 turrets, turrets without a zone, a zone without turrets, a zone twice, machines on one side only either way,
  the zone's radius out of range, unknown parameters, `at` missing, a turret in `neutral`); `BattleSidesTests`: fungal has 4 turrets
  and a zone per side, meadow and chessboard none; mirrored; in the play area, clear of each other and every battle object, off the
  trail; no overlap with a placed model (the castle, rocks, giants, fairy rings and the team mushrooms, by their shapes); the zones
  (spawn and machine) on level ground; `Machines_TurretsStandOutsideTheCastle_TheZoneBehindIt` (a wall between the castle's middle
  and each turret, 6 m or more from the gate, a metre or more off the trail, the zone 5 m or more behind the castle's middle); the bake
  writes turrets and machines and leaves them out without. Checked that moving a turret into the castle and the zone behind the keep
  fails them. `dotnet test SDFPipeline.Tests`: 465 pass. Levels re-baked; the Unity C# compile-checked outside Unity.

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
