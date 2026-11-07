# 66 — Meshes cut along material borders (from TowerDefenseV2)

Bring TowerDefenseV2's new material-border meshing into `SDFPipeline/`. Until now every vertex had one material and the colour was
blended across each triangle, so a triangle with corners of two materials smeared the border into the shape of the triangles (the
"triangular artifacts" on painted details: the scouts' eyes and pupils, the weapons' bands, the castles' trims). With the border cut,
every face is of one material, the border runs along a line of edges through the SDF's true border, and the points where three
materials meet are vertices.

## Source

TowerDefenseV2 commit **`42b312c`** ("Meshes cut along material borders ...", on its `main`), on top of `8f62f79`. Our meshing code
is the same as TowerDefenseV2's at `8f62f79` apart from two comment lines (`QuadricSimplifier.cs`: one `<param name="locked">` line;
`MeshingOptions.cs`: "(Ported from BattleRoyale, ticket 43.)"), so the files copy over; keep our `MeshingOptions.cs` as it is (the
commit does not touch it). Paths: TowerDefenseV2's `pipeline/src/SDFPipeline/...` is our `SDFPipeline/...`, its
`pipeline/tests/SDFPipeline.Tests/...` our `SDFPipeline.Tests/...`. Name `42b312c` in the commit message.

## What the algorithm does

1. **Cut** (`BorderCut.Cut`, after `MaterialBorders.Straightened`, before simplification): bisect a border point on every contour edge
   whose ends are of two regions; in a face whose corners are of three regions, find the point where the three meet; split each face
   along the segments between them, within its own plane. The border points stay on the contour's edges (moving them onto the
   surface turned thin faces over beside creases).
2. **Simplifier face-region mode** (`QuadricSimplifier.SimplifyInSteps(..., faceRegions:)`): every face keeps its region; a vertex in
   one region folds freely (also into a border point), a border point only along its own border into the next one (a boundary-plane
   quadric per border edge prices bending it), a point where three regions meet stays. Tracked points land only on faces of their
   own region.
3. **Split and sample** (`BorderCut.SplitByRegion`, `ModelMesher`): each border point gets one copy per region, sampled a little way
   into its own faces, so after a `Union` the colour changes sharply on the border and after a `SmoothUnion` both sides have nearly
   the same mix. Every vertex lists its own region's material first.
4. **One-sided border normals** (`ModelMesher.OneSidedNormals`): each copy takes the SDF's normal just inside its own side; copies
   within the crease angle share their average (paint on a smooth surface shows no seam), the others keep their own (an eye set into
   a head no longer bends the skin's normals towards the eye).

TowerDefenseV2's `kk rig skin --shading smooth` / `--wireframe on` preview options are in the same commit, but we have no `Kk.Cli`
or `SkinPreview` here: leave them out, and look at the results in Unity.

## Work

1. Copy from TowerDefenseV2 `42b312c` (`git -C ~/Desktop/gdev/TowerDefenseV2 show 42b312c:pipeline/<path> > <our path>`):
   - `src/SDFPipeline/Meshing/BorderCut.cs` (new) → `SDFPipeline/Meshing/BorderCut.cs`
   - `src/SDFPipeline/Meshing/ModelMesher.cs`, `QuadricSimplifier.cs`, `DualContouring.cs` (`TriangleMesh.FaceRegions`)
     → `SDFPipeline/Meshing/`
   - `tests/SDFPipeline.Tests/Meshing/BorderCutTests.cs` (new), `LevelOfDetailTests.cs` → `SDFPipeline.Tests/Meshing/`
2. Check that nothing of ours calls `QuadricSimplifier` or `CreaseNormals` in a way the commit changed: `ModelMesher` is the one
   caller that passes face regions; the ground (`GroundMesher`) still uses vertex regions and is unchanged. Grep for other callers
   (the IDE, the BattleRoyale ports) and run their tests.
3. `dotnet test SDFProject.sln` (the pipeline projects): everything passes. `GpuSdfTests.Simplifying_OnTheGpu_GivesTheCpusMesh` can
   miss its 2 % band under the load of the full run (it did in TowerDefenseV2 before and after the change); run it alone to check.
4. Before and after on a few models at the default 128 cells (the rule below: no full re-mesh in this ticket's first part): the
   amanita and chanterelle scouts (eyes, pupils, stems), one weapon, `castle0`, one game piece. Note `sdf mesh`'s triangle counts, and
   capture each in Unity before and after (the lookdev captures). Put the images in `Media/border-cut/` and the counts in Status.
5. With the user's go: re-mesh everything (`Update to Unity` run config) and check the per-level triangle budgets (high ≈ 2M,
   integrated GPU / web ≈ 500k) against the counts before.

## Expect

- In TowerDefenseV2, at 128 cells: models with many small painted details (towers, 15 materials) gained 35-40 % triangles and took
  about twice as long to mesh; characters +3 % to -20 %. At 256 cells: +1 % to +13 %. Watch the web preset's budget on levels with
  many painted pieces (the chessboard, the computer level).
- Back faces only where a part is about one cell thick (a sheet folds through when simplified), as before the change.
- The outline (inverted hull along the normals) can split where border copies now have different normals (folds), as it already
  does at creases. A smoothed outline normal in a spare vertex channel would fix both; not in this ticket.

## Done when

- The files above match `42b312c` (paths mapped), and the pipeline tests pass, `BorderCutTests` included (cut keeps area and volume,
  three-material junctions, union vs smooth union, eye-in-head normals).
- Before/after captures and triangle counts for the five models are in `Media/border-cut/` and Status.
- After the user's go: everything re-meshed and the budgets checked.

## Status

Steps 1-3 done; step 4 half done (counts, no Unity captures); step 5 waits for the user's go.

- Step 1: copied from TowerDefenseV2 `42b312c`: `Meshing/BorderCut.cs` (new), `ModelMesher.cs`, `QuadricSimplifier.cs`, `DualContouring.cs`,
  tests `BorderCutTests.cs` (new) and `LevelOfDetailTests.cs`. `MeshingOptions.cs` untouched. Skin-preview options left out (no Kk.Cli).
- Step 2: other users of `QuadricSimplifier` / `CreaseNormals` / `ModelMesher`: `GroundMesher` (vertex regions, unchanged), `Kk.Rigging/RigMesher.cs`,
  `SDFPipeline.Cli/Program.cs`, and their tests. All compile and pass.
- Step 3: `dotnet test SDFProject.sln -c Release`: SDFProject.Tests 533/533, Kk.Rigging.Tests 93/93, SDFPipeline.Tests 1228/1229. The one
  miss was `SimplificationTests.LargeFlatWalls_SimplifyToTheirCorners_Quickly` (29 s against a 20 s limit, timing under the full run's load);
  it passes alone, as does `GpuSdfTests.Simplifying_OnTheGpu_GivesTheCpusMesh`.
- Step 4, `sdf mesh` at 128 cells, triangles before -> after (CLI built from HEAD before the change, then after; output in scratch, not Unity):
  - amanita scout: body 2,752 -> 1,954, eye 1,566 -> 1,472, hand 612 -> 612
  - chanterelle scout: body 3,620 -> 2,250, eye 1,566 -> 1,472, hand 612 -> 612
  - pistol (body): 1,372 -> 1,858 (+35 %)
  - castle0 (sum over its 14 distinct parts): 15,656 -> 16,466 (+5 %); e.g. keep 3,534 -> 3,708, merlon 210 -> 266, tower_small 1,506 -> 1,770
  - knight-dark (chess piece, one material): 1,104 -> 1,104, unchanged
  Meshing time about the same here (castle0 31 s -> 26 s, pistol 3.1 s -> 3.3 s).
- Left: Unity captures before/after into `Media/border-cut/` (the editor was open, so no batch Unity run), and step 5 (full re-mesh and
  budget check) with the user's go.
