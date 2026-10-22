# 33 — Final boss: Chanterelle Prime

The last battle of the campaign (32, step 8). **Chanterelle Prime**, last year's blue runner-up who rigged the whole tournament, fights
Mushroom 42 in person, at giant size, in the tournament's own stadium: the **Champion's Arena**. Three phases, each built from the
systems the battles already have: capture the flag (21) drops Prime's shield, the machines (24) give Prime a helicopter and its
adds, the weapons (22) and spores (25) are 42's tools, the bots (23) are both teams' crews. Winning hands over to 32's ending, where
42 is back at their desk.

Depends on: 20-25 (match, flags, weapons, bots, machines, spores), 26 (level ruleset: this ticket fills in its template, with the
exceptions named under "Footprint"), 31 (the `Cutscene` sequencer and caption box; the `chanterelle-prime` model is made here and
shared with 31), 32 (the campaign: `BattleSetup`, difficulty, Victory and Defeat, RETRY EASIER, the cast names, beat B7 and the
ending). Story names and lines follow 31 and 32: 42 is always **they/them**; Prime is **he/him** (31). Note (2026-10-22): 31 is now
pre-rendered comic panels and no longer provides the `Cutscene` sequencer or caption box; the sequencer and the victory beat need
a new home (32 or here). 31 makes `prime-crown`, a gold crown slightly too small, which the rig below uses.

## Decisions to confirm with the user

The ticket assumes these; each is one line to change.

1. **The arena** is the Champion's Arena, a fungal stadium (below), not the computer's core.
2. **Prime's look**: the chanterelle scout at 4.6 times, a crown slightly too small, a silver "2nd" medal, a short royal cape.
3. **Three phases**: shield and flag; helicopter; on foot with the crown as the flag and 42's minigun back.
4. **Lives**: 42 has 3 lives a phase at Normal (respawns as usual); losing the last is a defeat. Red bots respawn without limit.
5. **No player control is taken** in the fight except the three short transitions (at most 6 s each, skippable).

## 1. The arena: theme and scale

**Theme: the Champion's Arena**, the minigun-mushroom tournament's final stadium, where Prime lost last year's final by one capture.
Prime has rebuilt it on the server: a bowl of fungal stone round a sand floor, stands packed with cheering crowd mushrooms, four
giant glowing floodlight mushrooms at the corners, and Prime's **Royal Box** at the east end. Everything is fungal: fungal stone,
shelf fungus, mycelium, moss, spore-sand, mushrooms. The same theme as the fungal level (26: "fungi and what fungi live on"), dressed
as a stadium.

Why this and not the computer's core:

- **The story closes**: the tournament ends where last year's final ended, in front of a crowd. B7 "Second place" (32) is Prime
  standing in the arena that cost Prime the title. The computer was the level before (30), so a CPU die would repeat it.
- **The machines are already on theme**: 24's tank, helicopter and turret are mushroom forms. A fungal arena reuses them as they
  are (blue only), and the Prime-copter is 24's helicopter design scaled up. A CPU theme would need a new set of machines.
- **Open sky**: a helicopter phase needs room above (6-18 m); an outdoor bowl has it, a room needs a flight ceiling (26, Risks).
- **Readability**: a giant blue boss on pale sand under a dusk sky is the clearest silhouette the game can give, from any spot.
- **Cost**: the fungal level's foliage, rocks and stumps are reused for the fill; the new models are the arena pieces and Prime.

**Scale: life size**, as the outdoor levels: the scout's eye at 1.5 m, its capsule 1.75 m. Prime is a 6.5 m giant inside it (a
"scale hack", which the victory beat undoes). Written in the level file's header.

**Palette**, kept clear of team red and blue: pale spore-sand (warm cream), grey-green fungal stone with lichen, ochre shelf fungus,
moss green, the floodlights' warm gold. The crowd mixes yellow, orange, cream, purple and green caps. The only blues are Prime, the
Royal Box's banner and the blue crew; the only reds are 42's team and the Champion's Corner (team dressing of a base, 26 §1).

## 2. Footprint, boundary and light

| | Champion's Arena |
|---|---|
| Play area | `play size=(120, 80)`: x ±60, z ±40; smaller than the 150 × 100 battles, as a boss fight wants Prime in view |
| Floor | ground heightfield, `noise=0.05` (raked sand, nearly flat, so shockwaves read and bots never snag) |
| Boundary | the stands: `hills ring width=24 height=12` with a new `terrace=1.5` option (steps 1.5 m deep and about 0.75 m high, see 33.3; fallback: plain slope with tier lines in the `stand-stone` material); `noise amplitude=0.4` only; island `edge margin=6 rim=3 shoulder=3`, `island-underside` behind the top tier. In front of the stands an **arena wall**, 2.5 m of fungal stone round the floor's edge |
| Beyond | only sky; the play area's 20 m invisible walls (20) keep everyone on the floor |
| Light | `sky preset=dusk` (`content/skies/dusk.sky`): a low gold sun **from the west**, behind the red side, so Prime is lit from the front and nobody looks into the sun; if the preset's sun is not in the west, the sky file gets a `sunYaw` (33.3). The four floodlights are emissive caps only (no real lights: the toon look has one directional) |

**Exceptions to 26** (each needs a small change, 33.3):

- **Asymmetric layout**: west is 42's side, east is Prime's Royal Box. A `layout boss` line in the level file turns off
  `BattleSidesTests`' mirror check for this level; every other check (in the play area, clearances, pinch points, level spawn
  ground) stays.
- **Phase tags on `place` lines**: `phases=1-2` or `phases=3` shows a placed model only in those phases (the balcony, the centre
  pillar, the toppled pillars, the wreck). The bake writes the tag; the builder puts each tagged model under a `Phase <n>` group that
  `BossArena` switches. Colliders go with it; the navmesh is baked once per phase layout (three surfaces per agent, switched with the
  phase).

**Regions** (four, `wobble=0` for the painted lines):

| Region | Shape | Material |
|---|---|---|
| base | the floor | `arena-sand`: pale spore-sand with rake lines in the shader |
| `track` | `RoundedBox2D size=(112, 72) radius=16` | `arena-track`: darker packed sand, the running track along the wall |
| `field` | `RoundedBox2D size=(100, 60) radius=12` | `arena-sand` again, so the track is a 6 m ring |
| `centre-circle` | `Circle radius=9` at (6, 0) | `arena-emblem`: a mycelium ring and a cap emblem in moss green |

The Champion's Corner and the Royal Box carry their own paving in their models; no path or trail (the arena is one open floor).

## 3. Layout

Coordinates (x, z); red west, blue east. Flag to flag 90 m (red stand (−54, 0) to blue stand (36, 0)).

```
   z +40  ----------------------------------- north stands ---------------------------------
         |  [F]                     shelter                                        [F]     |
         |      H(-22,32)          (-14,12)       E electro(0,26)    shelter          bS   |
         |  H(-56,22)    pillar(-28,10)                 pillar(20,10)  (14,20)  T(48,20) |
   z  0  | RF(-54,0) M(-50,10)   ...barriers...  C(6,0) centre pillar   T(32,9) BF(36,0) [ROYAL BOX] |
         |  RS(-50,-18)  pillar(-28,-10)                pillar(20,-10) (14,-20) T(48,-20)|
         |      H(-22,-32)         (-14,-12)    Fl flamethrower(0,-22)  shelter     MZ   |
         |  [F]   P pistol(-40,-8)  shelter                                         [F]    |
   z -40  ----------------------------------- south stands ---------------------------------
          x -60                                  x 0                                  x +60
```

### Red side (west): the Champion's Corner

| Object | Where | Notes |
|---|---|---|
| Base | `champions-corner` at (−54, 0), yaw 90: a low red-dressed podium plateau of fungal stone, 14 × 20 m, 0.3 m high with sloped edges, the red flag stand on it | no building; 26's "back rank" case with a podium |
| Red flag stand | (−54, 0) | the capture point in phases 1 and 3 |
| Spawn | (−50, −18), radius 3, yaw 90 | 18.4 m from the red stand; on level ground beside the podium |
| Champion's plinth | (−50, 10): `champion-plinth` with a minigun pod on it | **dark until phase 3**; then 42's own golden minigun appears on it (§5) |
| Weapon pod | pistol at (−40, −8) | dual pistols from the start of the fight |
| Health pods | (−56, 22), (−22, 32), (−22, −32) | the near one behind the podium, two on the flanks a little short of halfway |

### Blue side (east): the Royal Box

| Object | Where | Notes |
|---|---|---|
| Royal Box | `royal-box` at (50, 0), yaw −90: a dais 16 m deep (x 42-58) and 28 m wide (z ±14), its top 4 m up, side stairs at z ±14 down to the floor, a 12 m back wall with Prime's banner, and a **balcony** reaching 8 m forward (x 34-42) at 3.5 m over the blue flag stand | landmark; mesh collider. Prime stands on the dais at (50, 0) in phase 1 |
| Blue flag stand | (36, 0), under the balcony | the balcony hides it from Prime's eyes (a dead zone at Prime's feet), not from the turrets |
| Spore shield | a dome of radius 7 m over (50, 0) on the dais | phase 1 only; stops every shot and lob |
| Blue spawn | (52, 28), radius 3, yaw −150 | for the blue crew; beside the box's north stair |
| Turrets | (32, 9) yaw −60, (32, −9) yaw −120, (48, 20) yaw −30, (48, −20) yaw −150 | 24's blue turrets; the two front ones cover the blue stand |
| Machine zone | (52, −30), radius 4, yaw −90 | where phase 2's dropped tanks respawn |
| Pods | none | the blue crew uses the neutral pods |

### Neutral pods

- **Electro-gun** (long range) at (0, 26): 60 m, and its bolt **jumps between the Prime-copter's spore sacs** (they are registered
  as enemies with `Combat.Register`), so it is the phase 2 tool.
- **Flamethrower** (short range) at (0, −22): for the crew round the blue stand in phase 1 and Prime's stem when the cap is stuck in
  phase 3 (8 m: brave, rewarded).
- The scene's four kinds (22's rule): pistol, minigun (phase 3 only), electro-gun, flamethrower.

### Cover and Prime's line of sight

Prime's eyes are about 9.3 m up on the dais in phase 1 (4 m dais + 5.3 m), 8-14 m in phase 2, 5.3 m in phase 3. A wall of height
h at distance D from the eyes (height e) hides a standing scout up to s = D (h − 1.75) / (e − h) behind it.

- **Fungal stone pillars** (`arena-pillar`, 6 m, 2.2 m round, mesh collider), 5 of them: (−28, ±10), (20, ±10) and the centre
  pillar (6, 0). From the dais a pillar at (20, ±10), 30 m from Prime, hides a standing scout up to 38 m behind it: the run to the
  blue flag goes from shadow to shadow. The centre pillar breaks the flag-to-flag line and is crushed by the crash in phase 3.
- **Toadstool shelters** (`shelter-toadstool`, 5.5 m high, caps 8 m across, capsule stem and mesh cap), 4: (−14, ±12), (14, ±20).
  **Overhead cover** against the Prime-copter's cannon and the downdraft dive; their caps are under the copter's 9 m minimum height.
- **Knee cover**: 14 `arena-barrier` crescents (0.9 m high, 4 m long, convex) hand-placed in pairs between the red podium and the
  centre, and in the north and south lanes; plus the fungal level's rocks, stumps and puffballs as a `trees group=rocks` section.
  0.9 m: players jump them, bots go round (26 §4). The shockwave in phase 3 (0.6 m high) is **not** stopped by them: jumping is the
  answer, so cover never makes the slam unreadable.
- **Lanes**: north (z 20-36, the electro-gun and the flank health pod), centre (past the pillars and the centre circle), south (z
  −20 to −36, the flamethrower). The pillars and shelters split them; the open stretch over the centre circle is the long sight line
  (about 60 m, x −30 to 30) for the electro-gun at the copter.

## 4. Fill layers (26 §4)

| Layer | What | Section and numbers | Collider |
|---|---|---|---|
| Ground clutter | the fungal level's `moss-turf0-4` and `fungi-tuft0-4`, only along the arena wall and round the barriers: a tidy stadium floor | `scatter` moss `density=0.12 cluster=3 clusterScale=10 spacing=0.6 avoid=centre-circle margin=0.8`; tufts `0.03 cluster=4 clusterScale=8`; `band=(-8, 0)` keeps both to the floor's edge | none |
| Knee cover | `arena-barrier0-1` hand-placed (14); `rock0-4`, `stump-brackets0-1`, `puffball0` | `place` (one overlap group); `trees group=rocks band=(-52, -10) density=0.006 spacing=6 scale=(0.6, 1.0) margin=3` | convex |
| Tall blockers | `arena-pillar0-1`, `shelter-toadstool0-1` | `place`, 9 in all (above) | mesh / capsule + mesh |
| Landmarks | Royal Box (east), Champion's Corner (west), 4 `floodlight-mushroom` (14 m) at (±54, ±34) behind the wall's corners, the copter wreck (phase 3) | `place` | mesh |
| Border | the terraced stands with the **crowd**: 4 `crowd-mushroom` variants on the stands; the `arena-wall` segments | crowd `trees group=crowd band=(2, 20) density=0.22 spacing=1.3 scale=(0.9, 1.15) maxSlope=45` (about 700); wall: 50 `arena-wall` segments of 8 m, `place`d round the floor's edge by a one-off script into one commented group | none (crowd); convex (wall) |

- **The crowd cheers**: their draw group sways with the instanced renderer's wind (`wind=0.06 windHeight=1.2`), and a new global
  `_CrowdCheer` uniform (0-1, 33.6) multiplies it and adds a hop: high on a capture, a stun, a phase break, the victory; a low murmur
  otherwise. No per-instance work on the CPU.
- The east stand's crowd is Prime's fans: the `crowd-mushroom-blue-fan` variant (cream caps with a blue scarf band) in a block of
  about 60 behind the Royal Box, team dressing of the blue base (26 §1). The rest mix the palette's colours.

## 5. Chanterelle Prime

### The look

The chanterelle scout (`content/parts/chanterelle_scout.sdf`) at **4.6 times**: **6.5 m** to the cap's top, a cap 3.7 m across,
the same wavy funnel and ridges in the same blue. Marked as the champion who wasn't:

- a gold **crown**, slightly too small, perched on the cap's dip (1.2 m across, 0.8 m high, five points with blue gems);
- a silver **"2nd" medal** on a blue ribbon, floating on the stem's front at chest height (0.8 m across, it swings);
- a short royal blue **cape** with a cream fur trim, hanging from the cap's back rim (3 m long);
- the scouts' floating **eyes** (0.55 m each, 4.6 times) and **hands** (0.9 m, chanterelle flesh), as every scout.

### Parts (no skeleton, like the scouts and machines)

One model per moving piece, origin at its pivot (24.1's way), in `content/models/characters/prime/`, meshed at **scout scale**
(1.41 m tall) and scaled 4.6 in the boss scene, so 31's beats use the same prefab at 1:1. A `PrimeRig` (data only, like
`MachineRig`) on the prefab points at the pieces.

| Piece | Pivot | Moves |
|---|---|---|
| `prime-stem` | the base's centre | the body: squash and stretch, lean, the walk's bob; carries the medal |
| `prime-cap` | the stem's top | tilts forward 35° to kneel (exposing the gills, the weak point), lifts 0.5 m for the slam's wind-up |
| `prime-crown` | the crown's base, on the cap | detaches in phase 3: thrown, then a capturable object; spins up |
| `prime-cape` | the cap's back rim | swings with the motion (a damped spring on two angles, by script) |
| `prime-medal` | the ribbon's top | swings |
| eyes ×2 | the scouts' `eye` part, chanterelle setup | look, blink (scale y), glow for telegraphs (material colour), close to block weak-point hits |
| hands ×2 | the scouts' `hand` part, chanterelle flesh | point, slam, grab, hold the copter's skids |

### Weak points (one a phase, so the player always knows what to shoot)

| Phase | Weak point | Damage taken | When open |
|---|---|---|---|
| 1 | the **gills** under the cap | ×2 (rest of the body ×1) | only while **stunned** after a capture; the shield blocks everything otherwise |
| 2 | the copter's 4 **spore sacs**, then the gills | sacs take their own health; gills ×2 | sacs always; gills while dazed after the crash |
| 3 | the **eyes** | ×2; ×1 body | eyes open between attacks, shut (×0) during a telegraph |

Weak-point hits show the hit marker in gold and a bigger "pop" number; a hit on the shield gives a dull spark and no marker.

### Telegraphs (readable at the web preset)

Every attack has a **wind-up** with three cues at once: a **body pose** (seen from any angle), an **eye glow** (colour per attack:
white-blue for the glare, gold for the slam, green for spores), and a **ground marker** where it lands (a flat ring or circle on
the floor, drawn on top of clutter, in a hard toon band, pulsing faster as it nears). Plus an audio cue (described in §9). On Easy
the wind-ups are 30% longer, on Hard 20% shorter (§7).

## 6. The fight

Prime's health: **20300 at Normal**, in three bars: **1800 / 4500 / 14000** (33.9 tuned these from the first guesses 5000 = 1500 / 1500 / 2000,
which a balance simulation showed to be a 3.5 minute fight; the weapons do far more per second than the first numbers assumed). The HUD bar shows three segments; a bar cannot be
damaged past its end (overflow is lost), so every phase plays out. All damage below is at Normal, against 42's 100 health (200 with
overheal); compare 22's pistol 12 a hit, the minigun's 7, a machine's 6, the flamethrower's 70/s.

Before phase 1: B7 "Second place" (32), in this scene, ends at 42's eyes at the red spawn; then Prime's **name card** (§9) and
the fight starts.

### Phase 1 — "The Royal Box" (bar 1: 1500)

Prime stands on the dais behind the **spore shield**. The match is capture the flag (21) as usual: the blue flag on its stand under
the balcony, the red one on the Champion's Corner.

| Attack | Wind-up (Normal) | Effect | Every |
|---|---|---|---|
| **Spore Mortar** | 1.2 s: Prime's hands scoop, green eye glow; a 3 m ring on the floor under each target | 3 lobbed spore balls (22's poison ball, blue-green) at 42 and up to two red bots; 15 on landing within 3 m, then a cloud 8/s for 3 s (22's `Area`) | 8 s |
| **Royal Glare** | 1.0 s: eyes glow white-blue, a thin aim line from both eyes to the target | a beam 3 s long that follows the target at 25 °/s (sidestep to outrun it); 30/s while on you; stopped by pillars, shelters and walls (ray) | 18 s, only on a target in sight |
| **Crew** | Prime points; caption "Blue team, on the field!" | the 4 blue scouts (32's names: Runner-Up, Honourable Mention, Participant, Also-Ran), respawning in 8 s; the 4 blue turrets | from the start |

**What the player must do**: take the blue flag and capture it at the red stand while the red flag is home (21's rule, unchanged).
Each capture: the shield shatters, Prime stumbles and **kneels** for **12 s** (cap tilted forward, gills to the field, eyes dizzy),
the crowd roars. Damage counts only then: gills ×2. Then the shield regrows (1 s, a rising dome) and the blue flag is back on its
stand. Expected: three captures at Normal (33.9: 42 captures at the red stand, 96 m from the dais, so the pistols' falloff and spread
leave about 670 per 12 s window, not the 900-1200 first guessed; 1440 is the figure at point-blank).

- The blue crew plays CTF too: a **blue capture** heals Prime 250 (at most to the bar's top) with a taunt ("Points for the home
  team!").
- Prime's attacks never target a player standing under the balcony (Prime can't see there); the turrets do.

**Transition 1 → 2** (6 s, the sequencer; invulnerable, the crew and turrets hold fire):

1. Prime, on the dais, crossly: "Fine! FINE. Ref, I'm calling a timeout."
2. Ref Puffball (caption from the sky): "Timeouts are not in the rulebook."
3. Prime: "The rulebook says *helicopter*." The **Prime-copter** rises from behind the Royal Box; Prime's hands grab its skids and
   Prime is lifted off the dais, dangling, cape flapping.

Arena change: the shield emitter is gone; the **balcony falls** into a ramp from the floor to the dais (phase-tagged models), which
makes the dais walkable high ground with a health pod on it (new, (50, 0), phases 2-3). The blue scouts drop to two.

### Phase 2 — "The Prime-copter" (bar 2: 1500)

The Prime-copter (§8) flies laps of the arena with Prime hanging under it by the hands: the copter's body 14-18 m up, Prime's feet
7-11 m up. Flown by 24's `HelicopterPilot` with boss parameters (height over the floor 14-18 m, never under 9 m over anything, 6 m/s,
inside the wall by 8 m).

| Attack | Wind-up | Effect | Every |
|---|---|---|---|
| **Cap Cannon** | 0.8 s: eyes flash red-white, the copter's muzzle glows and the copter turns its nose to you | 12 hitscan shots over 2 s, 8 each (a machine's 6 scaled), aim error 3° (23's `BotAim` at 0.5); shelters and pillars stop them | 7 s |
| **Downdraft Dive** | 1.5 s: a 6 m circle on the floor at the target, shadow growing; the copter tilts in | the copter dives to 6 m over the circle and Prime's hands slam it: 30 in the circle and a push of 6 m out; then it climbs back over 2 s | 12 s |
| **Tank Drop** | 1 s: a hatch under the copter opens, a blue light | drops a **blue tank** (24, 60 health) that lands with a puff and drives as 24's tanks do; at most 2 alive, respawning in the machine zone | 35 s |
| **Crew** | | 2 blue scouts, respawning in 8 s; the 4 turrets | |

**What the player must do**: burst the copter's **4 spore sacs** (glowing blue puffs hanging under its rim, 1 m round, **400 health
each**; their damage counts on the bar, 1600 in all). The electro-gun's bolt jumps between them (they hang about 2.5 m apart, inside
its 6 m jump). When the fourth bursts, the rotor sputters: the copter **crash-lands** at the centre circle (6, 0) (2 s wind-up: a
huge shadow there and a falling whistle; 50 within 8 m on impact) and Prime sits **dazed** on the wreck for **10 s**: gills ×2.
The remaining 2900 come from that window and the next ones. If the window ends with bar 2 left, two sacs regrow and the copter takes off again,
smoking.

**Transition 2 → 3** (6 s):

1. The copter gives up for good with a sad "pfft" on the centre circle, crushing the centre pillar; the **crown flies off** and lands
   on top of the wreck.
2. Prime climbs out: "My crown! Nobody touch the crown. That's an order."
3. Ref Puffball: "A loose crown is a capturable object. Rule four."
4. 42: "There's a rule four?"

Arena change: the **wreck** at the centre (a landmark with a mesh collider and a 2 m ramp of debris up to the crown), the two east
pillars (20, ±10) **topple** into knee-high cover pointing west (phase-tagged), the floodlights turn from gold to a hotter
orange-red, the crowd chants.

### Phase 3 — "The Crown" (bar 3: 2000)

Prime on foot on the arena floor, walking at 2 m/s on a **Boss** navmesh (33.3), towards the target it chose.

**The crown is the flag**: a blue `TeamFlag` (21) with the wreck's top as its stand. Taking it, carrying it (10% slower, as any flag)
and capturing it at the red stand is ordinary CTF. While Prime wears the crown, **Royal Immunity**: Prime takes **half damage**.
Once captured:

- the crown sits on the red stand (glow column in gold, 21's `FlagGlowColumn`);
- Prime takes full damage;
- **42's own golden minigun** appears on the Champion's plinth (−50, 10): "the champion's minigun", 22's minigun numbers with **×1.5
  damage against Prime** only (10.5 a bullet; a gold skin of the same weapon model, `minigun-gold`). Only 42 can take it. It is the
  scene's minigun kind (22's four-kind rule holds). 42 keeps it on death in this phase only (it goes back to the plinth and 42
  respawns with it in hand).
- The carrier dies: the crown drops (21). **Prime touching it** returns it to Prime's head (a flag's own team returning it, 21's
  rule): immunity back. Untouched, it goes home to the wreck after 30 s.

| Attack | Wind-up | Effect | Every |
|---|---|---|---|
| **Cap Slam** | 1.2 s: hands and cap rise, gold eye glow, a 14 m gold ring on the floor round Prime | a shockwave ring from Prime out to 20 m at 12 m/s, 0.6 m high: 35 and a push of 8 m unless you are **in the air** (jump: 22's 1.1 m clears it); then the cap is **stuck** in the floor 3 s: gills ×2 | 9 s, when a target is within 20 m |
| **Hand Grab** | 1.0 s: a hand flies over the target and hovers, a 1.5 m shadow under it | grabs whoever is in the shadow: 40, thrown 8 m. Each hand has **300 health**; bursting one stuns Prime 4 s (gills ×2) and it regrows in 20 s | 10 s, alternating hands |
| **Royal Glare** | as phase 1 | as phase 1 | 14 s, on a target farther than 20 m |
| **Crew** | | 2 blue scouts, respawning in 10 s; turrets (32, ±9) only (the far two were knocked out by the crash) | |

**Sore Loser** (bar 3 under 25%): wind-ups 20% shorter, two more blue scouts arrive with a caption ("Participation Award! Get in
here!"), the crowd boos Prime.

Prime's target choice (phase 3): the crown carrier first, then the player with the most damage dealt in the last 10 s, then the
nearest; Prime never targets a dead or spawn-protected player (20, 22).

### Expected length

| Phase | Normal | What sets it |
|---|---|---|
| 1 | 2-3 min | two flag runs of about 60 s each |
| 2 | 2-3 min | sacs at 60 m with the electro-gun, one crash window |
| 3 | 2-4 min | a crown run, then the minigun at about 105/s on the body, 210 on the eyes |
| **Fight** | **7-10 min** | plus 3 × 6 s of transitions; no clock (no time limit, no sudden death) |

## 7. The player's support

### Red team (32's cast)

**Mushroom 7** (eager), **Mushroom 13** ("Lucky", the worrier) and **Mushroom 99** (big, few words): three red bots at skill **0.6**
(32's computer level), respawning in 5 s with no limit. Their brains (23) get a boss bias per phase through `Temperament`:

- phase 1: one raider (the blue flag), one guardian (the red flag), one hunter (the crew);
- phase 2: all hunt the sacs and tanks, fetch the electro-gun, keep under shelters during a Cap Cannon wind-up;
- phase 3: one raider for the crown, two harass Prime from range; never stand in a slam ring (they read the same telegraph data).

Short lines in the caption box, at most one every 20 s: Mushroom 7 "The gills! Shoot the gills!"; Mushroom 13 "Is that copter
supposed to be that big?"; Mushroom 99 "Crown."

### Pods

Health pods: the three red ones, plus the dais pod from phase 2. On each phase start, the crowd throws **two health caps** onto the
floor at (−10, ±20) (temporary health pods, 40 as usual, gone after 30 s). Weapons: pistol pod, the two neutral pods, the champion's
minigun in phase 3.

### Spores (25)

On from the start (32: on from the fungal level). The meter is **full at every phase start** (a checkpoint gift). New gains (in
`SporeRules`): 5 per 100 damage to Prime, 15 a spore sac, 20 a hand, 25 a stun you started (a capture or the crash); the crew's kills
and the flag as in 25. Rush carries the flag or the crown; Armour halves Prime's attacks too; Regrowth outheals the mortar's cloud.

### Difficulty (32's Easy / Normal / Hard)

| | Easy | Normal | Hard |
|---|---|---|---|
| Prime's health | 20300 (1800 / 4500 / 14000, as Normal: its ease is in the rows below, 33.9) | 20300 (1800 / 4500 / 14000) | 25400 (2250 / 5650 / 17500) |
| Prime's damage | ×0.6 | ×1 | ×1.3 |
| Wind-ups | ×1.3 | ×1 | ×0.8 |
| Stun windows | ×1.25 | ×1 | ×0.85 |
| Blue crew skill | 0.55 | 0.7 | 0.85 |
| Blue crew size | phase 1: 3, then 2 | 4, 2, 2 | 4, 3, 3 |
| 42's lives a phase | 5 | 3 | 2 |

RETRY EASIER (32, after two losses in a row) applies Easy's damage and wind-up rows for that attempt, on top of the chosen
difficulty's health.

### Lives, checkpoints and retry

- 42 respawns as usual (5 s, the red spawn, 2 s protection), losing a **life** each death; the HUD shows them as cap pips. Falling
  off is impossible (walls), so every death is Prime's, the crew's or a machine's.
- **Checkpoint** at each phase start: Prime's bar full for that phase, the arena in that phase's layout, everyone respawned, flags
  home, the meter full, 42's lives full.
- Losing the last life is a **defeat** (§8). RETRY restarts from the current phase's checkpoint in place (no scene reload, 32's
  rule); RESTART FIGHT starts from phase 1 (no B7). The transitions are skipped on a retry.

### Medals (32 §6 leaves the boss's rules here)

- **Bronze**: a win.
- **Silver**: a win with 3 or fewer lives lost in all.
- **Gold**: a win with no life lost in phase 3 and 1 or fewer in all, within 10 min, at Normal or Hard, not with RETRY EASIER.

## 8. Victory and defeat

### Victory beat (in this scene, 32's `Cutscene`, about 20 s, skippable)

At bar 3's end: 32's end banner and slow-down (time 0.3 for 1 s), "PRIME DEFEATED!", then:

1. Prime sways, the cap wobbles, and Prime **shrinks** with a series of pops from 6.5 m to scout size (the scale hack undone), the
   cape left behind as a puddle of blue cloth.
2. Prime (tiny, indignant): "I demand a recount."
3. Ref Puffball (from the sky): "Recount complete. Still second."
4. Prime: "I'd like to speak to the tournament's manager."
5. Ref Puffball: "I *am* the manager."
6. The crown rolls to 42's feet; 42 picks it up, looks at it, and puts it on Mushroom 7's cap instead: "Hold this. I've got a desk
   to get back to." The crowd goes wild (`_CrowdCheer` at 1).
7. The arena floor starts breaking into blue pixel blocks from the edges (31's pixel spores), the sky flashes white.

Then 32's **VICTORY** screen (the medal, the time, lives lost, damage dealt). **CONTINUE** hands over to 32's ending: fade, Loading,
`Home.unity`, the ending cutscene (32.8), the credits. Nothing of the ending is in this ticket.

### Defeat

32's **DEFEAT** screen in blue: "DEFEATED", a Prime taunt (five: "Silver suits you."; "Have you considered second place? It's
lovely."; "That was my *warm-up* crown."; "The crowd is cheering. For me."; "Best of one, final answer."), the phase reached and
Prime's bar left, a tip for that phase (e.g. "Jump the shockwave."; "Hide under a toadstool when the eyes flash."; "The electro-gun
jumps between the sacs."). Buttons: **RETRY PHASE**, **RESTART FIGHT**, RETRY EASIER (after two losses), MENU.

## 9. Assets to make

Budgets per 26 §5 (LOD0; LOD1 about a quarter, LOD2 about a tenth), 256 cells, 3 LODs; colliders as 26's rules.

### Models

| Model | Folder | Size (m) | Collider | LOD0 tris | Materials |
|---|---|---|---|---|---|
| `prime-stem` | `characters/prime/` | 0.23 × 0.75 at scout scale (1.05 × 3.5 in the boss) | none (Prime's hit shape is a set of capsules and spheres by script) | 6k | `chanterelle_flesh` |
| `prime-cap` | `characters/prime/` | 0.8 across (3.7) | none | 10k (hero) | `chanterelle_cap`, `chanterelle_ridges` |
| `prime-crown` | `characters/prime/` | 0.26 × 0.17 (1.2 × 0.8) | none (a flag's trigger) | 3k | `prime_gold`, `prime_gem_blue` |
| `prime-cape` | `characters/prime/` | 0.6 × 0.65 (3 × 3) | none | 3k | `prime_cape`, `prime_fur` |
| `prime-medal` | `characters/prime/` | 0.17 (0.8) | none | 1.5k | `prime_silver`, `prime_ribbon` |
| eyes, hands | the scouts' `eye`, `hand` | | none | as the scouts (eye 4.3k) | as the scouts, chanterelle |
| `primecopter-body` | `machines/` | 5.0 × 4.7 × 5.2 (24's helicopter at 7) with a 4-sac rim and wide skids | convex | 10k | `machine_blue_*` |
| `primecopter-rotor` | `machines/` | 5 across | none | 2.5k | `machine_blue_*` |
| `primecopter-tail-rotor` | `machines/` | 1.2 | none | 1.5k | `machine_blue_*` |
| `primecopter-sac` | `machines/` | 1.0 round | sphere (by script) | 1k | `prime_sac` (glowing blue, subsurface) |
| `primecopter-wreck` | `arena/` | 7 × 3 × 6, crumpled cap rotor, debris ramp | mesh | 15k | `machine_blue_*`, `fungal_stone` |
| `royal-box` | `arena/` | 16 × 12 × 28 (dais 4 m, back wall 12 m) | mesh | 20k (landmark) | `fungal_stone`, `royal_banner`, `shelf_fungus` |
| `royal-box-balcony` / `-fallen` | `arena/` | 8 × 0.8 × 12 / a ramp 10 m long | mesh | 4k each | as the box |
| `spore-shield` | `effects/` | dome r 7 | none (shots stopped by a sphere test in code) | 1.5k | Unity shader (see effects) |
| `champions-corner` | `arena/` | 14 × 0.3 × 20 podium, red cap-flag dressing | mesh | 8k | `fungal_stone`, team red |
| `champion-plinth` | `arena/` | 1.2 × 1.2 × 1.2, a cap-shaped base like 31's trophy | convex | 3k | `fungal_stone`, `prime_gold` |
| `minigun-gold` | `weapons/` | 22's minigun | none | as the minigun | `prime_gold` |
| `arena-pillar0-1` | `arena/` | 2.2 round, 6 high | mesh | 5k | `fungal_stone`, lichen |
| `arena-pillar-toppled` | `arena/` | 6 long, 1.1 high | convex | 5k | as the pillar |
| `shelter-toadstool0-1` | `arena/` | 5.5 high, cap 8 across | capsule stem, mesh cap | 6k | `shelf_fungus`, moss |
| `arena-barrier0-1` | `arena/` | 4 × 0.9 × 1 crescent | convex | 3k | `fungal_stone` |
| `arena-wall` | `arena/` | 8 × 2.5 × 1 | convex | 2k | `fungal_stone` |
| `floodlight-mushroom` | `arena/` | 14 high, cap 6 across | capsule | 15k | `fungal_stone`, `floodlight_cap` (emissive gold, colour by script) |
| `crowd-mushroom0-3`, `-blue-fan` | `arena/crowd/` | 1.1-1.5 high, two coarse eyes (the `eye_machine` part at 0.6) | none | 1.2k | `crowd_cap_*`, `crowd_flesh` |

The `chanterelle-prime` prefab of 31 is `PrimeRig` with these pieces at 1:1 (31: "if 33 lands first, use theirs").

Bounds tests in `SDFPipeline.Tests/Models/PrimeTests.cs` and `ArenaTests.cs`: Prime assembled at its pivots is 1.41 m ± 5% at scout
scale with every piece touching its parent; the copter within 5.5 m with the rotor turned; the pillars 6 m; the barrier's top
0.8-1.0 m; the crowd under 1.6 m; the theme test `ContentArena_IsFungal` (no model from `home/`, `chess/`, `games/` or the meadow's
`trees/`).

### Effects (Unity, WebGL 2: vertex/fragment, 8's `FxBuffer` and `FxParticles`)

- **Ground markers**: flat rings and discs drawn into one `FxBuffer` (one draw for all), toon bands, pulsing faster towards the hit,
  colour per attack, drawn over clutter with a small depth offset.
- **Glare beam**: two `LightningGun`-style quad strips from the eyes, white-blue core, hard rim; the thin aim line in the wind-up.
- **Shockwave**: a torus mesh (built in code, 1k) scaled out at 12 m/s, toon gold, plus a dust ring of particles.
- **Spore mortar**: 22's poison ball and cloud, recoloured.
- **Shield**: the dome with a fresnel-rim toon shader, hexagonal spore cells by UV, a ripple at each blocked hit; shatters into
  60 shards (particles) on a capture.
- **Copter**: sac burst (a blue spore puff), smoke trail when hurt, the crash (dust, debris, a 1 s screen shake within 20 m).
- **Stun**: stars of spores circling Prime's cap; the gills' glow while open.
- **Crown**: gold glow column (21's column, gold) while it is loose or carried; a gold sparkle on pick-up.
- **Victory**: the shrink pops, the floor's pixel-block break-up (31's pixel spores).
- Particles at once: at most 600 in the fight (31's 400 in the intro); the crash and the shatter are the peaks.

### UI (runtime, built into the HUD like `FlagHud` and `SporeHud`; ~5 px from the edges)

- **Boss bar**: top centre under the clock's row (which shows no clock here: the score row becomes the lives row): "CHANTERELLE PRIME"
  in Lilita One, a bar in three segments (1800 / 4500 / 14000 wide in proportion), the current one bright, the spent ones grey, the
  coming ones dim; a white "chip" trailing behind damage for 0.5 s; a shield icon over the bar while the shield is up (no damage
  possible); a half-shield icon for Royal Immunity; a gold flash on weak-point damage; "STUNNED 12" counting down in the window.
- **Name card**: once, at the fight's start (2.5 s): a blue banner sliding in from the right with "CHANTERELLE PRIME", under it
  "Last year's runner-up. Rigged all of this." and a small silver "2nd" medal icon; the crowd roars.
- **Phase card**: at each phase start (1.5 s): "PHASE 2 — THE PRIME-COPTER" and the objective in one line.
- **Objective line** under the boss bar: "Capture Prime's flag to drop the shield" / "Burst the spore sacs (2 / 4)" / "Take the
  crown home" / "Shoot the eyes!".
- **Lives**: 42's cap pips on the clock's row (where the score pips were), red, a pip popping on a death.
- The flag HUD (21) stays in phases 1 and 3 (the crown shown as "the crown: on the wreck / carried by Mushroom 7 / dropped").
- Victory and Defeat are 32's screens with this ticket's texts.

### Audio cues (described only; no audio work here)

- Crowd: a stadium murmur loop, a roar on captures, stuns and phase breaks, boos in Sore Loser, a chant in phase 3.
- Prime: a smug three-note sting (31's) on the name card; babble per caption line, pitched low and big.
- Wind-ups: the mortar a wet scoop, the glare a rising electric hum, the slam a deep inhale and creak, the grab a whoosh, the cannon a
  charge whine, the dive a falling whistle, the tank drop a hatch clunk.
- Impacts: the slam a booming thud, the shockwave a rolling rumble, the shield's shatter glass-like and spore-soft.
- The copter: a giant rotor's lazy "whup whup", sputtering when the last sac bursts, a sad "pfft" on the final crash.
- Victory: the shrink as rising pops, a fanfare from the crowd; defeat: a sad trombone, softly.

## 10. Systems and code

### Rules in `SdfBattle.Core` (plain C#, EditMode tests in `SdfBattle.Tests`)

- `BossRules`: every number above (bars, multipliers, wind-ups, damage, cooldowns, stun windows, lives, crew sizes, sac and hand
  health, Sore Loser's threshold) per difficulty; serialized on the scene's `Boss` component like `MachineRules`.
- `BossPhase`: `Intro`, `Phase1`, `Transition12`, `Phase2`, `Transition23`, `Phase3`, `Victory`, `Defeat`.
- `BossState` (the state machine): the phase, the bars (`Hurt(damage, part)` with the part's multiplier, the shield, immunity, the
  overflow cut at a bar's end), `Vulnerable` windows (stun, daze, stuck cap), checkpoints (`Snapshot` / `RestoreCheckpoint`), lives
  (`ReportDeath(you)`), events `PhaseChanged`, `Stunned`, `ShieldChanged`, `ImmunityChanged`, `Healed`, `Defeated`, `Lost`. It
  listens to `CaptureTheFlagState.Captured` (phase 1: a red capture stuns, a blue capture heals) and to the crown's `TeamFlag` (phase
  3), and reports Prime's damage to `SporeState` (the new gain reasons).
- `BossAttack` and `BossAttackPlanner`: each attack is `Ready → WindUp → Active → Recover` with times from the rules; the planner picks
  the next by phase, cooldowns, the target's distance and sight (Glare only in sight, Slam only within 20 m), never two wind-ups at
  once, and `Targeting` (phase 3's order: carrier, recent damage, nearest; never dead or protected). Pure data: the Unity side asks
  "what now" and draws it.
- `BossParts`: the weak points and their health (sacs, hands), `IDamageable` data, regrowth timers.
- Reused unchanged: `MatchState` (players, deaths, respawns, protection, `CanHurt`), `CombatState.Hurt` (damage to players, 25's
  armour), `CaptureTheFlagState` and `TeamFlag` (the blue flag, the crown with Prime as its own team's returner), `MachineState`
  (tanks, turrets), `BotAim` (the cannon), `SpawnPicker`, `Ballistics` (the mortar).
- Small changes: `MatchRules` gains `Objective` (`Captures` as today, `Boss`): with `Boss`, captures do not win and the clock does not
  run; the match ends on `BossState.Defeated` or `Lost`. `SporeRules` gains the boss gains. 32's `BattleSetup` gains `Boss` with the
  difficulty and the start phase (for RETRY PHASE).
- **EditMode tests** (`BossStateTests`, `BossAttackPlannerTests`): the bars and their order; no damage through the shield; a red
  capture stuns for 12 s and gills take ×2; a blue capture heals 250, not over the bar; overflow lost at a bar's end; the four sacs
  then the crash then the daze, sacs regrowing when the window ends; the crown's immunity on and off with the flag, Prime's touch
  returning it; the champion's minigun ×1.5 against Prime only; eyes ×0 in a wind-up; slam damage only when grounded; lives per
  difficulty, the defeat on the last, a checkpoint restore per phase; difficulty scaling; Sore Loser at 25%; the planner's cooldowns,
  one wind-up at a time, sight and range rules, targeting order; the spore gains; no damage in a transition.

### Unity (`Assets/Battle/Runtime/Boss/`)

- `Boss` (on Battle, made by `Match.Start` when the scene has a `BossArena`): ticks `BossState` and the planner, drives Prime's body
  and the copter, registers Prime's hit shapes and the sacs and hands with `Combat.Register` (so the electro-gun's jumps and the
  flame's cone see them), deals Prime's damage through `Combat.Hurt(player, Damage.ByMachine("Chanterelle Prime", blue, attack,
  ...))` so the kill feed and death screen name Prime (24's path).
- `PrimeBody`: the acting with the pieces (pose tweens for each wind-up, blink, look, cape and medal springs, kneel, the slam, the walk
  bob), at 4.6 scale; the same component plays Prime in 31's and 32's beats at 1:1.
- `PrimeWalker`: phase 3 movement on the **Boss** navmesh (plain path queries like `BotNavigation`), 2 m/s, turning at 90 °/s.
- `PrimeCopter`: 24's `HelicopterPilot` with the boss's heights and speed; the dive; Prime hanging under it (the stem on a damped
  pendulum from the hands).
- `BossArena`: the phase groups (shown and hidden, colliders with them), the navmesh surfaces per phase layout, the floodlights'
  colour, `_CrowdCheer`, the crowd's health caps, the minigun plinth.
- `BossTelegraphs`: the ground markers, beams and rings from the planner's state.
- `BossHud`: §9's UI.
- `Bots`: the per-phase temperaments and "stay out of markers" (a bot that stands in an active marker steps out of it, using the
  marker's circle).
- Transitions and the victory beat: shot lists for 31's `Cutscene` sequencer, kept as text in `content/story/boss.txt` (31's rule).

### Pipeline

- Level file: `layout boss`, `place ... phases=`, `hills ... terrace=`, a `boss` section (`prime at=(50, 0) yaw=-90`, `crash
  at=(6, 0)`, `plinth at=(-50, 10)`, `shield at=(50, 0) radius=7`), baked into level.json; `LevelFileTests` for each (good and bad
  cases) and `BattleSidesTests` for the boss layout (all checks but the mirror, the pods and stands clear of every phase's models).
- `BattleNavmesh`: a **Boss** agent type (radius 1.8 m, 6.5 m high, step 0.5 m, slope 25°, ground only) on boss levels, and one
  bake per phase layout; Check Navmesh: from the red spawn to the blue stand, every pod and the wreck's top (the crown) in each
  phase; the Boss agent from the wreck to the red stand.
- `sky`: `sunYaw` on a sky preset if dusk's sun is not in the west.

## 11. Performance (Integrated GPU / Web: 500k triangles, 200 draws)

Measured in 33.9 (web preset, 256-cell meshes, 3 LODs) by `BossBudget` inside `BossPlayTest -webPreset`: every enabled renderer in 42's own
camera frustum at the red spawn looking east (the busiest view), LOD groups at the level they pick, a draw per material (outlines
included, static batching not credited), at moments of each phase with the real Prime, copter, machines, scouts and effects. The
instanced groups (crowd, clutter) do not draw in a batch run, so they are added from 33.3's `LevelSnapshot` of the same view (52.6k
triangles, 22 draws). The HUD canvases and screen-space effects are not counted.

| Moment (red spawn, looking east) | Scene renderers | + instanced (33.3) | Total triangles | Total draws |
|---|---|---|---|---|
| Phase 1, 14 s in: shield, 4 crew, 4 turrets, 3 red bots | 90.5k tri / 159 draws | 52.6k / 22 | about 143k | about 181 |
| Phase 2, 22 s in: copter flying, Prime hanging, tanks | 91.1k / 171 | 52.6k / 22 | about 144k | about 193 |
| Phase 2, the crash: copter down | 90.2k / 166 | 52.6k / 22 | about 143k | about 188 |
| Phase 3, 16 s in: Prime on foot, wreck, 2 crew | 100.8k / 174 | 52.6k / 22 | about 153k | about 196 |

By group in the busiest moment (phase 3): placed models 22.6k / 64, ground 9.8k / 31, scouts 22.2k / 34, Prime 3.1k / 8, machines 1.5k /
4, effects and other 41k / 33. Triangles are far under the 500k budget (about 30%); **draws are the tight one: 196 of 200 before the
HUD**, so a view with more in it (the blue stand looking west with the tanks) can pass 200 on the web preset. Cheapest cuts, if a
human run shows it: batch the scouts' parts (34 draws), drop the outline material on the crowd's far ring, or merge the 61 placed
models' outline materials. The first estimate (450k, 185 draws) was far too high on triangles and about right on draws.

- Prime is one object on screen; no instancing. Its hit shapes are 6 capsules and spheres tested in code, no mesh colliders.
- The planner ticks 10 times a second; the markers are one `FxBuffer` draw; the crowd's cheer is one shader uniform.
- No compute, no runtime lights beyond the sun, no real-time reflections: WebGL 2 safe.
- Frame time target: 60 fps on the M4 Max at the web preset with a 3 ms headroom at the crash (the peak of particles).

## 12. Risks

- **The terraced stands**: `hills` makes slopes, not steps. A `terrace` option is a small heightfield change, but the crowd's `trees`
  scatter must land on treads, not risers (snap to the tread's centre). Fallback: a plain slope with tier lines in the material.
- **Phase-tagged models and three navmesh layouts**: new to the level format and the builder; the bake, the scene and the navmesh
  data triple for the tagged parts. Keep the tags few (balcony, ramp, centre pillar, two toppled pillars, wreck, dais health pod).
- **Mirror tests**: the asymmetric layout needs `BattleSidesTests` to skip the mirror for `layout boss` only; easy to make too
  lenient. Keep every other check.
- **Prime at 4.6 times**: meshes made at scout scale show their facets 4.6 times bigger. The cap and eyes are the hero pieces: if
  they look coarse up close, mesh them at boss scale (more triangles) and use LOD0 only within 25 m.
- **Readability on the low preset**: FXAA at 0.75 scale blurs thin aim lines and rings; markers use thick hard bands and 0.3 m
  minimum line width; check at Ultra Low too.
- **The shockwave and the jump**: "in the air" must read the controller's grounded state at the ring's pass, not a height test,
  or a jump on a barrier fails. Test both.
- **The copter over cover**: 9 m minimum over anything keeps the dive from clipping the shelters (5.5 m); the dive's lowest point
  (6 m) is only over open floor (the dive circle is never placed within 6 m of a shelter or pillar).
- **Bots and telegraphs**: bots that ignore markers die often and the fight feels unfair to them; "stay out of markers" must be cheap
  (circle tests against the planner's list).
- **Balance**: the numbers are first guesses; the stun windows decide the length. Tune in play on all three difficulties.
- **Theme leaks**: the champion's minigun is a weapon (players' theme), fine; the Royal Box banner must stay a fungal banner (a shelf
  fungus with a woven-mycelium flag), not a human stadium's.

## Subtickets

In build order; one agent each (33.1, 33.2 and 33.4 can run in parallel).

### 33.1 — Prime's model

The five pieces in `content/models/characters/prime/`, materials in `materials/characters/` (`prime_*`), the `PrimeRig` prefab built
by the mushroom builder (like `MachineBuilder`), renders checked kneeling, slamming and with the crown off; bounds tests. Shared
with 31 (its `chanterelle-prime`).

### 33.2 — Arena models

`content/models/arena/`: Royal Box (and balcony, fallen), Champion's Corner, plinth, pillars (and toppled), shelters, barriers, wall,
floodlights, the crowd (5 variants), materials in `materials/arena/`; renders, budgets, bounds tests and `ContentArena_IsFungal`.

### 33.3 — Level file, pipeline and navmesh

`content/levels/arena.level` (every section commented, as the three levels are), the level format's `layout boss`, `phases=`,
`terrace=`, the `boss` section, `sunYaw` if needed; tests; the bake; `Arena.unity` built by `LevelBuilder`; the Boss agent and per-phase
navmesh; Check Navmesh; snapshots and the budget table of §11 filled in with measurements.

### 33.4 — Boss rules

`SdfBattle.Core`: `BossRules`, `BossState`, `BossAttack`, `BossAttackPlanner`, `BossParts`, the `MatchRules.Objective`, the spore
gains; the EditMode tests of §10.

### 33.5 — The Prime-copter

The copter models (body, rotor, tail rotor, sac, wreck) after 24.1's way, the prefab, `PrimeCopter` flight with the hanging Prime,
the dive, the tank drop, the crash.

### 33.6 — Boss runtime

`Boss`, `PrimeBody`, `PrimeWalker`, `BossArena`, `BossTelegraphs`, the effects of §9, `_CrowdCheer`, the bots' phase temperaments and
marker avoidance; the champion's minigun (`minigun-gold`, the plinth). Done when the three phases play through in Play mode with
placeholder transitions.

### 33.7 — Boss HUD and the end screens

`BossHud` (bar, name card, phase cards, objective, lives), the Victory and Defeat texts and RETRY PHASE / RESTART FIGHT in 32's
screens, the boss medals in 32's `Medals`.

### 33.8 — Transitions and the victory beat

The two transitions and the victory beat as sequencer shot lists in `content/story/boss.txt`; B7 (32) placed in this scene; the
hand-off to 32's ending; skipping; transitions skipped on a retry.

### 33.9 — Balance and web check

Play the fight on Easy, Normal and Hard; tune `BossRules`; a web build: frame time on the web preset, the peaks (crash, shatter),
telegraph readability at Ultra Low.

## Acceptance criteria

- `Arena.unity` is built from `arena.level` by the level builder; nothing hand-placed; one theme (fungal stadium) with the theme test
  passing; `dotnet test` passes with the new format features and the boss layout's checks.
- From 32's campaign, after the computer and B7: the name card, then phases 1-3 play as §6 says, with the transitions, at each of
  Easy, Normal and Hard, in 6-12 minutes for a player who knows the fight.
- Phase 1: no damage to Prime until a red capture; a capture stuns for 12 s with the gills at ×2; a blue capture heals 250.
- Phase 2: the four sacs, the crash, the daze; the electro-gun jumps between sacs; tanks drop and fight as 24's do.
- Phase 3: half damage while crowned; the crown is taken, dropped, returned by Prime's touch and captured by 21's rules; the
  champion's minigun appears on capture and does ×1.5 to Prime only; the slam is avoided by jumping.
- Every attack has a pose, an eye glow and a ground marker for its wind-up, readable on the Integrated GPU / Web preset.
- 42 loses a life per death; the last one opens Defeat with RETRY PHASE (from the phase's checkpoint, no reload) and RESTART FIGHT;
  32's RETRY EASIER after two losses.
- Winning plays the victory beat and opens 32's Victory, whose CONTINUE goes to 32's ending in `Home.unity`.
- Red bots (Mushroom 7, 13, 99) and the blue crew play each phase without getting stuck (Check Navmesh in each phase layout; a few
  fights spectated); bots step out of markers.
- The EditMode tests of §10 pass in the Test Runner; the battle tests of 20-25 still pass.
- Web preset: ≤ 500k triangles and ≤ 200 draws from every snapshot view in each phase; 60 fps on the M4 Max at the web preset.
- 42 is "they" everywhere; no text uses a pronoun for Prime until the user decides it.

## Open questions

1. **Arena**: the Champion's Arena (fungal stadium) as proposed, or the computer's core after all?
2. **Prime's look**: crown, medal and cape together, or fewer? Prime's pronouns (31, question 2)?
3. **Lives**: 3 a phase at Normal, or unlimited deaths with only a time limit?
4. **The champion's minigun**: a gold variant only in this fight, or does 42 keep a gold skin after the campaign (a reward)?
5. **Phase 2's grip**: Prime dangling under the copter by the hands (proposed, silly and readable), or riding inside a bigger copter?
6. **Replays**: does the boss appear in Level Select with its own medal, like the arenas (assumed yes)?

## Status

**Not started.**
