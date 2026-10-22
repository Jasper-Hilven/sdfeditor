# 26 — Level ruleset

The rules for designing a battle level (a scene), taken from the three levels there are: the meadow (`meadow.level`), the fungal
level (`fungal.level`) and the chessboard (`chessboard.level`), and from the chessboard's multi-height fill that is being built now.
Every later level ticket (the toy box, the kitchen, the bathroom, the inside of a computer) follows this ruleset and fills in the
template at the end. This ticket changes no code and no content; it is the reference those tickets link to.

Depends on: 01-04 (ground, foliage, hills, island), 06 (castles), 07 (sky), 11-14 (chessboard, game pieces, chess cube, fungal
level), 15 (meadow), 16-25 (battle). Where a number below comes from one level, the level is named.

## 1. Theme

- **One theme per scene, players aside.** Ground, border, sky or room, props, landmarks, base and machines all belong to the level's
  theme. Only the players (the mushroom scouts) come from another theme. The meadow is a plain meadow (no fungi since 15); the fungal
  level is fungi and what fungi live on (stumps, logs, lichened rock, moss, leaf litter); the chessboard is board games (no mushroom
  features, floating eyes or hands on its pieces, no grass, hills or sky).
- **On theme** means: a thing a person would expect to find in that place, at that place's scale. Board games for the chessboard
  include chess, draughts, dice, dominoes, cards, Monopoly, meeples, poker chips, Scrabble, Trivial Pursuit, tiddlywinks, Jenga,
  Connect Four and a Pictionary timer. Machines are on theme too: the fungal level's tank, helicopter and turret are mushroom forms
  (24); a toy box's would be toys. Each level adds a test like `ContentMeadow_HasNoFungi`: no model the level names comes from
  another theme's folder, and no material it uses is another theme's.
- **Scale.** The scout's eye is at 1.5 m and its capsule is 0.3 m round and 1.75 m high. An outdoor level is life size (trees 8-14 m,
  rocks to 2.4 m scale). An indoor level puts the scouts among giant objects: the chessboard is about 60-120 times life size (a king
  5.6 m, a die 2.2 m, a draught 1.8 m across, a tiddlywink 1.2 m), and its clutter about 60-80 times. Pick one factor per level, write it
  in the level file's header, and size every model from it, so a die and a pawn on the same floor agree.
- **Toon look.** Flat light bands, the ink outline, a hard highlight, rim light and the soft subsurface glow; plain forms that survive
  coarse meshing (blob canopies, no leaf cards). Few materials per model, plain colours and finishes of the theme (lacquer, ivory,
  wood, felt, card; bark, moss, stone).
- **Team colours are for the teams.** Red is west (amanita scarlet), blue is east (chanterelle blue): the flags, the scouts, the
  glow columns, and the team dressing of a base (19's team mushrooms on the fungal castles). Props stay out of them: a field of red or
  blue props reads as a team's. The chessboard fill gives its tiddlywinks, wedges and marbles yellow, green, purple and orange for this
  reason. A single red die among many colours is fine; a group of red or blue props is not.

## 2. Footprint

| | Outdoor (meadow, fungal) | Indoor (chessboard) |
|---|---|---|
| Play area | `play size=(150, 100)`, centred on the origin, x across, z along | `play size=(160, 160) noise=0` |
| Floor | ground heightfield, noise on (default 0.2 m) | flat as a table, `noise=0` |
| Boundary | hills outside the rectangle: `ring width=28 height=10`, crest 8-12 m, `noise amplitude=2.5 scale=18`; the island `edge margin=6 rim=3 shoulder=3` and the `island-underside` behind the crest | `room size=(160, 160, 160)`: four walls on the play area's edges and a ceiling at 160 m, colliders on all six faces |
| Beyond | only sky; falling off the island kills | nothing: the room is all there is |
| Light | `sky preset=day` (meadow), `night` (fungal, Eye-Moon Night), `dusk` exists; from `content/skies/` | no sky: one warm directional light from overhead; walls and ceiling cast and take no shadows; pieces shadow the floor; the camera clears to black |

- **Regions**: the floor other than the base ground, from 2D shapes (`Circle`, `Box2D`, `RoundedBox2D`, `Polyline`), later ones on top;
  at most four; each drawn in `materials/ground/ground_<region>.sdfmat`. `regions base=<material>` changes the base (the fungal level's
  `dirt`). A wandering path or trail from gate to gate, 2.6 m wide (`noise=0.5 noiseScale=4`), marks the middle lane outdoors. Ruled
  floors (squares, tiles, a circuit board's traces) take `wobble=0` so their edges stay straight at eye height.
- **Base plateaus**: a placed base takes `flatten=22` (level ground 22 m round) and, if it wants paving, `ground=tiles groundMargin=0`.
- **Room walls**: `room` today draws the floor's checker on walls and ceiling (`RoomChecker`, 8 m squares). A room that is not a
  chessboard needs its own wall and ceiling look; the level ticket that first needs one specifies it (see Risks in the template).
- Indoor levels keep `reach=0` on every scatter (nothing goes under the walls); outdoor ones use the default reach up the hills.

## 3. Battle layout

Two sides, `west` (−x) and `east` (+x), **mirrored across x = 0** (x negated, yaw negated). `BattleSidesTests` checks the mirror, that
everything is inside the play area, clear of each other, off the path's middle and clear of every placed model from 0.35 m to 2.3 m up.

### Per side

| Object | Rule | Meadow / fungal | Chessboard |
|---|---|---|---|
| Base | a castle at x = ∓62, yaw ±90, gate towards the middle; or a back rank with no building | `castle1` / `castle0` at (∓62, 0), back wall to x = ∓72.3, play edge ∓75 | none: the back rank, 2 squares from the wall |
| Flag stand | inside the base, before the keep's door, half a metre towards the gate to clear the stairs; or on the middle line near the back wall | (∓60.5, 0) / (∓61.5, 0) | (∓72, −4) |
| Spawn | **beside the base, outside its walls**, on open level ground (the test `SpawnZones_LieOnLevelGround`), facing the field like the gate, radius 2-3 m (3 m default); 9 m out from the wall and the north large tower, 8 m past the near health pod, clear of the path, 5.5 m from turrets and dressing, 15-25 m from its own flag; with no base, 6 m in front of the flag | (∓52, 20), r 2 / 2.5 | (∓66, −4), r 3 |
| Weapon pods | one or more, any of minigun, pistol, sniper, electro-gun, flamethrower, poison-gun, blob-gun, each once; both sides the same | minigun and pistol, either side of the gate's apron (∓48, ±6), or in fairy rings (∓46, 38) and (∓44, −22) | electro-gun (∓66, 6), poison-gun (∓66, −14) |
| Health pods | three: one near the base (beside the weapon pods, or behind the base), two on the north and south flanks a little past halfway to the middle | (∓51, 11), (∓28, ±40); fungal (∓73.5, −9), (∓28, ±43) | (∓74, −14), (∓28, 64), (∓28, −33) |
| Machines (where themed) | 4 turrets round the base, outside its walls: two before its front either side of the gate (5 m out, 10 m from the trail), two off its sides covering the flanks; one machine zone, radius 1.5-8 m (3 m default), behind the base on open level ground the tanks can leave | fungal only: turrets (∓47, ±10), (∓62, ±20); zone (∓70, 22.5) r 3 | none yet (a room's helicopters need a flight ceiling) |

### Neutral pods

- Exactly two: one long-range (sniper or electro-gun) and one short-range (flamethrower, poison-gun or blob-gun).
- On the middle line x = 0, one north and one south of the centre, 28-64 m apart: meadow sniper (0, 32), flamethrower (0, −32);
  chessboard sniper (0, 16), blob-gun (0, −12). The fungal level's sit in two fairy rings near the middle, (−8, 24) and (18, −34),
  which is the only unmirrored exception; a new level keeps to x = 0.
- The long-range pod goes on the side with the longer open sight lines; the short-range one where the cover is close.
- Each level picks a weapon mix the others do not have (meadow: minigun, pistol, sniper, flamethrower; fungal: minigun, pistol,
  electro-gun, poison-gun; chessboard: electro-gun, poison-gun, sniper, blob-gun), so each plays differently.

### Clearances

- The scatter leaves out instances within an object's radius (pod 1.3 m, flag stand 1.05 m, turret 1 m, spawn and machine zone their
  radius) plus the layer's margin plus 0.2 m; trees sections 1.5 m; trees sections round a pod 6 m (`PodTreeClearing`), so every pod
  stands in a clearing seen from afar.
- Hand-placed pieces and landmarks keep 10 m from every pod, stand and spawn (chessboard), and 6 m or more from the neutral pods.
- A flag stand and a pod stand on ground a scout can walk to; nothing on a rock or a piece's top.

### Lanes and sight lines

- Flag to flag is 121 m outdoors (±60.5) and 144 m on the chessboard (±72). There is **no straight open line from flag to flag or
  from spawn to spawn**: a blocker stands on it near the middle (the chessboard's giant fallen king across (0, 2) and its Jenga towers at
  (∓30, −3/−5), a little off the spawn line, north on one side and south on the other; the meadow's path bends round (0, 2) and the
  groves; the fungal level's giant mushrooms at (−18, −6), (2, 14)).
- **Three lanes**: north, centre and south. The centre lane runs past the neutral pods and the central blocker; the flank lanes pass
  the flank health pods. A long wall of blockers can make them (the chessboard's domino run: an S from the north wall to the south
  wall, point-symmetric about the centre, bowed 18 m to each side, tiles 1.55 m apart so nobody slips between, with gates at z = 50 and
  68 and round the pods, and a toppled stretch in each half that is shot over). Outdoors the groves and rocks make them.
- At least one long open sight line of 60-100 m per side for the sniper and electro-gun (60 m range), and close cover near the base
  and the short-range pod (flamethrower 8 m, poison-gun about 30 m, blob-gun about 40 m).
- Every lane is about equal from both bases: a point-symmetric layout (turned half round about the centre) is as fair as a mirrored
  one, and lets the north lane of one side be the south lane of the other.

## 4. Multi-height fill

The field is filled in layers, low to high. Each layer is a `scatter` or `trees` section in its own draw group, or hand-placed.
Within one draw group a trees section keeps clear of the earlier trees sections' instances by its margin (`KeepsClearOf`), so pieces
of one group never cut through each other and leave no pinch points; scatter layers and other groups may still overlap (grass under
crowns, rocks round trunks).

| Layer | Height | Section | Colliders | Typical numbers |
|---|---|---|---|---|
| Ground clutter / foliage | to a shin, 0.05-0.5 m | `scatter` (group `foliage`, `tokens`) | none | grass `density=0.48 cluster=1.5 clusterScale=6 spacing=0.45`; flowers `0.096 cluster=2 clusterScale=10 spacing=0.6`; moss `0.72`, fungi tufts `0.288`; board clutter `0.22 cluster=3 clusterScale=12 spacing=0.9` in drifts with bare floor between, its smallest bits `0.05 cluster=4 clusterScale=6` |
| Knee and waist cover | 0.3-1.5 m | `trees group=<pieces>` inside the play area | convex | `band=(-74, -6) density=0.005 spacing=7 cluster=2.5 clusterScale=18 margin=1.5`; small rocks `density=0.01-0.02 spacing=4-4.5 scale=(0.8, 1.6) margin=2.5` |
| Tall blockers | 2-15 m | `trees` inside the play area | capsule (trunks), convex, mesh | groves `band=(-50, -8) density=0.024 spacing=4.5 cluster=3 clusterScale=10 margin=3` (fungal `0.0405 spacing=4`); giants `0.0027 spacing=9`; board pieces `band=(-72, -10) density=0.0045 spacing=7 cluster=3 clusterScale=20 margin=3` |
| Landmarks | 6-14 m | `place`, by hand | mesh or convex | 6-7 pairs, mirrored, plus one at the centre |
| Border | the crest, or the walls | `trees` on the hills, or the room | capsule (front row), none (clusters) | front row `band=(12, 28) density=0.0105 spacing=6 scale=(0.95, 1.4) maxSlope=35`; edge row `band=(-3, 6) density=0.004 spacing=9`; crest clusters `band=(27.5, 31.5) density=0.05 spacing=3.5` |

- **Ground clutter** tells the floor apart and shows speed; it never blocks, so it has no collider and the navmesh does not see it.
  Keep it sparse: cluster 1.5-2 for a carpet (grass, moss), density about 0.5/m² for the main layer and a fifth of that for the accent
  layer (flowers, tufts). On a hard floor (a board, a tabletop) use drifts (cluster 3-4) with bare floor between, density 0.2/m² or
  less. `avoid` the path and the base's paving, `margin=0.5-0.8`, `maxSlope=20-25`, `scale=(0.85, 1.15)`.
- **Knee cover** is what a scout crouches behind and shoots over. Bots step 0.4 m and players jump 1.1 m: below 0.4 m everyone walks
  over it, from 0.4 to 1.1 m players jump it and bots go round, above 1.1 m it is a wall to both. Choose heights on purpose: most knee
  cover 0.6-1.1 m, so it is cover and a player's shortcut. Each piece has a convex collider (marbles a sphere-like hull).
- **Tall blockers** hide a standing scout (taller than 2 m) and make the groves, scrums and lanes. Clumps 10-20 m wide with open
  glades between; `band` 8 m or more inside the edge so the border keeps its own row; `margin=3` from the path, the paving and the
  earlier layers.
- **Landmarks** are few and big, readable as what they are from 100 m (a silhouette, not a texture): the chessboard's Jenga tower
  10-12 m (blocks missing mid-height to see and shoot through), hourglass 7 m, three-storey card castle 9-11 m, Connect Four frame 9 m
  wide and 7 m tall, game box lid 14 × 10 m on its long edge as a wall, dice tower 6 m; and the route-makers: game boxes on end 12 m,
  a pile of four boxes 9.6 m, two boxes leant into a tent 7.5 m, a two-storey card wall 8.5 m, a leaning Jenga tower 7.8 m, and at the
  centre a giant fallen king in two halves, 33 m long and 9 m high at the base, with a gap between the halves to run through. Each pair is mirrored (or point-symmetric about
  the centre); each has a job in the layout (break a sight line, split two lanes, shelter a pod, mark a corner). Hand-placed groups
  start with a comment line, which `LevelFileTests` reads as an overlap group: pieces may touch within a group, never across.
- **Border dressing** closes the view. Outdoors: a front row of trees climbing the inner slopes to the crest, 5-8 m apart, large
  enough that crowns and crest hide what is behind; a few trees at the field's edge to walk up to; crest clusters behind the crest
  filling the gaps between trunks. Indoors: the room's walls, and taller pieces nearer them (corners take the card castles and Connect
  Four frames).

## 5. Models

- **SDF programs** in `content/models/<theme>/<name>.sdf`, parts in `content/parts/<theme>/`, materials in
  `materials/<theme>/<name>.sdfmat` (ground in `materials/ground/`). Each file starts with a comment that says what it is and its size in
  metres. Origin at the base's centre, resting on y = 0, front along +z. Re-render (`sdf render`, the `.png` next to the model) on every
  change.
- **Detail**: big simple forms take `Detail detail=0.5` (half the vertices their size would give); no `Distort` or noise on big faces,
  which defeats the simplifier and multiplies triangles. Shader patterns (stones, fibres) give surface detail at no triangle cost.
- **Colliders** (the model's `collider` line): `none` (the default) for ground clutter, flags and anything walked through; `convex`
  for knee cover and blockers up to 3 m (a convex hull larger than 3 m would block walkable ground and becomes `mesh` on its own);
  `capsule` for tree trunks; `mesh` for anything that is walked on, shot through or has an opening (castles, the Jenga tower, the card
  castle, the Connect Four frame). `collider mesh lod=1` builds it from a finer level.
- **Meshing**: 256 cells, 3 LODs (`sdf unity --resolution 256 --lods 3`, the `Update to Unity (256)` run config); castle parts at 128;
  never 512 (the scatter budgets break and it takes minutes).
- **Variants**: 2-5 variants of each clutter or knee-cover model, 3-13 models per trees section, picked by `weight`; the instance
  scale (`scale=(0.9, 1.1)` for pieces, up to (0.8, 1.6) for rocks) adds the rest. Each variant is a draw per ring, so more variants
  cost draws, not triangles.
- **Draw groups and instancing**: everything a level holds in quantity goes in a `scatter` or `trees` section, drawn instanced in its
  group; draws scale with variants, not with instances. `place` is for unique models and hand-set pieces, each a scene object with its
  LOD group. A base's repeated parts are instanced through its own `draw castle` group.
- **Budgets** per model at LOD0 (LOD1 about a quarter, LOD2 about a tenth):

| Kind | LOD0 triangles | Examples |
|---|---|---|
| Ground clutter | 200-500, one material | tokens, tiles, notes; castle details 200-500 |
| Ground foliage | 1.5-3k | grass turfs, flowers, moss |
| Knee cover | about 3k | chip stacks, marbles, small rocks |
| Tall blockers | 4-8k | trees, pawns, checker towers; crest clusters 2-5k |
| Landmarks | 15-25k | Jenga tower, card castle, Connect Four frame |
| Base | about 100k | `castle0` 98.6k at LOD0, its parts 1-5k each |
| Pickups | to 5.5k | health cross 5.5k, flag stand 3.6k, flag 1.3k |

- A model replacing another (a new theme's tree for the meadow's) stays within 20% of the one it replaces at each LOD.
- Tests: each new model's bounds in `SDFPipeline.Tests/Models/` (its size, its base on y = 0, a walkable top where it is walked on).

## 6. Performance

- **Runtime**: meshes only (no live raymarching), URP, toon look, per-vertex material data, no compute shaders (WebGL2). One Unity
  project builds Mac, Windows and the web.
- **Budgets** per frame from anywhere in the play area: High about 2M triangles; Integrated GPU / Web about 500k triangles and at most
  200 draw calls. Shares, from the tickets that set them: foliage ≤ 700k triangles and ≤ 50 draws on High, ≤ 200k on Web; trees ≤ 400k
  on High, ≤ 120k on Web, ≤ 80 draws with outlines; both bases together 300-400k on High.
- **Quality presets** (`RenderPipelineSetup`), all one shadow cascade:

| Preset | Shadows | Samples, scale | LOD bias | Who |
|---|---|---|---|---|
| Ultra High | 120 m, 4096 | MSAA 8, 1.25 | 1.5 | beyond the dev machine |
| High | 60 m, 4096 | MSAA 4, 1 | 1 | the dev machine (M4 Max), desktop default |
| Integrated GPU / Web | 30 m, 1024 | FXAA, 0.75, no HDR | 0.7 | minimum spec, web default |
| Ultra Low | 15 m, 512 | FXAA, 0.5, no HDR | 0.4 | below the minimum |

- **Rings** per draw group (`draw <group> rings=[...]`), from the levels:

| Group | Rings | Outline / shadows | Low preset |
|---|---|---|---|
| foliage, clutter | `[6, 15, 35]` (clutter `[10, 25, 45]`), `fade=15` | 1 / 1 | `lowDensity=0.5 lowDistance=0.7` |
| trees, blockers | `[25, 60, 1000]` | 3 / 2 | `lowShadows=1 lowDistance=0.7` |
| crest clusters | `[45, 1000]` | 2 / none | `lowDistance=0.7` |
| rocks, knee cover | `[20, 50, 1000]` | 2 / 1 | `lowDistance=0.7` |
| base parts | `[25, 60, 400]` | 2 / 3 | `lowShadows=1 lowDistance=0.4` |

- Wind only where it belongs: grass `wind=0.04 windHeight=0.5`, leafy crowns `0.06 windHeight=5`, mushroom caps `0.025 windHeight=8`;
  none for pieces, rocks, giants or anything indoors. `tintHeight=0.08` colours a turf's mound like the ground under it; 0 on a board.
- **Shadows**: nothing casts from far away on the low preset (`lowShadows=1`); crest clusters and room walls cast none; small detail
  takes `shadows near` (LOD0 only).
- Every new level is measured: `LevelSnapshot` logs triangles and draws per group in each eye view on High and on Integrated GPU / Web;
  the level ticket lists them next to the meadow's.

## 7. Bots and the navmesh

- The level builder bakes the navmesh from the level's **colliders** on every rebuild (`BattleNavmesh`): ground, base walls, floors,
  stairs and wall-walks, trunks, rocks and pieces with colliders. Ground clutter has none and is invisible to it.
- **Scout agent**: radius 0.35 m (the 0.3 m capsule with 5 cm to spare), 1.75 m high, step 0.4 m, slope 45°, drop links down ledges to
  6 m and no other links (a bot jumps only when it finds itself stuck). **Tank agent**: radius 0.45 m, 0.8 m high, step 0.15 m, slope
  30°, ground only.
- **The height window**: the navmesh reaches from 12 m below the lowest spawn to 7.5 m above the highest (`AboveSpawn`). A wall-walk at
  5 m and tower rings at 5.3 m are in it; caps, keep tops and anything a player climbs higher are not. Keep every surface a bot must
  reach under 7.5 m over the spawns, and the spawns at the floor's height.
- **Pinch points**: a gap between two blockers is either open (1.5 m or more clear: two scouts pass) or shut (under 0.6 m: nobody
  passes). Nothing between, where a player squeezes through and a bot snags. The trees sections of one group keep their margin from
  each other for this; hand-placed groups are checked by eye from above.
- **Tops**: flat tops between 0.4 and 1.1 m are reached by jumping, not by bots; they show as unreachable spots. A few are fine; rounded
  or sloped tops avoid them.
- **Check Navmesh** (Tools > SDF Battle > Check Navmesh) for every level: a bot gets from each spawn to the enemy stand and every pod;
  the base's upper floors join the ground; unreachable spots of open ground on a 3 m grid (rock tops and the like) are few; on a level
  with machines the scouts' navmesh keeps off each turret and the tank navmesh holds each machine zone and joins the two.
- Helicopters fly at 6-12 m and over a base's top (about 20 m); a room level with helicopters gives them a ceiling under its own.

## 8. Building a new level

### Checklist

1. **Ticket**: fill in the template below and get the user's yes on theme, scale, base and twist before modelling.
2. **Models**: write each `.sdf` (stubs first if the layout needs to bake before a model lands: a `RoundBox` or `Cylinder` of the right
   size and collider, commented `# STUB: placeholder until the real model lands`, overwritten by the model's owner), its materials,
   `sdf render` each and look at the png; bounds tests.
3. **Mesh**: `sdf unity --resolution 256 --lods 3` (castle parts 128); check each LOD0 against its budget.
4. **Level file**: `content/levels/<name>.level`, every section with a comment that says what it holds and why (as the three levels
   do); add the level to `BattleSidesTests.LevelNames` and its weapons to that test's table, and a theme test like
   `ContentMeadow_HasNoFungi`.
5. **Bake**: `sdf level content/levels/<name>.level --into Unity/Assets/Level/Generated/<name>`; `dotnet build` and `dotnet test` pass
   (`LevelFileTests`, `BattleSidesTests`, the scatter tests).
6. **Unity rebuild**: open Unity or run `SdfLevel.Editor.LevelBuilder.BuildAllInBatch` in batch mode; the log has no `error CS`, shader
   error or exception; the scene `Assets/Scenes/<Name>.unity` and its navmesh are built.
7. **Navmesh**: Tools > SDF Battle > Check Navmesh; every path found, few unreachable spots.
8. **Snapshots**: `SdfLevel.Editor.LevelSnapshot.Capture -level <name> -snapshotDir <dir>`; look at every view (spawn in four
   directions, corner, above, the busiest spot, the low preset); list triangles and draws per group on both presets.
9. **Play mode**: a full match with 7 bots: spawn, take a pod, cross each lane, carry a flag home; bots do not stick; frame time on
   the Integrated GPU / Web preset.
10. **Main checkout**: after merging, re-mesh the changed models and rebake the level there (generated files are not in git), then
    rebuild the scene.

### Template for a level ticket

Each new level ticket has these headings, with numbers:

- **Theme and scale**: the one theme; what is on theme and what is not; the scale factor against a 1.5 m eye; the palette, kept
  clear of team red and blue.
- **Footprint and boundary**: play size; island with hills and edge, or a closed room (its size and wall look); the floor's regions
  (at most four) and the path or lane marks.
- **Sky/lighting**: a sky preset, or the room's light (direction, colour, which faces cast shadows).
- **Bases**: what each side's base is (a building with a courtyard, or a back rank), its position and yaw, flatten and paving; the
  flag stand and the spawn (outside the base, radius, facing).
- **Pods and lanes**: the side weapons and the neutral long- and short-range pair, with positions; the three health pods per side;
  the north, centre and south lanes; what breaks the flag-to-flag and spawn-to-spawn lines; the long sight lines.
- **Ground clutter**: models, density, cluster, spacing, avoid, draw rings.
- **Knee cover**: models and heights (0.3-1.5 m), colliders, band, density, spacing, margin.
- **Tall blockers**: models and heights (2-15 m), band, density, spacing, cluster, margin.
- **Landmarks**: each pair, its size, collider, position and job in the layout.
- **Border**: the hill woods and crest clusters, or the room's walls and what stands by them.
- **Machines**: none, or turrets, tanks and helicopters in the theme, with positions and the machine zone.
- **New models list with budgets**: a table of every new model: name, size in metres, collider, LOD0 triangles, materials.
- **Gameplay twist**: what makes this level play differently (its weapon mix, a lane trick, a height, a hazard).
- **Risks**: what the pipeline cannot yet do (a room's non-checker walls, a second floor over 7.5 m, machines in a room), and the
  budget views most likely to be over.

## Acceptance

- This file exists and the level tickets for the toy box, the kitchen, the bathroom and the inside of a computer link to it and fill
  in its template.

## Status

Written; nothing to build. Numbers checked against `meadow.level`, `fungal.level`, `chessboard.level` (with the multi-height fill in
progress), `LevelFile`, `LevelScatter`, `BattleLayout`, `BattleNavmesh` and `RenderPipelineSetup` as of 2026-10-22. When a level or one
of those changes a rule, update this ticket with it.
