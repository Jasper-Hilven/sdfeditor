# 42 — Endless rotation and automatic difficulty

Part of **Minigun Monsters** (41-50). The game boots straight into a match and then plays forever: each match is on a random level,
on a random team, at a difficulty that follows your results.

Depends on: 41. Blocks: 47, 48.

## Decisions

- **Boot**: loading screen, then straight into a match on the Meadow (it is in the initial web download) on a random team. The match
  starts by itself, with a "click to play" overlay because the browser needs a click to capture the pointer and start audio.
- **Rotation**: the next level is random among the 7, but never one of the last 3 played, so it comes from 4.
- **Team**: random at every match (red or blue, 50/50).
- **Difficulty**: automatic, with no setting. It uses the existing ladder Super Easy, Easy, Normal and Hard, starts on Normal, is
  clamped at both ends, and is saved.
  - Lost: the next match is one step easier.
  - Won with the enemy on 0 captures: one step harder.
  - Won with the enemy scoring: the difficulty stays.
- `DifficultyRules` today changes only blue ("the red bots never change"). It must apply to the **enemy** team, whichever side you are on.

## Work (plain C# in Core, EditMode tests first)

- `LevelRotation`: the 7 level ids and a history. `Next(Random)` picks evenly from the levels not among the last 3. Test the
  distribution and that no level repeats within 4 matches.
- `AutoDifficulty.Next(current, MatchResult)`, where `MatchResult` is your team's captures and the enemy's. Test every branch and the
  clamps. A draw cannot happen (sudden death).
- `DifficultyRules.Apply(setup, difficulty, enemySide)`: skill and size go to the enemy side. Drop `easierSteps` and `retryEasier`.
- `GameSave` (replaces the campaign save) holds the difficulty, the last 3 levels, and the heads fields 47 adds. It goes through the
  existing storage (`PlayerPrefsCampaignStorage`, `PortalCampaignStorage` and the CrazyGames Data module). The old campaign save is
  ignored.
- `GameLoop` (runtime, replaces `CampaignRunner`):
  1. boot
  2. Meadow
  3. at the match end, record the result, set the next difficulty and level, and hand over to 48's countdown
  4. load the next level through `SceneLoader` and `ContentStreamer` (the next level's bundle is prefetched during the match)
- The warm-up prompts (`WarmUp`) still run on the first match of a session.
- `WebPortal`: `gameplayStart` and `gameplayStop` around each match.

## Done when

- The EditMode tests for the rotation, difficulty and save pass.
- A Play-mode run of 5 matches through `CampaignPlayTest` (renamed) gives a level sequence with no level inside the last 3, a
  difficulty that moves as the rules say, and the save survives a reload.

## Status

Done in code; EditMode green, the five-match play test passes in Play mode, the reload check passes in a new process.

**Done**
- **Boot and loop**: `GameLoop` (replaces `BootFlow`) is on the persistent Campaign object: Boot starts the Meadow with the loading tip and
  the click to play, on a random team; the warm-up runs on that first match only. A match's level is recorded when it goes live; the next
  level is picked then and prefetched (`ContentStreamer.Prefetch`). The match's end moves the difficulty and writes the save. CONTINUE,
  RETRY and MENU start the next match; RESTART in the pause menu reloads this one without recording it. The HUD comes back after each load
  (the Loading screen used to stay up on a boot match).
- **Rotation**: `LevelRotation`, the seven scene names, never one of the last three. `AutoDifficulty` (ladder, clamps, the three branches).
- **Difficulty**: `DifficultyRules.Apply(setup, difficulty, enemySide)`: skill and size of the enemy side only; `BattleSetup` and
  `MatchSetup` carry `yourTeam`, and `Match.Start` takes it.
- **Save**: `GameSave` (JSON, version 1, key `minigun.save`; difficulty and the last three levels; another version or a broken save reads
  as the default; the old campaign save is ignored). `GameStorage`: the portal first, PlayerPrefs behind it, writes to both.
  `WebPortal.Init` is called again (nothing called it since 41), and the save is written to the portal once the portal is up.
- **Credits**: a CREDITS button in the pause menu (`GuiCommandId.Credits`, appended); the credits roll over the paused match and return to
  the pause menu.
- **Tests**: `LevelRotationTests` (9), `AutoDifficultyTests` (7), `DifficultyRulesTests` (10), `GameSaveTests` (9), all green.
- **Play test**: `GameLoopPlayTest` (replaces `CampaignPlayTest`, which 41 removed): `Run` plays five matches in Play mode, `CheckReload` in a
  new process. `tools/unity-batch.sh` runs it with `SDF_NO_QUIT=1` (another agent added that switch).

**Verified**
- EditMode (`tools/unity-batch.sh tests42 -runTests -testPlatform EditMode`): 656 passed, 0 failed, 0 skipped (656 total); 35 of them new.
- Play test, five matches (`SDF_NO_QUIT=1 tools/unity-batch.sh playtest42 -executeMethod SdfCampaign.Editor.GameLoopPlayTest.Run`): 0 errors.
  Levels: Meadow, Toybox, Computer, Fungal, Kitchen (Toybox was restarted once, from the pause menu). Teams: Blue, Red, Blue, Red, Blue.
  Difficulty: Normal, then after 3-0 Hard, after 1-3 Normal, after 0-3 Easy, after 3-0 Normal, after 3-2 Normal. The enemy side's skill
  matched each (0.50, 0.65, 0.50, 0.35, 0.50). The warm-up ran on match 1 only. CREDITS rolled over the paused match and came back to the pause menu.
- Reload (`... -executeMethod SdfCampaign.Editor.GameLoopPlayTest.CheckReload`, a new process): 0 errors. The Meadow was made for Normal,
  and the saved last three levels were Fungal, Kitchen, Meadow.
- GUI prefab and `Boot.unity` rebuilt in batch (`GuiScene.BuildInBatch`, `BootBuilder.BuildInBatch`).

**Left for a person or a later ticket**
- A Play-mode look at the pause menu with CREDITS, the Loading screen's click on the first match, and the end screens.
- The web build: the CrazyGames SDK init and the save in its Data module are unverified (no portal here). A cross-device save is not merged:
  the first write from this device overwrites the portal's copy.
- The end screens still say CONTINUE, RETRY and MENU; each starts the next match. Ticket 48 replaces them with the countdown.
- Across sessions the first match is always the Meadow (the Boot rule), even if the Meadow is among the last three in the save.
- Ticket 47 adds its head fields to `GameSave` with a new `CurrentVersion`.
- `GuiCommandId.RetryEasier` is still in the enum (removing it would shift the saved values of the other commands); nothing sends it now.


**First play in the Fungal level (2026-11-02, user request: "the first time someone plays this game, he should play the fungal scene,
also move the tutorial to there").**
- `LevelRotation.First`: on a first play (nothing in the save's last levels, or only unknown ones: `IsFirstPlay`) Boot starts in the Fungal
  level (`LevelRotation.FirstPlayLevel`) instead of the rotation's pick. A `streamcheck` URL still forces its level. The Fungal level is
  then in the save's last three like any played level, so the rotation goes on without it for three matches; later sessions start on the
  rotation's pick as before.
- The warm-up (`WarmUp`, `WarmUpPlayer`) runs on that first match only: `LevelRotation.WarmUpOn(level)` is a first play and the Fungal
  level. Before, it ran on the first match of every session, on any level; a returning player no longer gets it, and a forced first level
  on a new save has none. Its level data is the scene's own: the prompts need nothing placed, and the arrow and the minigun prompt use the
  minigun pod of your side, which `WarmUpPlayer` now finds by your team (it took the west's, red's, so a blue player was sent across the
  map). The Fungal level's red pod is at (-46, 0, 38), about 65 m from the red spawn (-70, 0, -22.5), to the spawn's left (the Meadow's was
  36 m); blue's mirrors it.
- Tests: `LevelRotationTests` `AFirstPlayStartsInTheFungalLevel`, `AfterTheFirstPlayTheRotationGoesOnWithoutTheFungalLevel`,
  `TheWarmUpIsInTheFungalLevelOnAFirstPlayOnly`; the fresh-game half of `TheFirstLevelFollowsTheRotationAndNeverTheLastThree` (any of the
  seven) is gone. `GameLoopPlayTest` expects the Fungal level and the warm-up on the clean save's first match, and no warm-up on the reload.
  EditMode: 737 passed, 0 failed.
- Play test (`SDF_NO_QUIT=1 tools/unity-batch.sh playtest42 -executeMethod SdfCampaign.Editor.GameLoopPlayTest.Run`): 0 errors. Levels:
  Fungal (on Blue, with the warm-up), Meadow, Bathroom, Chessboard, Toybox. Reload (`CheckReload`): 0 errors, first match Bathroom with no
  warm-up, saved last three Toybox, Kitchen, Bathroom.
- Not looked at in Play mode by a person: walking to the Fungal pod with the arrow (the distance and the woods between). Losing the first
  match before the warm-up is done is not a first play any more (the level is recorded when the match goes live): the warm-up is not shown again.
