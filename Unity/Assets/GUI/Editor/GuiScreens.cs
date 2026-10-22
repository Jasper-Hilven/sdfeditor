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
    /// (<see cref="GuiPrefabs"/>): main menu, HUD, scoreboard, pause, settings, death, round end, the gallery of every component
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
            f.Text(logo.transform, "Tagline", "Last year's champion. Probably still.", TextStyle.BodyInk, 34, GuiPalette.Cream, Centre, new Vector2(0, -170), new Vector2(900, 50));

            var buttons = Object("Buttons", t);
            Place(buttons.transform, Centre, new Vector2(0, -170), new Vector2(420, 420));
            p.ButtonInstance(buttons.transform, "PLAY", GuiPalette.Amanita, GuiCommandId.Play, Top, Vector2.zero, new Vector2(420, 100), 52);
            p.ButtonInstance(buttons.transform, "SETTINGS", GuiPalette.Chanterelle, GuiCommandId.Show, Top, new Vector2(0, -120), new Vector2(380, 80), 38, GuiScreen.Settings, 0.4f);
            p.ButtonInstance(buttons.transform, "GUI GALLERY", GuiPalette.Moss, GuiCommandId.Show, Top, new Vector2(0, -220), new Vector2(380, 80), 38, GuiScreen.Gallery, 0.4f);
            p.ButtonInstance(buttons.transform, "QUIT", GuiPalette.Dusk, GuiCommandId.Quit, Top, new Vector2(0, -320), new Vector2(380, 80), 38, spots: 0);

            f.Text(t, "Version", Application.version, TextStyle.BodyInk, 26, GuiPalette.WithAlpha(GuiPalette.Cream, 0.7f), BottomRight, new Vector2(-5, 5), new Vector2(600, 44), TextAlignmentOptions.Right);
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

            // Top left, 5 px from the screen's edges like the rest of the HUD: the frame rate, when the settings show it, or the hint to
            // change the quality level, which is wider
            hud.fps = f.Text(t, "FPS", "60 FPS", TextStyle.BodyInk, 24, GuiPalette.Moss, TopLeft, new Vector2(10, -5), new Vector2(560, 30), TextAlignmentOptions.Left);

            // Top centre, 5 px under the screen's top: the round's clock, the team score either side and the flags' statuses outside it
            // See-through like the other HUD panels, the time in cream on it
            var clock = f.Panel(t, "Clock", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), Top, new Vector2(0, -5), new Vector2(230, 90), 45);
            clock.Shading = 0;
            hud.clock = f.FillText(clock.transform, "Time", "3:00", TextStyle.Title, 62, GuiPalette.Cream);
            Flags(hud, clock.rectTransform);

            // Top right: the kill feed
            var feed = Object("Kill Feed", t);
            Place(feed.transform, TopRight, new Vector2(-5, -5), new Vector2(700, 340));
            var layout = feed.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var killFeed = feed.AddComponent<GuiKillFeed>();
            killFeed.entries = Enumerable.Range(0, 5).Select(i => Instance<GuiKillFeedEntry>(p.KillFeedEntry, feed.transform, $"Entry {i + 1}", TopRight, Vector2.zero)).ToArray();

            // Moments: elimination pop-up, pick-up prompt, spectating
            var popup = Object("Eliminated", t);
            Place(popup.transform, Centre, new Vector2(0, -170), new Vector2(700, 140));
            hud.eliminatedTitle = f.Text(popup.transform, "Title", "SAUTÉED!", TextStyle.Title, 62, GuiPalette.Gold, Top, Vector2.zero, new Vector2(700, 76));
            hud.eliminatedName = f.Text(popup.transform, "Name", "Runner-Up", TextStyle.BodyInk, 40, GuiPalette.Cream, Bottom, new Vector2(0, 6), new Vector2(700, 56));
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

            // Bottom left: health, its name and then its bar on one line
            var health = f.Panel(t, "Health", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomLeft, new Vector2(5, 5), new Vector2(340, 48), 16);
            f.Text(health.transform, "Label", "HEALTH", TextStyle.BodyInk, 20, GuiPalette.Paper, Left, new Vector2(14, 0), new Vector2(86, 30), TextAlignmentOptions.Left);
            hud.health = p.BarInstance(health.transform, "Bar", GuiPalette.Moss, 0.72f, Left, new Vector2(102, 0), new Vector2(226, 28), 20);
            hud.health.bar.Outline = 3;
            hud.health.bar.Shadow = 3;
            // Flat: no gloss spot, which at this size is a white blob taking room from the value
            health.Shading = 0;
            hud.health.bar.Shading = 0;

            // Over the health: the spores (ticket 25), the meter, the power cards or the power on
            Spores(hud, health.rectTransform);

            // Bottom right: the four weapon slots, the one in hand marked and named
            Weapons(hud);
            return Save(root);
        }

        /// <summary>
        /// The four weapon slots (ticket 22) in a panel at the bottom right, each a key box with the weapon's icon and its number; the
        /// name of the one in hand over them. Shown as a round starts: the pistol in hand in slot 1, the rest empty.
        /// </summary>
        private void Weapons(GuiHud hud)
        {
            var panel = f.Panel(hud.transform, "Weapons", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomRight, new Vector2(-5, 5), new Vector2(430, 140), 24);
            hud.weaponLabel = f.Text(panel.transform, "Label", "PISTOL", TextStyle.BodyInk, 24, GuiPalette.Paper, TopRight, new Vector2(-22, -10), new Vector2(390, 30), TextAlignmentOptions.Right);
            var count = SdfBattle.Inventory.SlotCount;
            hud.weaponSlots = new ToonShape[count];
            hud.weaponIcons = new Image[count];
            hud.weaponNumbers = new TMP_Text[count];
            for (var i = 0; i < count; i++)
            {
                var held = i == 0;
                var box = f.Shape(panel.transform, $"Slot {i + 1}", ToonShapeKind.Box, held ? GuiPalette.Gold : GuiPalette.WithAlpha(GuiPalette.Night, 0.55f),
                    Centre, new Vector2(-153 + i * 102, -10), new Vector2(88, 88), s => { s.CornerRadius = 12; s.Outline = 4; s.Shadow = 5; });
                if (held)
                    box.transform.localScale = Vector3.one * 1.12f;
                var icon = f.Icon(box.transform, "Icon", held ? "pistol" : null, Centre, Vector2.zero, Vector2.zero);
                Stretch(icon);
                icon.rectTransform.offsetMin = new Vector2(8, 6);
                icon.rectTransform.offsetMax = new Vector2(-6, -10);
                icon.enabled = held;
                var number = f.Text(box.transform, "Number", (i + 1).ToString(), TextStyle.Label, 22, held ? GuiPalette.Ink : GuiPalette.WithAlpha(GuiPalette.Paper, 0.6f),
                    TopLeft, new Vector2(7, -3), new Vector2(70, 28), TextAlignmentOptions.TopLeft);
                hud.weaponSlots[i] = box;
                hud.weaponIcons[i] = icon;
                hud.weaponNumbers[i] = number;
            }
        }

        /// <summary>
        /// The spores (ticket 25), 5 px over the health panel: the meter (the health panel again, a spore bar in percent), shown as a
        /// round starts; and, hidden till then, the three power cards (the key as the layout prints it, the power's name) and the line
        /// of a power on (its name and seconds); and the tint of a power on at the screen's edge, over the red of being hit.
        /// </summary>
        private void Spores(GuiHud hud, RectTransform health)
        {
            var t = hud.transform;
            var corner = new Vector2(health.anchoredPosition.x, health.anchoredPosition.y + health.sizeDelta.y + SporeGap);

            var meter = f.Panel(t, "Spore Meter", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomLeft, corner, health.sizeDelta, 16);
            meter.Shading = 0;
            f.Text(meter.transform, "Label", "SPORES", TextStyle.BodyInk, 20, GuiPalette.Paper, Left, new Vector2(14, 0), new Vector2(86, 30), TextAlignmentOptions.Left);
            hud.sporeBar = p.BarInstance(meter.transform, "Bar", GuiPalette.Spore, 0, Left, new Vector2(102, 0), new Vector2(226, 28), 20);
            hud.sporeBar.bar.Outline = 3;
            hud.sporeBar.bar.Shadow = 3;
            hud.sporeBar.bar.Shading = 0;
            hud.sporeBar.bar.Glow = 4;
            hud.sporeBar.format = "{0:0}%";
            hud.sporeBar.label.text = "0%";
            hud.sporeMeter = meter.gameObject;

            var powers = SdfBattle.SporeRules.All;
            hud.sporeCards = new GameObject[powers.Length];
            hud.sporeLetters = new TMP_Text[powers.Length];
            for (var i = 0; i < powers.Length; i++)
            {
                var card = SporePanel(t, $"Spore {powers[i]}", corner + new Vector2(i * (SporeCard.x + SporeGap), 0), SporeCard);
                var key = f.Shape(card.transform, "Key", ToonShapeKind.Box, SdfBattle.Spores.ColourOf(powers[i]), Left, new Vector2(6, 0), new Vector2(32, 32), s =>
                {
                    s.CornerRadius = 6;
                    s.Outline = 2;
                    s.Shadow = 2;
                    s.Shading = 0;
                });
                hud.sporeLetters[i] = f.FillText(key.transform, "Letter", SdfBattle.Spores.Keys[i].ToString(), TextStyle.Label, 20, GuiPalette.Ink);
                var label = f.Text(card.transform, "Label", SdfBattle.SporeRules.NameOf(powers[i]), TextStyle.BodyInk, 18, GuiPalette.Cream, Left, new Vector2(44, 0),
                    new Vector2(SporeCard.x - 50, 32), TextAlignmentOptions.Left);
                AutoSize(label, 11, 18);
                hud.sporeCards[i] = card.gameObject;
                card.gameObject.SetActive(false);
            }

            var power = SporePanel(t, "Spore Power", corner, new Vector2(health.sizeDelta.x, SporeCard.y));
            hud.sporePowerLabel = f.Text(power.transform, "Label", "MYCELIUM RUSH  12", TextStyle.BodyInk, 22, SdfBattle.Spores.RushColour, Left, new Vector2(14, 0),
                new Vector2(health.sizeDelta.x - 28, 32), TextAlignmentOptions.Left);
            AutoSize(hud.sporePowerLabel, 12, 22);
            hud.sporePower = power.gameObject;
            power.gameObject.SetActive(false);

            hud.sporeTint = f.Shape(t, "Spore Tint", ToonShapeKind.Vignette, GuiPalette.WithAlpha(GuiPalette.Spore, 0), Centre, Vector2.zero, Vector2.zero, s => s.FillAmount = 0.35f);
            Stretch(hud.sporeTint);
            hud.sporeTint.transform.SetSiblingIndex(hud.vignette.transform.GetSiblingIndex() + 1);
        }

        /// <summary>A power's card: the key, then the name; and the line of a power on, as big as the health panel.</summary>
        private static readonly Vector2 SporeCard = new Vector2(165, 44);

        /// <summary>Between the health panel and the spores, and between the cards.</summary>
        private const float SporeGap = 5;

        private ToonShape SporePanel(Transform parent, string name, Vector2 corner, Vector2 size)
        {
            var panel = f.Panel(parent, name, GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomLeft, corner + size * 0.5f, size, 12);
            // Pivoted on its middle, so the full cards' pulse (SporeHud) grows them evenly into the gaps
            panel.rectTransform.pivot = Centre;
            panel.Shading = 0;
            return panel;
        }

        private static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
        }

        /// <summary>
        /// Capture the flag (ticket 21) around the clock: beside it, mirrored, the team score and outside it each flag's status (at
        /// home, carried by a name, or dropped with the seconds until it goes home): [red flag] 0 [clock] 0 [blue flag]; FlagHud moves
        /// the statuses under the clock on a screen too narrow for them. Hidden until needed: a marker over each flag away from home
        /// (pinned to the screen's edge with an arrow when off screen, with its distance), and the banner for what the flags do.
        /// </summary>
        private void Flags(GuiHud hud, RectTransform clock)
        {
            var t = hud.transform;
            var centreY = clock.anchoredPosition.y + (0.5f - clock.pivot.y) * clock.sizeDelta.y;
            var edge = clock.sizeDelta.x * 0.5f + SdfBattle.FlagHud.ScoreGap;
            var row = Object("Team Score", t);
            Place(row.transform, Top, new Vector2(0, centreY), new Vector2(0, 60), Centre);
            var teams = SdfBattle.Teams.All.ToArray();
            hud.teamScores = new TMP_Text[teams.Length];
            hud.flagStatusPanels = new RectTransform[teams.Length];
            hud.flagStatus = new TMP_Text[teams.Length];
            hud.flagMarkers = new RectTransform[teams.Length];
            hud.flagMarkerArrows = new RectTransform[teams.Length];
            hud.flagMarkerLabels = new TMP_Text[teams.Length];
            foreach (var team in teams)
            {
                var i = (int)team;
                var name = SdfBattle.Teams.Name(team);
                var colour = SdfBattle.FlagHud.Colour(team);
                var sign = team == SdfBattle.Team.Red ? -1 : 1;
                hud.teamScores[i] = f.Text(row.transform, $"{name} Score", "0", TextStyle.Title, 52, colour, Centre, new Vector2(sign * (edge + 25), 2), new Vector2(50, 60));

                var panel = f.Shape(t, $"{name} Flag Status", ToonShapeKind.Box, GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), Top,
                    new Vector2(sign * (edge + SdfBattle.FlagHud.StatusGap), centreY), new Vector2(SdfBattle.FlagHud.StatusWidth, 52), s =>
                    {
                        s.CornerRadius = 18;
                        s.Outline = 4;
                        s.Shadow = 5;
                    });
                panel.rectTransform.pivot = new Vector2(team == SdfBattle.Team.Red ? 1 : 0, 0.5f);
                f.Shape(panel.transform, "Pole", ToonShapeKind.Box, GuiPalette.Cream, Left, new Vector2(22, 0), new Vector2(6, 34), s =>
                {
                    s.CornerRadius = 3;
                    s.Outline = 2;
                    s.Shadow = 0;
                });
                f.Shape(panel.transform, "Cloth", ToonShapeKind.Box, colour, Left, new Vector2(27, 7), new Vector2(26, 18), s =>
                {
                    s.CornerRadius = 4;
                    s.Outline = 2.5f;
                    s.Shadow = 0;
                });
                hud.flagStatus[i] = f.Text(panel.transform, "Status", "At home", TextStyle.BodyInk, 24, GuiPalette.Cream, Left, new Vector2(62, 0),
                    new Vector2(SdfBattle.FlagHud.StatusWidth - 74, 44), TextAlignmentOptions.Left);
                // A long carrier's name shrinks to fit rather than run out of the panel
                AutoSize(hud.flagStatus[i], 14, 24);
                hud.flagStatusPanels[i] = panel.rectTransform;

                var marker = Object($"{name} Flag Marker", t);
                Place(marker.transform, Centre, Vector2.zero, new Vector2(40, 40));
                f.Shape(marker.transform, "Dot", ToonShapeKind.Pill, colour, Centre, Vector2.zero, new Vector2(30, 30), s =>
                {
                    s.Outline = 3.5f;
                    s.Shadow = 3;
                });
                var arrow = f.Shape(marker.transform, "Arrow", ToonShapeKind.Arrow, colour, Centre, Vector2.zero, new Vector2(30, 34), s =>
                {
                    s.CornerRadius = 4;
                    s.Outline = 3;
                    s.Shadow = 0;
                });
                arrow.rectTransform.pivot = new Vector2(-0.6f, 0.5f);
                hud.flagMarkerArrows[i] = arrow.rectTransform;
                hud.flagMarkerLabels[i] = f.Text(marker.transform, "Distance", "", TextStyle.BodyInk, 24, GuiPalette.Cream, Centre, new Vector2(0, -34), new Vector2(120, 30));
                hud.flagMarkers[i] = (RectTransform)marker.transform;
                marker.SetActive(false);
            }

            // Under the row, where FlagHud puts it for the screen's width
            var bottom = centreY - clock.sizeDelta.y * 0.5f;
            hud.flagBanner = p.BannerInstance(t, "Flag Banner", "YOU HAVE THE FLAG!", GuiPalette.Amanita, Top, new Vector2(0, bottom - 18), new Vector2(900, 92), 46);
            hud.flagBannerText = hud.flagBanner.GetComponentInChildren<TMP_Text>();
            hud.flagBanner.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- Scoreboard

        private GameObject Scoreboard()
        {
            var root = Root("Scoreboard", GuiScreen.Scoreboard);
            var t = root.transform;
            var board = root.AddComponent<GuiScoreboard>();
            var panel = p.PanelInstance(t, "Panel", GuiPalette.WithAlpha(GuiPalette.Cream, 0.96f), Centre, new Vector2(0, -10), new Vector2(980, 840)).transform;
            p.BannerInstance(panel, "Header", "SCOREBOARD", GuiPalette.Chanterelle, Top, new Vector2(0, 50), new Vector2(560, 100), 54);
            board.subtitle = f.Text(panel, "Subtitle", "MEADOW  ·  FIRST TO 3 CAPTURES  ·  10:00", TextStyle.Label, 28, GuiPalette.Dusk, Top, new Vector2(0, -66), new Vector2(900, 40));
            var columns = Object("Columns", panel);
            Place(columns.transform, Top, new Vector2(0, -110), new Vector2(900, 30));
            f.Text(columns.transform, "Player", "PLAYER", TextStyle.Label, 22, GuiPalette.Dusk, Left, new Vector2(122, 0), new Vector2(200, 30), TextAlignmentOptions.Left);
            f.Text(columns.transform, "Kills", "KILLS", TextStyle.Label, 22, GuiPalette.Dusk, Right, new Vector2(-124, 0), new Vector2(80, 30));
            f.Text(columns.transform, "Status", "STATUS", TextStyle.Label, 22, GuiPalette.Dusk, Right, new Vector2(-14, 0), new Vector2(110, 30));
            var rows = Object("Players", panel);
            Place(rows.transform, Top, new Vector2(0, -146), new Vector2(900, MatchView.ScoreboardRows * 60));
            board.rows = Enumerable.Range(0, MatchView.ScoreboardRows)
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
            // The match stops while this is up (GuiFlow)
            var panel = p.PanelInstance(t, "Panel", GuiPalette.Cream, Centre, new Vector2(0, 0), new Vector2(620, 370)).transform;
            p.BannerInstance(panel, "Header", "PAUSED", GuiPalette.Chanterelle, Top, new Vector2(0, 50), new Vector2(420, 100), 54);
            p.ButtonInstance(panel, "RESUME", GuiPalette.Moss, GuiCommandId.Resume, Top, new Vector2(0, -90), new Vector2(420, 96), 46, spots: 0.5f);
            p.ButtonInstance(panel, "SETTINGS", GuiPalette.Chanterelle, GuiCommandId.Show, Top, new Vector2(0, -210), new Vector2(420, 86), 40, GuiScreen.Settings, 0.4f);
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
                // Seven rows close together, so the last stays clear of the back button
                Place(row.transform, Top, new Vector2(0, y), new Vector2(860, 84));
                f.Text(row.transform, "Label", label, TextStyle.Label, 38, GuiPalette.Ink, Left, Vector2.zero, new Vector2(520, 60), TextAlignmentOptions.Left);
                y -= 86;
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
            menu.dismissQualityHints = Instance<Toggle>(p.Toggle, Row("Dismiss FPS/quality tips"), "Dismiss Quality Hints", Right, new Vector2(-180, 0));
            menu.dismissQualityHints.isOn = false;
            p.ButtonInstance(panel, "BACK", GuiPalette.Dusk, GuiCommandId.Back, Bottom, new Vector2(0, 36), new Vector2(320, 86), 42, spots: 0);
            return Save(root);
        }

        // ---------------------------------------------------------------- End of your round

        private GameObject Death()
        {
            var root = Root("Death", GuiScreen.Death);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.AmanitaDark, 0.6f), 0.45f);
            var result = root.AddComponent<GuiRoundResult>();
            result.death = true;
            result.icons = f.Kit.icons;
            result.title = f.Text(t, "Title", "SPLATTERED!", TextStyle.Title, 150, GuiPalette.Cream, Centre, new Vector2(0, 340), new Vector2(1200, 170));
            result.avatar = Instance<GuiAvatar>(p.Avatar, t, "Killer", Centre, new Vector2(-300, 70), new Vector2(120, 120));
            result.detail = f.Text(t, "Detail", "by Runner-Up\nwith the Sniper", TextStyle.BodyInk, 44, GuiPalette.Cream, Centre, new Vector2(0, 70), new Vector2(460, 130));
            result.weapon = f.Icon(t, "Weapon", "sniper", Centre, new Vector2(330, 70), new Vector2(200, 110));
            result.back = f.Text(t, "Back", "Back in 5", TextStyle.Title, 56, GuiPalette.Gold, Centre, new Vector2(0, -90), new Vector2(600, 70));
            // No stats: the battle has no places (you respawn), and your kills are on the scoreboard
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
            result.title = p.BannerInstance(t, "Title", "ROUND WON!", GuiPalette.Amanita, Centre, new Vector2(0, 360), new Vector2(1180, 130), 76).GetComponentInChildren<TMP_Text>();
            result.detail = f.Text(t, "Detail", "Your team wins", TextStyle.BodyInk, 44, GuiPalette.Cream, Centre, new Vector2(0, -90), new Vector2(1200, 60));

            var stats = f.Panel(t, "Stats", GuiPalette.WithAlpha(GuiPalette.Night, 0.9f), Centre, new Vector2(0, -210), new Vector2(360, 150), 26);
            result.kills = f.Text(stats.transform, "Kills", "5", TextStyle.Title, 76, GuiPalette.Cream, Centre, new Vector2(0, 10), new Vector2(160, 90));
            f.Text(stats.transform, "Kills Label", "KILLS", TextStyle.BodyInk, 24, GuiPalette.Paper, Bottom, new Vector2(0, 12), new Vector2(200, 30));

            p.ButtonInstance(t, "MAIN MENU", GuiPalette.Dusk, GuiCommandId.LeaveMatch, Centre, new Vector2(0, -420), new Vector2(330, 90), 42, spots: 0);
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
            p.ButtonInstance(t, "BACK", GuiPalette.Dusk, GuiCommandId.Back, TopRight, new Vector2(-5, -5), new Vector2(200, 76), 36, spots: 0);

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

            // People: avatars, scoreboard rows, kill feed entries
            var people = Section("Players", new Vector2(300, -790), new Vector2(1570, 260));
            var roster = MatchView.NewRoster(SdfBattle.Cast.You);
            for (var i = 0; i < 6; i++)
            {
                var avatar = Instance<GuiAvatar>(p.Avatar, people, $"Avatar {i + 1}", TopLeft, new Vector2(30 + i * 86, -64));
                avatar.color = roster[i].color;
                avatar.avatar = i % 2;
                avatar.dimmed = i == 5;
                avatar.disc.color = i == 5 ? Color.Lerp(roster[i].color, GuiPalette.Dusk, 0.75f) : roster[i].color;
            }
            var row = Instance<GuiPlayerRow>(p.PlayerRow, people, "Player Row", TopLeft, new Vector2(560, -64), new Vector2(470, 60));
            row.background.color = GuiPalette.Gold;
            row.playerName.text = SdfBattle.Cast.You;
            row.rank.text = "1";
            row.status.text = "YOU";
            var row2 = Instance<GuiPlayerRow>(p.PlayerRow, people, "Player Row Out", TopLeft, new Vector2(560, -140), new Vector2(470, 60));
            row2.background.color = Color.Lerp(GuiPalette.Paper, GuiPalette.Dusk, 0.55f);
            row2.playerName.text = SdfBattle.Cast.Blue[0];
            row2.rank.text = "8";
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
            Add("PICK UP", GuiPalette.SporeDeep, GuiCommandId.TogglePickup);
            Add("RELOAD", GuiPalette.SporeDeep, GuiCommandId.Reload);
            Add("DIE", GuiPalette.AmanitaDark, GuiCommandId.Die);
            Add("WIN", GuiPalette.Chanterelle, GuiCommandId.Win);
            EndRow();

            var state = f.Text(t, "State", "Playing", TextStyle.Body, 18, GuiPalette.Paper, TopLeft, new Vector2(14, y), new Vector2(234, 70), TextAlignmentOptions.TopLeft);
            state.textWrappingMode = TextWrappingModes.Normal;
            var status = state.gameObject.AddComponent<GuiStatus>();
            status.label = state;
            return GuiFactory.Save(root, $"{Folder}/Test Panel.prefab");
        }
    }
}
