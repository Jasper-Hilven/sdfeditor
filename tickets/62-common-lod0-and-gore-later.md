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

Not started.
