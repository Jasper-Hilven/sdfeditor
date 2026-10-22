# 30 — Inside the computer level

The last battle level of the campaign: **inside 42's computer**, at chip scale. A motherboard city in a closed case: gold circuit
traces glowing like roads, heatsinked chips standing as buildings, capacitors as towers, two RAM banks as the bases, the GPU as a
wall of fans along the north side and the CPU cooler as a giant fan tower in the middle. Players aside, everything in it is computer
hardware. Built into `Assets/Scenes/Computer.unity` from `content/levels/computer.level`.

It follows the ruleset in [26](done/26-level-ruleset.md) and fills in its template below. Every number not given here (pod and stand
radii, clearances, agent sizes, preset budgets) is the ruleset's.

Depends on: 26 (ruleset), 16-25 (battle: pods, sides, match, flag, weapons, bots, machines, spores), 31 (story intro), 32 (campaign
flow: where this level sits, what comes before and after). Feeds 33 (final boss, Chanterelle Prime): the boss level may reuse this
level's models, so they live in their own folder and nothing in them is specific to this layout.

## Story

Single player: 42 and red bots against blue bots, no fake multiplayer. It comes after the house-room levels (toy box, kitchen,
bathroom) and is the last battle before the boss: the blue mushrooms have pulled 42 all the way into the machine, and this is
Chanterelle Prime's home turf ("Prime's server" in 32). 32 sets the match rules here (blue plays five against four and respawns in
3 s against red's 5 s) and the beat before it (B6, "The results file"); its scoreboard of light is the beat's effect, not a level
model, so the level's no-text rule stands. This ticket only builds the level and its hazard (the data pulses). 42 is always
they/them in captions, comments and docs.

## Theme and scale

- **One theme: the inside of a desktop computer**, a big workstation board in its case. On theme: what a person sees when they open
  the side panel. Board parts: circuit traces, pads, vias, silkscreen, SMD resistors and capacitors, tantalum and electrolytic
  capacitors, chokes, crystals, TO-220 transistors, chips (SOIC, QFP, BGA under heatsinks), pin headers and jumpers, SATA ports,
  PCIe and DIMM slots, the coin-cell BIOS battery, mounting screws. Cards and cooling: RAM sticks, the GPU, the CPU and its tower
  cooler, VRM and chipset heatsinks, case fans. Cables: ribbon cables, power connectors and their wire bundles, zip ties. Dirt: solder
  blobs, thermal paste, dust bunnies.
- **Not on theme**: anything from the other levels or the home (no board games, toys, food, bathroom things, no fungi, grass or sky,
  no `home/` model: 31's rule); nothing outside a case (no keyboard, mouse, monitor, desk); no abstract "cyberspace" (no floating
  ones and zeros, Tron grids, holograms or text). Hardware only, and **no text anywhere** (no part numbers, labels or logos): it
  reads as noise at eye height.
- **Scale: 250 times life size** (1 mm = 0.25 m), written in the level file's header. The scout is 7 mm tall, about the height of a
  small electrolytic capacitor; its eye is at 6 mm. Every model below is sized from real parts by that factor: an 0805 resistor
  0.5 m, a 10 × 16 mm capacitor 2.5 × 4 m, a PCIe x16 slot 22 m long, a DIMM with heatspreader 33 × 11 m, a 120 mm fan 30 m, a
  GPU 67 m long. The board itself (160 × 120 m, so 640 × 480 mm) is bigger than any real one; the parts on it are at one scale.
- **Palette**, kept clear of team red and blue:
  - Board (solder mask): a deep, dark **teal-green**, satin, a faint lighter copper-pour hatch in the shader (no triangles). Dark
    enough that the traces glow against it, light enough that the toon bands still read.
  - Traces: **gold**, emissive. Pads and solder: **tin silver**. Silkscreen: **off-white**, used sparingly.
  - Parts: black epoxy, tan ceramic, tantalum yellow, aluminium silver, black rubber, copper, gunmetal heatsinks; capacitor sleeves
    black-and-gold, purple and olive.
  - Light accents: warm white and gold only. **No cyan**: it is too close to the east's chanterelle blue.
  - Team colour only where the ruleset allows it: the flags, scouts, glow columns, machines, and each base's **RAM light bars**
    (red west, blue east, the bases' team dressing like 19's team mushrooms).
- **Toon look**: flat light bands, the ink outline, a hard highlight on metal, plain forms. Fin stacks, grilles and pin rows are
  big simple forms with the pattern in the shader where possible, never hundreds of modelled fins.

## Footprint and boundary

- `play size=(160, 120) noise=0`: a closed room, flat as a board. x across (west −80 to east +80), z along (south −60 to north +60).
- `room size=(160, 64, 120) square=8`: **the case, lying on its side**. The floor is the motherboard; the four walls are the case's
  inside (steel, 64 m high: a case 160 mm deep); the ceiling is the **tempered-glass side panel**. Colliders on all six faces.
- **Wall look** (new, 30.2; the room draws the chessboard's checker today): dark gunmetal steel, a honeycomb vent pattern on the upper
  half of the west and east walls (in the shader), and an **LED strip**: a warm-white emissive band 0.5 m high at 3 m up, all round
  the four walls, the room's one lit edge. The ceiling glass: very dark smoked grey, a faint diagonal reflection streak and a soft
  warm glow patch off-centre (42's room lamp beyond, as a gradient only: no home model).
- **Regions** (`regions base=pcb`, `wobble=0` on all: ruled straight like the chessboard's squares), drawn in
  `materials/ground/ground_<region>.sdfmat`:
  1. `trace` (gold, emissive): **three buses** per side, each three parallel traces 0.8 m wide and 1.6 m apart (centre to centre),
     with 45° jogs like a real board. Centre lines (west; east mirrored):
     - north bus: (−44, 10) → (−40, 10) → (−22, 28) → (0, 28), ending at the north neutral pod;
     - centre bus: (−42, 0) → (−7, 0), ending at the CPU socket;
     - south bus: (−44, −10) → (−40, −10) → (−22, −28) → (0, −28), ending at the south neutral pod.
     Data flows from the RAM (the bases) to the CPU (the middle); the pulses (twist) run this way.
  2. `silk` (off-white): rings round each pad (a `Circle` 0.6 m larger than its pad, under it) and a rectangle outline round each
     base courtyard; nothing else.
  3. `pad` (tin): a `Circle` r 2.2 under every pod and flag stand, a `Box2D` 7 × 7 m under each spawn zone, so the battle objects
     read as soldered spots on the board.
  Later regions win: trace, then silk, then pad. No path or trail: the buses are the lane marks.

## Sky / lighting

- No sky; the camera clears to black (the room is all there is).
- One **cool white directional light** from above (through the glass), 55° up, from the south-west, colour (0.92, 0.96, 1.0), about
  70% of the chessboard's intensity; a low dark-teal ambient, so the emissive traces and the LED strip carry the mood.
- Walls and ceiling cast and take no shadows; parts shadow the board.
- Glow is **cheap emissive toon** (30.1): an `emission` amount per material, added after the toon bands, so a lit face stays at its
  colour in shadow. No real lights, no light probes, no bloom on the web preset (it has no HDR); the glow must read from the flat
  bright colour against the dark board alone. Emissive parts: the traces, the pulses, the LED strip, the RAM light bars, the case
  fans' rings, a few tiny SMD LEDs (gold/white). At most about a tenth of the screen glows at eye height (14's rule for glow).

## Bases

Each side's base is a **RAM bank fortress**: two bank models (`ram-bank-red` / `ram-bank-blue`, each two DIMMs with
heatspreaders standing in their slots, 36 × 4.6 × 11 m), running along x from the back wall towards the middle, with the courtyard
between them.

| West (east mirrored, x negated, yaw negated) | Position | Notes |
|---|---|---|
| North RAM bank | `ram-bank-red at=(−62, 9) yaw=90` | spans x −80 to −44, z 6.7 to 11.3; flush with the back wall (no gap behind) |
| South RAM bank | `ram-bank-red at=(−62, −9) yaw=90` | spans z −11.3 to −6.7 |
| Gate | `pcie-x1-slot0 at=(−46, 0) yaw=0` | a 6.25 × 1.9 × 2.75 m slot across the courtyard's mouth: two 3.6 m gates north and south of it |
| Courtyard | x −80 to −44, z ±6.7 | 13.4 m wide, plain board with a silk outline; no flatten or paving (the floor is flat) |
| Flag stand | (−72, 0) | at the back of the courtyard on a pad; flag to flag 144 m |
| Spawn | (−62, 21) yaw=90 r 3 | outside the north bank, 6.7 m from its face, on a 7 × 7 m pad; 23 m from its own flag |

The RAM is dark gunmetal with a frosted light bar along the top glowing the side's colour (the team dressing): the bases read as red
and blue from across the board and nothing else does. Both banks together are the base budget (about 80k triangles each side, 30.6).

## Pods and lanes

### Weapons

The mix: **minigun, poison-gun, electro-gun, flamethrower**, which no other level has (meadow: minigun, pistol, sniper, flamethrower;
fungal: minigun, pistol, electro-gun, poison-gun; chessboard: electro-gun, poison-gun, sniper, blob-gun; toy box and bathroom: pistol,
blob-gun, electro-gun, flamethrower; kitchen: minigun, flamethrower, electro-gun, blob-gun). The electro-gun fits the
place.

| West (east mirrored) | Position |
|---|---|
| Side pod: minigun | (−48, 24), north of the gate, looking out over the north lane |
| Side pod: poison-gun | (−48, −24), south of the gate, for holding the courtyard (about 30 m) |
| Health, near the base | (−74, −20), outside the south bank by the back wall |
| Health, north flank | (−34, 42) |
| Health, south flank | (−34, −42) |
| Neutral long-range: electro-gun | (0, 28), north, where the two north buses meet; the north lane is the open one |
| Neutral short-range: flamethrower | (0, −28), south, where the two south buses meet; the south lane has the close cover |

The neutral pods are 56 m apart on x = 0.

### Lanes

- **North lane** (z 30 to 50): between the PCIe slots and the GPU. Open: the long sight line along z ≈ 46, 70-80 m from each end
  before the scattered capacitors break it near the middle (checked in the snapshots), for the electro-gun's 60 m. The VRM heatsinks
  stand in it near the middle.
- **Centre lane** (z −8 to 8): out of the courtyard's gates, along the centre bus, under the cooler's overhang and round its trunk.
- **South lane** (z −30 to −55): the chipset towers, the BIOS battery and the power connectors make close cover for the flamethrower and
  the poison-gun.
- **Lane walls**: a PCIe x16 slot each side of the centre lane in each half, `pcie-x16-slot0 at=(−18, ±16) yaw=90` (22.25 × 1.9 ×
  2.75 m, spanning x −29.1 to −6.9), with a **ribbon-cable bridge** over its middle (`ribbon-bridge0 at=(−18, ±16) yaw=0`: 16 m
  long along z, 5 m wide, a 4 m high flat top, 38° ramps). Lanes join at both ends of each slot (13.8 m open between the west and
  east slots at the middle, 15 m and more at the west end) and over the bridges.
- **Flag to flag** (z = 0) is broken by the gate slots and the cooler's trunk; **spawn to spawn** (z = ±21) by the bridges' ramps (2.4 m
  high there) and the north caps.
- The layout is mirrored across x = 0 and, apart from the GPU and the battery, also across z = 0: north and south are equally far
  from both bases.

## Ground clutter

Scatter, group `parts`, **no colliders**, one material each where possible, `reach=0`, `avoid=trace,pad` (the buses stay clean, the
roads read), `scale=(0.85, 1.15)`, `tintHeight=0`, no wind. On a hard floor: drifts with bare board between; the drifts read as the
little fields of parts round every chip.

| Layer | Models (real size → at 250×) | Numbers |
|---|---|---|
| Main | `smd-resistor0`, `smd-resistor1` (0805: 0.5 × 0.31 × 0.13 m, black with tin ends); `smd-cap0` (0805 ceramic, tan, 0.5 × 0.31 × 0.31 m); `solder-blob0`, `solder-blob1` (flattened silver domes 0.2-0.4 m); `via-pad0` (a cluster of 3-5 vias, tin rings with dark holes, 1.2 m across, 0.04 m high) | `density=0.15 cluster=3 clusterScale=10 spacing=0.8 margin=0.6` |
| Accent | `dust-bunny0`, `dust-bunny1` (grey fluff balls 0.4-0.6 m); `zip-tie-tail0` (a cut tail, white, 3-5 m long, 0.63 × 0.25 m section, lying in a slight curl) | `density=0.03 cluster=4 clusterScale=6 spacing=0.9 margin=0.8` |
| Hand-placed | `thermal-paste-dot0` (grey glossy blobs 0.5-1 m, 0.15 m high): six smears round the CPU socket, in the cooler's group | — |

Draw: `draw parts rings=[10, 25, 45] outline=1 shadows=1 blend=1 fade=15 lowDensity=0.5 lowDistance=0.7`; small detail
takes `shadows near`. 9 scattered variants (fewer than the chessboard's 20 tokens: draws scale with variants).

## Knee cover

Trees section, group `smd`, `collider convex`, `band=(−76, −6) density=0.005 spacing=7 cluster=2.5 clusterScale=18 margin=1.5
scale=(0.9, 1.1)`. Mostly 0.6-1.1 m: cover to crouch behind and a player's jump; bots go round. Tops rounded or sloped where the
part allows, so few flat tops leave unreachable spots.

| Model | Real part | Size at 250× (l × w × h) |
|---|---|---|
| `tantalum-cap0` | 7343 tantalum, yellow with a tan stripe | 1.8 × 1.1 × 0.72 m |
| `smd-inductor0` | 6 × 6 × 3 mm shielded inductor, dark grey, rounded top | 1.5 × 1.5 × 0.75 m |
| `crystal-smd0` | HC-49 SMD crystal, a silver capsule | 2.85 × 1.2 × 1.05 m |
| `to220-lying0` | TO-220 transistor lying flat, legs bent into the board, sloping tab | 4 × 2.5 × 1.1 m |
| `screw-mount0` | M3 pan-head screw in a plated mounting hole, domed | 1.5 × 1.5 × 0.63 m |
| `smd-can-cap0` | 4 × 4.5 mm SMD aluminium capacitor, olive sleeve | 1 × 1 × 1.1 m |

Draw: `draw smd rings=[20, 50, 1000] outline=2 shadows=1 lowDistance=0.7`.

## Tall blockers

Trees sections, group `components`, convex colliders (the PCIe x1 slot becomes mesh by its length), no wind.

| Section | Models (size at 250×) | Numbers |
|---|---|---|
| Field | `cap-electrolytic0` (8 × 12 mm: 2 × 3 m, black-and-gold sleeve), `cap-electrolytic1` (10 × 16 mm: 2.5 × 4 m, purple), `cap-electrolytic2` (10 × 20 mm: 2.5 × 5 m, black-and-gold); `choke0` (13 × 13 × 10 mm power choke: 3.25 × 3.25 × 2.5 m); `pin-header0` (2 × 5 header with a yellow jumper cap: 3.2 × 1.3 × 2.1 m, pins 0.47 m apart, so shut); `heatsink-pin0` (pin heatsink on a chip: 3.5 × 3.5 × 2.5 m); `sata-ports0` (a 2 × 2 SATA port block: 3.75 × 2.5 × 3.75 m); `pcie-x1-slot0` weight 0.5 | `band=(−72, −10) density=0.003 spacing=9 cluster=3 clusterScale=16 margin=3 scale=(0.9, 1.1)`: clumps 10-16 m wide with open board between |
| By the walls | `cap-big0` (16 × 25 mm: 4 × 6.25 m, black-and-gold), `cap-electrolytic2` | `band=(−12, −3) density=0.002 spacing=10 margin=3`: the taller parts nearer the walls (26's rule for indoor borders) |

Capacitor tops carry the vent cross as a shallow groove (no flat top problem above 1.1 m). Draw: `draw components rings=[25, 60, 1000]
outline=3 shadows=2 lowShadows=1 blend=2 lowDistance=0.7`.

## Landmarks

Hand-placed (`place`), each group starting with a comment line (an overlap group for `LevelFileTests`). Pairs mirrored across x = 0;
the three singles stand on x = 0 and are symmetric themselves. All keep 10 m from every pod, stand and spawn.

| Landmark | Size | Collider | Position | Job |
|---|---|---|---|---|
| **CPU cooler** `cpu-cooler0`: the socket (12 × 12 × 1.5 m frame, CPU lid inside, lever along the south edge), a cold-plate block 10 × 10 × 2.5 m on it, 8 copper heatpipes 1.5 m thick rising straight from it as one trunk to a **fin stack** 12.5 (x) × 31 (z) m from 11 to 38 m up, and a **120 mm fan on each x face** (30 × 30 × 6.25 m, bottom 10 m up), both spinning slowly | 31 × 25 × 39 m | mesh (trunk and socket; nothing above 7.5 m is walked on) | (0, 0) | the centre blocker: breaks flag to flag, shelters the centre lane under its overhang, seen from everywhere |
| **GPU** `gpu0`: a three-fan card standing in its slot, fans (22.5 m each) facing south, backplate against the north wall, closed down to the board (no gap under it) | 67 × 10 × 28 m | convex boxes / mesh | (0, 55) | the north border: a wall of fans along the north lane |
| **BIOS battery** `coin-battery0`: a CR2032 standing on edge in a vertical holder, a raised "+" on its face | 6 × 2.5 × 5.5 m | convex | (0, −50) | breaks the south lane's line at the middle |
| **VRM heatsinks** `vrm-heatsink0`: a long gunmetal block with a fin-pattern top on a row of chokes | 15 × 5 × 7.5 m | convex (mesh by size) | (∓16, 37) yaw=90 | stand in the north lane near the middle; cover by the electro-gun pod |
| **Chipset towers** `chipset-heatsink0`: a BGA chip under a squat finned heatsink, a building | 10 × 10 × 6.25 m | convex | (∓50, −46) | close cover in the south lane; shelter the poison-gun pod |
| **PCIe x16 slots** `pcie-x16-slot0` | 22.25 × 1.9 × 2.75 m | mesh | (∓18, ±16) yaw=90 | the lane walls |
| **Ribbon bridges** `ribbon-bridge0`: a grey ribbon cable (its pin-1 stripe dark grey, not the usual red) draped over each slot, folded flat at both feet, its sides closed down to the board (no crawl gap under the ramps) | 16 × 5 × 4 m | mesh | (∓18, ±16) yaw=0 | the crossing over each lane wall, a raised spot to shoot from; breaks spawn to spawn |
| **Power connectors** `power-connector0`: an 8-pin EPS plug in its header, its wire bundle (eight black and yellow wires, 1 m each) rising in a curve to the ceiling and out of sight | 5.3 × 2.3 m foot, 64 m high | mesh up to 7.5 m, none above | (∓70, −53) | marks the south corners |
| **Case fans** `case-fan0`: 120 mm, black frame, white blades, a warm-white emissive ring, slowly spinning | 30 × 30 × 6.25 m | none (out of reach) | (∓76.9, 0) y=14 yaw=±90 | on the back walls over each base's RAM: the bases' marker from afar |

Spinning (cooler fans, GPU fans, case fans): a rotating part, turned by a small script (like the helicopter rotors), no physics.

## Border

The case walls with the LED strip and honeycomb vents; the GPU along the north wall; the RAM banks against the west and east walls;
the case fans above them; the power connectors' bundles in the south corners; the big capacitors in the wall band. Nothing outside the
room.

## Machines

Per side, as on the fungal level (24): 4 turrets, 2 tanks, 1 helicopter, in computer parts, team-coloured, with the scouts' floating
eyes like 24's (to confirm with the user: the eyes mark them as team members, not props).

| Machine | Model | Size |
|---|---|---|
| Turret | `cap-turret`: an electrolytic capacitor can on its rubber foot, the sleeve in the team's colour; the head (the top with its vent cross) turns, the barrel is a capacitor lead from under its rim | 0.8 m across, 2 m high |
| Tank | `chip-tank`: a black SOIC chip body on two tin-plated tread rolls (its gull-wing legs), a team-coloured top; the turret a small silver crystal can, the barrel a resistor | 0.8 × 0.5 × 0.45 m |
| Helicopter | `fan-copter`: a micro blower fan on a small team-coloured board, the impeller spinning flat on top as the rotor, a tiny tail fan on a flat cable | 0.8 m |

Parts named like 24's (`cap-turret-red-head`, `chip-tank-blue-body`, ...), in `content/models/machines/`. The level picks its machine
look with a new line (`machines look=computer`, 30.7); the fungal level keeps its own.

| West (east mirrored) | Position |
|---|---|
| Front turrets | (−38, 5) yaw=60, (−38, −5) yaw=120: either side of the courtyard's mouth, clear of the centre bus |
| Flank turrets | (−62, 32) yaw=30, (−62, −32) yaw=150 |
| Machine zone | (−72, −34) r 3, the open south-west corner behind the base line; tanks drive out along the south bank |

Helicopters fly under the room's machine ceiling (spawn + 10 m, `HelicopterMachine`): at 6-10 m they meet the RAM banks (11 m), the
VRM heatsinks (7.5 m), the cooler's fans (from 10 m) and the power bundles. 30.7 gives them keep-out footprints (every placed model
taller than 6 m, from its bounds) that their flight points and legs avoid.

## Gameplay twist: data pulses

Bright **pulses run along the buses from each base to the middle** (RAM to CPU), every 4 s, at 18 m/s, a 3 m long white-gold band
over all three traces of a bus. A scout (player or bot) on a bus's traces within 1.5 m of a passing pulse gets **+35% move speed for
2.5 s** ("Overclocked"), refreshed by the next pulse, never stacked; the flag carrier too, which more than cancels the 10% carrying
slow. Machines are not boosted.

So the buses are the fast way out to the middle, but they are lit, open roads: everybody sees who runs them. Coming home, the pulses
come at you; a boost works whichever way you run, but you catch fewer of them. Both sides' buses are mirrored, so it is fair.

- **Format**: a `pulse` flag on a `trace` polyline (pulses run from its first point to its last) and one line
  `pulses every=4 speed=18 length=3 boost=1.35 for=2.5`.
- **Look**: one overlay mesh of flat strips 2 cm over the traces, UV.x = metres along the polyline, an unlit additive shader
  (`SdfPulse`) drawing the band from time: one draw call, no per-frame CPU work, no lights.
- **Rule**: in `SdfBattle.Core` (`PulseRules`, pure: is point p on a bus boosted at time t), the same formula as the shader, with
  EditMode tests. A small gold streak at the feet (FxParticles) and an "Overclocked" tag on the HUD (5 px from the edge).
- **Bots** need nothing new: they get it when their path follows a bus. A lower navmesh area cost on the buses (modifier volumes) so
  they prefer them going out is an option for 30.9, not a must.

Not in this ticket (ideas for later, only with the user's yes): wind from the cooler's fans, a power surge that hurts.

## New models list with budgets

All in `content/models/computer/`, materials in `materials/computer/` (ground in `materials/ground/`), `Detail detail=0.5` on big
simple forms, no noise on big faces, 256 cells and 3 LODs. LOD0 triangles:

| Model | Size (m) | Collider | LOD0 tris | Materials |
|---|---|---|---|---|
| `smd-resistor0`, `smd-resistor1` | 0.5 × 0.31 × 0.13 | none | 200 each | 1 (black epoxy; tin ends by colour data) |
| `smd-cap0` | 0.5 × 0.31 × 0.31 | none | 200 | 1 |
| `solder-blob0`, `solder-blob1` | 0.2-0.4 across | none | 200 each | 1 |
| `via-pad0` | 1.2 × 1.2 × 0.04 | none | 300 | 1 |
| `dust-bunny0`, `dust-bunny1` | 0.4-0.6 | none | 500 each | 1 |
| `zip-tie-tail0` | 3-5 × 0.63 × 0.25 | none | 400 | 1 |
| `thermal-paste-dot0` | 0.5-1 × 0.15 | none | 300 | 1 |
| `tantalum-cap0` | 1.8 × 1.1 × 0.72 | convex | 2k | 2 |
| `smd-inductor0` | 1.5 × 1.5 × 0.75 | convex | 2k | 1 |
| `crystal-smd0` | 2.85 × 1.2 × 1.05 | convex | 2.5k | 2 |
| `to220-lying0` | 4 × 2.5 × 1.1 | convex | 3k | 2 |
| `screw-mount0` | 1.5 × 1.5 × 0.63 | convex | 2k | 2 |
| `smd-can-cap0` | 1 × 1 × 1.1 | convex | 2.5k | 2 |
| `cap-electrolytic0..2` | 2 × 3, 2.5 × 4, 2.5 × 5 | convex | 4-5k each | 3 (sleeve, aluminium top, rubber) |
| `cap-big0` | 4 × 6.25 | convex | 6k | 3 |
| `choke0` | 3.25 × 3.25 × 2.5 | convex | 4k | 2 |
| `pin-header0` | 3.2 × 1.3 × 2.1 | convex | 5k | 3 |
| `heatsink-pin0` | 3.5 × 3.5 × 2.5 | convex | 6k | 2 |
| `sata-ports0` | 3.75 × 2.5 × 3.75 | convex | 5k | 2 |
| `pcie-x1-slot0` | 6.25 × 1.9 × 2.75 | convex (mesh by size) | 4k | 2 |
| `pcie-x16-slot0` | 22.25 × 1.9 × 2.75 | mesh | 8k | 2 |
| `ribbon-bridge0` | 16 × 5 × 4 | mesh | 8k | 1 |
| `vrm-heatsink0` | 15 × 5 × 7.5 | convex | 12k | 2 |
| `chipset-heatsink0` | 10 × 10 × 6.25 | convex | 12k | 3 |
| `coin-battery0` | 6 × 2.5 × 5.5 | convex | 8k | 2 |
| `power-connector0` | 5.3 × 2.3 × 64 | mesh to 7.5 m | 18k | 3 |
| `case-fan0` (frame, `Blades` part) | 30 × 30 × 6.25 | none | 15k | 3 (one emissive ring) |
| `cpu-cooler0` (`FanWest`, `FanEast` parts) | 31 × 25 × 39 | mesh | 25k | 4 (copper, aluminium fins, fan frame, blades) |
| `gpu0` (`Fan0..2` parts) | 67 × 10 × 28 | mesh | 25k | 4 |
| `ram-bank-red`, `ram-bank-blue` | 36 × 4.6 × 11 | mesh | 40k each | 4 (slot, PCB edge, heatspreader, emissive team light bar) |
| `cap-turret-<team>-foot/head/barrel` | 0.8 × 2 | as 24's turret | 4k | team |
| `chip-tank-<team>-body/turret/barrel` | 0.8 × 0.5 × 0.45 | as 24's tank | 3k | team |
| `fan-copter-<team>-body/rotor/tail-rotor` | 0.8 | as 24's helicopter | 3k | team |

Reused (on theme or theme-free): the pods, health cross, flag stand and flags (`pickups/`), the weapons, the scouts. Nothing from
`games/`, `fungi/`, `trees/` or `home/`.

Ground materials: `ground_pcb`, `ground_trace` (emissive gold), `ground_silk`, `ground_pad`.

## Performance

Targets: High about 2M triangles; Integrated GPU / Web 500k and at most 200 draws from anywhere. Expected busiest view (west spawn
looking east): own RAM banks 80k, cooler 25k, GPU 25k, slots and bridges 30k, the far base at LOD1-2 20k, clutter about 80k
(main layer 0.15/m² at 200-300 triangles in three rings), components and SMD about 120k: about 400k on High, under 300k on Web.
Draws: 9 + 6 + 9 scattered variants across three groups (about 75 with outlines and shadows) plus the placed models and the
pulse overlay. Measured by `LevelSnapshot` (30.10) and listed here next to the meadow's.

Emission costs one multiply-add in the toon shader, the pulses one transparent draw over thin strips (little overdraw).

## Bots and the navmesh

- Everything walkable is the board, the bridges (top 4 m) and the parts' low tops: all inside the height window (spawns at 0, so up
  to 7.5 m). The cooler's fin stack, the RAM tops, the GPU and the fans are above it and never walked.
- **Pinch points**: RAM banks flush with the back wall (shut); the two DIMMs in a bank 0.5 m apart, closed at the ends by the
  latches (shut); the courtyard gates 3.6 m (open); bus to bridge foot 2 m (open); the cooler's heatpipes one closed trunk (no gaps);
  pin headers' pins 0.47 m apart and a convex hull anyway (shut). No gap 0.6-1.5 m wide or 0.6-1.75 m high anywhere: the bridges and
  the GPU are closed down to the board.
- Bridges: ramps at 38° (under the 45° agent slope); Check Navmesh must show them joined at both feet.
- Tops between 0.4 and 1.1 m (tantalum, inductor, crystal, TO-220, screw) are rounded or sloped where the part allows; a few
  unreachable spots are fine.
- Tank navmesh: the machine zone (−72, −34) joins the field along the south bank; tanks (step 0.15 m) go round all knee cover.

## Gameplay summary

Weapon mix: minigun and poison-gun at the bases, electro-gun north and flamethrower south in the middle. Three lanes split by PCIe
slots with cable bridges over them; data pulses on the buses to rush the middle; a long open north lane under the GPU and a tight
south lane among the chipset towers.

## Risks

- **No emission yet**: the toon shader and the material format have none, and every vertex stream (uv0-uv7) is already used
  (uv5 is a float2). 30.1 must find room for one more scalar (widen uv5, or pack it) and do the same for the ground regions.
- **Room walls**: `room` draws the checker only; the case look (steel, vents, LED strip, smoked glass) is new (30.2).
- **Visual noise**: many small shiny parts, glowing lines and a patterned board. Keep the board's hatch faint, clutter sparse and
  off the buses, metals with one hard highlight, no text, at most a tenth of the screen glowing. Check every snapshot view for it.
- **Thin traces in the ground**: 0.8 m wide regions must stay straight and unbroken at eye height with `wobble=0`; if the ground's
  resolution cannot, widen them to 1.0 m, or draw the traces with the pulse overlay mesh instead.
- **Machines in a room**: helicopters under a 10 m ceiling among taller objects (keep-outs, 30.7), and a per-level machine look.
- **Big single models**: the cooler, GPU and RAM banks at 256 cells may come out over their budgets; fins, grilles and fan blades
  must be few big forms with the detail in the shader.
- **Draw calls on the web**: 24 scattered variants is more than the fungal level's; if the Web view is over 200 draws, drop variants
  (one resistor, one dust bunny) before rings.
- **Bloom**: no HDR on the web preset, so no bloom there; if the glow looks flat on High, a bloom pass is a separate decision.

## Subtickets

One per agent; each merged before the next that needs it starts.

| # | What | Needs |
|---|---|---|
| 30.0 | The user's yes on theme, scale (250×), the RAM bases, the machines' eyes and the data pulses | — |
| 30.1 | Emission: `emission` in `.sdfmat`, the vertex data, `SdfToon` and `SdfToonFar` (the outline unchanged), ground regions; WebGL2 build check | 30.0 |
| 30.2 | Room look `look=case`: steel walls, vents, LED strip, smoked glass ceiling; the chessboard unchanged | 30.1 |
| 30.3 | Ground clutter models and the ground materials | 30.1 |
| 30.4 | Knee cover and tall blocker models | 30.0 |
| 30.5 | Landmarks: cooler, GPU, battery, VRM, chipset, PCIe slots, ribbon bridge, power connector, case fan; spinning parts | 30.1 |
| 30.6 | Base: `ram-bank-red`, `ram-bank-blue` | 30.1 |
| 30.7 | Machines: cap turret, chip tank, fan-copter per colour; `machines look=`; helicopter keep-outs | 30.0 |
| 30.8 | Layout: `computer.level` (stubs for missing models), regions and buses, sides, scatter, draws; `BattleSidesTests` level and weapons, `ContentComputer_HasOnlyComputerParts`, bounds tests; bake | 30.3-30.6 (stubs allowed) |
| 30.9 | Data pulses: format, bake, overlay mesh and `SdfPulse`, `PulseRules` and the boost, FX and HUD tag, EditMode tests | 30.8 |
| 30.10 | Unity rebuild, Check Navmesh, snapshots and budgets on both presets, a full match with 7 bots, web frame time; re-mesh and rebake in the main checkout | all |

## Acceptance

- `content/levels/computer.level` bakes; `dotnet build` and `dotnet test` pass, with the level in `BattleSidesTests` (mirrored, in the
  play area, clear of placed models) and `ContentComputer_HasOnlyComputerParts` (no model or material from another theme's folder).
- Every new model has its `.png` render, its bounds test and its LOD0 within the table above (±20%).
- `Computer.unity` builds with no `error CS`, shader error or exception; Check Navmesh finds a path from each spawn to the enemy stand
  and every pod, over both bridges, with few unreachable spots; the tank navmesh joins each machine zone to the field.
- Snapshots from every view (spawn four ways, corner, above, the cooler, the low preset) read as a toon motherboard, not noise; no
  red or blue outside the teams; triangles and draws per group listed here on both presets, the Web view within 500k and 200 draws.
- In Play mode, a full match with 7 bots: spawn, take each pod, cross each lane and both bridges, ride a pulse and see the boost and
  the HUD tag, carry a flag home; bots do not stick; helicopters stay out of the tall models; frame time on the Web preset in budget.
- 33 can reuse the models: none depends on this layout.

## Status

Not started.
