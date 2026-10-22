# 20 — Match core: players, teams, spawning, the match

The base of the capture-the-flag game (tickets 20-23): a match of **4 vs 4**, red (west, amanita scouts) against blue (east,
chanterelle scouts), in a battle scene (first the fungal level). You are one of the eight; the other seven are bots (ticket 23).
This ticket makes the players, the teams, the spawning and the match's clock and phases; no flag rules (21), no combat (22).

Depends on: 17 (battle sides), 18 (red vs blue).

## Decisions (from the user)

- 4 vs 4, **bots only** for now. Fake multiplayer names for the bots come later; until then plain names (e.g. "Red 2").
- Keyboard and mouse: WASD, mouse to aim, **Space to jump**. No sprint, no crouch. The web build captures the mouse (pointer
  lock) on click, Escape releases it.
- On death: **5 s** cooldown, then back at the team's spawn zone.
- **Spawn protection**: 2 s without damage after spawning, ended early by firing.
- Falling off the island (or out of the play area below a kill height) is a death.
- Match: **first to 3 captures** wins; **10 min** time limit, then most captures wins, on a tie **sudden death** (next capture
  wins). After the win the result screen, then **10 s** later a new round with everything reset.
- Friendly fire off.

## Players

- `Player` (one per participant, human or bot): team, display name, health 100 (the health itself is 22's, the field is here),
  alive/dead, respawn timer, spawn protection timer. The human's controls drive it through `FirstPersonController`; a bot's brain
  (23) drives the same movement API, so both move by the same rules (speed, slope, step, gravity, jump).
- `FirstPersonController`: add jump (about 1.1 m high, grounded check, no double jump) and a speed multiplier other systems set
  (the flag carrier's 0.9 in 21).
- **Bodies**: every player but the one you are is drawn as their team's scout (amanita-scout red, chanterelle-scout blue prefabs), a
  capsule collider for hits, the floating eyes and hands following the aim. Your own body is not drawn (first person).

## Spawning

- The single spawn marker per side (ticket 17) becomes a **spawn zone**: in `content/levels/*.level` `spawn at=(x, z) yaw=..`
  gains a `radius` (default 3 m), and the bake writes it; `BattleSide.spawn` keeps the centre, a `spawnRadius` field is added.
  Players spawn at a free spot in the zone (not inside another player), facing the yaw. Check that the fungal level's zones lie on
  open, flat ground inside each castle; widen or move them if not (tests as in 17).

## The match

- `Match` (replaces `FakeMatch` in battle scenes; `FakeMatch` stays for the GUI showcase): phases countdown (3 s) → playing → round
  end (result) → next round; the score per team, the clock, sudden death. Rules elsewhere (21, 22) report to it through events
  (captured, killed, died); it owns resets.
- The HUD reads the match through the interface it reads `FakeMatch` by now (`GuiViewModel` or what the GUI uses), so the views stay;
  capture-the-flag specific HUD is ticket 21.
- Scene setup: the level builder (or a runtime bootstrap reading `BattleArena`) creates the eight players at match start; nothing is
  hand-placed in the scene (README: Unity never holds hand-made level data).

## Done when

In Play mode on the fungal level: you spawn in the red castle with 7 idle placeholder players (standing at their spawns), can walk
and jump, die by falling off the island and respawn after 5 s, the clock runs and the round ends at 10 min. EditMode tests for the
match's phase and score logic (Unity Test Framework), `dotnet test` for the level-file change.

## Status

**Change after ticket 24 (the user):** no countdown at a round's start: the players were frozen for 3 s for nothing. `Match.rules` now
starts with `countdownSeconds = 0` (the scenes rebuild with it, LevelBuilder version 19), so everyone moves at once; `MatchRules` keeps
the countdown as a setting (3 by default, which the rules' tests use).

**Done in code, the pipeline and the bake; left: the Unity side checked in the open editor** (scene rebuild, Play mode, the EditMode
tests run in the Test Runner). Built:

- **Rules in plain C#** (`Assets/Battle/Core`, assembly `SdfBattle.Core`, no scene needed): `MatchRules` (4 per team, first to 3, 600 s,
  3 s countdown, 10 s result, 5 s respawn, 2 s protection, health 100, friendly fire off), `Team` (red = west = 0, blue = east = 1, the
  `BattleSideId` values), `PlayerState` (team, name, bot or you, health field, alive, respawn wait, spawn protection, kills, deaths,
  captures), `Death` (victim, cause fell/killed, killer player or a machine's name and team, weapon; only an enemy player gets the kill),
  `MatchState` (phases countdown → playing → round end → the next round's countdown; score per team; clock; sudden death on a tie
  with no time limit; respawns; protection; `ReportCapture`, `ReportDeath`/`ReportFall`, `ReportFired`, `CanHurt`; events `RoundReset`
  (before anyone spawns: 21 sends the flags home, 22 empties inventories), `PhaseChanged`, `Spawned`, `Died`, `Captured`,
  `SuddenDeathStarted`, `RoundEnded`), `SpawnPicker` (a random free spot in the zone, 0.8 m from other players, the body inside the
  zone; the least crowded spot when the zone is full).
- **EditMode tests** (`Assets/Battle/Tests`, assembly `SdfBattle.Tests`; `com.unity.test-framework` 1.6.0 in the manifest):
  `MatchStateTests` (20: defaults, countdown then play, 4 vs 4, third capture wins, captures only while playing, time up with a leader,
  tie → sudden death → next capture wins, the 10 s result then a full reset, 5 s respawn, one death per life, kill credit for enemy
  players only and none for a machine, 2 s protection and firing ending it, friendly fire off, no damage in the countdown, respawns
  during the result, a late player) and `SpawnPickerTests` (6). All 26 pass when run outside Unity against the compiled assemblies;
  in Unity: Window > General > Test Runner > EditMode > Run All.
- **Unity** (`Assets/Battle/Runtime`): `Match` on the Battle object (added by the level builder with you, the two scout prefabs and the
  GUI prefab): makes you (the scene's player) and seven bots (Red 2-4, Blue 1-4, under Battle/Players: capsule, controller driven by
  no input yet, the team's scout), ticks `MatchState`, puts a spawned player at a free spot of its side's zone facing the zone's way,
  hides a dead one (body off, capsule off), freezes everyone in the countdown, reports a fall below the lowest spawn − 12 m or out of
  the play area below the lowest spawn − 3 m. F8 (editor and development builds) or the component's context menu "Test: Drop You Off
  The Island" throws you past the island's edge, as the 20 m walls round the play area keep anyone from falling off by walking.
  `Player` (the body: `State`, `Controller`, `Capsule`, `Appear`, `Vanish`, `Player.Of(collider)`), `ScoutBody` (eyes look with the
  pitch, hands swing with it, both bob; colliders on the model removed). `BattleSide` gains `spawnRadius`, `Team`, the zone's circle in
  the gizmo.
- `FirstPersonController`: one movement API for you and bots (`SetMove`, `SetAim`, `Jump`, `Teleport`, `speedMultiplier`, `frozen`,
  `readsInput`, `Pitch`/`Yaw`/`AimDirection`), jump 1.1 m from the ground only, a head against a ceiling ends the rise, **no sprint**
  (Shift run removed), no pointer lock from a click on a GUI button; `Configure` gives every player the same capsule.
- **HUD**: `MatchHud` makes the GUI prefab in the battle scene (test panel off, an event system if none) and writes the match into its
  `FakeMatch` every frame (`FakeMatch.driven`: no fake bots, no clock, firing does nothing): phase, clock, countdown, the roster in team
  colours, alive, kills, your health, the death screen ("The island got you" for a fall), the result. `GuiFlow` (driven only): back to
  the HUD on respawn and at the next countdown, the result also from the death screen. The views are unchanged; the HUD still says the
  battle-royale things (rings, "last mushroom standing") until ticket 21's capture-the-flag HUD.
- **Level file**: `spawn at=(x, z) yaw=.. radius=..` (0.5 to 10 m, 3 by default), parsed into `BattleObject.ZoneRadius`; a spawn's
  footprint is its whole zone, so the overlap, clearance, play-area and scatter checks cover the zone; level.json writes the spawn's
  `radius`. The 3 m zones ran into the castles' gate walls: fungal now `spawn at=(∓57.5, 0) radius=2.5`, meadow `radius=2` at (∓57, 0)
  (the courtyard between flag and gate allows no more), chessboard the default 3 m. Tests: `LevelFileTests` (the radius, its default,
  a pod given one, out of range, an unknown parameter), `BattleSidesTests.SpawnZones_LieOnLevelGround` (under 0.2 m from lowest to
  highest in every zone) and the bake's `radius`. Levels re-baked; `LevelBuilder` version 15 (Match on Battle, the zones), so the three
  scenes rebuild by themselves when the editor reloads.

Left (in Unity): let it compile and rebuild the scenes (or Tools > SDF Level > Rebuild), run the EditMode tests, and the Play-mode check
of "Done when" on `Fungal.unity`.
