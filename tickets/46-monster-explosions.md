# 46 — Monster explosions

Part of **Minigun Monsters** (41-50). When a monster dies, its head bursts in its own way. The mushroom explosion becomes a
data-driven monster explosion with one profile per head.

Depends on: 45. Works best after 44 (the body's death plays at the same moment).

## The explosions

| Head | Explosion |
|---|---|
| Yeti | shattering ice shards, snowballs, a burst of freezing white frost |
| Vampire Count | deep purple mist, tiny cartoon bats flying outward |
| Leprechaun | bright green shamrock confetti, gold spark dust |
| Swamp Thing | dark brown mud, tangled weeds, dirty pond water |
| Cyclops | muddy brown slime; one giant dazed eyeball bounces away |
| Cthulhu | ocean-blue slime, writhing tentacles that deflate |
| Acid Alien | glowing sizzling yellow acid splatters |
| Mummy | unravelling bandages, a sand cloud |
| Werewolf | brown fur tufts, snapping fangs, bright orange sparks |
| Jack-o'-Lantern | orange pumpkin goop, flying seeds, dark candle smoke |
| Frankenstein | bright green slime, cracking electric-blue sparks |
| Evil Santa | dark green goop, red confetti, shattered candy canes |

## Work

- `ExplodingMushroom` becomes `ExplodingMonster`. The head's SDF-intersection pieces are cut as today (the exploding pieces in
  `content/models/characters/exploding/` show how), plus the profile's debris.
- `MonsterExplosion` (a ScriptableObject per head) holds:
  - the palette (goop, splat and mist colours)
  - the debris (mesh, count, speed, spin, life)
  - the mist or smoke (colour, amount)
  - a special (none, bouncing eyeball, bats flying out, tentacles deflating, electric sparks)
- **Debris models** (small SDF parts in `content/models/effects/monster/`): ice shard, snowball, bat, shamrock, weed tangle, eyeball,
  tentacle, bandage strip, fur tuft, fang, pumpkin seed, bolt and candy-cane piece. Drawn instanced and pooled, with nothing
  allocated per death.
- **Specials**:
  - The bats and the eyeball are short scripted motions (no physics on the web).
  - The tentacles scale down.
  - Frankenstein's sparks reuse the electro-gun's lightning.
- **Gore**:
  - Off: a cartoon puff in the profile's colours, with the body puffing too (44).
  - Splatty: pieces and the profile.
  - Extra Splatty: more debris and splats.
  - Ultra Gore: as today, plus the profile.
- **Sound**: one cue per profile family (ice, mist, splash, sparks, pop) in the sound bank (ticket 40). Empty cues stay silent until a
  clip is put in.
- `SpecialEffectsBench` shows all 12 in a row for review, with a capture strip per head.

## Done when

- All 12 explosions play in the bench and in a match, at every gore level, inside the effects' frame budget on the web (8 deaths at
  once on the Kitchen).
- The capture strips have been looked at by the user.

## Status

Not started.
