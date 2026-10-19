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

## 3. Small instanced parts drop LOD too early

**Seen** (found while merging 08): since 06 each part instance has its own LOD group, sized to the part. A small part like the
amanita-scout's eye shows LOD2 from about 4 m, because the eye switches to LOD1 below 32 % of the screen's height. At LOD2 its pupil
rings simplify into a cross. The same holds for any small instanced detail with fine colour patterns: merlon caps, baby amanitas.

**Fix**: a small part's levels should switch at least as late as its model's would. Two ways:
- an instance's LOD heights never switch earlier than the whole model's LOD group would;
- a lower error limit, or a higher `Detail`, for parts with fine material patterns like the eye's rings.

Check the explosion in `SpecialEffects`: the intact scout's eyes and the pieces' eyes must match at frame 0.

## Acceptance

- No red on any stem at any level of detail, on the castle parts and on the other cap-over-stem models. Show it with renders per
  level of a tower, the keep and a baby amanita, and with level snapshots near and far.
- No z-fighting where parts meet: snapshots from the wall-walk at the joints, a tower ring and the keep ring, at several distances,
  on both presets; also check while moving, by hand or by a scripted camera path.
- Scripted walks as in 06: gate to courtyard, up the keep stair onto its ring, along the wall-walk round both tower kinds. They still
  pass and cross every height step without jumping.
- Pipeline tests pass, including the new one.

## Status

Parts 1 to 3 are done; the walks and snapshots in Unity are not (see below). 06 step 5 is not part of this branch.

- **1. Cap colour bleed: the cause** was neither simplification across borders nor the mirrored meshes, but the material sampled
  per vertex. Simplification keeps regions apart, but a merged vertex may come to stand where another material is: a cap vertex in
  the corner under the cap where the stem goes in. Its material was sampled at its new place, so it took the stem's colour, or a
  stem vertex the cap's, and the faces it shares with its own region then ran from one colour to the other across the whole face:
  on the small tower at LOD0 a white corner on red faces across the cap's 2.2 m underside, at LOD2 red faces reaching from the cap
  down the stem to the ring at 5 m. Shared part meshes and parts used once alike; any cap over a stem.
- **The fix** (`ModelMesher.InRegions`): a vertex whose sampled material is not its region's is sampled instead at the nearest point
  of its own region on the way back to where contouring put it (the vertex itself does not move). `QuadricSimplifier` now hands on
  which contoured vertex each simplified one is (`TriangleMesh.Sources`, its only change). A border band in the simplifier itself
  (no face of two regions with a corner far from the border) was tried and made no difference, so it was left out.
- **Test**: `LevelOfDetailTests.CapOverStem_ColoursChangeOnlyAtTheBorder_OnEveryLevel`, the small tower's fly agaric at 128 cells
  with 3 levels: every corner of a face whose corners have different materials lies within two cells plus the level's error of the
  border. It fails on the old code (LOD0: a corner 16.4 cells off). Measured on the castle parts after the fix: cap vertices never
  lie below the cap's underside at any level, and faces of both materials lie along the border (on the small tower 9.32 to 9.44 m up,
  where the stem meets the underside at 9.41 m).
- **Re-meshed**: every model with a vertex the fix re-samples changes, by a few vertices along borders (0.1 % at LOD0 to 5 % at
  LOD2); the others are byte-identical. At their committed settings and committed here: at 128 cells `chanterelle-scout`, `flower0`,
  2, 3, 4, `grass-turf0` to 4, `rock1`, `rock4`, `fruit-tree0` to 2 and `castle0`; at 512 cells `amanita-scout`, the exploding
  pieces (all but the hands, which stay identical) and the sky's `eye-moon` and `floating-cap0`. Unchanged: the other trees and
  rocks, `flower1`, the goop. **Not re-meshed**: the weapons and the chess pieces (512 cells), which change the same way; the
  `Pipeline to Unity` run configs re-mesh them.
- **2. Floor heights**, from the adjacency in `castle0.sdf` (worked out with a script that, over the whole castle, finds the top two
  floors at each point): joints' pads touch bays and towers; towers touch bays and joints, large and small towers' rings touched each
  other, and the small towers' rings the keep's; the keep's ring touches bays and joints; neighbouring bays overlap at every joint.
  The scheme: **bays (and the gate bay) 5.00 m, joint pads 5.15 m, tower, gate-turret and keep rings 5.30 m**, with the large and
  small towers' rings 16 cm smaller (radius 3.52 m) and the keep's ring 15 cm smaller (4.85 m), so no two rings at 5.3 m overlap.
  Neighbouring bays overlap only under a pad or ring. Every step between neighbours is 15 or 30 cm.
  - **The towers' lean tilted their rings** through the floors next to them: 5 degrees on the gate turrets took their rings 21 cm up
    and down where the walk meets them. The leans are cut (sideways to a fifth, outwards to half: the turrets now lean about 1
    degree apart). The top two floors are then at least 10.5 cm apart anywhere (a leaning ring over a joint pad), 15 cm almost
    everywhere; steps are at most about 33 cm.
  - Scaled towers sink or rise by the new ring height: 11 by 0.318 m, 13 by 0.424 m, 17 by 0.212 m.
  - **Stair**: 22 steps of 24 cm to 5.3 m over the same arc as before, 30 cm apart on the middle line (38 degrees); the keep's ring
    openings are unchanged (the last fourteen steps come up under them).
  - **Other overlaps**: the gate passage is cut 10 cm below the ground, so its floor is the tiled ground, not a face of its own at
    y = 0; the mycelium is cut 3 cm below the ground; the merlons floated 5 cm above the parapet and now sit 5 cm into it; the joint
    cap's flat underside lay 4 mm under the parapet's top and is lowered 5 cm into it. The parapets of neighbouring bays meet inside
    the joint stems and tower stems.
  - The parts' and the layout's comments give the new heights; the layout's header documents the scheme.
- **3. LOD switching** (`MushroomBuilder`, version 12): a use of a part meshed once changes level no earlier than the whole model's
  LOD group would (its heights, scaled to the use's size); a use's own error limits only make it change later. The scout's eyes now
  change where its body does, not at 4 m. In `SpecialEffects` the exploding pieces take the intact scout's LOD heights as well as
  its size and middle, so intact eyes and pieces' eyes change level at the same distance.
  - For the castle's parts drawn by the instanced renderer this changes nothing (its rings decide); drawn as GameObjects, small
    parts such as merlons now keep LOD0 out to where the castle as a whole would (about 50 m).
- `dotnet build` is clean; `dotnet test`: 318 pipeline and 533 project tests pass.

Left, to check by hand or in a later run:

- **Unity**: prefabs, Meadow and Chessboard rebuilt in batch mode with no compile or shader errors, but no snapshots, walks or Play-mode
  check yet: the walks (gate to courtyard, up the keep stair onto its 5.3 m ring, along the wall-walk past both tower kinds, every
  step 15 or 30 cm), z-fighting at the joints and rings at several distances on both presets, and renders per level of a tower, the
  keep and a baby amanita.
- The explosion's frame 0 (`SpecialEffectsSnapshot`): the intact scout's eyes against the pieces'.
- Under every cap the gills disc lies inside the rounded cap (its `Round` takes the cap 1.2 cm below the gills), so the undersides
  are red, not striped; a modelling fix for 06's look, not for the bleed.
