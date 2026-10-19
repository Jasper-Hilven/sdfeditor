# 05 — Tuning

Make the level meet its budgets on both presets and in the web build, and make the numbers easy to check again.

Depends on: 00–04.

## Budgets (per frame, first-person camera anywhere in the play area)

| | High (M4 Max) | Integrated GPU / web |
|---|---|---|
| Triangles (incl. outlines, shadows) | ≈ 2M | ≈ 500k |
| Draw calls | ≤ 1,500 | ≤ 200 |
| Frame rate | 120 fps at native res | 60 fps at 1080p × 0.75 render scale |
| GPU memory | – | ≤ 300 MB |
| Web download | – | as small as practical; report it |

Rough split to aim for: ground < 100k, foliage ≤ 700k / 200k, trees ≤ 400k / 120k, castles and props the rest.

## Work

1. **Measure**: a debug overlay or editor window that shows triangles, draw calls, batches, and per-category counts (ground,
   foliage per ring, trees, castles) for the current view; a scripted fly-through (fixed camera path over the play area) that logs
   min/avg/max frame time and the counts on both presets.
2. **Integrated-GPU stand-in**: there is no integrated-GPU machine. Profile the low preset on the M4 Max with a frame-rate cap and
   in low-power mode, and in the web build (Chrome and Safari). Treat the web build at 1080p as the reference for min-spec.
3. **Tune** in this order, re-measuring each time:
   - LOD heights/distances and the foliage rings (03's table) and density;
   - outline: only where it matters (LOD0 of models, foliage within 6 m);
   - shadows: distance, cascades, which objects cast (foliage only within 6 m, underside never);
   - mesh budgets: `--resolution`/`Detail` of heavy models, LOD error growth;
   - SRP batcher/instancing compatibility of every material (one toon material makes this easy);
   - web: texture compression, mesh compression, code stripping, Brotli.
4. **Record** the results in this ticket's folder or a `docs/performance.md`: per preset, the fly-through numbers and the settings.

## Acceptance

- The fly-through meets the budget table on both presets; the web build runs at ≥ 60 fps at 1080p × 0.75 on the M4 Max with the
  low preset.
- No visible popping or shimmering introduced by the tuning.
- The measurement tools stay in the project so the budgets can be re-checked after art changes.
