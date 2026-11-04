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

Not started.
