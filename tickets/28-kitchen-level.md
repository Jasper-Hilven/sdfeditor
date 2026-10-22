# 28 — Kitchen level

A battle level on a giant kitchen island: the scouts fight across the worktop between the sink and the hob, among crumbs,
sugar cubes, cans and a kettle, and anyone who walks off the edge falls 55 m to the kitchen floor. In the campaign (31, 32) it is one
of the house rooms (toy box, kitchen, bathroom) that bring Mushroom 42 "closer to home" before the inside of the computer and the
final match against Chanterelle Prime. It is a level like the others: single player, 42 and three red bots against four blue bots,
capture the flag. It follows the level ruleset (26) and fills in its template.

Depends on: 26 (the ruleset), 17-25 (battle, match, flag, weapons, bots, machines, spores), 31 and 32 (the story and where the level
sits in the campaign). Shares the weapon-mix check with the toy box and bathroom tickets: no two levels have the same four weapons.

## Where the battle is: on the worktop

Three places were weighed:

| | Countertop (kitchen island) | Kitchen floor | Dining table |
|---|---|---|---|
| Kitchen features in play | sink and hob in the worktop itself, tap, kettle | cabinet plinths, a dropped pan; the sink and hob far overhead, out of play | none: plates and cutlery only (a dining room, not a kitchen) |
| Boundary | the worktop's edge: a fall to the floor kills, like the island levels | four walls of cabinets: a closed room like the chessboard | the table's edge: a fall |
| What is new | a pit (the sink) and a hazard (the burners) inside the field | little: a second closed room | little: the meadow's island again with plates |
| Clutter | crumbs, sugar, rice, coffee beans, cereal: what lands on a worktop | dust, a lost pea; sparse | crumbs and spills, fewer kinds |

**Decision: the worktop of a kitchen island.** It is the only one of the three where the sink and the hob, the two things that say
"kitchen" from anywhere, are in the field and change the play: a deep pit to cross or go round, and burners that heat in turns. An
island worktop (open on all four sides) gives the fall-death edge that the floating island has, which the game already handles
(`Match` kills a scout outside the play area 3 m below it), and leaves the whole kitchen around it as the view: wall-run counters,
cabinets, a window with daylight, the floor far below. A dining table would bring the plates without the kitchen; the floor would be a
second chessboard room with the interesting things out of reach.

## Theme and scale

- **One theme: a kitchen.** On theme: what is found on and around a kitchen worktop (food, its crumbs and spills, packets and jars,
  crockery, cutlery, utensils, small appliances) and the kitchen itself as the backdrop. Off theme: toys (the toy box's), soap,
  sponges for the bath and towels (the bathroom's; a washing-up sponge is a kitchen thing), board games, fungi and mushroom features
  (no eyes or hands on props, no mushroom shapes), grass, trees, sky. Nothing from `content/models/home/` (42's room, 31): the kitchen is
  its own folder, `content/models/kitchen/`, materials in `materials/kitchen/`.
- **Scale: 60 times life size**, one factor for everything (written in the level file's header). A scout (1.75 m) is a 2.9 cm figure
  on the worktop; the eye at 1.5 m is 2.5 cm above it. So the worktop, 2.5 × 1.67 m real, is 150 × 100 m; a sugar cube 0.96 m; a cookie
  3.6 m across; a tin can 6.6 m; a pepper mill 12 m; the kettle 13 m; the counter stands 55 m above the floor; the ceiling is 101 m
  over the worktop.
- **Palette**: honey oak worktop with darker stave lines, brushed steel (sink, kettle, spoons), black glass (the hob), sage green
  cabinets, cream walls and white metro tiles in the backdrop; props in mustard, lemon, mint, sage, cream, butter yellow, cocoa
  brown, apricot orange, lilac and soft pink. Warm white daylight.
- **Team colours kept out**: no scarlet and no chanterelle blue on props. So: no tomato cans, ketchup, strawberries, red apples,
  cherries, chillies or paprika; no blueberries, blue enamel or blue-striped crockery. The sprinkles are yellow, mint, pink and white;
  the fruit is lemons, limes, oranges, a green pear and a banana; the cake is lemon. The burners glow ember orange to yellow when hot,
  never scarlet (checked against the red team's colour in a snapshot). The only red and blue are the scouts, the flags, the glow
  columns, the machines and the bases' tea towels (team dressing).

## Footprint, boundary and lighting

- **Play area**: `play size=(150, 100) noise=0`, centred on the origin, x across (west −x, east +x), z along (north +z). Flat.
- **The worktop**: the play area plus a 1 m lip (152 × 102 m), a 2.4 m thick oak slab (4 cm real) with a rounded front edge, over
  the island's cabinet base set 3 m back under it (sage green doors and brass handles, down to the floor 55 m below). The edge is
  the boundary: nothing stops a scout walking off; falling off kills (`Match.outsideKillDepth`, 3 m under the spawns outside the play
  area), with the death screen's "fell" line. There are no walls on the play area's edges.
- **The sink is a pit in the worktop**: a single deep steel bowl 27 × 22 m (x −13.5 to 13.5, z 20 to 42), 13.2 m deep (22 cm real),
  with a 1.5 m steel rim flush with the oak. It is deeper than `Match.killDepth` (12 m): a scout who falls in is dead before reaching
  the bottom, with no new code. A few centimetres of grey water and suds lie at the bottom (no collider). A test checks the bowl stays
  deeper than the kill depth.
- **Regions** (`wobble=0` on all, ruled edges):
  1. `oak`, the base: the worktop, with 2.4 m staves along x (4 cm real) drawn by the shader, so the grain runs east-west towards the
     enemy, and a few knife marks;
  2. `steel`: the sink's rim, `RoundedBox2D center=(0, 31) size=(30, 25) radius=2`;
  3. `hob`: black glass, `RoundedBox2D center=(0, -6) size=(35, 31) radius=1.5`, with a printed control strip along its south edge
     (white touch symbols, shader drawn);
  4. `flour`: white flour spilled round each cake and in a drift at (∓20, −42), `Circle` radius 7-9 m, `wobble` on (a spill is soft).
- **The room (backdrop)**: the kitchen, 300 × 270 m, its floor 55 m below the worktop, its ceiling 101 m above. North wall: a wall
  run of counters with a 72 × 72 m sash window over it (daylight, a pot of basil on the sill), white metro tiles to 36 m above that
  counter, upper cabinets beside the window. West and east walls: counters and upper cabinets with under-cabinet light strips glowing
  warm white. South wall: a tall fridge (cream, a few magnets) and the door. Floor: grey slate tiles; two brass pendant lamps hanging over
  the island from the ceiling, their shades 65 m above the worktop. Everything in the backdrop is out of reach (the nearest counter is
  75 m from the island's end across the drop) and takes no collider; it casts and takes no shadows.
- **Light**: one directional light, warm white daylight (about `#FFF4E0`) from the window, high (55° up) from the north-north-west,
  so the props throw shadows south-south-east across the worktop. Pieces and landmarks shadow the worktop; the backdrop does not.
  The under-cabinet strips, the pendant lamps and the window are emissive (glow, no lights). Ambient a little cool from the window.
  No sky; the camera clears to the walls' cream.

## Bases

- **A bread box per side**, the same model on both: a wooden roll-top bread box 24 × 15.6 × 13.2 m (40 × 26 × 22 cm real), its roll
  top rolled up into the lid so the whole front is open towards the middle. West `bread-box0 at=(-62, 0) yaw=90` (x −69.8 to −54.2,
  z −12 to 12), east `at=(62, 0) yaw=-90`. Inside: one hall 14.4 × 22.8 m, 12 m high, a half loaf (cover, 1.3 m crust wall) and a jam jar
  dressing it. Its floor is a 0.3 m bottom board with a bevelled front (under the bots' 0.4 m step), so the inside joins the worktop.
  Walls 0.6 m. Collider `mesh`. Behind the back wall the worktop runs 5.2 m to the edge.
- **Team dressing**: a gingham tea towel draped over the lid and down one side: red and cream on the west box, blue and cream on the
  east (`tea-towel-red`, `tea-towel-blue`, no collider).
- No `flatten` (the worktop is flat) and no paving.
- **Flag stand**: inside, 4 m in front of the back wall: west (−65, 0), east (65, 0).
- **Spawn**: beside the box, outside its south wall, facing the field: west (−54, −22) yaw 90, east (54, −22) yaw −90, radius 3.
  10 m out from the wall, 24.6 m from its own flag, 6.2 m and more from the turrets.

## Pods and lanes

**Weapons**: the sides get the **minigun** and the **flamethrower**; the neutral pods are the **electro-gun** (long) and the
**blob-gun** (short). The mix {minigun, flamethrower, electro-gun, blob-gun} is none of the other levels' (meadow: minigun, pistol,
sniper, flamethrower; fungal: minigun, pistol, electro-gun, poison-gun; chessboard: electro-gun, poison-gun, sniper, blob-gun). The
minigun holds the open hob; the flamethrower holds the bread box's open front, where every attacker has to come close.

| Object | West | East |
|---|---|---|
| Flag | (−65, 0) | (65, 0) |
| Spawn, r 3 | (−54, −22) yaw 90 | (54, −22) yaw −90 |
| Minigun pod | (−49, 7) | (49, 7) |
| Flamethrower pod | (−49, −7) | (49, −7) |
| Health pod, base | (−65, −8), inside the box by the south wall | (65, −8) |
| Health pod, north flank | (−26, 38) | (26, 38) |
| Health pod, south flank | (−26, −38) | (26, −38) |
| Turrets | (−48, 15) yaw 60, (−48, −15) yaw 120, (−64, 18) yaw 30, (−64, −18) yaw 150 | x and yaw negated |
| Machine zone | (−68, 26) r 3 yaw 90 | (68, 26) r 3 yaw −90 |

**Neutral**: electro-gun at (0, 13), between the sink's front rim (7 m) and the kettle; blob-gun at (0, −36), south of the hob in the
close cover. 49 m apart.

**Lanes** (flag to flag 130 m):
- **North, the sink lane**: three ways past the sink. The back ledge, 8 m of worktop between the bowl and the north edge (z 42-50),
  passing the tap's foot with 3.5 m between it and the bowl, a fall on both sides; the rolling pin across the bowl at z = 31, a
  player's bridge, exposed to the electro-gun; or round the front of the bowl past the electro-gun pod. The north flank health pods sit
  where the ledge begins.
- **Centre, the hob**: the black glass arena, 35 × 31 m, open, with the kettle at its middle and the four burners round it, no knee
  cover on it (the scatter avoids it). The minigun's lane, and the twist's.
- **South, the pantry lane**: the cluttered strip south of the hob, the blob-gun pod in its middle, the cakes and the south flank
  health pods; close cover everywhere, the spawns at its west and east ends.
- **Broken lines**: flag to flag (z = 0) is blocked by the kettle at (0, 0) and by the plate stacks at (∓34, −2); spawn to spawn
  (z = −22) by the pepper mills at (∓26, −21).
- **Long sight lines**: along the north lane over the open sink, about 90 m from the cereal box's end to the other (z 26-30), and
  across the hob, about 60 m from the plate stacks to the pepper mill on the far side. The electro-gun (60 m) covers both from its pod.
  Close cover: the bread box's front (flamethrower 8 m) and the south lane (blob-gun about 40 m).

## Ground clutter

What spills on a worktop, in drifts with bare oak between (a hard floor: cluster 3-4, 0.2/m² or less). No colliders, `reach=0`,
`avoid=steel` (and the sink's hole), `margin=0.6`, `scale=(0.85, 1.15)`, `maxSlope=20`. On the hob too: crumbs on black glass read well.

| Layer | Models (size) | Numbers |
|---|---|---|
| Main, group `crumbs` | `crumb0..2` (bread crumbs 0.15-0.45 m, golden), `rice-grain0..1` (0.42 × 0.12 m, ivory), `cereal-loop0..2` (0.78 m across, 0.25 m thick; honey, cocoa, mint), `coffee-bean0..1` (0.72 × 0.5 × 0.3 m), `sprinkle0..3` (0.36 × 0.1 m; yellow, mint, pink, white) weight 0.6 | `density=0.2 cluster=3 clusterScale=12 spacing=0.8` |
| Accent, group `crumbs` | `sugar-drift0..1` (a low mound of crystals 1.5-2.5 m across, 0.15 m high, sugar white and salt grey), `pea0..1` (frozen peas, 0.48 m, one and three together, green) | `density=0.04 cluster=4 clusterScale=6 spacing=0.7` |

Hand-placed spills (a `place` group each): cereal loops pouring from each toppled cereal box's open end, a trail of coffee beans
from the north flank health pods towards the sink, a spilled sugar heap round each cake.

Draw `crumbs`: `rings=[10, 25, 45] outline=1 shadows=1 blend=1 fade=15 lowDensity=0.5 lowDistance=0.7`; no wind, `tintHeight=0`.

## Knee cover

Group `pantry` (with the tall blockers), `trees` inside the play area, `band=(-46, -4) density=0.005 spacing=7 cluster=2.5
clusterScale=18 scale=(0.9, 1.1) avoid=steel,hob margin=1.5`. Colliders `convex` (a hull over 3 m becomes `mesh` by the rule). Rounded
or domed tops, so few flat tops sit between 0.4 and 1.1 m.

| Model | Size | Height and use |
|---|---|---|
| `sugar-cube0` | 0.96 m cube, soft edges | 0.96: cover and a player's step |
| `sugar-cube1` | a cube tipped onto an edge, a corner chipped | 1.1 |
| `sugar-cube2` | two cubes, one leaning on the other | 1.4: a wall to both |
| `cookie0` | 3.6 m across, domed, cocoa chips | 0.72 |
| `cookie-bitten0` | the same with a bite out, crumbs | 0.72 |
| `butter-pat0` | 1.8 × 1.8 m, softened and slumped | 0.9 |
| `bottle-cap0` | crown cap 1.9 m across, crimp up (lilac, mustard) | 0.45: bots go round, players hop over |
| `jar-lid0` | 4.2 m across, upside down (a dish to jump into) | 0.72 rim |
| `teaspoon0` | 8.4 m long, bowl 2.4 m wide, handle rising from it | 0.4 to 1.0 |
| `sponge0` | washing-up sponge 6 × 4.2 m, yellow foam, green scourer on top | 1.5: the tallest, a low wall |
| `garlic-clove0` | lying, 1.8 × 1.0 m | 1.0 |

## Tall blockers

Group `pantry`, `trees` inside the play area, `band=(-46, -8) density=0.003 spacing=9 cluster=3 clusterScale=18 scale=(0.9, 1.1)
avoid=steel,hob margin=3`: loose clumps of jars and cans 10-20 m wide with open worktop between. Colliders `convex` up to 3 m across,
`mesh` above (cans, mugs, cartons).

| Model | Size (height × width) | Note |
|---|---|---|
| `tin-can0` | 6.6 × 4.5 m | mustard label (beans) |
| `tin-can1` | 7.2 × 4.5 m | mint label (chickpeas) |
| `tin-can-lying0` | 4.5 high, 6.6 long, dented | cover on its side |
| `jam-jar0` | 5.4 × 4.2 m | apricot jam, a cream lid |
| `honey-jar0` | 6 × 4.8 m | amber, a wooden dipper in it |
| `spice-jar0`, `spice-jar1` | 6 × 2.7 m | turmeric yellow, dried herbs green |
| `salt-shaker0` | 4.8 × 3 m | glass, steel cap |
| `mug0` | 6 × 4.8 m and handle | mustard |
| `mug-tipped0` | 4.8 high, 7 long, lying, open end to look into | sage |
| `vinegar-bottle0` | 12 × 3.6 m | green glass |
| `milk-carton0` | 14.4 × 4.2 × 4.2 m | cream and yellow print |
| `plate-stack-low0` | 3 side plates, 10.2 m across, 3.6 m high | cream with a lilac rim |

Draw `pantry`: `rings=[25, 60, 1000] outline=3 shadows=2 lowShadows=1 blend=2 lowDistance=0.7`; no wind.

## Landmarks

Hand-placed (`place`), each group with its comment line for the overlap test. Mirrored pairs plus the centre pieces on x = 0, all
10 m or more from the side pods, stands and spawns and 6 m or more from the neutral pods.

| Landmark | Size | Collider | Where | Job |
|---|---|---|---|---|
| `sink0` (bowl, rim, drain, suds) | 27 × 22 m bowl, 13.2 m deep | mesh | (0, 31) | the pit splitting the north lane in three |
| `tap0` | gooseneck 18 m tall, foot 3 m across, the spout over (0, 33) at 14 m, a drip every 2 s | mesh (foot and column) | (0, 47) | marks the north from anywhere; cover on the back ledge |
| `rolling-pin0` | French pin 30 m long, 3 m thick in the middle, 1.1 m at the tips | mesh | (0, 31) yaw 90, tips on the rims at x = ±15 | a player's bridge over the sink: jump on at a tip (1.1 m), walk the taper up to 2.15 m |
| `kettle0` | 13 m tall with handle, 9.6 m across, steel | mesh | (0, 0) on the hob | the central blocker on the flag line |
| `burner-large` ×2 | ring 10.8 m across, flat, 0.02 m over the glass | none | (∓10.5, 3) | the twist (below) |
| `burner-small` ×2 | ring 8.4 m across | none | (∓10.5, −15) | the twist |
| `plate-stack0` ×2 | 5 side plates, 10.2 m across, 6 m high | mesh | (∓34, −2) | the second block on the flag line; cover beside the hob |
| `pepper-mill0` ×2 | 12 m tall, 3.6 m across, oak | convex | (∓26, −21) | breaks the spawn-to-spawn line |
| `cereal-box-toppled0` ×2 | 18 × 4.2 m, 12 m high, on its long side, flaps open, cereal pouring out | mesh | (∓34, 22) yaw ±20 | the wall between the north and centre lanes on each half |
| `fruit-bowl0` ×2 | 15 m across, 6 m rim, fruit heaped to 9 m (lemons, limes, oranges, green pear, banana) | mesh | (∓44, 38) | shelters the north flank, a 4.5 m path behind it along the edge |
| `cake0` ×2 | 12 m across, 6 m high, lemon sponge, one slice out towards the middle | mesh | (∓44, −36) | south flank cover; the slice's notch is a firing slot |
| `blender0` ×2 | 15 m tall, base 7.2 m, glass jug with a yellow smoothie | mesh | (∓66, −42) | marks the back south corner, hides the spawn from the far south lane |

Checks: kettle to the electro-gun pod 8.2 m; plate stacks to the flamethrower pods 10.7 m; cereal box ends 11 m from the north front
turrets; the fruit bowl 10.5 m from the north flank health pod; the cake 12 m from the south one; the tap's foot 3.5 m from the bowl and
1.5 m from the edge on the north side (a gap nobody needs: the ledge passes south of it).

## Border

No walls round the play area: the worktop's edge is the boundary and the kitchen is the view (see Footprint). The tall props stand
back from the edge (`band` 4-8 m inside), so a scout sees the drop and the kitchen beyond it between them. The blenders mark the two
south corners behind the spawns; the fruit bowls stand near the north corners; the tap rises over the north edge's middle.

## Machines

Yes: 4 turrets, 2 tanks and 1 helicopter per side (24), in kitchen forms at 24's sizes, team-coloured, with the scouts' floating eyes
(as 24's machines; see Open questions).

- **Turret, `timer-turret`** (about 2 m): a wind-up kitchen timer, a squat dome with a dial on a short stand. The dial head turns, the
  barrel is its winding knob. Team colour on the dial's rim and stand, cream face.
- **Tank, `tin-tank`** (≤ 0.8 m): a sardine tin on four bottle-cap wheels; its turret a pepper-pot dome that turns, a drinking straw
  for a barrel. Team-coloured tin.
- **Helicopter, `whisk-copter`** (≤ 0.8 m): a balloon whisk hanging nose down as the body, its handle the tail with a little tail
  rotor (a bottle cap), the main rotor two crossed butter knives. Eyes inside the wires. Team-coloured handle.
- Positions: in the table above (turrets 5 m and more off the box's front and sides; the zone off the box's back north corner,
  4 m in from the edge, open to the field).
- Helicopters fly 6-12 m over the **worktop**, never lower over the sink (see Risks), about 19 m over the bread boxes (13.2 m), and keep
  6 m inside the play area's edge as on the island. The ceiling (101 m) is far over them.

## New models list with budgets

All in `content/models/kitchen/` (machines in `content/models/kitchen/machines/`, materials in `materials/kitchen/`), toon look, 60×
scale, origin at the base's centre on y = 0, 3 LODs at 256 (the bread box's parts at 128). Budgets are LOD0 triangles.

| Model | Size (m) | Collider | LOD0 | Materials |
|---|---|---|---|---|
| `crumb0..2` | 0.15-0.45 | none | 200 | crumb |
| `rice-grain0..1` | 0.42 × 0.12 | none | 200 | rice |
| `cereal-loop0..2` | 0.78 × 0.25 | none | 300 | cereal (3 tints) |
| `coffee-bean0..1` | 0.72 × 0.5 × 0.3 | none | 250 | coffee |
| `sprinkle0..3` | 0.36 × 0.1 | none | 200 | sprinkle (4 tints) |
| `sugar-drift0..1` | 1.5-2.5 × 0.15 | none | 500 | sugar (shader crystals) |
| `pea0..1` | 0.48 per pea | none | 400 | pea |
| `sugar-cube0..2` | 0.96 cube; 1.1; 1.4 | convex | 1.5-3k | sugar |
| `cookie0`, `cookie-bitten0` | 3.6 × 0.72 | convex (mesh) | 3k | biscuit, cocoa |
| `butter-pat0` | 1.8 × 1.8 × 0.9 | convex | 2k | butter |
| `bottle-cap0` | 1.9 × 0.45 | convex | 2k | cap (2 tints) |
| `jar-lid0` | 4.2 × 0.72 | mesh | 2.5k | lid |
| `teaspoon0` | 8.4 × 2.4 × 1.0 | mesh | 3k | steel |
| `sponge0` | 6 × 4.2 × 1.5 | convex (mesh) | 3k | foam, scourer |
| `garlic-clove0` | 1.8 × 1.0 | convex | 2.5k | garlic |
| `tin-can0`, `tin-can1`, `tin-can-lying0` | 6.6-7.2 × 4.5 | mesh | 4-6k | tin, label |
| `jam-jar0`, `honey-jar0` | 5.4-6 × 4.2-4.8 | mesh | 5-6k | glass, jam/honey, lid |
| `spice-jar0..1` | 6 × 2.7 | convex | 4k | glass, spice, lid |
| `salt-shaker0` | 4.8 × 3 | convex | 4k | glass, steel |
| `mug0`, `mug-tipped0` | 6 × 4.8 | mesh | 5-6k | glaze (2 tints), coffee |
| `vinegar-bottle0` | 12 × 3.6 | mesh | 5k | green glass, cap |
| `milk-carton0` | 14.4 × 4.2 × 4.2 | mesh | 4k | carton |
| `plate-stack-low0` | 10.2 × 3.6 | mesh | 6k | crockery |
| `sink0` | 30 × 25 × 13.2 (below y = 0) | mesh | 15k | steel, water, suds |
| `tap0` | 18 tall, reach 14 | mesh | 10k | steel |
| `rolling-pin0` | 30 × 3 | mesh | 6k | oak |
| `kettle0` | 13 × 9.6 | mesh | 18k | steel, black handle |
| `burner-large`, `burner-small` | 10.8 / 8.4 × 0.02 | none | 1k | burner (heat parameter per vertex) |
| `plate-stack0` | 10.2 × 6 | mesh | 10k | crockery |
| `pepper-mill0` | 12 × 3.6 | convex | 8k | oak, steel knob |
| `cereal-box-toppled0` | 18 × 4.2 × 12 | mesh | 12k | card print, cereal |
| `fruit-bowl0` | 15 × 9 | mesh | 22k | ceramic, lemon, lime, orange, pear, banana |
| `cake0` | 12 × 6 | mesh | 18k | sponge, cream, lemon icing |
| `blender0` | 15 × 7.2 | mesh | 18k | glass, smoothie, base |
| `bread-box0` | 24 × 15.6 × 13.2 | mesh | about 80k (its slats instanced in `draw base`) | oak, steel, bread |
| `tea-towel-red`, `tea-towel-blue` | 14 × 10 draped | none | 4k | gingham (team) |
| `counter-base` | the island's cabinets under the worktop, 146 × 96 × 55 | none | 6k | sage, brass |
| `worktop-edge` | the 2.4 m slab's rounded edge round the play area | none (the ground is the floor) | 4k | oak |
| `kitchen-room` (walls, floor, ceiling, window, wall-run counters, upper cabinets, light strips, fridge, pendant lamps) | 300 × 156 × 270 | none | 40k in all, big plain forms | 8 at most |
| `timer-turret` (red, blue) | about 2 high | as 24's turret | 5k | timer, team |
| `tin-tank` (red, blue) | ≤ 0.8 | as 24's tank | 4k | tin, team |
| `whisk-copter` (red, blue) | ≤ 0.8 | as 24's helicopter | 4k | steel, team |

**Reused** (theme-neutral battle objects): `weapon-pod`, the health pod, the flag stand, `flag-red`, `flag-blue`, the scouts and the
weapons. No existing prop model is on theme (the games, chess, fungi, meadow and home folders are all off it).

## Gameplay twist: the hob heats up

The four burners heat in turns, one mirrored pair at a time, so both sides always face the same heat:

- A 45 s cycle: the **back pair** (the large rings, z = 3) warms for 3 s (a dull ember glow and a ticking sound when there is audio),
  is hot for 12 s (bright orange to yellow, a heat shimmer over it), cools for 3 s; then the **front pair** (the small rings, z = −15) does
  the same; 9 s of cold glass between. The cycle starts with the back pair 20 s into a round.
- A scout or a tank standing on a hot ring takes **10 a second** (max health 100; about a pistol hit a second), reported as "the hob"
  in the kill feed when it kills (no kill credit). Cooling rings do not hurt.
- The kettle is never on a ring: the central blocker is always safe to stand by, so the hob is a ring of danger round a safe middle.
- **Bots**: each ring is a NavMesh area `Burner`; when its pair warms, its cost rises (×50) for bots and tanks, so they path round it
  and a bot on it steps off; when it is cold the cost is 1 and bots cross freely.
- The twist with the weapon mix: the minigun owns the hob while it is cold; when a pair heats, the arena splits and the flanks open.
- Plus the edges: the worktop's edge and the sink kill. Nothing pushes a scout (no knockback in the game), so a fall is a misstep.

## Bots and the navmesh

- **Edges**: the navmesh ends 0.35 m inside the worktop's edge and round the sink's bowl (the agent radius); the floor (55 m down) and
  the sink's bottom (13.2 m down) are outside the height window (12 m under the spawns), so there are no drop links over either. Mark
  the sink's bowl `Not Walkable` with a modifier anyway.
- **Bots near the edge**: a bot's strafing in a fight must keep to the navmesh (`NavMesh.Raycast` before a sidestep, as the tank does),
  or it walks off. Check in Play mode on the back ledge (8 m wide) and at the corners; fix in `Bot` if they fall.
- **The rolling pin** is a player-only route (tips at 1.1 m): its top is an unreachable island; give it a `Not Walkable` modifier so it
  is not counted.
- **The bread box**: the 0.3 m bevelled bottom board joins the inside to the worktop; the flag stand and the base health pod are
  reachable. Its lid (13.2 m) is above the window.
- **Tops**: the plate stacks (6 m), fruit bowls' rims (6 m) and the low plate stacks (3.6 m) are in the window but not reachable: a
  few unreachable spots. Knee cover tops are domed or under 0.4 / over 1.1 m where possible.
- **Pinch points**: the back ledge between the tap's foot and the bowl is 3.5 m (open); the path behind the fruit bowls 4.5 m; the
  bread box's open front 22.8 m. The pantry group's margins (1.5 m knee, 3 m tall) keep the rest open or shut.
- **Tanks**: hob glass and oak are flat; the zone (∓68, 26) joins the field round the box's north side; the two zones join through the
  north or the centre lane (not the rolling pin).
- **Check Navmesh**: each spawn to the enemy flag and every pod; the inside of each box joined; the burners' areas present; few
  unreachable spots; the turrets' keep-off and the tank zones joined.

## Performance

Targets as the ruleset: High about 2M triangles; Integrated GPU / Web 500k and at most 200 draws. Expected heaviest views: from a
spawn looking across the hob to the far bread box (both boxes, the kettle, the pepper mills and the plate stacks in view), and from the
back ledge over the sink (the fruit bowl, tap, sink and both cereal boxes). The `kitchen-room` backdrop is always in view: keep it at
40k triangles and 8 draws or fewer, no shadows; the pendant lamps and strips are emissive, not lights. Draw counts: `crumbs` 22 variants,
`pantry` 26 variants. `LevelSnapshot` triangles and draws per group on both presets go in this ticket next to the meadow's and the
chessboard's.

## Risks

- **The ground has no holes**: the sink needs a cut-out in the worktop's floor (the island edge's code on an inner shape, or a
  `hole` region that leaves the ground out), with the sink model's rim meeting it without a seam. 28.1.
- **A room bigger than the play area, its floor below it**: `room` today puts checker walls on the play area's edges. The kitchen
  needs its walls 75-85 m out and its floor 55 m down, drawn by a model (`kitchen-room`) instead of `RoomChecker`, and the worktop's
  edge and cabinet base; and the camera's far plane over 400 m. 28.1.
- **Machines per level**: `Machines` and `LevelBuilder.AddMachines` load `Assets/Machines/Prefabs/<kind>-<team>.prefab`, one fungal
  set for all levels. The kitchen needs a per-level machine set (`machines style=kitchen` in the level, or a set name in `level.json`).
- **The helicopter over the sink**: `HelicopterPilot` keeps 6 m over the highest surface under it; over the bowl that is 13.2 m down,
  so it would sink into the bowl. Clamp its floor to the worktop's height (y = 0) plus 6 m.
- **The burners**: a new runtime piece (heat cycle, damage, the glow per vertex, a runtime NavMesh area cost) and a "the hob" death
  cause; EditMode tests for the cycle and damage.
- **Falls by bots**: see Bots. Falls by players are the point, but the back ledge must stay 8 m wide.
- **Team colours**: the hot burners' orange next to the red team; checked in a snapshot. Sprinkle and fruit colours as listed.
- **Budgets**: the fruit bowl (many fruit) and the bread box (slats) are the likeliest to run over; the backdrop's large faces must
  stay plain (no noise) to mesh small.
- **Kill depth coupling**: the sink kills only because it is deeper than `Match.killDepth` (12 m). A test in `SDFPipeline.Tests`
  checks the sink's depth against it, so changing one without the other fails.

## Open questions

1. The worktop, the bread boxes and the burner twist: the user's yes before modelling (ruleset checklist step 1).
2. Do the kitchen machines keep the scouts' floating eyes, as 24's do, or are they plain like the props?
3. A nod to 42 in the backdrop (a "42" magnet on the fridge)? It is a kitchen thing, not a `home/` model; allowed?
4. Campaign order (32): toy box, kitchen, bathroom is assumed; and a one-line caption on loading ("The kitchen. Mind the hob.")?
5. Audio (drip, burner tick, sizzle) waits for an audio ticket.

## Subtickets

- **[28.1 Pipeline: a hole in the ground (the sink)](28.1-ground-hole.md)**
- **[28.2 Pipeline: a room larger than the play area, open edges, backdrop model](28.2-open-room.md)**
- **[28.3 Models: worktop clutter](28.3-models-clutter.md)**
- **[28.4 Models: knee cover and tall blockers](28.4-models-knee-and-blockers.md)**
- **[28.5 Models: sink, tap, kettle and the other landmarks](28.5-models-landmarks.md)**
- **[28.6 Models: the bread box base and tea towels](28.6-bread-box.md)**
- **[28.7 Models: the kitchen room, the island base and the worktop edge](28.7-backdrop.md)**
- **[28.8 Kitchen machines and the per-level helicopter floor](28.8-kitchen-machines.md)**
- **[28.9 The level file kitchen.level and its tests](28.9-level-file.md)**
- **[28.10 The hob heats up](28.10-hob-twist.md)**
- **[28.11 Bake, build and check](28.11-unity-and-checks.md)**

## Acceptance criteria

- `content/levels/kitchen.level` bakes; `dotnet build` and `dotnet test` pass, with the kitchen in `BattleSidesTests` (mirrored, in
  the play area, clear of each other and the placed models, weapons minigun, flamethrower, electro-gun, blob-gun), `LevelFileTests`
  (no overlaps across groups), the theme test and the sink-depth test.
- Every new model renders (`.png` next to it), meshes at 256 with 3 LODs within its budget, and has a bounds test.
- `Kitchen.unity` builds from the bake with no errors; Check Navmesh: each spawn reaches the enemy flag and every pod, few
  unreachable spots, no drop links off the worktop or into the sink, the tank zones joined.
- Snapshots from each spawn in four directions, a corner, above, the back ledge, the hob with a hot pair and the low preset: no red or
  blue props, the kitchen visible beyond the edge, the drop readable.
- Budgets: within 2M triangles on High and 500k triangles / 200 draws on Integrated GPU / Web from every snapshot view; numbers listed
  here.
- Play mode, a full match with 7 bots: falling off the edge and into the sink kills; the rolling pin can be crossed by a player; the
  burners heat in mirrored pairs on the cycle, hurt 10 a second and bots step off them; bots do not walk off the edge; machines fight
  in their kitchen forms and the helicopters stay over the worktop; a flag carried home in each lane.

## Status

**Not started.**
