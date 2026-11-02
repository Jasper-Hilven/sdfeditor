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

Not started.
