# 45 — Twelve monster heads

Part of **Minigun Monsters** (41-50). One head per month, modelled in SDF and sitting on the soldier's head socket (43). The head
replaces the mushroom as each character's identity.

Depends on: 43 (the socket and scale). Blocks: 46, 47.

## The heads

| Month | Head | Look |
|---|---|---|
| Jan | Abominable Snowman (Yeti) | jagged teeth, a frozen snarl, a snowy coat |
| Feb | Vampire Count | pale, slicked-back hair, giant fangs, high collar |
| Mar | Leprechaun Mutant | devious, sharp-toothed, a tattered green top hat |
| Apr | Swamp Thing | moss-covered and dripping, glowing yellow eyes in mud |
| May | Raging Cyclops | one eye, a protruding lower tusk |
| Jun | Deep-Sea Cthulhu | aquatic, writhing face tentacles |
| Jul | Acid Alien | sleek, insect-like, huge glossy black eyes |
| Aug | Ancient Mummy | round, wrapped in dusty bandages |
| Sep | Feral Werewolf | wide, furry, an oversized snout, snapping jaws |
| Oct | Glowing Jack-o'-Lantern | a glowing orange pumpkin with a jagged carved grin |
| Nov | Frankenstein's Monster | flat top, big metal bolts in the neck |
| Dec | Crazed Evil Santa | snarling, a glowing red nose, a giant festive hat |

## Decisions

- The heads are **big and cartoonish**, about 1.5 to 2 times a human head, so you can read them at range. The neck sits on the
  soldier's collar.
- Each head has its own eyes (the floating eyes are gone).
- The head keeps its own colours. The team shows on the body (43's skins), not the head.
- The heads are static (not rigged), on `Head`, so they nod and turn with the clips. A jaw is part of the mesh.

## Work

- `content/models/characters/monsters/<id>.sdf`, ids `yeti`, `vampire`, `leprechaun`, `swamp`, `cyclops`, `cthulhu`, `alien`,
  `mummy`, `werewolf`, `pumpkin`, `frankenstein` and `santa`, with `.sdfmat` in `materials/characters/monsters/`. Origin at the
  socket, faces +Z (the soldier's frame).
- Emission where the description says glowing (the Swamp Thing's eyes, the pumpkin's insides, Santa's nose), through the emission
  added in ticket 30.
- Meshed at 128 cells with LODs and `_fast`, through `MushroomBuilder`'s route, which becomes a `MonsterHeadBuilder` or a shared
  builder. About 3k triangles at LOD0.
- `MonsterHeads` (Core): the id, the month, the display name and the prefab path, one table read by 46, 47, 48 and 50.
- The hit capsule on `Head` is resized per head to the head's bounds. Headshots must stay fair: the same size across heads within
  ±15%, so no head is easier to hit.
- **Order**: Yeti and Jack-o'-Lantern first, to settle the scale, the neck join, the readability at 50 m and the budget. Then the
  other 10 in pairs.
- A capture sheet of all 12 on red and blue bodies, front and three-quarter, near and at 50 m.

## Done when

- The 12 heads are meshed, imported and on the socket, and a match has bots with random heads (the random pick is in 47).
- The capture sheet has been looked at by the user.

## Status

Not started.
