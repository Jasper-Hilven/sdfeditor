using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SdfGui.Editor.GuiFactory;

namespace SdfGui.Editor
{
    /// <summary>
    /// The screens of the GUI as prefabs in Assets/GUI/Prefabs/Screens, each filling the canvas and built from the component prefabs
    /// (<see cref="GuiPrefabs"/>): main menu, lobby, HUD, scoreboard, pause, settings, death, round end, the gallery of every component
    /// and the test panel. Each has a <see cref="GuiScreenRoot"/> for <see cref="GuiFlow"/> and draws from the match through its view.
    /// </summary>
    public sealed class GuiScreens
    {
        public const string Folder = GuiPrefabs.Folder + "/Screens";

        private readonly GuiPrefabs p;
        private readonly GuiFactory f;

        public readonly Dictionary<GuiScreen, GameObject> Screens = new Dictionary<GuiScreen, GameObject>();
        public GameObject TestPanel;

        public GuiScreens(GuiPrefabs prefabs)
        {
            p = prefabs;
            f = prefabs.Factory;
        }

        public void Build()
        {
            Screens[GuiScreen.MainMenu] = MainMenu();
            Screens[GuiScreen.Lobby] = Lobby();
            Screens[GuiScreen.Hud] = Hud();
            Screens[GuiScreen.Scoreboard] = Scoreboard();
            Screens[GuiScreen.Pause] = Pause();
            Screens[GuiScreen.Settings] = Settings();
            Screens[GuiScreen.Death] = Death();
            Screens[GuiScreen.RoundEnd] = RoundEnd();
            Screens[GuiScreen.Gallery] = Gallery();
            TestPanel = Test();
        }

        private static GameObject Root(string name, GuiScreen screen)
        {
            var root = Object(name, null);
            Stretch(root.transform);
            root.AddComponent<GuiScreenRoot>().screen = screen;
            return root;
        }

        private static GameObject Save(GameObject root) => GuiFactory.Save(root, $"{Folder}/{root.name}.prefab");

        private ToonShape Dim(Transform parent, Color color, float start = 0.05f)
        {
            var dim = f.Shape(parent, "Dim", ToonShapeKind.Vignette, color, Centre, Vector2.zero, Vector2.zero, s => s.FillAmount = start);
            Stretch(dim);
            dim.raycastTarget = true; // keeps clicks off what is under the menu
            return dim;
        }

        // ---------------------------------------------------------------- Main menu

        private GameObject MainMenu()
        {
            var root = Root("Main Menu", GuiScreen.MainMenu);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.9f), -0.2f);

            // Logo: a big spotted cap with the name on it, the 2 on a gold burst
            var logo = Object("Logo", t);
            Place(logo.transform, Top, new Vector2(0, -70), new Vector2(1100, 430));
            f.Shape(logo.transform, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiPalette.Spore, 0.35f), Centre, new Vector2(0, -10), new Vector2(1300, 520));
            f.Shape(logo.transform, "Cap", ToonShapeKind.Cap, GuiPalette.Amanita, Centre, new Vector2(0, 40), new Vector2(980, 330), s =>
            {
                s.Outline = 9;
                s.Shadow = 14;
                s.Spots = 1;
                s.Seed = 3;
                s.Wobble = 2;
            });
            f.Text(logo.transform, "Minigun", "MINIGUN", TextStyle.Title, 120, GuiPalette.Cream, Centre, new Vector2(0, 70), new Vector2(900, 130));
            f.Text(logo.transform, "Mushrooms", "MUSHROOMS", TextStyle.Title, 104, GuiPalette.Gold, Centre, new Vector2(0, -40), new Vector2(900, 120));
            var burst = f.Shape(logo.transform, "Two Burst", ToonShapeKind.Burst, GuiPalette.Gold, Centre, new Vector2(470, -100), new Vector2(190, 190), s =>
            {
                s.Outline = 6;
                s.Shadow = 8;
                s.Seed = 0.2f;
            });
            f.FillText(burst.transform, "Two", "2", TextStyle.Title, 110, GuiPalette.Amanita);
            f.Text(logo.transform, "Tagline", "The finest online multiplayer. Probably.", TextStyle.BodyInk, 34, GuiPalette.Cream, Centre, new Vector2(0, -170), new Vector2(900, 50));

            var buttons = Object("Buttons", t);
            Place(buttons.transform, Centre, new Vector2(0, -170), new Vector2(420, 420));
            p.ButtonInstance(buttons.transform, "PLAY", GuiPalette.Amanita, GuiCommandId.Play, Top, Vector2.zero, new Vector2(420, 100), 52);
            p.ButtonInstance(buttons.transform, "SETTINGS", GuiPalette.Chanterelle, GuiCommandId.Show, Top, new Vector2(0, -120), new Vector2(380, 80), 38, GuiScreen.Settings, 0.4f);
            p.ButtonInstance(buttons.transform, "GUI GALLERY", GuiPalette.Moss, GuiCommandId.Show, Top, new Vector2(0, -220), new Vector2(380, 80), 38, GuiScreen.Gallery, 0.4f);
            p.ButtonInstance(buttons.transform, "QUIT", GuiPalette.Dusk, GuiCommandId.Quit, Top, new Vector2(0, -320), new Vector2(380, 80), 38, spots: 0);

            var online = f.Text(t, "Players Online", "12,483 players online", TextStyle.BodyInk, 30, GuiPalette.Cream, BottomLeft, new Vector2(60, 50), new Vector2(600, 44), TextAlignmentOptions.Left);
            var status = online.gameObject.AddComponent<GuiStatus>();
            status.what = GuiStatus.What.PlayersOnline;
            status.label = online;
            f.Text(t, "Version", "v0.1 · fake multiplayer edition", TextStyle.BodyInk, 26, GuiPalette.WithAlpha(GuiPalette.Cream, 0.7f), BottomRight, new Vector2(-60, 50), new Vector2(600, 44), TextAlignmentOptions.Right);
            return Save(root);
        }

        // ---------------------------------------------------------------- Lobby

        private GameObject Lobby()
        {
            var root = Root("Lobby", GuiScreen.Lobby);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), -0.2f);
            var lobby = root.AddComponent<GuiLobby>();

            var panel = p.PanelInstance(t, "Panel", GuiPalette.Cream, Centre, new Vector2(0, 10), new Vector2(1260, 860)).transform;
            var header = p.BannerInstance(panel, "Header", "FINDING PLAYERS...", GuiPalette.Amanita, Top, new Vector2(0, 50), new Vector2(760, 104), 54);
            lobby.title = header.GetComponentInChildren<TMP_Text>();

            // Mode card and the fill of the lobby
            var mode = f.Panel(panel, "Mode", GuiPalette.Night, TopLeft, new Vector2(40, -80), new Vector2(1180, 100), 20);
            f.Text(mode.transform, "Mode", "LAST MUSHROOM STANDING", TextStyle.Title, 40, GuiPalette.Gold, Left, new Vector2(30, 14), new Vector2(620, 50), TextAlignmentOptions.Left);
            f.Text(mode.transform, "Map", "Floating Island  ·  12 players  ·  free for all", TextStyle.BodyInk, 26, GuiPalette.Cream, Left, new Vector2(30, -24), new Vector2(620, 40), TextAlignmentOptions.Left);
            lobby.count = f.Text(mode.transform, "Count", "12/12", TextStyle.Title, 56, GuiPalette.Cream, Right, new Vector2(-30, 0), new Vector2(200, 70), TextAlignmentOptions.Right);
            lobby.fillBar = p.BarInstance(mode.transform, "Fill", GuiPalette.Moss, 1, Right, new Vector2(-250, 0), new Vector2(260, 34), 0);

            // Twelve player rows, in two columns
            var rows = Object("Players", panel);
            Place(rows.transform, Top, new Vector2(0, -210), new Vector2(1180, 6 * 70));
            var players = new List<GuiPlayerRow>();
            for (var i = 0; i < FakeMatch.LobbySize; i++)
            {
                var column = i / 6;
                var row = Instance<GuiPlayerRow>(p.PlayerRow, rows.transform, $"Player {i + 1}", column == 0 ? TopLeft : TopRight, new Vector2(0, -(i % 6) * 70), new Vector2(570, 60));
                players.Add(row);
            }
            lobby.rows = players.ToArray();

            lobby.countdown = f.Text(panel, "Countdown", "Dropping onto the island in 5", TextStyle.Label, 36, GuiPalette.Dusk, Bottom, new Vector2(-150, 64), new Vector2(760, 60), TextAlignmentOptions.Left);
            ((RectTransform)lobby.countdown.transform).anchoredPosition = new Vector2(-180, 60);
            p.ButtonInstance(panel, "START NOW", GuiPalette.Moss, GuiCommandId.StartNow, BottomRight, new Vector2(-300, 34), new Vector2(300, 84), 38, spots: 0.5f);
            p.ButtonInstance(panel, "LEAVE", GuiPalette.Dusk, GuiCommandId.LeaveMatch, BottomRight, new Vector2(-40, 34), new Vector2(230, 84), 38, spots: 0);
            return Save(root);
        }

        // ---------------------------------------------------------------- HUD

        private GameObject Hud()
        {
            var root = Root("HUD", GuiScreen.Hud);
            var t = root.transform;
            var hud = root.AddComponent<GuiHud>();

            // Behind everything: the red of being hit, and the splats from where it came
            hud.vignette = f.Shape(t, "Damage Vignette", ToonShapeKind.Vignette, GuiPalette.WithAlpha(GuiPalette.AmanitaDark, 0), Centre, Vector2.zero, Vector2.zero, s => s.FillAmount = 0.35f);
            Stretch(hud.vignette);
            var direction = Object("Damage Direction", t);
            hud.damageDirection = Place(direction.transform, Centre, Vector2.zero, new Vector2(10, 10));
            hud.damageSplats = new[]
            {
                f.Shape(direction.transform, "Splat", ToonShapeKind.Splat, GuiPalette.WithAlpha(GuiPalette.Amanita, 0), Centre, new Vector2(0, 360), new Vector2(240, 200), s => { s.Seed = 0.3f; s.Shadow = 0; s.Outline = 4; }),
                f.Shape(direction.transform, "Splat Left", ToonShapeKind.Splat, GuiPalette.WithAlpha(GuiPalette.AmanitaDark, 0), Centre, new Vector2(-150, 330), new Vector2(120, 110), s => { s.Seed = 0.7f; s.Shadow = 0; s.Outline = 3; }),
                f.Shape(direction.transform, "Splat Right", ToonShapeKind.Splat, GuiPalette.WithAlpha(GuiPalette.AmanitaDark, 0), Centre, new Vector2(150, 340), new Vector2(100, 90), s => { s.Seed = 0.9f; s.Shadow = 0; s.Outline = 3; }),
            };

            // Crosshair: four arms and a dot, and the hit marker over them
            var crosshair = Object("Crosshair", t);
            hud.crosshair = Place(crosshair.transform, Centre, Vector2.zero, new Vector2(100, 100));
            hud.crosshairArms = new RectTransform[4];
            for (var i = 0; i < 4; i++)
            {
                var vertical = i % 2 == 0;
                var arm = f.Shape(crosshair.transform, $"Arm {i + 1}", ToonShapeKind.Pill, GuiPalette.Cream, Centre, Vector2.zero, vertical ? new Vector2(8, 22) : new Vector2(22, 8), s =>
                {
                    s.Outline = 2.5f;
                    s.Shadow = 0;
                    s.Shading = 0;
                });
                var direction2 = i switch { 0 => Vector2.up, 1 => Vector2.right, 2 => Vector2.down, _ => Vector2.left };
                arm.rectTransform.pivot = new Vector2(0.5f, 0.5f) - direction2 * 0.5f;
                arm.rectTransform.anchoredPosition = direction2 * hud.crosshairGap;
                hud.crosshairArms[i] = arm.rectTransform;
            }
            f.Shape(crosshair.transform, "Dot", ToonShapeKind.Pill, GuiPalette.Cream, Centre, Vector2.zero, new Vector2(8, 8), s =>
            {
                s.Outline = 2.5f;
                s.Shadow = 0;
                s.Shading = 0;
            });
            hud.hitMarker = f.Shape(t, "Hit Marker", ToonShapeKind.Cross, GuiPalette.WithAlpha(GuiPalette.Cream, 0), Centre, Vector2.zero, new Vector2(56, 56), s =>
            {
                s.Thickness = 7;
                s.Outline = 2.5f;
                s.Shadow = 0;
            });

            // Top left: players left and your kills
            var left = f.Panel(t, "Players Left", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), TopLeft, new Vector2(50, -46), new Vector2(230, 96), 22);
            f.Shape(left.transform, "Cap", ToonShapeKind.Cap, GuiPalette.Amanita, Left, new Vector2(18, 4), new Vector2(66, 50), s => { s.Outline = 4; s.Shadow = 4; s.Spots = 1; });
            hud.alive = f.Text(left.transform, "Count", "9", TextStyle.Title, 58, GuiPalette.Cream, Left, new Vector2(96, 4), new Vector2(80, 70), TextAlignmentOptions.Left);
            f.Text(left.transform, "Label", "LEFT", TextStyle.BodyInk, 24, GuiPalette.Paper, Right, new Vector2(-16, -16), new Vector2(80, 30), TextAlignmentOptions.Right);
            var kills = f.Panel(t, "Kills", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), TopLeft, new Vector2(300, -46), new Vector2(210, 96), 22);
            f.Shape(kills.transform, "Splat", ToonShapeKind.Splat, GuiPalette.Amanita, Left, new Vector2(14, 0), new Vector2(66, 66), s => { s.Outline = 3.5f; s.Shadow = 3; s.Seed = 0.45f; });
            hud.kills = f.Text(kills.transform, "Count", "2", TextStyle.Title, 58, GuiPalette.Cream, Left, new Vector2(90, 4), new Vector2(70, 70), TextAlignmentOptions.Left);
            f.Text(kills.transform, "Label", "KILLS", TextStyle.BodyInk, 24, GuiPalette.Paper, Right, new Vector2(-14, -16), new Vector2(90, 30), TextAlignmentOptions.Right);
            hud.fps = f.Text(t, "FPS", "60 FPS", TextStyle.BodyInk, 24, GuiPalette.Moss, TopLeft, new Vector2(56, -156), new Vector2(200, 30), TextAlignmentOptions.Left);

            // Top centre: the round's clock and the island's edge
            var clock = f.Panel(t, "Clock", GuiPalette.Paper, Top, new Vector2(0, -40), new Vector2(230, 90), 45);
            hud.clock = f.FillText(clock.transform, "Time", "2:14", TextStyle.Label, 62, GuiPalette.Ink);
            var pips = Object("Edge", t);
            Place(pips.transform, Top, new Vector2(0, -144), new Vector2(280, 40));
            hud.ringPips = Enumerable.Range(0, 4).Select(i => f.Shape(pips.transform, $"Ring {i + 1}", ToonShapeKind.Pill, GuiPalette.Moss, Left, new Vector2(i * 34, 0), new Vector2(26, 26), s =>
            {
                s.Outline = 3.5f;
                s.Shadow = 3;
            })).ToArray();
            hud.ringLabel = f.Text(pips.transform, "Label", "EDGE 1/4", TextStyle.Title, 30, GuiPalette.Cream, Left, new Vector2(146, 0), new Vector2(134, 40), TextAlignmentOptions.Left);

            // Top right: the kill feed
            var feed = Object("Kill Feed", t);
            Place(feed.transform, TopRight, new Vector2(-50, -46), new Vector2(700, 340));
            var layout = feed.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var killFeed = feed.AddComponent<GuiKillFeed>();
            killFeed.entries = Enumerable.Range(0, 5).Select(i => Instance<GuiKillFeedEntry>(p.KillFeedEntry, feed.transform, $"Entry {i + 1}", TopRight, Vector2.zero)).ToArray();

            // Moments: crumble warning, elimination pop-up, pick-up prompt, spectating
            var crumble = p.BannerInstance(t, "Crumble Warning", "THE ISLAND'S EDGE IS CRUMBLING!", GuiPalette.Amanita, Top, new Vector2(0, -210), new Vector2(940, 104), 50);
            crumble.Wobble = 4;
            var crumbleSub = f.Text(crumble.transform, "Hint", "Head for the middle!", TextStyle.BodyInk, 30, GuiPalette.Gold, Bottom, new Vector2(0, -44), new Vector2(600, 40));
            crumbleSub.name = "Hint";
            hud.crumbleBanner = crumble.gameObject;

            var popup = Object("Eliminated", t);
            Place(popup.transform, Centre, new Vector2(0, -170), new Vector2(700, 140));
            hud.eliminatedTitle = f.Text(popup.transform, "Title", "ELIMINATED", TextStyle.Title, 62, GuiPalette.Gold, Top, Vector2.zero, new Vector2(700, 76));
            hud.eliminatedName = f.Text(popup.transform, "Name", "xX_Morel_Xx", TextStyle.BodyInk, 40, GuiPalette.Cream, Bottom, new Vector2(0, 6), new Vector2(700, 56));
            hud.eliminatedPopup = popup;

            var pickup = f.Panel(t, "Pick Up", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), Bottom, new Vector2(0, 220), new Vector2(520, 76), 20);
            var key = f.Shape(pickup.transform, "Key", ToonShapeKind.Box, GuiPalette.Paper, Left, new Vector2(16, 0), new Vector2(56, 56), s => { s.CornerRadius = 12; s.Outline = 4; s.Shadow = 5; });
            f.FillText(key.transform, "Letter", "E", TextStyle.Label, 36, GuiPalette.Ink);
            hud.pickupLabel = f.Text(pickup.transform, "Label", "Pick up Spore Sac", TextStyle.BodyInk, 34, GuiPalette.Cream, Left, new Vector2(92, 0), new Vector2(410, 60), TextAlignmentOptions.Left);
            hud.pickupPrompt = pickup.gameObject;

            var spectating = p.BannerInstance(t, "Spectating", "SPECTATING", GuiPalette.Dusk, Bottom, new Vector2(0, 120), new Vector2(720, 84), 40);
            spectating.Spots = 0;
            hud.spectating = spectating.gameObject;
            hud.spectatingLabel = spectating.GetComponentInChildren<TMP_Text>();

            // Bottom left: health
            var health = f.Panel(t, "Health", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomLeft, new Vector2(50, 46), new Vector2(470, 118), 24);
            f.Shape(health.transform, "Cap", ToonShapeKind.Cap, GuiPalette.Amanita, Left, new Vector2(18, 6), new Vector2(82, 62), s => { s.Outline = 4.5f; s.Shadow = 5; s.Spots = 1; s.Seed = 1.3f; });
            f.Text(health.transform, "Label", "HEALTH", TextStyle.BodyInk, 24, GuiPalette.Paper, TopLeft, new Vector2(118, -10), new Vector2(200, 30), TextAlignmentOptions.Left);
            hud.health = p.BarInstance(health.transform, "Bar", GuiPalette.Moss, 0.72f, BottomLeft, new Vector2(116, 20), new Vector2(330, 46), 32);

            // Bottom right: spores (ammo) and the reload ring
            var spores = f.Panel(t, "Spores", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomRight, new Vector2(-50, 46), new Vector2(430, 140), 24);
            f.Text(spores.transform, "Label", "SPORES", TextStyle.BodyInk, 24, GuiPalette.Paper, TopRight, new Vector2(-22, -10), new Vector2(200, 30), TextAlignmentOptions.Right);
            hud.ammo = f.Text(spores.transform, "Count", "164", TextStyle.Title, 84, GuiPalette.Cream, Right, new Vector2(-130, 4), new Vector2(220, 90), TextAlignmentOptions.Right);
            hud.magazine = f.Text(spores.transform, "Magazine", "/ 250", TextStyle.BodyInk, 32, GuiPalette.Paper, Right, new Vector2(-22, -6), new Vector2(110, 40), TextAlignmentOptions.Right);
            hud.ammoBar = p.BarInstance(spores.transform, "Bar", GuiPalette.Spore, 0.656f, Bottom, new Vector2(0, 14), new Vector2(390, 20), 0);
            hud.ammoBar.bar.Outline = 3.5f;
            hud.ammoBar.bar.Shadow = 3;
            hud.ammoBar.bar.Glow = 8;
            hud.reloadRing = f.Shape(spores.transform, "Reload", ToonShapeKind.Ring, GuiPalette.Spore, Left, new Vector2(26, 8), new Vector2(84, 84), s =>
            {
                s.Thickness = 16;
                s.FillMode = ToonFillMode.Radial;
                s.FillAmount = 0.4f;
                s.Glow = 10;
            });
            hud.reloadLabel = f.Text(spores.transform, "Reloading", "RELOADING", TextStyle.Title, 30, GuiPalette.Spore, Top, new Vector2(0, 42), new Vector2(300, 40));
            return Save(root);
        }

        // ---------------------------------------------------------------- Scoreboard

        private GameObject Scoreboard()
        {
            var root = Root("Scoreboard", GuiScreen.Scoreboard);
            var t = root.transform;
            var board = root.AddComponent<GuiScoreboard>();
            var panel = p.PanelInstance(t, "Panel", GuiPalette.WithAlpha(GuiPalette.Cream, 0.96f), Centre, new Vector2(0, -10), new Vector2(980, 900)).transform;
            p.BannerInstance(panel, "Header", "SCOREBOARD", GuiPalette.Chanterelle, Top, new Vector2(0, 50), new Vector2(560, 100), 54);
            board.subtitle = f.Text(panel, "Subtitle", "LAST MUSHROOM STANDING", TextStyle.Label, 28, GuiPalette.Dusk, Top, new Vector2(0, -66), new Vector2(900, 40));
            var columns = Object("Columns", panel);
            Place(columns.transform, Top, new Vector2(0, -110), new Vector2(900, 30));
            f.Text(columns.transform, "Player", "PLAYER", TextStyle.Label, 22, GuiPalette.Dusk, Left, new Vector2(122, 0), new Vector2(200, 30), TextAlignmentOptions.Left);
            f.Text(columns.transform, "Kills", "KILLS", TextStyle.Label, 22, GuiPalette.Dusk, Right, new Vector2(-230, 0), new Vector2(80, 30));
            f.Text(columns.transform, "Status", "STATUS", TextStyle.Label, 22, GuiPalette.Dusk, Right, new Vector2(-104, 0), new Vector2(110, 30));
            f.Text(columns.transform, "Ping", "PING", TextStyle.Label, 22, GuiPalette.Dusk, Right, new Vector2(-14, 0), new Vector2(84, 30), TextAlignmentOptions.Right);
            var rows = Object("Players", panel);
            Place(rows.transform, Top, new Vector2(0, -146), new Vector2(900, 12 * 60));
            board.rows = Enumerable.Range(0, FakeMatch.LobbySize)
                .Select(i => Instance<GuiPlayerRow>(p.PlayerRow, rows.transform, $"Player {i + 1}", Top, new Vector2(0, -i * 60), new Vector2(900, 52)))
                .ToArray();
            f.Text(panel, "Hint", "hold TAB", TextStyle.Body, 24, GuiPalette.Dusk, Bottom, new Vector2(0, 14), new Vector2(300, 30));
            return Save(root);
        }

        // ---------------------------------------------------------------- Pause and settings

        private GameObject Pause()
        {
            var root = Root("Pause", GuiScreen.Pause);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.75f), -0.3f);
            var panel = p.PanelInstance(t, "Panel", GuiPalette.Cream, Centre, new Vector2(0, -20), new Vector2(620, 600)).transform;
            p.BannerInstance(panel, "Header", "PAUSED", GuiPalette.Violet, Top, new Vector2(0, 50), new Vector2(420, 104), 60);
            f.Text(panel, "Note", "The match keeps going: it's online!", TextStyle.Body, 28, GuiPalette.Dusk, Top, new Vector2(0, -80), new Vector2(560, 40));
            p.ButtonInstance(panel, "RESUME", GuiPalette.Moss, GuiCommandId.Resume, Top, new Vector2(0, -150), new Vector2(420, 96), 46, spots: 0.5f);
            p.ButtonInstance(panel, "SETTINGS", GuiPalette.Chanterelle, GuiCommandId.Show, Top, new Vector2(0, -270), new Vector2(420, 86), 40, GuiScreen.Settings, 0.4f);
            p.ButtonInstance(panel, "LEAVE MATCH", GuiPalette.Amanita, GuiCommandId.LeaveMatch, Top, new Vector2(0, -380), new Vector2(420, 86), 40);
            return Save(root);
        }

        private GameObject Settings()
        {
            var root = Root("Settings", GuiScreen.Settings);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), -0.3f);
            var menu = root.AddComponent<GuiSettingsMenu>();
            var panel = p.PanelInstance(t, "Panel", GuiPalette.Cream, Centre, new Vector2(0, -10), new Vector2(980, 820)).transform;
            p.BannerInstance(panel, "Header", "SETTINGS", GuiPalette.Chanterelle, Top, new Vector2(0, 50), new Vector2(500, 104), 58);

            var y = -110f;
            Transform Row(string label)
            {
                var row = Object(label, panel);
                Place(row.transform, Top, new Vector2(0, y), new Vector2(860, 90));
                f.Text(row.transform, "Label", label, TextStyle.Label, 38, GuiPalette.Ink, Left, Vector2.zero, new Vector2(400, 60), TextAlignmentOptions.Left);
                y -= 98;
                return row.transform;
            }
            menu.quality = Instance<GuiSelector>(p.Selector, Row("Quality"), "Quality", Right, Vector2.zero);
            menu.gore = Instance<GuiSelector>(p.Selector, Row("Gore"), "Gore", Right, Vector2.zero);
            menu.gore.options = GuiSettings.GoreLevels;
            menu.gore.index = 1;
            menu.gore.label.text = GuiSettings.GoreLevels[1];
            menu.sensitivity = Instance<Slider>(p.Slider, Row("Mouse sensitivity"), "Sensitivity", Right, new Vector2(-10, 0));
            menu.volume = Instance<Slider>(p.Slider, Row("Volume"), "Volume", Right, new Vector2(-10, 0));
            menu.volume.fillRect.GetComponent<ToonShape>().color = GuiPalette.Sky;
            menu.screenShake = Instance<Toggle>(p.Toggle, Row("Screen shake"), "Screen Shake", Right, new Vector2(-180, 0));
            menu.showFps = Instance<Toggle>(p.Toggle, Row("Show FPS"), "Show FPS", Right, new Vector2(-180, 0));
            menu.showFps.isOn = false;
            p.ButtonInstance(panel, "BACK", GuiPalette.Dusk, GuiCommandId.Back, Bottom, new Vector2(0, 36), new Vector2(320, 86), 42, spots: 0);
            return Save(root);
        }

        // ---------------------------------------------------------------- End of your round

        private GameObject Death()
        {
            var root = Root("Death", GuiScreen.Death);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.AmanitaDark, 0.85f), -0.1f);
            var result = root.AddComponent<GuiRoundResult>();
            result.death = true;
            result.icons = f.Kit.icons;
            f.Shape(t, "Splat", ToonShapeKind.Splat, GuiPalette.Amanita, Centre, new Vector2(0, 150), new Vector2(1000, 640), s =>
            {
                s.Seed = 0.37f;
                s.Outline = 8;
                s.Shadow = 14;
                s.Wobble = 3;
            });
            result.title = f.Text(t, "Title", "SPLATTERED!", TextStyle.Title, 150, GuiPalette.Cream, Centre, new Vector2(0, 230), new Vector2(1200, 170));
            result.avatar = Instance<GuiAvatar>(p.Avatar, t, "Killer", Centre, new Vector2(-300, 70), new Vector2(120, 120));
            result.detail = f.Text(t, "Detail", "by xX_Morel_Xx\nwith the Sniper", TextStyle.BodyInk, 44, GuiPalette.Cream, Centre, new Vector2(0, 70), new Vector2(460, 130));
            result.weapon = f.Icon(t, "Weapon", "sniper", Centre, new Vector2(330, 70), new Vector2(200, 110));

            var stats = f.Panel(t, "Stats", GuiPalette.WithAlpha(GuiPalette.Night, 0.9f), Centre, new Vector2(0, -150), new Vector2(620, 150), 26);
            result.placement = f.Text(stats.transform, "Placement", "#7 of 12", TextStyle.Title, 76, GuiPalette.Gold, Left, new Vector2(40, 10), new Vector2(330, 90), TextAlignmentOptions.Left);
            f.Text(stats.transform, "Placement Label", "PLACE", TextStyle.BodyInk, 24, GuiPalette.Paper, BottomLeft, new Vector2(42, 12), new Vector2(200, 30), TextAlignmentOptions.Left);
            result.kills = f.Text(stats.transform, "Kills", "2", TextStyle.Title, 76, GuiPalette.Cream, Right, new Vector2(-40, 10), new Vector2(160, 90), TextAlignmentOptions.Right);
            f.Text(stats.transform, "Kills Label", "KILLS", TextStyle.BodyInk, 24, GuiPalette.Paper, BottomRight, new Vector2(-42, 12), new Vector2(200, 30), TextAlignmentOptions.Right);

            p.ButtonInstance(t, "SPECTATE", GuiPalette.Violet, GuiCommandId.Spectate, Centre, new Vector2(-180, -330), new Vector2(330, 90), 42, spots: 0.4f);
            p.ButtonInstance(t, "NEXT ROUND", GuiPalette.Moss, GuiCommandId.NextRound, Centre, new Vector2(180, -330), new Vector2(330, 90), 42, spots: 0.4f);
            return Save(root);
        }

        private GameObject RoundEnd()
        {
            var root = Root("Round End", GuiScreen.RoundEnd);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.8f), -0.2f);
            var result = root.AddComponent<GuiRoundResult>();
            result.death = false;
            result.icons = f.Kit.icons;
            f.Shape(t, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiPalette.Gold, 0.5f), Centre, new Vector2(0, 120), new Vector2(900, 900));
            f.Shape(t, "Burst", ToonShapeKind.Burst, GuiPalette.Gold, Centre, new Vector2(0, 120), new Vector2(360, 360), s =>
            {
                s.Outline = 7;
                s.Shadow = 10;
                s.Wobble = 3;
            });
            result.avatar = Instance<GuiAvatar>(p.Avatar, t, "Winner", Centre, new Vector2(0, 120), new Vector2(220, 220));
            result.title = p.BannerInstance(t, "Title", "LAST MUSHROOM STANDING!", GuiPalette.Amanita, Centre, new Vector2(0, 360), new Vector2(1180, 130), 76).GetComponentInChildren<TMP_Text>();
            result.detail = f.Text(t, "Detail", "You outlasted everyone", TextStyle.BodyInk, 44, GuiPalette.Cream, Centre, new Vector2(0, -90), new Vector2(1200, 60));

            var stats = f.Panel(t, "Stats", GuiPalette.WithAlpha(GuiPalette.Night, 0.9f), Centre, new Vector2(0, -210), new Vector2(620, 150), 26);
            result.placement = f.Text(stats.transform, "Placement", "#1 of 12", TextStyle.Title, 76, GuiPalette.Gold, Left, new Vector2(40, 10), new Vector2(330, 90), TextAlignmentOptions.Left);
            f.Text(stats.transform, "Placement Label", "PLACE", TextStyle.BodyInk, 24, GuiPalette.Paper, BottomLeft, new Vector2(42, 12), new Vector2(200, 30), TextAlignmentOptions.Left);
            result.kills = f.Text(stats.transform, "Kills", "5", TextStyle.Title, 76, GuiPalette.Cream, Right, new Vector2(-40, 10), new Vector2(160, 90), TextAlignmentOptions.Right);
            f.Text(stats.transform, "Kills Label", "KILLS", TextStyle.BodyInk, 24, GuiPalette.Paper, BottomRight, new Vector2(-42, 12), new Vector2(200, 30), TextAlignmentOptions.Right);
            result.next = f.Text(t, "Next", "Next round in 10", TextStyle.BodyInk, 32, GuiPalette.Paper, Centre, new Vector2(0, -320), new Vector2(600, 44));

            p.ButtonInstance(t, "NEXT ROUND", GuiPalette.Moss, GuiCommandId.NextRound, Centre, new Vector2(-180, -420), new Vector2(330, 90), 42, spots: 0.4f);
            p.ButtonInstance(t, "MAIN MENU", GuiPalette.Dusk, GuiCommandId.LeaveMatch, Centre, new Vector2(180, -420), new Vector2(330, 90), 42, spots: 0);
            return Save(root);
        }

        // ---------------------------------------------------------------- Gallery: every component and style in one place

        private GameObject Gallery()
        {
            var root = Root("Gallery", GuiScreen.Gallery);
            var t = root.transform;
            var backdrop = f.Shape(t, "Backdrop", ToonShapeKind.Box, GuiPalette.Night, Centre, Vector2.zero, Vector2.zero, s =>
            {
                s.Outline = 0;
                s.Shadow = 0;
                s.Shading = 0;
                s.CornerRadius = 0;
                s.raycastTarget = true;
            });
            Stretch(backdrop);
            p.BannerInstance(t, "Header", "GUI GALLERY", GuiPalette.Moss, Top, new Vector2(0, -24), new Vector2(560, 96), 54);
            p.ButtonInstance(t, "BACK", GuiPalette.Dusk, GuiCommandId.Back, TopRight, new Vector2(-40, -30), new Vector2(200, 76), 36, spots: 0);

            Transform Section(string title, Vector2 position, Vector2 size)
            {
                var panel = f.Panel(t, title, GuiPalette.Cream, TopLeft, position, size, 24);
                f.Text(panel.transform, "Title", title.ToUpperInvariant(), TextStyle.Label, 30, GuiPalette.Dusk, TopLeft, new Vector2(24, -12), new Vector2(size.x - 48, 40), TextAlignmentOptions.Left);
                return panel.transform;
            }

            // Shapes: every kind the shader draws
            var shapes = Section("Shapes", new Vector2(300, -150), new Vector2(760, 330));
            (ToonShapeKind kind, Color color)[] kinds =
            {
                (ToonShapeKind.Box, GuiPalette.Amanita), (ToonShapeKind.Pill, GuiPalette.Chanterelle), (ToonShapeKind.Ring, GuiPalette.Spore),
                (ToonShapeKind.Cap, GuiPalette.Amanita), (ToonShapeKind.Splat, GuiPalette.AmanitaDark), (ToonShapeKind.Burst, GuiPalette.Gold),
                (ToonShapeKind.Glow, GuiPalette.Spore), (ToonShapeKind.Arrow, GuiPalette.Chanterelle), (ToonShapeKind.Cross, GuiPalette.Cream),
                (ToonShapeKind.Check, GuiPalette.Moss), (ToonShapeKind.Vignette, GuiPalette.AmanitaDark),
            };
            for (var i = 0; i < kinds.Length; i++)
            {
                var (kind, color) = kinds[i];
                var at = new Vector2(40 + i % 6 * 120, -70 - i / 6 * 128);
                var size = kind == ToonShapeKind.Pill ? new Vector2(96, 60) : new Vector2(84, 84);
                f.Shape(shapes, kind.ToString(), kind, color, TopLeft, at + (new Vector2(84, 84) - size) * new Vector2(0.5f, -0.5f), size, s =>
                {
                    if (kind == ToonShapeKind.Cap || kind == ToonShapeKind.Box)
                        s.Spots = 1;
                    if (kind == ToonShapeKind.Ring)
                    {
                        s.Thickness = 16;
                        s.FillMode = ToonFillMode.Radial;
                        s.FillAmount = 0.65f;
                        s.Glow = 8;
                    }
                    if (kind == ToonShapeKind.Cross)
                        s.Thickness = 12;
                    if (kind == ToonShapeKind.Check)
                        s.Thickness = 13;
                    if (kind == ToonShapeKind.Splat)
                        s.Seed = 0.6f;
                    if (kind == ToonShapeKind.Box)
                        s.CornerRadius = 18;
                    if (kind == ToonShapeKind.Vignette)
                        s.FillAmount = -0.2f;
                });
                f.Text(shapes, kind + " Label", kind.ToString(), TextStyle.Body, 20, GuiPalette.Dusk, TopLeft, at + new Vector2(-10, -88), new Vector2(104, 26));
            }

            // Buttons
            var buttons = Section("Buttons", new Vector2(1090, -150), new Vector2(780, 330));
            p.ButtonInstance(buttons, "PRIMARY", GuiPalette.Amanita, GuiCommandId.Show, TopLeft, new Vector2(30, -70), new Vector2(340, 86), 40, GuiScreen.Gallery);
            p.ButtonInstance(buttons, "SECONDARY", GuiPalette.Chanterelle, GuiCommandId.Show, TopLeft, new Vector2(400, -70), new Vector2(340, 86), 40, GuiScreen.Gallery, 0.4f);
            p.ButtonInstance(buttons, "CONFIRM", GuiPalette.Moss, GuiCommandId.Show, TopLeft, new Vector2(30, -180), new Vector2(340, 86), 40, GuiScreen.Gallery, 0.4f);
            p.ButtonInstance(buttons, "QUIET", GuiPalette.Dusk, GuiCommandId.Show, TopLeft, new Vector2(400, -180), new Vector2(340, 86), 40, GuiScreen.Gallery, 0);
            p.ButtonInstance(buttons, "SMALL", GuiPalette.Violet, GuiCommandId.Show, TopLeft, new Vector2(30, -284), new Vector2(150, 40), 22, GuiScreen.Gallery, 0);
            p.ButtonInstance(buttons, "SMALL", GuiPalette.SporeDeep, GuiCommandId.Show, TopLeft, new Vector2(200, -284), new Vector2(150, 40), 22, GuiScreen.Gallery, 0);
            f.Text(buttons, "Hint", "hover, press: they squish", TextStyle.Body, 24, GuiPalette.Dusk, TopLeft, new Vector2(400, -288), new Vector2(340, 34), TextAlignmentOptions.Left);

            // Controls
            var controls = Section("Controls", new Vector2(300, -510), new Vector2(760, 250));
            Instance<Slider>(p.Slider, controls, "Slider", TopLeft, new Vector2(40, -86));
            Instance<Toggle>(p.Toggle, controls, "Toggle On", TopLeft, new Vector2(520, -74));
            Instance<Toggle>(p.Toggle, controls, "Toggle Off", TopLeft, new Vector2(620, -74)).isOn = false;
            Instance<GuiSelector>(p.Selector, controls, "Selector", TopLeft, new Vector2(40, -160));

            // Bars and meters
            var bars = Section("Bars", new Vector2(1090, -510), new Vector2(780, 250));
            p.BarInstance(bars, "Health", GuiPalette.Moss, 0.72f, TopLeft, new Vector2(30, -70), new Vector2(330, 46), 30);
            p.BarInstance(bars, "Low Health", GuiPalette.Amanita, 0.18f, TopLeft, new Vector2(30, -134), new Vector2(330, 46), 30);
            var spores = p.BarInstance(bars, "Spores", GuiPalette.Spore, 0.56f, TopLeft, new Vector2(30, -198), new Vector2(330, 22), 0);
            spores.bar.Glow = 8;
            f.Shape(bars, "Reload Ring", ToonShapeKind.Ring, GuiPalette.Spore, TopLeft, new Vector2(400, -70), new Vector2(150, 150), s =>
            {
                s.Thickness = 22;
                s.FillMode = ToonFillMode.Radial;
                s.FillAmount = 0.4f;
                s.Glow = 12;
            });
            f.Shape(bars, "Tank", ToonShapeKind.Pill, GuiPalette.Spore, TopLeft, new Vector2(600, -66), new Vector2(64, 160), s =>
            {
                s.FillMode = ToonFillMode.Vertical;
                s.FillAmount = 0.6f;
                s.Glow = 14;
                s.Outline = 5;
            });
            f.Shape(bars, "Low Tank", ToonShapeKind.Pill, GuiPalette.Amanita, TopLeft, new Vector2(690, -66), new Vector2(64, 160), s =>
            {
                s.FillMode = ToonFillMode.Vertical;
                s.FillAmount = 0.15f;
                s.Glow = 14;
                s.Outline = 5;
            });

            // People: avatars, ping, a lobby row, kill feed entries
            var people = Section("Players", new Vector2(300, -790), new Vector2(1570, 260));
            var roster = FakeMatch.NewRoster("You", 7);
            for (var i = 0; i < 6; i++)
            {
                var avatar = Instance<GuiAvatar>(p.Avatar, people, $"Avatar {i + 1}", TopLeft, new Vector2(30 + i * 86, -64));
                avatar.color = roster[i].color;
                avatar.avatar = i % 2;
                avatar.dimmed = i == 5;
                avatar.disc.color = i == 5 ? Color.Lerp(roster[i].color, GuiPalette.Dusk, 0.75f) : roster[i].color;
            }
            var pings = new[] { 24, 88, 180 };
            for (var i = 0; i < pings.Length; i++)
            {
                var ping = Instance<GuiPingBars>(p.PingBars, people, $"Ping {pings[i]}", TopLeft, new Vector2(30 + i * 110, -170));
                ping.ping = pings[i];
                ping.label.text = pings[i].ToString();
                var lit = pings[i] < 60 ? 3 : pings[i] < 120 ? 2 : 1;
                var tone = lit == 3 ? GuiPalette.Moss : lit == 2 ? GuiPalette.Chanterelle : GuiPalette.Amanita;
                for (var b = 0; b < 3; b++)
                    ping.bars[b].color = b < lit ? tone : GuiPalette.WithAlpha(GuiPalette.Dusk, 0.8f);
            }
            var row = Instance<GuiPlayerRow>(p.PlayerRow, people, "Player Row", TopLeft, new Vector2(560, -64), new Vector2(540, 60));
            row.background.color = GuiPalette.Gold;
            row.playerName.text = "You";
            row.rank.text = "1";
            row.status.text = "YOU";
            var row2 = Instance<GuiPlayerRow>(p.PlayerRow, people, "Player Row Out", TopLeft, new Vector2(560, -140), new Vector2(540, 60));
            row2.background.color = Color.Lerp(GuiPalette.Paper, GuiPalette.Dusk, 0.55f);
            row2.playerName.text = "puffball99";
            row2.rank.text = "12";
            row2.status.text = "OUT";
            row2.status.color = GuiPalette.Amanita;
            var entry = Instance<GuiKillFeedEntry>(p.KillFeedEntry, people, "Kill Feed Entry", TopRight, new Vector2(-24, -70), new Vector2(410, 52));
            entry.killer.text = GuiText.Name(roster[2]);
            entry.victim.text = GuiText.Name(roster[3]);
            var mine = Instance<GuiKillFeedEntry>(p.KillFeedEntry, people, "Your Kill Feed Entry", TopRight, new Vector2(-24, -140), new Vector2(410, 52));
            mine.background.color = GuiPalette.Paper;
            mine.killer.text = GuiText.Name(roster[0]);
            mine.victim.text = GuiText.Name(roster[4]);
            mine.weapon.sprite = f.Kit.icons != null ? f.Kit.icons.Weapon(4) : null;
            foreach (var e in new[] { entry, mine })
                e.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            entry.headshot.gameObject.SetActive(false);

            // Palette and type
            var palette = Section("Palette", new Vector2(40, -150), new Vector2(230, 900));
            (string name, Color color)[] swatches =
            {
                ("Ink", GuiPalette.Ink), ("Cream", GuiPalette.Cream), ("Paper", GuiPalette.Paper), ("Amanita", GuiPalette.Amanita),
                ("Chanterelle", GuiPalette.Chanterelle), ("Gold", GuiPalette.Gold), ("Moss", GuiPalette.Moss), ("Spore", GuiPalette.Spore),
                ("Violet", GuiPalette.Violet), ("Sky", GuiPalette.Sky), ("Night", GuiPalette.Night), ("Dusk", GuiPalette.Dusk),
            };
            for (var i = 0; i < swatches.Length; i++)
            {
                f.Shape(palette, swatches[i].name, ToonShapeKind.Box, swatches[i].color, TopLeft, new Vector2(20, -62 - i * 50), new Vector2(48, 40), s =>
                {
                    s.CornerRadius = 10;
                    s.Outline = 3.5f;
                    s.Shadow = 3;
                });
                f.Text(palette, swatches[i].name + " Label", swatches[i].name, TextStyle.Body, 24, GuiPalette.Ink, TopLeft, new Vector2(80, -64 - i * 50), new Vector2(140, 36), TextAlignmentOptions.Left);
            }
            f.Text(palette, "Title Sample", "Title", TextStyle.Title, 56, GuiPalette.Amanita, TopLeft, new Vector2(20, -670), new Vector2(200, 70), TextAlignmentOptions.Left);
            f.Text(palette, "Label Sample", "Label", TextStyle.Label, 40, GuiPalette.Ink, TopLeft, new Vector2(20, -740), new Vector2(200, 50), TextAlignmentOptions.Left);
            f.Text(palette, "Body Sample", "Body text", TextStyle.Body, 30, GuiPalette.Ink, TopLeft, new Vector2(20, -796), new Vector2(200, 40), TextAlignmentOptions.Left);
            var inked = f.Shape(palette, "Ink Sample Backdrop", ToonShapeKind.Box, GuiPalette.Night, TopLeft, new Vector2(14, -842), new Vector2(200, 44), s =>
            {
                s.CornerRadius = 10;
                s.Outline = 0;
                s.Shadow = 0;
            });
            f.FillText(inked.transform, "Ink Sample", "Over the world", TextStyle.BodyInk, 26, GuiPalette.Cream);
            return Save(root);
        }

        // ---------------------------------------------------------------- Test panel

        private GameObject Test()
        {
            var root = Object("Test Panel", null);
            Stretch(root.transform);
            root.AddComponent<GuiScreenRoot>().alwaysOn = true;
            // A canvas of its own, drawn over every screen
            var canvas = root.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;
            root.AddComponent<GraphicRaycaster>();

            var panel = f.Panel(root.transform, "Panel", GuiPalette.WithAlpha(GuiPalette.Night, 0.92f), Left, new Vector2(24, -10), new Vector2(262, 690), 18);
            panel.Outline = 4;
            panel.Shadow = 6;
            var t = panel.transform;
            f.Text(t, "Title", "TEST", TextStyle.Title, 30, GuiPalette.Gold, TopLeft, new Vector2(14, -6), new Vector2(234, 40), TextAlignmentOptions.Left);

            var y = -48f;
            void Heading(string text)
            {
                f.Text(t, text, text, TextStyle.BodyInk, 20, GuiPalette.Paper, TopLeft, new Vector2(14, y), new Vector2(234, 26), TextAlignmentOptions.Left);
                y -= 28;
            }
            var column = 0;
            void Add(string text, Color color, GuiCommandId command, GuiScreen screen = GuiScreen.Hud)
            {
                p.ButtonInstance(t, text, color, command, TopLeft, new Vector2(12 + column * 122, y), new Vector2(116, 36), 19, screen, 0);
                column = 1 - column;
                if (column == 0)
                    y -= 42;
            }
            void EndRow()
            {
                if (column == 1)
                {
                    column = 0;
                    y -= 42;
                }
                y -= 6;
            }

            Heading("Screens");
            Add("MENU", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.MainMenu);
            Add("LOBBY", GuiPalette.Dusk, GuiCommandId.Play);
            Add("HUD", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Hud);
            Add("SCORES", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Scoreboard);
            Add("PAUSE", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Pause);
            Add("SETTINGS", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Settings);
            Add("DEATH", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Death);
            Add("ROUND END", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.RoundEnd);
            Add("GALLERY", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Gallery);
            EndRow();
            Heading("Round");
            Add("NEW ROUND", GuiPalette.MossDark, GuiCommandId.StartNow);
            Add("BOTS ON/OFF", GuiPalette.MossDark, GuiCommandId.ToggleAutoEvents);
            Add("HIT ME", GuiPalette.AmanitaDark, GuiCommandId.Damage);
            Add("HEAL", GuiPalette.MossDark, GuiCommandId.Heal);
            Add("MY KILL", GuiPalette.AmanitaDark, GuiCommandId.YouKill);
            Add("HEADSHOT", GuiPalette.AmanitaDark, GuiCommandId.YouHeadshot);
            Add("BOT KILL", GuiPalette.Violet, GuiCommandId.BotKill);
            Add("CRUMBLE", GuiPalette.Violet, GuiCommandId.Crumble);
            Add("PICK UP", GuiPalette.SporeDeep, GuiCommandId.TogglePickup);
            Add("RELOAD", GuiPalette.SporeDeep, GuiCommandId.Reload);
            Add("DIE", GuiPalette.AmanitaDark, GuiCommandId.Die);
            Add("WIN", GuiPalette.Chanterelle, GuiCommandId.Win);
            EndRow();

            var state = f.Text(t, "State", "Playing", TextStyle.Body, 18, GuiPalette.Paper, TopLeft, new Vector2(14, y), new Vector2(234, 70), TextAlignmentOptions.TopLeft);
            state.textWrappingMode = TextWrappingModes.Normal;
            var status = state.gameObject.AddComponent<GuiStatus>();
            status.what = GuiStatus.What.TestState;
            status.label = state;
            return GuiFactory.Save(root, $"{Folder}/Test Panel.prefab");
        }
    }
}
