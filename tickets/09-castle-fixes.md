# 09 — Castle fixes: cap colour bleed, coplanar floors

Two faults in the A2 castle from 06: the cap's red bleeds onto the stem at a coarser level of detail, and the walk-level floors of
neighbouring parts lie in the same plane, so they z-fight where they overlap.

Depends on: 06 (castle part kit, instanced castle parts). Independent of 07 and 04; small enough to run alongside either. The
`SdfToon.shader` wall patterns (06 step 5) are not part of this ticket.

## 1. Cap red bleeds into the stem at a coarser LOD

**Seen**: on the castle's fly agarics (towers, keep, gate turrets, maybe the baby amanitas and the merlon caps) the cap's red runs
down onto the white stem at one level of detail, but not at LOD0.

**Find the cause first**: check each level of each part (`sdf mesh … --lods 3`, then `sdf render` per level or look at the glb in
Unity) to see which parts and levels show it, then work out where the colour comes from. The likely causes, in the order to check:
- **Material borders lost in simplification**: `QuadricSimplifier` keeps vertices of different regions from merging, but a vertex
  *next to* a border may merge into it (see its comments). At a coarse level, triangles may then span from a cap vertex down the
  stem, and the per-vertex colour is interpolated across that whole triangle.
- **Material picked per vertex where cap and stem meet**: if the material at a border vertex comes from the nearest surface and the
  cap's underside overhangs the stem closely, coarse vertices on the stem's top can pick up the cap's material.
- **Instanced part meshes**: since 06, a repeated part is meshed once on its own grid. Its LOD error, and so which level is shown at a
  given distance, differs from before; check whether the bleed comes only from the shared part meshes or also from parts used once.
- **Mirrored copies** (`Assets/Level/Meshes/meadow/Mirrored/`): check the mirrored meshes keep the per-vertex colour data in step with
  the flipped winding.

**Fix**: in the pipeline, not by hand-editing meshes, so that it holds for every model (trees and showcase mushrooms have the same cap
over stem). Typical fixes:
- keep the material border as a hard edge at every level, with its vertices split so each side carries its own colour;
- stop triangles spanning more than one material at coarse levels;
- if the shapes themselves are the issue, give the cap's underside clearance above the stem in the part files.

Add a test in `SDFPipeline.Tests` (a cap-over-stem model meshed with `--lods 3`: at every level, no triangle mixes the two materials'
colours beyond a border band of one vertex). Re-mesh the models affected and check that the 40 models that were byte-identical in 06
change only where the fix touches them; report which change.

## 2. Coplanar walk-level floors between neighbouring parts

**Seen**: neighbouring parts each have a floor at the wall-walk's 5 m, and they overlap where they meet:
- the wall bays' walks, the joint pads between them, the towers' and gate turrets' balcony rings, and the keep's ring.

Two overlapping faces in the same plane z-fight: the GPU cannot tell which to draw, and the overlap flickers as the camera moves.

**Fix**: give the floors of parts that meet different heights, about **15 cm** apart:
- low enough to walk over: the controller's step height is 0.4 m, and the stairs already rise 25 cm per step;
- high enough that the depth buffer always separates them, also on WebGL, which has no reversed-Z, at the distances where the
  overlaps are seen.

A height scheme in which no two parts that touch share a level, for example:

| Part | Floor |
|---|---|
| wall bay, gate bay | 5.00 m |
| wall joint pad | 5.15 m |
| tower and gate-turret rings | 5.30 m |
| keep ring | 5.15 m or 5.30 m, whichever its neighbours are not |

- Work out the real adjacency from `castle0.sdf` and pick the scheme from it. Three levels may be enough, as above. A part that touches
  two others must differ from both, and no step between neighbours may go over about 0.3 m.
- **Stairs**: the keep stair's top step must arrive at the keep ring's new height, keeping each rise ≤ 25 cm and the slope ≤ 40°.
- **Openings**: the keep ring's openings above the stairs (06) must still line up.
- **Other coplanar overlaps**: check the whole castle for faces lying in one plane where parts overlap, not just the walk level:
  - the courtyard floor and gate passage against the flattened, tiled ground;
  - the parapets of neighbouring bays;
  - the merlons sitting on the parapet;
  - the mycelium threads on the ground.

  Apply the same rule: offset one side by a few centimetres, or sink the one that should not show so it lies just below the other.
- The parts' comments say "the wall-walk's 5 m". Update them to the new heights.

## Acceptance

- No red on any stem at any level of detail, on the castle parts and on the other cap-over-stem models. Show it with renders per
  level of a tower, the keep and a baby amanita, and with level snapshots near and far.
- No z-fighting where parts meet: snapshots from the wall-walk at the joints, a tower ring and the keep ring, at several distances,
  on both presets; also check while moving, by hand or by a scripted camera path.
- Scripted walks as in 06: gate to courtyard, up the keep stair onto its ring, along the wall-walk round both tower kinds. They still
  pass and cross every height step without jumping.
- Pipeline tests pass, including the new one.
