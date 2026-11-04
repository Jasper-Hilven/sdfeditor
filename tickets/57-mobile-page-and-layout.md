# 57 — The mobile page: safe areas, the web page's CSS, touch-sized menus

Part of **Minigun Monsters** on mobile. After 56. Requirements from docs.crazygames.com/requirements/technical:

- Mobile games run fullscreen in the CrazyGames app and must keep their GUI out of the device's safe areas (notches, rounded corners).
- The page's `body` must disable text selection (`user-select: none`, `-webkit-user-select`, `-webkit-touch-callout: none`) so a long
  press does not open the magnifier or a context menu.
- Orientation: set in the submission (landscape); the site asks players to rotate, the game needs no lock of its own.
- iOS (if CrazyGames ever turns it on): the AudioContext can be interrupted when the app goes to the background; resume it in a
  user gesture (`touchend`/`click`), not only on visibility change.
- DPR is CrazyGames' (1 on iOS and low-memory Android).

## Work

- **CSS**: the build uses the Default WebGL template and CrazyGames serves only `Build/` and `StreamingAssets/` (ticket 36), so the
  CSS must come from the game: add it to the page from a jslib at startup (next to `Assets/Plugins/crazySDK.jslib`'s pattern), on every
  device. Also `touch-action: none` on the canvas.
- **Safe area**: on touch devices, inset the HUD, the touch controls and the menus by `Screen.safeArea` (a `GuiSafeArea` on the
  canvas's content roots, re-applied on resolution or orientation change). Check it with a simulated notch in the snapshot tool.
- **Touch-sized menus**: on touch, every button in Pause, Settings, Confirm, the end card and Credits at least about 64 px tall at
  800x450; the Settings rows (sliders and selectors) usable with a thumb. Hide the keyboard-only rows and hints there.
- **Audio resume** on the first touch and after returning from the background (a jslib hook into Unity's AudioContext).
- `description.md` and `control.html`: a "Touch" controls section (left stick, right drag, fire, jump, weapon icons, powers, auto-fire).
  The submission's orientation (landscape) and "mobile: yes" are the user's to tick on the portal.

## Done when

- Built page: no text selection or long-press menu (checked in headless Chrome with mobile emulation: `user-select` computed `none`).
- Snapshots at 800x450 with a simulated safe area: nothing under the notch; menu buttons at the size above (`Media/mobile-57/`).
- EditMode tests pass; desktop unchanged.

## Status

Done in code, built and checked in batch and in headless Chrome; left for a human: a real phone with a notch.

- **The jslib** `Assets/Plugins/sdfPage.jslib` (C# side `PageBridge`, `Assets/GUI/Runtime/PageBridge.cs`, called before the first scene):
  adds a `<style>` to the page on every device (`html, body` and `canvas`: `user-select: none`, `-webkit-user-select`, `-webkit-touch-callout: none`,
  no tap highlight, `overscroll-behavior: none`; the canvas also `touch-action: none`), appends `viewport-fit=cover` to the viewport meta so the page
  reaches under a notch, and stops the browser's context menu on the canvas. Audio: a module-load hook (`$SdfPageBoot__postset`, so it runs before the
  engine's FMOD makes its AudioContext) wraps `AudioContext`/`webkitAudioContext` to remember every context and resumes them (state not
  `running`) on `touchstart`, `touchend`, `pointerup`, `click`, `keydown`, `visibilitychange` (back to visible), `pageshow` and `focus`, with
  Unity's own `WEBAudio.audioContext` as a fallback. Also `SdfPageSafeInset` (CSS `env(safe-area-inset-*)` through a hidden probe) and `SdfPageViewport`.
- **Safe area**: `SdfPlatform.SafeAreaRule` (Platform/Core, tested): insets as fractions from `Screen.safeArea` and from the page's CSS insets, the
  larger of the two, clamped (30% each, 50% per axis). `GuiSafeArea` on the GUI root (touch devices only; re-read when the screen size, `Screen.safeArea`,
  the canvas or the device changes, and twice a second for the page's insets) insets every `GuiScreenRoot` (the HUD, the touch layer, every
  menu), and pushes the full-screen backgrounds (dim, vignette, backdrop: direct children that stretch) back out so they still cover the display.
  `GuiSafeArea.Simulated` stands in for a notch; the snapshot argument `-snapshotSafeArea` sets 6% each side, 5% bottom, 2% top and tints the unsafe band red.
- **Touch-sized menus** (`GuiTouchLayout`, per screen: entries move, resize, scale or hide a rect on a touch device, the built layout put back
  otherwise, so the desktop is unchanged): every button 154 units (64 px at 800x450). Pause: RESUME across, the other four in two columns, panel 1100 wide.
  Confirm: YES/NO 380x154, panel 920x540. Settings: the rows are in a scroll view (`ScrollRect`; on a desktop everything fits), on touch the panel is
  1500 wide, rows 110 apart, selectors 1.5x, sliders 1.5x, toggles 1.6x, BACK 460x154, the mouse-sensitivity row and the head preview hidden (the
  touch look and auto-fire rows were already touch-only). Victory/Defeat: the head arrows scaled 3x (72 px) with the caption and hint moved under
  them. Credits has no buttons (a held touch skips; its hint is already "Hold to skip").
- **Power buttons** (from the lead's review of 56): moved from the lower left (the joystick's ground) to a row of three above the right thumb's
  buttons (`TouchPowerY` = 575 from the bottom, right-aligned, clear of the kill feed and of the joystick half).
- **Docs**: `description.md` and `control.html` have a Touch controls section.
- **Checks**: new `SafeAreaRuleTests` (Platform) and `TouchMenuTests` (GUI: every button of Pause, Confirm, Settings, Victory, Defeat at least 150 units on touch,
  Settings rows and controls grow, powers in the right half); EditMode 777 pass, 0 fail. Touch play test: 0 errors; desktop five-match play test: 0 errors.
  Snapshots `Media/mobile-57/` (800x450, touch, simulated notch, unsafe band red): Hud, Pause, Settings, Confirm, Victory, Defeat. Headless Chrome (iPhone
  emulation 844x390, touch, `?device=mobile`, the CrazyGames build served with Content-Encoding br): body and canvas `user-select: none`, canvas
  `touch-action: none`, `sdf-page-style` present, `viewport-fit=cover` set, one AudioContext captured and running; `Media/mobile-57/web-mobile-chrome.png`.
- **Uncertain**: Screen.safeArea and the CSS insets are zero in headless Chrome, so a real notch was only simulated; the audio resume on a real iOS
  background/foreground cycle is untested; the Settings list's last rows (touch look, auto-fire) are only seen by scrolling.
