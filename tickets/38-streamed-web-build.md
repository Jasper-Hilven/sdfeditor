# 38 — Streamed web build

The release WebGL build was one 174.5 MB data file loaded before the title. CrazyGames wants an initial download of at most 50 MB (measured
from the start of loading to the first `gameplayStart`, so the player and anything fetched before the first match count), 250 MB in all and
1500 files, with relative paths. Now the player carries only Boot, the GUI, the menu backdrop, the intro comic and the Meadow; every later
arena and comic is an AssetBundle next to it, fetched when the campaign goes there and prefetched in the background before.

## Decision: plain AssetBundles, no Addressables

- CrazyGames' own Addressables guide (docs.crazygames.com/resources/unity-addressables-guide) only says: build, then upload the generated
  folder (StreamingAssets) with the build; it gives no URL rules, no size rules for bundles and does not mention remote catalogs. The
  requirements page says files must use relative paths and that externally hosted files are judged by time to gameplay. So bundles uploaded
  inside the build folder, loaded by a URL relative to the page, are the supported shape.
- Addressables would add a package (catalog JSON, settings assets, a build step of its own, group/label bookkeeping) for what is here a fixed
  list of 7 scenes. `com.unity.modules.assetbundle` and `unitywebrequestassetbundle` are already in the manifest, so there is **no package
  to pin**: Unity 6000.3.0f1, `BuildPipeline.BuildAssetBundles` + `UnityWebRequestAssetBundle`, URP 17.3.0.
- Compression: LZ4 chunk-based (the only one WebGL loads; the web server's gzip/Brotli can sit on top). The player stays Brotli.

## Build

- `Tools > SDF Mushrooms > Build Web`, or `Unity -batchmode -quit -projectPath Unity -buildTarget WebGL -executeMethod
  SdfMushrooms.Editor.WebBuild.Build` (through the batch lock when other agents work). One command writes the player and the bundles into
  `Unity/Builds/Web` (`Build/`, `TemplateData/`, `index.html`, `StreamingAssets/Bundles/`). Upload that whole folder. `-nostream` builds every release scene
  into the player instead (old behaviour); `-scene` builds the named scenes only (also not streamed).
- `SdfCampaign.Editor.StreamedBundles` (Campaign/Editor) does the work around the player build (`using var`: it moves, writes, and puts back):
  - the scenes after the first battle (`CampaignBuildScenes.StreamedPaths()`) are scene bundles (`fungal`, ...); a bundle holds scenes or
    assets, never both, so a scene's assets go in `fungal-assets`, and assets that several later scenes use go in `shared-<hash>` bundles, one
    per distinct set of scenes. Every asset is in exactly one bundle (`GetDependencies` of the scenes, so no implicit copies in several).
  - the comics (`Story/Resources/Comics/*`, 14 MB of panels) are one bundle each (`comic-beat-fungal`). The folder is moved out of Resources
    (`Assets/Story/StreamedComics`) while the player is built so the player does not carry it too, and moved back afterwards (also restored at
    the start of the next build if a build crashed).
  - files are named `<bundle>-<hash12>.bundle` (the content hash: a CDN or browser cache never serves a stale one, and Unity's cache key is the
    hash too). The list is written to `Resources/StreamedContent.json` for the player build only (`StreamManifest`: bundles with size and
    hash, and per key `scene:Fungal` / `comic:beat-fungal` the bundles to load, shared first, scene last) and deleted afterwards. Without it
    (editor, desktop, `-nostream`) nothing is streamed.
  - a generated `link.xml` (deleted afterwards) keeps the managed types of the bundled scenes' scripts and the engine modules their components
    come from, because code stripping only sees the player's scenes.
  - the log lists every bundle's size, what each scene loads, and the assets that are in the player and in a bundle too.
- Shared assets: whatever Boot, the Meadow or any Resources folder uses is in the player and **cannot** be shared with a bundle, so a later
  arena that also uses it gets a copy in its bundle. Today 196 assets, 81.5 MB of source files (scouts' and weapons' meshes, the island
  underside, rocks, the GUI font asset), mostly the weapons, scouts and island underside; they are in `shared-*` bundles once, not per arena.
  Moving the Meadow into a bundle too would remove that, but then it would be part of the initial download anyway (it is fetched before
  the first match), so it was not worth it.
- Shader variants work from bundles (URP toon shaders with the scene's materials): the Fungal arena renders correctly from its bundles in a browser.

## Runtime

- `ContentStreamer` (Campaign/Runtime) is the only thing that touches bundles. `Enabled` = WebGL player with a non-empty manifest.
  URL = `Application.streamingAssetsPath` + `/Bundles/<file>` (was the page's folder; CrazyGames loads the game from its own page and
  serves only `StreamingAssets` besides `Build`), so it works wherever the folder is hosted. Download by
  `UnityWebRequestAssetBundle.GetAssetBundle(url, hash)` (cached by Unity's Caching in IndexedDB by hash; plus the browser's HTTP cache), with a
  40 s no-progress stall guard. Loaded bundles are kept until the new scene is live; then `Release` unloads every bundle the current scene and
  the prefetch do not need, so only the current and the next arena are in memory.
- `Application.CanStreamedLevelBeLoaded` is replaced by `ContentStreamer.CanLoad` in `CampaignRunner.Available` and `GuiCampaign.IsAvailable`:
  a streamed arena counts as available (otherwise `CampaignProgress` would skip those steps).
- `SceneLoader.Run`: if the scene's key still needs bundles, it downloads them first, with the Loading screen's bar over the first 70% (the
  scene load takes the rest). A beat's load has no briefing (it is black): it gets the Loading screen (arena name and bar) just for the download.
- Prefetch: `CampaignRunner.BeginMatch` calls `ContentStreamer.Prefetch(StreamPlan.Upcoming(...))`: the next steps' scenes and comics up to
  and including the next battle (after the boss, the ending's comic). It starts 8 s after the match began and fetches one bundle at a time, so
  the match keeps the bandwidth. The title, Level Select and a Continue never prefetch: nothing is fetched before the first match except when
  the player goes to a streamed arena.
- Comics: the beat plays over its arena's scene, so the arena's list includes its comic bundle. `StoryIntro` has two hooks
  (`StreamedHas`, `StreamedPanels`, set by `ContentStreamer.Install`) so panels come from the loaded bundle; a replay from the Story gallery
  (and the ending's comic) go through `SceneLoader.Ensure`, which shows the Loading screen if the comic is not in yet.
- Failure: a failed bundle (404, network, stall) never leaves a frozen screen. Prefetch failures are only logged (fetched when needed). In
  the Loading screen the bar is replaced by "DOWNLOAD FAILED - CLICK TO RETRY"; a click fetches again (bundles already in are kept); Escape goes
  back to the title. Without a Loading screen it goes back to the title at once.
- Dev check: on a page whose URL contains `streamcheck`, `unityInstance.SendMessage("Campaign", "StreamCheck", "beat-fungal")` runs that step.
  (The default template's `unityInstance` is not global; a test copy of `index.html` with `window.unityInstance = unityInstance` does it.)

## Sizes (release, Brotli player, LZ4 bundles)

| | MB |
|---|---|
| `Web.data.unityweb` | 46.6 |
| `Web.wasm.unityweb` | 9.6 |
| `Web.framework.js.unityweb` + loader | 0.2 |
| **Initial download** (player) | **56.4** |
| 33 bundles (`Bundles/`), 152.7 MB | |
| arena assets: fungal 24.9, chessboard 28.4, kitchen 19.1, toybox 17.8, computer 13.7, arena (boss) 12.1, bathroom 10.6 | |
| shared: 12.8, 6.0, and 8 small ones (0.0 to 1.0) | |
| scene bundles 0.3 to 0.4 each, comics 0.7 to 1.2 each (8) | |
| Total upload | about 209 (34 files + the player's) |

Per arena (what the Loading screen waits for if the prefetch did not finish): Fungal 41.3, Chessboard 37.4, Toybox 25.5, Kitchen 28.2.

**The initial download is still over 50 MB (56.4)**: what is left is the Meadow's and the menu backdrop's meshes (122 MB uncompressed, 47 MB
Brotli, nothing else is big; Brotli hardly shrinks meshes) plus the wasm (9.6). The vertex compression of ticket 37 is what brings it under
(it already took 3 MB off between two builds here); other things in the player to look at: Story screens (3 PNGs, 4.5 MB uncompressed), the
Meadow's `regions.png` (4.8 MB), the Unity splash logo (2.7 MB).

## Tested

- EditMode: all 612 pass (`StreamManifestTests`: bundle order, remaining bytes, JSON, no manifest, `StreamPlan.Upcoming`).
- Real browser: Claude-in-Chrome was not connected, so headless Google Chrome (SwiftShader GL) driven over the DevTools protocol, served by
  `python3 -m http.server`. Boot to the title works (no missing shader or script errors apart from URP's hidden debug shaders, which need
  GPU features SwiftShader lacks); `StreamCheck beat-fungal`: all 9 Fungal bundles fetched from `/Bundles/`, the comic's panels show from
  its bundle, then the Fungal arena runs from bundles (toon shading, sky, HUD, scouts, pistol); `StreamCheck meadow` then a click: 8 s later the
  Fungal bundles are fetched one after the other in the background (shared, assets, scene, comic); a 404 on `chessboard-assets` shows
  the retry message on the Loading screen (the other bundles stay), the file put back and a click gives CLICK TO PLAY.
- Not tested: a real GPU, the CrazyGames preview (the page is not on their CDN: relative URLs resolve against the page's folder, which is what
  their guide and requirements say), Unity's cache across a reload in a normal profile, Safari, memory on a 4 GB device.

## Status

Done and tested as above. Left: get the initial download under 50 MB (ticket 37's mesh packing; then re-measure), try the CrazyGames
preview, and check the cache across reloads. The first Loading wait of an arena depends on the prefetch finishing (41 MB at the player's
connection speed within one match); if it often does not, prefetch earlier (e.g. start in the Meadow's warm-up).

## Next (for the agent that takes this ticket)

37 is done (its full rebuild and web build ran: the streamed player is 26.4 MB, the full build 48.7 MB, see done/37-vertex-compression.md). Then:
1. Re-measure the initial download (the player's `.data` + `.wasm` + `.framework`/loader in `Unity/Builds/Web/Build`). If it is
   over 50 MB: take the smaller wins listed above (the Story screen PNGs, the Meadow's `regions.png`, the splash logo); if still over,
   give the player a tiny start scene and stream Boot and the Meadow too, which also ends the 81.5 MB of assets duplicated between the
   player and the bundles.
2. Start the next arena's prefetch earlier (in the Meadow's warm-up / on the title once a campaign is chosen) if a 41 MB arena would
   not finish within a match on a ~20 Mbit connection.
3. Check the cache across a page reload in a normal Chrome profile, and Safari, with the browser tools if they are connected.
