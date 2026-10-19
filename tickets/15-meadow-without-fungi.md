# 15 — Meadow without fungi

The meadow becomes a meadow and nothing else: grass, flowers, birches, pines, fruit and old trees, rocks, a path, and a plain
**stone castle** on flagstones at each end, under a day sky. No mushroom castle, no giant mushrooms, no eye-moon, no spores or
floating caps. Everything fungal moves to its own level, ticket [14](14-fungal-level.md).

The mushrooms stay as the **cast**: the characters are mushrooms in every level, so a mushroom scout on the meadow is fine. The
levels themselves are each one thing: a plain meadow, a mushroom chessboard (11–13, unchanged), the fungal level.

Depends on: 14.2 (the castle's wall material changes there; the swap here makes the meadow independent of it). Runs in parallel with
the rest of 14.

## What changes in `content/levels/meadow.level`

- `castle0` → `castle1` (below), same places, yaw, `ground=tiles groundMargin=2.5 flatten=22`. The tiles stay: they fit a stone
  castle.
- The 12 giant-mushroom placements (`giant-amanita0`, `giant-porcini0/1`, `giant-amanita-cluster0`) are removed; 14.6 places them
  in the fungal level. Where they stood, the field stays open (or a few more rocks), so the field is not left with gaps that look
  like something is missing: check the snapshots.
- `sky preset=night` → `sky preset=day` (no eye moon, abyss spores or floating caps). Check the toon bands, outlines and the
  island's edge under the day sky in snapshots; if the open abyss under the island looks bare by day, note it, do not fix it here.
- Comments: the header and the `place` section's comment no longer speak of mushrooms.
- Everything else (regions, scatter, trees, clusters, rocks, draw sections) stays as it is. The grass, flowers, trees and rocks were
  checked: none of them holds a mushroom (only `flower0`'s comment mentions mushrooms; reword it).

## `castle1`: the stone castle

A plain toon castle of dressed stone, the counterpart of the fly agaric hold. Same footprint and the same **layout, floor heights
and walks** as `castle0`, so ticket 09's floor rules, the walk tests and `CastleChecks` hold for it unchanged.

- Model `content/models/buildings/castle1.sdf`, laid out like `castle0.sdf` (same bay positions, joints, towers, keep, gate and
  stairs), with its own parts in `content/parts/castle_stone/`, named `stone_*` (`stone_wall_bay`, `stone_wall_gate`,
  `stone_wall_joint`, `stone_tower_large`, `stone_tower_small`, `stone_gate_turret`, `stone_keep`, `stone_stair`, `stone_merlon`).
  The fly agaric parts in `content/parts/castle/` are not changed.
- Forms: straight walls with a batter at the foot instead of a stem's bulb; round towers with conical roofs instead of caps; square
  merlons; the keep a square tower; stairs of stone steps along the keep instead of shelf fungi. The walk heights (5 m, 5.15 m,
  5.3 m) and the 15 cm steps between overlapping floors are copied exactly.
- Materials: `castle_stone.sdfmat` (exists, unused) gets `pattern stones size=0.55 grout=0.035`, the pattern `castle_wall` had
  before 14.2; `castle_roof.sdfmat` (exists, unused) for the roofs; a new `castle_wood.sdfmat` for the gate. Rewrite their comments
  for this castle.
- Details: window frames and lanterns as stone or iron variants (`stone_window_frame`, `stone_lantern`; the current ones use
  `amanita_stem`); no mycelium, young amanitas, veil or wall shelves. Optionally banners; skip if it costs a day.
- Same instancing and drawing as `castle0`: every repeated part meshed once and drawn per use (only rigid placements above `Part`
  uses), the `draw castle` section unchanged, colliders `mesh`. Meshed at 128 cells with 3 LODs, as `castle0`'s parts.
- Budgets: no more triangles or draw calls per view than `castle0` in the same snapshots.

## Tests and checks

- `ContentMeadow_IsAValidLevel` (`SDFPipeline.Tests/Levels/LevelFileTests.cs:108`): the castles are `castle1`.
- `ContentMeadow_HasNoFungi`: no model the level names comes from `content/models/mushrooms/`, `fungi/`, `chess/` or is `castle0`,
  and no material those models use is an `amanita_*`, `game_*` or `chess_*` one; the sky is not `night`.
- A test that `castle1` keeps `castle0`'s floor heights (the walk and ring heights per part), so the two cannot drift apart.
- `CastleChecks` runs on the meadow's `castle1` (floor audit, walks, z-fight views): no regressions against `castle0`'s results.
- Re-bake the meadow, rebuild `Meadow.unity`, and take the level snapshots and the castle snapshots. The showcase (`Mushrooms.unity`)
  gets `castle1` next to `castle0`.
- Unity batch logs clean; `dotnet build` and `dotnet test` pass.

## Subtickets

| # | Subticket | Depends on |
|---|---|---|
| 15.1 | `castle1` parts and model, materials, render | - |
| 15.2 | Mesh `castle1`, floor-height test, `CastleChecks` | 15.1 |
| 15.3 | `meadow.level`: castle swap, mushrooms out, day sky; tests | 15.2, 14.2 |
| 15.4 | Rebuild Unity, snapshots; README and Status | 15.3 |

## Acceptance

- The meadow shows no fungus: stone castles on flagstones, grass, flowers, trees, rocks, under the day sky (snapshots).
- `castle1` walks and floors pass the same checks as `castle0`; no z-fighting; budgets at or below `castle0`'s.
- `castle0`, its parts and the giant mushrooms are unchanged by this ticket and used by the fungal level.
- Tests above pass; Unity batch runs clean.

## Status

Not started.
