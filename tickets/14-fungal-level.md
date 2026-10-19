# 14 — Fungal level

A third level, `content/levels/fungal.level`, built into `Assets/Scenes/Fungal.unity`: the same floating island as the meadow
(150 m × 100 m playable, border hills, edge, underside), but **everything that grows there is a fungus**. Mushrooms instead of trees,
moss and small fungi instead of grass and flowers, a felted mat of mycelium instead of flagstones, and the fly agaric castle
(`castle0`) made fully fungal: no stone wall pattern, no tiled ground. Under the Eye-Moon Night sky, whose spores and floating caps
already belong to this world.

The meadow becomes the plain counterpart, with no fungi at all: ticket [15](15-meadow-without-fungi.md). The two tickets split the
current meadow's mix (a mushroom castle and giant mushrooms on a grass meadow with birches and pines) into two levels that are each
one thing.

Depends on: 01–04 (ground bake, regions, placements, scatter, trees, clusters, instanced renderer), 06 and 09 (castle parts, wall
patterns, floor heights), 07 (sky presets). Independent of 11–13. Runs **in parallel with 15**, except that 15's castle swap waits
for 14.2 (see 15).

## The rule: 300% fungal

Everything in the level is one of these:
- **a fungus**: mushrooms of every size, brackets and shelf fungi, puffballs, cup fungi, coral fungi, mycelium, spores;
- **what fungi live with or on**: moss, lichen, dead wood (stumps, fallen logs, a dead trunk covered in brackets), leaf litter and
  humus, rock (with lichen and moss, as the current rocks have);
- **the mushroom cast**: the characters and the castle they live in.

Nothing else: no living leafy trees, no flowers, no grass turfs, no flagstones or dressed stone. A test checks it (14.6).

**Keep every meadow asset.** No existing model, part or material is deleted or changed, except the castle's wall material (14.2).
New models go into new folders with new names; the grass, flowers, trees, clusters and their materials stay as they are and
remain used by the meadow and the chessboard's hills.

## Look

- **Palette.** The ground a dark, mossy olive-green turning teal in the shade (darker and bluer than the meadow's grass, so the
  two levels read apart at a glance). The castle and giant mushrooms keep their amanita red and cream and porcini browns. Mycelium a
  pale cream felt. One accent: a cold **cyan-green glow** (bioluminescent mycena, the gills' edges of a few mushroom trees, the
  mycelium threads near the castles). It is drawn as `game_window_glow` draws (high `subsurfaceRadius`), so under the night sky's
  `glowKeep=1` it keeps its brightness while the rest is dimmed. Glow is an accent: at most about a tenth of what is on screen
  glows at eye height.
- **Toon look only.** Every new model renders through `SdfToon.shader` and the ink outline like the existing ones; the same
  roughness, speckle and colour-variation ranges as the current mushroom materials (`amanita_*`, `game_porcini_*`), so the new
  pieces sit next to the castle and characters without a second style.
- **Shapes.** Round, soft and bulbous, like the castle and the characters: big smooth forms, no fine detail that only shimmers
  (`Detail detail=` at the root as the chess pieces and giant mushrooms have it).
- **Scale.** Big enough to read from 1.5 m eye height and to stand in for the meadow's things: mushroom trees 6–14 m, crest masses
  as big as the meadow's clusters, the foliage at the size of the grass turfs and flowers it replaces.

## Layout

Start `fungal.level` as a copy of `meadow.level` as it is at the start of this ticket, so the island, hills, edge, underside,
draw sections and budgets start from known numbers. Then change:

| Meadow | Fungal level |
|---|---|
| ground: grass (`ground_grass`) | ground: moss (`ground_moss`), with the new `base` line (14.1) |
| region `dirt`, trodden earth round each castle | region `litter`: dark humus and leaf litter round each castle, same circles |
| region `path`, gate to gate | region `trail`: a trodden track of pale spore dust, same polyline |
| region `tiles` (flagstones, castles' footprints, gate aprons) | region `mycelium`: a felted cream mat, the castles' footprints (`ground=mycelium groundMargin=2.5`) and gate aprons, **plain**, no stones |
| `castle0` on tiles | `castle0` on mycelium, same places, yaw and flatten |
| big rocks placed in the field | the same rocks (lichen and moss on rock fit the rule) |
| giant mushrooms placed in the field | the same 12 placements (15 removes them from the meadow) |
| — | 3–4 fairy rings placed in the open field (14.5) |
| scatter: grass turfs | scatter: moss turfs (14.3), same density and clustering |
| scatter: flowers | scatter: fungi tufts (14.3), same density and clustering |
| trees, hill band: tree0–2, old-tree1, pine0–1 | mushroom trees (14.4) |
| trees, the meadow's edge | mushroom trees, the smaller ones |
| crest clusters cluster0–9 | cap masses (14.4) |
| field groves: tree0–2, birch, willow, fruit, old, pine | mushroom trees, mixed with stumps and puffballs (14.4, 14.5) |
| field rocks group | rocks plus fallen logs and stumps with brackets (14.5) |
| sky `night` | sky `night` (unchanged) |

Regions: three of the four the ground shader holds (`litter`, `trail`, `mycelium`); one stays free. Every `avoid=` list becomes
`litter,mycelium,trail`.

## Subtickets

Each is one agent's work (one ticket per agent); merge on finish, then start the next that depends on it.

| # | Subticket | Depends on |
|---|---|---|
| 14.1 | The level's base ground material | - |
| 14.2 | Castle fully fungal | - |
| 14.3 | Moss turfs and fungi tufts | - |
| 14.4 | Mushroom trees and cap masses | - |
| 14.5 | Field pieces: fairy rings, stumps, logs, puffballs | - |
| 14.6 | Ground materials, `fungal.level`, tests | 14.1, 14.3, 14.4, 14.5 |
| 14.7 | Mesh, Unity build, snapshots, budget | 14.2, 14.6 |
| 14.8 | Docs | 14.7 |

### 14.1 — The level's base ground material

Today the ground outside every region is `ground_grass`, hard-coded as `LevelBaker.GrassMaterial`
(`SDFPipeline/Levels/LevelBaker.cs:55`), and level.json names it under the role `grass`, which `LevelBuilder` reads
(`MaterialOf(materials, "grass")`, `Unity/Assets/Level/Editor/LevelBuilder.cs:440`).

- New optional parameter on the `regions` line: `regions base=moss` makes the ground outside the regions
  `materials/ground/ground_moss.sdfmat`. Leaving it out keeps `ground_grass`.
- Keep the role name `grass` in level.json, so Unity needs no change. Say in `LevelFile`'s doc comment and the level.json comment that
  the role is the base ground, whatever its material.
- `LevelFile` reads and writes it; an unknown material gives the usual error naming the file it looked for.
- Tests: read/write round trip; a level without `base` bakes `level.json` and the material texture **byte-identical** to before
  (meadow and chessboard); a level with `base=moss` names `ground_moss` under `grass`.
- Files: `SDFPipeline/Levels/{LevelFile,LevelDefinition,LevelBaker}.cs`, `SDFPipeline.Tests/Levels/` (the region and level-file tests).

### 14.2 — Castle fully fungal

The castle is grown, not built: no stone courses on its walls and no flagstones around it.

- `materials/buildings/castle_wall.sdfmat`: replace `pattern stones size=0.55 grout=0.035` with the stems' fibres, `pattern fibres
  size=0.08 grout=0.012`, as `castle_stem.sdfmat` has. The bays are three stems packed along a core (`castle_wall_bay.sdf`), so the
  fibres running up them read as stems grown together. Rewrite the file's comment ("laid in courses of blocks ... stone grown of
  mushroom") to say so.
- **Check the walk tops.** `stones` drew flagstones on faces that look up; `fibres` run along the part's y axis. Look at the wall-walks
  and the parapet tops at eye height in a snapshot. If the fibres look wrong on the up-facing faces (stretched, aliased or ruled), drop
  the pattern line from `castle_wall` altogether: speckle and colour variation already carry the surface. Do not add a new pattern
  kind for this ticket; say in Status which one was chosen and why.
- Comments that speak of tiles or stone: `content/parts/castle/castle_wall_gate.sdf` ("its floor is the level's tiled ground" →
  "the level's ground"), the castle0 header if it mentions stone, `materials/buildings/castle_wall.sdfmat`.
- **The `stones` pattern kind stays** in `MaterialPattern`, `SdfPattern.hlsl` and the ground shader: the meadow's tiles and ticket 15's
  stone castle use it. `castle_stone.sdfmat` and `castle_roof.sdfmat` (unused today) stay for 15.
- Re-mesh the parts that use `castle_wall` (`castle_wall_bay`, `castle_wall_gate`) and `castle0` at **128 cells**, as their committed
  glbs are (06.0's rule). The glbs' `extras.sdf.materials` must carry `fibres` (or no pattern) for `castle_wall`.
- Test: no material used by `castle0`'s parts has a `stones` pattern (walk the parts from `castle0.sdf` through `Part` nodes).
- Until 15 swaps the meadow's castle, the meadow shows this fungal castle on its tiles; that is expected.

Status (14.2): done. `castle_wall.sdfmat` now has `pattern fibres size=0.08 grout=0.012` and a comment saying the wall is grown stems;
`castle_wall_gate.sdf`'s comment says "the level's ground" (castle0's header and the other parts never mention stone or tiles). Test
`CastleMaterialsTests.Castle0_UsesNoStonesPattern_InAnyOfItsParts` walks castle0 and its `Part` nodes and checks every material's
pattern. **Chosen: fibres, kept.** The `sdf render` views are CPU renders that do not draw patterns, and Unity snapshots were not run
here, so the walk tops were judged from the shader: `SdfPattern.hlsl` draws fibres as 3D noise (not a planar projection, so no
stretching or ruling) and already fades them out on faces that look up (`lines *= 1 - smoothstep(0.5, 0.8, normal.y)`), so walk tops
and parapet tops carry speckle and colour variation only. 14.7's snapshots should still glance at them; if wrong, drop the pattern
line. Re-meshing: no glb is committed (`content/**/*.glb` is gitignored; 06.0's "committed glbs" means the local ones), so none was
added; 14.7's `sdf unity` run re-meshes the castle parts and `castle0` at 128 cells and carries `fibres` for `castle_wall`.

### 14.3 — Moss turfs and fungi tufts

The foliage, scattered by the thousand and drawn instanced like the grass turfs and flowers it replaces. Folder
`content/models/fungi/foliage/`, materials in `materials/fungi/`.

- **Moss turfs** `moss-turf0`–`moss-turf4` (5, as the grass turfs): cushions of moss in two or three greens, some with fine
  sporophyte stalks; one with grey-green lichen crust; one with a few tiny mycena on top. Footprint and height like
  `grass-turf0`–`4` (measure them); the lowest 8 cm a mound tinted to the ground (`tintHeight=0.08`, as the grass).
- **Fungi tufts** `fungi-tuft0`–`fungi-tuft4` (5, as the flowers): a clump of glowing mycena bonnets (the cyan-green accent); a
  tuft of fairy-ring champignons; a few puffballs; scarlet elf cups; a coral fungus. Each oversized for the forest floor like
  `flower0` is ("so it reads at a distance").
- Budgets: per variant, triangles at each LOD within 20% of the grass turf or flower it replaces (measure both from the glbs and
  list them in Status). Same wind behaviour (they sway with the foliage group's `wind=0.04 windHeight=0.5`; the puffballs and cups
  barely move, which the height rule already gives).
- `sdf render` PNG next to each model, as the other models have.

### 14.4 — Mushroom trees and cap masses

The woods, drawn instanced in the trees and clusters groups like the meadow's trees. Folder `content/models/fungi/trees/` and
`content/models/fungi/clusters/`; reusable caps and stems as parts in `content/parts/fungi/`.

- **Mushroom trees**, 6 variants (the meadow's hill band has 6), each with a trunk collider like the trees:
  - `parasol0`, `parasol1`: tall slender stems with a ring, broad flat scaly caps (Macrolepiota), 10–14 m;
  - `inkcap0`: a tall shaggy bell, its rim inked black and dripping, 8–10 m;
  - `bracket-tower0`, `bracket-tower1`: a dead trunk covered in tiers of shelf fungi, narrowing upwards; the pine's silhouette, which
    the hill line needs;
  - `honey-cluster0`: a clump of honey fungus, many stems fanning out from one base, 6–8 m.
- **The front row must hide what is behind it**, as the meadow's does: crowns (caps) big and overlapping enough, at the same band,
  density and spacing as the meadow's front row, that from eye height nothing past the crest shows. Check it in the eye-height
  snapshots (14.7).
- **Glow**: one of the parasols and the ink cap carry the cyan-green glow on their gills' edges, so the woods glimmer at night.
- **Cap masses** `cap-cluster0`–`cap-cluster5` (6, down from the meadow's 10 crest clusters, fewer variants, fewer draw calls): caps
  of several kinds grown into one mass, the size of the meadow's `cluster0`–`9`, for behind the crest (never nearer than 30 m:
  coarse forms only).
- Wind: caps sway less than leafy crowns; set the trees and clusters draw sections' `wind` in 14.6 so the stems do not bend like
  branches.
- Budgets: each tree's LOD0 no more than the meadow's tree it replaces plus 20%; measure and list.

### 14.5 — Field pieces: fairy rings, stumps, logs, puffballs

Folder `content/models/fungi/field/`.

- `fairy-ring0`, `fairy-ring1`: a ring of 20–30 small mushrooms, 6–10 m across, with a slightly darker circle of moss. One model
  each, the mushrooms as instanced `Part` uses so each is meshed once. No collider (they are knee-high).
  `fungi-tuft1` (the champignons) can share its part.
- `stump-brackets0`, `stump-brackets1`: a broken stump with tinder or turkey-tail brackets, 1–2.5 m, convex collider.
- `log-fungi0`: a fallen log with oyster mushrooms along it, 5–7 m long, mesh collider low enough to step onto.
- `puffball0`: a giant puffball, 2–3 m, convex collider.
- Budgets like the field rocks they stand among (LOD0 within the largest rock's plus 20%).

### 14.6 — Ground materials, `fungal.level`, tests

- Ground materials in `materials/ground/`: `ground_moss.sdfmat`, `ground_litter.sdfmat`, `ground_trail.sdfmat`,
  `ground_mycelium.sdfmat` (plain, no `stone=`/`grout=`; a pale cream with fine speckle and the default edge wobble, so it reads as
  felt fraying out into the moss). Existing ground materials stay unchanged.
- `content/levels/fungal.level` as the Layout table says, with its own header comment in the style of `meadow.level`'s, and every
  section's comment rewritten for what this level holds. Fairy rings placed in the open field, clear of the trail, litter, mycelium
  and the giant mushrooms.
- If the giant mushrooms (`giant-amanita0`, `giant-porcini0/1`, `giant-amanita-cluster0`) can be listed in a `trees` section (check
  what a trees entry needs: per-part meshes and a trunk collider like the trees' prefabs), use them in the field groves too;
  otherwise keep them as `place` entries only.
- Tests in `SDFPipeline.Tests/Levels/LevelFileTests.cs`:
  - `ContentFungal_IsAValidLevel`: play size, hills, regions `litter`, `trail`, `mycelium` with no `tiles` and no stones pattern;
    `base` is `moss`; both castles are `castle0` on `mycelium` with a plateau.
  - `ContentFungal_HasNothingButFungi`: every model the level names (`place`, `scatter`, `trees`) comes from
    `content/models/fungi/`, `mushrooms/`, `rocks/`, `terrain/` or is `castle0`; none from `grass/`, `flowers/` or `trees/`.
  - The meadow's tests stay as they are (15 changes them).

### 14.7 — Mesh, Unity build, snapshots, budget

- Mesh the new models at the `Pipeline to Unity` settings (256 cells, 3 LODs; castle parts at 128). Bake `fungal.level` with
  `sdf level`.
- `LevelBuilder.BuildAllInBatch` builds `Fungal.unity`; add it to the build settings after `Chessboard`. The showcase
  (`Mushrooms.unity`) gets a `Fungi` row with the new models.
- Snapshots with `LevelSnapshot.Capture`: the eye-height shots, the castle, a wood edge, the crest from the field, a fairy ring, the
  glow at night. Run `CastleChecks` on the fungal level's castle as well as the meadow's (floor audit, walks, z-fight views).
- Measure against the budgets (README): high about 2M triangles; integrated GPU / web about 500k triangles and ≤ 200 draw calls.
  List triangles and draw calls per view as ticket 11 did, next to the meadow's figures.
- Unity batch logs with no `error CS`, shader errors or exceptions; `dotnet build` and `dotnet test` pass.

### 14.8 — Docs

- `tickets/README.md`: the row for 14; under "The level", say there are now three levels (meadow, chessboard, fungal) and what each
  is.
- This ticket's Status.

## Acceptance

- `fungal.level` bakes; `Fungal.unity` builds and is in the build settings; snapshots show a level where everything that grows is a
  fungus, under the Eye-Moon Night sky.
- `castle0` has no stone pattern on any material (test) and stands on mycelium, not tiles.
- The front row of mushroom trees hides the island's edge from eye height, as the meadow's trees do.
- No meadow asset is deleted or changed except `castle_wall.sdfmat` (and the comments 14.2 names); the meadow and chessboard bake
  byte-identical level data (test in 14.1).
- Budgets measured and listed; `dotnet build`, `dotnet test` and the Unity batch runs are clean.

## Status

Not started.
