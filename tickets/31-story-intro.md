# 31 — Story intro: pulled into the computer

The game opens with a short, skippable intro that tells the whole premise as a **comic**: **Mushroom 42**, a red amanita scout and
last year's champion of the minigun-mushroom tournament, is at home playing a computer game when the blue mushrooms reach out of the
screen and pull them in. Inside the computer, 42 has to win the tournament all over again, level by level, against bots. The last
panel shows 42 at the red spawn of the first level; then the level loads and the player is in control.

Depends on: 18 (red vs blue scouts), 25 and before (a playable meadow with bots). Feeds 32 (game flow: when the intro plays, what
comes after it) and 33 (final boss: Chanterelle Prime, who is introduced here and whose crown is designed here).

## The story (decided)

- **Mushroom 42**: the red amanita scout (`amanita-scout`), last year's champion. Always **they/them**, in balloons, captions, code
  comments and docs.
- **Chanterelle Prime** (working name): last year's runner-up, the blue champion, a chanterelle scout wearing a **gold crown that is
  slightly too small**. Prime is **he/him**. He rigged the tournament and pulled 42 into the computer to beat them on home turf. He
  is the villain of the campaign and the final boss (33).
- **The campaign**: single player, 42 and red bots against blue bots, several levels. The "fake multiplayer" framing (the main
  menu's "12,483 players online", "fake multiplayer edition", the lobby's "FINDING PLAYERS...") is dropped; 32 replaces those texts.
- **Tone**: silly-earnest toon. 42 takes the tournament very seriously; nobody else in the room is surprised that a mushroom owns a
  computer.

## Presentation (decided): pre-rendered comic panels

The intro is a sequence of **comic panels rendered in Unity in the editor** and shipped as images. The game holds only the
panel images, one music track and a small viewer; there is no home scene, no cutscene sequencer and no intro geometry in the build.

- **Rendered in Unity**, not by `sdf render`: the panels use the game's own `SdfToon` shader, ink line and subsurface glow, so the
  scouts look like the scouts the player is about to control. On top of that the capture adds a **heavy comic post pass** (below),
  which only runs in the editor at capture time.
- **Balloons and caption boxes are baked into the panels** at capture time (TextMesh Pro in Unity, drawn over the render and saved
  into the PNG). The runtime draws no story text.
- **The viewer** shows one panel at a time, full screen, with background music, a **Next** button and a **Skip** button. Nothing
  else: no captions, no letterbox, no auto-advance.
- Why panels and not a real-time cutscene: no runtime cost on the web, no sequencer or actor animation (poses are set once per
  panel), each panel can be art-directed (crops, speed lines, impact frames, halftone), and the room never has to be lit and
  optimised for live play. The cost is a cut to a loading screen at the end, accepted.

## The theme rule for the intro

- The home is a scene of its own, **`Home.unity`**, used **only in the editor** to pose and render the panels (not in the build
  settings' scene list). One theme: **42's room**, a small cosy gamer's room furnished for a mushroom about 1 m tall (desk, chair,
  monitor, snacks, shelf, trophy, lamp, window), with a few mushroom forms as 42's own taste (a cap-shaped lamp, the trophy's cap
  base). Players aside, nothing in it comes from the battle levels: no grass, trees, castles, pods or flags as objects.
- The meadow appears in the room only as a **picture on the monitor** (a snapshot texture), never as geometry.
- **No home props leak into the battle levels.** The desk, monitor, snacks, trophy and the rest live in their own folder
  (`content/models/home/`, `Generated/home/`) and are placed only by the home builder. A test checks that no `.level` file places a
  `home/` model.
- The warp-tunnel panel is rendered in a cleared, empty stage (no room, no level), and belongs to neither theme.
- The arrival panel is rendered in the real `Meadow.unity`, at the red spawn, with nothing added but the scouts.
- The chanterelle hands that come out of the screen are scout parts (players), so they may appear in the room.

## The panels

Twelve panels, each 16:9 at **1920 × 1080**, one per screen. A panel may be split into 2–3 inset frames (a comic page strip) where
the table says so; the split is drawn by the capture, inside the one image. Reading time is the player's: the table's "about" is a
guide for the amount of text, at most 2 balloons per frame, each under about 12 words.

| # | Beat | Picture | Text (baked in) |
|---|---|---|---|
| 1 | The room | Wide from the room's door corner at 1.1 m: evening, lamp light, desk and glowing monitor on the left third, the shelf with the gold **minigun trophy** catching the light on the right, the window with a dusk sky behind. 42 in the chair, back to us. | Caption: "Mushroom 42. Last year's minigun champion. Still undefeated. (They checked.)" |
| 2 | Playing | Over 42's cap: the monitor fills half the frame showing the meadow in first person, a blue scout in the sights; 42's hands a blur over the keyboard (speed lines). | SFX lettering: "TAK TAK TAK" |
| 3 | The win | Two frames. Left: the monitor, "VICTORY!" on the screen. Right: close on 42 from the monitor's side, both hands up, eyes flicked to the trophy, lit by the screen. | 42: "Still got it. Hi, trophy." |
| 4 | The glitch | Straight at the monitor, slightly low, 42's cap edge in the foreground: the picture tears into blue pixel blocks, the lamp in the corner mid-flicker. | SFX: "BZZRT" |
| 5 | Prime | The monitor fills the frame: **Chanterelle Prime's face**, crown slightly too small, a smug grin, blue backdrop, scanlines. | Prime: "Hello, champion. Remember me? Second place. *Silver.*" / "This year, we play on *my* server." |
| 6 | The reply | Reverse, over the monitor's top edge onto 42, lit blue by the screen, a 10° dutch tilt. | 42: "That's not how computers wor—" |
| 7 | The hands | Side view of desk, 42 and monitor: the screen bulges like a soap film, four blue chanterelle hands push out and grab 42's floating hands; the snack bowl tips, crisps in the air. 42's eyes wide. | SFX: "SHLORP" |
| 8 | The pull-in | Behind 42, low: 42 stretched towards the screen (squash and stretch, the cap last), the chair spinning away empty (motion lines), pixel spores bursting. | Caption: "It was, in fact, how computers work." |
| 9 | The warp | A toon tunnel of rings in amanita red, chanterelle blue and spore gold; 42 small in the centre, tumbling, hands flailing; a few chanterelle hands chasing and falling behind. Strong radial speed lines. | SFX: "WHOOOOSH" |
| 10 | Arrival | The meadow, low and wide from the side at 0.5 m: 42 face-down in the grass at the red spawn, a dust puff, a bounce line, the castle behind. | SFX: "BOING" |
| 11 | The teammate | Two-shot: a red teammate scout ("Mushroom 7") on the left, 42 on the right, the red flag stand behind. Above, in the sky, a jagged blue caption box from Prime. | Mushroom 7: "42! You're here! Prime's rigged the whole tournament. Every level. Also, welcome." / Prime (sky box): "Win it again, champion. If you can." |
| 12 | Ready | Close, low, heroic: 42 looking up from under their cap, hands clenched, the meadow and castle behind. | 42: "Fine. Bring the minigun." |

The intro is shown once before the first level (32 decides when exactly, and adds "Watch intro" to the menu). It is skippable on
the first viewing too.

## The comic look

All of this is done at capture time, in the editor. The runtime only shows the finished images.

- **Base render**: URP with the game's toon materials, at 2× resolution (3840 × 2160) and downsampled for clean lines. Each panel
  sets its own camera (pose, FOV), the lights and the poses; the room's light is the lamp (warm) plus the monitor (cool); panels
  4–7 tint the monitor light blue.
- **Heavier toon**: fewer bands than in game (2–3 hard steps), stronger rim, and the outline width scaled up (about 2× the game's)
  for the panels only, through a capture-only material property, not a change to the game's materials.
- **Ink pass** (a full-screen post pass on the capture): edge detection on depth and normals for an extra, slightly wobbly ink line
  where the mesh outline misses (creases, contact lines).
- **Halftone**: the shadow band filled with a dot screen (dots on a 45° grid, about 6 px at 1080p), in a darker shade of the
  surface's colour, not black.
- **Paper**: a faint warm paper grain and a slight colour posterisation over the whole panel.
- **Comic devices** drawn by the capture per panel, from the panel's data: speed lines (radial or parallel), motion lines, impact
  bursts behind SFX lettering, a panel border (thick ink, slightly irregular), inset frames with gutters.
- **Lettering**: balloons with tails pointing at the speaker, caption boxes (cream with an ink outline, as the GUI's panel style),
  speaker names in their colour (42 amanita red, Prime chanterelle blue, Mushroom 7 red, the narrator none), the body font for
  speech, the display font (Lilita One) for SFX. Prime's sky caption has a jagged blue box.

The post pass is a capture-only shader and script in `Assets/Story/Editor/`; it never ships, so it may use anything the editor can
render (not limited to WebGL2).

## Panel data

The panels are **data in the repo as text**: `content/story/intro.txt` (or `.json`), one entry per panel, with:

- the stage (`home`, `warp` or `meadow`), the camera pose and FOV;
- actor poses: the scouts' body, eyes and hands (position, rotation, scale, for squash and stretch), and prop parts (the chair's
  `Seat`, the crisps, the snack bowl);
- the monitor's screen texture (`screen-meadow`, `screen-victory`, `screen-glitch`, `screen-prime`) and its glitch and bulge
  amounts, the lamp's intensity;
- the comic devices (speed lines, motion lines, bursts), the inset frames (rects);
- the text: balloons (speaker, text, anchor, tail target) and captions.

Nothing is placed by hand in `Home.unity`; the capture reads the file, poses the stage, renders and letters each panel.

## Assets to make

Sizes are for a scout about 1 m tall (check the scout's bounds before modelling; furniture should look made for it). The room is
only rendered in the editor, so the models need no runtime budget beyond reasonable detail; one LOD (LOD0) is enough.

### Models (`content/models/home/`, materials in `materials/home/`)

| Model | About | Notes |
|---|---|---|
| `room-shell` | 4 × 3 m floor, 2.2 m walls (three walls and a floor; the fourth wall is where the camera stands), skirting | Plain; a wallpaper pattern in the material, not the mesh |
| `window` | 0.8 × 0.7 m frame, sill, half-drawn curtains | Behind it a flat dusk backdrop card (`window-backdrop`, a quad with a sky gradient and a few stars) |
| `desk` | 1.2 × 0.6 m, 0.55 m high, rounded toon edges, one drawer | |
| `chair` | gaming chair, 0.5 m seat height, round back | A separate `Seat` part that spins (panel 8) |
| `monitor` | 0.8 m wide, 0.5 m screen, chunky bezel, stand | A `Screen` part: a flat quad with UVs, for the screen material |
| `keyboard` | 0.45 × 0.15 m, keys as bumps | |
| `mouse` + `mousepad` | 0.1 m mouse, 0.3 m pad | |
| `snack-bowl` | 0.25 m bowl of crisps | Tips over (panel 7): crisps as 5–6 separate small pieces |
| `crisp-bag` | 0.2 m, crumpled | |
| `soda-can` | 0.12 m | |
| `shelf` | 1 m wall shelf on two brackets | |
| `trophy` | 0.4 m: a golden minigun on a cap-shaped base, "1st" plate | The champion's trophy; the brightest gold in the room |
| `shelf-stuff` | 3–4 small things: books, a game box, a plush blob | |
| `lamp` | desk lamp, 0.6 m, cap-shaped shade | Emissive shade; the room's key light |
| `rug` | 1.5 m round | |
| `poster` | 0.5 × 0.7 m, last year's tournament poster | Its picture is a texture made from a GUI snapshot (the main menu logo) |
| `prime-crown` | a gold crown, five points with blue gems, sized to sit **slightly too small** on the chanterelle scout's cap | Shared with 33 (which scales it with Prime); placed on `chanterelle-scout` for the panels. If 33 lands first, use theirs |

Hands out of the screen: the existing `hand` part with the chanterelle scout's flesh material (four instances), no new model.
Mushroom 7: the existing `amanita-scout`.

### Materials

- `materials/home/`: wood (desk, shelf), plastic (monitor, keyboard, chair), fabric (curtains, rug, chair seat), wallpaper (warm,
  low-contrast stripes), gold (trophy, crown, from the existing brass/gold family if one fits), crisp (warm yellow, subsurface), can
  (red-orange). Toon setup, subsurface kept meaningful (crisps, lamp shade, curtains).
- **Screen material** (editor only): unlit, shows a texture, with scanlines and a glitch amount (block offset, blue tint, tear), and
  the soap-film **bulge** as a vertex offset (a dome by a uniform). Its textures: `screen-meadow` (a `LevelSnapshot` of the meadow
  from eye height with the HUD, through `GuiSnapshot`), `screen-victory` (the same with a VICTORY! banner), `screen-prime` (Prime's
  face close up, a `MushroomSnapshot` of the chanterelle scout with the crown on a blue backdrop).

### Effects (capture only)

- **Warp tunnel** (panel 9): a long open tube, inside out (SDF-baked or built in code), with toon rings in amanita red, chanterelle
  blue and spore gold, hard bands.
- **Pixel spores**: square glowing particles bursting out of the screen (panels 7–8) and in the tunnel (panel 9): placed as quads by
  the capture, not simulated.
- **Landing puff** (panel 10): the existing spore puff from ticket 25, or quads.

### The viewer (runtime)

- **`StoryIntro`** scene or a `Story` screen in `GuiScreens` (built by code like the other screens): a full-screen `RawImage` with
  the current panel, letterboxed to 16:9 on other aspect ratios (bars in ink black).
- **Next** (bottom right) and **Skip** (top right), buttons in the GUI's existing button style, about 5 px from the edges like the
  rest of the HUD. Next also on click on the panel, Space, Enter and the right arrow; Skip also on Escape (or a gamepad's Start).
  On the last panel Next reads "Play".
- **Transitions**: a 0.25 s cross-fade between panels, a short ink-wipe or "page turn" optional.
- **Music**: one looping background track through an `AudioSource`, fading in on the first panel and out (1 s) on Play or Skip. The
  user provides the file: `Unity/Assets/Story/Audio/intro-music.ogg` (the viewer plays nothing and logs one warning if it is
  missing).
- **End**: Play or Skip loads the first level (32 decides which and how; until then the meadow), through the usual loading.
- The panels are loaded as `Texture2D` assets in `Assets/Story/Panels/` (`panel-01.png` … `panel-12.png`), imported without
  mipmaps, compressed for the web (crunched or as JPG-quality), so the web build grows by **no more than about 6 MB**. They are not
  loaded until the intro starts.

### Tools

- **`HomeBuilder`** (editor, like `LevelBuilder` and `GuiScreens`): builds `Home.unity` from the meshed home models, with the
  placement in a text file in `content/` (the level format's `place` lines if a level can do without ground, as the chess cube's
  room does; otherwise `content/scenes/home.txt`), never by hand in the scene. Places the lamp light, 42 in the chair, the hidden
  hands.
- **`StoryCapture`** (editor): Tools > SDF Story > Render Panels, and batchmode `-executeMethod`, as `GuiSnapshot`. Reads
  `content/story/intro.txt`, opens each panel's stage (`Home.unity`, the warp stage, `Meadow.unity`), poses it, renders, runs the
  comic post pass, letters the panel and writes `Assets/Story/Panels/panel-NN.png`. Also makes the screen textures. A single panel
  can be re-rendered by number.
- **Panels are generated but committed**: unlike meshes and bakes, the panel PNGs are the shipped art and are checked into git, so a
  fresh clone has the intro without running the capture (which needs the meshed home models and baked meadow).

## Open questions

1. **Arrival**: the meadow's red spawn, or a small tutorial area first (32)?
2. **Teammate's name**: "Mushroom 7" is a placeholder.
3. **Panel split**: one panel per screen as above, or 2–3 panels per screen as a comic page? The table assumes one per screen with
   a few inset frames.

## Risks

- **Scout acting in stills**: the stretch in panel 8 may need more than scaling the body; a squash in the capture's vertex pass or a
  baked blendshape if scale reads poorly. Stills hide most of it; try scale first.
- **The comic post pass vs. the toon shader**: the halftone needs the shadow band, which the toon shader computes; the capture may
  need a debug output (a second camera pass writing the band index) rather than guessing from colour.
- **Text in images**: changing a line means re-rendering its panel; localisation means a set of panels per language. Accepted for
  now; the panel data keeps the text so a re-render is one command.
- **Theme leaks**: the monitor's meadow picture is fine; a stray tree or pod model in the room is not. The `home/` folder rule and the
  level test guard it one way; review the home placement by hand the other way.
- **Web download**: twelve 1080p panels plus music; if crunch looks muddy on the halftone, use JPG at quality 85 and check the size.

## Done when

- `Home.unity` is built by `HomeBuilder` from the home models; nothing in it is hand-placed; it holds no battle-level models; it is
  not in the build.
- `StoryCapture` renders the twelve panels from `content/story/intro.txt` with the comic look (heavy toon, ink, halftone, paper,
  lettering) into `Assets/Story/Panels/`.
- The viewer plays the panels in order with the music looping; Next advances, Skip ends; both end in the first level with the player
  in control.
- 42 is "they" and Prime "he" in every balloon, caption and comment.
- The web build grows by no more than about 6 MB.
- A test that no `.level` file places a `home/` model; the panels looked at by hand.

## Status

**Not started.** Presentation changed from a real-time cutscene to comic panels rendered in Unity (2026-10-22).
