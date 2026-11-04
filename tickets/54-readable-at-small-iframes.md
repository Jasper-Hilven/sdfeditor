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

Done in code and built; left for a human: a look at the sheets and at the HUD in Play mode on a real 800x450 window.

- **The rule**: `GuiFactory.MinReadable = 29` pt (12 px / (450/1080) = 28.8). `GuiFactory.Text` and the button, banner, bar and auto-size
  helpers go through `GuiFactory.Readable`, which raises any size under it (auto-size minimums too). The Gallery and the Test Panel
  turn it off (`GuiFactory.EnforceMinimum`). Runtime: `NameTags` near/far 26/15 to 40/29. Titles and big numbers untouched.
- **Texts raised** (pt before to 29+): HUD: FPS hint 24, HEALTH and RAGE labels 20, their bar values 20, spore key letters 20, spore
  names 18 (auto 11-18), power-on line 22 (auto 12-22), weapon name 24, slot numbers 22, flag status 24 (auto 14-24, now ellipsis),
  flag marker distance 24; kill feed names 27; scoreboard subtitle 28, columns 22, rows 28-30 and status 24, hint 24; loading and
  end card: stat captions 24, head key hint 26 (auto 18), head caption and NEW HEAD autos (min 20); caption: speaker 24, line auto
  20-30, hint 20-26; credits hint 26; selector auto min 18. Credits' small print is 44 x 70 = 30.8.
- **Re-flowed**: health/rage panels 340x48 to 440x60 (label 130 wide, bar 276x38); spore cards 165x44 to 215x56; weapons panel 430x140
  to 430x164; flag status panels 52 to 60 high; kill feed moved 70 px down so it clears the flag status panels; flag banner 900 to 760
  wide; scoreboard columns spread (kills, deaths, captures) and status moved; caption box 900x136 to 1000x196.
- **Checks**: new `SdfGui.Tests` (`Assets/GUI/Tests`): every TMP_Text in the built prefabs (not Gallery, Test Panel, the GUI
  composite, the unused Level Card) is at least 29 pt, auto-size by its minimum, `size=N%` tags counted. EditMode 754 pass, 0 fail.
  Snapshots of all screens at 821x462, 800x450 and 1920x1080 looked at: no overlaps or run-offs; sheets in `Media/gui-54/`.
- **Uncertain**: the spore cards (to x=655) and the caption box (from x=460) can overlap when both show; a very long player name in the
  head caption or flag status is shrunk or cut. Texts filled at runtime (names, tips) were not in the snapshots.
