# 46 — Monster explosions

Part of **Minigun Monsters** (41-50). When a monster dies, its head bursts in its own way. The mushroom explosion becomes a
data-driven monster explosion with one profile per head.

Depends on: 45, 51 (head-only gore: the head burst, the blood colour per head, the body's ragdoll). Works best after 44.

**Changed by 51:** only the head bursts; the body never puffs or bleeds. Each profile's goop, splat and mist colours start from the
head's blood colour in 51's table.

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
  - Off: a cartoon puff of the head in the profile's colours; the body only ragdolls (51).
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

**Done in code and tested; left for a human: a look at the captures, the web frame-time check, the sounds by ear.** EditMode 721 passed, 0 failed (20 of them this ticket's, `MonsterExplosionsTests`). The Play-mode check (`GoreCheck`, extended to all 12 heads and every gore level) passed: 690 checks, no errors, no exceptions.

**Built by 51 (the plug-in point):** `HeadBurstProfile.Register(headId, profile)`; this ticket's `ExplosionProfile` is registered for all twelve heads before the first scene. The head's seven cut pieces, gobs and splats are 51's and unchanged; the profile's `Special` runs right after them. Off has no profile (51: the head puffs only; the body never bursts).

What was done:

- **Debris models** (`content/models/effects/monster/*.sdf`, materials in `materials/effects/` plus the heads' own): ice shard, snowball, bat, shamrock, weed tangle, eyeball (the shared `eye` part at 0.8), tentacle, bandage strip, fur tuft, fang, pumpkin seed, bolt, candy-cane piece, and red confetti (a 14th, for Santa's confetti). Meshed at 128 cells, one level (`sdf mesh content/models/effects/monster --into Unity/Assets/Mushrooms/Generated/effects/monster --resolution 128 --lods 1`): 300 to 1,500 triangles each. Registry: `MonsterDebrisBuilder` (`tools/unity-batch.sh debris46 -executeMethod SdfEffects.Editor.MonsterDebrisBuilder.BuildFromCommandLine`), generated and git-ignored like the heads' registry.
- **Data**: `MonsterExplosion` (a ScriptableObject made in code per head by `MonsterExplosions`, no asset files): palette (goop, splat, mist; goop and splat are the head's blood colour, the mist is turned toward the ticket's colour), debris specs, mist, glints, sparks, drops, extra gobs and splats, special, sound family. Gore levels scale it (`DebrisScale` 1 / 1.6 / 2, `MistScale`, extra splats 0 / 6 / 12, Ultra Gore flings 1.35x); a special's count (the eyeball, bats, tentacles, bolts) stays fixed.
- **Runtime**: `MonsterDebris` is one pool (640 pieces, times the quality's gore amount), scripted motion only (ballistic, fluttering, bat flight, bouncing and rolling eyeball, deflating tentacle; the ground is the burst's height refined by a downward ray while a piece falls near it), drawn with `Graphics.RenderMeshInstanced` (a surface call and an ink-line call per model part, 20 calls at most for 8 deaths at Ultra Gore). `MonsterBurst.Play` emits everything into pools made at match start; Frankenstein's bolts are the electro-gun's `CombatFx.Jump`. `ExplodingMushroom` became `ExplodingMonster` (a mushroom, or with `headId` a head with its profile).
- **Sound**: five cues in the bank (`monster.burst.ice|mist|splash|sparks|pop`). Ice has no clip yet (silent); mist uses the poison spray pitched down, splash the blob impacts, sparks the electro chain, pop the puffball puff pitched up.
- **Bench**: `SpecialEffects.unity` has a row of the 12 heads (each on a stand, with its pieces). Keys: M all 12, `,` and `.` one at a time, V moves the camera in front of the row, the loop takes them in turn; `goreLevel` on the bench (default 2).

Profiles (debris at Splatty; mist puffs; special):

| Head | Debris | Mist or other | Special |
|---|---|---|---|
| Yeti | 12 ice shards, 5 snowballs | pale frost puffs, 20 white glints | none |
| Vampire | 6 bats | 20 deep purple puffs | bats fly out, flapping |
| Leprechaun | 24 shamrocks | 18 gold glints, 10 gold sparks | none |
| Swamp Thing | 4 weed tangles | brown puffs, 16 dirty drops, 14 extra gobs | none |
| Cyclops | 1 giant eyeball | 10 extra gobs, 3 splats | eyeball bounces, rolls, gaze wanders |
| Cthulhu | 4 tentacles | 12 extra gobs, 10 drops | tentacles wriggle and deflate |
| Acid Alien | none | 16 extra gobs, 6 splats, 24 glowing glints, 14 drops, a little smoke | none |
| Mummy | 10 bandage strips | 26 big sand puffs (the sand cloud) | none |
| Werewolf | 12 fur tufts, 4 fangs | 22 orange sparks | none |
| Jack-o'-Lantern | 18 seeds | 12 dark smoke puffs, 10 extra gobs | none |
| Frankenstein | 2 bolts | 26 blue sparks, 10 extra gobs | 4 lightning bolts |
| Evil Santa | 7 candy-cane pieces, 28 red confetti | 10 extra gobs | none |

Notes against the old text: Santa's goop is his candy-pink blood (51's table), not the ticket's dark green; Off throws nothing but the head's puff (51), not a puff in the profile's colours.

**The NullReferenceExceptions** (`Match.Update`, `Gore.Update`, `SporeHud.LateUpdate`, `CombatFx.LateUpdate`, every frame): not a bug in Gore or the head burst. Unity recompiled and reloaded the domain while Play mode was running, because a script in the shared checkout changed during the run (in my baseline run it was my own edit of `Cues.cs`, logged as "Assetdatabase observed changes in script compilation related files" then "Reloading assemblies after finishing script compilation"). A reload empties every non-serialized field of the live `Match`, `Gore`, `CombatFx`, so all of them throw each frame. The same happened to ticket 48's `GameLoopPlayTest` while I edited scripts (6 million log lines, a 1.9 GB log; I stopped that run). With no script edited during the run, `GoreCheck` has none. Rule: no `.cs` edits while any Play-mode driver runs in the checkout.

Checks:

- Per kill at all 12 heads and 4 levels: the profile is registered; at Splatty and up the debris thrown equals the level's count and the explosion allocated 0 bytes (`GC.GetAllocatedBytesForCurrentThread` around `MonsterBurst.Play`; the first death of the run included); at Off nothing is thrown.
- 8 deaths at once at Ultra Gore (a different head each), in the Meadow: at most 242 debris alive, 20 instanced draw calls. The web frame-time check on the Kitchen (8 deaths at once) is not measured: the editor's frame time says nothing about WebGL2. What the editor can give is above (pool 640, draws, zero allocations per death); the web number needs a web build.

Captures: `Media/gore-46/gore-<id>.png` for each of the 12 (a row per gore level, Off to Ultra Gore; columns 0.15, 0.5, 1.2, 2.4 and 4.5 s after the kill, in the match) and `gore-check.txt`: `SDF_NO_QUIT=1 tools/unity-batch.sh gore46 -executeMethod SdfBattle.Editor.GoreCheck.Run -captureDir Media/gore-46`. The bench's own strips (`SpecialEffectsSnapshot.Capture`, `monster-<id>.png`, `monsters-all.png`, `monsters-row.png`) come out all black in this environment (the edit-mode snapshot renders every frame black, the older shots too, with "Attachment 0 was created with 4 samples but 8 requested"): not verified visually. The bench scene builds and the debris draws in a match; the bench's debris was not seen.

Left: the user's look at `Media/gore-46`; the web frame time; the bench's strips; the ice sound; by ear, the reused clips. Looks (plainly): the Vampire's mist is a cloud of flat purple discs with tiny dark bats among them; Santa's burst is pink goop with red paper squares and small red-and-white candy pieces; the debris is small next to the head's pieces and the mist.
