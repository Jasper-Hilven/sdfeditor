# 61 — Smaller GUI icons, unused content out of the player

Part of **Minigun Monsters**, toward CrazyGames' 20 MB mobile homepage limit (bytes before the first `gameplayStart`). **The first play
stays on the Fungal level** (the user: the most impressive by far); the savings have to come from the download, not a lighter first level.

## Today (player 16.23 MB compressed, 21.2 MB uncompressed; from the build log's player list)

- `Assets/GUI/Icons/*.png`: 12 head icons and 8 weapon icons, about 125-260 KB each, about 3.5 MB uncompressed (made by `GuiIcons`).
- TextMesh Pro's defaults in the player: `LiberationSans SDF.asset` (1.0 MB), `LiberationSans.ttf` (0.34), `EmojiOne.png` (0.34),
  pulled in by TMP Settings (default font asset, fallbacks, default sprite asset) even though the GUI uses Fredoka and Lilita One.
- Other leftovers named by 59 part 1: `NavMeshSurface Icon.png` / `NavMeshModifier Icon.png` (0.34 MB each, gizmo icons from
  `com.unity.ai.navigation`, in a bundle), `Test Panel.prefab` and `Gallery.prefab` (developer GUI) in the shared bundles,
  `Lilita One SDF Atlas` (1 MB in a shared bundle) if unused, the music layer `music_lvl_02_orchestra_layer_01.ogg` in the player and
  in a bundle (1.3 MB twice), a `Default-Skybox-Cubemap` copy in each level bundle.

## Work

- **Icons**: find the largest size each icon is ever drawn at (HUD weapon slots, scoreboard, death screen, end card, touch buttons,
  at 1920x1080 and on touch) and import them at that size (max size, and compression: crunched or ASTC/ETC2/DXT for WebGL, whichever
  WebGL2 supports and looks clean; keep alpha edges clean, no visible artefacts). Or make `GuiIcons` render them smaller. Compare a
  snapshot of the scoreboard and the HUD before and after (`Media/icons-61/`).
- **TMP defaults**: if no text uses or falls back to LiberationSans or EmojiOne (check every TMP text in the built prefabs and runtime
  text such as `NameTags`, and the TMP Settings' fallback lists), point TMP Settings' default font at the GUI's own font and drop the
  defaults from the build (no Resources reference left). Check that no glyph turns into a box: snapshots of every screen.
- **Leftovers**: remove from the build what nothing in the game uses (developer prefabs, gizmo icons, unused atlas); make the music
  layer exist once (player or bundle); the skybox cubemap once (or not at all if every level's sky is its own). Say what each was.
- Nothing visible or audible may change beyond the icons' tiny resampling.
- Measure the player and the bundles before and after (CrazyGames build), and the Fungal first play before `gameplayStart`.

## Done when

- The player is smaller by the measured amount (table in the Status); every screen's snapshot shows the same text and icons.
- EditMode (including `ReadableTextTests`), the desktop and touch play tests and the CrazyGames build pass.

## Status

Done. CrazyGames build, Brotli (bytes of `Build/*.br` and `StreamingAssets/Bundles`); "before" is the build right after ticket 60's split.

| MiB | before | after | change |
|---|---|---|---|
| `Web.data.br` | 9.19 | 8.25 | -0.94 |
| `Web.wasm.br` | 7.04 | 7.04 | 0 |
| **Player** (data + wasm + framework + loader) | **16.34** | **15.39** | **-0.95** |
| Bundles (all) | 41.71 | 40.25 | -1.46 |
| Fungal's bundles (9: shared x7, `fungal-assets`, `fungal`) | 10.05 | 8.60 | -1.45 |
| **Player + Fungal's bundles (first play)** | **26.39** | **23.98** | **-2.41** |

Player build report: 21.2 MB uncompressed to 16.4 MB, "complete build size" 16.4 to 15.4 MB. Still over 20 MB for touch: ticket 62 and the leftovers below.

**Icons.** Stored at most 192 px on the longest side for heads and 256 px for weapons (they were 180-355 px; drawn at most about 115 px
(death avatar, 1080p) and 200 px wide (kill feed, death screen)), both sides a multiple of 4, with a 4 to 10 px transparent pad.
Cause of the bulk: the crops were any size (289x228, 214x236, ...), and DXT/ASTC cannot compress a side that is not a multiple of 4, so
Unity stored every icon as RGBA32 (258 KB for the pistol, 3.3 MB for the 19). Now **DXT5 with crunch, quality 80** (WebGL override and
default), no mipmaps: 4 to 10 KB each in the player (was 53 to 259 KB). WebGL2 has no per-device format in a single build (Unity 6's
"texture subtarget" variants would double the data); DXT5 crunched is transcoded at load on every device (a phone without S3TC gets
RGBA32 in memory for 19 small icons only), so it is the one choice that is small everywhere. `GuiIcons.Fit` (area-averaged, premultiplied
so the ink edge has no halo) does the resize and the padding in `Render`, `GuiIcons.Configure` the import settings, and
`Tools > SDF GUI > Fit Icons` applies both to the PNGs already there (no re-render). Snapshots before and after, 1920x1080 and touch
800x450, in `Media/icons-61/{before,after}` and `{before,after}-touch` (Scoreboard, Hud, Death, Victory): the same text and icons; the
ink edges are as clean.

**Removed from the build**
- TMP Essentials' defaults: `LiberationSans SDF` (1.0 MB), its `.ttf` (0.34), `EmojiOne.png` and sprite asset (0.34), the fallback and the
  outline/drop-shadow materials. They were in the player because they lived in `Resources/` (everything there ships) and TMP Settings
  pointed at them. Moved to `Assets/TextMesh Pro/Defaults/` (not Resources, so unreferenced and not built); TMP Settings' default font is now
  the GUI's `Fredoka SDF`, no default sprite asset, emoji support off. No text used or fell back to them (`GuiFactory` sets Fredoka or
  Lilita on every label, `NameTags` copies a Fredoka template); all snapshots show no box glyph; the headless build shows the same text.
- The orchestral music layer 1 (`music_lvl_02_orchestra_layer_01.ogg`, 0.9 MB) was in the player (the menu cue) and in
  `shared-53f5c110` (Meadow, Chessboard and Fungal's `ArenaAudio`). `ArenaAudioSetup.Attach` now leaves layer 1 empty on the orchestral
  arenas and `ArenaAudio.Resolve` takes it from the SoundBank's `music.menu` clip, so it exists once, in the player (and is now the same
  clip instance as the menu music, which `MusicDirector` was written to expect). The three scene files hold just that one reference
  change (`ArenaAudioSetup.RefreshScenes` rebuilds the component in place). `shared-53f5c110`: 3.57 to 2.67 MiB.

**Left, and why**
- Every level scene and the Boot scene hold a copy of the GUI prefab, so `shared-1df53434` carries `GUI.prefab`, its nested screens, `Fredoka SDF`
  and `Lilita One SDF` atlases (2.1 MB) and the 19 icons a second time: the player has them already (StreamedBundles' "in the player and in a
  bundle too" report: `Fredoka SDF.asset` 1.35 MB, Gallery and Test Panel 0.48 MB, the screens...). Removing the GUI from the level scenes
  (the Boot scene's one is enough) or loading it from the player is a level-builder change (`LevelBuildScenes`): for ticket 62, about
  1.3 MiB less in every first play. This is also why `Test Panel` and `Gallery` stay: they are inside `GUI.prefab` for debug builds
  (`GuiFlow.DevTools`), 0.5 MB uncompressed of the duplicate, gone with it.
- `Lilita One SDF Atlas` is used (the titles); not removed.
- `NavMeshSurface Icon.png` and `NavMeshModifier Icon.png` (0.34 MB each uncompressed, in `shared-1df53434`) and the TMP "Font Asset
  Icon.psd" and "Text Component Icon.psd" gizmos: script icons of package components on scene objects (the level's NavMeshSurface and
  modifiers). They are in an immutable package, so no import setting sticks; the navigation surfaces could be removed from the built
  scenes after baking (`BattleNavmesh`, level builder). Small once compressed, left for 62.
- URP's 10 film grain textures (2.5 MB uncompressed in the player) and `PerfectDOSVGA437.ttf` (debug HUD font): in package settings
  (`UniversalRenderPipelineGlobalSettings`), ticket 58's file; not touched.
- The skybox cubemap copy per level bundle: not found in this build's bundles by name (`Default-Skybox-Cubemap` no longer appears in the
  bundle reports); nothing to fix.

**Checks**: EditMode 779 pass, 0 fail (incl. `ReadableTextTests`). Desktop play test: five matches, 0 errors. Touch play test: 0 errors
(the known "waiting for the jump" timeout hit four times in a row while another agent's Unity build kept the machine at load 7-12;
passing once it was calmer, and it failed the same with the three scenes' audio change reverted). CrazyGames build in headless Chrome on
`http://localhost:...`: boots, Fungal's nine bundles fetched, the card shows (`Media/icons-61/web-fungal-card.png`), a click starts the
match with HUD, weapon slot icon, kill feed icon, no exception, no missing glyph; the console has the three SwiftShader hidden-shader
lines and the 67 "sound not loaded yet" lines that the build before had too (`Media/icons-61/web-fungal-match.png`).
