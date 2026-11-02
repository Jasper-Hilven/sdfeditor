# 41 — Remove the story, the boss and the campaign

Part of **Minigun Monsters** (tickets 41-50). The game stops being a story campaign. The intro, the beats, the comics, the ending, the
boss and the campaign flow are deleted. What is left is a battle on one of the 7 levels, which 42 turns into an endless loop.

Depends on: nothing. Blocks: 42, 48, 50.

## Decisions

- The levels stay exactly as they are: Meadow, Fungal, Chessboard, Toy Box, Kitchen, Bathroom and Computer. The Champion's Arena is
  removed completely.
- The boss is removed completely: Chanterelle Prime, its three phases, its crew and the golden minigun.
- Credits stay in the menu. The user's uncommitted `Credits.cs` and `Credits.prefab` changes are not touched.
- The menus no longer have the desk diorama backdrop (ticket 35). The pause menu draws over the running match.

## Work

- **Boss**:
  - `content/levels/arena.level`, the Arena scene and its build entry.
  - Prime's models and materials (`content/models/characters/prime`, `materials/characters/prime`) and `minigun-gold`.
  - `BossBots`, the boss rules and their tests, the boss HUD and the boss copter.
  - `IsBoss` branches (for example in `DifficultyRules`), the boss music in `ArenaAudio` and `MusicDirector`, and `HappyTime` on the boss.
- **Story**:
  - `content/story`, `Assets/Story` (viewer, panels, screens, comics in Resources), the pipeline's `SDFPipeline/Story`, and `Home.unity`
    with its room models if nothing else uses them.
  - `Beat`, `BeatSequencer`, `BeatPlayer`, `BeatWorld`, `ComicBeatPlayer`. `Taunts` stays only if the kill feed or bots use it.
- **Campaign**:
  - `CampaignDefinition`, `CampaignStep`, `CampaignRunner`, `CampaignProgress`, `Medals` (unless 47 reuses it), and the level select.
  - The title screen, RETRY EASIER and the difficulty picker.
- **Menu backdrop**: `MenuBackdrop`, `DeskDioramaBuilder`, `WallAtlasBuilder`, and the comic thumbnail atlases.
- **Streamed web build** (38): no comic and arena bundles. The initial download keeps Boot, the GUI and the Meadow.
- **Temporary flow** until 42: Boot loads the Meadow, and a match end returns to the Meadow.

## Done when

- Nothing references the deleted code or assets: the project compiles, and the EditMode tests pass (the deleted tests go with their code).
- Boot runs into a Meadow match with no title or story screen. The web build runs.
- The build is smaller. Note the before and after size of the initial download and of the bundles.

## Status

Done in code, Unity compiles, EditMode tests pass, the web build runs in batch. Not done as the ticket asks: the pipeline's boss layout (below).

**Done**
- **Boss**: Prime, the Champion's Arena (`arena.level`, its scene, its models and materials, `minigun-gold` and its pod), the boss
  rules, the copter, the boss HUD and the boss tests are deleted. `IsBoss` branches gone from `DifficultyRules`, `MatchSetup`,
  `MatchRules` (the `MatchObjective` enum is gone), `BattleSetup`, the bots, `Cheats`, `Machines`, the navmesh bake, the audio (boss theme,
  boss cues), `MatchHud`, `GuiEndBanner`.
- **Story**: `Assets/Story`, `content/story`, `content/scenes/home.txt`, `Home.unity` and its models and materials, `SDFPipeline/Story`
  and its test, the `StreamedContent` comic bundles, the comic code in `ContentStreamer` and `StreamedBundles`, `Beat*`, `ComicBeatPlayer`,
  the story viewer hook, `Taunts` (its only users were the campaign and the Defeat screen's Prime taunt; the kill feed and bots never
  used it), the Cutscene and Ending screens.
- **Campaign**: `CampaignRunner`, `CampaignDefinition`, `CampaignStep`, `CampaignProgress`, `Medals` (its users: the Title and Victory
  medal, the boss and campaign code; ticket 47's text does not mention medals), `StreamPlan`, the campaign tests and the play tests.
  Title, Level Select, Difficulty, RETRY EASIER and the Story gallery screens are gone; the component Gallery (developer tool) stays.
  The Settings rows for difficulty and backdrop are gone.
- **Menu backdrop**: `MenuBackdrop`, `DeskDioramaBuilder`, `WallAtlasBuilder`, the wall atlases and `DeskScreen.mat`, `WallCard.shader`.
  `GuiPlayTest` removed (it drove the removed screens).
- **Kept**: `Credits.cs`, `Credits.prefab`, `CreditsPlayer`, `CreditsMenu` (the user's credits work is untouched; `CreditsMenu` now
  shows the credits over the HUD), `Difficulty.cs` (the ladder for 42), `SkipHold` (moved out of the deleted `BeatSequencer.cs`, used by the
  credits and the warm-up), the warm-up, `SceneLoader`, `ContentStreamer`, `StreamManifest`, `CampaignStorage` and its tests (42 uses them).
- **Temporary flow**: `Campaign/Runtime/BootFlow.cs`. Boot starts the Meadow; the commands that start, retry or leave a match (Continue,
  Next, Retry, Restart, QuitToMenu and the others) load the Meadow again. `SceneLoader.Abandoned` does the same for a cancelled download.
  `Boot.unity` rebuilt by `BootBuilder` (no backdrop, no menus); `UpdateAll` rebuilt the seven levels, `GUI.unity`, `Mushrooms.unity`
  and the build scene list (Boot, Meadow, Fungal, Chessboard, Toybox, Kitchen, Bathroom, Computer; showcases in development only).
- **Streamed build**: the arena and the comics are out of the bundles. The other six levels still stream.

**Measured** (WebGL, `SdfMushrooms.Editor.WebBuild.Build`, batch)
- Before: initial download `Web/Build` 29.3 MB (Web.data.br 20.96 MB, Web.wasm.br 9.68 MB, framework and loader 0.11 MB); the
  streamed `Bundles` folder 51.0 MB in 35 files, including the arena and eight comics.
- After: initial download 23.0 MB (Web.data.unityweb 14.3 MB, Web.wasm.unityweb 9.56 MB, framework and loader 0.20 MB); bundles 37.0 MB in
  25 files (no arena, no comics). The "before" figures are the numbers the previous build folder held; the files were overwritten by
  this build.

**Tests**
- EditMode (`tools/unity-batch.sh ... -runTests -testPlatform EditMode`): 425 passed, 0 failed, 8 skipped, 433 total, from 641 passed
  before (the deleted boss, story and campaign tests went with their code). The 8 skips are `SdfRig.Tests.SoldierBodyTests`, which need
  ticket 43's generated soldier prefabs.
- Pipeline (`dotnet test SDFPipeline.Tests`): 6 failed, all in `Levels/BattleSidesTests` (the arena cases and the boss layout), which are
  now removed from that file. The filtered re-run of `BattleSidesTests`, `LevelFileTests` and `BackdropTests` is in the commit notes.

**Left for a person or a later ticket**
- A Play-mode look: Boot into the Meadow, a match end, the Victory and Defeat screens with no medal or taunt, the pause menu over the
  match, and the web build run in a browser.
- The pipeline still has the boss layout code: `LevelLayout.Boss`, `BossSpec`, the `boss` section and `phases` tags in `LevelFile` and
  `LevelBaker`, `BattleLayout`, `BattleNavmesh`'s asymmetric parts. No level uses it now. Removing it needs its own pass with the
  pipeline tests.
- Dead code left on purpose, to remove when it is in the way: `WarmUpPlayer` (its driver was `CampaignRunner`; the warm-up is now
  not started), the boss cues in `Cues.cs`/`SoundBankSetup.cs` (`boss.phase`, `boss.shield-down`, `boss.crash`), the boss theme file in
  `Audio/Library`, `PhaseGroup`, the Victory and Defeat `GuiEndScreen` fields from the old design, and the `Story/Resources` comics that
  `StreamedBundles` no longer reads.
- Nothing reaches the credits from the game now: the title that held their button is gone (the Tools menu's Play Credits still works).
- 42 starts from here: Boot and the match end both go through `BootFlow`, which 42 replaces.
