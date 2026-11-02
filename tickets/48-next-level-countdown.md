# 48 — The next-level countdown

Part of **Minigun Monsters** (41-50). After every match, won or lost, an end card counts down to the next level. During it you can
switch your head without going to the menu.

Depends on: 42 (the loop), 47 (the heads and unlocks).

## Decisions

- Countdown: 10 s, "Next level in 10…", then the next level loads by itself. There is no button to press, though Space or Enter may
  skip the rest of the countdown.
- The keys in the **A and D positions** switch to the previous or next unlocked head (by physical key position, so Q and D on
  AZERTY). The card shows the key letters for the user's layout, as the spore HUD does.
- The card shows:
  - WIN or LOSE, and the score
  - the next level's name and card
  - your head on your body, with ◀ ▶
  - "NEW" and a short reveal when this win unlocked a head (the new head is chosen for you, and you can switch back)
  - a small line when the difficulty changed ("Tougher opponents next" / "Easier opponents next")
- The CrazyGames midgame ad plays at the end of the countdown, under the existing `AdBreakPolicy`, before the next level loads.

## Work

- `GuiEndScreen` becomes the countdown card. Remove CONTINUE, RETRY and RETRY EASIER.
- The input goes through the Input System by physical key (as the spore keys in ticket 25). It is ignored while the pause menu is open,
  and the countdown pauses with the menu.
- The next level's bundle is already prefetched (42). If it is not ready at 0, the Loading screen shows with its retry.
- Bots on the winning side play a victory clip or dance (44's stand-in) behind the card, if the camera shows the field.

## Done when

- In Play mode: win and lose both count down and load the next level. A/D switch heads on QWERTY and AZERTY, and the head is worn in
  the next match. The unlock reveal shows on an unlocking win. The ad hook is called once per match end in the editor's simulate-ads
  mode.

## Status

Done in code; EditMode 721 pass, 0 fail (`tests48`, XML); the five-match play test and the reload check pass.

**Done**
- **Core** (`SdfCampaign.Core`, `NextLevelCard.cs`): `NextLevelCountdown` (10 s unscaled, Space or Enter skips, paused while `flow.IsPaused`,
  finishes once), `OncePerMatch` (the midgame ad asked once per match end), `NextLevelCard.DifficultyLine` ("Tougher opponents next" /
  "Easier opponents next"), the countdown and level-name text. Tests: `NextLevelCardTests` (12).
- **Loop** (`GameLoop`): a win that unlocks a head chooses it (`HeadUnlocks.SetCurrent`), so the next match wears it; `DifficultyBefore` for
  the line; `ContinueAfterCountdown` runs `WebPortal.MidgameBreak` then `Next()`; `StepHead` for ◀ ▶ and A/D; `WebPortal.BattleFinished` at each end.
- **Card** (`GuiEndScreen`, rebuilt in `GuiScreens`): CONTINUE, RETRY and RETRY EASIER removed. Shows the score and stats, "NEXT  <LEVEL>",
  the difficulty line, "Next level in N…", your head on the preview with ◀ ▶ and "A and D change head" (the keys' layout letters), "NEW HEAD!"
  when the win unlocked one. A and D by physical key (`GameKeys`, read from the Input System); Space or Enter skip. The countdown's end fires
  `GuiCommandId.NextLevel`. The winning side's bots try `body_victory`, then the dance stand-in, each frame until they dance (not checked).
- **Keys**: `GameKeys.Source` (a test seam: the keyboard the keys are read from, null = the machine's), `GameKeys.SkipHeld()`.
- **WebPortal**: `MidgameBreaks` counts calls to `MidgameBreak` (the check's count).
- **GUI prefabs** rebuilt in batch (`GuiScene.BuildInBatch`).
- **Play test**: `GameLoopPlayTest` extended (see below).

**Verified**
- EditMode (`tools/unity-batch.sh tests48 -runTests -testPlatform EditMode`): 721 passed, 0 failed, 0 skipped (721 total; the 12 new are here).
- Play (`SDF_NO_QUIT=1 tools/unity-batch.sh play48 -executeMethod SdfCampaign.Editor.GameLoopPlayTest.Run`): 0 errors. Five matches, each
  ending by its countdown or Space: 3-0 (Hard), 1-3 (Normal), 0-3 (Easy), 3-0 (Normal; Space skipped), 3-2. "Next level in 10" counts down (checked
  to be 1.5 s lower after 2 s). NEW shows on the first win (Evil Santa, the unlocked head) and on the 3-0 (Abominable Snowman) but not the losses.
  The difficulty line shows "Tougher" after the wins that moved it and "Easier" after the losses that moved it; none on the 3-2 win.
  A then D on the card on match 1 brought the head back to the same one (Evil Santa), and the next match's soldier wears the head the card
  left on (checked on each match). The midgame ad hook was called once per match end (`MidgameBreaks`), each time before the next level loads.
  Match 2 restart from the pause menu and CREDITS on match 3 still work.
- Reload (`... -executeMethod SdfCampaign.Editor.GameLoopPlayTest.CheckReload`, new process): 0 errors.

**Not verified / left**
- The simulated ad itself: batch Unity has no portal and `WebPortal.AdsAllowed` stays false, so no ad is shown; only the hook's calls are counted.
  Turning the ad on needs the user's decision (CrazyGames' rules).
- AZERTY: the keys are read by physical place (checked on the test's keyboard, QWERTY layout), but the layout's letters on the card were not
  seen with an AZERTY keyboard here.
- The pause menu cannot be opened from the end card, so the countdown's pause rule is wired (`flow.IsPaused`) but not exercised in play.
- The victory dance: wired; not looked at in play.
- The "next level's card" is its name only (no level picture exists in the project).
- The Defeat tip, the Victory banner text ("VICTORY!"/"DEFEATED") are kept from 32.6.
