# 47 — Head unlocks and selection

Part of **Minigun Monsters** (41-50). You start with the head of the month and every head you unlocked before. Each won match unlocks
the next head in the calendar. Your head can be changed at any time in Settings.

Depends on: 42 (the save and match results), 45 (the heads). Blocks: 48.

## Decisions

- **Head of the month**: from the device's local date. It is free while its month lasts, and stays unlocked for good once you have
  played a match during that month.
- **Unlocking**: each won match unlocks the next locked head in calendar order after the current month, wrapping round (in November:
  December, then January, …). When all 12 are unlocked, a win unlocks nothing.
- **Your head**: the last one you picked. On the first run it is the head of the month.
- **Other players**: a random head from all 12, each player different within a match where possible. Locked heads do appear on
  others, which shows you what is still to unlock.
- **Settings**: a simple cycle (◀ head ▶) over the unlocked heads only, with a 3D preview of the head on your team's body, turning
  slowly.

## Work

- `HeadUnlocks` (Core, plain C#, tests): `Available(now, save)` and `OnMatchWon(now, save)`, which returns the head unlocked or none.
  Test the month boundaries, the wrap, all-unlocked, and a clock moved backwards (nothing is ever taken away).
- `GameSave` (42) gets the unlocked heads, the months seen and the chosen head.
- The match gives you your head and gives the bots random heads (45's table).
- Settings screen: the head row (◀ ▶, the head's name, its month, the preview), using the existing settings GUI.
- `HappyTime` (`WebPortal`) on a win that unlocks a head.

## Done when

- The EditMode tests pass. In Play mode: a win unlocks the right next head, it shows in Settings, and the choice survives a reload and
  is worn in the next match.

## Status

Done in code; EditMode green (688), the play test and the reload check pass. Only the Yeti and the Jack-o'-Lantern are modelled, so every other head shows as a stand-in.

**Done**
- **Rules** (`SdfHeads.Core/HeadUnlocks.cs`, plain C#, the date passed in): `HeadProgress` (`unlocked`, `monthsSeen`, `current`), `HeadUnlocks`: `IsAvailable`, `Available(now, p)` (calendar order: the head of this month, every month a match was played in, every win's unlock), `HeadOfMonth`, `RecordPlayed`, `OnMatchWon(now, p)` (records the month, unlocks the next head not yet yours after this month, wrapping; null when all 12 are yours), `Current`, `SetCurrent` (only an unlocked head), `Previous`/`Next` (cycle over the unlocked, wrapping). Nothing is ever removed, so a clock moved back loses nothing.
- **Others**: `HeadCast` (a `System.Random`; random from all 12, locked ones too, each different within a match, repeating only past 12), `HeadSelection` (`Yours`, the match's cast; `Match.Start` begins a new cast), `HeadStandIn.Of(id)` (an unbuilt head shows as the built head of the nearest month) and `HeadStandIn.Label` ("Name (placeholder)").
- **Save**: `GameSave.CurrentVersion` is now 2 with `heads` (a `HeadProgress`); version 1 saves migrate (difficulty and levels kept, heads empty), other versions read as the default.
- **Match**: `SoldierPuppet.Create` (replaces 45's stand-in line) wears your pick (the head of the month without a campaign) and draws the bots' heads; an unbuilt or unimported head falls back to its stand-in.
- **Campaign**: `GameLoop` has `CurrentHead`, `AvailableHeads`, `PreviousHead()`, `NextHead()`, `SetHead(id)`, `LastUnlockedHead` (the head the last match's win unlocked, else null: for ticket 48), records the month when a match goes live, unlocks on a win (yours > enemy) and calls `WebPortal.HappyTime()` then. `GameLoop.Clock` is the injectable date.
- **Settings**: a Head row (a `GuiSelector` over the unlocked heads, placeholders say so), a turning 3D preview (`GuiHeadPreview`, own camera and render texture, on your team's body) and the month beside the panel; GUI prefabs rebuilt in batch.
- **Tests**: `HeadUnlocksTests` (23), 3 more in `GameSaveTests`; `HeadsPlayTest` (Run, CheckReload) in Play mode.

**Verified**: EditMode 688 passed, 0 failed. Play (`SDF_NO_QUIT=1 tools/unity-batch.sh heads47 -executeMethod SdfCampaign.Editor.HeadsPlayTest.Run`, clock 15 Oct 2026): a new save has only the pumpkin; a 3-0 win unlocks Frankenstein; Settings lists two heads (the second a placeholder); the arrow picks it; the next match asks for Frankenstein, wears the stand-in (the pumpkin) and all 8 heads are distinct. `CheckReload` in a new process: the save has the pick and the match wears it again. Preview capture: `Media/heads-47/settings-head-preview.png`.

**Not verified / left**: a look at the full Settings screen (the long placeholder names rely on the selector's auto-size); the web portal's HappyTime; the real head models (the other 10 heads) in the cycle.
