# 08 — Special effects

A test bench scene for the weapons' effects, `Assets/Scenes/SpecialEffects.unity`, built from code like the mushroom showcase:
lightning from the electro gun, tracer lines from the bullet guns, goop from the blob gun, and the amanita scout bursting into
pieces cut from its own SDF. Toon look only, WebGL2-safe (no compute, no particle-system features), cheap enough for the
integrated-GPU / web preset.

Depends on: 00 (URP, toon shaders), 03 (nothing directly; the bench reuses the showcase's lighting).

## Effects

- **Lightning (electro gun)**: a jagged bolt from the orb at the muzzle to a point on the target, with 2–4 forks, rebuilt by midpoint
  displacement every 50 ms (one shape in ten left out, so it strobes), thinning out over half a second. Cel-painted: a white core in a
  hard-edged electric-blue band, no soft glow. A flash and a spray of sparks where it strikes, a flickering orb at the muzzle.
- **Tracers (pistol, minigun, sniper)**: a tapering streak per shot at a set speed, length, width and colour per weapon, a burst of
  shots with spread; a star-shaped muzzle flash for a couple of frames; dust puffs and sparks where a shot lands (a raycast). The sniper
  leaves a thin trail along its line that thins away.
- **Goop (blob gun)**: gobs of goo in a ballistic arc, stretched along their flight with speed and wobbling, squashed flat where they
  land (ground or rock), then a puddle stuck along the surface that pops out, stays 3 s and shrinks back into it, with drops thrown
  off. Gobs and puddles are SDF-baked models in the gun's `blob_goo` material, drawn with the toon shader and outline.
- **Exploding mushroom**: the amanita scout is swapped for its pieces in place, which fly apart with spin, bounce on the ground and
  shrink away; a starburst flash, a toon puff of cream and cap-red balls and a cloud of glowing spores go with it; the eyes and hands
  are pieces of their own and pop off faster. The mushroom pops back after 3.6 s.

## Status

Done (not committed):

- **Pieces of the amanita scout** (`content/models/characters/exploding/amanita-scout-*.sdf`, 14 files):
  - The scout's shape moved, unchanged, into the part `content/parts/amanita_scout.sdf`; `amanita-scout.sdf` is now `Part
    part=amanita_scout`. Each piece is `Intersection(Part amanita_scout, Material amanita_flesh (cell))`, so an edit to the part changes
    the scout and every piece, and the cut faces are in the new `materials/characters/amanita/amanita_flesh.sdfmat` (Intersection takes
    the material of the child that is farther out, the cell on the cut faces).
  - The cells split space exactly, as a tree of cuts: a plane under the gills (a little tilted) takes the cap off; the cap is a 3D
    Voronoi of six seeds (a domed middle that holds the top of the stem, and five rim wedges at uneven angles, the bisectors leaning
    out); below it, the part `content/parts/amanita_scout_core.sdf` (a `Revolution` of stacked `Trapezoid2D` bands following the
    stem's measured width, 1.2–3 cm clear of the stem and of the eyes and hands) holds the stem, bulb and ring. Inside the core the
    stem breaks under the ring and above the bulb, and the bulb in two along a slanted plane; outside it, planes x = 0 and y = 0.705
    give each eye and each hand a piece of its own, whole (no cut face on them). Shared planes are written once rounded and then negated
    exactly, so neighbouring cells have no gap. The bands overlap 3 cm along their own slopes, so the core's inside has no seam
    where its distance comes back to zero (a first version without the overlap showed sheets in the pieces in the preview).
  - The files were written once by a small generator (not in the repo); they are the source now. `Detail detail=0.5` at their root:
    1.1–4k triangles per piece at LOD0 at 512 cells (about 37k for all 14, the scout itself is 28k).
- **Pipeline**:
  - `PartSplitter`: a part's own parts are meshes too (the scout's eyes and hands, now inside the `amanita_scout` part), and a
    transform or modifier round nothing but parts strips to nothing rather than an empty body. The scout's meshes are now
    `amanita_scout`, `eye`, `hand` (were `body`, `eye`, `hand`); its old `amanita-scout_body*.asset` meshes are deleted.
  - Model setting `surface part=<name>` (`ModelSettings`, `MeshingOptions.SurfaceOf`, `SdfProgramFile.LoadPart`, CLI): where a piece's
    surface is the part's own (within half a cell), thickness and occlusion are measured on the part, not the piece. Without it the
    vertices along a cut had thickness 0, which turned the cap's thin-edge colour on along every seam (orange zigzags), and the stem
    pieces lost the cap's occlusion. The pieces use it.
- **Goop models**: `content/models/effects/goop-blob0..2.sdf` (lumpy gobs, flying along +z) and `goop-splat0..1.sdf` (a lobed puddle
  with droplets, a starburst puddle; base at y = 0), all `blob_goo`, 1.2–1.5k triangles.
- **Showcase**: `MushroomScene` leaves the groups `characters/exploding` and `effects` out of `Mushrooms.unity`.
- **Unity** (`Assets/Effects/`):
  - `Shaders/SdfEffect.shader`: one unlit toon shader for every streak and ball, spread to face the camera in the vertex shader (the
    mesh holds only positions and directions). Streaks and glowing balls are a band colour with a hard-edged core; lit balls are a disc
    shaded as a sphere in two toon bands from the sun, with an ink ring. Opacity dissolves with a 4 × 4 ordered dither, so fading keeps
    hard edges. Transparent queue, no depth write (so depth priming leaves it alone), no blending, no textures, target 3.5.
  - Runtime: `FxBuffer` (every effect quad of a frame in one preallocated dynamic mesh, one draw), `FxParticles` (pooled sparks, puffs,
    drops, spores, ground bounce), `FxRandom` (seeded), `FxEffect` (base: fired, stepped by the bench's clock, never by its own Update),
    `LightningGun`, `TracerGun`, `GoopGun`, `ExplodingMushroom`, `SpecialEffectsBench` (keys 1–6, Space for all, L loop, R reset,
    P pause, and IMGUI buttons; the loop fires each effect on its own interval).
  - Editor: `SpecialEffectsScene` (Tools > SDF Effects > Rebuild Scene; batch `SdfEffects.Editor.SpecialEffectsScene.BuildInBatch`)
    builds the scene anew with `MushroomScene.CreateCamera`/`BuildLighting`, the forest floor with a collider, the guns at 1.05 m with
    muzzle points (bullet guns aimed at the backstop), the chanterelle (capsule collider) as the lightning's target, rock1 as the
    tracers' backstop, rock3 for the goop, the amanita with its 14 pieces waiting inactive under it, 9 gobs and 14 puddles pooled,
    and adds the scene to the build settings after Meadow. `SpecialEffectsSnapshot` (batch `...SpecialEffectsSnapshot.Capture
    -snapshotDir <dir>`) builds it and steps the bench in the editor to render each effect mid-action.
  - The swap is seamless at frame 0: the pieces' LOD groups take the intact model's size and middle (so they show the level it
    showed; with their own smaller size they dropped to LOD1 and the seams turned jagged), their ink lines are off for the first 50 ms
    while the intact model's ink line (its surfaces hidden) still goes round them, then the intact model goes.

Verified:

- `dotnet build` clean; `dotnet test`: 254 pipeline tests (11 new) and 533 project tests pass. New: `ExplodingPiecesTests` (on a 36³
  grid over the scout, every point inside it is in exactly one piece and every point outside in none, and no piece reaches beyond it;
  each eye and hand piece is the whole eye or hand and nothing of the stem, its cell clear of both; the model meshes as
  `amanita_scout`/`eye`/`hand` and a piece as one mesh), a part inside a part, the `surface` setting and its errors, and thickness along
  a cut measured on the part.
- Unity 6000.3.0f1 batch mode on this worktree: the scene builds and the snapshots run with no `error CS`, no C# warnings in the new
  code and no shader errors. Snapshots looked at: lightning (two flickers differ, forks, impact flash and sparks), tracers (flash
  star, orange/cyan bands, dust and sparks on the backstop, sniper trail), goop (gobs in the arc, squash, lobed and star puddles with
  drops, still there at 2 s), the burst at 0.02 / 0.05 / 0.25 / 0.6 / 1.4 / 2.7 s and the pop back, and everything at once.
- WebGL build (`WebBuild.Build`, `-buildTarget WebGL`) of all three scenes succeeds with no shader errors (26 MB, 92 s), `SdfEffect`
  compiled for WebGL 2; not run in a browser. The project was switched back to macOS afterwards.
- Frame 0: the pieces in place against the intact scout, without the flash: 3,061 of 1.44M pixels differ by more than 24 levels
  (0.2 %), as hairline seams on the cap and slightly different shading on one eye (the pieces are meshed at detail 0.5).
- Cost: the effects' streaks and balls are one draw (a bolt is about 70 quads, a spore cloud 48; the buffer holds 4,096). Each gob or puddle in use is a
  toon draw and an outline draw, so the blob gun's worst case is about 2 × 23; the burst is 2 × 14 for its 3 seconds. Nothing is
  instantiated or allocated per shot (the IMGUI panel allocates its labels, as IMGUI does).

To check by hand:

- Play mode in the editor: keys, buttons, the loop, the feel of the timings; the lightning's strobe and the minigun burst in motion.
- The web build in a browser, and frame time on the web preset with all effects running.
- Whether the puffs read as smoke: they are lit discs with an ink ring; seen against the sun they are mostly in the lit band.

After merging main (06's part instances):

- The scout's eyes and hands, inside the `amanita_scout` part, are now instanced parts (one mesh each, a node per side): part uses
  are found through other parts too. A part with no mesh of its own (only inside an intersection or as a cutter) is skipped without a
  warning, so the pieces mesh quietly. Instanced parts' thickness and occlusion follow the `surface` setting at their first placement.
- Frame 0 is a little further from the intact scout (4,419 pixels over 24 levels, 0.3 %): the intact scout's eye instances have LOD
  groups of their own, sized by the eye, so from a few metres they show LOD2, whose pupil rings simplify into a cross; the pieces'
  eyes (synced to the whole scout's group) show LOD0. That is 06's per-part LOD rule; small rings like the eye's may want a lower
  error or `Detail` there.

Notes and what remains:

- The other mushrooms have no pieces yet: a piece set is a part with the model's shape, a core (if it has floating bits) and the
  cell files; the generator could become an `sdf` command if more are wanted.
- The goop sticks to whatever its raycast hits, but does not follow a moving target, and puddles on a slope do not slide.
- Tracer and lightning targets are colliders found by raycast; a real weapon system would hand the effects its own hit results.
- `SpecialEffects.unity` is rebuilt only from the menu or the batch method, not on every model import like the showcase.
