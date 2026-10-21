# 19 — Team mushrooms at the fungal castles

On the fungal level each castle is dressed in its side's colour: **reddish** mushrooms on and around the west castle, **bluish**
ones on and around the east castle (ticket 18: west red, east blue). The castle model (`castle0.sdf` and its parts) is **not
touched**: the mushrooms are separate models placed by `fungal.level`.

Depends on: 17 (battle sides), 18 (the colours).

## Models

- A few small team mushroom models (e.g. 2-3 shapes per colour, or shared shapes with a red and a blue material set), in the fungal
  style (`content/models/fungi/`), toon look, LODs as the other field fungi. Red: amanita-like warm reds, not the scouts' exact cap
  red; blue: cool blues, glowing a little like the fungal level's other fungi.
- Small and decorative: no collider, or a convex one only where one is big enough to bump into. Nothing that blocks the gate, the
  spawn, the flag stand, the walkways or the stairs.

## Placement

- **Near** each castle: clusters at the foot of the walls and towers, on the plateau round the castle, at the gate's sides.
- **On top**: on the castle's tops (tower and keep rings, wall-walk edges, battlements), where they don't block the walkways. If
  `place` can only put a model's base on the ground, add a height (e.g. `y=` or `on=castle`) to `place` in `LevelFile`, with a test,
  rather than baking them into the castle.
- Mirrored placements, west red and east blue; or, if instancing suits better, a `scatter` limited to a ring round each castle.
- The battle objects, the trail and the fairy rings stay clear; `BattleSidesTests` and the level tests keep passing.

## Done when

Models meshed, `fungal.level` baked, tests pass, the scene rebuilds (Unity is open: by itself or Tools > SDF Level > Rebuild).

## Status

Done in the pipeline: models meshed, `fungal.level` baked, tests pass. Left: the scene rebuild in Unity (by itself from the bake, else
Tools > SDF Level > Rebuild) and a look there and in Play mode.

- **Parts** `content/parts/fungi_team/`: `team_red_dome`, `team_red_bell`, `team_red_button` and the same three in blue (one shape,
  two colour sets; the toon colour comes per vertex). Stems go on 10 cm below the origin so they sit on a domed cap. `collider none`,
  `shadows near`. Materials `materials/fungi/team_{red,blue}_{cap,stem,gills}.sdfmat`: red a warm coral-scarlet with a few cream
  flecks (lighter and more orange than the scouts' and castle's fly agaric red), subsurface 0.12; blue a cobalt pinkgill blue,
  subsurface 0.2 (glows a little, less than the mycena's 0.3).
- **Models** `content/models/fungi/team/`, `team-{red,blue}-`: `crown0` (6 mushrooms, 0.9-2.1 m, for cap tops), `clump0` (7, 0.3-1.2 m,
  wall foot and courtyard), `clump1` (9, 0.4-1.9 m, gate sides and plateau), `sprig0` (3, 0.2-0.4 m, 0.65 x 0.35 m, parapet and ring
  rims). Only `Part` uses under `Detail detail=0.5`, so `LevelBuilder` draws them instanced in the `castle` group; `collider none`.
  Meshed at 256 cells, 3 LODs (plain and `_256`) into `Generated/fungi/team/`. Triangles per part (LOD0/1/2): dome 1,548/380/~150,
  bell 1,012/172/92, button 828/184/82.
- **Format**: `place` takes `y=` (0 to 100, metres above the ground at `at`; `Placement.Y`, baked into level.json's position). The
  castle stands on its flat plateau, so it is the height over the castle's base. Tests: `LevelFileTests` (read, below-ground error),
  `GroundRegionTests` (bake writes ground + y), `BattleSidesTests` overlap check now counts y.
- **Placement** in `fungal.level`, 35 per castle, west red, east blue mirrored across x = 0 (heights each castle's own, from its SDF):
  7 crowns on the caps (keep 20.35 m, scale 1.3; large towers 15.7 / 14.87 m; small towers 11.89 / 12.28 m, scale 0.8; gate turrets
  9.1 / 9.26 m, scale 0.55); 10 sprigs on the parapet top between merlons (5.5 m); 4 sprigs on the large towers' ring rims (5.26-5.34 m,
  outer half, between lantern and veil); 8 `clump0` at the wall and tower foot outside; 2 `clump1` either side of the gate, 2 on the
  plateau; 2 smaller `clump0` in the courtyard against the side walls. Gate, apron, trail, spawn, flag stand, pods and stairs clear.
- `FungalTeamMushroomsTests`: red west / blue east, mirrored; lifted ones within 10 cm of the castle's surface, ground ones 0.3 m or
  more clear of it a metre up. `dotnet test SDFPipeline.Tests`: 430 pass.
- Budget: about 190 part instances per castle (380 in all), drawn instanced; 24 prototypes (parts keyed per model). Mostly LOD1/LOD2
  from across the field (~30-70k triangles); near one castle at LOD0 perhaps 100-150k more. To check in Unity against the web budget.
