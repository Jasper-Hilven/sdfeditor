# 51 — Gore: only the heads explode

Part of **Minigun Monsters** (41-52). Only the monster head bursts, in pieces and in its own blood colour. The body is a plain human
soldier: it never bursts or bleeds, it ragdolls to the ground. This keeps the game at **PEGI 12** (non-realistic violence against
human-like characters: no realistic blood, no dismembered human bodies).

Depends on: 44 (the body's death), 45 (the heads and the `MonsterHeads` table). Before 46: 46 builds its per-head debris and specials
on this ticket's head burst and colours.

## Decisions

- **Head**: on a kill, the head bursts into pieces cut from its own mesh (the SDF-intersection pieces the mushroom burst uses today,
  `content/models/characters/exploding/`), with its blood in its own colour. This happens at every gore level except Off.
- **Body**: never bursts, never bleeds, no blood decals on it at any level. On death it becomes a ragdoll, pushed by the hit (direction
  and the weapon's `GoreRules.Strength`), and lies for the existing corpse time (`GoreRules.MaxCorpses`). It replaces 44's directional
  death clips.
- **Neck**: the headless corpse shows the soldier's collar closed over the neck. There is no stump and no blood at the neck.
- **Blood colour per head.** None of them is realistic red. Each is distinct from the other 11 and from both team colours, so a burst
  never reads as the team's. These are proposals that the user may change:

| Head | Blood |
|---|---|
| Yeti | pale ice blue |
| Vampire Count | deep purple |
| Leprechaun | emerald green |
| Swamp Thing | dark olive brown |
| Cyclops | ochre |
| Cthulhu | ocean teal |
| Acid Alien | glowing acid yellow |
| Mummy | sand-grey dust (a cloud, not liquid) |
| Werewolf | charcoal black |
| Jack-o'-Lantern | pumpkin orange |
| Frankenstein | lime green |
| Evil Santa | candy pink |

- **Gore levels** (`GoreRules`, the settings' Off, Splatty, Extra Splatty, Ultra Gore) only change the head:
  - Off: the head pops in a cartoon puff in its blood colour, with no pieces and no gobs. The body ragdolls as at every level.
  - Splatty: the pieces and some gobs.
  - Extra Splatty: more gobs and splats.
  - Ultra Gore: the pieces fling far, trailing gobs (today's `Flings`).

  Splats land on the ground and the level, never on bodies.

## Work

- `GoreRules`:
  - The blood colour comes from the head id (a column in 45's `MonsterHeads` table), no longer from the team (`Recolour`).
  - `Bursts` stays per level for the head.
  - The body has no gib kinds. `GibKind` is for the head's pieces only.
  - EditMode tests: every head has a colour; the colours are pairwise distinct (a minimum RGB distance) and away from human red and the
    two team colours; no level makes the body burst.
- **Ragdoll** on the soldier (43's skeleton):
  - Look in `../BattleRoyale` for a ragdoll setup first, and port it if there is one. Otherwise build one in an editor builder: about
    11 bones, capsule and box colliders, character joints.
  - Turned on at death, off and pooled with the corpse.
  - Its layer collides with the level only, not with players, bullets or other corpses.
  - Web budget: at most `MaxCorpses` ragdolls simulate at once. Each one sleeps (kinematic) once it has settled or after 3 s.
- `Gore` (runtime): the head burst and the body's ragdoll start in the same frame. Remove every body gib, body blood and stump.
- Update 44 (death = ragdoll, not directional clips) and 46 (palette from this table; Off = head puff only) where they say otherwise.

## Done when

- The EditMode tests for the gore rules pass.
- In Play mode, a kill at each gore level shows the head bursting in its colour and the body ragdolling with no blood. 8 deaths at once
  on the Kitchen stay inside the web frame budget.
- A capture of the 12 head bursts and a ragdolled corpse has been looked at by the user.

## Status

Not started.
