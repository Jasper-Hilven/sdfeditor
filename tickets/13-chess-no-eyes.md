# 13 — Chess pieces without eyes and hands

The 19 chess pieces of ticket 11 are turned mushrooms with floating eyes and hands. They should be plain: a chess piece is a
piece, not a creature. Remove the eyes and hands from all 19 and keep everything else (shape, materials, collider, base).

Depends on: 11. Independent of 12 and 06 (12 places game pieces in `chessboard.level`; this ticket does not touch the `place` block).

## What changes

- `content/models/chess/*.sdf` (19 files: pawn, rook, knight, bishop, queen, king in light and dark, and the fallen variants): the
  two `SymmetryX` blocks holding `Part part=eye` and `Part part=hand` are deleted, and the header and `Detail` comments that
  mention eyes and hands are adjusted. Nothing else in the files changes.
- **Parts and materials stay.** `content/parts/eye.sdf` and `hand.sdf` and the `eye_*` materials are still used by
  `models/sky/eye-moon`, `models/characters/chanterelle-scout`, `models/games/monopoly-hotel`, `models/games/card-ace-held`, and the
  parts `meeple` and `amanita_scout`. Nothing is removed (13.3 re-checks this).
- Known problem 2 of ticket 11 (eyes meshed in place across the SymmetryX mirror) disappears with the eyes; the pieces become
  one body mesh each, fewer renderers per piece (helps 11's draw-call item).
- Re-mesh the 19 pieces at 256 cells, rebuild prefabs and scenes.

## Subtickets

| # | Subticket | Depends on |
|---|---|---|
| 13.1 | [Edit the light pieces](13.1-edit-light-pieces.md) | - |
| 13.2 | [Edit the dark pieces](13.2-edit-dark-pieces.md) | - |
| 13.3 | [Audit parts and materials, add a test](13.3-audit-and-test.md) | 13.1, 13.2 |
| 13.4 | [Re-mesh the 19 pieces](13.4-remesh.md) | 13.1, 13.2 |
| 13.5 | [Rebuild Unity prefabs, scenes and snapshots](13.5-unity-rebuild.md) | 13.4 |
| 13.6 | [Update the text of 11 and 12](13.6-update-docs.md) | - |

## Acceptance

- No file in `content/models/chess/` contains `part=eye`, `part=hand`, `eye_` or a "floating eyes/hands" comment.
- Each piece looks as before without the eyes and hands (`sdf render`); collider and base unchanged.
- The 19 pieces are re-meshed (256 cells, 3 LODs), the mesh log has no "meshed in place" warning for them; LOD0 triangles per piece
  are lower than before.
- Prefabs and `Chessboard.unity` are rebuilt, no `error CS` or shader errors in the Unity batch log; `dotnet build` and `dotnet test` pass.

## Status

Not started.
