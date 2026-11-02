# 50 — Rebrand: GUI, description and media

Part of **Minigun Monsters** (41-50). The game's name, logo, GUI and store text change from Minigun Mushrooms to **Minigun Monsters**.
The levels, the weapons (mushroom-shaped, fitting the fungal levels) and the machines stay.

Depends on: 41, and 45 for the art. It comes last: its media need 43-46 done.

## Decisions

- Name: **Minigun Monsters**. The logo reads MINIGUN / MONSTERS. Drop the "Second Place" ribbon unless the user wants it kept.
- **Never call it multiplayer**. No "online", "players online", "lobby", "matchmaking", "ping" or "server" in the game, the GUI,
  `description.md` or `control.html`. Do not say it is single-player or bots-only either. Other players have names and monster heads.
- The CrazyGames listing does not use the multiplayer tag.

## Work

- GUI: the logo, the loading screen, `GuiPalette`, the HUD, `GuiLevelCard`, `GuiFlow`, `GuiStartQuality`, `MatchView` and `GuiViewModel`.
  Remove mushroom wording and art, and remove screens 41 removed.
- Scoreboard (Tab): names, the head icon per player, and kills, deaths and captures.
- Kill feed and bot names: check that the name list and `DeathLines` read as players' names. Remove any mushroom wording.
- `description.md`, rewritten:
  - endless matches on 7 arenas, a random team, unlock a monster head with every win, the head of the month, adaptive difficulty
  - the weapons, the monster powers, the machines, gore and quality levels
  - controls with A/D to switch heads on the end card, and Q/F/C for the powers
- `control.html`, the covers and the preview videos (`Media`, `CoverStage`, the preview recorder) re-rendered with monster heads on the
  soldier bodies.
- The game's title in Player Settings, the WebGL template and the CrazyGames SDK settings.

## Done when

- A search of the GUI, text and `description.md` finds no "mushroom" (except the levels' own props) and nothing that claims
  multiplayer.
- The new covers and videos have been looked at by the user.

## User review of the covers (2026-11-02)

The covers must be about **monsters, not mushrooms** (the lead decides the composition):
- Monster heads big and front and centre, facing the camera and readable at thumbnail size. Show at least three different heads.
- One head bursting in its own blood colour with its 46 profile debris (for example the Vampire's purple mist and bats). Not the big
  pale disc.
- No mushroom shapes as the subject. Pick a background level without mushroom props, for example the Meadow away from any fungus, or
  the Kitchen or Toybox. The weapons may be in shot (they are mushroom-shaped by design) but are not the focus.
- Nothing cropped at the edges in any format.
- The Fungal video clip is dropped or swapped for a level where the soldiers and heads are visible.

## Status

**Done; the user's look at the redone covers and the videos is pending** (`Media/review-50.png` is the contact sheet).

- Logo: MINIGUN / MONSTERS (`GuiScreens.Logo`: two gradient words on a rage-red splat with a violet halo, glints, a tagline plate "A head for every month."); the "Second Place" ribbon and rosette are gone. It sits at the top of the Loading screen (the only screen that shows it now the title is gone), object named `Logo`.
- Loading screen reflowed around the logo; two new tips (heads unlock with wins, A and D on the end card). `GuiPalette`: Amanita/Chanterelle/Spore renamed Red/Amber/Cyan (`RedDark`, `CyanDeep`). Confirm card no longer says "campaign". Credits: MINIGUN MONSTERS, new cast, no Prime or referee. Test panel: AI instead of bots.
- Scoreboard (Tab): a head icon per player (12 icons rendered by `GuiIcons` from the `MonsterHeads` table into `Assets/GUI/Icons`, `ViewPlayer.head` filled by `MatchHud` from the worn head), names, kills, deaths, captures; WINNER marks the winner. The amanita and chanterelle scout icons are deleted. The death screen's killer avatar shows the killer's head.
- Player names (`Cast`): handles (Nightowl42, ToastyMage, Zed_13, Pixelpuss; Big Gerald, SuzyTwoShoes, Captain Nope, xX_Vlad_Xx, Grandma Gunner). `DeathLines`: cap, gills and funged wording became head, hair and monstered; headshot titles read HEAD POPPED, HEADSHOT, HEAD SPLIT.
- `description.md` and `control.html` rewritten (endless matches on 7 arenas, random team, a head unlocked with every win, the head of the month, adaptive difficulty, weapons, powers, machines, gore and quality, A/D on the end card, Q/F/C); no multiplayer wording, no single-player or bots.
- Player Settings product name is Minigun Monsters (the Default WebGL template and the CrazyGames settings take the title from it; the SDK settings asset holds no title).
- Checks: 721 EditMode pass; the done-when search finds no mushroom or multiplayer wording in the GUI prefabs' text, the player-visible strings, `description.md` or `control.html` (what is left is asset paths, the `mushroom` machine-set id and namespace names). GUI snapshots work in batch: `Media/gui-50/` (Loading, Scoreboard, Hud, Victory (the end card), Death, ...). The GUI showcase scene still shows its old mushroom characters behind the HUD (developer scene, not the game).
- Covers and videos (after the user's review of the 12 heads): `CoverStage` now stages soldiers (the skin prefabs, a sampled aim or run pose, `HeadWear` heads): a red soldier with the Jack-o'-Lantern head leaping with the minigun in the grip socket, a werewolf and a Cthulhu soldier diving aside, a headless blue soldier with the Frankenstein head's own pieces and blood colour bursting behind; the old scout, eye and hand code is gone. `CoverLogo` lifts `Logo` from `Loading.prefab`. `CoverCapture` makes its stage scene correctly in a batch editor (the untitled start scene is replaced by the level). Covers re-rendered at 1920x1080, 800x1200, 800x800 and the two video-size ones (1080x1620, 1080x1080) into `Media/Covers`.
- Preview videos: the recorder is unchanged except `RecordInBatch` (batch run that exits 0 or 1; `SDF_NO_QUIT=1 tools/unity-batch.sh previews -executeMethod SdfMedia.Editor.PreviewVideoRecorder.RecordInBatch`). Same clips (Meadow minigun, Fungal flamethrower, Toybox blob gun, Kitchen dual pistols), 4 s each after a 0.5 s cover hold, 16.5 s, Epic, Ultra Gore, double damage; 1920x1080, 1080x1620, 1080x1080. The old locked mushroom clip was removed. Bots show random heads, heads pop and bodies ragdoll in the Meadow, Toybox and Kitchen clips; the Fungal clip is mostly the weapon's green cloud with soldiers small and far (re-recorded twice, same or worse).
- Left: the user's look at the covers and videos.

- Covers redone after the user's review (2026-11-02), monsters not mushrooms: Meadow backdrop (path and castle, no fungus; the Toybox as a backdrop did not work with the stage: the camera ends up inside the room's walls). Four heads: three red soldiers in a tight, low-angle group (Jack-o'-Lantern leaping in front with the minigun aimed up and away at the enemy, Werewolf on his left and Yeti on his right, both with a pistol), and behind them a blue soldier whose Vampire head bursts as 46's profile: deep purple mist puffs (smaller and scattered, no pale disc), tiny bats (the game's debris model) and the Vampire's own head pieces. The old Glow disc and rays of the explosion are gone; the muzzle flash glow is softer. `CoverStage` composition constants, `CoverCapture` framings (portrait and square wider, so all three soldiers fit), new batch arguments `-coverLevel`, `-coverYaw`, `-coverOnly` for trying a framing in a scratch folder. Re-rendered at 1920x1080, 800x1200, 800x800 and the two video-size ones.
- Video clip swap: the Fungal flamethrower clip is now the Chessboard flamethrower clip (`PreviewVideoRecorder.Arenas`); other clips, settings and the 16.5 s length unchanged; all three videos re-rendered (1920x1080, 1080x1620, 1080x1080), opening on the new covers. The flamethrower is held in view in the Chessboard clip but its flames do not show in the sampled frames, and the soldiers are small there.
- Portal sizes: neither 36 nor 38 (nor any ticket) states cover or video sizes; the covers and videos keep the sizes above (landscape 1920x1080, portrait 800x1200, square 800x800).
- 721 EditMode pass. Contact sheet `Media/review-50.png`.
