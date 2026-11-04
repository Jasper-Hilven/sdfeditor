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

Not started.
