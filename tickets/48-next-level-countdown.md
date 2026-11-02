# 48 — The next-level countdown

Part of **Minigun Monsters** (41-50). After every match, won or lost, an end card counts down to the next level. During it you can
switch your head without going to the menu.

Depends on: 42 (the loop), 47 (the heads and unlocks).

## Decisions

- Countdown: 10 s, "Next level in 10…", then the next level loads by itself. There is no button to press, though Space or Enter may
  skip the rest of the countdown.
- The keys in the **A and D positions** switch to the previous or next unlocked head (by physical key position, so Q and D on
  AZERTY). The card shows the key letters for the user's layout, as the spore HUD does.
- The card shows:
  - WIN or LOSE, and the score
  - the next level's name and card
  - your head on your body, with ◀ ▶
  - "NEW" and a short reveal when this win unlocked a head (the new head is chosen for you, and you can switch back)
  - a small line when the difficulty changed ("Tougher opponents next" / "Easier opponents next")
- The CrazyGames midgame ad plays at the end of the countdown, under the existing `AdBreakPolicy`, before the next level loads.

## Work

- `GuiEndScreen` becomes the countdown card. Remove CONTINUE, RETRY and RETRY EASIER.
- The input goes through the Input System by physical key (as the spore keys in ticket 25). It is ignored while the pause menu is open,
  and the countdown pauses with the menu.
- The next level's bundle is already prefetched (42). If it is not ready at 0, the Loading screen shows with its retry.
- Bots on the winning side play a victory clip or dance (44's stand-in) behind the card, if the camera shows the field.

## Done when

- In Play mode: win and lose both count down and load the next level. A/D switch heads on QWERTY and AZERTY, and the head is worn in
  the next match. The unlock reveal shows on an unlocking win. The ad hook is called once per match end in the editor's simulate-ads
  mode.

## Status

Not started.
