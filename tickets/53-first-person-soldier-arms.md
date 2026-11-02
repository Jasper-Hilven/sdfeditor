# 53 — First-person arms: the soldier's, not a mushroom's

Part of **Minigun Monsters**. In first person, the arms and hands holding your weapon still look like brown, spotted mushroom stalks
(see the Kitchen dual-pistol and Meadow minigun frames in `Media/review-50.png`). You are a soldier now (43), so your arms should be the
soldier's: sleeves in your team's colour and the dark gloves, holding the weapon.

Depends on: 43 (the soldier and its red and blue skins), 44 (`WeaponView`, the first-person hands, "the floating hands take the team's
glove colour"). The weapons themselves stay as they are: they are mushroom-shaped on purpose (ticket 50).

## Decisions

- First person shows **forearms in the team's sleeve colour plus gloved hands**, the same look as the third-person soldier, gripping
  each weapon: one hand for the pistol and sniper, both hands for the minigun, flamethrower, electro-gun, poison-gun and blob-gun, and
  one per pistol for dual pistols.
- No mushroom stalk, spots or cap pattern on anything that reads as your arm or hand.
- The recoil, reload and switch motion of today's view model stays.

## Work

- First find what draws the stalks: `WeaponView`'s first-person hands, a part of each weapon model, or the old scout's hands. Say
  which in the Status. If the brown spotted shape is part of a weapon model's grip and not an arm, keep the grip, add the gloved hands
  and sleeves around it, and note it.
- Build the first-person arms:
  - Preferred: from the soldier itself, either the soldier's arm and hand meshes from 43 or a small SDF forearm-and-glove part in
    `content/parts/` that reuses the soldier's materials.
  - Meshed at 128 cells, recoloured per team (43's skins).
  - Posed per weapon at its grip and support points.
  - The hands can be static poses (a grip pose per weapon). A rig is not needed.
- `WeaponView`: show them for you only, in the team's colour, and keep them out of the third-person body (which is hidden in first
  person, 44).
- Capture every weapon in first person, red and blue, through a batch route.

## Done when

- In the first-person capture of every weapon, the arms and hands are the soldier's (sleeves and gloves, team colour), with no
  mushroom look, holding the weapon at its grip.
- The EditMode tests pass, and the user has looked at the capture.

## Status

Done in code and captured (721 EditMode pass, no new test cases); left for a human: the look by eye in play (the capture is `Media/arms-53/arms.png`: red above blue, every weapon).

What drew the stalks: there were no first-person hands at all. `WeaponView` showed only the weapon model scaled to 0.3 in the camera's corner, and the brown, spotted, pale shapes that read as arms were the weapon models' own grips (the pistol's ink-cap slide and tan stalk grip with its bulb, the minigun's bolete cap and stem), seen close up. The models are kept as they are, as the ticket says; the hands and sleeves are added round their grips.

How the arms are built and posed:
- Four small SDF models in `content/models/characters/soldier-arms/` meshed at 128 cells (the pipeline's usual 128 cells per 2 m, 5 mm cells for the glove): `soldier-glove-red/blue` (a right fist round a vertical grip, in the soldier's `soldier_gloves_<team>` material: four fingers, thumb, short cuff, 396 triangles) and `soldier-sleeve-red/blue` (a tapering tube one metre long in `soldier_fatigues_<team>`, 294 triangles). Prefabs `Assets/Mushrooms/soldier-glove-*.prefab`, `soldier-sleeve-*.prefab`; `WeaponTableAsset.gloveModels` and `sleeveModels` (red, blue) are filled by `WeaponTableSetup`.
- `FirstPersonArms` (Battle/Runtime) holds each weapon's hands: the primary (right) fist's place, turn and size in the weapon's model space, and for the minigun, sniper, electro-gun, flamethrower, poison-gun and blob-gun a support (left, mirrored glove) hand: the minigun's two spade grips, a hold under the barrel for the others. The pistol has one hand, dual pistols one on each pistol (the second mirrored). Static poses, no rig.
- `WeaponView` (first person only) makes the gloves as children of each weapon model, so they take the recoil, kick and the minigun's shiver, in the team's colour (remade if the team changes), and each frame aims a sleeve from the glove's wrist to a shoulder point under the camera. They hide with the weapon (the sniper's zoom). The third-person body and bots are untouched.
- The weapons' first-person `viewGrip` heights were raised by 0.05 to 0.19 (minigun most: -0.36 to -0.17) so the hands are on the screen; the minigun's grips had been below the bottom edge.
- Capture: `SdfBattle.Editor.ArmsCapture.Run` (`SDF_NO_QUIT=1 SDF_ARMS_OUT=<folder> tools/unity-batch.sh arms53 -executeMethod SdfBattle.Editor.ArmsCapture.Run`): plays the Meadow, makes you immortal, puts each of the 8 weapons (single pistol, dual pistols and the six others) in your hands as red then blue, and renders your camera to a PNG each; the contact sheet is those 16 frames.

Known weaknesses: the gloves are the soldier's dark ones, so against the big weapon models they read as dark mitts; the weapons are big and fill the corner, so the primary hand is partly behind the weapon body; the minigun's hands are dark and its sleeves barely show. The support-hand holds are first guesses by eye from side renders.
