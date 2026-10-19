# Tickets: the first level

The plan for the first playable level, step by step. Each ticket holds what it needs on its own; do them in order, as each builds on
the one before. Tickets with no work left move to [`done/`](done/); what is left there is only to look at by hand, as each
ticket's Status says.

| # | Ticket | Outcome |
|---|---|---|
| 00 | [URP port](done/00-urp-port.md) | Unity on URP; the toon look unchanged; a web build runs |
| 01 | [Ground](done/01-ground.md) | `sdf level` bakes a level file to terrain chunks; Unity builds the level scene from them |
| 02 | [Ground materials and castles](done/02-ground-materials-and-castles.md) | Grass, dirt, path and tiles on the ground; castles and rocks placed, with colliders |
| 03 | [Foliage and the instanced renderer](done/03-foliage.md) | Grass turfs and flowers scattered and drawn instanced, with wind; the renderer is general |
| 04 | [Hills and island](done/04-hills-and-island.md) | Instanced tree line on the border hills, crest clusters, the island's rocky underside. Done; left: walking and wind in Play mode, web frame time, a resolution rule for the 512 config |
| 05 | [Tuning](done/05-tuning.md) | Budgets met on the high and integrated-GPU/web presets. Will not yet do this |
| 06 | [Castles](done/06-castles.md) | Castle split into instanced parts with LODs per part, shader wall detail, mushroom theme. After 02, **in parallel with 03**, before 04 |
| 07 | [Sky](done/07-sky.md) | Sky presets per level from `content/skies/`; Eye-Moon Night for the meadow (eye moon, glowing abyss, spores); sky-tinted toon bands and distance fade, also in the IDE preview. After 02, in parallel with 03; its toon-shader change before 06's shader patterns; preferably before 04 |
| 08 | [Special effects](done/08-special-effects.md) | `SpecialEffects` scene: lightning, tracers, goop, exploding mushroom from SDF-intersection pieces |
| 09 | [Castle fixes](done/09-castle-fixes.md) | Cap red no longer bleeds into stems at coarse LODs; walk-level floors of neighbouring parts 15 cm apart, no z-fighting. After 06; alongside 04 or 07 |
| 10 | [Sky: remaining](done/10-sky-remaining.md) | What is left of 07: web frame time and Safari, the sky's motion checked in Play mode, the tower snapshot on the new castle, the showcase snapshot's stray object. After 07 |
| 11 | [Chessboard](done/11-chessboard.md) | Second level: a 160 m chessboard in anarchy chess, mushroom chess pieces and game pieces placed, dusk sky, straight square edges (per-region `wobble`), `Chessboard.unity` built. Left: web draw calls, triangle budget and web build (will not yet do this), a Play-mode walk |
| 12 | [Game pieces](done/12-game-pieces.md) | Mushroom dice, Monopoly houses, dominoes, meeples, chips and cards for the chessboard level: modelled, meshed, placed, `Chessboard.unity` rebuilt (subtickets 12.1-12.7). Left: the triangle budget with the 37 pieces (will not yet do this) and a Play-mode look. After 11 |
| 13 | [Chess without eyes](done/13-chess-no-eyes.md) | The 19 chess pieces lose their floating eyes and hands; re-meshed, prefabs and scenes rebuilt. After 11 |
| 14 | [Chess cube](done/14-chess-cube.md) | The chessboard is one theme, board games: a closed cube of six chessboards (floor, four walls, ceiling), no hills, island, foliage or sky; every piece plain, without mushrooms, eyes or hands (14.1-14.8). Left: Play-mode walk, lamp tone by eye, budgets (will not yet do this). After 11, 12, 13 |
| 14 | [Fungal level](14-fungal-level.md) | Third level, `fungal.level` / `Fungal.unity`: the meadow's island where everything that grows is a fungus (mushroom trees, moss, fungi tufts, mycelium ground); `castle0` fully fungal, no stone pattern, no tiles; Eye-Moon Night. Not started. After 01–09 |
| 15 | [Meadow without fungi](15-meadow-without-fungi.md) | The meadow with no fungi: a new stone castle `castle1` on the tiles, the giant mushrooms out, the day sky. Not started. After 14.2 |

## The level

- A 150 m × 100 m playable area of grass on a **floating island**; beyond it only sky.
- Two **castles**, one on each short side, walkable inside, standing on dirt or tiled ground.
- The borders are **hills covered in trees** that cannot be climbed.
- The ground foliage is made of the existing grass turfs (`content/models/grass/`) and flowers (`content/models/flowers/`).
- **First-person** camera at about 1.5 m eye height.

## Constraints that hold for every ticket

- **Models are SDF programs** (`.sdf`) baked offline by `SDFPipeline` (`sdf mesh`, `sdf render`). The game runtime only draws
  meshes: no live raymarching.
- **Unity is the one runtime**, on **URP** (ticket 00), built for Mac, Windows and the web (WebGL2; WebGPU later maybe). Nothing in
  the runtime may need compute shaders, as WebGL2 has none.
- **Toon look only** in Unity (`SdfToon.shader` + `SdfToonOutline.shader` ink line); every material's data comes per vertex. The
  realistic look is gone.
- **Data lives in the repo** as text, like models: the level is a file in `content/levels/`, baked by the CLI into a generated folder
  that Unity reads. Unity never holds hand-made level data.
- **The level is its own static Unity scene** (`Assets/Scenes/Meadow.unity`), next to the `Mushrooms.unity` model showcase, rebuilt
  from the baked data. The `Pipeline to Unity` run configs (all three) update it along with the models.
- **Generated files are not in git**: the `.glb` meshes, mesh `.asset`s, level bake output and their `.meta` files are ignored. A fresh
  clone runs `sdf unity` (the `Update to Unity (256)` run config) before opening Unity, which then rebuilds prefabs and scenes.
- **Procedural only**: heights and ground masks come from 2D SDF shapes and noise, no hand-painted maps.
- **Presets**: *high* (dev machine, M4 Max) and *integrated GPU / web* (min-spec). There is no integrated-GPU test machine; the low
  preset is profiled on the M4 Max and in the web build.
- **Instancing**: everything the level holds in quantity (grass, flowers, trees, crest clusters) is baked as instance data and drawn
  by one instanced renderer, so draw calls scale with the number of variants, not of objects. Only unique models (castles, big
  rocks) are scene objects.
- **One theme per scene**: each scene, players aside, has exactly one theme. The meadow is mushrooms; the chessboard is board
  games. Ground, borders, sky, props and pieces all belong to the scene's theme; only the players may come from another.
- **Budgets** (per frame): high ≈ 2M triangles; integrated GPU / web ≈ 500k triangles, ≤ 200 draw calls.
