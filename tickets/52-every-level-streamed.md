# 52 — Every level in its own bundle

Part of **Minigun Monsters** (41-52). With the endless rotation (42), any of the 7 levels can be the one played first, so the
Meadow no longer has a reason to be in the player. The web player carries only Boot and the GUI, and **every** level is a streamed
bundle, the Meadow included.

Depends on: 41 (no comics, no Arena, no menu backdrop), 42 (`GameLoop` picks the level and prefetches the next). Before 50's covers
and videos are re-rendered for the upload.

## Decisions

- The player holds Boot, the GUI, and what every match needs: the soldier body, the heads (45), the weapons and the effects. These
  go in one `common` bundle, or stay in the player if that is smaller in all. Measure both and note the result.
- Each of the 7 levels is its own scene bundle plus its `<level>-assets`. There is no special first level in the build.
- The first level is picked at boot by 42's rule. The build does not decide it, and nothing in the build assumes the Meadow.
- At boot, the first level's bundles download behind the Loading screen, which shows the level's name and a bar. The next level is
  prefetched during each match, as now (38).

## Work

- `StreamedBundles` (ticket 38's editor build step):
  - Stream every level scene (all 7) and keep only Boot in the player's scene list.
  - Assets that several levels share go in `shared-<hash>` bundles, as now. Assets the player uses stay out of the bundles.
  - The player is no longer forced to hold the Meadow, so the 81.5 MB duplicated between the player and the bundles (38's "Shared
    assets") should drop: measure it.
- `ContentStreamer` / `StreamManifest`: a key per level (`scene:<Level>`). Remove the comic keys and the arena special cases left by 41.
  Rename `CampaignBuildScenes` to fit the new flow (it has no campaign anymore).
- Boot: hand the picked level to `SceneLoader`, which fetches its bundles first. A failed download keeps the Loading screen's retry, and
  with no title screen to go back to, Escape does nothing.
- The editor, the desktop builds and `-nostream` are unchanged: with no manifest, nothing is streamed.
- Tests (EditMode): every level has a manifest key; Boot is the only player scene; no asset is in two bundles; the load order is
  shared, then assets, then scene.

## Done when

- A web build has 7 level keys in `StreamedContent.json` and Boot alone in the player.
- The build log's sizes are noted in this ticket: the player (the initial download, CrazyGames' limit is 50 MB counted up to the first
  `gameplayStart`, so the first level's bundles count too), each level's bundles, and the total.
- In a browser: boot into a non-Meadow level from bundles, play to the end, and the next level loads from its prefetch. A 404 on one
  bundle shows the retry.

## Status

Not started.
