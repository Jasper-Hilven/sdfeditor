# 47 — Head unlocks and selection

Part of **Minigun Monsters** (41-50). You start with the head of the month and every head you unlocked before. Each won match unlocks
the next head in the calendar. Your head can be changed at any time in Settings.

Depends on: 42 (the save and match results), 45 (the heads). Blocks: 48.

## Decisions

- **Head of the month**: from the device's local date. It is free while its month lasts, and stays unlocked for good once you have
  played a match during that month.
- **Unlocking**: each won match unlocks the next locked head in calendar order after the current month, wrapping round (in November:
  December, then January, …). When all 12 are unlocked, a win unlocks nothing.
- **Your head**: the last one you picked. On the first run it is the head of the month.
- **Other players**: a random head from all 12, each player different within a match where possible. Locked heads do appear on
  others, which shows you what is still to unlock.
- **Settings**: a simple cycle (◀ head ▶) over the unlocked heads only, with a 3D preview of the head on your team's body, turning
  slowly.

## Work

- `HeadUnlocks` (Core, plain C#, tests): `Available(now, save)` and `OnMatchWon(now, save)`, which returns the head unlocked or none.
  Test the month boundaries, the wrap, all-unlocked, and a clock moved backwards (nothing is ever taken away).
- `GameSave` (42) gets the unlocked heads, the months seen and the chosen head.
- The match gives you your head and gives the bots random heads (45's table).
- Settings screen: the head row (◀ ▶, the head's name, its month, the preview), using the existing settings GUI.
- `HappyTime` (`WebPortal`) on a win that unlocks a head.

## Done when

- The EditMode tests pass. In Play mode: a win unlocks the right next head, it shows in Settings, and the choice survives a reload and
  is worn in the next match.

## Status

Not started.
