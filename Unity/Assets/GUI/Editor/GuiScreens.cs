using System.Collections.Generic;
using System.Linq;
using SdfCampaign;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SdfGui.Editor.GuiFactory;

namespace SdfGui.Editor
{
    /// <summary>
    /// The screens of the GUI as prefabs in Assets/GUI/Prefabs/Screens, each filling the canvas and built from the component prefabs
    /// (<see cref="GuiPrefabs"/>): title, level select, difficulty, confirm, story, HUD, scoreboard, pause, settings, death, the banner and Victory and Defeat,
    /// the developer's gallery of every component and the test panel. Each has a <see cref="GuiScreenRoot"/> for <see cref="GuiFlow"/> and draws from the match through its view.
    /// </summary>
    public sealed class GuiScreens
    {
        public const string Folder = GuiPrefabs.Folder + "/Screens";

        private readonly GuiPrefabs p;
        private readonly GuiFactory f;

        public readonly Dictionary<GuiScreen, GameObject> Screens = new Dictionary<GuiScreen, GameObject>();
        public GameObject TestPanel;
        /// <summary>The touch controls (ticket 56): a layer over the HUD, not a screen of its own (<see cref="GuiScreenRoot.touchLayer"/>).</summary>
        public GameObject TouchControls;

        public GuiScreens(GuiPrefabs prefabs)
        {
            p = prefabs;
            f = prefabs.Factory;
        }

        public void Build()
        {
            Screens[GuiScreen.Hud] = Hud();
            TouchControls = Touch();
            Screens[GuiScreen.Scoreboard] = Scoreboard();
            Screens[GuiScreen.Pause] = Pause();
            Screens[GuiScreen.Settings] = Settings();
            Screens[GuiScreen.Death] = Death();
            Screens[GuiScreen.MatchEnd] = MatchEnd();
            Screens[GuiScreen.Victory] = Victory();
            Screens[GuiScreen.Defeat] = Defeat();
            GuiFactory.EnforceMinimum = false;
            Screens[GuiScreen.Gallery] = Gallery();
            GuiFactory.EnforceMinimum = true;
            Screens[GuiScreen.Loading] = Loading();
            Screens[GuiScreen.Confirm] = Confirm();
            Screens[GuiScreen.Caption] = Caption();
            Screens[GuiScreen.Credits] = Credits();
            GuiFactory.EnforceMinimum = false;
            TestPanel = Test();
            GuiFactory.EnforceMinimum = true;
        }

        private static GameObject Root(string name, GuiScreen screen)
        {
            var root = Object(name, null);
            Stretch(root.transform);
            root.AddComponent<GuiScreenRoot>().screen = screen;
            return root;
        }

        // ---------------------------------------------------------------- Touch layouts (ticket 57)

        /// <summary>A thumb-sized button in the 1920 x 1080 layout: 64 px at 800 x 450.</summary>
        private const float Thumb = GuiTouchLayout.ThumbHeight;

        private static GuiTouchLayout.Entry ForTouch(Component target, Vector2? position = null, Vector2? size = null, float scale = 0, bool hide = false) =>
            new GuiTouchLayout.Entry
            {
                target = (RectTransform)target.transform,
                hide = hide,
                move = position.HasValue,
                position = position ?? default,
                resize = size.HasValue,
                size = size ?? default,
                scale = scale,
            };

        /// <summary>How the screen looks on a touch device: the entries are applied while one is in use (<see cref="GuiTouchLayout"/>).</summary>
        private static void TouchLayout(GameObject root, IEnumerable<GuiTouchLayout.Entry> entries) =>
            root.AddComponent<GuiTouchLayout>().entries = entries.ToArray();

        private static GameObject Save(GameObject root) => GuiFactory.Save(root, $"{Folder}/{root.name}.prefab");

        private ToonShape Dim(Transform parent, Color color, float start = 0.05f)
        {
            var dim = f.Shape(parent, "Dim", ToonShapeKind.Vignette, color, Centre, Vector2.zero, Vector2.zero, s => s.FillAmount = start);
            Stretch(dim);
            dim.raycastTarget = true; // keeps clicks off what is under the menu
            return dim;
        }

        // ---------------------------------------------------------------- Confirm

        // Over Boot's desk and wall (ticket 35) the screens that were a flat purple let the world show through, as dim as the text needs
        private GameObject Confirm()
        {
            var root = Root("Confirm", GuiScreen.Confirm);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.7f), -0.3f);
            var view = root.AddComponent<GuiConfirm>();
            var panel = p.PanelInstance(t, "Panel", GuiPalette.Cream, Centre, Vector2.zero, new Vector2(800, 380)).transform;
            view.question = f.Text(panel, "Question", "Leave the match?\nThe campaign is saved at the start of this arena.", TextStyle.Body, 40, GuiPalette.Ink, Top, new Vector2(0, -50),
                new Vector2(720, 170));
            view.question.textWrappingMode = TextWrappingModes.Normal;
            var yes = p.ButtonInstance(panel, "YES", GuiPalette.Moss, GuiCommandId.Confirm, Bottom, new Vector2(-170, 36), new Vector2(280, 90), 46, spots: 0.5f);
            var no = p.ButtonInstance(panel, "NO", GuiPalette.Dusk, GuiCommandId.Cancel, Bottom, new Vector2(170, 36), new Vector2(280, 90), 46, spots: 0);
            TouchLayout(root, new[]
            {
                ForTouch(panel, size: new Vector2(920, 540)),
                ForTouch(view.question, size: new Vector2(840, 210)),
                ForTouch(yes, new Vector2(-220, 40), new Vector2(380, Thumb)),
                ForTouch(no, new Vector2(220, 40), new Vector2(380, Thumb)),
            });
            return Save(root);
        }

        // ---------------------------------------------------------------- HUD

        private GameObject Hud()
        {
            var root = Root("HUD", GuiScreen.Hud);
            var t = root.transform;
            var hud = root.AddComponent<GuiHud>();

            // Behind everything: the red of being hit, and the splats from where it came
            hud.vignette = f.Shape(t, "Damage Vignette", ToonShapeKind.Vignette, GuiPalette.WithAlpha(GuiPalette.RedDark, 0), Centre, Vector2.zero, Vector2.zero, s => s.FillAmount = 0.35f);
            Stretch(hud.vignette);
            var direction = Object("Damage Direction", t);
            hud.damageDirection = Place(direction.transform, Centre, Vector2.zero, new Vector2(10, 10));
            hud.damageSplats = new[]
            {
                f.Shape(direction.transform, "Splat", ToonShapeKind.Splat, GuiPalette.WithAlpha(GuiPalette.Red, 0), Centre, new Vector2(0, 360), new Vector2(240, 200), s => { s.Seed = 0.3f; s.Shadow = 0; s.Outline = 4; }),
                f.Shape(direction.transform, "Splat Left", ToonShapeKind.Splat, GuiPalette.WithAlpha(GuiPalette.RedDark, 0), Centre, new Vector2(-150, 330), new Vector2(120, 110), s => { s.Seed = 0.7f; s.Shadow = 0; s.Outline = 3; }),
                f.Shape(direction.transform, "Splat Right", ToonShapeKind.Splat, GuiPalette.WithAlpha(GuiPalette.RedDark, 0), Centre, new Vector2(150, 340), new Vector2(100, 90), s => { s.Seed = 0.9f; s.Shadow = 0; s.Outline = 3; }),
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
            hud.fps = f.Text(t, "FPS", "60 FPS", TextStyle.BodyInk, 29, GuiPalette.Moss, TopLeft, new Vector2(10, -5), new Vector2(700, 38), TextAlignmentOptions.Left);

            // Top centre, 5 px under the screen's top: the round's clock, the team score either side and the flags' statuses outside it
            // See-through like the other HUD panels, the time in cream on it
            var clock = f.Panel(t, "Clock", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), Top, new Vector2(0, -5), new Vector2(230, 90), 45);
            clock.Shading = 0;
            hud.clock = f.FillText(clock.transform, "Time", "3:00", TextStyle.Title, 62, GuiPalette.Cream);
            Flags(hud, clock.rectTransform);

            // Top right: the kill feed
            var feed = Object("Kill Feed", t);
            Place(feed.transform, TopRight, new Vector2(-5, -75), new Vector2(700, 380));
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
            hud.eliminatedName = f.Text(popup.transform, "Name", "Big Gerald", TextStyle.BodyInk, 40, GuiPalette.Cream, Bottom, new Vector2(0, 6), new Vector2(700, 56));
            hud.eliminatedPopup = popup;

            var pickup = f.Panel(t, "Pick Up", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), Bottom, new Vector2(0, 220), new Vector2(520, 76), 20);
            var key = f.Shape(pickup.transform, "Key", ToonShapeKind.Box, GuiPalette.Paper, Left, new Vector2(16, 0), new Vector2(56, 56), s => { s.CornerRadius = 12; s.Outline = 4; s.Shadow = 5; });
            f.FillText(key.transform, "Letter", "E", TextStyle.Label, 36, GuiPalette.Ink);
            hud.pickupKey = key.gameObject;
            hud.pickupLabel = f.Text(pickup.transform, "Label", "Pick up Minigun", TextStyle.BodyInk, 34, GuiPalette.Cream, Left, new Vector2(92, 0), new Vector2(410, 60), TextAlignmentOptions.Left);
            hud.pickupPrompt = pickup.gameObject;

            var spectating = p.BannerInstance(t, "Spectating", "SPECTATING", GuiPalette.Dusk, Bottom, new Vector2(0, 120), new Vector2(720, 84), 40);
            spectating.Spots = 0;
            hud.spectating = spectating.gameObject;
            hud.spectatingLabel = spectating.GetComponentInChildren<TMP_Text>();

            // Bottom left: health, its name and then its bar on one line
            var health = f.Panel(t, "Health", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomLeft, new Vector2(5, 5), new Vector2(440, 60), 16);
            f.Text(health.transform, "Label", "HEALTH", TextStyle.BodyInk, 29, GuiPalette.Paper, Left, new Vector2(14, 0), new Vector2(130, 40), TextAlignmentOptions.Left);
            hud.health = p.BarInstance(health.transform, "Bar", GuiPalette.Moss, 0.72f, Left, new Vector2(150, 0), new Vector2(276, 38), 29);
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

        // ---------------------------------------------------------------- Touch controls (ticket 56)

        /// <summary>A touch button's sizes in canvas units (the canvas scales by 450/1080 at 800 x 450: 154 is 64 px, 9 mm on CrazyGames' mobile size).</summary>
        public const float TouchButtonSize = 154, TouchFireSize = 250, TouchSmallFireSize = 170, TouchStickSize = 384, TouchPowerY = 575;

        /// <summary>
        /// The touch controls, a layer over the HUD (<see cref="GuiScreenRoot.touchLayer"/>) shown on a touch device only. The thumbs: left,
        /// the floating joystick (drawn where the finger lands) with the small fire button over it; right, the big fire
        /// button with jump and zoom beside it and the three powers in a row above them; along the bottom the four weapon slots; at the top left the pause and the scoreboard.
        /// Centres are given from the corner they anchor to.
        /// </summary>
        private GameObject Touch()
        {
            var root = Object("Touch Controls", null);
            Stretch(root.transform);
            root.AddComponent<GuiScreenRoot>().touchLayer = true;
            var layer = root.AddComponent<TouchLayer>();
            var t = root.transform;
            var buttons = new List<TouchButton>();

            // The floating joystick: a ring round a faint disc, and the knob; placed by the layer where the thumb lands
            var stick = f.Shape(t, "Stick Base", ToonShapeKind.Ring, GuiPalette.WithAlpha(GuiPalette.Cream, 0.55f), BottomLeft, Vector2.zero, Vector2.one * TouchStickSize, s =>
            {
                s.Thickness = 10;
                s.Outline = 3;
                s.Shadow = 0;
            });
            stick.rectTransform.pivot = Centre;
            var disc = f.Shape(stick.transform, "Disc", ToonShapeKind.Pill, GuiPalette.WithAlpha(GuiPalette.Night, 0.3f), Centre, Vector2.zero, Vector2.one * (TouchStickSize - 24), s =>
            {
                s.Outline = 0;
                s.Shadow = 0;
                s.Shading = 0;
            });
            disc.rectTransform.pivot = Centre;
            var knob = f.Shape(stick.transform, "Knob", ToonShapeKind.Pill, GuiPalette.WithAlpha(GuiPalette.Cream, 0.9f), Centre, Vector2.zero, Vector2.one * 150, s =>
            {
                s.Outline = 5;
                s.Shadow = 6;
            });
            knob.rectTransform.pivot = Centre;
            layer.stickBase = stick.rectTransform;
            layer.stickKnob = knob.rectTransform;
            layer.stickRadius = (TouchStickSize - 24) * 0.5f;

            TouchButton Button(string name, TouchButtonKind kind, int index, Vector2 anchor, Vector2 centre, Vector2 size, Color colour, string text, float textSize)
            {
                var shape = f.Shape(t, name, ToonShapeKind.Pill, colour, anchor, centre, size, s =>
                {
                    s.Outline = 5;
                    s.Shadow = 8;
                });
                shape.rectTransform.pivot = Centre;
                shape.rectTransform.anchoredPosition = centre;
                var button = shape.gameObject.AddComponent<TouchButton>();
                button.kind = kind;
                button.index = index;
                button.shape = shape;
                button.rest = colour;
                if (!string.IsNullOrEmpty(text))
                    button.label = f.FillText(shape.transform, "Label", text, TextStyle.Title, textSize, GuiPalette.Cream);
                buttons.Add(button);
                return button;
            }

            // Right thumb: fire under it, jump and zoom beside it
            Button("Fire", TouchButtonKind.Fire, 0, BottomRight, new Vector2(-200, 330), Vector2.one * TouchFireSize, GuiPalette.Red, "FIRE", 62).shape.Spots = 0.4f;
            Button("Jump", TouchButtonKind.Jump, 0, BottomRight, new Vector2(-500, 150), Vector2.one * TouchSmallFireSize, GuiPalette.Amber, "JUMP", 40);
            Button("Zoom", TouchButtonKind.Zoom, 0, BottomRight, new Vector2(-480, 390), Vector2.one * TouchSmallFireSize, GuiPalette.Sky, "ZOOM", 38);

            // Left thumb: a second, smaller fire button over the joystick
            Button("Fire Left", TouchButtonKind.FireLeft, 0, BottomLeft, new Vector2(150, 600), Vector2.one * TouchSmallFireSize, GuiPalette.RedDark, "FIRE", 40);

            // The powers (shown when the rage meter is full): a box each, in the power's colour, in a row above the right thumb's buttons.
            // Not over the rage meter at the lower left: that is the joystick's ground, and a thumb landing there must not fire a power
            // (ticket 57); not under the kill feed at the top right either.
            var powers = SdfBattle.SporeRules.All;
            for (var i = 0; i < powers.Length; i++)
            {
                var power = Button($"Power {powers[i]}", TouchButtonKind.Power, i, BottomRight, new Vector2(-(105 + (powers.Length - 1 - i) * 205), TouchPowerY), new Vector2(200, TouchButtonSize),
                    SdfBattle.Spores.ColourOf(powers[i]), "", 0);
                power.label = f.FillText(power.transform, "Label", SdfBattle.SporeRules.NameOf(powers[i]), TextStyle.Label, 34, GuiPalette.Ink);
                AutoSize(power.label, 29, 34);
            }

            // The four weapon slots along the bottom, mirroring the HUD's own slot boxes
            for (var i = 0; i < SdfBattle.Inventory.SlotCount; i++)
            {
                var slot = Button($"Slot {i + 1}", TouchButtonKind.Slot, i, Bottom, new Vector2(-246 + i * 164, 82), Vector2.one * TouchButtonSize,
                    GuiPalette.WithAlpha(GuiPalette.Night, 0.55f), "", 0);
                slot.shape.Kind = ToonShapeKind.Box;
                slot.shape.CornerRadius = 22;
                slot.icon = f.Icon(slot.transform, "Icon", null, Centre, Vector2.zero, Vector2.zero);
                Stretch(slot.icon);
                slot.icon.rectTransform.offsetMin = new Vector2(12, 10);
                slot.icon.rectTransform.offsetMax = new Vector2(-10, -22);
                slot.icon.enabled = false;
                slot.label = f.Text(slot.transform, "Number", (i + 1).ToString(), TextStyle.Label, 34, GuiPalette.Ink, TopLeft, new Vector2(12, -6), new Vector2(80, 44), TextAlignmentOptions.TopLeft);
            }

            // Top left: pause (two bars) and the scoreboard (held)
            var pause = Button("Pause", TouchButtonKind.Pause, 0, TopLeft, new Vector2(82, -82), Vector2.one * TouchButtonSize, GuiPalette.Dusk, "", 0);
            foreach (var x in new[] { -20f, 20f })
                f.Shape(pause.transform, "Bar", ToonShapeKind.Pill, GuiPalette.Cream, Centre, new Vector2(x, 0), new Vector2(20, 64), s =>
                {
                    s.Outline = 3;
                    s.Shadow = 0;
                    s.Shading = 0;
                });
            Button("Scoreboard", TouchButtonKind.Scoreboard, 0, TopLeft, new Vector2(260, -82), Vector2.one * TouchButtonSize, GuiPalette.Dusk, "SCORE", 36);

            layer.buttons = buttons.ToArray();
            return Save(root);
        }

        /// <summary>
        /// The four weapon slots (ticket 22) in a panel at the bottom right, each a key box with the weapon's icon and its number; the
        /// name of the one in hand over them. Shown as a round starts: the pistol in hand in slot 1, the rest empty.
        /// </summary>
        private void Weapons(GuiHud hud)
        {
            var panel = f.Panel(hud.transform, "Weapons", GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), BottomRight, new Vector2(-5, 5), new Vector2(430, 164), 24);
            hud.weaponLabel = f.Text(panel.transform, "Label", "PISTOL", TextStyle.BodyInk, 29, GuiPalette.Paper, TopRight, new Vector2(-22, -10), new Vector2(390, 38), TextAlignmentOptions.Right);
            var count = SdfBattle.Inventory.SlotCount;
            hud.weaponSlots = new ToonShape[count];
            hud.weaponIcons = new Image[count];
            hud.weaponNumbers = new TMP_Text[count];
            for (var i = 0; i < count; i++)
            {
                var held = i == 0;
                var box = f.Shape(panel.transform, $"Slot {i + 1}", ToonShapeKind.Box, held ? GuiPalette.Gold : GuiPalette.WithAlpha(GuiPalette.Night, 0.55f),
                    Centre, new Vector2(-153 + i * 102, -18), new Vector2(88, 88), s => { s.CornerRadius = 12; s.Outline = 4; s.Shadow = 5; });
                if (held)
                    box.transform.localScale = Vector3.one * 1.12f;
                var icon = f.Icon(box.transform, "Icon", held ? "pistol" : null, Centre, Vector2.zero, Vector2.zero);
                Stretch(icon);
                icon.rectTransform.offsetMin = new Vector2(8, 6);
                icon.rectTransform.offsetMax = new Vector2(-6, -10);
                icon.enabled = held;
                var number = f.Text(box.transform, "Number", (i + 1).ToString(), TextStyle.Label, 29, held ? GuiPalette.Ink : GuiPalette.WithAlpha(GuiPalette.Paper, 0.6f),
                    TopLeft, new Vector2(7, -3), new Vector2(70, 38), TextAlignmentOptions.TopLeft);
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
            f.Text(meter.transform, "Label", "RAGE", TextStyle.BodyInk, 29, GuiPalette.Paper, Left, new Vector2(14, 0), new Vector2(130, 40), TextAlignmentOptions.Left);
            hud.sporeBar = p.BarInstance(meter.transform, "Bar", GuiPalette.Rage, 0, Left, new Vector2(150, 0), new Vector2(276, 38), 29);
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
                var key = f.Shape(card.transform, "Key", ToonShapeKind.Box, SdfBattle.Spores.ColourOf(powers[i]), Left, new Vector2(8, 0), new Vector2(40, 40), s =>
                {
                    s.CornerRadius = 7;
                    s.Outline = 2;
                    s.Shadow = 2;
                    s.Shading = 0;
                });
                hud.sporeLetters[i] = f.FillText(key.transform, "Letter", SdfBattle.Spores.Keys[i].ToString(), TextStyle.Label, 29, GuiPalette.Ink);
                var label = f.Text(card.transform, "Label", SdfBattle.SporeRules.NameOf(powers[i]), TextStyle.BodyInk, 29, GuiPalette.Cream, Left, new Vector2(56, 0),
                    new Vector2(SporeCard.x - 62, 42), TextAlignmentOptions.Left);
                AutoSize(label, 11, 29);
                hud.sporeCards[i] = card.gameObject;
                card.gameObject.SetActive(false);
            }

            var power = SporePanel(t, "Spore Power", corner, new Vector2(health.sizeDelta.x, SporeCard.y));
            hud.sporePowerLabel = f.Text(power.transform, "Label", "FRENZY  12", TextStyle.BodyInk, 29, SdfBattle.Spores.FrenzyColour, Left, new Vector2(14, 0),
                new Vector2(health.sizeDelta.x - 28, 42), TextAlignmentOptions.Left);
            AutoSize(hud.sporePowerLabel, 12, 29);
            hud.sporePower = power.gameObject;
            power.gameObject.SetActive(false);

            hud.sporeTint = f.Shape(t, "Spore Tint", ToonShapeKind.Vignette, GuiPalette.WithAlpha(GuiPalette.Rage, 0), Centre, Vector2.zero, Vector2.zero, s => s.FillAmount = 0.35f);
            Stretch(hud.sporeTint);
            hud.sporeTint.transform.SetSiblingIndex(hud.vignette.transform.GetSiblingIndex() + 1);
        }

        /// <summary>A power's card: the key, then the name; and the line of a power on, as big as the health panel.</summary>
        private static readonly Vector2 SporeCard = new Vector2(215, 56);

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
            text.fontSizeMin = GuiFactory.Readable(min);
            text.fontSizeMax = GuiFactory.Readable(max);
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
                    new Vector2(sign * (edge + SdfBattle.FlagHud.StatusGap), centreY), new Vector2(SdfBattle.FlagHud.StatusWidth, 60), s =>
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
                hud.flagStatus[i] = f.Text(panel.transform, "Status", "At home", TextStyle.BodyInk, 29, GuiPalette.Cream, Left, new Vector2(62, 0),
                    new Vector2(SdfBattle.FlagHud.StatusWidth - 74, 44), TextAlignmentOptions.Left);
                // A long carrier's name shrinks to fit rather than run out of the panel
                AutoSize(hud.flagStatus[i], 14, 29);
                hud.flagStatus[i].overflowMode = TextOverflowModes.Ellipsis;
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
                hud.flagMarkerLabels[i] = f.Text(marker.transform, "Distance", "", TextStyle.BodyInk, 29, GuiPalette.Cream, Centre, new Vector2(0, -36), new Vector2(140, 38));
                hud.flagMarkers[i] = (RectTransform)marker.transform;
                marker.SetActive(false);
            }

            // Under the row, where FlagHud puts it for the screen's width
            var bottom = centreY - clock.sizeDelta.y * 0.5f;
            hud.flagBanner = p.BannerInstance(t, "Flag Banner", "YOU HAVE THE FLAG!", GuiPalette.Red, Top, new Vector2(0, bottom - 18), new Vector2(760, 92), 46);
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
            p.BannerInstance(panel, "Header", "SCOREBOARD", GuiPalette.Amber, Top, new Vector2(0, 50), new Vector2(560, 100), 54);
            board.subtitle = f.Text(panel, "Subtitle", "MEADOW  ·  FIRST TO 3 CAPTURES  ·  10:00", TextStyle.Label, 29, GuiPalette.Dusk, Top, new Vector2(0, -64), new Vector2(900, 40));
            var columns = Object("Columns", panel);
            Place(columns.transform, Top, new Vector2(0, -108), new Vector2(900, 38));
            f.Text(columns.transform, "Player", "PLAYER", TextStyle.Label, 29, GuiPalette.Dusk, Left, new Vector2(122, 0), new Vector2(200, 38), TextAlignmentOptions.Left);
            f.Text(columns.transform, "Kills", "KILLS", TextStyle.Label, 29, GuiPalette.Dusk, Right, new Vector2(-310, 0), new Vector2(100, 38));
            f.Text(columns.transform, "Deaths", "DEATHS", TextStyle.Label, 29, GuiPalette.Dusk, Right, new Vector2(-165, 0), new Vector2(130, 38));
            f.Text(columns.transform, "Captures", "CAPTURES", TextStyle.Label, 29, GuiPalette.Dusk, Right, new Vector2(5, 0), new Vector2(160, 38));
            var rows = Object("Players", panel);
            Place(rows.transform, Top, new Vector2(0, -150), new Vector2(900, MatchView.ScoreboardRows * 60));
            board.rows = Enumerable.Range(0, MatchView.ScoreboardRows)
                .Select(i => Instance<GuiPlayerRow>(p.PlayerRow, rows.transform, $"Player {i + 1}", Top, new Vector2(0, -i * 60), new Vector2(900, 52)))
                .ToArray();
            board.hint = f.Text(panel, "Hint", "hold TAB", TextStyle.Body, 29, GuiPalette.Dusk, Bottom, new Vector2(0, 14), new Vector2(300, 38));
            return Save(root);
        }

        // ---------------------------------------------------------------- Pause and settings

        private GameObject Pause()
        {
            var root = Root("Pause", GuiScreen.Pause);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.75f), -0.3f);
            // The match stops while this is up (GuiFlow)
            var panel = p.PanelInstance(t, "Panel", GuiPalette.Cream, Centre, new Vector2(0, 0), new Vector2(620, 640)).transform;
            p.BannerInstance(panel, "Header", "PAUSED", GuiPalette.Amber, Top, new Vector2(0, 50), new Vector2(420, 100), 54);
            var resume = p.ButtonInstance(panel, "RESUME", GuiPalette.Moss, GuiCommandId.Resume, Top, new Vector2(0, -90), new Vector2(420, 96), 46, spots: 0.5f);
            // The two that throw the match away ask first
            var restart = p.ButtonInstance(panel, "RESTART", GuiPalette.Red, GuiCommandId.Restart, Top, new Vector2(0, -210), new Vector2(420, 86), 40, spots: 0.4f);
            restart.GetComponent<GuiCommand>().confirm = "Restart this match?";
            var settings = p.ButtonInstance(panel, "SETTINGS", GuiPalette.Amber, GuiCommandId.Show, Top, new Vector2(0, -312), new Vector2(420, 86), 40, GuiScreen.Settings, 0.4f);
            // The credits roll over the paused match and come back here (ticket 42)
            var credits = p.ButtonInstance(panel, "CREDITS", GuiPalette.Moss, GuiCommandId.Credits, Top, new Vector2(0, -414), new Vector2(420, 86), 40, spots: 0.3f);
            var quit = p.ButtonInstance(panel, "QUIT TO MENU", GuiPalette.Dusk, GuiCommandId.QuitToMenu, Top, new Vector2(0, -516), new Vector2(420, 86), 40, spots: 0);
            quit.GetComponent<GuiCommand>().confirm = "Leave the match?";
            // On a touch device: RESUME across the top, the other four in two columns, every button 64 px tall
            TouchLayout(root, new[]
            {
                ForTouch(panel, size: new Vector2(1100, 640)),
                ForTouch(resume, new Vector2(0, -80), new Vector2(900, Thumb)),
                ForTouch(restart, new Vector2(-230, -258), new Vector2(440, Thumb)),
                ForTouch(settings, new Vector2(230, -258), new Vector2(440, Thumb)),
                ForTouch(credits, new Vector2(-230, -436), new Vector2(440, Thumb)),
                ForTouch(quit, new Vector2(230, -436), new Vector2(440, Thumb)),
            });
            return Save(root);
        }

        private GameObject Settings()
        {
            var root = Root("Settings", GuiScreen.Settings);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.75f), -0.3f);
            var menu = root.AddComponent<GuiSettingsMenu>();
            var panel = p.PanelInstance(t, "Panel", GuiPalette.Cream, Centre, new Vector2(0, -20), new Vector2(980, 1030)).transform;
            p.BannerInstance(panel, "Header", "SETTINGS", GuiPalette.Amber, Top, new Vector2(0, 50), new Vector2(500, 104), 58);

            // The rows scroll in a viewport (ticket 57): on a desktop all fit, on a touch device they are thumb-sized and a swipe moves them
            var viewport = Object("Rows", panel);
            Place(viewport.transform, Top, new Vector2(0, -104), new Vector2(980, 804));
            viewport.AddComponent<RectMask2D>();
            var catcher = viewport.AddComponent<Image>();
            catcher.color = Color.clear;
            var content = Object("Content", viewport.transform);
            Place(content.transform, Top, Vector2.zero, new Vector2(980, 784));
            var contentCatcher = content.AddComponent<Image>();
            contentCatcher.color = Color.clear;
            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = (RectTransform)content.transform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;

            var y = 0f;
            var touchRows = new List<(Transform row, Component control, float scale, bool shown)>();
            Transform Row(string label, bool touchShown = true)
            {
                var row = Object(label, content.transform);
                // Fourteen rows close together (the last two for touch devices), so the last stays clear of the back button
                Place(row.transform, Top, new Vector2(0, y), new Vector2(860, 56));
                f.Text(row.transform, "Label", label, TextStyle.Label, 38, GuiPalette.Ink, Left, Vector2.zero, new Vector2(520, 56), TextAlignmentOptions.Left);
                y -= 56;
                touchRows.Add((row.transform, null, 1, touchShown));
                return row.transform;
            }
            // The control just built sits in the last row; on a touch device it grows with the row
            T Control<T>(T control, float scale) where T : Component
            {
                var last = touchRows[touchRows.Count - 1];
                touchRows[touchRows.Count - 1] = (last.row, control, scale, last.shown);
                return control;
            }
            // Your head (ticket 47): the cycle in the panel, the turning preview and its caption beside it
            menu.head = Control(Instance<GuiSelector>(p.Selector, Row("Head"), "Head", Right, Vector2.zero), 1.5f);
            var previewObject = Object("Head Preview", t);
            Place(previewObject.transform, Centre, new Vector2(-720, 60), new Vector2(400, 500));
            previewObject.AddComponent<UnityEngine.UI.RawImage>().raycastTarget = false;
            menu.headPreview = previewObject.AddComponent<GuiHeadPreview>();
            menu.headCaption = f.Text(t, "Head Caption", "October", TextStyle.Label, 40, GuiPalette.Cream, Centre, new Vector2(-720, -230), new Vector2(440, 60));
            menu.quality = Control(Instance<GuiSelector>(p.Selector, Row("Quality"), "Quality", Right, Vector2.zero), 1.5f);
            menu.gore = Control(Instance<GuiSelector>(p.Selector, Row("Gore"), "Gore", Right, Vector2.zero), 1.5f);
            menu.gore.options = GuiSettings.GoreLevels;
            menu.gore.index = 1;
            menu.gore.label.text = GuiSettings.GoreLevels[1];
            // The mouse's sensitivity means nothing on a touch device (its row is hidden there; "Touch look" is its row)
            menu.sensitivity = Instance<Slider>(p.Slider, Row("Mouse sensitivity", touchShown: false), "Sensitivity", Right, new Vector2(-10, 0));
            menu.volume = Control(Instance<Slider>(p.Slider, Row("Master volume"), "Volume", Right, new Vector2(-10, 0)), 1.5f);
            menu.volume.fillRect.GetComponent<ToonShape>().color = GuiPalette.Sky;
            menu.music = Control(Instance<Slider>(p.Slider, Row("Music"), "Music", Right, new Vector2(-10, 0)), 1.5f);
            menu.music.fillRect.GetComponent<ToonShape>().color = GuiPalette.Sky;
            menu.sound = Control(Instance<Slider>(p.Slider, Row("Sound effects"), "Sound", Right, new Vector2(-10, 0)), 1.5f);
            menu.sound.fillRect.GetComponent<ToonShape>().color = GuiPalette.Sky;
            menu.screenShake = Control(Instance<Toggle>(p.Toggle, Row("Screen shake"), "Screen Shake", Right, new Vector2(-180, 0)), 1.6f);
            menu.showFps = Control(Instance<Toggle>(p.Toggle, Row("Show FPS"), "Show FPS", Right, new Vector2(-180, 0)), 1.6f);
            menu.showFps.isOn = false;
            menu.dismissQualityHints = Control(Instance<Toggle>(p.Toggle, Row("Disable auto quality adjustment"), "Dismiss Quality Hints", Right, new Vector2(-180, 0)), 1.6f);
            menu.dismissQualityHints.isOn = false;
            // Touch devices only: how far a swipe turns the view, and the auto-fire
            var touchLookRow = Row("Touch look");
            menu.touchLook = Control(Instance<Slider>(p.Slider, touchLookRow, "Touch Look", Right, new Vector2(-10, 0)), 1.5f);
            var autoFireRow = Row("Auto-fire");
            menu.autoFire = Control(Instance<Toggle>(p.Toggle, autoFireRow, "Auto Fire", Right, new Vector2(-180, 0)), 1.6f);
            menu.autoFire.isOn = true;
            menu.touchRows = new[] { touchLookRow.gameObject, autoFireRow.gameObject };
            ((RectTransform)content.transform).sizeDelta = new Vector2(980, -y);
            // BACK on the left, RESET ALL PROGRESS (ticket 63) on the right in danger red; the yes/no card asks first
            var back = p.ButtonInstance(panel, "BACK", GuiPalette.Dusk, GuiCommandId.Back, Bottom, new Vector2(-235, 36), new Vector2(280, 86), 42, spots: 0);
            var reset = p.ButtonInstance(panel, "RESET ALL PROGRESS", GuiPalette.RedDark, GuiCommandId.ResetProgress, Bottom, new Vector2(155, 36), new Vector2(440, 86), 34, spots: 0);
            reset.GetComponent<GuiCommand>().confirm = GuiFlow.ResetQuestion;

            // On a touch device: a wider panel, rows 110 apart with the controls scaled up (the viewport scrolls), no mouse row, no head
            // preview beside the panel, and a 64 px BACK
            const float touchRow = 110;
            var entries = new List<GuiTouchLayout.Entry>
            {
                ForTouch(panel, new Vector2(0, -45), new Vector2(1500, 930)),
                ForTouch(viewport.transform, size: new Vector2(1500, 620)),
                ForTouch(back, new Vector2(-420, 40), new Vector2(520, Thumb)),
                ForTouch(reset, new Vector2(300, 40), new Vector2(760, Thumb)),
                ForTouch(previewObject.transform, hide: true),
                ForTouch(menu.headCaption, hide: true),
            };
            var shownRows = 0;
            foreach (var (row, control, scale, shown) in touchRows)
            {
                if (!shown)
                {
                    entries.Add(ForTouch(row, hide: true));
                    continue;
                }
                entries.Add(ForTouch(row, new Vector2(0, -shownRows * touchRow), new Vector2(1380, touchRow)));
                if (control != null)
                    entries.Add(ForTouch(control, scale: scale));
                shownRows++;
            }
            entries.Add(ForTouch(content.transform, Vector2.zero, new Vector2(1500, shownRows * touchRow)));
            TouchLayout(root, entries);
            return Save(root);
        }

        // ---------------------------------------------------------------- End of your round

        private GameObject Death()
        {
            var root = Root("Death", GuiScreen.Death);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.RedDark, 0.6f), 0.45f);
            var result = root.AddComponent<GuiRoundResult>();
            result.icons = f.Kit.icons;
            result.title = f.Text(t, "Title", "SPLATTERED!", TextStyle.Title, 150, GuiPalette.Cream, Centre, new Vector2(0, 340), new Vector2(1200, 170));
            result.avatar = Instance<GuiAvatar>(p.Avatar, t, "Killer", Centre, new Vector2(-300, 70), new Vector2(120, 120));
            result.detail = f.Text(t, "Detail", "by Big Gerald\nwith the Sniper", TextStyle.BodyInk, 44, GuiPalette.Cream, Centre, new Vector2(0, 70), new Vector2(460, 130));
            result.weapon = f.Icon(t, "Weapon", "sniper", Centre, new Vector2(330, 70), new Vector2(200, 110));
            result.back = f.Text(t, "Back", "Back in 5", TextStyle.Title, 56, GuiPalette.Gold, Centre, new Vector2(0, -90), new Vector2(600, 70));
            // No stats: the battle has no places (you respawn), and your kills are on the scoreboard
            return Save(root);
        }

        // ---------------------------------------------------------------- End of the match (ticket 32.6)

        // The 2 s banner over the HUD: RED WINS! or BLUE WINS!, coloured by GuiEndBanner
        private GameObject MatchEnd()
        {
            var root = Root("Match End", GuiScreen.MatchEnd);
            var t = root.transform;
            var end = root.AddComponent<GuiEndBanner>();
            var banner = p.BannerInstance(t, "Banner", "RED WINS!", GuiPalette.Red, Centre, new Vector2(0, -110), new Vector2(1000, 160), 100);
            end.shape = banner;
            end.label = banner.GetComponentInChildren<TMP_Text>();
            return Save(root);
        }

        // The arena and the score under the banner, and your numbers in a panel; both end screens have them
        private void EndNumbers(Transform t, GuiEndScreen end, float scoreY, float statsY)
        {
            end.arena = f.Text(t, "Arena", "MEADOW", TextStyle.Title, 46, GuiPalette.Gold, Centre, new Vector2(0, scoreY + 120), new Vector2(1200, 60));
            end.score = f.Text(t, "Score", "3 – 1", TextStyle.Title, 150, GuiPalette.Cream, Centre, new Vector2(0, scoreY), new Vector2(1200, 170));
            var stats = f.Panel(t, "Stats", GuiPalette.WithAlpha(GuiPalette.Night, 0.92f), Centre, new Vector2(0, statsY), new Vector2(1100, 130), 26);
            TMP_Text Stat(string label, float x)
            {
                var number = f.Text(stats.transform, label, "0", TextStyle.Title, 64, GuiPalette.Cream, Centre, new Vector2(x, 14), new Vector2(240, 80));
                f.Text(number.transform, label + " Label", label.ToUpperInvariant(), TextStyle.BodyInk, 24, GuiPalette.Paper, Centre, new Vector2(0, -52), new Vector2(240, 30));
                return number;
            }
            end.captures = Stat("Captures", -390);
            end.kills = Stat("Kills", -130);
            end.deaths = Stat("Deaths", 130);
            end.time = Stat("Time", 390);
        }

        private GameObject Victory()
        {
            var root = Root("Victory", GuiScreen.Victory);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.85f), -0.2f);
            var end = root.AddComponent<GuiEndScreen>();
            end.victory = true;
            f.Shape(t, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiPalette.Gold, 0.4f), Centre, new Vector2(0, 80), new Vector2(1500, 1000));
            var card = EndContent(t);
            p.BannerInstance(card, "Title", "VICTORY!", GuiPalette.Moss, Top, new Vector2(0, -95), new Vector2(1100, 150), 100);
            EndNumbers(card, end, 160, -300);
            CountdownCard(card, end);
            return Save(root);
        }

        private GameObject Defeat()
        {
            var root = Root("Defeat", GuiScreen.Defeat);
            var t = root.transform;
            Dim(t, GuiPalette.WithAlpha(GuiPalette.Night, 0.88f), -0.2f);
            var end = root.AddComponent<GuiEndScreen>();
            end.victory = false;
            f.Shape(t, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiEndScreen.Blue, 0.35f), Centre, new Vector2(0, 80), new Vector2(1500, 1000));
            var card = EndContent(t);
            p.BannerInstance(card, "Title", "DEFEATED", GuiEndScreen.Blue, Top, new Vector2(0, -95), new Vector2(1100, 150), 100);
            EndNumbers(card, end, 160, -230);
            end.tip = f.Text(card, "Tip", "TIP", TextStyle.BodyInk, 34, GuiPalette.Cream, Centre, new Vector2(0, -345), new Vector2(1100, 50));
            CountdownCard(card, end);
            return Save(root);
        }

        // The end card's countdown (ticket 48): the next level and the difficulty line under the stats, the countdown at the bottom, and
        // your head on the left with its ◀ ▶ and the NEW reveal above it. The keys (A and D) are read by GuiEndScreen.
        private void CountdownCard(Transform t, GuiEndScreen end)
        {
            end.nextLevel = f.Text(t, "Next Level", "NEXT  KITCHEN", TextStyle.Title, 46, GuiPalette.Gold, Centre, new Vector2(0, -405), new Vector2(1200, 56));
            end.difficultyLine = f.Text(t, "Difficulty", "Tougher opponents next", TextStyle.BodyInk, 34, GuiPalette.Cream, Centre, new Vector2(0, -452), new Vector2(1200, 46));
            end.countdownLine = f.Text(t, "Countdown", "Next level in 10…", TextStyle.Title, 52, GuiPalette.Cream, Centre, new Vector2(0, -505), new Vector2(1000, 60));

            var head = Object("Head Preview", t);
            Place(head.transform, Centre, new Vector2(-760, 0), new Vector2(240, 300));
            head.AddComponent<UnityEngine.UI.RawImage>().raycastTarget = false;
            end.headPreview = head.AddComponent<GuiHeadPreview>();
            var previous = HeadArrow(t, "Previous Head", new Vector2(-840, -200), 180, GuiCommandId.HeadPrevious);
            var next = HeadArrow(t, "Next Head", new Vector2(-680, -200), 0, GuiCommandId.HeadNext);
            // The head's name and the NEW reveal shrink to their box (the longest, Glowing Jack-o'-Lantern, is wider than the column),
            // and the reveal is two lines, NEW HEAD! over the name (a line break in its text, no wrapping, so a hyphened name stays on one line)
            end.headCaption = Fitted(f.Text(t, "Head Caption", "PUMPKIN", TextStyle.Label, 36, GuiPalette.Cream, Centre, new Vector2(-760, -262), new Vector2(360, 50)), 20);
            end.keyHint = Fitted(f.Text(t, "Head Keys", "A and D change head", TextStyle.BodyInk, 26, GuiPalette.Cream, Centre, new Vector2(-760, -312), new Vector2(360, 40)), 18);
            end.newHead = Fitted(f.Text(t, "New Head", "NEW HEAD!", TextStyle.Title, 34, GuiPalette.Gold, Centre, new Vector2(-760, 222), new Vector2(360, 96)), 20);
            // On a touch device the arrows are thumb-sized (58 x 3 = 174 units, 72 px at 800 x 450), with the caption and hint under them
            TouchLayout(t.parent.gameObject, new[]
            {
                ForTouch(previous, new Vector2(-890, -215), scale: 3f),
                ForTouch(next, new Vector2(-630, -215), scale: 3f),
                ForTouch(end.headCaption, new Vector2(-760, -345)),
                ForTouch(end.keyHint, new Vector2(-760, -395)),
            });
        }

        // The end card's content, laid out at 1920 x 1080 and scaled down to fit the screen (GuiFitInside), so nothing leaves the screen
        // on a narrower (16:10) or a shorter (a wide browser window) canvas. The dim and the glow behind it stay full-screen.
        private static Transform EndContent(Transform parent)
        {
            var card = Object("Card", parent);
            Place(card.transform, Centre, Vector2.zero, new Vector2(1920, 1080));
            card.AddComponent<GuiFitInside>();
            return card.transform;
        }

        // A text that shrinks to fit its box, down to minSize, instead of running past it
        private static TMP_Text Fitted(TMP_Text label, float minSize)
        {
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = GuiFactory.Readable(minSize);
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        // A head's ◀ or ▶: the selector's arrow, a button running a command (the head before or after the current one)
        private RectTransform HeadArrow(Transform parent, string name, Vector2 position, float angle, GuiCommandId command)
        {
            var holder = Object(name, parent);
            Place(holder.transform, Centre, position, new Vector2(58, 58), Centre);
            var arrow = f.Shape(holder.transform, "Arrow", ToonShapeKind.Arrow, GuiPalette.Amber, Centre, Vector2.zero, new Vector2(40, 46), s =>
            {
                s.CornerRadius = 6;
                s.Outline = 4.5f;
                s.Shadow = 0;
                s.raycastTarget = true;
            });
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
            var button = holder.AddComponent<UnityEngine.UI.Button>();
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.targetGraphic = arrow;
            holder.AddComponent<ToonButton>().shape = arrow;
            var click = holder.AddComponent<GuiCommand>();
            click.command = command;
            click.screen = GuiScreen.Hud;
            return (RectTransform)holder.transform;
        }

        // ---------------------------------------------------------------- The logo

        /// <summary>
        /// The game's logo (ticket 50): MINIGUN over MONSTERS on a rage-red splat with a violet halo, and glints on the letters. Named "Logo" so the covers can lift it by name; <paramref name="scale"/> shrinks it from its top edge.
        /// </summary>
        private GameObject Logo(Transform parent, Vector2 position, float scale)
        {
            var logo = Object("Logo", parent);
            Place(logo.transform, Top, position, new Vector2(1100, 430));
            f.Shape(logo.transform, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiPalette.Violet, 0.4f), Centre, new Vector2(0, -10), new Vector2(1300, 520));
            f.Shape(logo.transform, "Splat", ToonShapeKind.Burst, GuiPalette.Rage, Centre, new Vector2(0, 20), new Vector2(1040, 360), s =>
            {
                s.Outline = 9;
                s.Shadow = 14;
                s.Seed = 3.3f;
            });
            f.LogoWord(logo.transform, "Minigun", "MINIGUN", 128, GuiPalette.Hex(0xFFFAEA), GuiPalette.Hex(0xFFDB7A), new Vector2(0, 66), new Vector2(980, 150), 22, 6, 6);
            f.LogoWord(logo.transform, "Monsters", "MONSTERS", 112, GuiPalette.Hex(0xB6F36B), GuiPalette.Hex(0x3FA34D), new Vector2(0, -56), new Vector2(980, 135), 16, 5, 5);
            f.Shape(logo.transform, "Glint 1", ToonShapeKind.Burst, Color.white, Centre, new Vector2(-292, 118), new Vector2(34, 34), s => { s.Outline = 0; s.Shadow = 0; s.Shading = 0; s.Seed = 1.4f; });
            f.Shape(logo.transform, "Glint 2", ToonShapeKind.Burst, Color.white, Centre, new Vector2(352, 0), new Vector2(26, 26), s => { s.Outline = 0; s.Shadow = 0; s.Shading = 0; s.Seed = 2.7f; });
            f.Shape(logo.transform, "Glint 3", ToonShapeKind.Burst, Color.white, Centre, new Vector2(-396, -40), new Vector2(22, 22), s => { s.Outline = 0; s.Shadow = 0; s.Shading = 0; s.Seed = 0.6f; });
            logo.transform.localRotation = Quaternion.Euler(0, 0, 1.5f);
            logo.transform.localScale = Vector3.one * scale;
            return logo;
        }

        // ---------------------------------------------------------------- Loading: the briefing

        private GameObject Loading()
        {
            var root = Root("Loading", GuiScreen.Loading);
            var t = root.transform;
            var loading = root.AddComponent<GuiLoading>();
            var backdrop = f.Shape(t, "Backdrop", ToonShapeKind.Box, GuiPalette.Night, Centre, Vector2.zero, Vector2.zero, s =>
            {
                s.Outline = 0;
                s.Shadow = 0;
                s.Shading = 0;
                s.CornerRadius = 0;
                s.raycastTarget = true;
            });
            Stretch(backdrop);
            f.Shape(t, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiPalette.Cyan, 0.18f), Centre, new Vector2(0, 80), new Vector2(1500, 900));
            Logo(t, new Vector2(0, -8), 0.42f);
            var banner = p.BannerInstance(t, "Arena Banner", "ARENA", GuiPalette.Red, Top, new Vector2(0, -232), new Vector2(900, 104), 64);
            loading.arena = banner.GetComponentInChildren<TMP_Text>();
            loading.goal = f.Text(t, "Goal", "CAPTURE THE FLAG", TextStyle.BodyInk, 42, GuiPalette.Gold, Top, new Vector2(0, -352), new Vector2(1400, 58));

            var red = p.PanelInstance(t, "Red Roster", GuiPalette.Cream, Centre, new Vector2(-340, -62), new Vector2(520, 360));
            loading.redNames = f.Text(red.transform, "Names", "RED TEAM", TextStyle.Body, 38, GuiPalette.Ink, Centre, Vector2.zero, new Vector2(460, 320), TextAlignmentOptions.Top);
            var blue = p.PanelInstance(t, "Blue Roster", GuiPalette.Cream, Centre, new Vector2(340, -62), new Vector2(520, 360));
            loading.blueNames = f.Text(blue.transform, "Names", "BLUE TEAM", TextStyle.Body, 38, GuiPalette.Ink, Centre, Vector2.zero, new Vector2(460, 320), TextAlignmentOptions.Top);
            f.Text(t, "Versus", "VS", TextStyle.Title, 64, GuiPalette.Cream, Centre, new Vector2(0, -62), new Vector2(140, 90));

            loading.tip = f.Text(t, "Tip", "TIP", TextStyle.BodyInk, 34, GuiPalette.Cream, Bottom, new Vector2(0, 190), new Vector2(1500, 50));
            loading.progress = p.BarInstance(t, "Progress", GuiPalette.Moss, 0, Bottom, new Vector2(0, 110), new Vector2(900, 56), 30);
            loading.progress.format = "{0:0}%";
            loading.clickToPlay = f.Text(t, "Click To Play", "CLICK TO PLAY", TextStyle.Title, 80, GuiPalette.Gold, Bottom, new Vector2(0, 90), new Vector2(1000, 110));
            loading.clickToPlay.gameObject.SetActive(false);
            return Save(root);
        }

        // ---------------------------------------------------------------- The caption box (ticket 32.7)

        // The warm-up's prompts over the HUD
        private GameObject Caption()
        {
            var root = Root("Caption", GuiScreen.Caption);
            CaptionBox(root, false);
            return Save(root);
        }

        // A story beat: the scene alone, the caption box and letterbox bars
        // The speaker over one wrapped line low on the screen, the skip hint and its bar 5 px from the corner, optionally the bars
        private void CaptionBox(GameObject root, bool letterbox)
        {
            var t = root.transform;
            var caption = root.AddComponent<GuiCaption>();
            if (letterbox)
            {
                void Bar(string name, Vector2 anchor)
                {
                    f.Shape(t, name, ToonShapeKind.Box, Color.black, anchor, Vector2.zero, new Vector2(4000, 110), s =>
                    {
                        s.Outline = 0;
                        s.Shadow = 0;
                        s.Shading = 0;
                        s.CornerRadius = 0;
                    });
                }
                Bar("Bar Top", Top);
                Bar("Bar Bottom", Bottom);
                caption.bars = t.Find("Bar Top").gameObject;
            }
            // Over the HUD (the warm-up's prompts) a small box near the bottom, between the health and the weapons and under the
            // pick-up line, so it does not cover the view; a story beat's is the big one over the bottom bar
            var size = letterbox ? new Vector2(1300, 210) : new Vector2(1000, 196);
            var box = Object("Box", t);
            Place(box.transform, Bottom, new Vector2(0, letterbox ? 150 : 12), size);
            caption.box = box.AddComponent<CanvasGroup>();
            f.Panel(box.transform, "Panel", GuiPalette.WithAlpha(GuiPalette.Night, 0.9f), Centre, Vector2.zero, size, letterbox ? 26 : 20);
            caption.speaker = letterbox
                ? f.Text(box.transform, "Speaker", "TOASTYMAGE", TextStyle.Label, 34, GuiPalette.Gold, Top, new Vector2(0, -16), new Vector2(1200, 44))
                : f.Text(box.transform, "Speaker", "TOASTYMAGE", TextStyle.Label, 29, GuiPalette.Gold, Top, new Vector2(0, -8), new Vector2(940, 38));
            caption.line = letterbox
                ? f.Text(box.transform, "Line", "Fill your rage and activate Frenzy. Trust me.", TextStyle.BodyInk, 42, GuiPalette.Cream, Bottom, new Vector2(0, 50), new Vector2(1220, 110))
                : f.Text(box.transform, "Line", "Fill your rage and activate Frenzy. Trust me.", TextStyle.BodyInk, 30, GuiPalette.Cream, Bottom, new Vector2(0, 54), new Vector2(960, 84));
            caption.line.textWrappingMode = TextWrappingModes.Normal;
            caption.line.alignment = TextAlignmentOptions.Center;
            if (!letterbox)
                AutoSize(caption.line, 29, 30);

            // Over the HUD the corner is the weapons' (and the health's): the hint sits in the box, under the line; alone, 5 px from the screen's corner
            var hintParent = letterbox ? t : box.transform;
            var corner = letterbox ? new Vector2(-5, 5) : new Vector2(-18, 10);
            var hintSize = 29;
            var trackSize = letterbox ? new Vector2(260, 22) : new Vector2(180, 16);
            caption.hint = f.Text(hintParent, "Hint", "Hold SPACE to skip", TextStyle.BodyInk, hintSize, GuiPalette.WithAlpha(GuiPalette.Cream, 0.8f), BottomRight,
                corner + new Vector2(-trackSize.x - 12, -7), new Vector2(300, 38), TextAlignmentOptions.Right);
            if (letterbox)
                caption.hint.rectTransform.anchoredPosition = corner + new Vector2(0, 25);
            var track = f.Panel(hintParent, "Skip Track", GuiPalette.WithAlpha(GuiPalette.Night, 0.8f), BottomRight, corner, trackSize, letterbox ? 8 : 6);
            track.Outline = 3;
            track.Shadow = 0;
            var fill = f.Shape(track.transform, "Fill", ToonShapeKind.Box, GuiPalette.Gold, Centre, Vector2.zero, Vector2.zero, s =>
            {
                s.CornerRadius = 6;
                s.Outline = 0;
                s.Shadow = 0;
            });
            Stretch(fill, 3);
            fill.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
            caption.skipBar = fill.GetComponent<RectTransform>();
        }

        // Behind the credits: a dim night over the scene, as dim as the text needs
        private ToonShape Backdrop(Transform parent, float alpha = 1)
        {
            var backdrop = f.Shape(parent, "Backdrop", ToonShapeKind.Box, GuiPalette.WithAlpha(GuiPalette.Night, alpha), Centre, Vector2.zero, Vector2.zero, s =>
            {
                s.Outline = 0;
                s.Shadow = 0;
                s.Shading = 0;
                s.CornerRadius = 0;
                s.raycastTarget = true;
            });
            Stretch(backdrop);
            return backdrop;
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
                (ToonShapeKind.Box, GuiPalette.Red), (ToonShapeKind.Pill, GuiPalette.Amber), (ToonShapeKind.Ring, GuiPalette.Cyan),
                (ToonShapeKind.Cap, GuiPalette.Red), (ToonShapeKind.Splat, GuiPalette.RedDark), (ToonShapeKind.Burst, GuiPalette.Gold),
                (ToonShapeKind.Glow, GuiPalette.Cyan), (ToonShapeKind.Arrow, GuiPalette.Amber), (ToonShapeKind.Cross, GuiPalette.Cream),
                (ToonShapeKind.Check, GuiPalette.Moss), (ToonShapeKind.Vignette, GuiPalette.RedDark),
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
            p.ButtonInstance(buttons, "PRIMARY", GuiPalette.Red, GuiCommandId.Show, TopLeft, new Vector2(30, -70), new Vector2(340, 86), 40, GuiScreen.Gallery);
            p.ButtonInstance(buttons, "SECONDARY", GuiPalette.Amber, GuiCommandId.Show, TopLeft, new Vector2(400, -70), new Vector2(340, 86), 40, GuiScreen.Gallery, 0.4f);
            p.ButtonInstance(buttons, "CONFIRM", GuiPalette.Moss, GuiCommandId.Show, TopLeft, new Vector2(30, -180), new Vector2(340, 86), 40, GuiScreen.Gallery, 0.4f);
            p.ButtonInstance(buttons, "QUIET", GuiPalette.Dusk, GuiCommandId.Show, TopLeft, new Vector2(400, -180), new Vector2(340, 86), 40, GuiScreen.Gallery, 0);
            p.ButtonInstance(buttons, "SMALL", GuiPalette.Violet, GuiCommandId.Show, TopLeft, new Vector2(30, -284), new Vector2(150, 40), 22, GuiScreen.Gallery, 0);
            p.ButtonInstance(buttons, "SMALL", GuiPalette.CyanDeep, GuiCommandId.Show, TopLeft, new Vector2(200, -284), new Vector2(150, 40), 22, GuiScreen.Gallery, 0);
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
            p.BarInstance(bars, "Low Health", GuiPalette.Red, 0.18f, TopLeft, new Vector2(30, -134), new Vector2(330, 46), 30);
            var spores = p.BarInstance(bars, "Spores", GuiPalette.Cyan, 0.56f, TopLeft, new Vector2(30, -198), new Vector2(330, 22), 0);
            spores.bar.Glow = 8;
            f.Shape(bars, "Reload Ring", ToonShapeKind.Ring, GuiPalette.Cyan, TopLeft, new Vector2(400, -70), new Vector2(150, 150), s =>
            {
                s.Thickness = 22;
                s.FillMode = ToonFillMode.Radial;
                s.FillAmount = 0.4f;
                s.Glow = 12;
            });
            f.Shape(bars, "Tank", ToonShapeKind.Pill, GuiPalette.Cyan, TopLeft, new Vector2(600, -66), new Vector2(64, 160), s =>
            {
                s.FillMode = ToonFillMode.Vertical;
                s.FillAmount = 0.6f;
                s.Glow = 14;
                s.Outline = 5;
            });
            f.Shape(bars, "Low Tank", ToonShapeKind.Pill, GuiPalette.Red, TopLeft, new Vector2(690, -66), new Vector2(64, 160), s =>
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
                avatar.head = roster[i].head;
                avatar.dimmed = i == 5;
                avatar.disc.color = i == 5 ? Color.Lerp(roster[i].color, GuiPalette.Dusk, 0.75f) : roster[i].color;
            }
            var row = Instance<GuiPlayerRow>(p.PlayerRow, people, "Player Row", TopLeft, new Vector2(560, -64), new Vector2(760, 60));
            row.background.color = GuiPalette.Gold;
            row.playerName.text = SdfBattle.Cast.You;
            row.rank.text = "1";
            row.status.text = "YOU";
            var row2 = Instance<GuiPlayerRow>(p.PlayerRow, people, "Player Row Out", TopLeft, new Vector2(560, -140), new Vector2(760, 60));
            row2.background.color = Color.Lerp(GuiPalette.Paper, GuiPalette.Dusk, 0.55f);
            row2.playerName.text = SdfBattle.Cast.Blue[0];
            row2.rank.text = "8";
            row2.status.text = "";
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
                ("Ink", GuiPalette.Ink), ("Cream", GuiPalette.Cream), ("Paper", GuiPalette.Paper), ("Red", GuiPalette.Red),
                ("Amber", GuiPalette.Amber), ("Gold", GuiPalette.Gold), ("Moss", GuiPalette.Moss), ("Cyan", GuiPalette.Cyan),
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
            f.Text(palette, "Title Sample", "Title", TextStyle.Title, 56, GuiPalette.Red, TopLeft, new Vector2(20, -670), new Vector2(200, 70), TextAlignmentOptions.Left);
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


        // ---------------------------------------------------------------- The ending and the credits (ticket 32.8; ticket 41 left the credits)

        // 45 s of text going up a dark screen; GuiCredits places the text, CreditsPlayer times it
        private GameObject Credits()
        {
            var root = Root("Credits", GuiScreen.Credits);
            var t = root.transform;
            var credits = root.AddComponent<GuiCredits>();
            Backdrop(t, 0.72f);
            f.Shape(t, "Glow", ToonShapeKind.Glow, GuiPalette.WithAlpha(GuiPalette.Cyan, 0.12f), Centre, new Vector2(0, 0), new Vector2(1500, 1000));

            var viewport = Object("Viewport", t);
            Stretch(viewport.transform);
            viewport.AddComponent<RectMask2D>();
            credits.viewport = (RectTransform)viewport.transform;
            credits.text = f.Text(viewport.transform, "Text", CreditsRoll.RichText(), TextStyle.BodyInk, 44, GuiPalette.Cream, Top, Vector2.zero, new Vector2(1500, 3000));
            credits.text.alignment = TextAlignmentOptions.Top;
            credits.text.lineSpacing = 18;

            credits.hint = f.Text(t, "Hint", "Hold SPACE to skip", TextStyle.BodyInk, 26, GuiPalette.WithAlpha(GuiPalette.Cream, 0.8f), BottomRight, new Vector2(-5, 30), new Vector2(500, 40), TextAlignmentOptions.Right);
            var track = f.Panel(t, "Skip Track", GuiPalette.WithAlpha(GuiPalette.Night, 0.8f), BottomRight, new Vector2(-5, 5), new Vector2(260, 22), 8);
            track.Outline = 3;
            track.Shadow = 0;
            var fill = f.Shape(track.transform, "Fill", ToonShapeKind.Box, GuiPalette.Gold, Centre, Vector2.zero, Vector2.zero, s =>
            {
                s.CornerRadius = 6;
                s.Outline = 0;
                s.Shadow = 0;
            });
            Stretch(fill, 3);
            fill.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
            credits.skipBar = fill.GetComponent<RectTransform>();
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

            var panel = f.Panel(root.transform, "Panel", GuiPalette.WithAlpha(GuiPalette.Night, 0.92f), Left, new Vector2(24, -10), new Vector2(262, 780), 18);
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
            Add("HUD", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Hud);
            Add("SCORES", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Scoreboard);
            Add("PAUSE", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Pause);
            Add("GALLERY", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Gallery);
            Add("SETTINGS", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Settings);
            Add("DEATH", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Death);
            Add("VICTORY", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Victory);
            Add("DEFEAT", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Defeat);
            Add("CONFIRM", GuiPalette.Dusk, GuiCommandId.Show, GuiScreen.Confirm);
            EndRow();
            Heading("Round");
            Add("NEW ROUND", GuiPalette.MossDark, GuiCommandId.NewRound);
            Add("AI ON/OFF", GuiPalette.MossDark, GuiCommandId.ToggleAutoEvents);
            Add("HIT ME", GuiPalette.RedDark, GuiCommandId.Damage);
            Add("HEAL", GuiPalette.MossDark, GuiCommandId.Heal);
            Add("MY KILL", GuiPalette.RedDark, GuiCommandId.YouKill);
            Add("HEADSHOT", GuiPalette.RedDark, GuiCommandId.YouHeadshot);
            Add("AI KILL", GuiPalette.Violet, GuiCommandId.BotKill);
            Add("PICK UP", GuiPalette.CyanDeep, GuiCommandId.TogglePickup);
            Add("RELOAD", GuiPalette.CyanDeep, GuiCommandId.Reload);
            Add("DIE", GuiPalette.RedDark, GuiCommandId.Die);
            Add("WIN", GuiPalette.Amber, GuiCommandId.Win);
            EndRow();

            var state = f.Text(t, "State", "Playing", TextStyle.Body, 18, GuiPalette.Paper, TopLeft, new Vector2(14, y), new Vector2(234, 70), TextAlignmentOptions.TopLeft);
            state.textWrappingMode = TextWrappingModes.Normal;
            var status = state.gameObject.AddComponent<GuiStatus>();
            status.label = state;
            return GuiFactory.Save(root, $"{Folder}/Test Panel.prefab");
        }
    }
}
