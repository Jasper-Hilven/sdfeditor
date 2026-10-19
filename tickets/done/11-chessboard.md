# 11 — Chessboard level

A second level, `content/levels/chessboard.level`, built like the meadow: one huge board of 20 × 20 squares of 8 m (160 m × 160 m,
centred on the origin), flat, in "anarchy chess": mushroom chess pieces of both colours and every kind stand and lie all over it,
off their squares, in scrums, a queens' party, a rout, a knights' brawl, a broken pawn line and strays. Round the board the island
rises into grassy hills like the meadow's; beyond them only sky. Scene: `Assets/Scenes/Chessboard.unity`.

Depends on: 01–03 (ground bake, regions, placements, scatter), 07 (sky). Game pieces from other
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
- **Unity**: `LevelBuilder.BuildAllInBatch` builds `Chessboard.unity` (builder version 5 then; with the default day sky then, now dusk); the scene is in the
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

## Done later

- **Sky**: the level has `sky preset=dusk` (`content/skies/dusk.sky`: a low warm sun in the west, a violet sky with the first stars, a few
  floating caps, spores from the abyss). Chosen over `day` (flat and dull on the cream and brown squares) and `night` (it belongs to the
  meadow, and the eye moon would compete with the pieces). The toon bands keep their daylight brightness, so the squares read well;
  the sun is low, so every piece throws a long shadow across the squares. Seen in the snapshots: banded violet-to-rose-to-amber sky
  over the hills, stars and a floating cap in it, the abyss dark violet below the island's edge.
- **Straight square edges**: the wobble was not the region map. Its distance values are exact for straight edges (bilinear filtering
  of a linear distance), but the ground shader moved every region edge by a fixed 0.06 m of noise (`RegionEdgeNoise` in
  `LevelBuilder`). Each region now has a `wobble` parameter on its line in the level file (metres, default 0.06, 0 for ruled edges); the
  baker writes it per channel into `level.json`, `LevelBuilder` (builder version 7) puts it into the material (`_GroundRegionWobble`)
  and `SdfGround.shader` scales the noise by it. `rim`, `light` and `dark` of the chessboard have `wobble=0`; the meadow is unchanged.
  Test `ContentChessboard_SquareEdges_AreStraightInTheRegionMapAndNotWobbledByTheShader`: all wobbles are 0, and the bilinearly
  filtered region map crosses 0 within 1 cm of every square edge tested (the edges at 0, ±8 and ±16 m, along both axes).
- **Game pieces**: placed in [12](12-game-pieces.md) (37 pieces, 20 models); overlaps of game and chess pieces are checked by
  its tests.
- **Snapshot views for the chessboard** (`LevelSnapshot`): the foliage and wind shots stand at the board's edge and look out at the
  hills (they showed 0 instances before: now 47 at the high preset, 24 at the web preset); a new `eye_corner_in` looks across the
  board from the corner.
- **Rebaked and rebuilt**: `sdf level content/levels/chessboard.level --into Unity/Assets/Level/Generated/chessboard` (100 chunks, 130
  placed models, 2,807 scattered) and `LevelBuilder.BuildAllInBatch`, with no `error CS` and no shader error. The user's editor had
  the project open, so the build and the snapshots ran in an APFS clone of `Unity/`, and the scene, materials and metas were copied back.
- **Looked at** (1920 x 1080, eye height 1.5 m, `/private/tmp/claude-501/ticket11/`, not in the repo): `edge_eye_c.png` and
  `edge_eye_along.png` show the square edges ruled straight from a few metres to the horizon, with no wobble, also where four squares
  meet; `sky_west.png`, `sky_up.png`, `edge_eye_a.png` show the sky; `board_high.png` shows the whole board from a corner at 40 m;
  `knight_side0.png` and `level/` hold the rest. The knight's neck (known problem 2) shows no thin artefact from the side at 5 m: the
  head turns into the stem as one smooth form, with an olive gill patch on the back of the head. Not looked at from other angles.

## Not done

1. **Draw calls of the placed pieces on the web preset** (will not yet do this, scope change by the user). Each piece is its own scene
   object with one body renderer plus the outline, so the 130 pieces are about 260 renderers before culling, against ≤ 200 draw calls on
   the web preset. Plan for later: measure in the web build; if over, draw the pieces through the instanced renderer like the castle
   parts (`draw` group per model), or merge per cluster.
2. **Triangle budget over the whole board** (will not yet do this): the worst view (from a corner across the board) on both presets;
   the pieces' LOD distances were not tuned.
3. **Web build** (will not yet do this): not run for this level; goes with the budget parts above.
4. **Gameplay by hand** (for a human): walk the board in Play mode, check that the pieces' convex colliders stop the player as the
   pieces look (a fallen piece's hull may block more than it shows) and that the player cannot leave over the rim and hills.

## Known problems

1. **Placements overlap** in places (the scrum, the queens' party): pieces sink into each other. Intended as chaos; game pieces are
   kept off the chess pieces by 12.6's test. Whether a piece blocks the player in a way that looks wrong goes with the Play-mode check.
2. **Knight's neck**: see above, no artefact seen from the side; a human may look from the other angles in Unity.

## Acceptance

- The level has a sky; the game pieces of 12 are placed. (Met.)
- The web preset stays within ≤ 200 draw calls and ≈ 500k triangles from any spot on the board; the high preset within ≈ 2M. (Will not yet do this.)
- Square edges are straight at eye height. (Met.)
- The player can walk the board and is stopped by the pieces' colliders as they look. (Left for a human, in Play mode.)
