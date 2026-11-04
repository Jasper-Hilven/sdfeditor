# 54 — Readable at CrazyGames' small window sizes

Part of **Minigun Monsters**, for the desktop launch on CrazyGames (and the base for mobile, 56-57).

CrazyGames requires text and images to be readable at `devicePixelRatio` 1 at these 16:9 sizes
(docs.crazygames.com/requirements/gameplay):

| Where | Sizes |
|---|---|
| Desktop, in the page | 907x510, 1216x684, 1077x606, **821x462** |
| Desktop, fullscreen | 1366x768, 1920x1080, 1536x864, 1280x720 |
| Mobile / tablet | **800x450** / 1080x607 |

The GUI is laid out at 1920x1080 (`GuiScene`: `CanvasScaler` ScaleWithScreenSize, match 0.5), so at 821x462 it is drawn at about
0.43x: the 18-24 pt texts (stat captions, key hints, the HUD's small labels, the tip, the kill feed, the scoreboard's columns) land
at 8-10 px, too small.

## Decisions

- **The target**: every player-visible text is at least **12 px tall on screen at 800x450** (the smallest size on the list), at DPR 1.
  With the 0.42 scale that is about 28 pt in the 1920x1080 layout. Titles and numbers are already large; this is about the small texts.
- Keep the 16:9 look at 1920x1080 as close as possible: make small texts bigger, shorten or drop secondary lines, give boxes room.
  Do not shrink the big titles to make room.
- The developer test panel (`DevTools` only) is out of scope.
- Every screen built by `GuiScreens`/`GuiPrefabs` counts: Loading, HUD, Scoreboard, Death, Pause, Settings, Confirm, Victory, Defeat,
  Credits, Caption, the kill feed and the power cards. The end card already fits any aspect (`GuiFitInside`, commit `02896ba0`).

## Work

- List each text under 28 pt in `Unity/Assets/GUI/Editor/GuiScreens.cs` and `GuiPrefabs.cs` (and any runtime-made text such as
  `NameTags`), with what it shows.
- Raise them to the target, re-flowing the layout where they then collide. Prefer one rule (a `GuiFactory` minimum size, or a
  `MinReadable` constant) over many separate numbers.
- Rebuild the GUI (`tools/unity-batch.sh gui -executeMethod SdfGui.Editor.GuiScene.BuildInBatch`) and snapshot every screen at
  821x462, 800x450 and 1920x1080 (`SdfGui.Editor.GuiSnapshot.Capture -snapshotDir <dir> -snapshotSize 821x462`; `-snapshotScreens`
  leaves out the test panel). Put the 821x462 sheet in `Media/gui-54/`.
- `NameTags` (Battle/Runtime): its near and far sizes hold at 800x450 too.

## Done when

- At 800x450 every player-visible text is at least 12 px tall (checked in the snapshots, and by a test over the built prefabs'
  `TMP_Text.fontSize` against the rule, where auto-size counts by its minimum).
- Nothing overlaps or runs off screen at 821x462, 800x450 or 1920x1080.
- EditMode tests pass.

## Status

Not started.
