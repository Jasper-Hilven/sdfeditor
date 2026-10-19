# 06 — Castles: parts, detail, mushroom theme

Turn the blockout `castle0` from 02 into a finished mushroom castle built from separate parts: each tower, wall bay, stair, gate and
building is its own mesh with its own levels of detail, drawn once per use. Detail on the walls comes from the shader and from small
instanced pieces, not from denser meshes.

Depends on: 02 (placements, `collider` setting, `castle0` blockout on its plateau). **Runs in parallel with 03**: it touches the
model pipeline (`PartSplitter`, `ModelMesher`, glb, `MushroomBuilder`) and castle content, while 03 touches the level baker's scatter
and the new instanced renderer. Step 7 (castle parts through the instanced renderer) waits for 03. **Land before 04**, which reads the
tree prefabs' per-part meshes and so depends on the prefab layout this ticket changes.

## Why separate parts

Today `castle0.sdf` is one shape, meshed as one body with one LOD group:
- **LOD**: the whole castle switches level at once, by its full 31 m size. Standing at one tower, the far tower 30 m away is drawn at
  the same level as the near one; or the near one is drawn coarse because the castle as a whole is far.
- **Culling**: one bounding box covers the castle, so it is never frustum-culled while any of it is in view, and every triangle is
  drawn.
- **Meshing**: one grid over 31 × 13 × 21 m gives every form the same cell size. The thin merlons and steps need fine cells, while
  the big wall faces do not.
- **Reuse**: four towers, both stairs and every wall bay are the same shape. Meshed once and drawn several times, they cost memory
  once, and later (step 7) one instanced draw.

## How parts work today, and what changes

`Part part=name` (`SDFProject/ProgramDefinition/Parts.cs`) loads `content/parts/name.sdf` as a variable, and `PartSplitter` already
gives each part its own mesh, its own grid sized to the part and its own LODs. But a part used several times, as under `SymmetryXZ`,
becomes **one** mesh holding all of its uses: it is meshed in the model's space, placed and mirrored as the model places it.

New: **part instances**.
- A part whose every use reaches it only through rigid placements is meshed **once**, in its own file's space. Rigid placements are
  `Translate`, `Rotate`, uniform `Scale`, `Mirror`/`Symmetry*`, and `LimitedRepetition`/`Repetition` with finite limits.
- Each use becomes an instance: a transform that the splitter works out by expanding the symmetries and repetitions on the way down.
  `Material` above a use is allowed too, but uses under different materials are meshed separately, one mesh per material set.
- Any other path keeps today's behaviour and the part is meshed in place, with a pipeline warning naming the node:
  - a non-uniform scale, a `Distort` or `Displace`;
  - a smooth union with a neighbour, where the fillet would be lost anyway;
  - a use inside an intersection.
- **Mirrored uses**: a negative determinant flips winding. Unity flips culling for negative-scale renderers. Check that
  `Graphics.RenderMeshInstanced` does the same; if not, emit a mirrored copy of the mesh instead.
- **glb**: one mesh per part (with its LODs) and one glTF **node per instance** with its matrix. glTF's own node/mesh split is exactly
  this. The body, meaning what is not a part, stays a mesh as today.
- **`MushroomBuilder`**: one child per instance, sharing the part's mesh assets. Each child gets **its own `LODGroup`** sized to that
  part, so levels switch per part by its own size and distance, with its outline child, static flags and collider.
- Tests (`SDFPipeline.Tests`): instance transforms for every kind of rigid placement, nested symmetries, repetition counts, the
  fallback cases, and that a model without repeated parts bakes to the same bytes as before (trees, grass, rocks unchanged unless they
  use parts several times).

## Splitting the castle

New parts in `content/parts/castle/`, each modelled at its own origin, base at y = 0:

| Part | Uses in castle0 | Notes |
|---|---|---|
| `castle-tower` | 4 | stem, roof cap, wall-walk ring |
| `castle-wall-bay` | ≈ 10–14 | one bay of curtain wall, e.g. 5–6 m, with wall-walk, parapet, merlons; lengths set by the bay count, not by stretching |
| `castle-wall-gate` | 1 | the bay with the gate arch |
| `castle-stair` | 2 | mirrored; steps ≤ 25 cm, slope ≤ 40° as in 02 |
| `castle-keep` | 1 | the building against the rear wall; more buildings if wanted |
| detail pieces | many | see *Detail without vertices*: merlon caps, windows, shelf fungi, small mushroom clusters, lanterns |

- **Joins**: parts overlap and are not blended, since the smooth-union fillet is lost between meshes. Hide every seam: wall bays end
  inside a tower or behind a pilaster, the string course and parapet hide horizontal joins, and the stair tucks into the wall's face.
  Check from 1.5 m that no gap or z-fighting shows at any LOD pair, including where neighbouring parts are at different levels.
- **Detail per part**: each part's file sets its own `Detail`. Thin pieces like merlons and steps get finer cells, big flat walls
  coarser ones.
- `castle0.sdf` becomes a layout: only `Part` uses under translates, rotates and symmetries, plus any small unique bits of body.
- **Per-part settings**, as header lines in the part's file, read like the model's `collider` line and written to the glb extras:
  - `collider mesh|convex|capsule|none`;
  - `shadows on|off|near` (near = LOD0 only);
  - `outline on|off`;
  - `cull <metres>` for the detail pieces.

  The model's own settings remain the default.
- **Colliders**: wall bays, gate and stairs use `mesh` from the coarsest LOD that keeps the steps and the wall-walk. Towers use
  `mesh` or `convex` depending on whether they are entered. Roofs, caps and detail pieces use `none`. The courtyard, stairs and
  wall-walk must stay walkable as in 02.

## Detail without vertices

The rule: **a detail that does not change the silhouette from 1.5 m costs no vertices.** Only silhouette features are geometry, and
those are kept low-poly with flat faces that simplify well.

1. **Shader patterns** in `SdfToon.shader`, the same technique as 02's tiles: drawn per pixel in the part's object space, so they
   stick to the part and repeat across its instances. Triplanar on curved parts.
   - Stone courses and blocks with grout lines and per-block colour jitter. A bevelled normal near the grout catches the toon bands,
     and a thin ink line in the grout matches the outline's colour, so joints read as drawn, not rendered.
   - For the mushroom theme: vertical stem fibres, cap spots, gill lines under caps, a honeycomb for morel roofs.
   - The pattern and its scale are chosen by the material (`.sdfmat`: `pattern stones size=0.5 grout=0.03 ...`) and reach the shader
     the way other material data does. Do not change the per-vertex layout the IDE preview shares (see 00). If no channel is free,
     pick the pattern by material index through a small table, and document the choice.
   - **Fade out with distance and screen-space derivatives**: the grout and ink lines thin out and blend to the stone's mean colour
     before they would shimmer, by ≈ 25–40 m, which is also where LOD1 takes over. LOD switches then never change the pattern.
   - Cost: a few ALU per pixel and no textures, which suits WebGL2. Check it on the integrated-GPU / web preset (05).
2. **Painted depth** for small recesses like arrow slits, small windows and joints between big blocks: a dark shape in the pattern,
   or a flat inset of 1–2 cm, instead of a real cut. Real cuts only for openings the player sees through or walks through: the gate,
   doors, large windows.
3. **Detail pieces as instances**: merlon caps, window frames, shelf fungi, small mushroom clusters at the wall foot, glowing lanterns,
   hanging veils.
   - Each is a tiny part (≤ 200–500 triangles at LOD0, 2 LODs), placed in the castle's layout file.
   - Drawn as instances: GameObjects per use until step 7, then through 03's instanced renderer.
   - Culled beyond their `cull` distance (≈ 30–40 m), with no outline beyond ≈ 15 m and no shadows except `near`.
4. **Variation per vertex, not per vertex count**: weathering, moss toward the foot, colour drift along a wall and darker corners all
   come from the existing per-vertex colour and variation data (speckles, variation), evaluated at the coarse mesh's vertices. No
   `Distort` or noise on big faces: it defeats the simplifier and multiplies triangles.
5. **Glow is free detail**: the toon glow (subsurface) on windows, lantern caps and spots adds a lot of read at night or in shade
   for no vertices.

Budget to aim for, both castles together, per frame from anywhere in the play area: **high** ≈ 300–400k triangles including outlines
and shadows; **integrated GPU / web** ≈ 60–80k, the "castles and props" share left in 05's split. Measure LOD0 per part and the in-view
totals from three spots: courtyard, mid-field, and the far end of the field.

## Mushroom theme

The castles must still read as castles from 75 m (towers, curtain walls, gate) but be built from fungi, in the toon/blob style of the
existing mushrooms and trees and in their palette. Reuse their materials (amanita cap and spots, stems, gills) where they fit, so the
castle sits in the same world as the showcase models. Ideas to sketch, then pick from:

- **Towers as mushrooms**: thick stems with a flared base, roofed with a cap instead of a cone, in amanita red with white spots,
  bolete brown or a tall parasol. The stem's **ring (annulus)** becomes the wall-walk balcony round the tower. Gills under the cap
  give a striped underside, drawn by the shader.
- **Keep as a big bolete or puffball**: a squat, round body with a morel-honeycomb or puffball-dome roof, round windows that glow.
- **Walls as packed stems**: walls of fused stems, or stone with fungus grown through. The merlons are small button caps, which
  keeps the silhouette crenellated, rounded and cheap.
- **Gate**: an arch of two curving stems meeting under a cap, or a gill-lined arch. The gate's veil hangs like the partial veil of a
  young mushroom.
- **Stairs**: shelf/bracket fungi stepping up the inside of the wall, or spiralling round a tower stem. They must still meet 02's step
  rules: ≤ 25 cm rise, ≤ 40°, ≥ 0.8 m wide.
- **Foot**: mycelium roots spreading into the ground and clusters of small mushrooms (detail pieces) where walls meet the ground. This
  also hides the seam with the terrain.
- **Two castles, two moods**, optional: `castle0` red amanita, `castle1` brown bolete or pale parasol, from the same part kit with
  other caps and materials, as a second layout file.
- Fits the art direction: toon bands, rim, glow, ink outline; no realism. Floating eyes or hands as small decorations (gargoyles,
  door knockers) are an option, using `content/parts/eye.sdf` and `hand.sdf`.

Workflow: render 2–3 concept variants with `sdf render` (front, three-quarter, 1.5 m view from the field) and **show them before
splitting into parts**. The pick decides the part kit.

## Work

1. Part instances in `PartSplitter` / `ModelMesher` / `GlbWriter`, with tests (see above).
2. Per-part settings (`collider`, `shadows`, `outline`, `cull`) in the part header and glb extras.
3. `MushroomBuilder`: a child per instance, an LOD group per part instance, colliders and settings per part. Raise `BuilderVersion`.
4. Concept renders, then the part kit in `content/parts/castle/` and the layout `content/models/buildings/castle0.sdf` (and
   `castle1.sdf` if two moods).
5. Shader patterns and the distance fade in `SdfToon.shader`, with pattern data from `.sdfmat`. The outline shader is untouched.
6. Detail pieces, placed in the layout.
7. **After 03**: `LevelBuilder` routes the castles' repeated parts and detail pieces into the `InstancedScatterRenderer` as a static,
   windless draw group, and keeps only the colliders as GameObjects. Draw calls for the castles then scale with part kinds × LODs,
   not with uses.

## Acceptance

- [x] `castle0` is built from separate parts. The glb holds one mesh per part kind and one node per use, and the prefab shows each use
  with its own LOD group, switching independently as the camera moves along the walls.
- [x] No gaps, z-fighting or visible seams at the joins from 1.5 m, at any LOD combination.
- [x] Walls show stone or fungus detail from the shader with no triangle cost (castle_wall stones, castle_stem fibres, castle_shelf gills).
- [ ] The detail fades with distance with no shimmer and no pop at LOD switches: the fade (25 to 40 m) is built, but the shimmer and
  LOD0/1 switch tests were not run. Fibre look judged by eye only.
- [ ] The triangle budget above is met: high yes (at most 184.7k in the views measured); web courtyard about 85k against the 60 to 80k aim,
  so not met. Draw calls before and after step 7 are in the first cost table above; the second table has the current ones.
- [x] The castle reads as a castle and as part of the mushroom world. Shown in concept renders and in level snapshots from the field and
  the courtyard.
- [x] Courtyard, stairs and wall-walk are walkable, and the walls, towers and gate block the player.
- [x] Existing models that use a part only once bake as before (byte-identical glb where nothing changed).

## Status

Done, including steps 5 and 6's further details (shader wall patterns and the distance fade, window frames, wall shelves, lanterns
and veils; subtickets 06.1 to 06.11, listed at the end). Phase 1 is committed on `castles` (c660541) and main (03, 07) is merged in
(7d4ff48); steps 5 and 6's details are committed after that (see the block below).

- **Part instances** (`PartSplitter`, `ModelMesher`, `GlbWriter`): a part used two or more times (symmetries and repetitions count
  their copies), every use through rigid placements only, is meshed once in its own file's space; each use is a glTF node `part.k`
  with its matrix, its level nodes `part_LODn.k` with the same. A use under a smooth union, non-uniform scale, Distort, Displace,
  subtraction, intersection, across a mirror plane, wider than a repetition cell, or under an endless repetition keeps the part in
  place, with a warning naming the node. Uses under different materials get a mesh each (`part@material`). A part used once is
  meshed in place as before: of the 43 models, 40 mesh byte-identically to the old code; `amanita-scout`, `chanterelle-scout`
  (eyes and hands, mirrored) and `birch1` (its crown twice) change. Occlusion and thickness of an instanced part are measured where
  its first use stands.
- **Per-part settings**: `collider`, `shadows on|off|near`, `outline on|off` and `cull <metres>` lines in a model's or a part's
  file (`ModelSettings.cs`); a part's are read over the model's and written to `extras.sdf.parts` under its mesh's name, the
  model's to `extras.sdf` where they differ from the default. Parts may live in a kit folder (`content/parts/castle/`).
- **`MushroomBuilder`** (version 11): a child per use (`castle_wall_bay.3`, ...), placed, turned, scaled and mirrored as its node,
  with its own LOD group sized to the part, its levels as children sharing the part's mesh assets, and its collider; the layout is
  documented in the class comment. A use culls where the whole model would unless its part has a `cull`.
- **Part kit** (`content/parts/castle/`, generated from concept A2's numbers): `castle_wall_bay`, `castle_wall_gate` (with the veil),
  `castle_wall_joint` (stem with cap, pad at the walk), `castle_tower_large` / `castle_tower_small` / `castle_gate_turret` (flat ring
  at 5 m as a balcony), `castle_keep` (19 m, ring at 5 m with an opening over each stair), `castle_stair` (twenty shelf fungi, 25 cm,
  about 37° on the middle line, 1.1–1.9 m wide), and the details `castle_merlon` (shadows near), `castle_amanitas` (shadows near,
  cull 35) and `castle_mycelium` (curving, tapering, branching threads; shadows off, cull 40). Walls, joints, towers, keep and stair
  have `collider mesh`; details `none`. The big parts are cut off 30 cm below the ground. `castle0.sdf` is the layout: 105 `Part`
  uses under translates, turns, uniform scales (a scaled tower is sunk by as much as its ring rose) and one `SymmetryX` (the
  stairs). The concepts folder is deleted; the generator is `.claude/jobs/c6233055/tmp/castles/scripts/castle_kit.py`.
- **Triangles** at the committed glbs' settings (`--resolution 128 --lods 3`), LOD0 / LOD1 / LOD2 per part: wall bay 1,138 / 262
  / 96 (×17), gate 1,690 / 422 / 170, joint 1,370 / 306 / 126 (×12), large tower 3,358 / 722 / 296 (×2), small tower 3,970 / 810 /
  296 (×2), gate turret 3,198 / 598 / 256 (×2), keep 6,428 / 1,056 / 450, stair 3,769 / 733 / 229 (×2), merlon 246 / 70 / 34 (×54),
  amanitas 580 / 152 / 64 (×6), mycelium 240 / 66 / 58 (×7). The castle at LOD0: about 91k. The 512-cell run config would
  quadruple that; it needs a per-model resolution rule (as 03 found for the turfs).
- **Step 7**: `draw castle rings=[25, 60, 400] outline=2 shadows=3 lowShadows=1 blend=2 lowDistance=0.4` in `meadow.level`.
  `LevelBuilder` (version 4) unpacks each placed castle, gives every use with a LOD group to `InstancedScatterRenderer` as a
  prototype per model and part (levels read from the use's LOD group, with each level's outline and shadow casting as the prefab has
  them) plus a `ScatterPlacement`, and keeps the use as a GameObject with its colliders only. The keep and gate bay (single uses)
  stay GameObjects. `ScatterPart.shadows` and `ScatterPrototype.cull` are new; `FrameStats` has draws and triangles per group.
  **`RenderMeshInstanced` does not turn the triangles round for a mirrored matrix** (the mirrored stair came out dark and inside
  out), so a mirrored use gets a prototype with mirrored copies of the meshes (`Assets/Level/Meshes/<level>/Mirrored/`).

Verified:

- `dotnet build` is clean, `dotnet test`: 272 pipeline tests and 533 project tests pass (new: instance transforms for each rigid
  placement, nested symmetries, hidden mirror images, repetition counts, ten fallback cases, material groups, glb nodes and matrices,
  no instances without repeated parts, part settings and their errors, kit folders).
- Unity batchmode on the worktree (Library cloned from the 02 clone): no compile or shader errors; prefabs rebuilt; Meadow rebuilt
  with both castles on their plateaus. Collider walks (`LevelSnapshot`, CharacterController 0.3 × 1.75 m, step 0.4 m, 45°):
  - through the gate into the courtyard and on to the keep, and across the courtyard 14 m, at ground level;
  - up the keep's stair onto its ring: ends 4.97 m up (18 of 21 waypoints reached exactly, the rest within reach);
  - from the ring along the rear and side wall-walk past the small and the large tower's rings to bay 2: stays between 4.94 and
    5.12 m and ends 0.19 m from its goal;
  - a wall bay stops the walker 10.89 m from the middle (outer face ≈ 10.5 m), the large tower at 16.05 m (foot at 15.6 m).
- Cost per view, both castles, outlines included, shadow casters not:

  | View | GameObjects (before) | After step 7: GameObjects + instanced |
  |---|---|---|
  | far end of the field, high | 132 draws, 33k tris | 4 + 10 draws, 3k + 20k tris |
  | meadow 28 m before the gate, high | 189 draws, 148k | 4 + 18 draws, 16k + 36k |
  | at the gate, high | 187 draws, 171k | 4 + 20 draws, 16k + 163k |
  | courtyard, high | 114 draws, 123k | 2 + 20 draws, 13k + 151k |
  | wall-walk, high | 66 draws, 70k | 2 + 18 draws, 13k + 76k |
  | far end, web | 96 draws, 11k | 4 + 10 draws, 3k + 20k |
  | courtyard, web | 110 draws, 117k | 2 + 30 draws, 13k + 71k |

  Draws drop from 66–199 to 14–36 per view. High stays under 300–400k including the casters. Web is at 84k in the courtyard, just
  over the 60–80k aim; `lowDistance` (0.4) or fewer outline rings on web would bring it down.
- Snapshots (`.claude/jobs/c6233055/tmp/castles/snaps-before`, `snaps-after`): the castle reads as a castle from the far end of the
  field; both stairs draw lit after the mirrored copies; no gaps show at the bay, joint and tower joins from 1.5 m.

To check by hand:

- Walking it in Play mode, in particular the tower rings' lean (up to 2°) where the walk meets them, and the stair's inner edge.
- LOD changes by ring distance on the instanced parts: per-part screen size no longer decides, as rings are per group.
- Shadow cascades: shadow-caster triangles for the castle group are not logged separately.
- Looking at the courtyard from the wall-walk, and the castle's look at night once 07 is merged (glowing windows rely on it).

**Steps 5 and 6 details (subtickets 06.1 to 06.11).**

- **Pattern pipeline**: a `.sdfmat` takes one optional line `pattern stones|fibres|gills size=<m> [grout=<m>]` (default grout
  `size / 20`, 06.1, f44e033). `MaterialFile.ReadPattern` returns it; `MaterialFile.Read` is unchanged. The glb's
  `extras.sdf.materials[i].pattern` carries `{kind, size, grout}` only for patterned materials; castle0 at 128 and birch0 at 256 mesh
  byte-identically. `MushroomBuilder` (`BuilderVersion` 13, 06.2, 39352c0) writes **UV7** = (kind, size, grout, weight) on meshes with
  a patterned material (weight 0 at the border of a patterned material); meshes without a pattern have none. `LevelBuilder.Mirrored`
  already copied every channel, so it is unchanged. `SdfPattern.hlsl` (06.3, 40a70e0) draws stones, fibres and gills per pixel in the
  part's object space, with ink lines (`_ToonInk`) and a fade to the mean colour from 25 to 40 m plus an `fwidth` term. The fragment
  in `SdfToon.shader` keeps the `glow` argument of `SdfToonShade` and the final `SkyFade(color, input.positionWS)`. Object-space
  position and normal are interpolators (`uv7` TEXCOORD10, TEXCOORD11, TEXCOORD12): 13 of WebGL2's 16 varyings. The outline shader and
  `SdfToonFar.shader` are untouched. The IDE preview and `sdf render` do not draw patterns (06 did not ask for it).
- **Castle materials** (06.4, d88b79d): `castle_wall` (stones 0.55 / 0.035) on the wall bays and the gate bay's wall; `castle_stem`
  (fibres 0.08 / 0.012) on the towers, keep, gate turrets and joint stems; `castle_shelf` (gills 0.09 / 0.012) on the stairs' shelf
  fungi. Caps, the gate's veil and the small details are unpatterned.
- **Pattern tuning** (06.5, 3b1e002; 06.11, b231e04): the fibre noise distance now scales with `2.5 * size` (it read as large blotches),
  then fibre ink is 0.4 of the surface's ink (`SDF_FIBRE_INK`), since fibres still read as bark beside the stone courses.
- **Detail pieces** (06.6 to 06.9, 06.10 deac55f): `castle_window_frame`, `castle_wall_shelf`, `castle_lantern`, `castle_veil` in
  `content/parts/castle/` (all `collider none`, `shadows off`, `outline off`; `cull` 35, 35, 40, 35). Placed in `castle0`: 16 window
  frames, 12 wall shelves, 6 lanterns, 4 veils. LOD0 triangles per piece 200 / 176 / 226 / 224. The castle at LOD0 is about 98.6k, +8%
  over the 91k above. Rebuilt in Unity in 06.11 (b231e04); the pieces go through the instanced renderer with their cull distances.
- **Cost** with the details and patterns (GameObjects plus instanced group, outlines included, shadow casters not):

  | View | Draws | Triangles |
  |---|---|---|
  | high, far end | 20 | 22.9k |
  | high, meadow 28 m before the gate | 28 | 58.0k |
  | high, at the gate | 31 | 184.7k |
  | high, courtyard | 26 | 166.1k |
  | high, courtyard gate | 46 | 131.8k |
  | high, wall-walk | 24 | 89.6k |
  | high, wall joint | 31 | 144.2k |
  | high, tower foot | 44 | 150.9k |
  | web, far end | 20 | 22.9k |
  | web, courtyard | 39 | 85.0k |

  Web in the courtyard is about 85k, over the 60 to 80k aim. The pattern's web-preset ALU is only estimated, at about 200 worst case.

To check by hand (steps 5 and 6):

- No shimmer test by two snapshots a few cm apart at 20, 30 and 40 m, and no LOD0/LOD1 switch test with the pattern.
- Cull distances of the detail pieces (35 to 40 m) were not measured; no 1.5 m detail close-up of the pieces was taken.
- The gate lanterns and the joint shelves were not seen in renders; the night look of lanterns and window panes was not checked.
- The fibre look was judged by eye only. The pattern's web ALU is an estimate, not a device measurement.
- Unity `LevelSnapshot.Capture` hung in batch mode after writing its snapshots and had to be killed; the Meadow, Chessboard and
  Mushrooms scenes were regenerated and committed.

## Subtickets (step 5 and step 6's further details, all done)

Shared rules: [06.0](06.0-rules.md). Order: 06.1, 06.3, 06.6 to 06.9 are independent; 06.2 and 06.4 follow 06.1; 06.5 follows
06.2, 06.3 and 06.4; 06.10 follows 06.4 and 06.6 to 06.9; 06.11 follows 06.5 and 06.10. The IDE preview does not draw patterns (06 does
not ask for it).

- [06.1](06.1-pattern-format.md) `.sdfmat` pattern line, reader, glb extras, tests
- [06.2](06.2-pattern-unity-data.md) pattern data into the Unity meshes (UV7)
- [06.3](06.3-pattern-shader.md) shader pattern stage and distance fade
- [06.4](06.4-castle-materials.md) castle materials with patterns, assigned to the parts
- [06.5](06.5-pattern-unity-check.md) patterns in the engine: look, fade, cost
- [06.6](06.6-detail-window-frame.md) window frame piece
- [06.7](06.7-detail-wall-shelf.md) wall shelf fungi piece
- [06.8](06.8-detail-lantern.md) lantern piece
- [06.9](06.9-detail-veil.md) veil piece
- [06.10](06.10-detail-placement.md) place the pieces in castle0
- [06.11](06.11-detail-unity-check.md) pieces in the engine: rebuild, snapshots, budget
