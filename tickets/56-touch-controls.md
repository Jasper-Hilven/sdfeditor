# 56 — Touch controls (landscape)

Part of **Minigun Monsters** on mobile. After 55 (`Device`, `PlayerInput` with a slot for a touch source) and 54 (readable sizes).
Shown only when `Device.IsTouch`; desktop is unchanged.

## The layout (how mobile shooters do it: Krunker, Shell Shockers, Bullet Force, CoD Mobile)

- **Move**: a floating joystick on the left half: it appears where the left thumb lands and follows it past its rim. Pushed to the rim
  = full speed (there is no sprint).
- **Look**: drag on the right half (any touch that did not start on a button). A sensitivity of its own in Settings ("Touch look"),
  default tuned so a full screen-width swipe is about 180 degrees.
- **Fire**: a big button on the right, under the thumb. A drag that starts on it also looks, so one thumb aims and fires. A second,
  smaller fire button on the left above the joystick.
- **Auto-fire** (Settings, on by default on touch): fires while the crosshair is on an enemy's body or head within the weapon's range,
  and never at a teammate or through walls (use `Combat`'s own trace). Fire buttons still work.
- **Aim assist** on touch only: look slows down (to about 50%) while the crosshair is near an enemy. No snapping.
- **Buttons**: jump; the four weapon slots as the HUD's existing weapon icons, tappable; zoom for the sniper (shown only with it);
  the three power buttons on the rage meter (shown when it is full, like the key hints now); scoreboard (hold); pause (top corner).
- **Menus and the end card**: tap works already (uGUI buttons); the end card's "A and D change head" hint reads "Tap the arrows" on
  touch, and "Space or Enter" hints become "Tap" hints.
- Multi-touch: each touch belongs to the control it started on until it lifts. Use the Input System's touches (EnhancedTouch), not
  the mouse emulation; make sure a touch does not also count as a mouse click in the game view.

## Work

- A `TouchControls` HUD layer (built in `GuiScreens`/`GuiPrefabs` like the rest of the GUI, art in the same toon style), feeding
  `PlayerInput`'s touch source.
- The keyboard hints in the HUD (power keys, warm-up prompts in `WarmUp`: "WASD to walk", "SPACE to jump", "Hold the left button to
  fire") get touch wording on touch devices.
- Buttons at least 9 mm: about 64 px at 800x450 (CrazyGames' mobile size); joystick base about 160 px there.
- Checks: a play test (batch) that forces the touch device (55's editor toggle or a test hook) and drives the touch source: walk to a
  pod, look, fire, jump, switch slot, use a power; a snapshot of the HUD with the touch layer at 800x450 and 1080x607
  (`Media/touch-56/`).

## Done when

- On a touch device a full match can be played with touch only: move, look, fire, jump, switch weapon, zoom, powers, scoreboard,
  pause, the end card. The touch play test passes.
- Desktop unchanged: EditMode tests and the five-match play test pass.
- Snapshots in `Media/touch-56/`; left for the user: a try on a real Android phone (CrazyGames preview or localhost with `?device=mobile`).

## Status

Not started.
