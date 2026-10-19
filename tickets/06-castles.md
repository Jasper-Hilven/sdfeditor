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

- `castle0` is built from separate parts. The glb holds one mesh per part kind and one node per use, and the prefab shows each use
  with its own LOD group, switching independently as the camera moves along the walls.
- No gaps, z-fighting or visible seams at the joins from 1.5 m, at any LOD combination.
- Walls show stone or fungus detail from the shader with no triangle cost. The detail fades with distance with no shimmer and no pop
  at LOD switches.
- The triangle budget above is met. Report draw calls per castle on both presets, before and after step 7.
- The castle reads as a castle and as part of the mushroom world. Shown in concept renders and in level snapshots from the field and
  the courtyard.
- Courtyard, stairs and wall-walk are walkable, and the walls, towers and gate block the player.
- Existing models that use a part only once bake as before (byte-identical glb where nothing changed).
