# 07 — Sky: presets, Eye-Moon Night, sky-lit toon

A sky per level, chosen in the level file from text presets in the repo, and a first preset, **Eye-Moon Night**, for the meadow.
The sky lights the toon world: a global tint and brightness on the toon bands, and a fade toward the horizon colour with distance in
place of fog. All of this is mirrored in the IDE preview.

Depends on: 02 (done). See *Order and conflicts* for 03, 04 and 06.

## Why

- Today the sky is Unity's `Skybox/Procedural` with a brown "ground" below the horizon. From a castle tower or the island's edge,
  that brown reads as solid earth under a floating island.
- The toon shaders ignore the sky. `SdfToonShade` takes only the sun's direction and its hue (`_MainLightColor` divided by its
  brightest channel); intensity, ambient and the skybox are never read. The shadow band is a fixed blue-violet
  (`MushroomStyle.shadowTone`). Changing the sky changes the background and nothing else, and a night sky would leave every model lit
  as by day.
- Fog is off, so the crests and the sky meet at full contrast and nothing tells distance.
- The art direction (toon, subsurface glow, floating eyes) asks for a sky that belongs to the world, not a physical atmosphere.

## The look: Eye-Moon Night

- **Sky**:
  - deep indigo at the zenith through violet to a dusky lilac-teal glow just above the horizon;
  - the gradient cut into **cel bands with soft, anti-aliased edges** (4–5 bands);
  - round stars as soft toon dots, a few faintly tinted, with a slow twinkle.
- **The moon is a giant floating eye**, about 28° up, about 9° across, pale and faintly glowing.
  - Its iris turns slowly and it blinks now and then.
  - It **does not track the player**: the motif is subtle.
  - It is the main light: shadows fall away from it.
- **Below the island, a glowing abyss**:
  - the lilac horizon darkens downward into near-black violet;
  - a faint teal glow band lies deep below;
  - bioluminescent spores (teal and amber) rise from the dark past the island's edge.
- **In the play area**:
  - a sparse drift of glowing motes around the player;
  - the castles' glowing windows, lanterns and caps (06), and every material's subsurface edge glow, carry the scene;
  - the far hills fade into the violet haze.
- **Motion is ambient only**: iris turn, blinks, twinkle, spores and motes. Nothing reacts to the player.

### Defaults chosen here (override freely)

| Question | Default in this ticket |
|---|---|
| Eye motif | Subtle: slow iris turn plus an occasional blink. No tracking. |
| Motion | Ambient only (iris, blink, twinkle, spores, motes). |
| Sky style | Cel bands with soft anti-aliased edges. |
| Showcase (`Mushrooms.unity`) | Stays on the neutral **day** preset's toon values (identity) and its current procedural skybox, so the 00 snapshots stay valid. |
| Floating glowing caps | Three to five small glowing caps drifting in the middle distance, as far-layer objects. Optional per preset (`floaters`). |
| Presets shipped | `day` (neutral, identity toon values) and `night` (Eye-Moon Night). `dusk` is left for later; the format must allow it. |

## Readability at night

Gameplay must stay readable. The darkness is in the sky and the haze, not on the playfield.

- The toon light is a hue and a band, not an intensity, so "night" is made by:
  - the `brightness` scale;
  - the shadow-band tint.

  The lit band at night stays at **≥ 70 %** of its day value, with a cool lavender tint. The shadow band is lifted and tinted
  blue-violet, never near-black.
- Glow must read **without bloom**. The integrated-GPU / web preset may drop bloom (05).
  - Subsurface/edge glow is exempt from the night brightness scale (`glowKeep`), so glowing parts stay at their daytime strength
    against the darker surroundings.
  - Motes, spores and the moon draw a core plus a soft halo in their own fragment shader, so they read as glowing with bloom off.
- The distance fade starts beyond typical engagement range (≈ 25 m) and stays partial (≤ ≈ 55 %), so the far castle (≈ 120 m) still
  shows its silhouette and glowing windows.
- Measured targets on the eye-height snapshots (starting values; tune and record in Status):
  - the ground's shadow band at ≥ 40/255 sRGB luminance and its lit band at ≥ 90/255;
  - a model's lit side against the sky behind it differs by ≥ 30/255 in luminance.

## Current state

- `Unity/Assets/Mushrooms/Editor/MushroomScene.cs`:
  - `BuildLighting(scene)`, used by both `Mushrooms.unity` and `Meadow.unity` (via `LevelBuilder`), makes:
    - the sun: directional, 1.5, 5600 K, warm filter, direction `SunDirection` (≈ 32° up);
    - the sky: `Skybox/Procedural` (`Assets/Mushrooms/Materials/Sky.mat`);
    - ambient: flat (0.17, 0.14, 0.10);
    - fog: off;
    - post-processing: the shared volume `Assets/Scenes/Mushrooms/Post Processing.asset` (ACES, bloom 0.175 at threshold 0, post
      exposure −0.26 EV).
  - `CreateCamera`: the level camera has its far plane at 400 m and a 60° vertical field of view.
- `Unity/Assets/Mushrooms/Shaders/SdfToon.hlsl`: `SdfToonShade` (bands, highlight, rim). It is used by:
  - `SdfToon.shader`;
  - `Unity/Assets/Level/Shaders/SdfGround.shader`, which passes rim 0 and occlusion 1.
- The edge (subsurface) glow is `UV6`: `SdfToonColor` lerps the albedo toward the edge colour by `UV6.a`, before shading. So the glow
  is darkened like any other colour.
- `SdfToonOutline.shader`: the surface colour × `_OutlineTone`, unlit.
- `MushroomStyle.cs`: the band tones (shadow tone gamma 0.55/0.5/0.66) as material properties.
- `IDE/wwwroot/js/sdf-renderer.js`:
  - the toon branch (`toonColor`, `toonShade`, `toonPixel`) has the same constants as `MushroomStyle`;
  - `background(rd)` is a fixed grey gradient;
  - `sdf render --style toon` uses the same toon branch.
- `SDFPipeline/Levels/LevelFile.cs`: sections `play, seed, hills, edge, regions, place`. Unknown sections are rejected.
- `level.json` is written by `LevelBaker`. Unity mirrors x (pipeline → Unity: `-x`, yaw flips sign).
- `LevelSnapshot.cs`: eye-height views in four directions, the corner, above, castle and region shots, and the web preset.
- Sightlines that place the sky's content:
  - from the centre of the play area, the crests (8–12 m) are ≈ 6° above eye level; from the edge of the play area, ≈ 17–20°;
  - looking level, the view reaches 30° up;
  - so the sky seen in play is the band **≈ 20–30°** and above. The horizon is never seen from the ground; it is seen from a tower
    (≈ 14 m) and at the island's edge (only reachable for snapshots).

## Sky presets: `content/skies/*.sky`

Text files in the style of `.sdf` and `.level` (`format 1`, comments, the same tokenizer, `name=value` parameters, indented
sub-lines). Directions and colours are in pipeline coordinates; colours are linear. Sketch for `night.sky`:

```
# Eye-Moon Night: an indigo sky banded like a cel drawing, a giant eye for a moon, and a glowing abyss under the island
format 1

light azimuth=140 elevation=28 color=(0.78, 0.8, 1.0)      # the moon is the main light; its direction is also where the eye hangs
tone brightness=0.75 shadowTint=(0.8, 0.85, 1.1) glowKeep=1
                                                             # multipliers on the toon bands; glow exempt from brightness by glowKeep

gradient softness=0.6                                        # band edges, in degrees
    band from=60 color=(0.035, 0.03, 0.11)                   # each band from an elevation (degrees) up to the next one
    band from=32 color=(0.07, 0.05, 0.19)
    band from=14 color=(0.14, 0.09, 0.27)
    band from=4 color=(0.24, 0.17, 0.36)
    band from=-6 color=(0.26, 0.28, 0.38)                    # the horizon glow
abyss
    band from=-6 color=(0.12, 0.08, 0.2)
    band from=-30 color=(0.03, 0.02, 0.06)
    glow elevation=-55 width=12 color=(0.05, 0.25, 0.28)     # the faint teal band deep below
stars density=0.6 size=1.6 twinkle=0.3 tint=0.25 minElevation=8

fade start=25 end=180 amount=0.55                            # toward the horizon colour behind each pixel, in place of fog

moon model=eye-moon size=9 glow=0.6                          # far-layer model, angular size in degrees, placed at the light
    turn amount=12 period=40                                 # iris wander, degrees and seconds (smooth noise, not tracking)
    blink every=(9, 25) duration=0.35                        # seconds between blinks, randomised in that range

floaters model=floating-cap0 count=4 distance=(250, 400) elevation=(-10, 18) drift=0.6 bob=1.5

spores
    abyss count=600 color=(0.3, 0.95, 0.85) altColor=(1.0, 0.7, 0.3) rise=1.2 size=0.25 from=-80 to=20
    near count=250 radius=35 color=(0.55, 0.95, 0.75) size=0.06 drift=0.3

post exposure=-0.1 bloom=0.25
```

- `day.sky` is the neutral preset. Its tone is identity (`brightness=1 shadowTint=(1,1,1) glowKeep=0`), with no fade, moon, stars
  or spores, and a plain day gradient. With it, `SdfToonShade` must produce **bit-identical** output to today.
- A preset is complete: every key has a value. The reader rejects unknown or repeated keys with line numbers, like `LevelFile`.

### Level file: `sky` section

```
sky preset=night                         # content/skies/night.sky
    fade end=200                         # any preset line, given again, overrides those keys of that line
    moon size=10
```

- Overrides merge **per key**, on the line of the same name (and the same name and index for repeated lines such as `band`).
  Replacing a whole list (all bands) is done by giving `gradient replace=true` with its own bands.
- No `sky` section means `preset=day`.
- `sdf level` resolves the preset plus the overrides and writes a fully resolved `"sky"` object into `level.json` (no preset names).
  It adds the far-layer models (`eye-moon`, `floating-cap0`) to `"models"`, so the level rebuilds when they change. Editing a `.sky`
  file must re-bake the levels that use it, so the bake hashes it.
- The spores' and floaters' layouts are seeded from the level's `seed`, so the bake stays deterministic.

## Work

### Pipeline

1. **`SDFPipeline/Skies/SkyFile.cs`**:
   - a reader for `.sky` and for the level's `sky` section overrides (same grammar);
   - records in `SkyDefinition.cs`;
   - merge rules as above.

   Add `sky` to `LevelFile.Sections`. `LevelBaker` writes `"sky"` to level.json. Tests cover: reading, every error with its line,
   merge per key and per indexed line, `replace`, the default `day`, and a byte-identical bake.
2. **Sky models**:
   - `content/models/sky/eye-moon.sdf`:
     - `Part part=eye` plus an upper and a lower lid as separate parts, each its own mesh (`PartSplitter`), so Unity can turn them
       to blink;
     - pale, glowing materials (`materials/sky/`);
     - `collider none`; no outline (06's per-part `outline off`, or the far-layer shader simply has none).
   - `content/models/sky/floating-cap0.sdf`: a small glowing cap with a trailing veil.
   - **Exclude the `sky` group** from the showcase rows (`MushroomBuilder.SceneModels` / `MushroomScene`).
3. **`sdf render --sky <preset>`** and the IDE preview (see *IDE preview*): render a model under a preset's tone, gradient and
   background, so models can be checked at night without Unity.

### Unity shaders

4. **`Assets/Mushrooms/Shaders/SdfSky.hlsl`**, shared by every shader that needs the sky:
   - globals set from the resolved sky:
     - `_SkyBandColors[8]` and `_SkyBandFrom[8]` (elevation, sin) and the band count, plus the abyss bands and glow;
     - `_SkyFade` (start, end, amount);
     - `_SkyTone` (brightness, glowKeep) and `_SkyShadowTint`.
   - `SkyGradient(float3 dir)`: the banded colour, edges anti-aliased with `fwidth`.
   - `SkyFade(float3 color, float3 positionWS)`: lerps toward `SkyGradient` of the view direction, with the elevation clamped to
     ≥ −2° so the ground fades toward the band behind it, not into the abyss.
   - Arrays as `float4` globals (`Shader.SetGlobalVectorArray`), which WebGL 2 supports. Nothing needs compute.
5. **`SdfToonShade`** (`SdfToon.hlsl`):
   - the shadow tone is multiplied by `_SkyShadowTint`;
   - the shaded colour is multiplied by `_SkyTone.x`;
   - a new `glow` input (0–1, the `UV6.a` the albedo used) mixes the result back toward its unscaled value by `glow * _SkyTone.y`,
     so glowing edges keep their strength at night.

   With identity values the output must be bit-identical to today: keep the operations ordered so `x * 1` is exact.
   - `SdfToon.shader`: pass the glow, then apply `SkyFade` to the final colour.
   - `SdfGround.shader`: glow 0, then `SkyFade`.
   - `SdfToonOutline.shader`: `SkyFade` on the ink colour, so far lines melt into the haze with their surface.
   - 03's foliage and 04's underside use these shaders and inherit all of it.
6. **`Assets/Mushrooms/Shaders/SdfSky.shader`**:
   - a skybox shader (target 3.5): `SkyGradient`, the abyss bands and glow, and procedural stars;
   - stars are hashed on a jittered grid of the direction (cube-face cells): round, at least 1.5 px wide via `fwidth` so they don't
     shimmer, twinkling on `_Time`, none below `minElevation`;
   - no textures.
7. **Far layer**: `SdfToonFar.shader`, or a `_FAR_LAYER` keyword on `SdfToon`, for the moon and floaters.
   - The vertex stage drops the camera's position (the object is placed at `_WorldSpaceCameraPos` + direction × a fixed radius)
     and writes depth at the far plane (`z = w × (1 − ε)`), so it is infinitely far and always behind the level. The camera's
     far plane stays at 400 m (WebGL has no reversed Z).
   - No shadow caster; no outline; no `SkyFade`.
   - The light is the view direction, so the moon is lit full-face, plus `glow`.
   - Queue after the skybox (URP draws it after opaques), ZTest LEqual, ZWrite off. Sort by distance via queue offsets; there are
     few objects. Check this ordering on WebGL.
8. **Spores and motes**: `SdfSpores.shader` plus a mesh of quads built in code by the builder from the seeded layout in level.json
   (one mesh per spore set, so one draw each).
   - Billboards are expanded in the vertex shader and animated on `_Time`:
     - abyss spores rise in a ring around the island's edge outline from `from` to `to` and wrap;
     - near motes drift in a box that wraps around the camera (`frac` of the position relative to the camera, as in a repeating
       volume), so they are always around the player at no CPU cost.
   - Additive blending, a core plus a soft halo drawn in the fragment shader (it reads without bloom), and HDR values above 1 to
     catch bloom on High.
   - Size limited in pixels, to keep overdraw low.
   - No particle system, no compute.

### Unity scene

9. **`SkySettings`** (runtime, `Assets/Mushrooms/Runtime/`, `[ExecuteAlways]`):
   - holds the resolved sky and sets every global in `OnEnable` and on validate (shader globals are not saved with scenes, so a
     component must set them in builds too);
   - drives the moon: iris wander by smooth noise on the eye's rotation; blinks by turning the lid parts, with seeded random
     intervals;
   - drives the floaters' drift and bob.

   It goes in every scene; the showcase gets the identity (day) tone values.
10. **`MushroomScene.BuildLighting`** takes an optional resolved sky.
    - **Without one (the showcase)**: today's sun, `Sky.mat`, ambient and volume, unchanged, plus a `SkySettings` with day values.
      `Mushrooms.unity` must render as in the 00 snapshots.
    - **With one (levels)**:
      - the main light from `light` (direction mirrored in x for Unity, intensity a fixed 1.5: the toon shaders read hue only);
      - an `SdfSky` skybox material;
      - `RenderSettings.ambientLight` from the shadow tint, for any URP Lit leftovers;
      - fog off;
      - a per-level volume profile `Assets/Scenes/<Level>/Post Processing.asset` with the sky's `post` exposure and bloom (ACES kept).
11. **`LevelBuilder`** (raise its version) reads `"sky"` from level.json:
    - calls `BuildLighting` with it;
    - places the moon and floaters as far-layer objects under `Level/Sky`, not static, since they animate;
    - builds the spore meshes.

    Sky objects cast no shadows and have no colliders.

### IDE preview (`IDE/wwwroot/js/sdf-renderer.js`)

12. In the toon branch:
    - uniforms `uSkyShadowTint`, `uSkyTone` (brightness, glowKeep), `uSkyFade` and the band arrays, defaulting to identity;
    - `toonShade` applies them exactly as `SdfToonShade` does (pass the thinness as the glow);
    - `toonPixel` applies the fade and the outline fade;
    - `background(rd)` uses the sky gradient when a sky is set.

    The IDE gets a sky choice (neutral / day / night) that loads the resolved preset from the server. Its default stays neutral, so
    existing previews and `sdf render` output are unchanged. Keep the "keep in step" comments on both sides.

### Verification

13. **`LevelSnapshot`**, on both presets (and the web preset once with bloom off):
    - the eye-height views (four directions, corner), plus one looking up at the moon;
    - **a tower view**: on top of a `castle0` tower, looking out over the crest to the edge and the abyss;
    - **the island's edge**: standing at the edge margin looking out and down;
    - **an outside overview** of the island hanging over the abyss.

    Log the sky's draw calls and triangles per view.
14. `MushroomSnapshot` of the showcase, compared with the 00 references.

## Acceptance

- `content/levels/meadow.level` has `sky preset=night`. `sdf level` resolves it into level.json and is byte-identical across runs;
  editing `night.sky` re-bakes the meadow.
- Reader errors (unknown preset, key, line; bad values) name the file and line.
- `Meadow.unity` shows Eye-Moon Night:
  - banded gradient without visible stepping or aliasing at the band edges;
  - non-shimmering stars;
  - the eye moon at ≈ 28° turning its iris and blinking, visible above the crests from anywhere in the play area;
  - the abyss with rising spores from the tower and the edge;
  - motes around the player.
- Shadows fall away from the moon; the light's direction and the moon's position agree.
- Models, ground (and 03's foliage once it lands) take the night tint and brightness; glowing edges and lanterns keep their strength;
  far hills and castles fade toward the horizon colour, outlines with them; sky objects do not fade.
- The readability targets above are met on High and on the web preset with bloom off.
- `Mushrooms.unity` matches the 00 snapshots (within 1 sRGB level per channel); with the `day` preset the toon output is
  bit-identical to before.
- The IDE preview and `sdf render --sky night` show the same tint, glow and fade as Unity for the same model (side-by-side check).
  With no sky chosen, they are unchanged.
- Cost:
  - the sky adds ≤ 8 draw calls and ≤ 15k triangles per frame;
  - no compute shaders, no textures beyond the existing ones;
  - the WebGL build runs in Chrome and Safari with no new shader errors;
  - report frame time with and without the sky on the web preset.

## Order and conflicts

- **03 (foliage)** can run in parallel. Both edit `SdfToon.shader` and `SdfToonOutline.shader`, but 03 edits the vertex stage
  (wind) and 07 the fragment, plus `SdfToon.hlsl`. Both also add a section to `LevelFile.Sections` and code to `LevelBuilder`. Expect
  small merges; land them one after the other, not as simultaneous edits of the same files.
- **06 (castles)** conflicts: its step 5 rewrites the fragment of `SdfToon.shader` (patterns and their own distance fade).
  - Do 07's shader steps 4–5 **before** 06's step 5, as one small change, so 06 builds on the new `SdfToonShade` signature and the
    `SkyFade` call; or wait until 06's step 5 has landed.
  - 06's glowing windows and lanterns rely on `glowKeep` to read at night.
  - 06's per-part `outline off` is handy for the moon but not required.
- **04 (hills, island)**: its Sky section now points here. 07 does not need 04, but 04's underside should be checked under the night
  sky, so 07 preferably lands first. The abyss spores ring the edge outline from 01; 04's underside sits in front of them.
- **05 (tuning)** includes the sky in the web-preset budget and decides whether bloom stays on the low preset.

## Notes

- Directions in `.sky` and level.json are in pipeline coordinates. Unity mirrors x, so azimuths flip sign, as for placements.
- The moon is a far-layer object, so its angular size is fixed whatever the camera's position. The eye part is 12 cm; the far
  shader's scale works out to `size` degrees.
- A low moon (28°) gives long shadows. The web preset's 30 m shadow distance will show the cutoff more than a high sun did; check
  it in the snapshots and leave the trade-off to 05.
- ACES shifts saturated blues and violets. Tune the preset's colours on the rendered snapshots, not by their numbers. The bloom
  threshold is 0, so a bright horizon band blooms into the crest silhouettes: keep the horizon dim at night.
- The day preset's sky gradient is not shown in the showcase, which keeps the procedural skybox for the 00 comparisons. It is used
  by any level that picks `day`.
- If the band arrays cost too much on WebGL (uniform count), fix the band count at 6 and pad.

## Status

Done (not committed). Worktree `.claude/worktrees/sky`, branch `sky`, on 3bd0d2b.

- **Presets**: `content/skies/day.sky` (identity tone, the showcase's sun at azimuth 38.7°/32°, a soft blue gradient, its exposure and
  bloom) and `night.sky` (Eye-Moon Night, as sketched; colours tuned darker on the snapshots, horizon kept dim, abyss spores 0.6 m).
  - Reader `SDFPipeline/Skies/SkyFile.cs`, records `SkyDefinition.cs`.
  - `light`, `tone`, `gradient`, `post` are required; the other lines are optional; a given line is complete.
  - Unknown or repeated lines, entries and keys are rejected with their line.
  - Level `sky` section (`LevelFile.Sections` + `LevelDefinition.Sky`): `preset=`, plus override lines merged per key, and entries by
    name and index; `replace=true` drops the preset's entries; a line the preset lacks is added and must be complete. No section means
    `day`.
  - An error in the level names its line. One in the preset names the level's sky line plus `content/skies/<preset>.sky:<line>:`.
- **Choices beyond the sketch**:
  - abyss bands run from their `from` *down* to the next;
  - the gradient's lowest band reaches down to where the abyss begins;
  - gradient plus abyss bands are at most 8;
  - `floaters` has a `scale` (how many times the model's size it looks from its distance; 8 for the 1 m cap).
- **Bake**: `LevelBaker` writes `"sky"`, with these parts:
  - the resolved lines;
  - `bands`, the combined list the shaders take;
  - the light's direction;
  - a 16-hex `hash` of the preset file;
  - seeded layouts: floaters, 600 abyss spores on the island outline 1.5–30 m out, 250 motes, and star and moon seeds.

  `SkyDocument.cs` and `SkyLayout.cs` hold this. Point lists are one JSON line per point (`InlineNumbers`). The sky's models go into
  `"models"`. A missing sky model is an error on the sky line. The CLI reports `sky night`.
- **Models**:
  - `content/models/sky/eye-moon.sdf`: `Part part=eye` plus parts `moon_lid_upper` / `moon_lid_lower` (content/parts). The lids are
    shell halves, turned 0.8 rad open about the eye's centre.
  - `floating-cap0.sdf`: a cap with a flared veil.
  - Materials in `materials/sky/`; both models are `collider none`.
  - Meshed into `Unity/Assets/Mushrooms/Generated/sky` at 512 cells, 3 LODs. The eye moon is 8.5k + 3k + 3k triangles at LOD0, and
    2.4k + 0.8k + 0.8k at LOD1; the cap is 12k / 2.7k / 0.9k.
  - The `sky` group is left out of the showcase (`MushroomBuilder.SceneModels`).
- **`sdf render --sky <preset>`** (implies toon) and the IDE (`sdf-renderer.js`):
  - New uniforms `uSkyTone`, `uSkyShadowTint`, `uSkyLight`, `uSkyFade`, the band arrays, the abyss glow and `uExposure` (0.6 × 2^(post
    exposure + 0.26)).
  - `toonShade` takes the thinness as glow; `toonPixel` fades the surface and the ink, in the perspective view only, since the axis
    views are diagrams.
  - The background is the tone-mapped gradient.
  - IDE: a "Sky: neutral / day / night" button (presets embedded, `BundledContent.Skies`); a sky switches to the toon look.
  - Without a sky the output is unchanged: `sdf render --style toon` of amanita-scout is pixel-identical to before.
- **Shaders**:
  - `SdfSky.hlsl`: the globals, plus `SkyShadowTint`, `SkyTone`, `SkyGradient` (fwidth-AA band edges and the abyss glow) and
    `SkyFade` (elevation clamped at −2°).
    - Unset globals (zero) read as the identity, through a set flag in `_SkyTone.z`, so prefab previews and old scenes are unchanged.
    - The amount-0 fade returns early, so the identity is exact.
  - `SdfToonShade` takes a `glow` input, multiplies the shadow tone by the tint and ends with `SkyTone`.
  - `SdfToon.shader`: passes `UV6.a` as the glow and returns `SkyFade`.
  - `SdfGround.shader`: glow 0, then `SkyFade`.
  - `SdfToonOutline.shader`: a `positionWS` varying, and `SkyFade` on the ink. The include is in its forward pass.
  - `SdfSky.shader` (skybox):
    - the gradient;
    - stars on 24 cells per cube face, at least 1.5 px, twinkling;
    - a soft halo round the moon, so it reads without bloom.
  - `SdfToonFar.shader`:
    - camera-relative;
    - lit from the eye, × (1 + `_FarGlow`);
    - no shadow, outline or fade.
    - Its depth is squeezed into a thin slab just inside the far plane rather than written at the far plane with ZWrite off: with no
      depth, the lids' inner faces behind the eye drew over it.
  - `SdfSpores.shader`:
    - abyss rise, or a camera-wrapped box;
    - a core plus halo, additive, HDR;
    - 1.5 px to `_SporeMaxPixels`.

    Its pixel size is measured in clip space, because `UNITY_MATRIX_P[1][1]` came out NaN here and drew nothing.
- **Unity**:
  - `SkySettings` (`Assets/Mushrooms/Runtime/`, ExecuteAlways):
    - sets the globals on enable and validate;
    - in play mode, turns the iris by Perlin noise, blinks the lids at seeded intervals, and drifts and bobs the floaters;
    - before each camera culls, moves the far layer's and the motes' bounds to that camera.
  - `MushroomScene.BuildLighting(scene, sky)`:
    - without a sky, as before, plus a `SkySettings`;
    - with one: the light (direction mirrored, colour sRGB of the sky's linear, 1.5), the skybox, the ambient from the tint, fog off,
      and a per-level `Assets/Scenes/<Level>/Post Processing.asset` (ACES, bloom threshold 0, the sky's bloom and exposure).
  - The existing showcase gets its `SkySettings` added without a re-layout (`EnsureSkySettings`). Re-laying it out would have moved one
    instance and the camera, because the saved layout predates current models.
  - `LevelSky.cs` (new; LevelBuilder version 3, one call each for lighting and `Level/Sky`) builds `Level/Sky`:
    - the `SkySettings`;
    - the moon at the light, 9° across the eye, eye at LOD0 and lids at LOD1;
    - 4 floaters at their coarsest LOD;
    - the two spore meshes (`Assets/Level/Meshes/<level> Sky/`);
    - materials `Assets/Level/Materials/<level> Sky *.mat` and `Assets/Scenes/<Level>/Sky.mat`.

Verified:

- `dotnet build` is clean. `dotnet test`: 250 pipeline tests and 533 project tests pass. 33 of the pipeline tests are new: reading,
  errors with lines, per-key and per-index merges, `replace`, an added line, the default `day`, a preset error naming file and line,
  bands, the spore layout, and a byte-identical bake with the sky.
- `sdf level` with `sky preset=night` is byte-identical across runs. The generated `level.json` equals a fresh bake. Editing
  `night.sky` changes `level.json` (resolved values and hash), so the watcher rebuilds the meadow.
- Batch builds (`BuildAllInBatch`, both presets) and snapshots have no `error CS` and no shader errors.
- Sky cost per view on High (logged by `LevelSnapshot`):
  - draws: 3–8, skybox included;
  - triangles: 1.7k–13.7k. The most is 8 draws and 13,722 triangles, looking towards the moon with a floater in view.
- No textures, no compute.
- Readability, as sRGB luminance on the 1080p eye views:

  | | Night | Day | Target |
  |---|---|---|---|
  | Grass, lit band | 108.5 | 124.2 | ≥ 90, night ≥ 70 % of day (87 %) |
  | Grass, cast-shadow band | 71 | 84 | ≥ 40 |
  | Castle's lit wall − sky behind it | 127.6 − 53.3 = 74 | (sky brighter by 45) | ≥ 30 |
  | Web preset, bloom off: grass / wall vs sky | 100–108 / 127 vs 52 | | |

  Glowing edges keep their strength (`glowKeep=1`). Far hills and castles fade into the violet haze, their outlines with them.
- `Mushrooms.unity` is identical, pixel for pixel, to the snapshots taken before the showcase got its `SkySettings`. Against the project
  with every Unity change of this ticket stashed, it differs by at most 1 sRGB level in a handful of pixels (amanita, scene). The very
  first baseline differs at 11 pixels of one fruit-tree1 trunk edge (up to 9 levels), but that difference reproduces with every change
  stashed, so it is not from this ticket.
- Web:
  - A WebGL 2 build of `Meadow.unity` alone (temporary script, removed) succeeds: 17 MB, no shader errors.
  - Headless Chrome (ANGLE/Metal) shows the night sky, stars, bands and tint. The console shows only the three URP shaders known from
    ticket 00 to be unsupported on WebGL.
  - The project was switched back to macOS.
- IDE: the Sky button cycles neutral → day → night in headless Chrome and lights the preview. `sdf render --sky night` of amanita-scout
  shows the cool tint, lifted shadow band, banded background and glowing eyes as in Unity's `eye_models`.

Snapshots (`.claude/jobs/c6233055/tmp/sky/`):

- `night_final/`, `day_final/`: eye_±x, eye_±z, eye_corner, eye_moon, sky_tower, sky_edge, sky_edge_level, sky_outside, eye_models,
  the web views, and on night eye_moon_web_nobloom and eye_-x_web_nobloom;
- `showcase_final/`;
- `web_meadow.png`;
- `ide_night.png`, `amanita_night.png`.

Not done / to check by hand:

- Frame time with and without the sky on the web preset. Headless Chrome is vsync-capped (16.7 ms with the sky), and there was no
  sky-off build to compare. Safari was not tried.
- The moon's iris turn, its blinks and the floaters' drift run only in Play mode (`SkySettings.Update`), which batch mode does not
  enter, so they are untested. Check the motion and the blink's lid angle (`lidCloseAngle` 45.8°) in the editor.
- The low moon's long shadows against the web preset's 30 m shadow distance: not examined. Left to 05.
- Star density and size were tuned at 1080p only. On the 960 px web canvas, stars are small squares.

Merging:

- **03 (main tree)**: its changes are the vertex stage of `SdfToon.shader` / `SdfToonOutline.shader` and `SdfScatter.hlsl` in
  HLSLINCLUDE. Ours are the fragment and `SdfToon.hlsl`, plus a `positionWS` varying and a forward-pass include in the outline, so the
  shaders should merge cleanly.
  Expect small textual conflicts in:
  - `LevelFile.cs`: the doc example, the section constants, the `Sections` array, the locals, the switch cases, and the
    `LevelDefinition` initializer;
  - `LevelDefinition.cs`: properties added after `Placements`;
  - `LevelBaker.cs`: the `Bake` signature, the document's new keys after `placements`, and the `models` line;
  - `Program.cs` `BakeOne`;
  - `LevelBuilder.cs`: the class doc list, and `BuilderVersion`, which both set to 3 (make it 4);
  - `LevelSnapshot.cs`: the usings, and the shots added after `ShootCastleAndRegions`;
  - the end of `content/levels/meadow.level`.

  After merging, re-bake (`level.json` and `Meadow.unity` are generated).
- **06**: step 5 rewrites the `SdfToon.shader` fragment. It should keep the `SdfToonShade(..., glow)` call with `saturate(input.uv6.a)`
  and `return float4(SkyFade(color, input.positionWS), 1.0)`. `MushroomBuilder` changes are small (`SceneModels` filter,
  `EnsureSkySettings` call in `BuildStale`).

Merged with main (03, 06, GUI kit, 08, 09): conflicts resolved in `LevelFile`, `LevelDefinition`, `LevelBaker` (`Bake(..., modelExists, sky)`),
`Program.cs`, `SdfRenderer`, `LevelBuilder` (version 5), `LevelSnapshot` (foliage cost and sky cost on one line) and `meadow.level`
(scatter, draw and sky sections); level.json and `Meadow.unity` regenerated. After the merge:

- `dotnet test`: 533 project tests and 316 pipeline tests pass.
- The meadow builds and snapshots with no errors, foliage and the new castle under the night sky. The sky's cost is unchanged (at most
  8 draws and 13.7k triangles).
- The `sky_tower` shot's tower point (13, 14, −8) predates the new castle; move it onto one of its towers.
- `MushroomSnapshot.Capture` now throws on main's `Mushrooms.unity`: it holds a stray `Zztest/settest` object, a missing-prefab
  instance with no renderers. That object came with the merge from main, not from this ticket.
