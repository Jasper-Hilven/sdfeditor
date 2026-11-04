# 64 - Menu wording and fit at every screen size

## What

1. The pause menu said "PAUSED" but the match runs on under it (ticket 55, `GuiFlow`). It is now "MENU", and the touch button is a menu icon.
2. Every screen's text and controls fit on every standard landscape screen size, on the desktop layout and on the touch layout.

## Decisions

- **Wording** (player-visible only, no code identifiers renamed): the Pause screen's banner PAUSED to MENU; the touch top-left button's two
  bars (a pause icon) to three bars (a menu icon); `description.md` and `control.html` ("Menu / back", "Menu: the menu button at the top
  left"); the developer test panel's PAUSE button to MENU and its status line "Esc menu". Loading tips, warm-up lines and the settings
  rows never said "pause".
- **`GuiScaleFloor`** (on the canvas): the canvas scale at match 0.5 would fall under 12 px for the 29 pt minimum (`Floor` = 12/29 = 0.4138
  px per unit) on 844x390 (0.398) and 780x360 (0.368). There the scaler's reference resolution shrinks with its aspect until the scale
  is the floor (780x360 gets a 1885x870 canvas); every larger screen keeps 1920x1080.
- **`GuiFitInside`** now reads the card's own size each frame too (a touch layout resizes it), and `GuiScreens.FitCard` wraps the content of
  Loading, Scoreboard, Pause, Settings (with the head preview), Confirm and Death in a card sized to what the content takes (banners above
  a panel included), scaled down to the screen, never up. The dim, vignette, backdrop and glow stay full-screen. Victory and Defeat had it
  already (02896ba0). Touch layouts resize the card with its panel (`ForTouch(card, size)`).
- **Scoreboard** compacted (panel 840 to 760 high, banner 14 units lower): 932 units in all, so it shows whole at full size on 844x390.
- **Settings** touch list 590 high (was 620) so the cut row stays clear of BACK; the desktop card is 1960x1100 so the banner's top shadow and
  the head preview stay inside at 4:3.
- **End card**: the head caption, key hint and NEW HEAD reveal boxes 400 wide at x -745 (inside the card at 4:3), the touch key hint wraps
  to two lines, the touch arrows moved in (-860, -660) so nothing leaves the card at 4:3.
- **Caption** (the warm-up's prompts) on touch: 720 wide, 186 up, clear of the joystick, JUMP, ZOOM and the weapon slots; it never takes a
  touch (no raycast targets).
- **JUMP** moved from x -500 to -360 (from the right): on screens narrower than 16:9.5 (4:3, 16:10) the bottom-centre weapon slots lay
  under it, and the touch play test could not tap it at 640x480 once the floor was in.
- **Snapshot tool**: `-snapshotSize` now always hides the test panel; Settings also saves `SettingsBottom.png` (scrolled to its last row);
  `audit.txt` per run lists texts under 12 px, texts bigger than their box and anything past the screen (or the safe area) per screen.

## Done when

- The Pause banner says MENU, the touch button is a menu icon, no player-visible text says the game pauses.
- On all 19 sizes (desktop and touch) and the two safe-area runs, no screen has text running out of its box, overlapping controls or content off screen
  (the audit lists none), and the sheets in `Media/fit-64/` look right.
- EditMode, the three play tests and the CrazyGames build pass.

## Status

Done and checked: EditMode 782 pass, 0 fail; desktop play test 0 errors; touch play test 0 errors (it failed twice at "waiting for the
jump" at 640x480 until the JUMP move above: a real overlap, not the flake); reset play test 0 errors; CrazyGames build made.
Sheets: `Media/fit-64/<desk|touch>-<W>x<H>.jpg` (one per size, 13 screens each: Loading, Hud, Scoreboard, Death, Pause, Settings,
SettingsBottom, Confirm, Victory, Defeat, Credits, Caption, MatchEnd) and `safe-844x390.jpg`, `safe-932x430.jpg` (touch, simulated notch).

Problems found (before) and fixes:

| Size | Screen | Problem | Fix |
|---|---|---|---|
| <= 1024x768 and 4:3, short (780x360, 844x390, 915x412) | Settings, Scoreboard, Loading, Pause, Death, Confirm | panel taller than the canvas (about 970 units at 844x390): header and BACK clipped, Loading's banner over its goal line and the rosters | `FitCard` on each |
| 1024x768, 1180x820 (4:3) | Settings | head preview left of the panel cut by the screen edge | card includes the preview |
| 4:3, 16:10 | Touch HUD | JUMP over weapon slot 4 | JUMP moved in |
| 4:3 and short | Touch caption | box over the weapon slots and JUMP | narrower, raised, no raycast |
| 4:3 | End card (touch) | head arrows and "Tap the arrows to change head" cut at the left | moved in, wraps |
| 780x360, 844x390 | Hud, Caption, Credits, Loading | 29 pt text at 10.7 to 11.5 px | `GuiScaleFloor` (HUD and touch controls now at 12 px) |
| all short | Settings (touch) | cut row under BACK | viewport 590 |

Result per size (the audit: overflow or off-screen = none for every cell; "12 px" = lowest text px on any screen):

| Size | Desktop layout | Touch layout | Touch + safe area |
|---|---|---|---|
| 907x510, 1216x684, 1077x606, 821x462, 800x450, 1080x607 | fits, 12 px | fits, 12 px | |
| 1366x768, 1920x1080, 1536x864, 1280x720 | fits (1920x1080 as built), 12 px | fits, 12 px | |
| 1180x820, 1024x768, 1280x800, 1440x900, 2560x1080 | fits, 12 px | fits, 12 px | |
| 932x430 | fits; end card 11.5 px, Loading % 11.9 | same | fits; 5-13 texts at 11 to 12 px |
| 915x412 | fits; end card 11.1, Loading % 11.4 | same | |
| 844x390 | fits; end card 10.5, Loading % 10.8 | same | fits; Scoreboard and end card 10 to 11 px (the notch leaves less room) |
| 780x360 | fits; Scoreboard 10.6 to 11.2, end card 9.7, Loading 10.0, Settings 11.7 | same | |

## Still not 12 px (and why)

The end card (Victory and Defeat, 29 pt captions in a 1080-unit layout), the Loading percentage and, at 780x360, the Scoreboard: each
is a whole screen laid out for 1080 units and fitted to a canvas 870 to 970 units tall, so the text scales down with it (9.7 to 11.9 px
at 780x360 to 932x430). Fixing them means a separate short layout for those screens.
Texts under the readability floor in the Gallery and Test Panel are developer-only. The scoreboard, the end card and the Settings list are not
checked with real names or tips longer than the samples.
