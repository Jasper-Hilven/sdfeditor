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

## Status

**Done except the covers and the preview videos**, which wait for the user's review of the 12 heads (`CoverStage`, `CoverLogo`, the preview recorder and the `Media` re-renders are untouched; `CoverLogo` still looks for a `Title.prefab` that 41 removed, so point it at the Loading screen's `Logo` object when the covers are made).

- Logo: MINIGUN / MONSTERS (`GuiScreens.Logo`: two gradient words on a rage-red splat with a violet halo, glints, a tagline plate "A head for every month."); the "Second Place" ribbon and rosette are gone. It sits at the top of the Loading screen (the only screen that shows it now the title is gone), object named `Logo`.
- Loading screen reflowed around the logo; two new tips (heads unlock with wins, A and D on the end card). `GuiPalette`: Amanita/Chanterelle/Spore renamed Red/Amber/Cyan (`RedDark`, `CyanDeep`). Confirm card no longer says "campaign". Credits: MINIGUN MONSTERS, new cast, no Prime or referee. Test panel: AI instead of bots.
- Scoreboard (Tab): a head icon per player (12 icons rendered by `GuiIcons` from the `MonsterHeads` table into `Assets/GUI/Icons`, `ViewPlayer.head` filled by `MatchHud` from the worn head), names, kills, deaths, captures; WINNER marks the winner. The amanita and chanterelle scout icons are deleted. The death screen's killer avatar shows the killer's head.
- Player names (`Cast`): handles (Nightowl42, ToastyMage, Zed_13, Pixelpuss; Big Gerald, SuzyTwoShoes, Captain Nope, xX_Vlad_Xx, Grandma Gunner). `DeathLines`: cap, gills and funged wording became head, hair and monstered; headshot titles read HEAD POPPED, HEADSHOT, HEAD SPLIT.
- `description.md` and `control.html` rewritten (endless matches on 7 arenas, random team, a head unlocked with every win, the head of the month, adaptive difficulty, weapons, powers, machines, gore and quality, A/D on the end card, Q/F/C); no multiplayer wording, no single-player or bots.
- Player Settings product name is Minigun Monsters (the Default WebGL template and the CrazyGames settings take the title from it; the SDK settings asset holds no title).
- Checks: 721 EditMode pass; the done-when search finds no mushroom or multiplayer wording in the GUI prefabs' text, the player-visible strings, `description.md` or `control.html` (what is left is asset paths, the `mushroom` machine-set id and namespace names). GUI snapshots work in batch: `Media/gui-50/` (Loading, Scoreboard, Hud, Victory (the end card), Death, ...). The GUI showcase scene still shows its old mushroom characters behind the HUD (developer scene, not the game).
- Left: the covers and the preview videos, after the user has looked at the 12 heads.
