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

Not started.
