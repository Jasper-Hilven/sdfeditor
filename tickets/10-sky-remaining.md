# 10 — Sky: what is left of 07

Ticket 07 (sky presets, Eye-Moon Night, sky-lit toon) is implemented and merged with main. This ticket holds what is left: work not
done, work done but not verified, and known problems. See 07's `## Status` for what was built and measured.

Depends on: 07 (done). Can run alongside 04 and 05.

## Not done

1. **Web frame time with and without the sky** (07's acceptance).
   - Headless Chrome was vsync-capped at 16.7 ms with the sky, which says nothing.
   - Measure in desktop Chrome on the web preset, with the level as built and with `Level/Sky` disabled (or `sky preset=day`).
2. **Safari**: the WebGL build was only run in Chrome.
3. **`dusk` preset**: left for later, as 07 planned. The format allows it.

## Done but not verified

1. **The sky's motion, in Play mode.** `SkySettings.Update` only runs while playing, so batch snapshots never exercised it. Check in
   the editor:
   - the iris wander (Perlin noise, `turnAmount` 12°, `turnPeriod` 40 s);
   - the blinks: lids turned by `lidCloseAngle` 45.8°, every 9–25 s, 0.35 s long. Check the lids meet and do not cross;
   - the floaters' drift and bob;
   - that the motes wrap round the player without popping, and the abyss spores loop without a visible jump.
2. **Far-layer culling in the scene view and in builds.** `SkySettings` moves the bounds of the moon, the floaters and the motes to
   each camera in `RenderPipelineManager.beginCameraRendering`. Batch snapshots and the WebGL build showed them, but there was no
   hands-on check in the Game view or a standalone player.
3. **The far layer's depth slab on WebGL.**
   - The moon and floaters write depth into a slab just inside the far plane: 1e-4 of NDC with OpenGL's 24-bit depth, 1e-6 reversed.
   - The WebGL build showed the night sky, but the moon was not in view; check the moon and its lids there.
4. **Stars at other resolutions.** They were tuned at 1080p. On the 960 px web canvas they are small squares; check that they do not
   shimmer when turning.
5. **Shadows with the low moon on the web preset.** The moon is 28° up, so shadows are long, and the web preset's shadow distance is
   30 m. The cutoff was not examined; leave the trade-off to 05.

## Known problems

1. **`sky_tower` snapshot.** Its point, (13, 14, −8) in the castle's coordinates, was set for the old blockout castle. With 06's castle
   it looks out from inside a hill. Put it on one of the new castle's towers.
2. **`MushroomSnapshot.Capture` throws `Sequence contains no elements`.**
   - Main's `Mushrooms.unity` holds `Mushrooms/Zztest/settest`, a missing-prefab instance (guid d6752747005934768baf16c0d5518af3)
     with no renderers.
   - Delete it, or make the close-up loop skip instances without renderers.
   - It came with the merge from main, not from 07, and it blocks the showcase comparison after the merge.
3. **Showcase scene layout drift.** The saved row layout of `Mushrooms.unity` predates the current models. Any rebuild that
   re-lays it out moves an instance and re-frames the camera, which changes the showcase snapshots.
   - 07 adds the scene's `SkySettings` without a re-layout (`MushroomScene.EnsureSkySettings`).
   - The next real re-layout will still change the snapshots; take new references then.
4. **The eye moon's LODs.** Its rings are vertex colours, so the eye is drawn at LOD0 (8.5k triangles), since LOD1 smears them.
   - Together with the spores and a floater that is 13.7k of the 15k budget, looking towards the moon.
   - If 05 needs room: a finer LOD1 for the eye part only, or the iris as its own part.
5. **`UNITY_MATRIX_P[1][1]` came out NaN** in `SdfSpores.shader` on this URP version, so the spore size is measured in clip space
   instead. Other shaders that use `UNITY_MATRIX_P` elements should do the same.

## Merge notes (main into sky, 88f0782)

- **`LevelFile` / `LevelDefinition`**: both 03's `scatter` and `draw` sections and `sky`.
- **`LevelBaker.Bake(..., modelExists, sky)`**: `sky` is now the last parameter, so tests pass it by name.
- **`level.json`**: `"sky"` comes before `"models"`, which lists the placed, scattered and sky models.
- **`LevelBuilder`** is version 5 (main had 4). `LevelSky.Add` runs after `AddPlayer`.
- **`LevelSnapshot`**: each eye shot logs the foliage cost and the sky cost on one line; the sky views come after the castle shots;
  the web shots are main's, then the no-bloom moon views.
- **`meadow.level`**: scatter, draw foliage, draw castle, then `sky preset=night`.
- **Shaders**: 06's and 08's shaders do not call `SdfToonShade`. `SdfToon.shader`'s fragment keeps the `glow` argument and
  `SkyFade`. A later wall-pattern change to that fragment must keep both.
- After the merge: `dotnet test` passed (533 + 316); the meadow built and snapshotted with no errors, foliage and the new castle
  under the night sky.

## Acceptance

- Frame time with and without the sky on the web preset, in Chrome and Safari, recorded in Status.
- The moon blinks, its iris wanders and the floaters drift in Play mode, with no culling pops; the motes wrap without popping.
- The `sky_tower` snapshot looks out from a tower of the current castle.
- `MushroomSnapshot.Capture` runs, and the showcase has new references.
