# 58 — Mobile performance and memory

Part of **Minigun Monsters** on mobile. Alongside 57 (it touches quality, not the GUI layout). No phone is available to the agent:
measure in desktop Chrome with throttling and the web build, and leave the real-phone check to the user.

The quality system: levels best first, an automatic setting that moves by frame rate (`GuiQualityRule`, `GuiStartQuality`,
`QualityHintRule`, `QualityExtras`, `QualityPresetCamera`), budgets on the web preset of about 500k triangles and 200 draw calls
(tickets/README). WebGL memory: initial 32 MB, growth to 2048 MB (`ProjectSettings`). CrazyGames sets DPR 1 on low-memory Android.

## Work

- **Start quality on touch devices** (`Device.IsTouch`, ticket 55): start at the lowest levels, not where desktop starts; the
  automatic rule may move up from there. Cap render scale / resolution on touch (e.g. the canvas at DPR 1 and a render scale under 1
  at the low levels), so a 1080p phone does not render 1080p of toon outline.
- **What costs on a mobile GPU**: the ink outline pass (`SdfToonOutline`), shadows, post-processing, foliage density, the gore's
  pieces and particles, eight animated soldiers. For the lowest levels: outline off or cheaper, shadows off, post off, foliage thinned,
  gore pieces capped. List what each low level turns off in the Status.
- **Memory**: measure the WebGL heap after loading each level and after five matches (from the browser: `performance.memory` in
  Chrome, or Unity's `TOTAL_MEMORY` growth logged by the game) with the streamed bundles unloaded between levels (check they are).
  The target: stays under about 1 GB on the heaviest level, so a 4 GB Android phone (and CrazyGames' 4 GB Chromebook) holds it.
- **Measure**: the release web build on localhost in headless Chrome with mobile emulation (800x450, DPR 1, `?device=mobile`) and
  CPU throttling 4x: frame time per level at the lowest level, and the heap. Report the numbers; a desktop number for comparison.

## Done when

- Touch devices start at a mobile quality level with the cuts above; desktop's quality behaviour is unchanged (tests).
- Frame times and heap per level measured and written in the Status; no level grows the heap past the target over five matches.
- EditMode tests pass. Left for the user: the frame rate on a real mid-range Android phone.

## Status

Done in code and measured in headless Chrome; the real-phone check is the user's. Commit: see git log (ticket 58).

**What changed**
- `MobileQualityRule` (Mushrooms/Core, tested) and `QualityExtras`: on a touch device (`Device.IsTouch`, also when the SDK says so later) the
  game starts on the lowest level, **Ultra Low**, before the first scene loads (`?nomobilequality` in the address turns that off, for
  comparing). The automatic rule (`GuiQualityRule`, in the HUD) climbs from there as before; it was not changed. Desktop behaviour is unchanged: every cut is
  `touch && level == Ultra Low`.
- Render target on touch, at every level: the URP asset's render scale is capped so the 3D view is at most 540 pixels tall
  (`MobileQuality`, once a second, never in the editor): Ultra Low 0.5 (a 1080p phone draws 540 lines), Web 0.75, anything above
  gets 540/height (0.9 at 600 high, 0.5 at 1080). The page's own DPR is the page's (ticket 57; CrazyGames sets DPR 1 on low-memory Android).
- What **Ultra Low turns off on touch** (on top of what Ultra Low already is: render scale 0.5, no HDR, no MSAA, FXAA, 512 shadow
  map to 15 m, LOD bias 0.4, low foliage preset):
  - the ink outline, everywhere (`_SdfInkOff`: the hull's vertices are clipped, so no fragment is shaded; the foliage renderer does not
    draw the outline pass at all);
  - the level's fixed sun shadow (`SunShadowOff`: the lookups are skipped; the map is kept for a climb back up);
  - post-processing on every camera (restored when the level goes up);
  - foliage thinned to 0.6 of what the low preset keeps, draw distances x0.8;
  - gore pools and corpse/ragdoll limits halved (made once per level load).
  Levels above it (Web, High, ...) change only by the render scale cap.
- `PerfLog` (`?perflog` in the address): every 10 s a console line with level, quality, scale, frame avg / 95th, memory. `?perfquality=N` forces a level.
- Found on the way: `SceneLoader` locked the pointer on the Loading click even on touch (an error on phones): now not on touch.
  `MonsterBurst` called `GC.GetAllocatedBytesForCurrentThread`, which the web player does not implement (an error log and exception per
  burst, 34 in one 5-match run): read as 0 on the web.

**Numbers** (headless Chrome 800x450 emulation, DPR 1, CPU 4x, `?device=mobile`, **ANGLE SwiftShader (software GL)**: the GPU is the
host CPU, so these are indicative only; the canvas was 960x600 as the page sets it). Ultra Low on touch, 35 s of play per level, mean of the windows of 10 s:

| level | avg frame ms | worst 95th ms | Unity reserved MB | wasm memory MB |
|---|---|---|---|---|
| Meadow | 48 | 57 | 141 | 199 |
| Fungal | 72 | 91 | 152 | 239 |
| Chessboard | 49 | 51 | 153 | 199 |
| Toybox | 40 | 48 | 135 | 199 |
| Kitchen | 44 | 47 | 142 | 199 |
| Bathroom | 37 | 51 | 135 | 199 |
| Computer | 41 | 56 | 141 | 199 |

Before the cuts (same rig, first 10 s window after load, with the load's hitches in it): Kitchen on Web 362 ms (95th 1424),
Ultra High 375, High 428 (95th 2071); Ultra Low (all cuts) 86 in the same first window, 37-49 after. The fixed Ultra Epic start (scale 2.0, the web
default today, see below) was 247-565 ms in its first window. Desktop comparison: the desktop-mode game in this rig dies at the first click (headless
pointer lock error), so only first windows exist: Ultra Epic 326-566 ms, Web (Kitchen) 130 ms. Real desktop and phone numbers are the user's.

**Memory**: five matches in a row (Fungal, Meadow, Computer, Chessboard, Fungal, each ended with the cheat code): wasm memory 199 MB
after the first load, 239 MB after the second and flat to the fifth; Unity reserved 144-157 MB. Far under the 1 GB target (the web
build starts at 32 MB and grows). The streamed bundles do unload: after each match `ContentStreamer: released ...` lists the
previous level's `-assets` and level bundles and its shared ones (`Resources.UnloadUnusedAssets` + `GC.Collect` follow in `SceneLoader`).

**Found, not changed**
- The web player starts on **Ultra Epic** (scale 2.0): `QualitySettings.asset` has no `WebGL` entry in `m_PerPlatformDefaultQuality` (only
  Nintendo Switch 2), although `RenderPipelineSetup` means web to start on Web. Desktop web play therefore starts at the top and the rule walks it down.
  Changing it changes desktop's behaviour, so left for the lead's decision (add `WebGL: 4`).
- URP's film grain textures cannot be dropped from `UniversalRenderPipelineGlobalSettings.asset`: URP refills the list when the asset is
  loaded (tried; the build is the same size). Nothing in the game uses film grain.
- Not measured: a real GPU (headless Metal froze on the first shader compiles), a real phone, tablets.

EditMode 777 pass (3 new), `GameLoopPlayTest` five matches 0 errors.
