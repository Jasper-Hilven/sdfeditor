# 32 — Campaign and game flow

The game becomes a **single-player campaign with bots**: one linear story from the title screen to the credits, eight battles in
order with a short story beat between each, and the screens that join them (title, level select, loading, pause, death, victory,
defeat, credits). This ticket fixes the order, each battle's match setup, the beats, the menus, saving, and the Unity scene and
loading architecture. The levels themselves are their own tickets (27-30), as are the intro (31) and the boss (33).

> **Note (2026-10-22)**: 31 changed from a real-time cutscene to pre-rendered comic panels (a viewer with Next, Skip and music).
> It no longer provides a `Cutscene` sequencer, caption box, warp or in-engine hand-over; `Home.unity` is editor-only. The parts
> below that reuse 31's sequencer (the beats, the ending, "Into the intro") need revisiting: either comic panels through 31's
> viewer, or a sequencer of 32's own.

Depends on: 20-25 (the match, flags, weapons, bots, machines, spores), 26 (level ruleset), 31 (intro, and its cutscene sequencer and
`Cutscene` screen, which the beats here reuse). Feeds: 27-30 (each level's place, difficulty and twist in the campaign), 33 (the boss
as the last battle).

## Decisions (from the user)

- **Single player against bots. The "fake multiplayer" concept is dropped**: no lobby, no fake ping, no fake player list or player
  count, no "it's online!" notes. See "What goes from the GUI" for every screen and text this touches.
- **The story**: Mushroom 42 (always **they/them**), a red amanita scout and last year's minigun-mushroom champion, is pulled into
  their computer game by the blue mushrooms and must win the tournament again. The villain is the blue runner-up **Chanterelle
  Prime**, who rigged it. Tone: silly-earnest toon. Prime is **he/him** (31).
- **One theme per scene**, players aside: a story beat plays in a battle level's own scene (or in 31's `Home.unity`), so it adds no
  props from another theme. Prime and the teammates are players (scouts), so they may appear anywhere.

## 1. The campaign order

```
 TITLE
   |
   v
 [0] Intro (31)              Home.unity -> warp -> Meadow           42 is pulled into the computer
   |
 [1] Meadow                  battle, with the warm-up                get your minigun back
   |
 [B1] "Nightfall"            beat, in Fungal.unity
 [2] Fungal                  battle                                  the machines appear; spores unlock
   |
 [B2] "That's my chess set"  beat, in Chessboard.unity
 [3] Chessboard              battle                                  the minigun is banned; the board is 42's own
   |
 [B3] "Off the table"        beat, in ToyBox.unity
 [4] Toy box (27)            battle                                  the machines are 42's old toys
   |
 [B4] "Down the hall"        beat, in Kitchen.unity
 [5] Kitchen (28)            battle                                  the hob is on (28's hazard)
   |
 [B5] "Bath time"            beat, in Bathroom.unity
 [6] Bathroom (29)           battle                                  the rigging shows: blue plays five
   |
 [B6] "The results file"     beat, in Computer.unity
 [7] Inside the computer (30) battle                                Prime's server: blue respawns faster
   |
 [B7] "Second place"         beat, in the boss scene (33)
 [8] Final boss (33)         battle                                  Chanterelle Prime
   |
 [9] Ending                  Home.unity                              42 is champion again, back at the desk
   |
 [10] Credits  ->  TITLE (campaign complete)
```

**Why this order.**

- **Difficulty**: the bots' skill climbs at every battle (0.15 to 0.7 at Normal, see the table in section 2), the team sizes go from
  3 vs 3 to 4 vs 5, and each battle adds one new thing to learn: moving and the minigun (meadow), machines and spores (fungal), a
  weapon mix without the minigun (chessboard), then each house level's hazard, then Prime's cheats.
- **Closer to home**: the first two levels are the tournament's own arenas, the ones 42 won last year (outdoors, life size). From
  the chessboard on, the arenas are **42's own house** as Prime copied it into the game, room by room on the way to the computer:
  the coffee table in the living room (chessboard), the toy box, the kitchen, the bathroom, and then the computer itself, where
  Prime waits. Each step is smaller in space and more personal, which is what makes the boss a home game for 42.
- **Day, then night, then indoors**: the meadow's day sky is the friendliest first look; the fungal level's Eye-Moon Night says
  "this is getting strange"; the chessboard's closed cube is the first room, and every level after it is a room.

**Each act in one line** (purpose, then twist):

| # | Act | Purpose | Twist |
|---|---|---|---|
| 1 | Meadow | Learn to move, shoot and carry a flag; get your minigun back. | Prime's new rule: champions start without their minigun. It is on a pod. |
| 2 | Fungal | The real tournament starts: 4 vs 4 at night. | The machines appear (24), "regulation update"; spores (25) unlock. |
| 3 | Chessboard | The first room: 42 recognises their own chess set. | The minigun is banned ("board-game reasons"): the side pods are electro-gun and poison-gun. |
| 4 | Toy box | A fight among 42's old toys. | The machines are toys (wind-up tank, toy helicopter), on theme per 26 and 27. |
| 5 | Kitchen | A hot, cluttered room. | The hazard 28 picks (the hob is on). |
| 6 | Bathroom | Wet and slippery. | The rigging shows: blue plays **five**. Plus 29's hazard. |
| 7 | Inside the computer | Prime's home turf. | Blue plays five and **respawns in 3 s** (red 5 s); 30's hazard. |
| 8 | Final boss | Chanterelle Prime in person. | As 33 defines it. |

The intro (31) ends at the meadow's red spawn with the player in control; there is **no separate tutorial area** (31's question 3):
the meadow's first minute is a warm-up (section 2).

## 2. The battles

### Match setup per battle

At **Normal** difficulty. Skill is `BotSkill.level` (23; 0 clumsy, 1 sharp, the default today 0.5). Team sizes count you in the
red team. All are capture the flag (21) unless the row says otherwise; a time-up tie goes to sudden death as today (20), so a match
always has a winner. A campaign match is **one round**: no 10 s result and next round (20); the victory or defeat screen ends it.

| # | Level | Red v blue | Blue skill | Red bots' skill | Win | Time | Machines | Spores | Weapons |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Meadow | 3 v 3 | 0.15 | 0.35 | first to 2 | 8 min | none | off | pistol, minigun (side pods), sniper, flamethrower (neutral) |
| 2 | Fungal | 4 v 4 | 0.30 | 0.40 | first to 3 | 10 min | both sides (24) | **on from here** | pistol, minigun, electro-gun, poison-gun |
| 3 | Chessboard | 4 v 4 | 0.40 | 0.45 | first to 3 | 10 min | none | on | pistol, electro-gun, poison-gun (side), sniper, blob-gun (neutral); no minigun |
| 4 | Toy box (27) | 4 v 4 | 0.50 | 0.50 | first to 3 | 10 min | toys (27) | on | per 27 |
| 5 | Kitchen (28) | 4 v 4 | 0.55 | 0.50 | first to 3 | 10 min | per 28 | on | per 28 |
| 6 | Bathroom (29) | 4 v **5** | 0.60 | 0.55 | first to 3 | 10 min | per 29 | on | per 29 |
| 7 | Computer (30) | 4 v **5** | 0.70 | 0.60 | first to 3 | 12 min | per 30 | on | per 30 |
| 8 | Boss (33) | per 33 | per 33 | per 33 | per 33 (a story objective) | per 33 | per 33 | on | per 33 |

- **Weapons** are the level file's pods (17), not a campaign setting; the column says what the three levels hold today. Each later
  level picks its own mix by 26's rule.
- **Mutators**: the per-battle twists in the act table. The ones this ticket owns are only setup numbers: the warm-up (meadow),
  spores off (meadow), the fifth blue bot (bathroom, computer), blue's 3 s respawn (computer). The hazards (hob, water, glitches) are
  the level tickets' (28-30) and live in their scenes; the campaign does not switch them.
- **Difficulty** (chosen at New Campaign, changeable in Settings; default Normal): **Easy** blue skill −0.15 and blue never more than
  four; **Hard** blue skill +0.15 (at most 0.95). Red bots' skill does not change with difficulty.
- **Retry on loss**: always; the defeat screen's RETRY restarts the same match at once (no beat, no reload of the scene: the match
  resets in place, as 20's round reset does). After **two losses in a row** on the same battle the defeat screen also offers
  **RETRY, EASIER**: blue skill −0.1 for this attempt, pressed again −0.2 at most; a win that way earns at most Silver.
- **Team names** (they replace 20's "Red 2", "Blue 1"): red is 42 and the numbered amanitas, blue is Prime's crew. Red: **Mushroom
  42** (you), **Mushroom 7** (31's eager teammate), **Mushroom 13** ("Lucky", the worrier), and from the fungal level **Mushroom 99**
  (big, few words). Blue: **Runner-Up**, **Honourable Mention**, **Participant**, **Also-Ran**, and the fifth (bathroom, computer)
  **Participation Award**. Prime does not play before the boss.

### The meadow's warm-up (first play only)

The match is held (as 31 holds it until the hand-over) while short prompts in the caption box teach the controls, each cleared by
doing it: "WASD to walk, mouse to look", "SPACE to jump", "Walk onto the **minigun** pod" (the pod is lit with an arrow marker),
"Hold the left button to fire" (the minigun spins up; nothing to hit yet, as the meadow has no practice targets on theme). Taking
the minigun and firing it starts the match: Mushroom 7 says "Now take their flag. Bring it home." The flag HUD's
markers (21) do the rest. Skippable (hold Space, as everywhere). Replays from Level Select and retries skip it.

### What a win gives

| # | Win unlocks |
|---|---|
| 1 | The next step; the meadow in Level Select; the minigun back (story) |
| 2 | Spores were unlocked at its start (the HUD's spore meter appears from the fungal level on); Mushroom 99 joins |
| 3-7 | The next step; the level in Level Select; its beat in the Gallery |
| 8 | The ending and credits; "campaign complete" on the title (a gold crown on the logo's cap) |

Every win also records the **medal** (section 6) and the beat that followed in the Gallery. Nothing carries into the next match
(weapons, spores and health reset per match as they do per round today).

## 3. The story beats

- **Format**: in-engine, as 31 recommends: the same `Cutscene` sequencer and caption box, a few shots with the scouts acting
  (moving their body, eyes and hands), in the **next battle's scene**, after it has loaded and before its match starts. The scene is
  loaded anyway, so a beat costs no download and no extra loading. The beat ends like 31's arrival: the camera moves into 42's eyes
  at the red spawn and the match starts. The ending plays in 31's `Home.unity`.
- **Length**: 30-60 s, 3 to 6 shots, one caption line on screen at a time (about 3 s each), no voice (31's babble per line).
- **Skippable**, first viewing too: hold Space, Escape or a gamepad's Start for 0.7 s (31's rule), which jumps to the hand-over.
  A click or Space press advances to the next line early.
- **Who is in them**: 42, the red teammates, Prime (in person: a player, so theme-safe), and the referee **Ref Puffball** (a
  puffball scout announcer; captions "from the sky" are acceptable when a body would cost too much).

Draft lines (to tune with the user):

- **B1 "Nightfall"** (Fungal, the red castle at night):
  - Prime (stepping out of a pixel pop on the castle wall): "Congratulations on winning a *meadow*. Regulation update: machines."
  - 42: "Machines aren't in the rulebook."
  - Prime: "They are now. I wrote the rulebook. This morning."
  - Mushroom 7: "Fill your spores and press Q. Trust me."
- **B2 "That's my chess set"** (Chessboard, the red back rank):
  - 42 (eyes wide at the giant board): "That's my chess set. That's the coffee ring I made in March."
  - Mushroom 13: "Is it normal for the tournament to be inside your house?"
  - Prime: "Board games are a sport now. The minigun? Banned. Board-game reasons."
- **B3 "Off the table"** (Toy box, at the red side):
  - Ref Puffball: "Arena four: the toy box. Please do not chew the arena."
  - 42 (to a wind-up tank in blue): "Sergeant Clank? You were *mine*."
  - Prime: "Your toys work for me now. Better dental."
- **B4 "Down the hall"** (Kitchen):
  - Mushroom 13: "Why is everything so big and so hot?"
  - 42: "Someone left the hob on. That someone was me."
  - Mushroom 99: "Hot."
- **B5 "Bath time"** (Bathroom):
  - Prime: "You're getting close, champion. Have a bath. Meet my fifth player."
  - Participation Award (waving): "Hi! I got a sticker!"
  - Mushroom 7: "Five against four? That's rigged!" Prime: "That's *strategy*."
- **B6 "The results file"** (Computer):
  - 42 (looking up at a giant scoreboard in the sky of light, on theme for 30): "CHAMPION: CHANTERELLE PRIME. They edited the
    results file."
  - Mushroom 99: "Delete it."
  - Ref Puffball: "Respawn timers for blue: three seconds. Red: five. That seems... fine?"
- **B7 "Second place"** (the boss scene, 33):
  - Prime: "Last year I lost by *one* capture. One! So I fixed the results. And the game. And your house."
  - 42: "You can't fix a house."
  - Prime: "I reorganised your cutlery."
- **Ending** (Home.unity, after the boss):
  - Ref Puffball (on the monitor): "By the power of the *actual* rulebook: Mushroom 42. Champion. Again."
  - Prime (tiny, on the monitor): "Best of three?"
  - 42 (back in the chair, glancing at the trophy): "...Bring the minigun."

## 4. Menus and the flow between screens

### Title

The current main menu, reworked (`GuiScreen.MainMenu` becomes `Title`): the logo stays (MINIGUN MUSHROOMS 2 on the spotted cap). The
buttons, top to bottom:

- **CONTINUE** (only with a save): goes to the first step not done; a beat not yet seen plays first.
- **NEW CAMPAIGN**: picks the difficulty (Easy / Normal / Hard, three buttons on one card), then the intro. With a save, a confirm
  first: "Start the story again? Your medals and unlocked levels stay."
- **LEVEL SELECT** (once the meadow is won).
- **SETTINGS**.
- **GALLERY**: the story so far (intro, beats seen, ending, credits) to watch again. The GUI component gallery of today becomes a
  developer screen, reached only from the test panel (F1) in the editor and development builds.
- **QUIT** (not in the web build: `Application.Quit` does nothing there).

Bottom left: the campaign's progress ("Arena 3 of 8 · 4 medals"), or nothing before a save; bottom right: the version
(`Application.version`, e.g. "v0.2"), both about 5 px from the edges.

### In a match

- **Pause** (Escape, or the web page losing pointer lock or focus): the match **stops** (`Time.timeScale = 0`, audio paused), the
  screen says **PAUSED**: RESUME, RESTART (confirm: "Restart this match?"), SETTINGS, QUIT TO MENU (confirm: "Leave the match? The
  campaign is saved at the start of this arena."). Escape again resumes. The GUI animates on unscaled time (ToonButton already does).
- **Death**: the death screen as today (title, killer and weapon, a machine named in its colour, "The island got you" for a fall)
  with a countdown "Back in 5"; then the respawn and the HUD. Escape still pauses; Tab still shows the scoreboard.
- **Scoreboard** (hold Tab): both teams with kills, deaths and captures; the subtitle is the arena and goal ("FUNGAL · FIRST TO 3").
- **Match end**: the deciding capture (or the boss's end) slows time to 0.3 for 1 s, a 2 s banner ("RED WINS!" / "BLUE WINS!") on
  the HUD, then Victory or Defeat. The old Round End screen goes.
- **Victory**: VICTORY! banner, the arena, the score (3 – 1), the medal earned with "NEW BEST" when it is, captures, kills, deaths,
  time. CONTINUE (to the next beat; from a Level Select replay: back to Level Select), RETRY, MENU.
- **Defeat**: DEFEATED banner in blue, one of Prime's taunts (5 per arena, e.g. "Silver suits you."), the score, a tip. RETRY,
  RETRY EASIER (after two losses in a row), MENU.

### Level select

A grid of eight cards in campaign order: name, a snapshot of the level (from `LevelSnapshot`), the best medal and best time. Locked
cards show a padlock and "Win the previous arena". A difficulty selector on top. PLAY loads the battle straight away: no beat, no
warm-up. BACK to the title.

### Flow

```
                         +-------------------------------------------------------+
                         |                        TITLE                          |
                         | CONTINUE  NEW CAMPAIGN  LEVEL SELECT  SETTINGS        |
                         | GALLERY   QUIT (not on the web)                       |
                         +---+---------+-------------+-----------+---------+-----+
                             |         |             |           |         |
               (next step)   |   difficulty card     |       SETTINGS   GALLERY --> CUTSCENE (replay) --> GALLERY
                             |   (+ confirm if save) |       (Back: Title)
                             v         v             v
                        +---------------------+   LEVEL SELECT --PLAY--+
                        |  step is a beat?    |                        |
                        +--yes-----------no---+                        |
                           |              |                            |
                           v              v                            v
     FADE -> LOADING (briefing, "click to play") -> CUTSCENE (beat, skippable) -> HUD (match)
                                                                         ^    |
                                                     RESUME / Esc        |    | Esc / pointer lock lost
                                                   +---------------------+    v
                                                   |                       PAUSE --RESTART--> (match resets) --> HUD
                                                   |                          |--SETTINGS--> SETTINGS --Back--> PAUSE
                                                   |                          '--QUIT TO MENU (confirm)--> FADE -> TITLE
                                                   |
                                HUD --you die--> DEATH ("Back in 5") --respawn--> HUD
                                HUD --Tab held--> SCOREBOARD --Tab up--> HUD
                                HUD --match won--> banner --> VICTORY --CONTINUE--> save --> next step (beat ... HUD)
                                                                     |--RETRY--> (match resets) --> HUD
                                                                     '--MENU--> FADE -> TITLE
                                HUD --match lost--> banner --> DEFEAT --RETRY / RETRY EASIER--> HUD
                                                                     '--MENU--> FADE -> TITLE
     after the boss: VICTORY --CONTINUE--> FADE -> LOADING -> Home.unity: CUTSCENE (ending) -> CREDITS -> TITLE
```

### Saving

- **Where**: `PlayerPrefs`, as the settings already are (`GuiSettings`). On the web Unity keeps PlayerPrefs in the browser's
  IndexedDB for the page's origin; call `PlayerPrefs.Save()` after every write so a closed tab loses nothing. No `.jslib` needed.
- **When**: after a beat is seen, after a battle is won (step done, medal), on a difficulty change. Never mid-match: quitting a match
  loses only that match.
- **Format**: one key, `campaign.v1`, holding JSON written by `JsonUtility` (small: well under 2 KB):

```json
{
  "version": 1,
  "difficulty": "normal",
  "next": "beat-fungal",
  "completed": false,
  "lossStreak": { "fungal": 1 },
  "seen": ["intro", "beat-fungal"],
  "levels": [
    { "id": "meadow", "won": true, "medal": "silver", "bestSeconds": 371, "bestScore": "2-0", "difficulty": "normal" }
  ]
}
```

- Ids, not indices, so steps can be inserted later. An unknown `version` or unreadable JSON keeps the old string under
  `campaign.broken` and starts a fresh save (logged, never a crash).
- **New Campaign** resets `next`, `seen`, `completed` and `lossStreak`; `levels` (wins, medals, Level Select) stay.

### Skip rules

- Every cutscene (intro, beats, ending) and the credits: hold Space / Escape / Start for 0.7 s, on the first viewing too.
- The meadow's warm-up: skippable the same way; never shown on a retry or a replay.
- Loading cannot be skipped; its "click to play" is required (the web needs a click to lock the pointer).
- Battles cannot be skipped; RETRY EASIER is the help for a stuck player (an open question asks whether to add more).

## 5. Unity: scenes and loading

### Scenes

| Scene | Holds | Build index |
|---|---|---|
| `Boot.unity` (new) | camera, the persistent `Campaign` object, the GUI on Title; a cheap backdrop (the GUI's own shapes and the scout avatar) | 0 |
| `Home.unity` (31) | 42's room: the intro and the ending | |
| `Meadow`, `Fungal`, `Chessboard` | the battle scenes as built today | |
| `ToyBox`, `Kitchen`, `Bathroom`, `Computer` (27-30), the boss scene (33) | the later battle scenes | |
| `Mushrooms`, `GUI`, `SpecialEffects` | showcases | dev builds only |

A release build's scene list is written by a small editor build step from the campaign's steps, so the showcases are not in the web
download (in a WebGL build every listed scene goes into the one data file loaded before start).

### The campaign state

- **Rules in plain C#** (`Assets/Campaign/Core`, assembly `SdfCampaign.Core`, no scene needed, EditMode tests like `SdfBattle.Core`):
  - `CampaignStep`: id, kind (`Cutscene`, `Battle`, `Credits`), scene name, title, and for a battle a `BattleSetup`.
  - `BattleSetup`: red and blue team sizes, blue and red skill, the names per team, captures to win, time limit, single round, warm-up,
    spores on, blue respawn seconds. Everything else (weapons, machines, hazards) comes from the scene.
  - `CampaignDefinition`: the ordered steps of section 1 with the numbers of section 2.
  - `CampaignProgress`: the save of section 4 (load, save, JSON round trip, migration), unlock rules (`IsUnlocked`, `NextStep`),
    `RecordWin(id, score, seconds, difficulty)` returning the medal and whether it is a new best, the loss streak.
  - `Medals.Of(...)` (section 6), `Difficulty.Apply(setup)`.
- **Data**: `Assets/Campaign/Resources/Campaign.asset`, written by an editor builder from a table in code (as `WeaponTableSetup` does
  for `Weapons.asset`), so it is regenerated, not hand-edited.
- **`CampaignRunner`** (MonoBehaviour on the persistent `Campaign` object in `Boot.unity`, `DontDestroyOnLoad`): `NewCampaign(difficulty)`,
  `Continue()`, `PlayLevel(id)`, `Retry()`, `Next()`, `QuitToTitle()`; holds the current step, the attempt's skill offset and
  `BattleSetup.Pending` for the scene being loaded; listens to the match's end and records it.
- **`Match` reads the setup**: at `Awake`, if `BattleSetup.Pending` is set, it overrides `MatchRules` (team sizes per team instead of
  one `playersPerTeam`, captures, time, single round, spores, blue respawn) and `Bots.skill` (per team). With none (a battle scene
  opened in the editor and played), the scene's own defaults hold, so every scene still plays alone as it does today.

### Loading and transitions

- `SceneLoader` on the Campaign object: **fade** to black (0.35 s, unscaled time, audio with it), the **Loading** screen, then
  `SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single)` (one battle scene in memory at a time, never two additively), then
  `Resources.UnloadUnusedAssets()` and a GC while still black, then fade in. The loader reports progress to the Loading screen.
- **Loading screen** = the briefing that the lobby used to be: the arena's name, the goal ("CAPTURE THE FLAG · FIRST TO 3"), both
  rosters with names (the lobby's player rows, no ping), a tip, a progress bar; at least 1.5 s, then **CLICK TO PLAY**, which
  locks the pointer (the web allows pointer lock only from a click) and starts the beat or the match. A beat shows its own first
  shot instead of waiting for the click, and asks for it at its hand-over.
- **Into the intro**: 31's warp hides the meadow's load; the Loading screen is not shown there.
- **One GUI for the whole game**: the GUI prefab is made once under the Campaign object and survives scene loads. `MatchHud` uses it
  when it is there and makes its own when it is not (a scene played alone, as today). This keeps the fade, Loading and transitions on
  one canvas and fonts loaded once.

### What GuiFlow and GuiScreen need

- `GuiScreen`: **remove** `Lobby`, `RoundEnd`; **rename** `MainMenu` to `Title`; **add** `LevelSelect`, `Loading`, `Victory`,
  `Defeat`, `Credits`, `Confirm` (a small yes/no card over any screen), `Difficulty` (the New Campaign card); `Cutscene` comes from 31.
  The developer's `Gallery` stays, reached from the test panel; the player's gallery is a new `Story` screen (a list of cutscenes).
- `GuiCommandId`: **remove** `Play`, `StartNow`, `NextRound`; **add** `Continue`, `NewCampaign`, `PlayLevel` (with a level id on
  `GuiCommand`, next to its screen), `Restart`, `Retry`, `RetryEasier`, `Next`, `QuitToMenu` (today's `LeaveMatch`), `Confirm`,
  `Cancel`, `WatchCutscene`. The commands that change scenes call `CampaignRunner`; the GUI stays free of scene references (as
  `GuiCommand` is now).
- `GuiFlow.FollowMatch`: no lobby; the match's end goes to Victory or Defeat (after the banner), not Round End; pausing sets and
  clears `Time.timeScale`; losing pointer lock or focus during the HUD opens Pause.

### What goes from the GUI

| Where | Today | After |
|---|---|---|
| Title tagline | "The finest online multiplayer. Probably." | "Last year's champion. Probably still." |
| Title bottom left | "12,483 players online" (`GuiStatus.What.PlayersOnline`, drifting) | the campaign's progress, or nothing; `PlayersOnline` deleted |
| Title bottom right | "v0.1 · fake multiplayer edition" | `Application.version` only |
| Title buttons | PLAY, SETTINGS, GUI GALLERY, QUIT | as in section 4 |
| Lobby screen | "FINDING PLAYERS...", "LAST MUSHROOM STANDING", "12/12", the fill bar, "Dropping onto the island in 5", START NOW, `GuiLobby` | deleted; the rosters move to Loading |
| Ping | `GuiPingBars`, `FakePlayer.ping`, the scoreboard's PING column, the gallery's ping samples | deleted |
| Scoreboard subtitle | "LAST MUSHROOM STANDING" | the arena and goal |
| Pause | no title, note "The match keeps going: it's online!", RESUME, SETTINGS | PAUSED; RESUME, RESTART, SETTINGS, QUIT TO MENU; the match stops |
| Round End | "LAST MUSHROOM STANDING!", "You outlasted everyone", "#1 of 12 PLACE", "Next round in 10", NEXT ROUND, MAIN MENU | deleted; Victory and Defeat instead |
| Death | sample "by xX_Morel_Xx" | sample "by Runner-Up"; "Back in 5" added |
| Bot names | `FakeMatch.BotNames` gamer tags (xX_Morel_Xx, SporeLord, ...), `LobbySize = 12` | the cast names of section 2 |
| `FakeMatch` | `MatchPhase.Lobby`, `StartLobby`, ping drift, "the match is online" | lobby and ping gone; the rest stays as the GUI showcase's driver (see open questions) |
| Test panel | LOBBY button | gone |
| Tickets 20 and 23 | "fake multiplayer names for the bots come later" | superseded by the cast names |

Any battle-royale text still left in the HUD (rings, "last mushroom standing", ELIMINATED) goes too.

## 6. Replayability

- **Medals per battle** (best kept): **Bronze** a win; **Silver** a win by 2 or more captures; **Gold** a win with blue at 0 captures
  within half the time limit, at Normal or Hard and not with RETRY EASIER. The boss's medal rules are 33's.
- **Level Select** to replay any won arena at any difficulty for a better medal.
- **Later tickets** (not here): mutators for replays (low gravity, one weapon only, "minigun mayhem", double spores); a **daily
  challenge** (an arena, a mutator and a seed from the date; local best only, no online board); a time trial per arena.

## Subtickets

In build order; each is one agent's work.

### 32.1 — Drop the fake multiplayer

Every row of "What goes from the GUI" except the new screens: the lobby, ping, players online, version and tagline texts, the
pause note, Round End's battle-royale texts, the gamer-tag bot names (cast names in `Match` as plain names for now), the test
panel's LOBBY. Pause stops the match (`Time.timeScale`), and pointer-lock or focus loss pauses. Rebuild the GUI prefabs
(`GuiScreens`), update `GuiSnapshot` shots. Nothing else changes yet: the menu's PLAY still starts what it starts today.

### 32.2 — Campaign core

`SdfCampaign.Core`: `CampaignStep`, `BattleSetup`, `CampaignDefinition` (all ten steps; 27-30 and 33 as named steps whose scenes may
not exist yet), `CampaignProgress` (JSON, `campaign.v1`, migration, unlocks, loss streak), medals, difficulty. EditMode tests: the
order, unlocks after each win, New Campaign keeping medals, a broken save, medals at their edges, difficulty clamps, a JSON round
trip.

### 32.3 — Match takes a setup

`Match` reads `BattleSetup.Pending`: per-team sizes (4 v 5), per-team bot skill, names, captures, time, **single round** (no reset
after the result), spores on or off, blue's respawn time; the match's end raised as an event with the score and time. Defaults
unchanged without a setup. EditMode tests in `SdfBattle.Tests` for uneven teams, single round and per-team respawn.

### 32.4 — Boot, loader and the persistent GUI

`Boot.unity` (built by an editor builder, nothing hand-placed), `CampaignRunner`, `SceneLoader` (fade, async load, unload unused),
the Loading screen with the briefing and CLICK TO PLAY, `MatchHud` binding to the persistent GUI, the release scene list. Done when
Boot → Meadow → Title → Fungal works in the editor and in a web build without a leak (memory after the third load within 10% of
the first).

### 32.5 — Title, Level Select, Confirm, Difficulty

The reworked Title, the Difficulty card, Confirm, Level Select (cards, padlocks, medals, snapshots), Settings' difficulty row, the
player's `Story` gallery (lists what is seen; plays it once 31's sequencer exists).

### 32.6 — Victory and Defeat

The end banner and slow-down, Victory and Defeat screens (medal, new best, stats, taunts, RETRY EASIER after two losses), RETRY
resetting the match in place, CONTINUE to the next step, saving.

### 32.7 — The first three arenas as a campaign

Meadow (warm-up, 3 v 3, spores off), Fungal, Chessboard wired with their setups; B1 and B2 as cutscenes with 31's sequencer (until
it lands: caption-only beats over a slow camera move in the scene, the same data). Playable from the Title through the chessboard's
victory, saved, continued after a reload of the web page.

### 32.8 — Ending and credits

The ending cutscene in `Home.unity` (31's scene and sequencer), the Credits screen (a 45 s scroll: the game's author, the tools,
the fonts' OFL notices for Fredoka and Lilita One), "campaign complete" on the Title.

### 32.9 — The remaining arenas and beats

As 27-30 and 33 land: each scene added to the steps and the release list, its setup checked against section 2, its beat (B3-B7)
made, its Level Select snapshot. One small change per level, done with or after that level's ticket.

### 32.10 — Web check

A full campaign run in a web build (Chrome and Safari): load times per arena, memory over all loads, a save surviving a tab close,
pointer lock after every CLICK TO PLAY, Escape in the browser opening Pause.

## Acceptance criteria

- No text, screen or component in the game speaks of online play, other players, a lobby or ping; `GuiPingBars`, `GuiLobby` and
  `GuiStatus.PlayersOnline` are gone.
- From a fresh save: Title → New Campaign (Normal) → intro → meadow warm-up → meadow match → B1 → fungal → B2 → chessboard, each with
  the setup of section 2 (team sizes, skill, captures, time, spores off on the meadow only), and the arenas after it as their tickets
  land, through the boss, ending and credits back to the Title.
- Losing shows Defeat with RETRY, which restarts the match without a reload; two losses in a row add RETRY EASIER.
- Pause stops the match; RESTART and QUIT TO MENU confirm first; losing pointer lock in the web build pauses.
- Closing the browser tab after a win and opening the game again: CONTINUE goes to the next beat; Level Select shows the won arenas
  with their medals.
- Every cutscene and the credits skip by holding Space for 0.7 s, on the first viewing too.
- Each battle scene still plays on its own when opened in the editor (no Boot needed).
- Loads run async behind a fade and the Loading screen; one battle scene in memory at a time; the web build's memory stays flat over
  five loads.
- EditMode tests for the campaign core and the match's new setup pass; 42 is "they" everywhere.

## Open questions

1. **Prime in matches**: should Prime play as a blue bot before the boss (say in the computer), or only appear in beats?
2. **Teammate names**: Mushroom 7, 13 and 99, and blue's Runner-Up, Honourable Mention, Participant, Also-Ran, Participation Award:
   keep, or other names?
3. **Gallery**: the player's gallery as story replays only, or also a model turntable (scouts, weapons, machines) as unlocks? The
   turntable costs download in the web build.
4. **Stuck players**: is RETRY EASIER enough, or should a battle be skippable after, say, five losses?
5. **`FakeMatch`**: rename it (e.g. `MatchView`) now that nothing is fake, or leave the name to save churn? The recommendation is to
   rename in 32.1 with Rider's rename refactor.
6. **Chessboard's twist**: "the board moves" (pieces sliding between squares) is not in the scene; is the minigun ban enough, or is
   a moving-pieces hazard a ticket of its own?
7. **Hard mode**: open from the start (assumed), or after the campaign is complete?
8. **Per-level download**: if the web build's single data file grows too large with eight arenas, move each arena into its own
   asset bundle (Addressables) so the first download is the Boot, Home and meadow only. A later ticket if the numbers ask for it.

## Status

Done in code (subtickets 32.1-32.10, in this folder), with the beat and ending format changed from 31's dropped sequencer to 32's own
(`Beat`, `BeatSequencer`, `BeatPlayer`: caption lines over a slow camera move in the loaded scene; the ending plays over Boot's
backdrop, as `Home.unity` is editor-only). Built: the fake multiplayer gone, `SdfCampaign.Core` (steps, setups, save `campaign.v1`,
medals, difficulty), `Match` taking a `MatchSetup`, `Boot.unity` with `CampaignRunner`, `SceneLoader`, Loading and the persistent GUI,
Title, Level Select, Confirm, Difficulty, Story, Victory, Defeat, retry in place, the meadow warm-up, beats B1-B7 and the ending as
data, credits, and a settings save fix. EditMode tests pass (`SdfCampaign.Tests`, `SdfBattle.Tests`: 290); the batch play test
`SdfCampaign.Editor.CampaignPlayTest.Run` walks the flow with 0 errors.

Left:
- Play-mode and by-eye checks: beats with real scout puppets (camera and stage numbers are first guesses), the warm-up, banner timing,
  the Loading screen with real names, the ending screen's room, the credits author name and wording, Level Select snapshots
  (`Assets/Campaign/Resources/LevelCards/<Scene>.png`), `intro-music.ogg` (31).
- The web run of 32.10's checklist (Chrome and Safari), and open question 8 (Addressables) if the data file is large.
- Levels 27-30 and the boss 33 slot in by data: see 32.9's "Adding a level to the campaign". Until their scenes exist those steps are
  skipped (Level Select shows "Coming soon") and the ending follows the chessboard.
- Boss step numbers are placeholders until ticket 33.
