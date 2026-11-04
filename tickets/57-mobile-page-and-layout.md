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

Not started.
