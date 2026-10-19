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

## Status

Worktree `.claude/worktrees/agent-ad74f8dae9c3ab193`, branch `worktree-agent-ad74f8dae9c3ab193`, on main's 0181038. Measured on the M4 Max
while the machine was shared with a long `sdf mesh` and three other agents, so the times are on a loaded machine.

- **Motion in Play mode** (done but not verified, 1): `SdfLevel.Editor.SkyMotionCheck.Run` (new, `Assets/Level/Editor/`).
  - It enters Play mode in batch mode (domain and scene reload off for the run, restored after) and holds the frame time at 1/60 s
    with `Time.captureDeltaTime`, so `SkySettings.Update` and the shaders' `_Time` run on simulated time. It fails on a short blink,
    crossing or parted lids, or a popping mote or spore. Run it without `-quit`: `-executeMethod SdfLevel.Editor.SkyMotionCheck.Run
    -snapshotDir <folder>`.
  - The meadow passes. In 70 s: 4 blinks (11.45, 32.08, 43.02, 54.27 s apart 11–21 s, inside 9–25), each 0.35 s, closing to 99.9–100 %
    at the fullest frame, never past 100 %.
  - The lids meet and do not cross: at the fullest frame their front edges are at +0.01 and −0.01 mm in the moon's own frame (the
    eye's middle), against +21.6 / −23.3 mm open. Both shells are cut 4 mm off the middle and rounded by 4 mm, so they meet exactly
    there; `lidCloseAngle` 45.8° is the parts' 0.8 rad. `moon_blink.png` shows every frame of a blink: the lids close to a line and
    open again.
  - The iris wanders up to 12.1° (`turnAmount` 12), at most 0.013° a frame: smooth. The floaters move at most 0.028° of the sky a
    frame (1.7°/s), no jumps.
  - Far-layer culling: the moon is drawn (3 of 3 renderers, the same brightness) seen from the spawn point, from (60, 2, 40) and from
    (−40, 50, −300), far outside the far layer's 100 m round the origin.
  - Pops, each set drawn alone on black at 1080p every frame, each pixel against the brightest within 12 px (40 px walking) in the
    frame before, the frame's edge left out: motes for 15 s standing, largest jump 0.01; walking at 4 m/s for 10 s, 0.03; abyss
    spores for 30 s from the island's edge, 0.00. None by more than a third. At 480 × 270 the same check caught sub-pixel motes
    twinkling (jumps up to 0.64), so it does see small changes.
- **`sky_tower`** (known problem 1): stands on the cap of the large tower at the oval's +x end (joint 4), found by a ray down onto
  its collider (about 15.8 m up), plus eye height, looking out of the island's end. It looks over the keep's flanking tower cap and
  the crest to the sky; the sky draws 4 times, 2.6k triangles.
- **Showcase** (known problems 2, 3): `MushroomSnapshot.Capture` runs on main's `Mushrooms.unity` (11 removed `Zztest`), 63 pictures.
  The new references are in `.claude/jobs/10-sky/showcase/` of this worktree (not committed).
- **Web frame time** (not done 1, 2): `WebBuild.Build` takes `-scene Assets/Scenes/Meadow.unity` (repeatable) to build one level
  (19 MB, 284 s). `SkySettings` has two hooks for a page to call through `unityInstance.SendMessage("Sky", …)`: `SetObjectsActive`
  (0 turns off the moon, floaters, spores and motes; the skybox and tone stay) and `FaceMoon` (the main camera towards the moon).
  - A page next to the build (`measure.html`, the template's with the instance kept in `window` and a script) alternates the sky on
    and off three times for 8 s each, from the spawn view and then facing the moon, after 10 s warm-up. Per Unity frame it times the
    rAF interval, the rAF callback on the CPU and, where `EXT_disjoint_timer_query_webgl2` exists, a GPU time query round it. It
    POSTs the results to the local server. The browser extension was not connected, so the pages were opened with `open -a`.
  - The display runs at 60 Hz: every interval is 16.7 ms median (p95 18), with and without the sky, in both browsers.

    | Browser, canvas (pixels) | View | CPU per frame, sky on / off (median, ms) | GPU query, sky on / off (median, ms) |
    |---|---|---|---|
    | Chrome 155, 1920 × 1200 | spawn | 1.3 / 1.4 | 2.6 / 2.6 |
    | | moon | 1.5 / 2.1 | 3.2 / 4.0 |
    | Chrome 155, 3200 × 1800 | spawn | 1.3 / 1.4 | 4.0 / 3.8 |
    | | moon | 1.3 / 2.2 | 3.5 / 3.2 |
    | Safari 26.6, 1920 × 1200 | spawn | 9 / 9 | (no timer query) |
    | | moon | 9 / 10 | |

  - The sky's cost is below the noise: the medians move by less than between rounds, and its p95 GPU times (8–16 ms) are the shared
    machine. Chrome's GPU query on ANGLE/Metal hardly grows with 2.5 times the pixels, so read it as an order of magnitude, not a
    GPU time. Safari's `performance.now()` is in whole milliseconds; its 9 ms is mostly WebGL submitting, sky or not. Both stay
    inside 16.7 ms with room.
  - The project's build target was left on WebGL in this worktree's Library only; nothing in ProjectSettings changed.
- **`dusk` preset** (not done 3): `content/skies/dusk.sky`: the sun at 9° in the west (warm orange light, long shadows), a lilac
  shadow band, the gradient from violet to rose to an orange horizon, a few stars above 35°, a warm fade, a dark abyss with a faint
  violet glow, 3 floaters and fewer, amber spores; no moon. A test reads all three presets (`ContentPresets_Read`). Seen on a
  throwaway copy of the meadow (`sky preset=dusk`, removed after) and in `sdf render --sky dusk`; the IDE's Sky button picks it up.
- **`UNITY_MATRIX_P`** (known problem 5): no other shader reads its elements. The only other projection use is
  `SdfToonOutline.shader`'s whole `UNITY_MATRIX_VP` on the normal, which draws (the outlines show). Nothing to change.
- **The eye moon's LODs** (known problem 4): not changed. Looking at the moon the sky draws 7 times and 12.8k triangles on High, inside
  the 15k; it is left until 05 needs the room.
- `dotnet build` clean; `dotnet test`: 533 and 320 pass. One run under load had a pipeline test fail once and pass on two reruns;
  which one was not caught.

Left for a hand check:

- Watching the moon blink and the floaters drift in the Game view: the check above measures and pictures them, but not in real time.
- Far-layer culling in a standalone Mac player (the check covers Play mode and the WebGL build showed the night).
- Stars on the 960 px canvas when turning (done but not verified 4) and the moon and its lids in the web build: no screenshots could be
  taken of the browsers here. The moon views of the frame-time pages are a place to look.
- Low-moon shadows on the web preset (done but not verified 5): left to 05.
- Tuning `dusk` by eye in the editor.
