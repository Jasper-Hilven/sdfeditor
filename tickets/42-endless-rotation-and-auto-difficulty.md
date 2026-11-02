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

Not started.
