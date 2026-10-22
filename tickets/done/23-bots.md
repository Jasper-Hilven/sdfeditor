# 23 — Bots

Seven bots, so a match plays 4 vs 4.

Depends on: 20, 21, 22.

## Decisions (from the user)

- **Bots only** for now; later they get fake multiplayer names.
- They **play individually**, no team plan: each bot picks its own goal, and the mix differs per bot and per moment.

## Navigation

- Unity's AI Navigation package (`com.unity.ai.navigation`): a navmesh baked per battle scene by the level builder, so it stays in
  sync with the level (no hand-made data). It covers the ground, the castle courtyards, stairs and wall-walks; jumps are off-mesh
  links only where needed. WebGL-safe (no compute).
- The navmesh also serves ticket 21's "unreachable" test for dropped flags.

## Behaviour

- Each bot has a temperament (weights for: attack the enemy flag, defend its own, chase the enemy carrier, hunt players, fetch a
  weapon or health) and picks a goal from them and the situation, reconsidering every few seconds or on events (its flag taken,
  carrier seen, low health). Two bots of a team may pick the same goal; that is fine.
- Sight: a view cone and line of sight; it remembers the last seen position for a few seconds.
- Aim: a reaction time and an aim error that shrinks while it tracks a target, so they are beatable; one skill setting.
- Weapons: it switches to the weapon that suits the distance (ticket 22's "good for").
- Moves with the same controller rules as you (ticket 20), so it gets no speed or jump advantage.

## Done when

A full match on the fungal level plays itself out with 8 bots (you as a spectator or as a player): flags taken, dropped,
returned and captured, a winner within the time limit most rounds. Bots do not get stuck on the stairs or the walls (watch a few
rounds). Frame time within the web budget with 8 players.

## Status

**Done in code; left: the Play-mode check of "Done when" in Unity** (and the EditMode tests in the Test Runner). Built:

- **Rules in plain C#** (`Assets/Battle/Core`, `SdfBattle.Core`): `BotSkill` (one setting, `level` 0-1: reaction 0.65 → 0.2 s, aim
  error from 14 → 5 deg shrinking to 3.2 → 0.7 deg, settle time, a tracking lag for a target crossing the view, turn speed 240 → 540
  deg/s, view cone 55 → 70 deg each side, sight 70 → 110 m, hearing 4 → 7 m, memory 3 → 5 s), `BotAim` (reaction time, then the
  error shrinking while it tracks, wandering round the target; the same target again keeps its tracking, a new one starts over),
  `BotSight` (view cone measured level, hearing range) and `BotMemory` (last seen place per player), `Temperament` and `BotStyle`
  (raider, guardian, hunter, scavenger, all-rounder: weights for attack, defend, chase, hunt, fetch; each team takes the styles in turn
  from a random start, each weight varied ±25%, so every team is a mix and no two matches the same), `BotBrain` (goals: bring the
  flag home, take the enemy flag, return its own dropped flag, chase its flag's carrier, defend its flag, hunt, fetch a weapon, fetch
  health incl. a little for the overheal to 200; scored from the temperament, urgency and nearness, a bonus for the goal it has, a
  little chance; a carrier always heads home; no team plan), `WeaponChoice` (ticket 22's "good for" as a suitability per weapon and
  distance: flame ≤ 8 m, dual pistols close-mid, minigun 10-35 m, sniper and electro-gun far, electro-gun and poison more on a group,
  poison more defending, blob-gun on the carrier; switching only for a clear gain, at most once a second), `Ballistics` (the low arc
  and aim pitch for the lobbed poison and blob, with the lead by flight time).
- **EditMode tests** (`Assets/Battle/Tests`): `BotBrainTests` (12), `BotAimTests` (6), `BotSightTests` (3), `WeaponChoiceTests` (8),
  `BallisticsTests` (6). All 147 battle tests (with tickets 20-22's) pass outside Unity (net8 + NUnit against Unity's CoreModule).
- **Navmesh** (`com.unity.ai.navigation` 2.0.9 added to the manifest): `Assets/Battle/Editor/BattleNavmesh.cs`, called by the level
  builder (version 17) after the battle objects: Level/Navigation Scout and Level/Navigation Tank, NavMeshSurfaces over the play area
  from 12 m below to 7.5 m above the spawns (courtyards, stairs, wall-walks and tower rings in; caps, keep top, the tops of the walls
  round the play area out), from the level's physics colliders (the castles are drawn instanced, their colliders are what the scene
  has), the player and Battle left out by NavMeshModifiers. Agent types made in ProjectSettings/NavMeshAreas.asset: **Scout** r 0.35
  (the 0.3 capsule and 5 cm to spare), 1.75 m, step 0.4, slope 45, drop links down ledges to 6 m (the only links; jumping up is the
  stuck fallback); **Tank** for ticket 24: r 0.45, 0.8 m, step 0.15, slope 30, no drops. Data at
  `Assets/Level/Meshes/<level>/NavMesh <agent>.asset` (generated, ignored by git). Tools > SDF Battle > Check Navmesh reports per level
  (without opening scenes): spawn to both stands and every pod reached, how much of each castle's upper floors joins the ground, open
  ground spots on a 3 m grid not reached.
- **Unity** (`Assets/Battle/Runtime`): `Bots` (on Battle, made by `Match.Start` after combat: a `Bot` per bot player, the shared
  `skill`, the pods, smoothed velocities, line of sight, events: a flag taken / dropped / returned / captured makes bots think again,
  a hit turns the victim to the shooter; plugs `BotNavigation.Reachable` into `CaptureTheFlag.NavmeshCheck`: a dropped flag is
  reachable when the navmesh on it, or below and beside it within a jump, has a complete path from the red spawn), `BotNavigation`
  (plain NavMesh queries, no NavMeshAgent; straight lines without a navmesh), `Bot` (drives `FirstPersonController.SetMove/SetAim/Jump`
  and `Arsenal.SetTrigger/SetZoom/Select` only, so the same rules as you: walks the path's corners, repaths every 1-3 s or when the
  goal moves; strafes in fights (checked against the navmesh's edge), holds its weapon's distance when hunting; stuck: jump, step
  aside, new path, new goal; senses five or six times a second; fires once its view is on its (erroneous) aim point, the sniper zoomed
  and after a steadying moment, the minigun through small turns; gizmos: path, goal and target in the Scene view), `BotSpectator`.
- **Spectating**: Tools > SDF Battle > Spectate Bots (an editor preference, or `Match.spectate`): you are no player, the match is 8
  bots, the camera follows one (Tab next, Shift+Tab back, F free camera: right mouse to look, WASD, Q/E, Shift); the HUD shows Red 1
  (the match's first player). Shared files touched: `Match.cs` (the `spectate` field, the `Bots.Create` call), `LevelBuilder.cs` (the
  bake call, the play area corners, version 17).
- **Frame cost** (estimated, not profiled yet): per bot per frame a few vector sums; ~5 sight checks a second with one ray per enemy in
  view; a path every 1-3 s; no threads, no compute, the same in WebGL 2.

Left (in Unity): let it compile and rebuild the scenes (version 17 rebuilds them by itself; else Tools > SDF Level > Rebuild), Tools >
SDF Battle > Check Navmesh, the EditMode tests, and on `Fungal.unity` in Play mode a few rounds spectated and played: flags taken,
dropped, returned and captured, a winner within 10 min most rounds; watch the keep stair, the wall-walks and the gate for stuck bots,
and the frame time with 8 players (web build). Tuning: `Bots.skill`, the temperaments in `Temperament.Of`, the scores in `BotBrain`.
