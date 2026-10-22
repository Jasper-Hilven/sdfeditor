# 29 — Bathroom level

A battle level on the floor of a bathroom, at giant scale: Mushroom 42 and the red bots against the blue bots among shampoo bottles,
soap bars, rubber ducks and toilet rolls, in front of a bathtub as tall as a castle. Mushrooms like the damp, so the room is steamy and
wet: a haze over the far walls, puddles, soap slicks you slide on. It is one of the house-room levels (toy box, kitchen, bathroom) that
bring 42 "closer to home" before the inside of the computer and the final boss against Chanterelle Prime (31, 33). Single player with
bots; no fake multiplayer.

Follows 26 (level ruleset) and fills in its template. Depends on: 11-14 (the chessboard: the room, flat floor, hand-placed pieces),
16-25 (battle: sides, pods, match, weapons, bots, machines), 26. Feeds 32 (campaign flow: where the level sits and its intro caption).

The level is `content/levels/bathroom.level`, baked into `Unity/Assets/Level/Generated/bathroom`, scene `Assets/Scenes/Bathroom.unity`.
Models in `content/models/bathroom/`, parts in `content/parts/bathroom/`, materials in `materials/bathroom/` (ground in
`materials/ground/`).

## The footprint: the floor round the bathtub

Three places were weighed:

| | The tiled floor round the tub | Inside the bathtub | The countertop by the sink |
|---|---|---|---|
| Ground | flat, `noise=0`, as the chessboard | a curved bowl: the sides slope from 0° to past 45°, so the navmesh and the scatter stop part-way up and the edges are unplayable | flat, but small, and it ends in a drop |
| Boundary | the room's four walls, as the chess cube: `room` exists | the tub's walls, 16 m and curved: no `room`, a new boundary kind, and no ceiling for the copters | open edges: falling off needs a kill floor and a far-away floor under it, a new indoor "island" |
| Water | puddles on the floor, a flat region | a half-filled tub means wading or swimming everywhere: no such movement, and the navmesh under water is a new problem | the sink basin is a hole in the middle |
| Room for 150 × 100 m | yes: a 5 × 3.3 m bathroom floor at 30 times | a 1.7 × 0.75 m tub is 51 × 22 m at 30 times: far too small, or at 90 times the scouts lose the toiletries' scale | a 1.2 × 0.5 m counter is a strip |
| Bathroom things | all of them, at the right height: the tub as a wall, the toilet rolls, the bottles, the ducks, the scale | only what is in a tub | only what is on a counter; the mirror behind it fills the view |

**Chosen: the tiled floor round the bathtub.** It is the only one the pipeline builds today (`play noise=0` and `room`, as the chessboard),
it is big enough for three lanes, and it has every bathroom thing at its real height: the tub stands in the play area as a 16.5 m
white cliff with towels hanging off it, the shower head drips from high on the wall, the scale lies on the floor. The tub's water and
the steam are seen, not walked in.

## Theme and scale

- **Theme**: a bathroom, and only a bathroom. On theme: the tub, its taps and shower head, the floor drain, towels and washcloths, soap,
  sponges, bath bombs and beads, rubber ducks, shampoo, conditioner and lotion bottles with their caps, toothbrushes and the cup,
  toilet rolls, a bath brush, cotton swabs, hairpins, the bathroom scale, water drops, suds and steam. Off theme: anything from the
  toy box, kitchen, board games, meadow or fungal level, and **no mushroom props** (no mushroom-shaped soap, no fungus in the grout,
  no mushroom ducks): the damp is shown by water, suds and steam, not fungi. The machines are bathroom things too (below).
- **Scale: 30 times life size** (header of the level file: "Scale: 30 times life size; a scout's 1.5 m eye is 5 cm"). A shampoo bottle
  22 cm tall is 6.6 m, a toilet roll 10 cm is 3 m, a soap bar 3 cm thick is 0.9 m, a water drop of 5 mm is 0.15 m. The room's floor,
  5 × 3.3 m, is the 150 × 100 m play area; its 2.33 m ceiling is at 70 m. Every model below is sized from this factor (the giant duck
  is a 30 cm novelty duck, the tub a 1.7 m tub).
- **Palette**: bathroom white and pastels, clear of team red and blue.

| Use | Colours |
|---|---|
| Floor tiles | warm white `#F3F0E8`, grout pale grey-mint `#C8D6CF` |
| Wall tiles | white `#F6F4EE` and pale mint `#CFE6D8` in a checker; ceiling flat white `#F7F6F2` |
| Enamel, porcelain, rolls | white and cream |
| Chrome (taps, drain, scale trim) | pale silver with the toon hard highlight, no reflection |
| Toiletries | lilac `#C9B7DE`, butter yellow `#F4E39A`, mint `#BFE3C8`, peach-apricot `#F6C9A0`, cream |
| Ducks | classic yellow `#F7D24A`, orange beak `#F29A3A` (orange, not red) |
| Water and puddles | near-colourless grey-mint, a white highlight band; never blue |
| Steam | warm white-mint `#E6EEEA` |

- **Kept out of the props**: no red and no blue, not even pink or sky blue, which read as a team from far away. The only team colours
  are the scouts, flags, glow columns, machines and each base's two toothbrushes (team dressing, like 19's team mushrooms).
- **Toon look**: plain rounded forms (bottles, bars, ducks survive coarse meshing), few materials each; terry cloth, sponge pores,
  quilted paper and tile grout as shader patterns, not geometry. No wind.

## Footprint and boundary

- `play size=(150, 100) noise=0`: x across (−75 to 75), z along (−50 to 50); a flat tiled floor.
- `room size=(150, 70, 100) square=5`: four tiled walls on the play area's edges and the ceiling at 70 m, colliders on all six faces.
  5 m squares (17 cm wall tiles) divide 150, 70 and 100.
- **Wall look**: `RoomChecker` today draws the chess colours on walls and ceiling. The bathroom needs white and mint tiles with a grout
  line on the walls and a plain white ceiling: a room material per level (29.1). The checker of two colours is a real retro bathroom
  wall, so the checker code stays and only its materials change.
- **Regions** (four, `wobble=0` where ruled), on the base `bathroom-tile` (`regions base=bathroom-tile`):

| Region | Shapes | Look |
|---|---|---|
| `grout` | 29 strips `Box2D size=(0.25, 100)` at x = −70, −65, ..., 70, and 19 strips `Box2D size=(150, 0.25)` at z = −45, ..., 45: 5 m floor tiles (17 cm) | grey-mint joint, matte |
| `wet` | `Circle center=(0, 24) radius=10`, `Circle center=(0, −28) radius=12`, and the two trickles below widened to 3 m | the tile a shade darker and glossier (damp floor); the grout is lost under it, as water fills the joints |
| `puddle` | `Circle center=(0, 24) radius=6` (under the tub's overflow), `Circle center=(0, −28) radius=8` (round the drain), and the trickle between them, split round the duck: `Polyline points=[(0, 18), (0, 9), (−7, 2), (0, −8), (0, −20)] width=1.2` and its mirror `[(0, 9), (7, 2), (0, −8)] width=1.2` | standing water: flat, a pale grey-mint, a hard white highlight band and ripple rings in the shader; no transparency, no reflection |
| `suds` | the soap slicks, mirrored: north `Polyline points=[(−60, 26), (−46, 34), (−34, 30), (−28, 23)] width=4` and south `[(−58, −32), (−42, −45), (−12, −46)] width=4`, with their mirrors at +x | a smeared white foam film with a bubble pattern, glossy |

  If the ground material can draw a straight square grid by itself (the `tiles` region's `grout` parameter is flagstones today), the
  48 grout strips go and the floor's tiles come from the base material; else keep the strips, as the chessboard keeps its squares.
- No path: the trickle of water marks the centre line, the slicks the two flanks.

## Sky/lighting

- No sky. One directional light through a **frosted window** high in the south wall (`window-frosted0`, a placed model: 24 × 20 m pane
  at x −12 to 12, y 42 to 62), so the light falls from the south and above: pitch 50° down, heading +z (north), soft daylight `#FFF8EC`,
  intensity 1.15; a warm ambient. A light along z keeps the two halves mirror images of each other in shadow too.
- The window is frosted, an emissive pale pane with a white frame: nothing is seen through it, so no outside world is built.
- **No mirror.** A real mirror needs planar reflections (a second camera, too dear on WebGL). Chrome gets the toon hard highlight and
  rim light, never a reflection probe.
- Walls and ceiling cast and take no shadows; the tub, duck, bottles and pieces shadow the floor (as on the chessboard).
- **Steam** (the damp mood), cheap on WebGL:
  - **Fog**: Unity's built-in fog, exponential squared, colour `#E6EEEA`, density 0.0075: 87% clear at 50 m, 57% at 100 m, 28% at the
    far wall 150 m away. The far walls go soft and milky; the electro gun's 60 m stays clear (80%). The camera clears to the fog colour,
    not black. `SdfToon` and `SdfToonOutline` take `multi_compile_fog` and mix it per vertex (one lerp: no cost worth counting on the
    integrated GPU). Same density on every preset. A new level line `fog density=0.0075 color=#E6EEEA` (29.1); `LevelBuilder` today
    forces "no fog" indoors.
  - **Steam plumes**: three slow `FxParticles` emitters over the tub's water (x = −15, 0, 15, at the rim, 16.5 m) puffing soft white
    sprites 6-10 m wide that rise and fade by 40 m; one small one over the drain. At most 80 particles in all, 40 on the low preset
    (overdraw is the cost, not count).
  - **The drip**: a drop falls from the shower head every 2 s (a single particle), with a ring on the tub's water (seen only from high
    up); a few drops slide down the tub's front at the overflow.

## Bases

Each side's base is a **soap-dish fort with a toothbrush-cup keep**, the same model for both sides, mirrored.

| Part | Model | Size | At (west; east mirrored) | Collider |
|---|---|---|---|---|
| Courtyard wall | `soap-dish0`: a 10 × 7 cm ceramic soap dish, cream, only its rim (the floor inside is the room's floor, so the stand and pods stay at y = 0), with drain slots as gates | 30 × 21 m (yaw 90: 21 m across x, 30 m along z), rim 1.2 m tall and 1.5 m thick, rounded on top, steep outside (over 55°) | centre (−62, 0) yaw 90; east (62, 0) yaw −90; the rim from x −72.5 to −51.5, z −15 to 15 | mesh |
| Gates | three slots in the rim: the main one 4 m wide facing the middle (x −51.5, z 0), two 2.5 m on the north and south ends (x −62, z ±15) | | | |
| Keep | `toothbrush-cup0`: a 7.5 cm tumbler, 11 cm tall, lilac-white frosted glass look (opaque) | 3.3 m tall, 2.25 m round | (−68.5, 0) | convex |
| Team dressing | `toothbrush-red0` / `toothbrush-blue0` ×2 in the cup, leaning out, team-coloured handles and white bristles (the only team colour on the floor) | 5.7 m long, tops at 6-6.5 m | in the cup | none (inside the cup's hull; above reach) |
| Inside cover | two `soap-bar0` (knee cover, reused) | 3 × 1.8 × 0.9 m | (−58, ±7) yaw 20 / −20 | convex |

- **Flag stand**: (∓63, 0), on the floor inside the dish, before the cup, 5.5 m from it.
- **Spawn**: (∓47, −17), radius 2.5, yaw ±90 (facing the field), outside the dish 4.9 m from its south-east corner, on open floor; 23.3 m
  from its own flag. No flatten and no paving: the floor is level already.
- The rim at 1.2 m is a wall to bots and players alike (over the 1.1 m jump) except at the gates, but low enough to shoot over from
  behind; the cup and its brushes mark the base from across the room. About 22k triangles per base at LOD0, far under a castle's 100k.

## Pods and lanes

- **Weapon mix**: pistol and blob-gun on each side, electro-gun and flamethrower neutral. Meadow has minigun, pistol, sniper,
  flamethrower; fungal minigun, pistol, electro-gun, poison-gun; chessboard electro-gun, poison-gun, sniper, blob-gun: this set is the
  bathroom's own. (Check it against 27, toy box, and 28, kitchen, when those are written.) The blob gun fits a room of soap.

| Object | West | East | Why |
|---|---|---|---|
| Pistol pod | (−44, 7) | (44, 7) | north of the gate, 7.5 m out |
| Blob-gun pod | (−44, −7) | (44, −7) | south of the gate, by the spawn, for holding the flag |
| Health, base | (−71, −22) | (71, −22) | behind the dish's south end, 9 m from the side turret |
| Health, north flank | (−34, 40) | (34, 40) | in the tub-end alcove, 8.5 m from the tub's end |
| Health, south flank | (−28, −38) | (28, −38) | in the south lane, a little past halfway |
| Neutral flamethrower (short range) | (0, 16) | | under the tub, at the trickle: close cover (the towels, the tub's wall, the duck) |
| Neutral electro-gun (long range) | (0, −16) | | on the open south side, by the drain's puddle, with the long sight lines |

The two neutral pods are 32 m apart on x = 0, 16 m from the duck between them.

- **Lanes**:
  - **North (the tub lane)**: from the base's north gate up past the machine zone and the north slick into the tub-end alcove
    (x ±25.5 to ±75, z 27.5 to 50, the health pod), then round the tub's corner and along its front, z 12 to 23, under the towels to the
    flamethrower. Close and tall: the tub is a 16.5 m wall on one side.
  - **Centre**: out of the main gate past the pistol pod and the bath brush, round the giant duck at the centre, between the two
    neutral pods.
  - **South**: from the spawn past the toilet-roll pyramid to the south health pod and the south slick, along the south wall past the
    scale and the drain. Open and long.
- **Flag to flag** (z = 0, 126 m): the giant duck at (0, 0) stands on it.
- **Spawn to spawn** (z = −17, 94 m): the toilet-roll pyramids at (−26, −15) and (26, −19), one a little north of the line, the other a
  little south, each 10.8 m wide.
- **Long sight lines**: along the south lane, z −35 to −45, 60-100 m with only toiletry clumps across it; from each gate diagonally to
  the electro-gun pod and the drain (about 55-70 m). Close cover near the base (the dish rim, soap bars), under the tub (towels, the
  tub's wall) and round the flamethrower.
- The layout is mirrored across x = 0; the north lane is the close one and the south the open one on both sides.

## Ground clutter

No colliders, `reach=0`, `maxSlope=20`, `scale=(0.85, 1.15)`. Draw group `suds`: `rings=[10, 25, 45] outline=1 shadows=1 blend=1
fade=15 lowDensity=0.5 lowDistance=0.7`, `tintHeight=0`, no wind.

| Layer | Models | Numbers | Avoid |
|---|---|---|---|
| Suds and drops, drifts with bare tile between (the damp) | `suds0..2` (foam heaps 0.6-1.5 m across, 0.2-0.45 m tall), `droplet0..2` (domed drops 0.12-0.3 m across, 0.06-0.12 m tall), `bubble0` (a 0.4 m bubble resting on the floor, drawn opaque in pale pearl with the rim light doing the shine) weight 0.3 | `density=0.2 cluster=3 clusterScale=12 spacing=0.9 margin=0.6` | `grout` is fine; avoid nothing else: suds on the slicks and drops in the puddles are wanted |
| Small things, tight handfuls | `hairpin0..1` (bobby pins 1.5 m long, dark bronze), `swab0` (a whole cotton swab 2.25 m, white stick, 0.3 m cotton tips), `swab-half0`, `bath-bead0..2` (oil beads 0.3 m, pearl yellow, lilac, mint) | `density=0.04 cluster=4 clusterScale=6 spacing=0.7 margin=0.6` | `puddle` |
| Drops alone, a fine sprinkle | `droplet0..2` | `density=0.15 cluster=2.5 clusterScale=8 spacing=0.5 margin=0.3` | |

About 0.39 instances per m² in all: hard-floor drifts, as the chessboard's tokens.

## Knee cover

Draw group `toiletries` (with the tall blockers, so they keep clear of each other): `rings=[25, 60, 1000] outline=3 shadows=2
lowShadows=1 blend=2 lowDistance=0.7`. Every model `collider convex`; tops rounded or domed so bots do not find flat islands.

| Model | Size | Height | Notes |
|---|---|---|---|
| `soap-bar0..2` | 3 × 1.8 m (10 × 6 cm) | 0.9 m | domed; cream, lavender, mint; `soap-bar2` worn thin and lying in a little suds |
| `sponge0..1` | 3.6 × 2.4 m | 1.05 m | butter yellow with a mint scrub layer, pillowy top; pores in the shader |
| `duckling0..2` | 1.2-1.5 m long | 0.9-1.1 m | the rubber-duck family: small yellow ducks, one tipped on its side (0.7 m) |
| `bath-bomb0..1` | 1.2 m round | 1.1 m | lilac and butter yellow, a sprinkle pattern; `bath-bomb-half0` 0.7 m, cut face up, fizzing suds on it |
| `washcloth0` | 4.5 × 4.5 m folded square | 0.9 m | peach and lilac terry; the top bulges, no flat top |
| `bottle-cap0..1` | flip cap 1.2 m round, screw cap 0.9 m round | 0.75 / 0.6 m | white and mint, lying on their sides or upright |

- Section: `trees group=toiletries band=(-72, -6) density=0.006 spacing=6.5 cluster=2.5 clusterScale=16 scale=(0.9, 1.1) margin=1.5`.
- A second sprinkle of the ducklings alone (`density=0.0012 spacing=8 margin=1.5`): the family wanders.
- Heights 0.6-1.1 m: players jump them, bots go round; all of them shoot-over cover.

## Tall blockers

Same group `toiletries`, `collider convex`. Clumps 12-18 m wide, like the spill of a knocked-over caddy, with open floor between.

| Model | Size | Notes |
|---|---|---|
| `shampoo0` | 6.6 m tall, 3.6 × 2.4 m oval | lilac, white flip cap |
| `shampoo1` | 5.4 m, stood on its cap (conditioner) | mint |
| `shampoo2` | lying on its side: 6.6 m long, 2.4 m tall | butter yellow; tall knee-to-chest wall, shot over by nobody |
| `lotion-pump0` | 6 m with the pump head | cream with a peach label band |
| `toilet-roll0` | one roll standing: 3 m tall, 3.6 m round | quilted paper pattern; the core a shadowed recess, not a hole |
| `toilet-roll1` | two rolls stacked: 6 m | |

- Section: `trees group=toiletries band=(-70, -10) density=0.004 spacing=8 cluster=3 clusterScale=16 scale=(0.9, 1.1) margin=3`.
- The tall section comes first in the group, the knee cover after it (each keeps clear of the earlier by its margin).

## Landmarks

Hand-placed, each group starting with a comment line (an overlap group for `LevelFileTests`). Placed pieces keep 10 m from the side
pods, stands and spawns and 6 m from the neutral pods; the tub, riser and window are the room's fixtures and count as border.

| Landmark | Size | Collider | At | Job |
|---|---|---|---|---|
| `bathtub0`: a built-in white enamel tub with a front panel (no feet: nobody crawls under), half filled; the water inside a flat mint-grey surface with a raft of suds at 8 m; the water creeping over the front rim at the middle in a thin glossy sheet | 51 × 22.5 × 16.5 m | mesh (from LOD1) | (0, 38.75) yaw 180: x −25.5 to 25.5, z 27.5 to 50, front face at z 27.5 | the north wall of the middle: splits the north lane from the far wall, makes the tub-end alcoves, feeds the puddle |
| `shower-riser0`: the tub's mixer tap and spout on the wall (y 20-24 m), a riser rail to a round shower head at 57 m, dripping | head 4.5 m across, rail 1 m thick, 40 m tall | none (out of reach) | (0, 49.5) on the north wall | seen from everywhere over the tub: the north marker; the drip |
| `towel-drape0` ×2: a bath towel hung over the tub's rim, butter yellow with white stripe bands, hanging nearly straight (over 60°) to the floor and pooling in a rounded roll 2-3 m high, 4.5 m out | 21 m wide, 16.5 m tall | mesh | (−14.5, 25) and (14.5, 25) yaw 180 | the soft cliffs: cover in the tub lane, 8 m from the flamethrower |
| `rubber-duck0`: the giant duck, a 30 cm novelty duck, yellow, looking north at the tub | 10.5 m long, 7.5 m wide, 9 m tall | convex | (0, 0) yaw 0 | the central blocker on the flag-to-flag line, 16 m from each neutral pod |
| `toilet-roll-pyramid0` ×2: six rolls, 3-2-1, the top one a little askew | 10.8 m wide, 3.6 m deep, 9 m tall | mesh | (−26, −15) yaw 10 and (26, −19) yaw −170 | break the spawn-to-spawn line, one north of it and one south |
| `bath-brush0` ×2: a long-handled bath brush lying on the floor, wooden handle 0.9 m thick, the head's bristles 2.4 m tall | 12 m long, head 3.6 × 2.4 m | convex | (−30, 14) yaw 75 and (30, 14) yaw −75 (from about (∓36, 12.4) to (∓24, 15.6)) | splits the north and centre lanes in each half; the handle is shot over and jumped, the head is a wall; 10.2 m from the pistol pod, 7 m clear of the towel |
| `bath-scale0`: a square bathroom scale, white with chrome trim, a glass top and a little display that reads "42" | 9.6 × 9.6 m, top at 0.75 m, a 1.5 m bevelled skirt at 27° all round (12.6 m over it) | convex | (0, −40) yaw 0 | a low walkable hill in the open south lane: a vantage spot bots can walk up |
| `floor-drain0`: a round chrome grate, flush with the floor, a slow swirl of suds over it (a ring of 8 `FxParticles` suds orbiting) | 4.5 m round, 0.05 m | none | (0, −28) | the puddle's centre; the south marker |
| `window-frosted0`: the frosted window with its sill | 24 × 20 m pane, y 42-62 | none | (0, −49.7) on the south wall | the light |

## Border

The room's tiled walls, as the chess cube's. The tallest things stand nearest them: the tub, riser and towels on the north wall, the
window on the south, the soap-dish bases near the west and east walls, and the tall blockers' band 10 m in from every wall. No border
dressing beyond that: the fog softens the far walls instead.

## Machines

On theme: bathroom things with the scouts' floating eyes, in red and blue (machines are team-coloured, like 24's). Same rules, stats
and parts as 24; new models in `content/models/machines/` named like 24's.

| Machine | Model | Size | Parts |
|---|---|---|---|
| Tank | `duck-tank`: a rubber duck in team colour riding a sponge tread; the head turns, a squirt-nozzle beak is the barrel | 0.8 m long | `Body` (duck body and sponge), `Turret` (head), `Barrel` (beak) |
| Helicopter | `spray-copter`: a trigger spray bottle in team colour, a flat rotor spinning on the spray head, a tiny fan on the nozzle | 0.8 m tall | `Body`, `Rotor`, `TailRotor` |
| Turret | `hairdryer-turret`: a hairdryer on a coiled-cord foot, the dryer turns, its nozzle is the barrel | 2 m | `Foot`, `Head`, `Barrel` |

| Per side | West | East |
|---|---|---|
| Turrets, front | (−48, 11), (−48, −11) | (48, 11), (48, −11) |
| Turrets, flanks | (−62, 20), (−62, −20) | (62, 20), (62, −20) |
| Machine zone | (−67, 32) r 3 | (67, 32) r 3 |

The turrets are 5 m off the dish, 6.1 m from the spawn, 5.7 m from the pistol pod. The zone is open floor in the north-west alcove that
the tanks can leave in three directions.

- **Copters in a room**: the indoor ceiling is the spawn plus 10 m (`HelicopterMachine`), so they fly at 6-10 m, under the 70 m
  ceiling but **into** the tub (16.5 m), duck and pyramids (9 m) and the tallest bottles (6.6 m). Keep-outs come
  automatically from placed models over 6 m in a room (ticket 30.7, `HelicopterKeepOuts`); no `nofly` lines. 29.8 checks the tub, duck and
  pyramids are covered.
- 24's fungal machines are off theme here: the machine set is chosen per level (a `machines set=bathroom` line or by the level's
  theme), 29.5.

## New models list with budgets

LOD0 triangles at 256 cells, 3 LODs (LOD1 about a quarter, LOD2 a tenth). `Detail detail=0.5` on the big plain forms (tub, duck,
bottles, rolls, dish). Bounds tests for each in `SDFPipeline.Tests/Models/`.

| Model | Size (m) | Collider | LOD0 | Materials |
|---|---|---|---|---|
| `suds0..2` | 0.6-1.5 across, 0.2-0.45 | none | 400 | foam |
| `droplet0..2` | 0.12-0.3, 0.06-0.12 | none | 200 | water |
| `bubble0` | 0.4 | none | 300 | bubble (pearl, rim) |
| `hairpin0..1` | 1.5 long | none | 300 | bronze |
| `swab0`, `swab-half0` | 2.25 / 1.1 long | none | 400 | plastic, cotton |
| `bath-bead0..2` | 0.3 | none | 200 | pearl |
| `soap-bar0..2` | 3 × 1.8 × 0.9 | convex | 2k | soap |
| `sponge0..1` | 3.6 × 2.4 × 1.05 | convex | 3k | sponge, scrub |
| `duckling0..2` | 1.2-1.5 long, 0.7-1.1 | convex | 3k | rubber, beak |
| `bath-bomb0..1`, `bath-bomb-half0` | 1.2 / 0.7 | convex | 2.5k | bath bomb |
| `washcloth0` | 4.5 × 4.5 × 0.9 | convex | 3k | terry |
| `bottle-cap0..1` | 1.2 × 0.75 / 0.9 × 0.6 | convex | 1.5k | plastic |
| `shampoo0..2` | 6.6 × 3.6 × 2.4 (lying 6.6 long) | convex | 5k | plastic, cap |
| `lotion-pump0` | 6 tall | convex | 6k | plastic, pump |
| `toilet-roll0..1` | 3.6 round, 3 / 6 tall | convex | 4k / 6k | paper |
| `bathtub0` | 51 × 22.5 × 16.5 | mesh | 25k | enamel, water, foam |
| `shower-riser0` | 4.5 head, 40 tall | none | 8k | chrome |
| `towel-drape0` | 21 × 16.5 × 5 | mesh | 18k | terry |
| `rubber-duck0` | 10.5 × 7.5 × 9 | convex | 18k | rubber, beak, eye paint |
| `toilet-roll-pyramid0` | 10.8 × 3.6 × 9 | mesh | 15k | paper |
| `bath-brush0` | 12 long, head 2.4 tall | convex | 6k | wood, bristle |
| `bath-scale0` | 12.6 square with skirt, 0.75 | convex | 6k | plastic, glass, chrome |
| `floor-drain0` | 4.5 round | none | 1k | chrome |
| `window-frosted0` | 24 × 20, sill | none | 1.5k | frosted glass (emissive), frame |
| `soap-dish0` | 30 × 21 × 1.2 | mesh | 10k | ceramic |
| `toothbrush-cup0` | 2.25 × 3.3 | convex | 4k | frosted glass |
| `toothbrush-red0`, `toothbrush-blue0` | 5.7 long | none | 3k | team plastic, bristle |
| `duck-tank-{red,blue}-{body,turret,barrel}` | 0.8 | as 24 | 5k total | rubber, sponge, team |
| `spray-copter-{red,blue}-{body,rotor,tail-rotor}` | 0.8 | as 24 | 4k total | plastic, team |
| `hairdryer-turret-{red,blue}-{foot,head,barrel}` | 2 | as 24 | 6k total | plastic, team |

- **Materials** (`materials/bathroom/`): enamel, ceramic, chrome, water, foam, bubble, soap, sponge, scrub, rubber, beak, terry, paper,
  plastic, cap, pump, bronze, cotton, pearl, bath bomb, wood, bristle, glass, frosted glass; ground `ground_bathroom-tile`,
  `ground_grout`, `ground_wet`, `ground_puddle`, `ground_suds`; room walls and ceiling (29.1). Subsurface kept for soap, sponge, bath
  bombs, ducks and frosted glass.
- **Reuse**: the pickups (pods, health cross, flag stand, flags), the scouts and `FxParticles`. Nothing from `games/`, `fungi/`,
  `mushrooms/` (other than the players), `trees/` or `rocks/`.

## Gameplay twist: slippery and wet

1. **Soap slicks** (the `suds` region): on a slick a player keeps their speed: ground acceleration ×0.25, braking ×0.1, top speed ×1.3;
   jumping as usual. The flank lanes become fast slides into the tub alcove and along the south wall, at the price of steering. Bots
   get the top speed (×1.2) without the slide (they follow the navmesh as usual), so a skilled player gains on them.
2. **Wet scouts conduct** (the `puddle` region): a scout standing in water, or out of it less than 2 s ago, takes ×1.5 damage from the
   electro gun, and splashes as they walk (a small `FxParticles` splash per step). The electro-gun pod sits by the drain's puddle and
   the trickle runs past both neutral pods and round the duck: the centre is wet, and the electro gun rules it.

Both are 2D zones baked from the regions (a mask in the level bake, read at runtime by position): no new colliders, no navmesh change,
no shader work beyond the region looks. Considered and left out: rising water that floods the low lanes (a moving water surface and
a navmesh that changes during the match, too much for bots and WebGL) and a drain whirlpool that pulls scouts (a force zone that
bots cannot plan round).

## Bots and the navmesh

- The navmesh comes from the colliders as usual: the floor, the dish rims (gates open), the cup, the tub's front, the towels' feet,
  the pieces. Puddles, slicks, suds, drops and the drain have no colliders and change nothing. Bots walk through water and over slicks
  like floor; the speed changes come from the zone mask.
- **Height window**: spawns at 0, so up to 7.5 m. The tub's top (16.5 m) and inside are out of it; nothing a bot must reach is above
  0.75 m (the scale).
- **The towels**: the hanging sheet must be steeper than 60° and the pooled roll's sides over 50°, so bots do not walk up into the
  folds and stick. Check it in Check Navmesh; any island on a fold top is an unreachable spot, which is fine.
- **The scale**: its 27° skirt makes it a walkable hill; Check Navmesh must show its top joined to the floor.
- **Tops**: soap, sponges, washcloths and bath bombs are domed; the lying shampoo bottle's side is round. Few flat islands.
- **Pinch points**: brush end to towel 7 m, dish gates 4 m and 2.5 m, the duck 8 m or more from everything: all open. The pyramid's
  roll-gaps are shut (under 0.6 m). Trees sections keep their margins; hand-placed groups are checked by eye from above.
- **Tanks**: the 0.45 m tank agent fits all three dish gates; the machine zone joins the open floor. Copters as above.

## Risks

- **Room look**: `RoomChecker` only draws the chess colours; per-level wall and ceiling materials are new (29.1).
- **Fog**: `LevelBuilder` turns fog off indoors and the toon shaders do not take it yet; the outline pass must fog the same or the ink
  lines stay sharp in the haze. Test on the web build.
- **Copters in a room** with blockers over their flight height (above).
- **Machine sets per level**: the machine prefabs are fungal today.
- **The puddle and slick zones**: a new baked mask and two small runtime rules (player motor, electro damage); the player motor's
  friction may not be one number today.
- **Steam overdraw** on the integrated GPU with the camera inside a plume (on the tub's rim: unreachable, so rare).
- **Budget views most likely over**: from a spawn looking across at the tub, both towels, the riser and the duck (about 90k of
  landmarks at LOD0-1, plus the toiletries); from above for the snapshots (all the steam). Measure both with `LevelSnapshot` on High and
  Integrated GPU / Web and list them next to the meadow's.
- **Theme drift**: "damp" invites fungus; the theme test and a look at every render keep mushrooms out.

## Decisions for the user (checklist step 1)

1. Footprint: the floor round the tub (above), at 30 times.
2. Bases: soap-dish forts with a toothbrush-cup keep and team-coloured toothbrushes.
3. Twist: soap slicks and wet scouts conducting the electro gun.
4. Machines: duck tank, spray copter, hairdryer turret; copters keep out of tall models via 30.7's automatic keep-outs.
5. Weapon mix: pistol and blob gun on the sides, electro gun and flamethrower neutral.

## Subtickets (one agent each, merged on finish)

- [29.1 — Room look and steam](29.1-room-look-and-steam.md): bathroom wall tiles and plain ceiling through 27.1's room looks, the generic `fog` line (bake, `LevelBuilder`, fog in
  `SdfToon`/`SdfToonOutline`, camera clear colour), the window light data, the steam plumes and the drip.
- [29.2 — Floor clutter models](29.2-models-floor-clutter.md): suds, droplets, bubble, hairpins, swabs, bath beads, and the five ground materials.
- [29.3 — Knee cover and tall blockers](29.3-models-knee-cover-and-blockers.md): soap bars, sponges, ducklings, bath bombs, washcloth, caps, shampoo bottles, lotion pump, toilet rolls.
- [29.4 — Big landmarks](29.4-models-big-landmarks.md): tub, riser, towel, giant duck, toilet-roll pyramid.
- [29.5 — Small landmarks and base](29.5-models-small-landmarks-and-base.md): bath brush, scale, drain, window, soap dish, cup, toothbrushes.
- [29.6 — Bathroom machines](29.6-bathroom-machines.md): duck tank, spray copter, hairdryer turret in red and blue as the `bathroom` set.
- [29.7 — Slick and wet zones](29.7-slick-and-wet-zones.md): the zone mask in the bake; the player motor's slick friction; the bots' slick speed; wet scouts and the electro gun's x1.5.
- [29.8 — Level file and tests](29.8-level-file.md): `bathroom.level` with every section commented, `BattleSidesTests.LevelNames` and its weapon table, the theme test, the bake.
- [29.9 — Unity check](29.9-unity-check.md): rebuild, Check Navmesh, snapshots and budgets on both presets; in the main checkout re-mesh and rebake after merging.

Order: 29.1 to 29.9, one agent at a time (the models need no code; 29.8 needs all of them).

## Acceptance

- `content/levels/bathroom.level` bakes; `dotnet build` and `dotnet test` pass, with the bathroom in `BattleSidesTests` (mirrored, in
  the play area, clear of the placed models) and `LevelFileTests`, and its theme test green.
- Every new model is meshed at 256 with 3 LODs within its budget, has a bounds test, and its render shows no red, blue or mushroom
  props.
- `Bathroom.unity` builds with no `error CS`, shader error or exception; the room is tiled white and mint with a plain ceiling, the far
  walls hazy in the steam, on the High and the Integrated GPU / Web preset alike.
- Check Navmesh: from each spawn a bot reaches the enemy stand and every pod; the scale's top joins the floor; few unreachable spots;
  the scouts' navmesh keeps off the turrets and the tank navmesh holds each machine zone and joins the two.
- Frame budgets from every snapshot view: High under 2M triangles; Integrated GPU / Web under 500k triangles and 200 draws; the numbers
  listed here next to the meadow's.
- Play mode, a full match with 7 bots: spawn, take each pod, cross each lane, carry a flag home; on a slick a player slides and a bot
  does not stick; a wet scout takes 1.5 times the electro gun's damage; the machines fight and none flies into the tub; no bot sticks on
  the towels.

## Status

Built and checked in Unity (29.9; details and the full performance table in `29.9-unity-check.md`): `Bathroom.unity` builds (24 placed models, 4,229 scattered, 2 navmeshes), Check Navmesh reports 0
problems (every spawn, flag stand and pod reached), the other scenes build with unchanged counts, all tests pass. Performance (batch snapshots, 22 views per preset): worst High 205k triangles / 130 draws
(budget 2M); worst Integrated GPU / Web 162k triangles / 104 draws (budget 500k / 200), so no density change was needed. Fixed in 29.9: the window pane's shadow (a dark rectangle over the middle), the wall's
toon highlight ellipse and the window's grey look. Not done: the orbiting suds over the drain and the puddle ripple rings. Left for a human: a Play-mode match, the slick feel, a web build.
