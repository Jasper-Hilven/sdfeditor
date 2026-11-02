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

Not started.
