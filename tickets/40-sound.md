# 40 — Sound

The game had no sound. Now: a sound bank of named cues, a pooled player, adaptive layered battle music, and every event of the game wired
to its cue. Sounds not yet fetched are empty cues, silent until a clip is put in them. Code in `Unity/Assets/Audio`.

## Pieces

- `Audio/Library`: the licensed library (51 files; the five 3.4 s `music_lvl_03_east_*` transitions are not copied, nothing plays them).
  `AudioLibraryImporter` sets the import settings by code: effects mono, Vorbis q0.6, Decompress On Load, 44.1 kHz; stings and the
  the heartbeat and music Compressed In Memory (music stereo, q0.4, loaded in the background; WebGL does not stream).
- `Cues` (names), `Cue` (clips, volume, pitch range, 2D/3D, distance, voice limit, cooldown, loop, priority), `SoundBank`
  (`Assets/Audio/Resources/SoundBank.asset`, made and filled by `SoundBankSetup` at editor load, on import and in
  `Tools > SDF Audio > Update Sound Bank`; it only fills what is missing, so inspector tuning stays). To add a sound: put a clip in the
  cue (or its file in `SoundBankSetup.Defaults`).
- `Audio` (the persistent host, made before the first scene; `Audio.Play(cue, position)`, `Audio.Play2D(cue)`), `AudioPlayer` (40 pooled
  sources, nothing allocated per shot, voice limit and cooldown per cue, 3D sounds beyond range dropped, handles for loops).
- `GameAudio` (on the match's object, made by `Match`): listens to `CombatFx`'s new shot events, the combat, match, flag, spore, machine and
  boss events, the pods; steps, heartbeat, minigun head/loop/tail, machine loops. `UiSounds` for `GuiCommand`.
- Music: `MusicDirector` plays one track of layers started together and looped, so they stay in sync. `MusicIntensity` (plain C#,
  `SdfAudio.Core`, tested) rates the fight 0 to 1 from enemies near (20 m) and in sight (45 m, in front), seconds since you fired and since
  a hit, the enemy flag carried by you (at least 0.7) or your flag carried by them (at least 0.5); the level rises by 0.5 a second and falls by 0.05;
  layer 1 always plays, layers 2 to 4 fade in at 0.15, 0.4 and 0.65 over 0.2. Menus play the in-game loop (orchestral layer 1) quietly, which the Meadow carries on with its other layers (other arenas crossfade, 1.5 s); the boss arena
  plays the boss theme.
- `ArenaAudio` on each arena's SceneInfo (added by the level builder, version 27): the floor (grass: meadow, fungal, arena; wood: toybox,
  kitchen, chessboard; carpet: bathroom, computer) and the music clips (orchestral: meadow, fungal, chessboard, arena; electronic: toybox,
  kitchen, bathroom, computer; the boss theme in the arena). The clips hang off the scene so the streamed web build bundles them with the
  arena (ticket 38). Without the component (scene not rebuilt) the surface comes from the table and, in the editor, the music is loaded by path.

## CC0 sounds (Assets/Audio/CC0, see SOURCES.md there)

Filled by `SoundBankSetup.Defaults()` (files written `CC0/...`; a cue that already has clips keeps them, except the menu cue, which moves
from the old orchestral layer 1 to the new loop). `AudioLibraryImporter` also covers the folder: the effects are forced to mono (they were
encoded stereo), Decompress On Load, Vorbis 0.6, 44.1 kHz; the music is stereo, Vorbis 0.4, Compressed In Memory, loaded in the background.
New cue `FlamethrowerStart` (66 cues): `GameAudio` plays the ignition when the flame starts, the loop following as it ends.

| Cue | Clip(s) |
|---|---|
| flamethrower start / loop / end | flamethrower_start / flamethrower_loop / flamethrower_stop |
| electro-gun | electro_lightning_shot, electro_lightning_shot_b (BMacZero Tesla generator recordings, CC0) |
| electro chain | electro_lightning_chain (0.23 s crackle, pitch 0.95 to 1.05) |
| poison-gun / poison impact | poison_spray, _b / blob_splat_impact_b, poison_spray_b (pitched down) |
| blob-gun / blob impact | blob_shot, _b / blob_splat_impact, _b |
| weapon pickup / health pickup | flag_pickup pitched 1.2 to 1.3 / flag_pickup pitched 1.05 to 1.15 |
| hit marker / head / kill | hit_marker_tick at pitch 1 / 1.5 / 0.62 and rising volume |
| hurt, respawn, jump, land | hurt, respawn, none (jump taken out, silent), land |
| turret / tank / helicopter fire | turret_shot / tank_cannon_shot / turret_shot pitched 0.8 to 0.9 |
| tank engine / helicopter rotor | none: taken out (the hum and the bubbling under the music); the cues stay, silent |
| flag pickup / drop / return / capture | flag_pickup / flag_dropped / flag_returned / flag_capture_jingle |
| flag lost, defeat | flag_dropped pitched 0.8, pitched 0.65 |
| sudden death | library sting_high_02 pitched 0.7 |
| boss phase, boss shield-down | library explosion_medium pitched 0.65 to 0.7, puffball_armour_puff pitched 0.55 |
| spore ready / rush / regrowth / armour | spore_meter_ready / mycelium_rush_whoosh / regrowth_heal_chime / puffball_armour_puff |
| comic panel | none: taken out (reading a comic is quiet), and the viewer raises no event for it |
| menu music | the library's orchestral layer 1, the in-game loop (the CC0 "Flowerbed Fields" was tried and taken out) |

The credits (`CreditsRoll`, and the Credits prefab's baked text) have a Sound section for Kenney and OpenGameArt.

## Left

- A Play-mode listen; the levels (`MusicDirector.MenuLevel/BattleLevel/BossLevel`, the cue volumes in the bank) by ear.
- Only the minigun spin-up cue is still empty (no fitting sound; the code plays it when the barrels begin to turn).
- By ear: the CC0 effects' levels in the bank (all set conservatively, 0.45 to 0.9, under the minigun's 0.7 to 0.8), the pitched reuses
  in the table above, the 106 s menu loop's seam and `MenuLevel`.
- A web build: the initial download grows by about 4 MB (the Meadow's four orchestral layers, the 106 s menu loop that sits once in the bank, and about
  1 MB of effects); the later arenas' music is in their bundles (the orchestral layers also copied into the fungal/chessboard/arena bundle,
  as the player carries them too).

Four sources are WAV, not OGG: the minigun heads (34 ms) and `heartbeat` from the library, and the CC0 `flag_pickup`. Unity's importer
(FMOD inside the editor) could not open them as OGG ("Couldn't perform seek operation" for the tiny clips, "Can't use FMOD_OPENMEMORY_POINT"
for the others), so they were decoded to 16-bit PCM with ffmpeg, keeping their `.meta` files (same GUIDs). Unity compresses them for the
build as it does the rest; a new source that fails to import the same way gets the same treatment.

