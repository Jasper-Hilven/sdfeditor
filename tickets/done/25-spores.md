# 25 — Spores: the fungal adrenaline

Unreal Tournament's adrenaline, fungal: every player fills a **spore meter** by fighting and playing the flag. When it is full, they
**sporulate**: one key press spends the whole meter on one of three temporary powers. First on the fungal level; the rules hold for
every level.

Depends on: 20 (match, respawn), 21 (flag events), 22 (health, damage, kill feed, the HUD), 23 (bots), 24 (machines' deaths).

## Decisions (from the user)

- **Three powers**, no more for now:
  - **Mycelium Rush**: super speed.
  - **Regrowth**: faster healing.
  - **Puffball Armour**: takes less damage.
  - (Bioluminescence, seeing enemies through walls, was dropped: too confusing.)
- **Keys: Q, F, C**, one key per power, no menu (the mouse is locked in play, and a hold-and-pick wheel is slower):
  - **Q** Mycelium Rush (the easiest key, for the panic escape with the flag),
  - **F** Regrowth,
  - **C** Puffball Armour (like a crouch: the defensive one).
  - All three are one step from the fingers on WASD, so moving never stops. None is bound in play (E is pickup, R in use; Q and F
    only in the bot spectator's fly camera, which is not played).
- **Other keyboard layouts work the same** (AZERTY, QWERTZ, Dvorak, ...): the keys are the **physical positions** of US Q, F and C,
  not the letters printed on them. On AZERTY that is A, F and C; on QWERTZ Q, F and C. The HUD shows **the letter of the player's own
  layout** on each option. See "Keyboard layouts" below; the same holds for movement (WASD is ZQSD on AZERTY).
- **HUD**, one of three states, never two at once:

  | State | Shows |
  |---|---|
  | Filling | The spore meter only |
  | Full | The three options (icon, name, key) only; no meter |
  | Power on | The power's name and the seconds it has left (e.g. `MYCELIUM RUSH 8`), nothing else |

## Defaults

The brainstorm's proposals, accepted by the user ("start building"), kept in the `SporeRules` table on the scene's `Spores`
component so they are one place to tune.

- **Gaining spores** (meter 0-100):
  - a kill: 25;
  - destroying an enemy machine (24): 15;
  - returning your flag by touch: 15; taking the enemy flag (from its stand or where it was dropped): 20; capturing: 50;
  - nothing while a power is on; the meter starts again from 0 when it ends.
- **The powers**:

  | Power | Effect | Lasts |
  |---|---|---|
  | Mycelium Rush | +60% move speed (on top of the flag carrier's 10% slower: 1.6 × 0.9) | 24 s |
  | Regrowth | +8 health a second, up to the health pods' overheal limit (200) | 20 s |
  | Puffball Armour | half the damage taken (after spawn protection; burn and poison too) | 24 s |

- **Death**: the meter is kept (as in UT); a power on ends at death. A new round empties every meter.
- **The flag**: a carrier may sporulate (Rush with the flag is the point of putting it on Q).
- One power at a time; a key with the meter not full does nothing (no sound, no message), so a stray press costs nothing.

## Keyboard layouts

The project reads input with the legacy Input Manager (`activeInputHandler: 0`; `Input.GetKey(KeyCode.Q)`, the `Horizontal` and
`Vertical` axes). `KeyCode` letters are not the same physical key on every platform and layout, so on AZERTY Q is not where the
ticket wants it, and WASD is not ZQSD.

- Turn on the **Input System package** with **Both** handlers (so everything that reads `Input` keeps working), and read these keys
  by **physical position**: `Keyboard.current[Key.Q]`, `[Key.F]`, `[Key.C]`. The Input System's `Key` names the US-layout
  position, the same on Mac, Windows and WebGL (which takes the browser's `KeyboardEvent.code`).
- The label on each option is the key's own name in the player's layout: `Keyboard.current[Key.Q].displayName` ("A" on AZERTY).
  The labels are refreshed when the layout changes (`InputSystem.onDeviceChange`, cheap) or simply each time the options show.
- **Movement too**: `FirstPersonController` moves with W, A, S, D by position (`Key.W`, `Key.A`, `Key.S`, `Key.D`), plus the arrow
  keys, so AZERTY players get ZQSD without a setting. Space, Shift, Tab, Escape and the number keys are the same everywhere already,
  but read them through the same path for consistency where it is no extra work.
- Tested by hand on an AZERTY layout (macOS: add the French keyboard, switch with the input menu) in the editor and in a web build.
- Not in this ticket: rebinding keys in the settings.

## 25.1 — Rules (`SdfBattle.Core`)

- `SporeRules` (the numbers above, serialized on the scene's component like `MachineRules`), `SporePower` (Rush, Regrowth, Armour).
- `SporeMeter` per player: `Amount`, `Full`, `Active`, `TimeLeft`, `Gain(reason)`, `Sporulate(power)` (false unless full and none
  on), `Tick(dt)`, `Reset()` (new round), `End()` (death).
- Hooks: kills and machine deaths from `CombatState` (24's machine deaths credit the killer here even though they give no kill
  credit), flag take / return / capture from ticket 21's events.
- The effects through the existing rules: Rush a speed factor the controller and the bots' agents read; Regrowth a heal each tick
  through `CombatState` (the same `HealthAfter` as the pods, at the overheal limit); Armour a damage factor in `CombatState.Hurt`.
- EditMode tests: gains per event, no gain while on, keys do nothing below full, the timers, the speed and damage factors, healing
  stopping at 200, death ending a power and keeping the meter, a new round emptying it.

## 25.2 — Input and HUD (Unity)

- Input as in "Keyboard layouts": Q / F / C by physical position, only in play (not while a menu, the scoreboard or death screen is
  up, nor while dead).
- HUD in `GuiScreens` (built into `HUD.prefab`), near the health bar, about 5 px from the screen edge like the rest of the HUD:
  - the **meter**: a bar filling with a spore glow;
  - the **options**: three cards in a row in key order (Q, F, C), each with its icon, name and the layout's key letter;
  - the **active line**: name and whole seconds left.
  - Exactly one of the three is visible (table above).
- The player's own feedback in the first-person view: a short spore puff when sporulating; a faint tint at the screen edge while a
  power is on (green Regrowth, gold Rush, pale Armour), kept subtle.
- Icons rendered like the weapon icons (`Tools > SDF GUI > Render Icons`), fungal: a mycelium thread, a sprouting cap, a puffball.

## 25.3 — What others see, and the bots

- Others see who has a power on: Rush leaves a short glowing mycelium trail on the ground behind the scout; Regrowth a soft green
  pulse in the gills; Armour a swollen, paler cap (a scale on the cap part, about 1.15). Cheap: no new meshes beyond the trail.
- **Bots** use spores too, through `BotBrain`: when full, Rush when carrying a flag or running to one, Regrowth when hurt below
  half, Armour when fighting at close range; else they keep the meter for one of those.

## Done when

In Play mode on the fungal level, with the bots: kills and flag play fill the meter; when full, the options show (and the meter does
not); Q, F and C start Rush, Regrowth and Armour, which show only their name and seconds left and work as the table says; the same
keys work on an AZERTY layout (A, F, C, shown as such on the cards), as does moving with ZQSD; bots sporulate. EditMode tests for the
rules pass. Works in a web build.

## Status

2026-10-23: durations doubled at the user's request (Rush 12 → 24 s, Regrowth 10 → 20 s, Armour 12 → 24 s). Regrowth keeps its 8 health a second,
so it heals twice the total; meter fill, costs and strengths are unchanged. `SporeStateTests` follow.

**Done in code**; left: the Unity side checked in the open editor (below). The new EditMode tests and the rest of the battle's (183)
pass when compiled and run outside Unity against its assemblies; not yet in Unity's Test Runner.

- **25.1**: `Assets/Battle/Core/SporeRules.cs` (`SporePower`, `SporeRules` with the numbers above and the bots' choice,
  `SporeMeter`), `SporeState.cs` (gains from `MatchState.Died`, `CaptureTheFlagState`'s events and `ReportMachineKilled`; `Sporulate`,
  `Tick`, death and new round). `CombatState` gained `SpeedBoostOf` (in `SpeedFactor`) and `DamageTakenOf` (in `Hurt`), which the
  spores set. Tests: `Assets/Battle/Tests/SporeStateTests.cs` (12).
- **25.2**: `Assets/Level/Runtime/GameKeys.cs`: keys by place (`KeyPlace`), through the Input System's `Keyboard` when it is on, the
  classic letters otherwise; `Label` for the HUD. `FirstPersonController` walks with `GameKeys.Walk()` (ZQSD on AZERTY). The Input
  System package (1.16.0, bundled with the editor) is in `Packages/manifest.json` and Active Input Handling is **Both**
  (`ProjectSettings.asset`, `activeInputHandler: 2`), so every `Input.*` call keeps working. `Assets/Battle/Runtime/Spores.cs` (made by
  `Match` after the machines; your Q/F/C with the pointer locked; `Machine` reports its destroyer) and `SporeHud.cs` (built at runtime
  from the HUD's health panel, pick-up prompt and vignette, as `CombatHud` builds the slots: meter, three cards, or the power's name and
  seconds; a tint of the power's colour at the screen's edge).
- **25.3**: through the combat's effect particles: a spore puff from the cap on sporulating, Rush's gold motes on the ground behind,
  Regrowth's green spores rising, Armour's swollen scout (1.18 across, 1.08 up). Bots look every 0.5 s (`SporeRules.ChooseForBot`).
- Not done: the power icons (the cards show the key and the name only); rebinding keys.

Left in Unity:
1. **Restart the editor** (the input handling changed; Unity imports the Input System package on the way). If it asks to switch to
   the new input system only, say **No**: the project needs Both.
2. The EditMode tests (Window > General > Test Runner > EditMode > Run All).
3. Play mode on `Fungal.unity`: the "Done when" above, with a second keyboard layout (System Settings > Keyboard > Input Sources:
   add French, switch in the menu bar).
4. A web build: the keys by place in the browser.
