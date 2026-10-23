# 34 — Story beats as comic panels

The campaign's story beats between battles become **pre-rendered comic panels**, in the intro's look (31) and through the intro's viewer,
instead of 32's caption lines over slow camera moves. This settles the question 32's note left open after 31 moved to panels: the beats
(and later the ending) are comics, not a sequencer of 32's own. A pilot for **B1 "Nightfall"** (`beat-fungal`), end to end, built so that
B2-B7 and the ending follow by data alone: a story file and a render.

Depends on: 31 (the panel format, the capture with its comic look and lettering, the viewer), 32 (the campaign, the beats, the hand-over).

## Decisions

- **One file per comic**, named as the campaign step that plays it: `content/story/intro.txt`, `content/story/beat-fungal.txt`, later
  `beat-chessboard.txt` ... and `ending.txt`. Same grammar as the intro (`StoryFile`, kept byte for byte in `Unity/Assets/Story/Editor/`).
- **A stage is `home`, `warp` or any battle level by its file's name** (`fungal` is `content/levels/fungal.level`, rendered in
  `Fungal.unity`). A file read from `content/` is checked against `content/levels/`; no stage list to extend for a new beat.
- **One theme per scene**: a panel in a level adds players only (`Mushroom <n>`, `Chanterelle Prime`, the blue hands), no screen and no room
  lights; a test checks every comic. Prime in person is the chanterelle scout with his crown, placed as on the intro's monitor.
- **Lettering**: balloons carry no names; each speaker's first panel has the narrator's note (3-8 words, an arrow at them). 42 is "they",
  Prime "he".
- **Where the panels go**: the intro's stay in `Assets/Story/Panels/` (its viewer prefab holds them); every other comic's in
  `Assets/Story/Resources/Comics/<comic>/panel-NN.jpg`, which the viewer finds by name, so a rendered comic plays with no build step.
- **The viewer**: `StoryIntro.Play(comic, onFinished)` plays any comic (`Begin` stays the intro's way in); other comics show in
  `Resources/StoryViewer.prefab`, the same screen without the intro's panels, so a beat does not load them. `StoryIntro.HasComic` says whether a
  comic's panels exist.
- **How a beat plays**: `Beats.FormOf(step, hasComic)`: its comic when the panels exist, else its shots (`BeatPlayer`), else nothing. In the
  campaign the comic shows over the battle's scene, loaded and held, from the moment the load's black lifts; after Play or Skip the HUD comes
  back with CLICK TO PLAY and the click lets the match go (`ComicBeatPlayer`), as after a beat of shots. The Story gallery replays a comic over
  its own backdrop, as the intro, with no scene load. The ending plays its comic over Boot once `ending.txt` is rendered.

## B1 "Nightfall" (`content/story/beat-fungal.txt`)

Four panels in `Fungal.unity` at night, by the red castle and spawn; 42 and Mushroom 7 by the spawn, Prime pops out of blue pixels 5 m away.

| # | Picture | Lettering |
|---|---|---|
| 1 | Low and wide: the red castle, 42 and Mushroom 7 on the left, Prime in a blue pixel pop on the right | Caption: "Arena two: Fungal. Nightfall." / SFX: "POP!" / Note (Prime): "Chanterelle Prime. Second place. Dresses for first." |
| 2 | Prime from below, the eye moon over his crown | Prime: "Congratulations on winning a *meadow*." / "Regulation update: *machines.*" |
| 3 | Two frames: 42 face on; Prime face on, his crown in sight | 42: "Machines aren't in the rulebook." / Note (42): "Mushroom 42. Still champion. On paper." / Prime: "They are now. I wrote the rulebook. This morning." |
| 4 | Mushroom 7 leaning in to 42, Prime gone, the castle behind | Mushroom 7: "Fill your spores. Press Q. Trust me." / Note (7): "Mushroom 7. Teammate. Has read the manual." |

## Adding the next beat

1. Write `content/story/beat-<level>.txt` (copy B1's; stage = the level's name; only players as actors). `dotnet test` checks it.
2. Unity: Tools > SDF Story > Render Comic... > the comic's Render (or `-executeMethod SdfStory.Editor.StoryCapture.CaptureInBatch -comic beat-<level>`).
3. Look at the panels, tune the cameras and anchors, render again; commit the JPGs. The campaign and the gallery play it from then on; its
   shots in `Beat.cs` stay as the fallback (or can go once every beat has panels).

## Status

Done: the generic stage and the comic files in `StoryFile` (both copies), `StoryCapture` (every comic, `-comic`, a level stage opens its
scene, Prime with his crown, `Render Comic...` window), B1's four panels rendered under `Assets/Story/Resources/Comics/beat-fungal/`
(shipped art, committed like the intro's), `StoryViewer.prefab` built, `StoryIntro.Play`/`HasComic`, `ComicBeatPlayer`, `Beats.FormOf`,
`CampaignRunner` (campaign, gallery replay, ending), tests. `dotnet test` (Story: 49, among them every comic's checks and B1's lines and notes);
Unity EditMode 552 passed; `CampaignPlayTest` 0 errors with B1 as panels (Next, Skip, hand-over, gallery replay over Boot) and B2 still as shots.
The play test's stale check that the kitchen had no level file was brought up to date.

**Left, by eye / in Unity:** a Play-mode run of B1 with a mouse (the hand-over click, Escape as Skip, the music, which is the intro's
`intro-music.ogg` once it exists); the panels' taste (camera numbers are first tunings: Prime's face half behind his pop's pixels in panel 1,
42 and 7 alike in panel 4, 7's lean); `StoryIntro.prefab` was not rebuilt here (the user's working copy kept), so Build Intro Viewer once
re-saves it; B2-B7 and the ending are still shots until their comics are written and rendered.

**Update (2026-10-23):** B2-B7 and the ending are written and rendered (`content/story/beat-chessboard.txt` ... `beat-boss.txt`, `ending.txt`),
each staged against its renders. Prime pops in only in B1, his first appearance; SFX are rare (POP! in B1, SHLORP and WHOOOOSH in the
intro) and drawn as comic lettering in a burst fitted to the text, coloured per SFX (`sfx ... color=`). The speakers include Mushroom 13,
Mushroom 99, Participation Award and the Ref (a sky box, no body); blue's players by cast name are chanterelle scouts. The ending's
monitor shows the current game (`screen-ending-banner`, `-victory`, `-title`; see `StoryScreens`). Left: B7's last panel (42 from
behind, the tail), Prime's hand gestures at giant scale, the intro's panel 10 without its BOING.
