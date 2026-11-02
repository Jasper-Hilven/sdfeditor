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

Not started.
