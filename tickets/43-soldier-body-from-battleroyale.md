# 43 — The body: BattleRoyale's soldier

Part of **Minigun Monsters** (41-50). Every player gets a body: the soldier of `../BattleRoyale` (its ticket 04), rigged on the Synty
skeleton, without its own head. The monster head (45) sits on its `head_top` and `Head` bone, and the body's skin shows the team.

Depends on: nothing (it can run alongside 41 and 42). Blocks: 44, 45.

## Source (copy from a committed commit and name it in the commit message; BattleRoyale is at `80df274`)

| From `../BattleRoyale` | What |
|---|---|
| `src/Kk.Rigging`, `src/Kk.Materials`, the format-2 parts of its `src/SDFPipeline` | Rigged SDF meshing (format 2, `skeleton=`, bones, sockets, `collider bones`, skinned .glb). This project's `SDFPipeline` has no rigging. Port only what the soldier needs, keeping this pipeline's CLI (`sdf mesh`) and tests |
| `content/skeletons/synty.skeleton` | The skeleton (49 bones, T-pose rest) |
| `content/models/soldier/soldier.sdf`, `ART.md`, `content/materials/soldier/` | The body and its skins (olive base and per-skin `.sdfmat`) |
| `unity/BattleRoyale/Packages/com.battleroyale.rig` | `RigBuilder`, `GlbFile`, `SkeletonExport`, `RigHit`, `RigInfo`, `RigConvert` (not `RigPlayer`: the body plays Unity's Animator, BR decision 25). Into this project as `Assets/Rig/` or a local package |
| `Assets/Game/Soldier/Model/` | `SoldierModels`, `SoldierSkins` (the vertex-colour skin remap at import, one mesh per skin) |

Both projects are on Unity 6000.3.0f1.

## Work

- **A headless soldier**: `content/models/characters/soldier/soldier.sdf`, from BR's file with no skull, hair, ears, nose, eyes, brows
  or jaw. The bones stay (the clips key them), and the face bones keep a tiny hidden piece if the pipeline needs every bone to own
  geometry. The collar stays, so the neck has somewhere to end. No helmet. The backpack is optional (it stays if it reads well with a
  flag on the back).
- **Team skins**: `blue` from BR, and a new `red` skin (BR has orange and pink, not red), made from the team colours in `GuiPalette`
  and the flags (`flag-red`, `flag-blue`) so body, flag and HUD agree. Two skin meshes, one per team.
- **Size**: the soldier is 1.815 m to the crown. The player capsule is 1.75 m (`BattleSide`) with the eye at about 1.5 m. Pick one:
  scale the body to the capsule, or the capsule to the body plus head. Measure what the bots and the hit tables
  (`Combat`, the head height) assume, and keep them consistent.
- **Sockets**:
  - `head_top` and `Head`: the monster head (45).
  - `grip` on `Hand_R`: the third-person weapon (this project's mushroom weapons, with an offset per weapon; dual pistols also on
    `support`).
  - `back`: the carried flag.
- **Hit capsules**: BR's `collider bones` (the head capsule resized by 45 per head) replace the mushroom's single capsule, or map onto
  `Combat`'s head rule. Pick the one with less churn, and keep headshots the same in feel.
- **Prefabs**: `soldier_red.prefab` and `soldier_blue.prefab` with a saved Generic avatar (BR's `RiggedImport` leaves the base
  prefab's avatar unsaved; fix that in the port).
- **Budget**: 8 bodies at about 6.5k triangles. The web frame time is checked with 8 animated bodies on the Kitchen (the heaviest level).

## Done when

- `sdf mesh` makes the headless soldier's skinned .glb in this repo. The two skin prefabs import with an avatar, the sockets and hit
  capsules.
- A capture of red and blue from four sides, and next to a mushroom scout for size.
- The ported rig tests (BR's model tests, cut to what was ported) pass here.

## Status

Done in code and tested; left for a human: a Play-mode look at the capture (visual approval) and the web frame time with 8 animated bodies on the Kitchen (needs 44 to animate them, and is not measured). Ported from BattleRoyale `80df27498e87bb919a25049b4c1c6d1435023b61` (`80df274`).

What was ported or made:

- **Rigging pipeline**: `Kk.Rigging/` (namespace kept for diffing; BR's `Kk.Rigging` minus `Anim/`, `Png/` and `SkinPreview`: no `.anim` clips, no contact sheets; the body plays Unity's Animator), with `RigFiles` (format 2 detection, loading, meshing to a skinned `.glb`). `Kk.Materials` and `kk` are not ported. In `SDFPipeline`: `MeshingOptions.KeepDensity` (joint density), the simplifier's `locked` argument and `InternalsVisibleTo Kk.Rigging`; this pipeline's own GPU rule is kept.
- **CLI**: `sdf mesh` and `sdf render` take a format 2 file like any other (a rigged model has one mesh, no LODs); `sdf unity` meshes it into `Unity/Assets/Rig/Generated/characters/soldier/` (new `--rigs-into`), not into the toon importer's `Mushrooms/Generated`.
- **Content**: `content/skeletons/synty.skeleton`; `content/models/characters/soldier/soldier.sdf` (BR's soldier without skull, hair, ears, nose, eyes, brows, jaw; the face bones keep a 4 mm sphere buried in the neck; collar, neck and shoulder straps stay; no helmet, no backpack part; 3,830 triangles at 128 cells, 50 bones, 5 sockets, 18 hit capsules); `materials/characters/soldier/` with the olive base set (never worn), `blue` from BR and a new `red` (fatigues = `BattleSide.ColorOf(West)`, the HUD amanita and flag red family).
- **Unity** (`Unity/Assets/Rig/`): `Core` (`RigHit`, `RigInfo`, `RigConvert`, `SoldierBody`, `SoldierModels`, `SkinRemap`, `CapsuleSizing`), `Editor` (`RigBuilder`, `GlbFile`, `MiniJson`, `SoldierSkins`, `SoldierCapture`), `Tests`. `MushroomBuilder.FillSkinned`/`SharedMaterials` (a skinned mesh in the same packed layout, position and UV1 as floats) and `SdfGlb` skipping integer attributes. Prefabs `soldier_red.prefab` and `soldier_blue.prefab` in `Assets/Rig/Generated/Soldier/` with one saved Generic avatar (BR left it unsaved), made by `SdfRig.Editor.SoldierSkins.ImportAll` after `sdf unity`; all of `Assets/Rig/Generated` is git-ignored like the other generated output. `RigPlayer` was not ported.

Decisions:

- **Size**: the body keeps the model's scale (1:1). The headless body ends at the neck (1.569 m); a head of about 25 cm on it reaches 1.82 m, within 5 cm of the 1.75 m capsule and the eye at about 1.5 m. Nothing in `Combat` or the bots changes.
- **Hit capsules**: both. The prefab carries BR's trigger capsules per bone (`RigHit`, the `Head` one 0.13 m), and `Combat`'s head rule (`headHeight`, `headCentre`) is left as it is until the body is wired in (44) and the heads sized (45); `CapsuleSizing.FitHead` resizes a head capsule. Less churn: the player and bot prefabs are not touched by this ticket (41 was changing them), so wiring the body in belongs to 44.
- **Skins**: `red` and `blue` only, by the vertex-colour remap at import, one mesh asset per skin.

Checks: `dotnet test Kk.Rigging.Tests` 93 passed (BR's rig, skeleton, glb, part and pose tests, cut to what was ported, plus `SoldierModelTests`); Unity EditMode 433 passed (including `SdfRig.Tests`, 26 cases on skin selection, socket lookup, the colour remap, capsule sizing and the built prefabs: avatar, sockets, 18 trigger capsules, 50 bones, under 6,500 triangles). Capture: `Media/soldier-43/soldier-sides.png` (red above blue, front, right, back, left) and `soldier-size.png` (blue and red soldiers next to the chanterelle and amanita scouts); the arms leave the tiles in the T-pose.
