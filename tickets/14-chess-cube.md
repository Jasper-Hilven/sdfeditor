# 14 — Chess cube: one theme, no mushrooms

The chessboard level (`content/levels/chessboard.level`, `Assets/Scenes/Chessboard.unity`) becomes a pure board-game scene with
nothing of the mushroom meadow left in it. The board is no longer on a floating island ringed by grassy hills: it is the floor of a
closed cube of six chessboards, four standing as walls round it and one as the ceiling. Every piece on it, chess and other games,
loses its mushroom features, its floating eyes and its floating hands.

Depends on: 11, 12, 13 (all done). Touches the same level file and models; nothing else in flight.

## The rule behind it

**Each scene, players aside, has exactly one theme.** The meadow is mushrooms; the chessboard is board games. A scene's ground,
borders, sky, props and pieces all belong to its theme; only the players (the mushroom characters) may come from another. Add this
rule is in the constraints in `tickets/README.md`.

## What changes

### 1. The cube

- **Floor**: the board as it is: 20 × 20 squares of 8 m, 160 m square, centred on the origin, corners dark.
- **Walls**: four vertical chessboards of 20 × 20 squares of 8 m, standing on the floor's four edges, 160 m tall, so the room
  is a 160 m cube. Their squares continue the floor's: the colour alternates across the corner where wall meets floor and where
  two walls meet, as on the faces of a cube painted with one checker pattern.
- **Ceiling**: a sixth chessboard at y = 160 m, facing down, the same pattern.
- **Remove**: the rim region (or make it the edge where floor meets walls, if a frame is still wanted), the hills, the island
  edge and its rocky underside, the grass and flower `scatter` blocks, the `draw foliage` block, and the abyss and floating sky
  dressing (spores, motes, floating caps). Nothing outside the cube is ever seen.
- **How to build walls and ceiling** is for this ticket to settle; the ground bake today is a heightfield and cannot make
  vertical faces. Options, roughly from least to most work:
  1. Walls and ceiling as large flat quads in Unity drawn with the ground's material/region map idea (a checker in the shader from
     world position, as `SdfGround.shader` already draws the floor's squares), built by `LevelBuilder` from a new level line, e.g.
     `room size=(160, 160, 160)`.
  2. Walls and ceiling as models (`.sdf`, painted squares flush on a slab as the cards do), placed by the level. Check first that
     a 160 m slab with 400 straight-edged squares meshes cleanly at 256 cells; the cards' 8-sided spots suggest it will not.
  3. Generalise the ground bake to more than one plane.
  Whatever is chosen: square edges straight at eye height (as 11 made the floor's), colliders on all six faces so nothing can
  leave the room, and the same toon look and ink line as the floor.
- **No sky dome**: the view is surrounded by the chessboards on every side, so the scene needs no sky at all: no sky dome,
  stars, sun disc, abyss or sky meshes (`chessboard Sky*` materials and `Meshes/chessboard Sky/`). Drop the `sky preset=dusk` line
  (or replace it with a light-only setting) and make sure `LevelBuilder` then builds no sky objects for the level and the camera
  does not clear to a sky it never shows.
- **Light**: a closed cube shuts out the sun. Decide how the room is lit and write it down: a light-only preset for indoors
  (light direction and colour, toon tone, no dome, its light from overhead), with walls and ceiling not casting the directional
  light's shadows; or lights in the room. Pieces must still cast shadows on the floor. The toon bands must still read.
- **Camera**: the far plane and distance fade must reach across 160 m and up to the ceiling; check the fade does not wash the
  far walls into a fog colour from the meadow's sky.

### 2. The pieces: no mushrooms, no eyes, no hands

Every model placed in the level is redone as a plain giant board-game piece in the same toon style, keeping its size, pose,
footprint and `collider` line so the placements and the overlap test from 12.6 still hold. Materials become plain board-game
colours and finishes (lacquer, ivory, wood, felt, card): no cap, stem, gills, warts, bulb, ring or spore materials.

- **Chess pieces** (`content/models/chess/`, 19 models, light and dark, standing and fallen): today mushrooms turned into chess
  pieces (bulb foot, stem, ring, cap, gills; the king's penny-bun crown, the rook's bolete drum, the knight's cap-head and gill
  mane). Make them classic turned Staunton-style pieces: base, collar, shaft, head; the king's cross, the queen's coronet, the
  bishop's mitre with its slit, the rook's battlements, the knight's horse head with a mane, the pawn's ball. Materials:
  `materials/chess/chess_{light,dark}_{cap,stem,gills}.sdfmat` replaced by plain light and dark piece materials (e.g.
  `chess_light`, `chess_dark`, maybe a felt base).
- **Cards** (`card-ace-held`, `card-face-down`, `card-house`, `card-king-stuck`, `card-three-lying`; parts `card_ace`,
  `card_king`, `card_three`): the suit of mushrooms becomes a real suit (hearts, spades, diamonds or clubs), the king a real
  double-headed king, the back a plain card-back pattern, not the fly agaric's red with warts. `card-ace-held` loses its floating
  eyes and hands; it then needs another way to stand (propped, stuck in the board like `card-king-stuck`, or leaning on something)
  or it becomes a different pose.
- **Monopoly houses and hotel** (`monopoly-house`, `monopoly-house-toppled`, `monopoly-hotel`; parts `monopoly_house`,
  `porthole_window`): the real game's pieces: a green house and a red hotel with gabled roofs, no cap, gills, porthole windows,
  chimney mushroom or eyes. Painted or flush windows and a door are fine if they stay in the board-game look.
- **Dice** (`die-amanita`, `die-amanita-tumbled`, `die-porcini`, `die-d8-webcap`; part `die_pips`): plain dice in plain colours
  with sunk or painted pips, no warts; the porcini family on `die-porcini` goes. Rename the models (e.g. `die-red`,
  `die-red-tumbled`, `die-ivory`, `die-d8`) and update the level and tests.
- **Dominoes** (`domino-*`; parts `domino_*`): plain ivory or black tiles with sunk pips and a plain divider; no mushroom-cap pips
  or cap rivet.
- **Meeples** (`meeple-standing`, `meeple-fallen`; part `meeple`): the plain wooden meeple shape in one colour; no hat, gills or
  eyes.
- **Poker chips** (`poker-chip-rolling`, `poker-chip-stack`; parts `poker_chip_red`, `poker_chip_blue`): the mushroom in the
  middle of each face becomes a plain inlay (a ring, a star, a value); the colours stay.
- **Shared parts stay** where other scenes use them: `content/parts/eye.sdf`, `hand.sdf`, the `eye_*` materials and the mushroom
  materials are used by the meadow's characters. Only drop what nothing else uses (check with grep before deleting a part or
  material).
- Re-render each changed model (`sdf render`, the `.png` next to it) and re-mesh at **256 cells, 3 LODs**, never 512.

### 3. Rebuild

Rebake the level (`sdf level ... --into Unity/Assets/Level/Generated/chessboard`), rebuild the prefabs and `Chessboard.unity`
in batch mode, take snapshots (eye height in the middle of the board, at a corner looking along a wall, looking up at the
ceiling, a crowd of pieces close up) and check them by eye. Update the level file's header comment and the README's ticket table.

## Out of scope

- Measuring the triangle and draw-call budget ("will not yet do this", as for 05, 11 and 12.7). Note the counts the build prints.
- The meadow, its castles and its characters.
- New gameplay.

## Subtickets

| # | Subticket | Depends on |
|---|---|---|
| 14.1 | [Level format and bake for the room](14.1-chess-cube-level-format.md) | - |
| 14.2 | [The room in Unity: walls, ceiling, light, camera, no sky](14.2-chess-cube-unity-room.md) | 14.1 |
| 14.3 | [Chess pieces, light, plain materials](14.3-chess-pieces-light.md) | - |
| 14.4 | [Chess pieces, dark](14.4-chess-pieces-dark.md) | 14.3 |
| 14.5 | [Cards](14.5-cards.md) | - |
| 14.6 | [Monopoly houses, hotel, meeples](14.6-monopoly-and-meeples.md) | - |
| 14.7 | [Dice, dominoes, poker chips](14.7-dice-dominoes-chips.md) | - |
| 14.8 | [Re-mesh, rebake, rebuild, snapshots](14.8-chess-cube-rebuild.md) | 14.1-14.7 |

## Acceptance

- `Chessboard.unity` shows a closed cube of six chessboards; from anywhere inside, nothing of the island, hills, grass, flowers,
  abyss or open sky is seen, and nothing can leave the room.
- No piece in the level has a mushroom feature, a floating eye or a floating hand; no material it uses is a mushroom material.
- Square edges are straight at eye height on floor, walls and ceiling.
- The room is lit so the toon bands read and the pieces cast shadows on the floor.
- The placement overlap test from 12.6 passes; `dotnet build` and `dotnet test` pass; Unity batch mode builds with no compile or
  shader errors.
- No sky dome or sky objects are built for the scene.

## Status

Not started.
