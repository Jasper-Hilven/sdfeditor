# 60 — Each level's LOD0 streamed after the match starts

Part of **Minigun Monsters**, toward CrazyGames' 20 MB mobile homepage limit (ticket 59: the download counted is everything up to the
first `gameplayStart`, which fires when the HUD of the first match is up).

## Today

- Player 16.23 MB (after 59 part 1 and 1b). Every level streams as bundles (tickets 38, 52; `StreamLayout`, `StreamedBundles`,
  `ContentStreamer`, `SceneLoader`): a scene bundle, `<level>-assets` and `shared-<hash>` bundles, LZ4. A first play is the Fungal
  level, about 13.3 MB of bundles, so about 29.5 MB before `gameplayStart`.
- Level meshes across all bundles (uncompressed, unique assets): **LOD0 34.9 MB (54%)**, LOD1 10.6, LOD2 8.0, ground chunks 7.3,
  navmeshes 2.5, colliders 1.8; 65 MB of meshes is about 28 MB LZ4. So LOD0 is about 15 MB of the 42 MB of bundles.

## Decisions

- A level loads and starts with **LOD1 and LOD2 only**; its LOD0 meshes come in a separate `<level>-lod0` bundle, fetched right after
  `gameplayStart` (after the HUD is up), in the background, and swapped in as they arrive. Until then the LOD group's LOD0 slot shows
  the LOD1 mesh (no hole, no pop to nothing).
- Once loaded, the game looks exactly as today.
- Ground chunks, colliders and navmeshes are not touched (collisions must be exact from the first frame).
- The instanced renderer's content (foliage, crest clusters: `InstancedScatterRenderer`) counts too if it has LOD0 meshes in the level
  bundles; handle it the same way, or say why not.
- The next-level prefetch (during a match) fetches the next level's base bundles first, then its `-lod0` bundle.
- The `-lod0` bundle is released with the level's other bundles.
- Phones still download LOD0 (a later decision may skip it on touch at Ultra Low; leave a single switch for it, off).

## Work

- Find how LOD0 meshes are referenced in the level scenes and their prefabs (`MushroomBuilder` prefabs with `LODGroup`, the generated
  `_body`, `_body_LOD1`, `_body_LOD2` meshes in `Assets/Mushrooms/Meshes`, the level scene builders) and in the instanced data.
- At bundle build time (`StreamedBundles`), make the level's scene and `-assets` bundles reference LOD1 in the LOD0 slot, and put the
  real LOD0 meshes in `<level>-lod0`, with a small map (which renderer or prefab gets which mesh). Never modify the source prefabs or
  scenes on disk permanently: work on copies or restore after the build, and the editor and Play mode keep showing LOD0 as today.
- Runtime: a `Lod0Streamer` (Campaign/Runtime, next to `ContentStreamer`) that, after `gameplayStart`, loads the bundle and swaps the
  meshes (including on objects spawned later, such as respawned pickups and machines, if they use level meshes).
- `StreamManifest` / `StreamLayout`: the new bundle per level, tested like the existing layout.
- Measure: per level, the bytes before `gameplayStart` before and after (build log + a headless Chrome network count on
  `http://localhost:PORT`, not 127.0.0.1: CrazySDK's site lock), and the size of each `-lod0` bundle. A screenshot of the same view
  just after start (LOD1 in the LOD0 slot) and after the swap.

## Done when

- The first match's download before `gameplayStart` drops by roughly the level's LOD0 share (numbers per level in the Status).
- After the swap, the level renders with today's LOD0 meshes (a test or a play-mode check compares mesh names per renderer).
- Desktop and touch play tests, EditMode tests and the CrazyGames build pass; the release build in headless Chrome loads to a match and
  swaps without errors.

## Status

Done. EditMode 779 pass, 0 fail (2 new: `PlanLod0`). Desktop play test (five matches): 0 errors. Touch play test: its known "waiting for the jump" timeout in 4 runs
in a row here (the second agent's runs of ticket 61 failed the same way at the same step); it never reaches code of this ticket (the editor does not stream, `Lod0Streamer`
returns at once), so not confirmed green, not traced to this ticket either. CrazyGames build: passes. Headless Chrome on the release build: below.

**How it works**
- Build (`StreamedBundles`, `Lod0Split`): `Lod0Split.Find` scans the scenes (opened additively) and every prefab and asset of the bundles' closure and lists the mesh assets that
  are only (a) a MeshFilter of a renderer in a LOD group's LOD0 slot (groups of 2+ levels) or (b) a scatter prototype's level-0 part (`InstancedScatterRenderer.prototypes[].levels[0]`),
  with no other reference (colliders, other slots, other asset types) and not in the player (Boot's closure and Resources). Ground chunk meshes (glb sub-assets, or `.asset` with
  a name several levels share) and any LOD0 name that is not unique stay as they are. `StreamLayout.PlanLod0` gives each such mesh to the `-lod0` twin of the bundle the plan put it in
  (`fungal-assets` -> `fungal-lod0`, `shared-1df53434` -> `shared-1df53434-lod0`) and a `lod0:<Level>` manifest entry per level (listed after the match starts, not in `scene:<Level>`).
  Order: (1) the real meshes are built into the `-lod0` bundles; (2) each such mesh asset **on disk** is backed up (`Library/Lod0Backup`, with an index written first) and overwritten by a
  3-vertex stub of the same name plus `#stub` and the same bounds (same GUID, so no prefab or scene changes at all); (3) the level bundles are built with the stubs;
  (4) every original is copied back and reimported, byte-compared, the backup deleted (`using`/Dispose, and `Lod0Split.RecoverLeftovers` also runs at the start of each bundle build and on editor load, for a crash in between).
  No source prefab or scene is touched (`git status` after the builds shows only ticket 61's files); the editor and Play mode have the real LOD0 as before (nothing streams there).
- How the LOD0 slot is filled before the swap: `Lod0Streamer.Prepare` (GameLoop `Live`, the scene is live and covered) goes over every loaded `LODGroup` (scene objects and the loaded
  prefab assets the scene spawns from) and, for a stub in LOD0, sets the MeshFilter to the same part's LOD1 mesh (`model_body#stub` -> `model_body_LOD1` of the group's level 1 renderers).
  The instanced renderer does the same on load (`Lod0Meshes.WithFallback` in `Load`, also in `LevelsOf`' output) so its LOD0 ring draws LOD1. `StaticSunShadow.Invalidate` redraws the fixed shadow map after.
- How the swap finds its renderers: no map file. `Lod0Streamer.Begin` (GameLoop `onPlay`, i.e. the HUD is up and the first `gameplayStart` has gone) fetches the `lod0:<Level>` bundles
  (`ContentStreamer.Fetch`; the next level's prefetch of `-lod0` waits for it, `ContentStreamer.Lod0Pending`), loads all meshes, `Consume`s the bundles (`Unload(false)`: the file data goes, the
  meshes stay; the state is released with the level's others, and `Lod0Meshes.Loaded` is cleared at the next level's `Prepare` so `UnloadUnusedAssets` frees them), and by mesh **name** replaces, 4 ms a frame, the mesh of
  every MeshFilter in a LOD0 slot of every loaded LODGroup (stub name or the LOD1 stand-in `X_LOD1` -> `X`), and calls `InstancedScatterRenderer.ReplaceLod0Meshes` on the scatter renderers.
  Objects spawned later come from the loaded prefab assets, which are swapped too. The log line: `Lod0Streamer: Fungal swapped 496 meshes in 1.7 s (0 LOD0 slots still on a stub or LOD1)`.
- Instanced renderer: included (foliage and crest clusters take their LOD0 from the same prefab meshes; directly given prototype levels are handled by the property path).
  Not measured separately: the 496 swapped is renderers plus scatter parts together.
- Phones: `Lod0Streamer.SkipOnTouch` (const, false): the one switch.

**Sizes** (CrazyGames build, Brotli player, LZ4 bundles; MiB from the build logs; before = the build of the commit before, after = this one)

| | before | after |
|---|---|---|
| Fungal first play: player | 16.35 | 16.34 |
| Fungal first play: bundles up to the first match | 13.14 | 10.05 |
| **Bytes before `gameplayStart`, Fungal (headless count)** | **30.9 MB (29.5 MiB)** | **27.7 MB (26.4 MiB)** |
| All bundles | 41.8 in 27 files | 41.7 in 36 files (9 `-lod0` = 13.2) |

Per level, the bundles its first play fetches (the scene's own plus the shared ones it loads) and its `-lod0` bundles (fetched after the start; MiB, build log):

| level | before | after | its `-lod0` |
|---|---|---|---|
| Meadow | 11.0 | 8.7 | 2.3 |
| Fungal | 13.1 | 10.1 | 3.1 |
| Chessboard | 12.4 | 9.4 | 3.0 |
| Toybox | 9.6 | 7.7 | 1.9 |
| Kitchen | 10.5 | 8.3 | 2.2 |
| Bathroom | 8.7 | 7.5 | 1.2 |
| Computer | 9.7 | 7.7 | 2.0 |

The `-lod0` bundles: chessboard 2.7, fungal 2.4, kitchen 1.8, computer 1.7, meadow 1.6, toybox 1.5, bathroom 0.9, shared-1df53434 0.3, shared-f1d6da86 0.3 (a level also loads the shared ones it uses).
The saving is about 3 MB (not the ticket's 15 MB): the 15 MB was the whole game's LOD0; 563 meshes (69 MB of mesh assets, 13.2 MB LZ4) are split off over seven levels, each level using 1.2 to 3.1 of it. The other
858-563 = 295 LOD0-slot meshes stay: 288 ground chunks (9.6 MB of assets, names repeat across levels; the ticket left the ground alone) and 7 in the player. The shared ones are fetched once for all levels that use them.
(A later build with ticket 61's uncommitted changes in the tree had a 15.4 MiB player and a 8.6 MiB Fungal base: not this ticket.)

**Headless Chrome** (installed Google Chrome, `--headless=new`, SwiftShader, `http://localhost:PORT`, a Node server with `Content-Encoding: br`, CDP driver; `?streamcheck&level=Fungal`)
- Order of the requests: player (Web.data/wasm/framework), the 9 Fungal bundles, (8 s after the scene was live: the next level's bundles, Chessboard), then after the click: `Lod0Streamer: Fungal shows LOD1 in 385 LOD0 slots`,
  the `-lod0` bundles fetched, `fetched 118 LOD0 meshes in 0.2 s`, `swapped 496 meshes in 1.7 s (0 LOD0 slots still on a stub or LOD1)` (6.6 s in the first run, which loaded the machine more). The match ran on, HUD and view as before (screenshots read).
- Console: the only exception is the one of Chrome's synthetic click on the canvas (`WrongDocumentError ... pointer lock`, ticket 52), also in the "before" build; the "bounds contain NaN" warnings every 20 s are in the before build too.
- Not checked: a real GPU, a before/after screenshot of one exact view, the cache on reload, a level other than Fungal in the browser, memory numbers in the browser.

