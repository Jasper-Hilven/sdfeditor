# 12 — Game pieces: meshing, Unity check and placement

Giant mushroom-themed pieces from other games (dice, Monopoly houses and hotels, dominoes, meeples, poker chips, playing cards)
for the chessboard level, an "anarchy chess" board in chaos. The models and materials are written and checked in the toon render;
what is left is to mesh them into Unity at a bearable cost, check them there, and place them in the level.

Depends on: 11 (the chessboard level, `content/levels/chessboard.level`: 20 × 20 squares of 8 m, 160 m square, centred on the
origin; merged in, with main's part instancing).

## Done

- 20 models in `content/models/games/`, each with a `collider` line, base on y = 0, sizes 1.5–7 m:
  - dice: `die-amanita`, `die-amanita-tumbled`, `die-porcini` (porcini sprouting from it), `die-d8-webcap`;
  - Monopoly: `monopoly-house`, `monopoly-house-toppled`, `monopoly-hotel` (with floating eyes);
  - dominoes: `domino-6-3-lying`, `domino-2-6-side`, `domino-5-5-standing`, `domino-chain` (three toppling);
  - meeples: `meeple-standing`, `meeple-fallen`;
  - poker chips: `poker-chip-stack`, `poker-chip-rolling`;
  - cards: `card-ace-held` (floating hands and eyes), `card-king-stuck`, `card-three-lying`, `card-face-down`, `card-house`.
- Parts in `content/parts/`: `die_pips`, `porthole_window`, `monopoly_house`, `meeple`, `domino_6_3`, `domino_5_5`, `domino_4_1`,
  `domino_2_6`, `poker_chip_red`, `poker_chip_blue`, `card_ace`, `card_king`, `card_three`.
- 13 materials in `materials/games/` (`game_*.sdfmat`); the models also use `amanita_cap`, `amanita_stem` and the eye materials.
- Card faces, chip faces and card backs are painted flush, no textures: the shape cut to a half-space and to 2D shapes, the later
  piece of a union winning where two lie on the same surface (`Union` gives ties to the later operand).
- `dotnet build` and `dotnet test` pass, also after merging 11 and main.

## Meshed

All 20 models meshed at 256 cells with 3 LODs into `Unity/Assets/Mushrooms/Generated/games/` (`sdf mesh <model> <glb> --resolution 256
--lods 3`, each 1.9 to 5.3 s alone, no warnings left). Triangles are drawn per LOD with every instance counted; "meshes" is the
number of different meshes in the file (an instanced part counts once). Read from the glb files. Largest: `monopoly-hotel` at
51k, so no model is over 60k at LOD0.

| Model | Meshes | LOD0 | LOD1 | LOD2 | glb |
|---|---|---|---|---|---|
| card-ace-held | 3 (card, eye x2, hand x2) | 18,424 | 5,526 | 3,380 | 2.09 MB |
| card-face-down | 1 | 14,740 | 4,786 | 2,820 | 1.99 MB |
| card-house | 2 (king, three) | 35,186 | 10,378 | 5,512 | 4.55 MB |
| card-king-stuck | 1 | 19,886 | 6,100 | 3,070 | 2.58 MB |
| card-three-lying | 1 | 12,624 | 4,588 | 2,738 | 1.77 MB |
| die-amanita | 1 | 6,598 | 1,670 | 1,088 | 0.84 MB |
| die-amanita-tumbled | 1 | 5,876 | 1,492 | 882 | 0.74 MB |
| die-d8-webcap | 1 | 3,220 | 734 | 412 | 0.40 MB |
| die-porcini | 1 | 8,106 | 1,956 | 996 | 0.99 MB |
| domino-2-6-side | 1 | 4,012 | 714 | 322 | 0.48 MB |
| domino-5-5-standing | 1 | 3,324 | 698 | 324 | 0.43 MB |
| domino-6-3-lying | 1 | 3,238 | 704 | 316 | 0.41 MB |
| domino-chain | 3 (three dominoes) | 11,132 | 2,036 | 872 | 1.29 MB |
| meeple-fallen | 2 (meeple, eye x2) | 6,918 | 1,714 | 762 | 0.64 MB |
| meeple-standing | 2 (meeple, eye x2) | 6,544 | 1,670 | 742 | 0.60 MB |
| monopoly-hotel | 3 (body, eye x2, porthole x13) | 50,982 | 14,050 | 6,430 | 1.93 MB |
| monopoly-house | 2 (house, porthole x4) | 19,092 | 4,838 | 2,194 | 1.38 MB |
| monopoly-house-toppled | 2 (house, porthole x4) | 19,146 | 5,084 | 2,266 | 1.39 MB |
| poker-chip-rolling | 1 | 9,924 | 2,894 | 1,166 | 1.24 MB |
| poker-chip-stack | 2 (red x3, blue x2) | 47,210 | 11,640 | 4,370 | 2.25 MB |

Findings of the part split check (12.4):

1. **`die_pips` is not a separate mesh in any die.** In `die-amanita`, `die-amanita-tumbled` and `die-porcini` the part sits inside
   an `Intersection` with a box under the die's `SmoothUnion`, so the mesher keeps it in the body: one body mesh per die, pips
   filleted into the faces, nothing overlapping. A toon-style `sdf render` close-up of `die-amanita` shows the pips as smooth
   domes grown out of the faces, not loose or doubled, so nothing was inlined. `die-d8-webcap` has no pips part.
2. **Instanced parts.** `poker-chip-stack` meshes each chip colour once (red x3, blue x2); `monopoly-hotel` instances 13
   portholes and 2 eyes, `monopoly-house` and `-toppled` 4 portholes each; `card-ace-held` 2 eyes and 2 hands. The portholes
   and eyes that stand on both sides of a `SymmetryX` do not cross the mirror, so they instance. The one crossing was in the
   `meeple` part: its eyes at x = +-0.15, scaled 1.5 (radius 0.18 m), reached over the mirror, so both meeples warned and met their
   eyes as one mesh. Fixed by moving the eyes to x = +-0.19 in `content/parts/meeple.sdf` (the eyes now touch rather than overlap
   by 6 cm; the render looks the same but for a hair of space between them); `meeple-standing` and `meeple-fallen` re-meshed at
   256, no warning, eyes instanced twice.
3. **Painted models are one mesh.** Every card model has one mesh for the card (`card-house` has two different cards, not on one
   surface; `card-ace-held` adds eyes and hands), so no back lies on a face. Chips are one mesh per chip, as are the dominoes; the
   chips of a stack are separate, offset pieces. No coincident second mesh anywhere.
4. **Triangles.** Table above; no model over 60k at LOD0.

## Subtickets

Run 12.1 to 12.3 in parallel, then 12.4; after it 12.5 and 12.6 in parallel; 12.7 last.

- [12.1](done/12.1-mesh-cards.md) mesh four card models at 256
- [12.2](done/12.2-mesh-dice-houses-chips.md) mesh the tumbled die, Monopoly pieces and chips at 256
- [12.3](done/12.3-remesh-existing-at-256.md) re-mesh the ten 512 models at 256
- [12.4](done/12.4-part-split-check.md) part split check and triangle table (after 12.1 to 12.3)
- [12.5](done/12.5-unity-check.md) Unity prefabs, showcase and colliders (after 12.4)
- [12.6](done/12.6-placement.md) placement with overlap check and test (after 12.4)
- [12.7](12.7-budget-rebuild.md) triangle budget, rebake and rebuild (after 12.5 and 12.6)

## To do

1. **Make meshing affordable.** `sdf mesh content/models/games --into Unity/Assets/Mushrooms/Generated/games --resolution 512 --lods 3`
   was far too slow: `card-house` ran more than 30 minutes, mostly on one core (likely `QuadricSimplifier`, the one meshing step
   that is not parallel). For comparison `die-amanita` takes 95 s, of which 24 s is the separate `die_pips` mesh (843k → 22k
   triangles).
   - Time each model alone (`sdf mesh <model.sdf> <out.glb> --resolution 512 --lods 3`) and find the slow phase.
   - Likely fixes: a root `Detail` below 1 on the big flat models (cards, card house, dominoes, hotel), which are simple forms with
     many material borders; fewer 2D primitives in the card faces (the king has about 60).
2. **Check the part split.** `PartSplitter` meshes every `Part` that adds to the shape as its own mesh. Since main's part instances
   (merged with 11), a part used more than once in a model through rigid placements only (Translate, Rotate, uniform Scale,
   SymmetryX/XZ with the part on its side of the mirror) is meshed once in its own file's space and drawn per use as an instance; a
   part used once is still meshed in place, as before. What that means here (from the rules, not yet seen in a mesh log):
   - `die_pips` is used once per die, inside a `SmoothUnion`, so it is still a separate mesh made in place: the fillet between body
     and pips is lost and the two meshes overlap. Check this looks right in Unity; if not, inline the pips.
   - The domino parts and the cards' parts are each used once per model (`domino-chain` uses three different dominoes), so they mesh
     in place, as before.
   - `poker-chip-stack` uses `poker_chip_red` three times and `poker_chip_blue` twice under Translate and Rotate only: each chip
     becomes one mesh with instances. `porthole_window` (eight uses in `monopoly-hotel`, four in `monopoly_house`) and the hotel's
     eyes go the same way, unless a use crosses its SymmetryX mirror: then the mesher warns and meshes all uses in place as one mesh.
     Read the warnings in the mesh log.
   - The cards were first built on `card_blank` and `card_back` parts. That split the white back into its own mesh with the painted
     face lying on the same surface, so it would z-fight. Now fixed: the blank is written out in every painted layer. The two .glb
     files meshed before the fix were deleted. Check every other painted model is one mesh too (chips: yes, no nested parts).
3. **Mesh** into `Unity/Assets/Mushrooms/Generated/games` and record the triangle counts per model and LOD.
4. **Unity check.** Run batch mode on the `Unity/` folder (`SdfMushrooms.Editor.MushroomSnapshot.Capture`, which runs again since
   11). Read the log for errors, look at the close-ups and the Mushrooms showcase, and check the colliders (`convex` above 3 m
   becomes a mesh).
5. **Place them** in `content/levels/chessboard.level`, in its `place` block, where the comment about the game elements marks the
   spot. The block below is untested: check what it overlaps (chess pieces, each other) and the triangle budget (≈ 2M high, 500k
   web; the 93 chess pieces already draw up to 143k at eye height, see 11), and drop or rescale pieces as needed. Then re-bake the
   level and rebuild `Chessboard.unity` (`SdfLevel.Editor.LevelBuilder.BuildAllInBatch`).

```
    # The game pieces from other games, strewn over the board as if the players had flipped the table: off the squares,
    # turned every which way, in spills and piles, knocked over
    # A dice spill
    die-amanita at=(-46.5, -29.2) yaw=17 scale=1.2
    die-amanita-tumbled at=(-41.3, -25.8) yaw=63 scale=1.1
    die-porcini at=(-50.8, -23.1) yaw=-34
    die-d8-webcap at=(-38.6, -31.7) yaw=112
    die-amanita-tumbled at=(28.4, 51.6) yaw=-148 scale=1.4
    die-d8-webcap at=(61.2, -12.5) yaw=41 scale=0.9
    die-porcini at=(-12.7, 68.3) yaw=203 scale=1.3
    # A crooked street of houses and a hotel, one house blown over; a second street in a far corner
    monopoly-house at=(12.6, -54.4) yaw=8
    monopoly-house at=(19.3, -51.2) yaw=-21
    monopoly-house-toppled at=(26.8, -55.9) yaw=74
    monopoly-hotel at=(37.2, -50.3) yaw=-12
    monopoly-house at=(-66.4, 44.7) yaw=135 scale=0.9
    monopoly-hotel at=(-58.1, 60.5) yaw=161
    monopoly-house-toppled at=(52.7, 27.9) yaw=-103
    # A domino run gone wrong, and stray tiles
    domino-chain at=(-20.5, -6.4) yaw=58
    domino-5-5-standing at=(-27.9, -11.3) yaw=50
    domino-6-3-lying at=(-13.2, 1.8) yaw=-24
    domino-2-6-side at=(5.6, 30.2) yaw=97
    domino-6-3-lying at=(66.9, 61.4) yaw=12 scale=1.2
    domino-5-5-standing at=(-70.3, -60.8) yaw=-37
    # Meeples, some still standing
    meeple-standing at=(44.8, -6.9) yaw=-120
    meeple-fallen at=(47.3, -1.2) yaw=31
    meeple-standing at=(-4.1, -42.6) yaw=170 scale=1.2
    meeple-fallen at=(-33.6, 36.4) yaw=-66
    meeple-standing at=(71.5, -70.2) yaw=225
    # Poker chips: two stacks and runaways
    poker-chip-stack at=(-61.8, 9.7) yaw=33
    poker-chip-rolling at=(-55.2, 14.6) yaw=-48
    poker-chip-stack at=(10.3, -21.4) yaw=-77 scale=0.8
    poker-chip-rolling at=(37.6, 70.8) yaw=125
    # Cards: a house of cards amid a spilled hand, cards thrown into the board, a card soldier on guard
    card-house at=(2.4, 49.1) yaw=27
    card-face-down at=(9.8, 55.7) yaw=-41
    card-three-lying at=(-4.3, 57.6) yaw=66
    card-king-stuck at=(-29.7, -63.4) yaw=-15
    card-ace-held at=(58.6, -38.2) yaw=-140
    card-king-stuck at=(73.6, 9.3) yaw=-95
    card-face-down at=(-73.8, -24.6) yaw=12
    card-three-lying at=(21.9, 13.7) yaw=158
```

## Notes

- Very flat ellipsoids (like 3.2 × 0.1 × 1.8) give such poor distances far away that the bounds finder framed nothing or framed
  too much; the gills are cut from a rounder ellipsoid by a thin box instead.
- `SymmetryXZ` evaluates only the +x +z quarter: a shape turned out of it, for example by a positive turn about y from +x, vanishes.
