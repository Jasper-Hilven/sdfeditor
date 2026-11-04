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

Done in code, built and checked in batch; left for a human: a try on a real Android phone (CrazyGames preview, or localhost with `?device=mobile`).

- **Layout** (canvas 1920x1080; at 800x450 multiply by 0.4167; buttons are 154 = 64 px): left thumb, a floating joystick on the left half
  (base 384 = 160 px, rim 180 units; appears where the thumb lands, the base follows the thumb past the rim) with a small fire button
  (170, 71 px) over it at the left edge and, over the rage meter, the three power buttons (200x154, in the power's colour; shown only
  when the meter is full, the HUD's key cards are hidden); right thumb, the big FIRE (250 = 104 px) at the lower right, JUMP (170)
  and ZOOM (170, only with the sniper in hand; a tap zooms in, the next out) to its left; along the bottom centre the four weapon
  slots (154 each, mirroring the HUD's slot colours and icons, the weapon's name over them); top left PAUSE and SCORE (hold, 154 each),
  the FPS hint moved under them. Right half, off the buttons: the look. A drag that starts on a fire button looks as well.
- **Built as** `TouchLayer` (a `GuiView`, `GuiScreenRoot.touchLayer`, shown by `GuiFlow.TouchShown` over the HUD and the scoreboard on a
  touch device, built by `GuiScreens.Touch()` into `Touch Controls.prefab`), `TouchButton`, and `TouchControls` on the GUI's root: a
  `PlayerInputSource` fed by Enhanced Touch (a mouse button is one finger when a touch device has no touchscreen, for `?device=mobile`
  on a desktop and the editor). `TouchControls.Feed` replaces the touchscreen for checks. Each finger keeps the control it started on.
- **Look**: a full screen-width swipe is 180 degrees at the default "Touch look" (`TouchRules.SwipeDegrees`, slider 0.4x to 2.5x); the
  controller's own sensitivity (and the sniper zoom's slowing) still multiplies it.
- **Auto-fire** ("Auto-fire", on by default): `TouchAssist.OnEnemy` is `Combat.Trace` along the aim for the weapon's range; the trigger is
  held while the first thing hit is an enemy player (body or head) or enemy machine, so a teammate or a wall in front stops it; it
  holds 0.12 s after the crosshair leaves. Not for the lobbed poison- and blob-guns (their shot is an arc). Fire buttons still work.
- **Aim assist** (touch only, always on): `TouchAssist.LookScale` slows the look to 50% while an enemy in sight (a trace from the eye to
  its middle ends on it) is within a circle round the crosshair (0.9 m seen from the eye, clamped to 1.5-10 degrees), back to full at twice
  that. It never moves the view.
- **Wording**: warm-up prompts ("Left thumb to walk, right thumb to look", "Tap JUMP", "Hold FIRE, or aim at an enemy"; no skip hint),
  scoreboard hint, loading tips, "TAP TO PLAY", the end card's "Tap the arrows to change head" (a tap off the buttons skips its
  countdown; a held touch skips the credits), no "Press H" hint, no E key box on the pick-up prompt (`TouchWording`).
- **Settings**: "Touch look" and "Auto-fire" rows (shown only on a touch device); the panel's rows are 56 apart (14 rows).
- **Checks**: new `TouchRulesTests` (EditMode 761 pass, 0 fail); `TouchPlayTest` (`SDF_NO_QUIT=1 tools/unity-batch.sh touchtest
  -executeMethod SdfCampaign.Editor.TouchPlayTest.Run`): forced touch device and a fake finger feed, a first match with its warm-up: both
  thumbs at once, jump, the walk onto the pod, fire with a drag, slots, sniper zoom, a power, scoreboard, pause, settings rows,
  auto-fire (none at the sky or on a teammate or with the setting off; fires on an enemy), the assist's 0.5 scale, the end card's tap
  skip: 0 errors. Desktop five-match play test: 0 errors. Snapshots (`-snapshotTouch` forces the touch device): `Media/touch-56/`
  (`Hud-800x450.png`, `Hud-1080x607.png`, `Settings-1080x607.png`).
- **Uncertain**: Enhanced Touch itself (the real touchscreen) is not exercised in batch, only the fake feed; the layout's thumb reach on a
  real phone; the touch look's default feel; the touch layer in the snapshot shows the joystick and all powers as a preview.
- **Auto-fire dwell**: the auto-fire no longer fires the moment the crosshair crosses an enemy. `TouchRules.AutoFire` starts the trigger
  only after the crosshair has stayed on the same enemy for `AutoFireDwell` (0.3 s); a different enemy restarts the dwell; the 0.12 s hold
  after leaving stays (the dwell also survives a gap that short). While the finger turns the view faster than `SwipeRate` (90 degrees per
  second, measured from the finger's own input before the assist scales it) the dwell resets, nothing starts and a shot going stops, and
  the aim assist's slowdown is not applied either (so a swipe is never turned into a lock-on). The trace is the bullet's own along the
  aim (`TouchAssist.EnemyUnder`); there was no head bias (the assist aims at the chest and the auto-fire only asks whether the crosshair
  is on an enemy), so nothing was removed. The fire buttons and the desktop are unchanged. Tests: `TouchRulesTests` (dwell, swipe,
  target change, hold, wobble) and `TouchPlayTest` (a 400 degrees per second swipe through an enemy fires nothing and is not slowed;
  holding on it fires after ~0.3 s).
- **Poison-gun and blob-gun on touch** (checked by code and by `TouchPlayTest`): both already worked with the fire buttons. Neither has a
  charge or release-fire (`WeaponTrigger` fires while the trigger is held, at the weapon's rate after its 0.3 s draw), pods are walk-on, the
  slots are tappable; shots fly and land (the test: walk onto the pod, tap slot, tap and hold FIRE, the shots land as clouds/splats). Levels
  with the poison-gun: chessboard, computer, fungal; with the blob-gun: bathroom, chessboard (neutral centre pod), kitchen, toybox. Only the
  auto-fire skipped them, and now supports them: `Combat.PredictLob` steps the projectile's own path (same lift, muzzle start, gravity and
  0.12 m sweep as `StepProjectiles`, no spread) and `TouchAssist.LobbedEnemy` starts the trigger when that path ends on an enemy player or
  machine (a teammate or a wall first: no); the poison cloud also counts when it lands within 0.8 of its radius of an enemy and no teammate
  is inside its radius. Same dwell and swipe rules. The lob needs the crosshair above the target (the shot leaves 5-8 degrees above the
  aim and drops); desktop has no arc preview or aim hint, so there is none on touch either (nothing was added).
- **Minigun fires early on touch**: its spin-up made it hard to start on an exact aim. `TouchAssist.NearEnemy`: an enemy player in sight
  within `TouchRules.MinigunNearAngle` (the aim assist's circle times `MinigunNearScale` 1.5) of the crosshair and in range starts the
  auto-fire at once (`MinigunDwell` 0); the line the bullets take must not meet a teammate or a wall in front of the enemy, a fast swipe
  (`SwipeRate`) still stops it, the hold is the usual 0.12 s. The shots go where the crosshair is (the minigun's spread is the miss
  factor). Other weapons keep their rules. Tests: `TouchRulesTests` (no dwell, swipe, angle) and `TouchPlayTest` (an enemy 3.5 degrees off
  the crosshair starts the minigun; a teammate in the line of fire does not; the lobbed guns by touch and their auto-fire).
