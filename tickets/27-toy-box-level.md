# 27 — Toy box level

The fourth battle level and the first of the house rooms: the inside of a giant wooden toy chest, full of toys. Mushroom 42 and the
red bots fight the blue bots between building blocks, marbles, crayons and toy cars, round a wooden train loop with two bridges and a
jack-in-the-box at its centre. A toy train runs the loop all match long: moving cover that knocks scouts aside and can be ridden.
Bases are two wooden block castles; the machines are wind-up tin soldiers, tin tanks and RC helicopters.

Follows the ruleset in [26 — Level ruleset](done/26-level-ruleset.md) and fills in its template. Files: `content/levels/toybox.level`,
models in `content/models/toys/`, parts in `content/parts/toys/`, materials in `materials/toys/` (ground in `materials/ground/`),
scene `Assets/Scenes/ToyBox.unity`.

Depends on: 26 (the rules), 11-14 (the chessboard's room, the closest example), 16-25 (battle, bots, machines, spores). Feeds 32
(campaign flow: the toy box is the first house-room level, after the outdoor ones and before the kitchen and the bathroom).

## Story place

Single player, 42 and three red bots against four blue bots, no fake multiplayer. The house rooms bring 42 "closer to home": a
toy chest is the first place in the campaign that looks like a real house. Nothing from 42's room (`home/`) appears here; 32 decides
whether a caption introduces the level ("The toy box. 42 had one like this.").

## Theme and scale

- **One theme: toys**, the kind found in a child's toy chest: wooden alphabet blocks, building bricks (LEGO- and Duplo-like, no
  brand marks), marbles, jacks, crayons, small die-cast toy cars, spinning tops, skittles, a ring stacker, a rocking horse, a toy
  Ferris wheel, a jack-in-the-box, a teddy bear, a wooden train set, puzzle pieces, beads and confetti.
- **Not on theme**: board games (dice, chess, dominoes, cards, meeples, chips: the chessboard's), anything from the kitchen, the
  bathroom or 42's room, anything that grows (no grass, moss, fungi, trees), mushroom features (no floating eyes or hands on props;
  the jack-in-the-box's jester and the teddy have painted toy faces), tin soldiers that are not team turrets (a plain soldier would
  read as a machine).
- **Scale: 60 times life size**, the chessboard's factor, so a 1.75 m scout is a 3 cm toy figure. The chest's floor is 2.5 × 1.67 m
  and its walls 1.07 m deep (150 × 100 m and 64 m). At 60x: a 2 × 4 brick 1.9 × 0.96 × 0.68 m, a 1.6 cm marble 0.96 m, a 4 cm
  alphabet block 2.4 m, a 5 cm toy car 3 m long, a crayon 5.4 m long and 0.48 m round, a 27 cm teddy 16 m sitting, a 2 cm puzzle piece
  1.2 m across. Every model is sized from 60x; the factor goes in the level file's header.
- **Palette**: natural beech and pine, cream, pastel yellow, mint and sage green, lilac and purple, peach and orange, white. Shaded
  toon, plain finishes: painted wood, glossy plastic, glass (marbles), wax (crayons), felt, card, tin.
- **Team colours stay out**: no red or blue toys, though red and blue are the commonest toy colours (red brick, red engine, blue
  car): every one is recoloured from the palette. Red and blue appear only on the flags, the scouts, the glow columns, the castles'
  team pennants and the team machines (tin soldiers' tunics, tanks, helicopters). Orange stays peach-orange, never scarlet; no teal.

## Footprint and boundary

- `play size=(150, 100) noise=0`: the outdoor levels' rectangle, so the castle layout's numbers (x = ∓62, play edge ∓75) carry over;
  x across, z along; flat (a chest's floor).
- **The chest**: `room size=(150, 64, 100)`: four walls on the play area's edges and the lid as the ceiling at 64 m, colliders on all
  six faces. Its look is new (27.3): walls of painted pine planks running up, 6 m wide boards (10 cm real) with dark seams, a
  stencilled pastel alphabet frieze (A-Z in yellow, green, lilac and peach) as a band from 8 to 12 m up, dovetail joints at the four
  vertical corners; the lid's underside the same planks in a darker, warmer pine with two cross battens. No checker.
- **The floor's regions** (four):
  - base `felt`: the chest's lining, sage-green felt, a fibre pattern in the shader;
  - `playmat`: a cream play rug in the middle, `RoundedBox2D center=(0, 0) size=(64, 84) radius=6`, under the train loop;
  - `road`: a printed grey road on the rug and felt, the centre lane's mark, 5 m wide, `Polyline` (−52, 0), (−17, 0), (−10, 7),
    (0, 8), (10, 7), (17, 0), (52, 0), `wobble=0` (it bends north round the jack-in-the-box, under both bridges);
  - `foam`: foam play mats under each castle, `Box2D center=(∓62, 0) size=(26, 34)`, pale yellow, 4 m tile seams drawn by the
    material's world grid (as `ground_tiles` does), `wobble=0`.
- Castles `flatten=22` (a no-op on a flat floor, kept for the rule) with no `ground=` paving: the foam is the paving.
- Every scatter and trees section has `reach=0`.

## Sky / lighting

- No sky: the chest is closed but for the lid, **propped open a crack along the north wall**. Along the top of the north wall a 3 m
  band of warm light (an emissive strip on the lid's edge, nothing modelled beyond it: no house room, no theme leak).
- One warm directional light (cream-yellow, like late-afternoon lamp light) coming in from the crack: from +z towards −z, pitched 50°
  down, no yaw. Light along z keeps the shadows mirrored across x = 0, so neither side gets the sunny or the shaded side.
- A soft fill (the ambient, cool lilac) so the south half is not murky; toon bands as the chessboard has them.
- Walls and lid cast and take no shadows; toys shadow the floor; the camera clears to black.

## Bases

- **A wooden block castle** on each side, `toy-castle0` (one model, both sides, as the fungal level uses `castle0` twice): a toy
  castle built from painted blocks, 26 m along z and 19 m deep, gate at +z.
  - Placed at (−62, 0) yaw=90 and (62, 0) yaw=−90, gate towards the middle; its walls reach x = ∓52.5 at the front and ∓71.5 at the
    back.
  - Walls of stacked beech and cream blocks with block crenellations, a wall-walk at 5 m; four round corner towers of cylinder
    blocks with lilac and mustard cone roofs (rings at 5.3 m, roofs to 10 m), at (∓53, ±13) and (∓71, ±13) after the turn; a gate
    tower over an arch block; a keep at the back of three stacked blocks (9 m) with a cone roof to 12 m. Stairs of 0.3 m step blocks
    up to the walk, as `castle0` has them. Floor heights as ticket 09: walk 5 m, pads 5.15 m, rings 5.3 m.
  - Team dressing: a pennant in the side's colour on each tower and the keep (`pennant-red`, `pennant-blue`, 1.2 × 2.5 m on a 3 m
    pole), placed with `y=` like 19's team mushrooms. The castle model itself has no red or blue.
  - Rejected: a dollhouse against a fort. The two bases would differ in shape and in cover; the layout must mirror.
- **Flag stand**: (∓60, 0), inside the courtyard before the keep, clear of the stairs.
- **Spawn**: (∓46, 20) yaw=±90 (west 90, east −90), radius 2.5, on the open floor off the castle's north front corner, 7.4 m from the
  corner tower, 24.4 m from its own flag, 12.5 m from the pistol pod.

## Pods and lanes

### Weapons

The level's own mix, **pistol, blob-gun, sniper, flamethrower**, which no other level has (meadow: minigun, pistol, sniper,
flamethrower; fungal: minigun, pistol, electro-gun, poison-gun; chessboard: electro-gun, poison-gun, sniper, blob-gun; kitchen
(28): minigun, flamethrower, electro-gun, blob-gun; bathroom (29): pistol, blob-gun, flamethrower, electro-gun, which its water
twist needs; computer (30): minigun, poison-gun, electro-gun, flamethrower). The goo of the blob gun fits the room, no minigun
means duels at middle range, and the one sniper sits on the north lane's long sight line.

| Object | West | East | Why |
|---|---|---|---|
| Pistol pod | (−44, 33) | (44, 33) | north of the spawn, a quick second gun |
| Blob-gun pod | (−46, −26) | (46, −26) | south of the castle, for the close fights round the rocking horse |
| Health pod (base) | (−73.5, −8) | (73.5, −8) | behind the castle, in the 3.5 m strip to the back wall |
| Health pod (north flank) | (−24, 42) | (24, 42) | on the north lane, a little past halfway |
| Health pod (south flank) | (−24, −42) | (24, −42) | on the south lane, a little past halfway |
| Neutral sniper (long range) | (0, 16) | | inside the train loop, north of the jack-in-the-box: the north half has the long sight lines |
| Neutral flamethrower (short range) | (0, −16) | | inside the loop, south of it: the south half has the close cover and the teddy's choke |

The neutral pods are 32 m apart, both inside the train loop: to take one you cross the track or go under a bridge.

### Lanes

- **North lane**: along the north wall, z 34-50, past the north flank health pods. Tall blockers keep 10 m off the walls (their
  band), so a strip z 40-50 holds only knee cover: a **100 m sight line** from one Ferris wheel (x = −56) to the other (x = 56)
  for the sniper.
- **Centre lane**: the road, from gate to gate along z ≈ 0, under the west bridge's arch, round the jack-in-the-box past the two
  neutral pods, under the east bridge. Short sight lines; the train crosses it twice a lap overhead.
- **South lane**: along the south wall, z −34 to −50, past the south flank health pods, closing to a 7.6 m choke between the loop's
  south end and the teddy's feet at x = 0. Close cover: the flamethrower's lane.
- **Flag to flag** (z = 0, 120 m): blocked by the jack-in-the-box at (0, 0). **Spawn to spawn** (z = 20): blocked by the block towers
  at (∓32, 20) and the loop's north curve.
- Mirrored across x = 0 throughout; north and south differ on purpose (open north, close south).

## Ground clutter

Group `bits`, no colliders, single material each, 200-500 triangles. Drifts with bare felt between, as on a hard floor (26 §4).

| Layer | Models | Numbers |
|---|---|---|
| Main drifts | `puzzle-piece0..2` (1.2 m, 0.12 m thick; two printed pastel faces up, one grey card back up), `confetti0..3` (patches of 8-12 paper dots and stars, 0.36 m each, over 1.2 m; yellow, mint, lilac, peach, one colour each) | `density=0.2 cluster=3 clusterScale=12 spacing=0.8 reach=0 margin=0.6 scale=(0.85, 1.15)` |
| Accent heaps | `brick-plate0..1` (a 1 × 1 round plate 0.48 × 0.2 m, a 1 × 2 plate 0.96 × 0.48 × 0.2 m), `bead0..2` (pony beads 0.54 m, 0.4 m tall), `crayon-stub0` (a broken stub 1.2 m long, 0.48 m round) | `density=0.05 cluster=4 clusterScale=6 spacing=0.6 reach=0 margin=0.6 scale=(0.85, 1.15)` |

- Both `avoid` the road and the foam (the bases' mats stay clean; the road reads as a lane).
- `draw bits rings=[10, 25, 45] outline=1 shadows=1 blend=1 fade=15 lowDensity=0.5 lowDistance=0.7`, no wind, `tintHeight=0`.

## Knee cover

`trees group=toys`, convex colliders, about 3k triangles, most 0.6-1.1 m so players jump them and bots go round.

| Model | Size (m) | Height | Note |
|---|---|---|---|
| `brick0`, `brick1` | 2 × 4 brick 1.9 × 0.96 | 0.68 | yellow, mint; studs on top (no flat jump-only top worth a bot) |
| `brick-stack0` | three bricks staggered, 2.9 × 1.9 | 1.05 | white, peach, yellow |
| `marble0..2` | 0.96 round | 0.96 | glass: yellow cat's-eye, green swirl, peach swirl; sphere-like hull |
| `crayon-pile0` | four crayons in a heap, 5.4 long | 1.0 | yellow, green, purple, orange, paper wrappers |
| `toy-car0`, `toy-car1` | 3.2 × 1.5 | 0.95 | a mustard hatchback, a mint pickup |
| `toy-car-tipped0` | 3.2 × 0.95 on its side | 1.5 | the hatchback on its side |
| `jack0` | metal jack lying, 1.0 across | 0.7 | silver tin |
| `arch-block0` | a half-arch block lying, 2.4 × 1.2 | 1.05 | natural beech |

`band=(-74, -6) density=0.005 spacing=7 cluster=2.5 clusterScale=18 scale=(0.9, 1.1) margin=1.5`. Weights: bricks and marbles 1, the
stack, crayons and cars 0.7, the tipped car and the jack 0.5.

## Tall blockers

`trees group=toys`, 4-8k triangles; convex up to 3 m, mesh above.

| Model | Size (m) | Height | Collider |
|---|---|---|---|
| `alpha-block0` | one 2.4 m alphabet block, tilted on an edge | 2.4 | convex |
| `alpha-block1` | two, the top one turned 20° | 4.8 | mesh |
| `alpha-block2` | three, leaning | 7.2 | mesh |
| `duplo-tower0`, `duplo-tower1` | 4 and 6 large bricks, 3.8 × 1.9, stepped | 4.6, 6.9 | mesh |
| `skittle0`, `skittle1` | wooden skittles, 2.4 across | 6 | convex (capsule-like, over 3 m: becomes mesh, accepted) |
| `spinning-top0` | tin top resting tilted on its rim, 4.2 across | 3.6 | mesh |

`band=(-72, -10) density=0.003 spacing=9 cluster=3 clusterScale=18 scale=(0.9, 1.1) margin=3`, after the knee cover's section in
the same group so neither cuts the other; 6 m clearing round each pod (`PodTreeClearing`). Clumps 10-20 m wide with felt between.

`draw toys rings=[25, 60, 1000] outline=3 shadows=2 lowShadows=1 blend=2 lowDistance=0.7`, no wind.

## Landmarks

Hand-placed, each group starting with a comment line (26 §4), 10 m or more from every pod, stand and spawn.

| Landmark | Size (m) | Collider | Position, yaw | Job |
|---|---|---|---|---|
| `jack-in-the-box0`: a 5 m painted box (yellow and lilac), the lid open, a spring and a jester (green and peach, bells) to 11 m | 5 × 5, 11 tall | mesh | (0, 0), 0 | the centre: blocks flag to flag, splits the centre lane |
| **The train loop** (the twist): `track-bridge0` × 2 and `track-curve0` × 12 | see below | see below | bridges (∓20, 0) yaw 0 | rings the neutral pods; the bridges carry the centre lane under the train |
| `block-tower0`: a leaning tower of alphabet blocks, two missing at 5 m to shoot through | 6 × 6, 10.5 tall | mesh | (−32, 20) yaw 15, (32, 20) yaw −15 | breaks the spawn-to-spawn line |
| `rocking-horse0`: a small wooden rocking horse, cream with a peach mane | 12 long, 4 wide, 9 tall, belly at 4 | mesh | (−36, −32) yaw 30, (36, −32) yaw −30 | splits the centre and south lanes before the blob-gun pod |
| `ring-stacker0`: five stacked rings on a cone peg, mint to lilac | 8 across, 11 tall | mesh | (−58, −40), (58, −40) | marks the south back corners, shelters the castle's south approach |
| `ferris-wheel0`: a toy Ferris wheel, 12 m wheel of spokes and six gondolas on an A-frame | 12 wide, 4 deep, 14 tall | mesh | (−56, 42) yaw 90, (56, 42) yaw −90 | ends the north sight line, a see-through screen before each castle's north side |
| `teddy0`: a teddy bear slumped against the south wall, legs out, honey-brown, one ear chewed | 13 wide, 9 deep, 16 tall | mesh | (0, −45.5) yaw 0 | makes the south lane's choke at x = 0 |

That is five mirrored pairs, two singles on x = 0 and the loop. The A-frame's legs leave 4 m or nothing between them (26 §7).

## Border

The chest's walls (27.3) with the lid crack along the north. Taller toys stand by the walls: the Ferris wheels in the north
corners, the ring stackers in the south, the teddy on the south wall; the castles fill the west and east walls. Tall blockers keep
10 m off the walls; knee cover and clutter come to 6 m and 0.6 m.

## Machines

Per side as 24: one helicopter, two tanks, four turrets, in the team's colour (team dressing), with the scouts' floating eyes (the
machines' eyes are 24's rule; the props have none). New toy models, same parts and limits as 24.1:

- **Turret**: a wind-up tin soldier, 2 m, on a round tin stand with a key in its back; tunic in team red or blue, white belts,
  a tall hat; holds a toy rifle. Parts: `Foot` (stand and legs), `Head` (torso, arms, hat: turns), `Barrel` (the rifle: tilts).
- **Tank**: a tin wind-up tank, 0.8 m long, team-coloured body, silver tracks, a key on the back. Parts: `Body`, `Turret`, `Barrel`.
- **Helicopter**: an RC helicopter, 0.8 m, team-coloured canopy, white skids, two blades. Parts: `Body`, `Rotor`, `TailRotor`.

| | West | East |
|---|---|---|
| Turrets (front) | (−47, 10) yaw 60, (−47, −10) yaw 120 | (47, 10) yaw −60, (47, −10) yaw −120 |
| Turrets (flanks) | (−62, 21) yaw 30, (−62, −21) yaw 150 | (62, 21) yaw −30, (62, −21) yaw −150 |
| Machine zone | (−69, 24) yaw 90 radius 3 | (69, 24) yaw −90 radius 3 |

The zone is off the castle's back north corner (8.5 m from its tower), on open floor the tanks can leave by the north lane.
Helicopters fly at 6-12 m and at 16 m over the castles (roofs 12 m), under a flight ceiling of 20 m, far under the 64 m lid; they
keep 3 m off the walls.

## Gameplay twist: the toy train

A wooden train runs the loop round the middle all match, on a fixed timetable from the round's start (its position is a function
of the round clock, so a respawn or a new round needs nothing saved).

- **The loop**: a stadium, the straights along z at x = ±20 (z −12 to 12, 24 m, each a bridge), the ends semicircles of radius
  20 m about (0, ±12), so the loop spans x ±21.2 and z ±33.2. Length 174 m.
  - `track-curve0`: a 30° arc of wooden track on a 20 m radius, 2.4 m wide, **0.3 m high** (under the 0.4 m step: everyone walks
    over it), no collider (the floor carries the scouts). Six per end, hand-placed.
  - `track-bridge0`: 24 m long; ramps of 8 m up to a deck at 3.8 m (25°), the deck 8 m with an arch beneath, 6 m wide and 3.2 m
    clear, which the road runs through. Wooden trestle look. Mesh collider; the ramps and the deck are walkable, so the deck is a
    perch over the centre lane (in the navmesh window).
- **The train** (`toy-train-engine`, `toy-train-flat`, `toy-train-wagon`): an engine 4.8 × 1.8 m, 2.6 m tall at the cab, mint green
  with yellow wheels and a white chimney; a flat wagon with a lilac deck at 1.0 m (rideable); an open wagon, peach, 1.8 m sides.
  15 m long with the couplings. Convex colliders per car.
- **Running**: anticlockwise from above at 7 m/s, a lap in 25 s, constant speed, the cars following the track's centre line (its
  height on the bridges). A puff from the chimney every second and a whistle puff 2 s before each bridge, for players to read.
- **Blocks**: the cars stop shots and sight; it is moving cover through the neutral pods' ring.
- **Knocks aside**: a scout or tank in its way is pushed 3 m off the track sideways and takes 20 damage. A kill by it shows in the
  kill feed as "Toy train" with no weapon, gives no kill credit and no spores. Flags, pods and turrets ignore it; a dropped flag on
  the track stays where it lies.
- **Rides**: a player who jumps onto the flat wagon (1.0 m) is carried round (a moving platform for the character controller),
  flag or no flag. Bots do not ride.
- Rejected twists: the lid opening (it would show the house room beyond, another theme) and a jack-in-the-box that springs (a
  centre landmark that changes the sight lines mid-match is unfair to whoever is behind it).

## New models list with budgets

All in `content/models/toys/` (team machines in `content/models/machines/` beside 24's), materials in `materials/toys/`. LOD0
triangles at 256 cells; castle parts at 128.

| Model | Size (m) | Collider | LOD0 | Materials |
|---|---|---|---|---|
| `puzzle-piece0..2` | 1.2 × 1.2 × 0.12 | none | 300 | card print, card back |
| `confetti0..3` | patch 1.2, 0.02 tall | none | 300 | paper (one colour each) |
| `brick-plate0..1` | 0.48 / 0.96 × 0.48 × 0.2 | none | 300 | plastic |
| `bead0..2` | 0.54, 0.4 tall | none | 300 | plastic |
| `crayon-stub0` | 1.2 × 0.48 | none | 400 | wax, paper |
| `brick0..1` | 1.9 × 0.96 × 0.68 | convex | 2.5k | plastic |
| `brick-stack0` | 2.9 × 1.9 × 1.05 | convex | 3k | plastic |
| `marble0..2` | 0.96 | convex | 2.5k | glass |
| `crayon-pile0` | 5.4 × 2 × 1.0 | convex | 3k | wax, paper |
| `toy-car0..1`, `toy-car-tipped0` | 3.2 × 1.5 × 0.95 | convex | 3.5k | painted tin, tyre, glass |
| `jack0` | 1.0, 0.7 tall | convex | 2k | tin |
| `arch-block0` | 2.4 × 1.2 × 1.05 | convex | 1.5k | wood |
| `alpha-block0..2` | 2.4 / 4.8 / 7.2 tall | convex / mesh | 3k / 5k / 7k | painted wood, wood |
| `duplo-tower0..1` | 3.8 × 1.9, 4.6 / 6.9 tall | mesh | 5k / 7k | plastic |
| `skittle0..1` | 2.4 × 6 | convex | 5k | painted wood |
| `spinning-top0` | 4.2 × 3.6 | mesh | 6k | tin |
| `jack-in-the-box0` | 5 × 5 × 11 | mesh | 22k | painted wood, cloth, spring tin |
| `block-tower0` | 6 × 6 × 10.5 | mesh | 18k | painted wood |
| `rocking-horse0` | 12 × 4 × 9 | mesh | 20k | painted wood, yarn |
| `ring-stacker0` | 8 × 11 | mesh | 15k | plastic |
| `ferris-wheel0` | 12 × 4 × 14 | mesh | 25k | plastic, tin |
| `teddy0` | 13 × 9 × 16 | mesh | 25k | plush, felt, button |
| `track-curve0` | 30° of a 20 m radius, 2.4 × 0.3 | none | 1.5k | wood |
| `track-bridge0` | 24 × 2.4 × 3.8 (arch 6 × 3.2) | mesh | 12k | wood |
| `toy-train-engine` | 4.8 × 1.8 × 2.6 | convex | 8k | painted wood |
| `toy-train-flat`, `toy-train-wagon` | 4.2 × 1.8 × 1.0 / 1.8 | convex | 3k | painted wood |
| `toy-castle0` (parts in `content/parts/toys/castle/`) | 19 × 26, walk 5, keep 12 | mesh | ≤ 80k (parts 1-5k) | wood, painted wood |
| `pennant-red`, `pennant-blue` | 1.2 × 2.5 on a 3 m pole | none | 1k | cloth, wood |
| `turret-toy-<team>-foot/head/barrel` | 2 tall | (as 24) | 5k total | tin, team paint |
| `tank-toy-<team>-body/turret/barrel` | 0.8 long | (as 24) | 4k total | tin, team paint |
| `helicopter-toy-<team>-body/rotor/tail-rotor` | 0.8 long | (as 24) | 4k total | plastic, team paint |

Materials (`materials/toys/`): beech, pine, painted wood (per palette colour), plastic (glossy, per colour), glass (marbles), wax,
paper, card, tin, plush, felt, cloth; ground `ground_felt`, `ground_playmat`, `ground_road`, `ground_foam`; the room's
`toybox-wall` and `toybox-lid`. No material from another theme's folder.

**Reuse**: the scouts, flags, flag stand, health and weapon pods, weapons. No scenery is reused: the chessboard's marbles and
dice live in `games/` (board games) and would fail this level's theme test; toy marbles are their own (`toys/marble0..2`, other
colours). Parts may be copied into `content/parts/toys/` if a model wants a shape (the chessboard's marble).

## Bots and the navmesh

- **Spawns** at the floor (y = 0); the bridge decks (3.8 m) and the castles' walks (5 m) and rings (5.3 m) are inside the 7.5 m
  window. The jack-in-the-box's box top (5 m), the teddy's lap (3 m), the rocking horse's back and the block towers are not reached
  by bots: few unreachable spots.
- **The track** is 0.3 m: walkable, no links needed. Under the bridges' arches 3.2 m clear (the agent is 1.75 m).
- **The train** is not in the baked navmesh. Each car carries a carving `NavMeshObstacle` (both agent types) while moving, and a bot
  whose path crosses the track within 25 m of the train ahead waits at the track's edge until it has passed (a "crossing" check in
  `Bot`). Tanks steer round it like players; a tank in its way is pushed.
- **Pinch points**: the rocking horse's rockers (0.8 m) are jumped by players and walked round by bots; the Ferris wheel's A-frame
  legs leave 4 m or touch; block clumps keep 1.5 m or under 0.6 m by their margins; the teddy's choke is 7.6 m.
- **Tank navmesh**: each machine zone joins the other through the north lane and the road; the bridge ramps (25°) are under the
  tank's 30° slope, so tanks may climb them (harmless; the train pushes them off).
- **Check Navmesh**: each spawn reaches the enemy stand and every pod, including the neutral pods inside the loop (over the track and
  under the bridges); the castles' walks join the floor; scouts keep off each turret.

## Performance

Measured with `LevelSnapshot` on High and Integrated GPU / Web (26 §6); targets, with the chessboard's figures beside them when
measured:

| View | High target | Web target |
|---|---|---|
| West spawn facing east | ≤ 1.5M triangles, ≤ 180 draws | ≤ 500k, ≤ 200 draws |
| The centre (jack-in-the-box, both bridges, the train) | ≤ 1.5M | ≤ 500k |
| A castle gate facing the other castle | ≤ 1.5M (both castles ≤ 160k) | ≤ 500k |
| Above | ≤ 2M | — |

Shares: bits ≤ 200k on High, ≤ 80k on Web; toys ≤ 400k on High, ≤ 120k on Web; landmarks drawn as scene objects with LOD groups.
The train adds 3 draws and 14k triangles at most.

## Risks

- **The room's look**: `room` draws only `RoomChecker`. 27.3 adds a plain look (`room size=(150, 64, 100) walls=toybox-wall
  ceiling=toybox-lid`), and a room whose size is not a whole number of 8 m squares (150 and 100 are not): check `RoomSpec.Squares`
  and the room's mesher with a non-cube, non-checker room.
- **The lid crack**: an emissive strip only; if it reads as a gap to look through, add nothing behind it (the house room is another
  theme).
- **Machines per level**: the builder makes `turret-<team>` and friends for every level; a level must pick its machine set
  (`machines set=toy` or a per-level prefab prefix). 27.5.
- **Helicopters in a room**: their flight area and limits assume the island's hills; they need the walls and the 20 m ceiling.
- **The train**: a new moving collider; riding needs moving-platform support in the character controller; a killer that is
  neither player nor machine in the kill feed; carving obstacles on the web (three moving obstacles, which should be cheap; if
  not, the crossing check alone).
- **Team colours**: the most tempting toys are red and blue; every model is reviewed on its png for a red or blue group.
- **Budgets most likely over**: the centre view (jack-in-the-box, two bridges, the train, the playmat's clutter) and the gate view
  with two block castles; the castle's many small blocks may tempt over 80k.
- **The track and the trees section**: tall blockers keep their margin from each placed track piece; check from above that no
  clump sits on the loop's inside between curve pieces.

## Subtickets

One agent each (26's checklist; the generic infrastructure first, as 28-30 reuse it). The original seven items are split into eleven:

- [27.1 Room looks](27.1-room-looks.md): walls and lid materials, any room size, the lid-crack strip, data-driven light (generic).
- [27.2 Machine sets](27.2-machine-sets.md): `machines set=<name>`, prefabs per set, helicopter limits in a room (generic).
- [27.3 Path movers](27.3-path-movers.md): a looping timetable vehicle, `mover` in the level file, `PathMover`, moving platform in the controller (generic).
- [27.4 Models: clutter and knee cover](27.4-models-clutter-knee.md)
- [27.5 Models: tall blockers, track, train](27.5-models-blockers-track-train.md)
- [27.6 Models: landmarks](27.6-models-landmarks.md)
- [27.7 Toy castle and pennants](27.7-toy-castle.md)
- [27.8 Toy machines](27.8-toy-machines.md)
- [27.9 Level file](27.9-level-file.md): `toybox.level`, tests, ground materials, the bake.
- [27.10 Train behaviour](27.10-train-behaviour.md): knock-aside, "Toy train" kill, puffs, bots' crossing check.
- [27.11 Bake and check](27.11-bake-and-check.md): remesh, rebuild, snapshots, budgets.

Open questions were answered with defaults: the train pushes and does 20 damage, riding is in, the machines keep the scouts' floating eyes, the teddy and
jester have painted toy faces.

## Acceptance

- `ToyBox.unity` is built from `toybox.level` with no hand placement; every model in it is a toy or a battle object, no red or blue
  props, scale 60x in the file's header.
- The layout mirrors across x = 0 (`BattleSidesTests`), the weapons are pistol, blob-gun, sniper and flamethrower, and no
  straight line joins the flags or the spawns.
- Check Navmesh: every spawn reaches the enemy stand and all pods; few unreachable spots.
- A full match with 7 bots: spawn, take each pod, cross each lane, carry a flag home; bots cross the track without being run over
  more than now and then, nobody sticks on a toy.
- The train laps every 25 s, blocks shots, knocks scouts aside for 20 damage, carries a player on its flat wagon.
- The machines are toys, in team colours, and fly under the lid.
- Triangles and draws per group listed for each snapshot view on High and Integrated GPU / Web, within 26's budgets.

## Open questions for the user

1. Theme, 60x scale, the block castles and the train twist: yes? (26's checklist asks before modelling.)
2. The train: 20 damage and a push, or a push only? Riding in or out of scope for the first cut?
3. The machines keep the scouts' floating eyes (24), or toy machines without them?
4. The teddy and the jester: painted toy faces, or faceless?

## Status

Not started.
