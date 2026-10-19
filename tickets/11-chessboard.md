# 11 — Chessboard level

A second level, `content/levels/chessboard.level`, built like the meadow: one huge board of 20 × 20 squares of 8 m (160 m × 160 m,
centred on the origin), flat, in "anarchy chess": mushroom chess pieces of both colours and every kind stand and lie all over it,
off their squares, in scrums, a queens' party, a rout, a knights' brawl, a broken pawn line and strays. Round the board the island
rises into grassy hills like the meadow's; beyond them only sky. Scene: `Assets/Scenes/Chessboard.unity`.

Depends on: 01–03 (ground bake, regions, placements, scatter), 07 (sky; the level has no `sky` line yet). Game pieces from other
games go on the same board in 12.

## Done

- **Level file** `content/levels/chessboard.level`: `play size=(160, 160) noise=0`; hills `ring width=28 height=10`; edge margin 6.
  - Regions: `rim` (166 m box, a 3 m frame), `light` (the whole board), `dark` (200 boxes of 8 m, (i + j) even, the corners dark).
    Materials `materials/ground/ground_{dark,light,rim}.sdfmat`.
  - `place`: 93 chess pieces in clusters (see the comments in the file); scale 0.85–1.2, any yaw.
  - `scatter`: grass turfs and flowers only on the hills, up to 12 m out (`avoid=rim,light,dark`); `draw foliage` as the meadow's.
- **19 chess pieces** in `content/models/chess/`: pawn, rook, knight, bishop, queen, king in light and dark, and seven fallen
  variants (`*-fallen`). Each is a turned mushroom (cap = the piece's head), `collider convex`, a root
  `Detail detail=0.5` (big smooth forms, many copies). Heights 2.7 m (pawn) to 5.6 m (king). Materials `materials/chess/chess_{light,dark}_{cap,gills,stem}.sdfmat`.
- **Meshed** at the `Pipeline to Unity` settings (`--resolution 512 --lods 3`) into `Unity/Assets/Mushrooms/Generated/chess/`:
  652 s for all 19 on the M4 Max. LOD0 per piece 15k (pawn) – 22k (queen) triangles (before [13](13-chess-no-eyes.md) removed the
  extra meshes); LOD1 about a fifth, LOD2 about a fifteenth.
- **Chess pieces without eyes and hands**: see [13](13-chess-no-eyes.md); the mesh and triangle figures in this ticket are from before it.
- **Baked** with `sdf level`: 100 chunks, 69,269 ground triangles at LOD0, 93 placed models, 2,807 scattered (grass and flowers on
  the hills only).
- **Unity**: `LevelBuilder.BuildAllInBatch` builds `Chessboard.unity` (builder version 5, with the default day sky); the scene is in the
  build settings after SpecialEffects. The showcase (`Mushrooms.unity`) gained a `Chess` row; its broken `Zztest` instance (10's
  known problem 2) is gone, and `MushroomSnapshot.Capture` runs again.
- **`LevelSnapshot`**: each eye shot also logs the placed models' triangles (`TrianglesInView` per level child: `Ground`,
  `Placed`); a new `ShootCrowd` shoots the busiest 12 m spot of the placed models from eye height (front and back) and from above.
- **Test** `LevelFileTests.ContentChessboard_IsAFlatCheckerboard_WithPiecesOfBothColoursOnIt`: the squares' colours, the rim, all
  placements on the board, every kind in both colours, each model found.
- After merging main (a3de2f8): `dotnet test` passes (533 + 317); in Unity batch mode the chessboard and meadow snapshots, the
  SpecialEffects build and snapshot and the showcase snapshot run with no `error CS` and no shader errors.

Measured (high preset, eye height): the ground 12.9k–14.5k triangles per view, the placed models 30k–87k, and 143k at
`crowd_eye_back` (looking into the scrum); web preset `eye_+x_web` 8.8k ground + 68k placed.

## Not done

1. **A sky.** The level has no `sky` line, so it gets the default day light. Pick a preset (or a new one) that suits the board.
2. **Game pieces** from other games (ticket 12) in the `place` block; the placeholder comment there marks the spot.
3. **Draw calls of the placed pieces on the web preset.** Each piece is its own scene object with one body renderer (plus
   the outline), so 93 pieces are about 190 renderers before culling, against ≤ 200 draw calls on the web preset. Measure in
   the web build; if over, draw the pieces through the instanced renderer like the castle parts (`draw` group per model), or merge
   per cluster.
4. **Triangle budget over the whole board.** Measured only in the snapshot views. Check the worst view (from a corner across the
   board) on both presets; the pieces' LOD distances were not tuned.
5. **Gameplay**: colliders were not walked against. The pieces are `collider convex`; a fallen piece's convex hull may block more than
   it shows. Check in Play mode, and that the player cannot leave over the rim and hills.
6. **Web build**: not run for this level.

## Known problems

1. **Square edges wobble** close up (seen in `eye_+x`): the region map (`regions.png`) is sampled too coarsely for straight 8 m
   squares at eye height. A finer region map, or analytic squares in `SdfGround.shader` for this level, would fix it.
2. **Knight's neck** shows a thin artefact where the head meets the stem in the toon render (side view); check in Unity close up.
3. **Snapshot views made for the meadow**: the foliage shots (`foliage_close`, `foliage_rings`, `wind`) look at the board, where
   there is no foliage, so they show 0 instances; `eye_corner` sees no pieces. Point them at the hills for this level.
4. **Placements overlap** in places (the scrum, the queens' party): pieces sink into each other. Intended as chaos, but check that no
   piece is hidden inside another or blocks the player in a way that looks wrong.

## Acceptance

- The level has a sky; the game pieces of 12 are placed.
- The web preset stays within ≤ 200 draw calls and ≈ 500k triangles from any spot on the board; the high preset within ≈ 2M.
- Square edges are straight at eye height.
- The player can walk the board and is stopped by the pieces' colliders as they look.
