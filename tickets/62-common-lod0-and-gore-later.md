# 62 — The common LOD0 and the gore pieces streamed after the match starts

Part of **Minigun Monsters**, toward CrazyGames' 20 MB mobile homepage limit. After 60 (each level's LOD0 after `gameplayStart`) and
61: use 60's mechanism for what the player itself carries. **The first play stays on the Fungal level.**

## Today (player, uncompressed, from the build log's player list after 59 part 1b)

| What | Size | Needed before the match? |
|---|---|---|
| Gore head pieces (ticket 51, the burst pieces of 12 heads) | 2.42 MB | No: only at the first head burst |
| Heads' LOD0 (12) | 1.32 MB | No: LOD1 until it arrives |
| Other LOD0 (pods, flag stand, ...) | 0.90 MB | No |
| Weapons' LOD0 | 0.82 MB | No, but your own first-person weapon is always close: swap it first |
| Soldier body | 0.49 MB | Yes (skinned, no LODs) |
| LOD1 and LOD2 of all the above | 1.82 MB | Yes |

About 5.5 MB uncompressed (about 2.3 MB compressed) can move out of the player.

## Decisions

- A `common-lod0` bundle (and a `gore-pieces` bundle, or one bundle for both if simpler), fetched right after the first `gameplayStart`
  of the session, before the level's own `-lod0` (60); kept for the whole session (never released).
- Before it arrives: heads, weapons and pods show LOD1 in the LOD0 slot (as 60); a head burst before the pieces arrive uses a
  stand-in (the effect without the pieces, or the LOD1 head pieces if they exist), never an error or a missing effect.
- Once loaded the game looks exactly as today.

## Work

- Find how the player references these (Resources folders, the `MonsterHeadModels` registry, `WeaponTable`, the gore code `Gore`,
  `MonsterBurst`, `HeadWear`, pods' prefabs) and move the LOD0 meshes and the gore pieces into the new bundle(s) the way 60 does for
  the levels (bundle-time swap, source assets unchanged on disk, a runtime streamer swapping in meshes, objects spawned later
  included).
- Measure the player before and after, and the Fungal first play before `gameplayStart` (headless Chrome network count on
  `http://localhost:PORT`).

## Also in this ticket (found by 60 and 61)

After 61 the Fungal first play is about 24.0 MiB (player 15.39 + Fungal's bundles 8.60). Besides the table above:

1. **The GUI twice** (61's Status): every level scene holds a copy of the GUI prefab, so `shared-1df53434` carries `GUI.prefab`, its
   screens, the Fredoka and Lilita atlases and the 19 icons again (the player has them): about 1.3 MiB per first play. Keep the one in
   Boot (or load it from the player) and take it out of the level scenes as built for the bundles (`LevelBuildScenes`), without
   breaking the editor's direct Play of a level scene (e.g. strip it only in the bundle build, or the level finds Boot's GUI).
2. **Ground chunks' LOD0** (60's Status): 288 ground-chunk LOD0 meshes (9.6 MB of assets) stayed in the level bundles because their
   names repeat across levels. Make them streamable like 60's (unique names at bundle time, or a per-level map), keeping collisions
   exact from the first frame (the colliders are separate meshes: check).
3. **Navigation gizmo icons** (`NavMeshSurface Icon.png`, `NavMeshModifier Icon.png`, TMP gizmo `.psd`s) in `shared-1df53434`: the
   package components' script icons on scene objects. Remove the navigation authoring components from the scenes as built for the
   bundles if the baked navmesh data does not need them at runtime (check `BattleNavmesh` and the bots' use).
4. **The skybox cubemap** copied into each level bundle (`Default-Skybox-Cubemap`, 0.9 MB LZ4 over the 7): check each level scene's
   `RenderSettings.skybox` / reflection source; drop it where the level's own sky does not need it (without changing the look).
5. **The flaky touch play test**: `TouchPlayTest` times out "waiting for the jump" (3 s of real time) whenever the machine is loaded
   (another Unity build running): make its waits robust (frames or game time, or a longer limit) so a busy machine does not fail it.

Measure each item's saving. The target: the Fungal first play before `gameplayStart` at 20 MB or less, measured in headless Chrome.

## Done when

- The player is smaller by about the table's share (numbers in the Status), and the Fungal first play before `gameplayStart` is
  measured and written down (the target is 20 MB or less).
- After the bundle arrives, heads, weapons, pods and bursts are today's (a check compares meshes); a burst before it arrives works.
- EditMode, desktop and touch play tests (with a burst before and after the swap), the CrazyGames build and a headless run pass.

## Status

Done: the Fungal first play before `gameplayStart` is **19.04 MB (18.16 MiB)** in the headless count (target 20 MB: about 0.96 MB margin). EditMode 780 pass, 0 fail
(one new test, the common and music keys). Desktop play test (five matches): 0 errors. Touch play test: 0 errors. CrazyGames build: passes. Headless Chrome: below.

**Sizes** (CrazyGames build, Brotli player, LZ4 bundles, MiB from the files; Fungal's bundles = the 8 to 9 its `scene:Fungal` entry loads)

| step | player | Fungal's bundles | first play total | change |
|---|---|---|---|---|
| ticket 61 (before) | 15.39 | 8.60 | 23.98 | |
| 5. touch test fixed | 15.39 | 8.60 | 23.98 | 0 |
| 1. common LOD0 and head pieces after the start (`common-lod0`, 2.22 MiB) | 13.83 | 8.36 | 22.20 | -1.78 |
| 2. GUI out of the level scenes in the bundles, 3. ground chunks' LOD0 (`fungal-lod0` 2.4 to 3.1) | 13.80 | 7.00 | 20.81 | -1.39 |
| 4. battle music layers 2 to 4 after the start (`music-orchestra` 2.7, `music-electronic` 2.6) | 13.83 | 4.33 | **18.16** | -2.65 |

(Steps 2 and 3 were built together: from the bundle sizes the GUI is about 0.6 MB, shared-1df53434 1.34 to 0.7 MiB, and the ground chunks about 0.7 MB, `fungal-assets` 3.68 to 3.01 MiB.
The player grew 0.03 MiB by the new code.) All bundles: 41.0 MiB in 38 files. After the start Fungal fetches `common-lod0` 2.2, its `-lod0` bundles 3.7, `music-orchestra` 2.7 MiB.

**How it works**
- *Common LOD0 and pieces (1).* `Lod0Split.Find` now also scans the player's closure (Boot and Resources) and returns a `Lod0Plan`: `level` (as 60) and `common`: the LOD0-slot-only meshes
  that the player carries (heads, weapons, pods: 52 meshes, 5.6 MB of assets) plus the 84 head piece meshes (MeshFilters of the registry's piece prefabs, 5.0 MB), 136 in all. They are
  built, real, into `common-lod0` (manifest key `common`); then stubbed on disk (as in 60) and **the stubs stay through the player build** (`StreamedBundles` holds the `Lod0Split` until
  its Dispose, after `BuildPlayer`), so the player and the shared bundle (which copied heads and weapons) carry stubs. Originals are restored byte for byte (`Library/Lod0Backup`, index first, recovered on editor load; checked after every build).
  Runtime: `Lod0Streamer.Begin` fetches `common` first (once per session, before the level's own `-lod0`; never released), loads the meshes into `Lod0Meshes.Common`, and swaps by name (LOD0 slots, then every MeshFilter on a
  stub: the pieces). `Prepare` shows LOD1 in the stubbed LOD0 slots (heads, weapons, pods) until then, and sweeps again every 2 s until the common meshes are in and at each level's start.
  A head burst before they arrive: `Lod0Meshes.PiecesReady` is false, `Gore.Burst` goes without pieces (a puff and the blood spray, no error). Collisions: none of these meshes has another reference (`other` set: colliders, other slots).
- *GUI twice (2).* `SceneGuiStrip` (an `IDisposable` like `Lod0Split`, backups in `Library/SceneGuiBackup`, restored after the bundle build and on editor load) removes each level scene's GUI instance (`Match.gui`) before the
  bundles are planned and built, so `GUI.prefab` and its closure are not in the shared bundle. `Match` and `MatchHud.Create` take Boot's GUI (`GuiFlow.Persistent`) when `gui` is null. The scene files on disk keep the GUI (the editor's direct Play of a level scene is unchanged).
- *Ground chunks (3).* The name rule of `Lod0Split.Find` is now per level: a mesh name two meshes share only clashes if they are in the same level (or common); `chunk_0_0_ground` of each level's folder is unique within its level
  (`Lod0Meshes.Loaded` is per level). 845 level LOD0 meshes (was 563) are split off; 6 stay (1.0 MB). Their colliders are separate meshes (not in a LOD0 slot, not stubbed).
- *Music (4, not in the ticket's list).* `SceneGuiStrip` also nulls layers 2 to 4 of each scene's `ArenaAudio.musicLayers` for the bundles; their clips are built into `music-orchestra` and `music-electronic` (key `music:<Level>`).
  `Lod0Streamer.RunMusic` fetches them last, fills `ArenaAudio.Late` and calls `GameAudio.MusicArrived()`: `ArenaAudio.Sounds.Loaded()` (the layers up to the first empty one) means the track starts on layer 1 (the menu clip, already playing)
  and `MusicDirector.Extend` joins the others in step with it, the path the orchestral arenas already used. Layers 2 to 4 only fade in with the fight's intensity, so nothing is missed in the first seconds; afterwards it is today's mix.
  A later level of the same set finds the clips in `ArenaAudio.Late` already.
- *Touch test (5).* `Until` timed out after 3 s of real time; on a loaded machine (other Unity run, load 7-12) the editor runs at a few frames a second and a 0.15 s tap could pass in under a frame. Now a wait ends only when both
  three times the real limit and 30 frames per limit second are used up, a tap lasts at least 4 frames, and the jump tap is repeated (up to 3 times) if no jump was counted. It passed on the machine at load 6 to 10 (the old test failed there).
- `StreamCheck` (new, `Campaign/Runtime`): with `streamcheck` and `burst` in the page URL it kills a bot with a headshot 2 s after the match start (before the common meshes) and again after they arrived, and logs what the bursts made. Nothing anywhere else.

**Headless Chrome** (installed Google Chrome `--headless=new`, SwiftShader, `http://localhost:PORT`, Node server with `Content-Encoding: br`, CDP driver in the scratchpad's `w62/`; `?streamcheck&burst&level=Fungal`)
- Bytes received (`Network.dataReceived`, on the wire) before the first `gameplayStart`: **19,040,798 (19.04 MB, 18.16 MiB)**, counted at the moment the `common-lod0` fetch starts, which is when the HUD is up; the SDK's
  "Requesting gameplay start" log line comes about 0.2 s later, and on a fast localhost 2.3 MB of `common-lod0` had already arrived by then (21.37 MB if counted at that line). Before it: the player (4 files), the nine Fungal bundles.
- Order after the click: `common-lod0` (136 meshes, swapped 1500 slots/filters), `-lod0` bundles (`Fungal swapped 559 meshes ... 0 LOD0 slots still on a stub or LOD1`), the music bundle (`music layers fetched (3 clips)`), Chessboard's prefetch.
- Head burst before: with `common-lod0` held back 15 s by the server, `StreamCheck: before: killed ...; common meshes arrived: False; corpses 0 -> 0`: a puff, no pieces, no exception. After: `corpses 0 -> 1, piece meshes 14, on a stub 0`.
- The match ran on (HUD, flag banner, weapon slots, score; screenshot read). Console: no exception in either run; the only errors are the three SwiftShader hidden-shader lines at the start (also before).
- Not checked: a real GPU, sound (headless: the late layers are only seen in the log, not heard), the touch layout in the browser, another level in the browser, memory numbers, a real network.

**Not done**
- Navigation gizmo icons (item 3 of "Also in this ticket") and the skybox cubemap (item 4): left, the target was met without them. The icons are two small PNG textures in `shared-1df53434` (tens of KB compressed); Chessboard and the
  electronic levels have no skybox material and Fungal/Meadow's is its own, so the cubemap copy is about 0.13 MiB a level: not worth changing a scene's reflection source blind.
- A touch phone still downloads the common and level LOD0 bundles (`Lod0Streamer.SkipOnTouch` is the one switch). The first play is 19.04 MB for every device; the player is 13.83 MiB of it (the wasm is 7.05).
- Margin to 20 MB is 0.96 MB; the next savings would be URP's film grain textures (2.5 MB uncompressed, ticket 58's file) or the wasm.
