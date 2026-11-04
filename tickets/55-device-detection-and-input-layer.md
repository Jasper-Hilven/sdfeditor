# 55 — Device detection and one input layer

Part of **Minigun Monsters**, the first step to landscape mobile on CrazyGames (Android Chrome first: CrazyGames turns Unity games off
on iOS by default). This ticket adds no touch controls yet; 56 does. Afterwards the game behaves the same on desktop, with one change:
**P** also opens the pause menu.

## Device detection

- A plain rule in `SdfPlatform.Core` (tested): `DeviceKind { Desktop, Mobile, Tablet }` from, in order of trust:
  1. a URL override for testing: `?device=mobile`, `?device=tablet`, `?device=desktop` (read from `Application.absoluteURL`);
  2. the CrazyGames SDK's `CrazySDK.User.SystemInfo.device.type` (`"desktop"`, `"mobile"`, `"tablet"`) once the SDK is up
     (`WebPortal`, only when `CrazySDK.IsAvailable`);
  3. `Application.isMobilePlatform` (true in mobile browsers in a WebGL build);
  4. in the editor: a Tools > Portal > Simulate mobile menu toggle (EditorPrefs), next to the existing Simulate ads.
- A runtime `Device` (Assembly-CSharp, next to `WebPortal`, or a static in Platform/Core that `WebPortal` feeds) with `Kind`, `IsTouch`
  (Mobile or Tablet) and a `Changed` event: the kind is known at Boot from 1, 3 and 4, and may change once when the SDK comes up (2).
  Log the kind and its source once.
- Nothing else reads `Application.isMobilePlatform` or the SDK's device directly.

## The input layer

Input is read straight from `UnityEngine.Input` (`GetKey`, `GetKeyDown`, `GetMouseButton`, `GetAxis`, `mouseScrollDelta`) in about a
dozen runtime files: `FirstPersonController`, `Combat`, `Arsenal`, `Match`, `BotSpectator`, `GuiHud`, `GuiFlow`, `WarmUpPlayer`,
`CreditsPlayer`, `SceneLoader`, `GameKeys` (keys by place, ticket 25; the Input System is on, Active Input Handling: Both) and the
effects bench. Make one layer that every gameplay reader uses:

- `PlayerInput` (Level/Runtime, or wherever `GameKeys` lives) with what the game asks for, not keys: `Move` (Vector2), `Look` (a
  per-frame delta in degrees, so mouse and touch feed it alike), `FireHeld`, `ZoomHeld`, `JumpPressed`, `SlotPressed(1-4)`,
  `SlotScroll`, `PowerPressed(Frenzy, Regenerate, ThickHide)`, `ScoreboardHeld`, `PausePressed`, `SkipPressed`, `HeadPrevious/Next`.
- Sources: the keyboard-and-mouse one (today's behaviour, keys by place, `GameKeys.Blocked` respected) and a slot for the touch one
  (56: it adds to the same values). Both may be active at once (a laptop with a touchscreen).
- Move every reader above onto it. Keep mouse sensitivity and the sniper zoom behaving exactly as now.
- **Pause**: P (by place) as well as Escape; CrazyGames advises against relying on Escape (it leaves fullscreen). Update the controls
  in `description.md` and `control.html` ("Escape or P").
- **Pointer lock** (`FirstPersonController`: a click takes the pointer, losing it opens the pause menu via `GuiFlow`): only on
  non-touch devices. On a touch device, no pointer lock and losing it does not pause.
- The play tests that feed a keyboard (`GameKeys.Source`, `GameLoopPlayTest`) keep working.

## Done when

- `DeviceKind` rule tested (URL override, SDK type, mobile platform, fallbacks); the editor toggle and the URL override switch it.
- No gameplay code reads `UnityEngine.Input` directly except the keyboard-and-mouse source (a search shows it).
- Desktop plays as before: EditMode tests pass, and the five-match play test passes
  (`SDF_NO_QUIT=1 tools/unity-batch.sh playtest -executeMethod SdfCampaign.Editor.GameLoopPlayTest.Run`).
- P pauses; description and controls updated.

## Status

Done (code); a real phone or tablet check is left for a human.

**Device detection.** `SdfPlatform.DeviceRule` (Platform/Core, tested in `DeviceRuleTests`) decides `DeviceKind` and its `DeviceSource`.
Order: 1. the URL (`?device=mobile|tablet|desktop`, from `Application.absoluteURL`); 2. the editor's Tools > Portal > Simulate mobile
(EditorPrefs `sdf.portal.editorMobile`; it sits above the SDK because the editor's simulated SDK always answers "desktop" and would
otherwise cancel the toggle); 3. `CrazySDK.User.SystemInfo.device.type` once the SDK is up; 4. `Application.isMobilePlatform`;
else Desktop. `SdfPlatform.Device` (Platform/Runtime) holds `Kind`, `Source`, `IsTouch` and the `Changed` event; it is decided on first
read (URL, editor, platform) and again once when `WebPortal` calls `Device.SdkReady()`. The kind and source are logged
("Device: Desktop (from Default)"). Nothing else reads `isMobilePlatform` or the SDK's device. Force it: add `?device=mobile` to the page
address, or tick Tools > Portal > Simulate mobile in the editor.

**Input layer.** `SdfLevel.PlayerInput` (Level/Runtime) asks what the game needs: `Move`, `Look` (per-frame, at sensitivity 1; the
controller multiplies by `lookSensitivity`, so mouse feel and the sniper zoom are as before), `FireHeld`, `ZoomHeld`, `JumpPressed`,
`SlotPressed(n)`, `SlotScroll`, `PowerPressed(SporePower)`, `ReloadPressed`, `UsePressed`, `ScoreboardHeld`, `PausePressed` / `PauseHeld`
(Escape or P), `SkipHeld`, `HeadPreviousHeld` / `HeadNextHeld`, `PointerHeld` / `PointerPressed`, `Engaged` (pointer locked, or a
touch device), the spectator camera and `DebugPressed(KeyCode)` for the test keys. Sources derive from `PlayerInputSource` (every
member optional); `PlayerInput.Add/Remove` registers one (56's touch source) and the values add up. `KeyboardMouseInput` is the
keyboard-and-mouse source, the only gameplay code besides `GameKeys` that reads `UnityEngine.Input`. `GameKeys.Blocked` zeroes
Move, Jump, slot and power presses. `KeyPlace.P` added. Readers moved: FirstPersonController, Arsenal, Spores, Combat, Match,
BotSpectator, GuiHud, GuiFlow, GuiEndScreen, WarmUpPlayer, CreditsPlayer, SceneLoader, SpecialEffectsBench (Cheats' legacy
`inputString` branch removed; it reads the Input System keyboard).

**Touch device:** no pointer lock is taken (FirstPersonController) and a lost lock does not pause (`GuiFlow`); Arsenal and Spores
treat a touch device as engaged. **Small desktop differences:** Enter and the pad's start now also skip the warm-up (one skip
definition), P also lets the pointer go and closes the confirm box like Escape, the credits skip with Space/Enter/pad start or
Escape/P. description.md and control.html say "Escape or P".

**Checks:** EditMode tests 751/751 passed (18 new device cases); five-match play test: 0 errors. Search for `Input.` in runtime
code outside CrazySDK: only `KeyboardMouseInput.cs` and `GameKeys.cs` (legacy fallback branch).

**Left for a human:** try `?device=mobile` / `Simulate mobile` in a build and on a real Android phone (no touch controls yet, so the
view cannot be turned there until 56); confirm P pauses in the web build.
