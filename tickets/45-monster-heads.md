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
- The hit capsule on `Head` is resized per head to the head's visible shape (changed by the user's review, see Status: no ±15%
  fairness clamp).
- **Order**: Yeti and Jack-o'-Lantern first, to settle the scale, the neck join, the readability at 50 m and the budget. Then the
  other 10 in pairs.
- A capture sheet of all 12 on red and blue bodies, front and three-quarter, near and at 50 m.

## Done when

- The 12 heads are meshed, imported and on the socket, and a match has bots with random heads (the random pick is in 47).
- The capture sheet has been looked at by the user.

## Status

**User review of phase 1 (2026-11-02): the Yeti and the Jack-o'-Lantern are approved, with two changes for every head, these two
included:**
- **20% bigger**: scale each head 1.2x about its socket origin (Yeti about 0.73 m, pumpkin about 0.52 m across). Re-fit the hit shape
  with it.
- **All heads a similar size, normalized** (user decision): every head is scaled so its tight-fit shape (below) is the same size as
  the others, within +-10% in volume and in each axis's radius. The target is 1.2x the mean of the Yeti's and the pumpkin's tight
  fits from phase 1 (the "20% bigger" above, applied to the common size, not to each head on its own). The scale is one uniform
  factor per head, in its model file, so the look is unchanged. Spikes, tufts, icicles, hats and stems stick out past the
  normalized core and don't count. A test checks all built heads against the target.
- **The hitbox equals the visible head** (user decision, replaces the +-15% clamp and the 85% fit): each head's hit shape is fitted
  to its own visible mesh. With the heads normalized, the hitboxes are also equal within +-10%, so no head is easier to hit. Fit
  it tightly: an ellipsoid fitted to the mesh's volume rather than the bounding box, so spikes, tufts and icicles don't inflate it,
  or a convex or mesh test if `Combat.Trace` can afford one on the web. Test that a shot just outside the visible head misses and
  one just inside hits.
  51's head pieces (`content/parts/<id>_head.sdf`, `tools/make-head-pieces.py`) must be re-cut from the bigger heads.
- **4 cm lower** (less neck): the head sits 4 cm lower on the soldier. Lower the whole head 4 cm against the socket (the head's
  underside at about 1.525 m, not 1.565). Don't move the socket, which 43's tests and 44's animation use. Check that the collar does
  not poke through the head's underside, and that the Yeti's icicles still clear the chest.

**User review 2 (2026-11-02): no visible neck, the Swamp's drips and the Vampire's collar removed.** All 12 heads lowered at least 5 cm more (the underside at 1.475 m or lower); the socket is unchanged. 5 cm was the baseline: from the back and the side the soldier's neck still showed under most round heads (the chin is the lowest point, the skull curves up behind it), so each head was lowered until the neck was gone in the front, side, three-quarter and back close-ups (`Media/heads-45/heads-closeup.png`, now with a back column). Lowering of each head's `dy` against phase 2 (cm): Yeti 13, Vampire see below, Leprechaun 16, Swamp 5, Cyclops 15, Cthulhu 15, Alien 12, Mummy 10, Werewolf 12, Pumpkin 8, Frankenstein 12, Santa 13. Swamp: the slime drips from brow, nose and chin are gone (mud, moss, reeds and the glowing eyes stay). Vampire: the black cape collar lined red is gone; the head had got its height from the collar, so the skull and hair were made taller (ellipsoids 0.15 to 0.19 and 0.175 to 0.215 tall) to keep every axis within 10%, and the head sits 20 cm lower than the old chin line, the chin at 1.32 m. `tools/normalize-head.py` takes `--under` (default now -0.34). Normalization after: Vampire scale 1.216 to 1.269 (fit 4.8% to 4.3% off), Swamp 1.265 to 1.266 (7.7% to 7.3%, z axis); the other ten scales are unchanged, all volumes 0.1045-0.1052 m3, worst axis 7.5% (Alien). Hanging parts now lie on the chest in front: the Vampire's fangs, the Alien's drool and claws, the Leprechaun's beard tips, the Yeti's icicles, the Cthulhu's tentacles, the Santa's and Mummy's beard and bandage ends (visible in `heads-all.png`, the new 12-head sheet: front and three-quarter near on the red body, 2 columns); the Vampire's jaw sits down in the collar line. The soldier's collar does not poke through any head in the close-ups. `BuiltHeadsTests.TheHeadsStandOnTheNeckAndClearTheCollar` now asks for an underside of 1.475 m or lower (and over 1.15 m). Head pieces re-cut and re-meshed for all 12 (128 cells); EditMode 721 pass.

**Phase 2 done (all 12 heads built); the user's visual review of the 10 new heads (Vampire, Leprechaun, Swamp, Cyclops, Cthulhu, Alien, Mummy, Werewolf, Frankenstein, Santa) is pending.** Phase 1 (the Yeti and the Jack-o'-Lantern, approved) is described below as it was; the review's changes are applied as follows.

Phase 2:
- **Normalized size**: target = 1.2x the mean of phase 1's tight fits (radii 0.290 x 0.281 x 0.295 m, volume 0.1048 m3; `HeadSize`). Each head has one uniform scale in its `content/parts/<id>_head.sdf` (a `Translate`/`Scale` pair at the top, written by `tools/normalize-head.py`, which also puts the underside at y=-0.29, 1.525 m, and sets the model's Detail to follow the scale). Scales and fits (off = the worst of volume and the three radii): Yeti 1.085 (6.1%), Pumpkin 1.388 (6.9%), Vampire 1.216 (4.8%), Leprechaun 1.173 (5.8%), Swamp 1.265 (7.7%), Cyclops 1.159 (5.9%), Cthulhu 1.245 (4.4%), Alien 1.226 (7.4%), Mummy 1.332 (6.5%), Werewolf 1.172 (7.4%), Frankenstein 1.091 (6.0%), Santa 1.114 (6.8%). All volumes 0.1046-0.1050 m3. `BuiltHeadsTests` checks every built head within 10%.
- **Hitbox = the visible head**: `HeadFit`/`HeadFitter` (Core) fit the upright ellipsoid with the mesh's own centre of volume and second moments, brought to the mesh's volume (spikes, tufts, icicles, hats, tentacles hold little volume); no clamp, no 85% factor. Three radii and a centre, turned with the player's yaw; `HeadHit.Hit` is the test `Combat.Trace` runs for a worn head. Tests: a shot 2 cm outside misses and 2 cm inside hits on every built head along x, y and z, and the same on a synthetic shape; not driven through a live `Combat` in Play mode.
- **4 cm lower**: the underside (lowest point within 12 cm of the neck's axis, sampled over the triangles) is at 1.525 m for the heads whose chin or main mass is the lowest part; heads with a beard tuft, a tentacle, a fang or a drip hang up to about 4 cm lower (the Leprechaun, Alien and Werewolf), and the test allows 4.5 cm. The collar does not poke through in the close-ups; the Yeti's icicles end near 1.47 m, as before; the Cthulhu's tentacles and the Alien's drool curl out in front of the chest.
- **Head bursts (51)**: `tools/make-head-pieces.py` and the `head-pieces` mesh run were repeated for all 12 (no gore code). The 47 stand-in needed no code change; its tests and `HeadsPlayTest` no longer assume which heads are built.
- **LOD0 triangles** (eyes included; all at 128 cells with 3 levels and `_fast`): Yeti 3868, Vampire 3196, Leprechaun 3420, Swamp 2906, Cyclops 2724, Cthulhu 2496, Alien 2870, Mummy 3220, Werewolf 2970, Pumpkin 2818, Frankenstein 2608, Santa 3336.
- **Capture sheet**: `Media/heads-45/heads-<id>.png` for each of the 12 (red above blue; front and three-quarter near, then at 50 m through the game's lens, the middle 160 x 240 pixels scaled 3x) and `heads-closeup.png` (front, side, three-quarter, one row per head in calendar-built order). The soldier is in the T-pose. Looks (plainly): Yeti white shaggy with grey-blue face, fangs and icicles; Vampire pale face, black hair, red eyes, two long fangs, no collar since review 2; Leprechaun warty green face, yellow slit eyes, ginger beard, sharp teeth, torn green top hat; Swamp brown mud with moss, reeds and glowing yellow eyes (no drips since review 2); Cyclops tan, one big eye under a brown brow bar, one big tusk; Cthulhu sea-green dome, two orange slit eyes, tentacles forward; Alien dark green-black, two huge black glossy eyes, antennae with yellow tips, yellow drool; Mummy round, patchy linen wraps, a black eye band; Werewolf tan-brown fur, tall ears, long snout, open jaws; Pumpkin orange with glowing carving; Frankenstein square green head, flat black hair, steel bolts; Santa white beard, glowing red nose, big red hat. Known rough spots: the Mummy's eye band cuts across the whole head (visible from the side), the Cyclops' brow reads as a bar, the small eye parts are coarse close up.

Left: the user's look at the 10 new heads and the sheet (including readability at 50 m); a Play-mode look with the animated bodies; a burst strip of the new heads (`GoreCheck` still only runs the Yeti and the pumpkin; add ids to its `Heads` list); the web frame time.

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
