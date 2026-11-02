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

Done and checked in a browser (headless Chrome, below). EditMode 688 pass, 0 fail (6 new here). GameLoop play test: five matches and the reload, 0 errors.

**What changed**
- `LevelBuildScenes` (was `CampaignBuildScenes`): the player lists Boot only; `StreamedPaths` is all 7 levels. The layout of the bundles is
  `StreamLayout` (Campaign/Core, plain data, tested), used by `StreamedBundles`: scene bundle + `<level>-assets` + `shared-<hash>` per set of levels.
- `StreamManifest`: a `scene:<Level>` key per level; the comic key and field are gone. `SceneLoader`: the comic `Ensure` is gone; Escape on
  the failed-download Loading screen does nothing (a click retries); `GameLoop.AbandonedLoad` only logs (it can only happen with no Loading screen).
- `GameLoop`: the first level is the rotation's pick (`LevelRotation.First`: never one of the saved last three), its bundles fetched behind
  the Loading screen with its name. Dev hook: a URL with `streamcheck` and `level=Kitchen` forces the first level (`LevelRotation.Forced`).
- `GameLoopPlayTest` follows the rotation's first pick, and the reload check expects it not to be one of the save's last three.

**Sizes** (MiB, release WebGL, Brotli player, LZ4 bundles; before = the 41 build in `Builds/Web`, after = this build)

| | before (41) | after (52) |
|---|---|---|
| `Web.data.unityweb` | 13.7 | 8.3 |
| `Web.wasm.unityweb` | 9.1 | 9.2 |
| framework + loader | 0.2 | 0.2 |
| **Initial download (player)** | **23.0** | **17.7** |
| Bundles | 37.0 in 23 files | 41.4 in 27 files |
| Total upload (player + bundles) | 60.0 | 59.1 |
| Level scenes in the player | Boot, Meadow | Boot |

Each level's own bundles (scene + assets): Meadow 4.3, Fungal 6.5, Chessboard 6.3, Toybox 3.6, Kitchen 4.3, Bathroom 2.7, Computer 3.5.
The 13 `shared-*` bundles are 10.1 in all. What the first level costs (its own + the shared ones it loads, from the build log): Meadow 10.7,
Fungal 12.8, Chessboard 12.1, Toybox 9.3, Kitchen 10.2, Bathroom 8.4, Computer 9.4. So **initial download + first level is 26.1 to 30.5**
(CrazyGames' limit is 50), against 23.0 before, when the Meadow was in the player and was always first. The player lost 5.3; the Meadow now
costs 10.7 as bundles (LZ4 instead of Brotli and a copy of what the player also carries).

Shared assets: the 81.5 MB that 38 had duplicated between the player and the bundles is now 79 assets, 7.8 MB of source files (the GUI
fonts, the weapon meshes and icons Boot's GUI uses, a music layer).

**Common bundle or the player** (the ticket's two options). What every match needs sits in the player today (the soldier body and its
animations, the heads, the weapons, the effects, loaded from Resources); the levels do not reference most of it, so a "common" bundle is
not something the build can produce by itself. Measured: leaving it in the player, as built, is the 17.7 initial + 8.4 to 12.8 above.
Not built, estimated from the build report: a common bundle can only save the duplicated part (the 79 assets, about 4.9 MB uncompressed in the
player, about 2.3 compressed), and moving the rest out of the player gives the same bytes in LZ4 instead of Brotli, so not smaller; it would
also need the soldier, heads and animations to load from a bundle (code of 44, 45 and 47). Decision: stay in the player. This is an estimate,
not a second build; with 20 MB of margin to the limit it is not worth the change now. If the margin is ever needed, the 2.3 MB is where to look.

**Browser check** (headless Google Chrome 156, SwiftShader, served by `python3 -m http.server` from `Unity/Builds/Web`, driven over the
DevTools protocol by a node script; screenshots and logs read, not assumed)
- `index.html?streamcheck&level=Kitchen`: the page's first fetches are the 9 Kitchen bundles (7 shared, `kitchen-assets`, `kitchen`), all HTTP 200
  from `StreamingAssets/Bundles/`, no Meadow, then the Loading screen, a click, and the Kitchen match runs from bundles (toon shading, HUD, pistol,
  tutorial text). 8 to 12 s after the match went live the next level's bundles were fetched one at a time (Bathroom: its shared, assets, scene).
- `level=Toybox`: same; the prefetch fetched Fungal's. The cheat code ended the match 3-0 (`GameLoop: Toybox ended 3-0 for Blue; next difficulty
  hard`), the Victory screen showed, CONTINUE: Toybox's bundles were released, no Fungal bundle was fetched again (it came from the prefetch), the
  Fungal arena was live, and its own prefetch fetched the Meadow's bundles.
- `level=Bathroom` with `bathroom-assets` removed from the server: the log shows `404 bathroom-assets` and `SceneLoader: download for scene:Bathroom
  failed`, and the Loading screen shows "DOWNLOAD FAILED - CLICK TO RETRY" (the other 8 bundles stay in). A click fetched again and failed again
  (404, message again). The file put back and a click: 200, the Bathroom match ran. (The Loading screen's team cards were empty during the
  download; not looked at further.)
- Chrome's CDP click on the canvas makes Unity log a `WrongDocumentError ... pointer lock` and show an alert (Unity's error handler); the
  driver accepts the alert and the game goes on. That is the synthetic click, not the build.
- Not checked: Escape on the failed screen in a browser (it is gone from the code, read not run), a real GPU, the CrazyGames preview, the cache
  across a reload, Safari.

**Done when**: 7 level keys in `StreamedContent.json`, Boot alone in the player: verified (the build log lists `scene:` keys for all 7; the player's
scene list is Boot only; `EveryLevelHasAKeyAndBootIsTheOnlyPlayerScene`). Sizes noted: verified (above). Browser: boot into a non-Meadow level from
bundles, the next level from its prefetch, a 404 shows the retry: verified. "Play to the end" was done with the cheat code (3 captures), not by
playing 10 minutes.
