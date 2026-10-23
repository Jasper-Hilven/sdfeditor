# 35 — Menu backdrop: 42's desk and the wall of comic panels

The flat purple behind the menus (and the dimmed meadow of the GUI scene) becomes a world: **42's desk as a 3D diorama in Boot, with a wall of
the comic panels the player has seen pinned behind it**. The camera moves between menus instead of the scene changing; what the campaign has
done shows on the desk and on the wall. A cheap fallback draws the same wall alone as flat UI.

Depends on: 31 (the room's models, `content/scenes/home.txt`, the panels), 32 (Boot, the menus, the save), 34 (the comics). One theme per
scene holds: the desk scene is strictly 42's room.

## Decisions

- **Boot holds the diorama** (`DeskDioramaBuilder`, called by `BootBuilder`): the intro's room read from `content/scenes/home.txt` the way
  `HomeBuilder` reads it, so it follows the room: `room-shell`, `rug`, `desk` (stretched to 1.8 m for room for props), `monitor`, `keyboard`,
  `mousepad`, `mouse`, `lamp`, `snack-bowl`, `chair`, `soda-can`, `crisp-bag`, `shelf`, `shelf-stuff`, `trophy`, `poster`, and 42 (the red
  amanita scout prefab) on the chair, a third of a turn towards the camera. Left in the editor-only Home scene: the window and its backdrop,
  Mushroom 7, the hands, the point lights. `Home.unity` itself is not shipped. One directional light (warm, from the front), a flat ambient, no
  point lights (the toon shader reads the main light only); shadows follow the quality level (off on the low ones).
- **Camera shots** (`MenuShots` in Core, the numbers are the poses): `Wide` for the Title and the cards over it (Settings, the difficulty card,
  Confirm keep the Title's shot), `Monitor` for Level Select (pushed in on the screen), `Wall` for the Story gallery, Credits and the developer's
  Gallery. Moves are frame-rate independent (half-way in 0.28 s) on **unscaled time**, with a slow drift (a few centimetres, periods of 40 to 90
  s), so a pause or a time scale of 0 does not freeze it. Screens not listed (HUD, Pause, Victory, Defeat ...) keep the shot; **in-match screens
  are untouched**, they show the dimmed live match as before. Loading and the Ending's own screen keep their opaque looks.
- **The monitor** shows the game (`Assets/Story/Screens/screen-cover` on the Title, `screen-game` on Level Select, `screen-ending-title` once
  the campaign is complete), through `SdfCampaign/WallCard` (unlit, scanlines), on the screen part and its LOD parts.
- **Progress shows**
  - *Desk props*: one small prop per arena won, on the desk (`DeskProgress.Props`: meadow `flower0`, fungal `puffball0`, chessboard
    `knight-light`, toy box `alpha-block0`, kitchen `cookie0`, bathroom `rubber-duck0`, computer `ram-bank-red`, boss `prime-crown`), all existing
    models, each scaled to its height and set on the desk; switched on from the save (`CampaignProgress.Won`).
  - *The trophy*: silver (second place's colour, the heat-glow property of the toon shader) until the campaign is complete, then its own gold
    (`CampaignProgress.Completed`, the same fact as the Title's crown; the editor's `GuiCampaign.PreviewComplete` shows it).
  - *The wall*: the panels of the comics seen (`CampaignProgress.Seen`: intro, beats, ending; the credits are no comic) as tilted pinned pictures in story
    order, **blank sheets** everywhere else (at least 18 sheets, so the wall is never bare; no spoiler: a blank says nothing). A loose,
    deterministic grid of 8 x 6 places, panels beyond 48 thinned evenly per comic, places behind the monitor kept clear.
  - *The light's tint* (and the flat wall's cork): cool violet at the start, lilac, gold at 8 arenas won (`WallTint`).
- **Thumbnails**: a wall of full 1920 x 1080 panels would weigh tens of MB. `WallAtlasBuilder` makes one atlas per comic (384 x 216 per panel,
  four across, JPG, about 100 KB each) and `wall-index.txt` in `Assets/Campaign/Resources/Wall/`, from the shipped panels; imported compressed,
  no mipmaps. Generated from committed art and committed too; `BootBuilder` runs it. Only the atlases of seen comics are loaded.
- **The cheap fallback** (`BackdropMode.FlatWall`): the same wall as a flat UI canvas on its own layer drawn by the Boot camera (no 3D, no post
  processing), cork background tinted per campaign step, slow parallax (two layers, a zoom per shot). Chosen by `BackdropChoice.Resolve`:
  **Auto = the desk, except on the lowest quality level (Ultra Low) and where the desk's models are not there** (a fresh clone before `sdf unity`:
  `BootBuilder` then builds only the component); Settings has a **Menu backdrop** selector (Auto / 3D desk / Flat wall, `GuiSettings.MenuBackdrop`)
  that forces either (the desk is still only drawn where it exists).
- **Screens**: Title's dim 0.9 to 0.55, Settings' 0.85 to 0.75, the difficulty card's 0.85 to 0.7; Level Select, Story and Credits' opaque
  purple becomes a Night tint at 0.6, 0.62 and 0.72 (`GuiScreens.Backdrop(parent, alpha)`). Fonts, palette and the 5 px edge margins unchanged;
  the Settings panel kept its size with nine rows.
- **Budget**: static meshes of the room (the models are LOD'd; the monitor's, desk's and props' about 30 renderers), 2 to 10 draw calls for the
  wall (one mesh for the paper, one per seen comic), one directional light, no real-time blur. Pinned cards are unlit quads.

## Steps (all done in this checkout)

1. Core (`Unity/Assets/Campaign/Core/MenuBackdrop.cs`): `MenuShot`/`MenuShots`, `DeskProgress`, `WallTint`, `BackdropChoice`, `WallContent`,
   `WallLayout`, `WallAtlas`. Tests: `Campaign/Tests/MenuBackdropTests.cs` (13).
2. Shader `Campaign/Shaders/WallCard.shader`; runtime `Campaign/Runtime/MenuBackdrop.cs` (shots, drift, desk state, 3D cards, flat wall).
3. Editor: `WallAtlasBuilder` (Tools > SDF Campaign > Build Wall Thumbnails), `DeskDioramaBuilder` (inside `BootBuilder`, Tools > SDF Campaign >
   Rebuild Boot Scene), `BootSnapshot` (Tools > SDF Campaign > Save Boot Snapshots; batch `-executeMethod SdfCampaign.Editor.BootSnapshot.Capture
   -snapshotDir <folder>`).
4. GUI: `GuiScreens` (translucent backdrops, the Settings row), `GuiSettings.MenuBackdrop`, `GuiSettingsMenu.backdrop`; run Tools > SDF GUI > Rebuild
   Prefabs and Scene, then Rebuild Boot Scene.

## Status

Done and looked at in snapshots (Title, Title with the campaign complete, Level Select, Story, Credits, Settings, difficulty card, Confirm; the flat
wall's Title, Level Select, Story): the desk, wall, screen, props and silver/gold trophy read, the buttons and text read over them. `Unity` EditMode:
572 passed (13 new); `CampaignPlayTest`: 0 errors through the Boot returns with the backdrop running in play mode.

**Left, by eye / in Unity:** a Play-mode look at the camera moves between menus and the drift (snapshots are stills); the Level Select shot
is mostly hidden by the cards (the monitor glows behind them), tune by taste; props of the later arenas only show once those are won (only the meadow's
flower and the fungal puffball were seen in the snapshots, from the sample save; the other six are not looked at, the left cluster may be behind 42's chair in the Wide shot); the web
build (size of the atlases, the flat wall on WebGL, the Ultra Low auto choice); a panel-wall look with every comic seen (more than 48 panels
thin out); the Ending screen still draws its own opaque desk over Boot (the fallback when `ending` has no panels).

## Done in the follow-up

- `GuiCampaign.PreviewFull` (editor) shows a save with every arena won and every comic seen; `BootSnapshot` now also takes `FullTitle`, `FullLevelSelect`,
  `FullStory` and `Ending`.
- The desk props: the right-hand four (kitchen to boss) stood at x 1.28 to 1.42, past the desk's end (it spans -1.5 to 0.3), floating in the air and out of
  view. All eight now stand in two rows at the desk's right end (`DeskProgress.Props`), looked at in `FullTitle.png`: knight, block, cookie, duck, RAM bank,
  crown and flower are in sight, none floating or clipping; the puffball sits mostly behind the knight.
- The wall with every comic seen: the nine comics have 47 panels in all, under the 48 places, so nothing thins out; the grid fills (`FullStory.png`).
- The Ending screen no longer draws its own desk from GUI shapes: it dims Boot's desk (backdrop alpha 0.3) over the `Wide` shot (`ShotFor(Ending)`) and holds the
  caption box (`Ending.png`). In the GUI scene (no desk) it shows the dimmed meadow like the other screens.
- GUI prefabs and scene and Boot re-saved by the editor (new object ids, no other change).

## Next (for whoever takes the rest)

1. A Play-mode look at the camera moves between menus and the drift (stills only so far; batch cannot show motion), then move this ticket to `done/`.
2. By taste: the Level Select shot (the monitor glows behind the cards), the puffball behind the knight, the bare cork square on the Ending's wall (the
   place kept clear behind the monitor, seen from the Wide shot).
3. The web build (atlas size, the flat wall on WebGL, the Ultra Low auto choice) comes with tickets 37 and 38.
