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

**Done in code and tested; left for a human: a look at the captures, the web frame-time check.** EditMode 656 passed, 0 failed; a Play-mode check in the Meadow (`GoreCheck`) passed (97 checks).

What was done:

- **`GoreRules`**: the blood colour comes from the head id (`BloodOf`, the `MonsterHeads` table), `Tint` turns the goop models' shades to it (the blood's own hue and saturation, the goo's brightness kept as shading), `Recolour` and the team hue are gone. `GibKind` is the head's only (`Piece`). New: `Puffs` (a kill with gore off), `BodyBursts` (always false), `Ragdolls`, the ragdoll's numbers (3 s, settle speed, push), `TeamRed`/`TeamBlue` (what `BattleSide.ColorOf` returns). Tests: every head has its table colour, pairwise 0.2 apart and away from human red and both teams, no level bursts the body, the pieces fly away and up, the goo takes the colour.
- **Head burst**: `content/parts/<id>_head.sdf` holds each head's shape (yeti and pumpkin moved out of their models, same triangles: 3,282 + 2 x 296 and 2,916) and `tools/make-head-pieces.py` writes `content/models/characters/head-pieces/<id>-head-piece0..6.sdf`: the head intersected with one Voronoi cell of seven fixed seeds (the cells split space, so the pieces are the head), cut faces in the new `head_inner` material, meshed at 128 cells and 1 level (`sdf mesh content/models/characters/head-pieces --into Unity/Assets/Mushrooms/Generated/characters/head-pieces --resolution 128 --lods 1`). `MonsterHeadModels.Entry.pieces` lists them, `Corpse.Make` bursts them where the head was: **a new head needs only its `_head` part, the script and one mesh run, no gore code**. Per level: Off a puff in the blood colour (no pieces, no gobs); Splatty the pieces and 28 gobs; Extra 40; Ultra three times the gobs, every piece flung far trailing drops and gobs. Splats land on the ground and level only.
- **Plug-in for 46**: `HeadBurstProfile` (`Register(headId, profile)`; `ReplacesPieces`, `Special(HeadBurst)` with the head's position, pose, push, blood, level, weapon), called by `Gore` for every burst at Splatty and up.
- **Ragdoll**: built, not ported (BattleRoyale has none). `SoldierRagdoll`: ten rigid bodies on 43's skeleton (pelvis, chest on Spine_02, two upper arms, two forearms, two thighs, two calves), a capsule along each bone, a `CharacterJoint` to the parent (swing 35 to 70 degrees, twist 10 to 30) made at death so the limits are round the pose it died in. Neck, head, hands and feet ride on their parent. `Gore` clones the team's soldier prefab from a per-skin pool, copies the dead body's pose, hides the original (`SoldierPuppet.HandOverToRagdoll`, until `Revive`) and pushes the chest by the hit's direction times `GoreRules.Strength`; the head burst happens in the same frame. Colliders are on the Ignore Raycast layer, exclude that layer (no other ragdoll, piece or gib) and ignore the players' capsules; they hit the level (and machines, which share its layer). Cheap: at most `MaxCorpses` (times the quality's gore amount) lie, the oldest sinks first; each simulates 3 s at most or until every body is under 0.15 m/s for 0.4 s, then its joints are destroyed and bodies turn kinematic with the colliders off. It sinks after the corpse time or when its player respawns. The collar is closed already (the neck is a closed cylinder under the collar, see 43's capture).
- **Removed from body gore**: the scout corpse (eyes, hands, cap, stem pieces), the stump gush and the pool under it, the team-coloured blood, the scout puff. A scout fallback body (soldier models not imported) only puffs.
- **Checks**: `tools/unity-batch.sh` takes `SDF_NO_QUIT=1` for an editor-driven play check. `SDF_NO_QUIT=1 tools/unity-batch.sh gore51 -executeMethod SdfBattle.Editor.GoreCheck.Run -captureDir Media/gore-51` kills a bot wearing the Yeti and the Jack-o'-Lantern at each gore level and checks: head pieces (7, none at Off), ten simulating bodies, nothing but the skinned body on the ragdoll (no piece, gib or blood, headless), lying still by 4.5 s; then 6 players killed at once: at most 6 ragdolls simulate, all lie after 3 s. No `SdfBody.PlayTests` case was added: that assembly cannot see the game's own types, so this driver is the Play-mode route. The EditMode `BuiltHeadsTests` also check 7 pieces per built head.
- **Captures** (`Media/gore-51/`): `gore-yeti.png`, `gore-pumpkin.png` (a row per level, Off to Ultra Gore; columns 0.25, 0.7, 1.6 and 4.5 s after the kill), `gore-ragdoll.png` (the headless corpse: falling, then lying from other sides), `gore-check.txt` (the trace).

Left:

- The user's look at the captures: the burst's colours and the pieces' cut faces (pale `head_inner`), the ragdoll's limb poses (the elbow and knee limits are cones, not hinges).
- The other 10 heads' bursts come free once they exist: add `content/parts/<id>_head.sdf` (their model refers to it), run `tools/make-head-pieces.py` and the `head-pieces` mesh, then the head builder.
- The web frame time with 8 ragdolls on the Kitchen: not measured (the editor run's frame time means nothing). Spawning a ragdoll the first time of each skin instantiates and builds it (a hitch, once; a pre-warm at match start would remove it).
- The Mummy's blood is a colour like the others, not a dust cloud (46's profile can add the cloud).
