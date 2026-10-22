# 18 — Red vs blue

The two sides read as **red** (west, amanita) and **blue** (east, chanterelle) at a glance: the players, the flags and the gizmos.

Depends on: 17 (battle sides, flag stands).

## The scouts

- **Chanterelle scout** (`content/models/characters/chanterelle-scout.sdf`, `materials/characters/chanterelle/`): blue instead of
  yellow-orange. Cap, ridges and flesh in a blue family (cap the strongest, flesh/stem lighter), same toon setup and subsurface as
  now, only the hues change. Keep the shape.
- **Amanita scout** (`amanita-scout.sdf`, `materials/characters/amanita/`): the stem and the hands get a red tint, clearly redder than
  now but less saturated / lighter than the cap, so the cap stays the strongest red.
- Other models that share these materials (check with grep: the chanterelle/amanita materials may be used by giant mushrooms, field
  fungi, the exploding character pieces) must **not** change: give the scouts their own materials if a material is shared.

## The flags

- `flag-gold` becomes `flag-blue` (model, prefab, builder entry, tests, docs). East is blue.
- Both flags are **plain**: one flat colour each, no flecks, pattern or noise (`flag_red` loses its white flecks). The pole and brass
  knob stay as they are.
- `BattleSide`'s colours (spawn gizmo) and comments: west red, east blue.

## Done when

- Meshed in the main checkout (only the changed models: `sdf mesh <file> --into Unity/Assets/Mushrooms/Generated/<group>` with the
  resolution and LODs each already uses), tests pass, references to gold/flag-gold gone, old `flag-gold` prefab/glb removed.
- Ticket 17 and README text updated where they say gold.

## Status

**Done in the pipeline; the prefabs and scenes are to be rebuilt in Unity.** Built:

- Chanterelle scout: `chanterelle_cap`, `chanterelle_ridges` and `chanterelle_flesh` are used by the scout alone, so they changed in
  place, hues only (toon setup, subsurface, detail, speckles as before). Cap a clear cornflower blue `(0.14, 0.4, 0.88)` (#2466E0),
  edge `(0.06, 0.2, 0.62)`, subsurface `(0.35, 0.62, 1)`; ridges a little darker `(0.1, 0.32, 0.78)`; flesh (stem and hands) lighter
  `(0.26, 0.5, 0.92)` (#4280EB), with pale blue flecks.
- Amanita scout: `amanita_stem` is shared (giant amanitas, `giant-amanita-cluster0`, the castle's veil, amanitas, lantern and merlon
  parts), so the scout has its own `materials/characters/amanita/amanita_scout_stem`: a rosy red `(0.84, 0.32, 0.27)` (#D65245),
  darker red patches and flecks, subsurface `(1, 0.44, 0.34)`; lighter and much less saturated than the cap `(0.72, 0.05, 0.025)`.
  `content/parts/amanita_scout.sdf` uses it for the stem, bulb, ring and hands. The exploding pieces (`characters/exploding/`) are cut
  from that part, so they follow the scout (their cut faces stay `amanita_flesh`); nothing else that used `amanita_stem` changed.
- Flags: `flag-gold` is now `flag-blue` (model, `FlagStandBuilder.Sides`, `FlagStandTests`, ticket 17, README); `flag_gold.sdfmat`,
  `flag-gold.glb`, its mesh assets and `Assets/Mushrooms/flag-gold.prefab` deleted. `flag_red` `(0.76, 0.06, 0.035)` and `flag_blue`
  `(0.08, 0.3, 0.82)` (#144DD1) are plain: base colour, roughness, specular and a little subsurface glow, no detail, variation, edge or
  speckles. Pole and brass knob unchanged. `FlagStandBuilder.BuilderVersion` 2, so both stands are rebuilt.
- `BattleSide.ColorOf`: west `(0.85, 0.12, 0.1)`, east `(0.15, 0.4, 0.9)`.
- Meshed in the main checkout, each variant as before: the two scouts and the 14 exploding pieces at 256 with 3 LODs (no suffix and
  `_256`), 512 with 3 LODs (`_512`), 128 with 1 LOD (`_fast`); both flags at 256 with 3 LODs. Triangle counts unchanged.
- `dotnet test SDFPipeline.Tests` passes.

Left (in Unity): Tools > SDF Mushrooms > Rebuild All (the `flag-blue` prefab, the showcase scene still names `flag-gold`), then Tools >
SDF Pickups > Rebuild Flag Stands if the stands did not rebuild by themselves, then rebuild the level scenes (Tools > SDF Level >
Rebuild); a look in Play mode.
