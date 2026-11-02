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

**Phase 1 done: the Yeti and the Jack-o'-Lantern are modelled, meshed, imported and on the soldiers. The user's review of them is pending; the other 10 are not started** (the Order above: look at these two for scale, neck join, readability at 50 m and budget first).

What exists:

- **Models**: `content/models/characters/monsters/yeti.sdf` and `pumpkin.sdf`, materials in `materials/characters/monsters/` (the pumpkin's `pumpkin_glow` is `emission = 1`: the carving's floor, the candle). `collider none`, `cull 200`, a top-level `Detail` (a head this small gets few cells at 128). The Yeti's eyes are the machines' `eye_machine` part at half size; the pumpkin's eyes are the carved triangles. Meshed with `sdf mesh content/models/characters/monsters --into Unity/Assets/Mushrooms/Generated/characters/monsters --resolution 128 --lods 3` and again with `--lods 1 --suffix _fast`; `MushroomBuilder` makes `Assets/Mushrooms/yeti.prefab`, `yeti_fast.prefab` and the pumpkin's (committed like the scouts'). LOD0: Yeti 3,874 triangles (3,282 head + 2 x 296 eyes), pumpkin 2,916.
- **`Assets/Heads`** (`SdfHeads.Core`, Editor, Tests): `MonsterHeads` (the table of 12: id, month, display name, prefab path, 51's blood colour, `Built`; `RandomBuiltId`), `HeadHit` (the hit shape and the fairness rule), `MonsterHeadModels` (the generated registry in `Assets/Heads/Generated/Resources`, git-ignored, made by `MonsterHeadBuilder` after the prefabs exist: `tools/unity-batch.sh heads -executeMethod SdfHeads.Editor.MonsterHeadBuilder.BuildFromCommandLine`), `HeadWear` (puts a head on the soldier's `head_top` socket), `MonsterHeadCapture`. The pipeline's route for a new head is the same two `sdf mesh` calls; no new builder was needed for the meshes.
- **Scale and neck join**: heads are modelled with the origin at the socket (1.815 m), facing +Z, the underside at about y = -0.25 (1.565 m, on the neck's top at 1.569, the neck's stub inside). Yeti bounds 0.61 x 0.59 x 0.61 m (spiky tufts and icicles included; its mantle and fringe hide the join, the icicles reach 1.47 m over the chest), pumpkin 0.43 x 0.50 x 0.43 m, against a human head of about 0.23 x 0.16: about 2 times. The head turns on the Head bone, 0.25 m under the socket.
- **Hit shape**: `HeadHit.Fit` takes 85 % of the head's bounds as an ellipsoid and holds its radii to within +-15 % of 0.20 m across and 0.22 m up, so no head is easier to hit (tested as a function and on the built bounds). Yeti 0.230 across, 0.251 up; pumpkin 0.181 across, 0.212 up. `HeadWear` resizes the soldier's Head capsule (`CapsuleSizing.FitHead`), and `Combat.Trace` uses a worn head's shape and height for the headshot ellipsoid in place of the old cap's. The puppet still removes the prefab's capsules, as 44 decided.
- **Stand-in**: `SoldierPuppet.Create` wears a random built head for you and every bot, one line (`MonsterHeads.RandomBuiltId`); 47 replaces it.
- **Capture sheet** (`Media/heads-45/`): `heads-yeti.png`, `heads-pumpkin.png` (red above blue; front and three-quarter near at 4.6 m, then front and three-quarter at 50 m through the game's 60 degree lens at 1080 lines, the middle 160 x 240 pixels scaled 3x without smoothing) and `heads-closeup.png` (front, side, three-quarter on the red body). The soldier is in the T-pose.
- **Checks**: EditMode 650 passed, 0 failed (35 of them this ticket's: table, blood colours, hit fairness, capsule fit, registry, triangle budget, head on the socket).

Left: the user's look at the capture (scale, the neck join with the Yeti's low icicles, readability at 50 m where the heads are a few pixels, the pumpkin's glow in Play mode and through the post-processing), then the other 10 heads in pairs, the 12-head sheet, and a Play-mode look with the animated bodies.
