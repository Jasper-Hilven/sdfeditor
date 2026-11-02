# 49 — Monster powers

Part of **Minigun Monsters** (41-50). The spore powers become monster powers. The rules, keys and numbers stay the same; only the names,
icons, colours and effects change.

Depends on: nothing (it can run alongside the others).

## Renames

| Was | Becomes | Does (unchanged) |
|---|---|---|
| Spore meter | **Rage** meter | filled by kills, enemy machines and flag play |
| Mycelium Rush (Q) | **Frenzy** | speed |
| Regrowth (F) | **Regenerate** | healing |
| Puffball Armour (C) | **Thick Hide** | less damage |

## Work

- `SporeHud`: the labels, icons (`GuiIcons`) and meter colour (rage red or orange), with the key letters by position as today.
- The power effects (`Spores` and its runtime visuals): replace spore particles with a rage glow or steam, a speed streak for Frenzy,
  a green pulse for Regenerate, and a stony or scaly sheen for Thick Hide. Optional: 44's `body_roar` on use, for the bots too.
- Text: every player-visible string, including warm-up prompts, kill-feed lines, the settings help and `description.md` (50).
- Code names: rename the player-visible API and enums where it is cheap. The about 54 internal references may keep `Spore` names if
  renaming them only churns. Make a note in `SdfBattle.Core` saying which.

## Done when

- No "spore", "mycelium", "regrowth" or "puffball" appears in anything the player sees. The EditMode tests for the powers pass
  unchanged in their numbers.

## Status

Done in code; left: a Play-mode look at the three effects and the power cards, and a Play-mode listen.

- Renamed: `SporePower.Rush/Regrowth/Armour` to `Frenzy/Regenerate/ThickHide`; the rule fields (`frenzySpeed`, `regeneratePerSecond`, `thickHideDamage`, ...), `Spores.FrenzyColour/RegenerateColour/ThickHideColour` and the tests. `Spore*` type names, `GuiHud`'s spore fields and the `spore.*` audio cues stay (noted in `SporeRules.cs`).
- Strings: rage meter, Frenzy, Regenerate, Thick Hide in the HUD (builder and prefab), loading tips, caption, end screen, pickup label, kill-feed lines, credits, `description.md`, `control.html`.
- HUD: meter and tint in rage red-orange (`GuiPalette.Rage`), Thick Hide stone grey (`GuiPalette.Stone`); `SporeHud` also sets the colours at run time.
- Effects: steam and sparks on use; Frenzy speed streaks and steam, Regenerate green pulse ring, Thick Hide grey glints and the swell.
- Tests: EditMode 433 of 433 pass. Capture: `Media/powers-49/Hud.png` (the power cards and line only show in a match).
