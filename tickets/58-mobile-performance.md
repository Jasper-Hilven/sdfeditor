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

Not started.
