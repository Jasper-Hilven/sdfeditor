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

## Done when

- The player is smaller by about the table's share (numbers in the Status), and the Fungal first play before `gameplayStart` is
  measured and written down (the target is 20 MB or less).
- After the bundle arrives, heads, weapons, pods and bursts are today's (a check compares meshes); a burst before it arrives works.
- EditMode, desktop and touch play tests (with a burst before and after the swap), the CrazyGames build and a headless run pass.

## Status

Not started.
