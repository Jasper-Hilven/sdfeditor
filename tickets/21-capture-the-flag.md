# 21 — Capture the flag

The rules of the flag on top of ticket 20's match.

Depends on: 20.

## Rules (from the user)

- Walk into the **enemy** flag stand (or the enemy flag where it lies) to take the flag. It is carried on the player's back, high
  and visible.
- The carrier keeps **all weapons**, no limits, but walks **10% slower** (speed multiplier 0.9).
- **Capture**: bring the enemy flag to your own flag stand **while your own flag is at home**. +1 for your team; the captured flag
  goes back to its stand.
- **Drop**: when the carrier dies the flag falls where they died (on the ground below, if they died in the air).
- **Return**: a player of the flag's own team touching their dropped flag sends it home at once. Untouched, a dropped flag goes
  home by itself after **30 s**.
- **Unreachable**: a flag dropped where nobody can get it (fell off the island, below the kill height, outside the play area, or not
  on the bots' navmesh from 23 once that exists) goes home **at once**.
- First to **3** wins (ticket 20 owns the clock, sudden death and the reset).

## Unity

- `FlagStand` (ticket 17) gains the state: at home / carried (by whom) / dropped (where, time left). The flag object moves between
  the stand, the carrier and the ground; `ReturnFlag()` resets it.
- Triggers: the stand's and the dropped flag's, reacting to `Player` colliders only.
- Events to the match: taken, dropped, returned (by whom or by time), captured (by whom).

## HUD

- Team score red n – n blue with three pips each, replacing the battle-royale rings and players-left.
- A flag status per team: at home / carried by <name> / dropped (seconds left), and an on-screen marker pointing at each flag when it
  is not at home.
- Banners: "You have the flag", "Blue took your flag", "Red scored", "Your flag was returned".
- A tall glow column of the team colour over each flag (at the stand or where it lies), readable from across the field.

## Done when

EditMode tests for the flag state machine (take, capture only with own flag home, drop, return by touch, 30 s reset, unreachable
reset, carrier speed). In Play mode with ticket 20's idle bots: you can take the blue flag, carry it home and score three times to
win.

## Status

**Done in code; left: the Unity side checked in the open editor** (the EditMode tests in the Test Runner, the Play-mode check of
"Done when" on `Fungal.unity`). No scene, prefab or level-data change: everything is added at runtime by the match. Built:

- **Rules in plain C#** (`Assets/Battle/Core`, `SdfBattle.Core`): `FlagRules` (carrier speed 0.9, a dropped flag home after 30 s),
  `TeamFlag` (per team: `Status` home / carried / dropped, `Carrier`, `Position` where it lies, `ReturnIn`; `FlagReturn` why it went
  home: touched, timed out, unreachable, captured, round reset), `CaptureTheFlagState` on a `MatchState`: `TouchStand(player, stand)`
  (the enemy stand with its flag home takes it; your own stand with the enemy flag while your own is home captures, through
  `MatchState.ReportCapture`, and the captured flag goes home), `TouchFlag(player, flag)` (a dropped flag: its own team returns it, the
  enemy takes it), `Drop` on `MatchState.Died` (any death: ticket 20's fall, ticket 22's kills), `Tick` (the 30 s), `ReportUnreachable`,
  `Reset` on `RoundReset`, `SpeedMultiplier(player)`; touches count only while the round is playing and for a living player. Hooks the
  scene sets: `DropPoint` (the ground below the body; null = no ground = home at once) and `IsReachable`. Events `Taken`, `Dropped`,
  `Returned` (reason, by whom), `Captured`, `Changed`.
- **EditMode tests** (`Assets/Battle/Tests/CaptureTheFlagStateTests.cs`, 25): defaults, take, no taking your own or a carried flag,
  carrier speed, capture, no capture while your flag is carried or dropped (then returned, then captured), three captures win, nothing
  in the countdown or by the dead, drop on death at the drop point with 30 s, pick-up by the enemy, return by touch, only a dropped flag
  reacts, 30 s reset, the wait restarting on a new drop, unreachable and no-ground resets, a late unreachable report, the round reset,
  sudden death, detaching. All pass outside Unity (with ticket 20's and the rest of `Assets/Battle/Tests`, 88 in all at the time).
- **Unity** (`Assets/Battle/Runtime`): `CaptureTheFlag` (added to Battle by `Match.Start` through `CaptureTheFlag.Attach`, after the
  HUD and before the first round): a `FlagTrigger` on each stand (capsule r 1.15 m, 2.5 m high) and one on each flag (r 0.8 m, kinematic
  body), live only while the flag lies; triggers react to `Player.Of(collider)` only, so machines (24) and anything else are ignored;
  enter and stay, so a carrier waiting on the stand scores the moment their flag comes home. The flag object moves with its state: the
  stand (`FlagStand.ReturnFlag`), the carrier's back (pole foot 0.55 m up, 0.34 m behind, leaning back 12 degrees, cloth trailing; hidden
  while it is on your own back), or planted where its carrier died on the first ground below (ray down). Unreachable = below the kill
  height (lowest spawn − `Match.killDepth`) or outside the play area; `CaptureTheFlag.Current.NavmeshCheck` is ticket 23's hook. The
  carrier's `speedMultiplier` is multiplied by 0.9 while carrying and divided back after (a spawn resets it), so ticket 22's slows
  combine. `FlagStand.State` (the `TeamFlag`) and `Carrier`. `FlagGlowColumn` + `Resources/FlagGlowColumn.shader`: a 45 m camera-facing
  column in the team colour on each flag (hard bands: core, colour, rim; fading up and near the camera; at least 0.8% of the distance
  wide so it reads across the field), additive, WebGL 2.
- **HUD** (`FlagHud`, built at runtime into the GUI's HUD screen from the HUD's own shape material and fonts, so the GUI prefabs and
  builders are untouched): RED ●●○ 2 – 1 ●○○ BLUE under the clock (the battle-royale edge rings and players-left panel hidden, the kills
  panel moved into the corner); a status panel per flag (At home / Carried by Blue 2 / On your back! / Dropped · home in 23 s); a marker
  with the distance over each flag away from home, pinned to the screen's edge with an arrow when off screen; banners (You have the
  flag!, Blue took your flag!, Red 2 has the blue flag, Red scored! / You scored!, Your flag was dropped / returned, The blue flag was
  dropped / returned); the result screen as "YOUR TEAM WINS!" / "BLUE WINS" with the score. Shared files touched: `Match.cs` (a `Hud`
  property and the `Attach` call), `FlagStand.cs` (the state).
- **HUD follow-up**: the score now sits on the clock's row, mirrored (RED ●●○ 2 [clock] 1 ●○○ BLUE, pips filling outward from the clock), the flag status panels moved up under the clock and the banner under them; the team names hide on narrow (4:3) screens to keep clear of the kill feed.

Left (in Unity): let it compile, run the EditMode tests (Window > General > Test Runner > EditMode), and on `Fungal.unity` in Play mode
take the blue flag (east castle), carry it home and score three times; check the carried flag's pose on a bot, the glow columns from
across the field, the markers and banners, dropping by F8 (the flag goes home at once) and the 30 s return.
