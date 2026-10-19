using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static SdfGui.Editor.GuiFactory;

namespace SdfGui.Editor
{
    /// <summary>
    /// The GUI's reusable components as prefabs in Assets/GUI/Prefabs/Components: panel, button, slider, toggle, selector, avatar, ping
    /// bars, progress bar, banner, player row and kill feed entry. The screens (<see cref="GuiScreens"/>) are made of instances of
    /// these, so a change to a component prefab shows on every screen. Rebuilt by Tools > SDF GUI > Rebuild Prefabs and Scene, which
    /// replaces them: change the builder rather than the prefabs to keep a change.
    /// </summary>
    public sealed class GuiPrefabs
    {
        public const string Folder = GuiAssets.Folder + "/Prefabs";
        public const string ComponentFolder = Folder + "/Components";

        private readonly GuiFactory f;

        public GameObject Panel, Button, Slider, Toggle, Selector, Avatar, PingBars, ProgressBar, Banner, PlayerRow, KillFeedEntry;

        public GuiPrefabs(GuiFactory factory) => f = factory;

        public GuiFactory Factory => f;

        public void Build()
        {
            Panel = BuildPanel();
            Button = BuildButton();
            Slider = BuildSlider();
            Toggle = BuildToggle();
            Selector = BuildSelector();
            Avatar = BuildAvatar();
            PingBars = BuildPingBars();
            ProgressBar = BuildProgressBar();
            Banner = BuildBanner();
            PlayerRow = BuildPlayerRow();
            KillFeedEntry = BuildKillFeedEntry();
        }

        private static string PathOf(string name) => $"{ComponentFolder}/{name}.prefab";

        // ---------------------------------------------------------------- Components

        private GameObject BuildPanel()
        {
            var panel = f.Panel(null, "Panel", GuiPalette.Paper, Centre, Vector2.zero, new Vector2(600, 400));
            return Save(panel.gameObject, PathOf("Panel"));
        }

        private GameObject BuildButton()
        {
            var shape = f.Shape(null, "Button", ToonShapeKind.Box, GuiPalette.Amanita, Centre, Vector2.zero, new Vector2(380, 86), s =>
            {
                s.CornerRadius = 22;
                s.Outline = 5;
                s.Shadow = 7;
                s.Spots = 0.85f;
                s.raycastTarget = true;
            });
            var button = shape.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = shape;
            shape.gameObject.AddComponent<ToonButton>().shape = shape;
            shape.gameObject.AddComponent<GuiCommand>();
            f.FillText(shape.transform, "Label", "BUTTON", TextStyle.Title, 40, GuiPalette.Cream);
            return Save(shape.gameObject, PathOf("Button"));
        }

        private GameObject BuildSlider()
        {
            var root = Object("Slider", null);
            Place(root.transform, Centre, Vector2.zero, new Vector2(420, 34));
            var background = f.Shape(root.transform, "Background", ToonShapeKind.Pill, GuiPalette.Night, Centre, Vector2.zero, Vector2.zero, s =>
            {
                s.Outline = 4;
                s.Shadow = 4;
                s.Shading = 0;
                s.raycastTarget = true;
            });
            Stretch(background);
            var fillArea = Object("Fill Area", root.transform);
            Stretch(fillArea.transform);
            var fill = f.Shape(fillArea.transform, "Fill", ToonShapeKind.Pill, GuiPalette.Moss, Centre, Vector2.zero, Vector2.zero, s =>
            {
                s.Outline = 4;
                s.Shadow = 0;
            });
            var handleArea = Object("Handle Slide Area", root.transform);
            var handleAreaRect = Stretch(handleArea.transform);
            handleAreaRect.offsetMin = new Vector2(17, 0);
            handleAreaRect.offsetMax = new Vector2(-17, 0);
            var handle = f.Shape(handleArea.transform, "Handle", ToonShapeKind.Pill, GuiPalette.Cream, Centre, Vector2.zero, new Vector2(50, 50), s =>
            {
                s.Outline = 5;
                s.Shadow = 5;
                s.raycastTarget = true;
            });
            // The slider moves the handle's anchors along x; it keeps its own height
            handle.rectTransform.anchorMin = new Vector2(0, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0, 0.5f);
            var slider = root.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0.6f;
            root.AddComponent<ToonButton>().shape = handle;
            return Save(root, PathOf("Slider"));
        }

        private GameObject BuildToggle()
        {
            var box = f.Shape(null, "Toggle", ToonShapeKind.Box, GuiPalette.Paper, Centre, Vector2.zero, new Vector2(60, 60), s =>
            {
                s.CornerRadius = 14;
                s.Outline = 5;
                s.Shadow = 5;
                s.raycastTarget = true;
            });
            var check = f.Shape(box.transform, "Checkmark", ToonShapeKind.Check, GuiPalette.Moss, Centre, new Vector2(2, 2), new Vector2(46, 46), s =>
            {
                s.Thickness = 11;
                s.Outline = 3.5f;
                s.Shadow = 0;
            });
            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.toggleTransition = UnityEngine.UI.Toggle.ToggleTransition.None;
            toggle.isOn = true;
            box.gameObject.AddComponent<ToonButton>().shape = box;
            return Save(box.gameObject, PathOf("Toggle"));
        }

        private GameObject BuildSelector()
        {
            var background = f.Shape(null, "Selector", ToonShapeKind.Pill, GuiPalette.Night, Centre, Vector2.zero, new Vector2(420, 64), s =>
            {
                s.Outline = 4;
                s.Shadow = 4;
                s.Shading = 0;
            });
            Button Arrow(string name, Vector2 anchor, float x, float angle)
            {
                var holder = Object(name, background.transform);
                Place(holder.transform, anchor, new Vector2(x, 0), new Vector2(58, 58), Centre);
                var arrow = f.Shape(holder.transform, "Arrow", ToonShapeKind.Arrow, GuiPalette.Chanterelle, Centre, Vector2.zero, new Vector2(40, 46), s =>
                {
                    s.CornerRadius = 6;
                    s.Outline = 4.5f;
                    s.Shadow = 0;
                    s.raycastTarget = true;
                });
                arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
                var button = holder.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.targetGraphic = arrow;
                holder.AddComponent<ToonButton>().shape = arrow;
                return button;
            }
            var selector = background.gameObject.AddComponent<GuiSelector>();
            selector.previous = Arrow("Previous", Left, 36, 180);
            selector.next = Arrow("Next", Right, -36, 0);
            selector.label = f.Text(background.transform, "Label", "High", TextStyle.Title, 34, GuiPalette.Cream, Centre, Vector2.zero, new Vector2(270, 60));
            selector.label.enableAutoSizing = true;
            selector.label.fontSizeMin = 18;
            selector.label.fontSizeMax = 34;
            return Save(background.gameObject, PathOf("Selector"));
        }

        private GameObject BuildAvatar()
        {
            var disc = f.Shape(null, "Avatar", ToonShapeKind.Pill, GuiPalette.Amanita, Centre, Vector2.zero, new Vector2(72, 72), s =>
            {
                s.Outline = 5;
                s.Shadow = 4;
            });
            var avatar = disc.gameObject.AddComponent<GuiAvatar>();
            avatar.disc = disc;
            avatar.icons = f.Kit.icons;
            avatar.icon = f.Icon(disc.transform, "Icon", "amanita-scout", Centre, new Vector2(0, 2), Vector2.zero);
            Stretch(avatar.icon, 5);
            return Save(disc.gameObject, PathOf("Avatar"));
        }

        private GameObject BuildPingBars()
        {
            var root = Object("Ping Bars", null);
            Place(root.transform, Centre, Vector2.zero, new Vector2(84, 32));
            var ping = root.AddComponent<GuiPingBars>();
            ping.bars = new ToonShape[3];
            for (var i = 0; i < 3; i++)
            {
                var height = 12 + i * 9;
                ping.bars[i] = f.Shape(root.transform, $"Bar {i + 1}", ToonShapeKind.Pill, GuiPalette.Moss, BottomLeft, new Vector2(i * 14, 0), new Vector2(11, height), s =>
                {
                    s.Outline = 2.5f;
                    s.Shadow = 0;
                    s.Shading = 0;
                });
            }
            ping.label = f.Text(root.transform, "Label", "40", TextStyle.Body, 22, GuiPalette.Ink, BottomRight, new Vector2(0, -2), new Vector2(44, 32), TextAlignmentOptions.BottomRight);
            return Save(root, PathOf("Ping Bars"));
        }

        private GameObject BuildProgressBar()
        {
            var bar = f.Shape(null, "Progress Bar", ToonShapeKind.Pill, GuiPalette.Moss, Centre, Vector2.zero, new Vector2(320, 42), s =>
            {
                s.Outline = 5;
                s.Shadow = 5;
                s.FillMode = ToonFillMode.Horizontal;
                s.FillAmount = 0.7f;
            });
            var progress = bar.gameObject.AddComponent<GuiBar>();
            progress.bar = bar;
            progress.label = f.FillText(bar.transform, "Label", "70", TextStyle.Title, 28, GuiPalette.Cream);
            progress.value = 0.7f;
            return Save(bar.gameObject, PathOf("Progress Bar"));
        }

        private GameObject BuildBanner()
        {
            var banner = f.Shape(null, "Banner", ToonShapeKind.Box, GuiPalette.Amanita, Centre, Vector2.zero, new Vector2(760, 100), s =>
            {
                s.CornerRadius = 26;
                s.Outline = 6;
                s.Shadow = 9;
                s.Spots = 0.55f;
                s.Wobble = 1.5f;
            });
            f.FillText(banner.transform, "Title", "BANNER", TextStyle.Title, 52, GuiPalette.Cream);
            return Save(banner.gameObject, PathOf("Banner"));
        }

        private GameObject BuildPlayerRow()
        {
            var background = f.Shape(null, "Player Row", ToonShapeKind.Box, GuiPalette.Paper, Centre, Vector2.zero, new Vector2(540, 60), s =>
            {
                s.CornerRadius = 18;
                s.Outline = 4;
                s.Shadow = 5;
            });
            var row = background.gameObject.AddComponent<GuiPlayerRow>();
            row.background = background;
            var t = background.transform;
            row.rank = f.Text(t, "Rank", "1", TextStyle.Label, 28, GuiPalette.Dusk, Left, new Vector2(12, 0), new Vector2(40, 50));
            row.avatar = Instance<GuiAvatar>(Avatar, t, "Avatar", Left, new Vector2(58, 0), new Vector2(50, 50));
            row.playerName = f.Text(t, "Name", "Player", TextStyle.Body, 28, GuiPalette.Ink, Left, new Vector2(122, 0), new Vector2(250, 50), TextAlignmentOptions.Left);
            row.kills = f.Text(t, "Kills", "0", TextStyle.Label, 30, GuiPalette.Ink, Right, new Vector2(-230, 0), new Vector2(60, 50));
            row.status = f.Text(t, "Status", "READY", TextStyle.Label, 24, GuiPalette.MossDark, Right, new Vector2(-104, 0), new Vector2(110, 50));
            row.ping = Instance<GuiPingBars>(PingBars, t, "Ping", Right, new Vector2(-14, 0));
            return Save(background.gameObject, PathOf("Player Row"));
        }

        private GameObject BuildKillFeedEntry()
        {
            var background = f.Shape(null, "Kill Feed Entry", ToonShapeKind.Box, GuiPalette.WithAlpha(GuiPalette.Night, 0.82f), Centre, Vector2.zero,
                new Vector2(460, 52), s =>
                {
                    s.CornerRadius = 14;
                    s.Outline = 3.5f;
                    s.Shadow = 4;
                    s.Shading = 0.5f;
                });
            var layout = background.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 6, 6);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var entry = background.gameObject.AddComponent<GuiKillFeedEntry>();
            entry.background = background;
            entry.group = background.gameObject.AddComponent<CanvasGroup>();
            entry.group.blocksRaycasts = false;
            entry.icons = f.Kit.icons;
            var t = background.transform;
            entry.killer = f.Text(t, "Killer", "Killer", TextStyle.BodyInk, 27, GuiPalette.Cream, Centre, Vector2.zero, new Vector2(100, 40));
            entry.weapon = f.Icon(t, "Weapon", "minigun", Centre, Vector2.zero, new Vector2(110, 44));
            Fixed(entry.weapon, 110, 44);
            entry.headshot = f.Shape(t, "Headshot", ToonShapeKind.Cap, GuiPalette.Amanita, Centre, Vector2.zero, new Vector2(38, 28), s =>
            {
                s.Outline = 3.5f;
                s.Shadow = 0;
                s.Spots = 1;
            });
            Fixed(entry.headshot, 38, 28);
            entry.victim = f.Text(t, "Victim", "Victim", TextStyle.BodyInk, 27, GuiPalette.Cream, Centre, Vector2.zero, new Vector2(100, 40));
            return Save(background.gameObject, PathOf("Kill Feed Entry"));
        }

        private static void Fixed(Component component, float width, float height)
        {
            var element = component.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = width;
            element.preferredHeight = element.minHeight = height;
        }

        // ---------------------------------------------------------------- Instances, with what differs from the prefab

        public Button ButtonInstance(Transform parent, string text, Color color, GuiCommandId command, Vector2 anchor, Vector2 position,
            Vector2? size = null, float fontSize = 40, GuiScreen screen = GuiScreen.Hud, float spots = 0.85f)
        {
            var button = Instance<Button>(Button, parent, text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1).ToLowerInvariant() + " Button" : "Button", anchor, position, size);
            var shape = button.GetComponent<ToonShape>();
            shape.color = color;
            shape.Spots = spots;
            if (size is { y: < 60 })
            {
                shape.CornerRadius = 12;
                shape.Outline = 3.5f;
                shape.Shadow = 4;
            }
            var label = button.GetComponentInChildren<TMP_Text>();
            label.text = text;
            label.fontSize = fontSize;
            var gui = button.GetComponent<GuiCommand>();
            gui.command = command;
            gui.screen = screen;
            return button;
        }

        public ToonShape PanelInstance(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var panel = Instance<ToonShape>(Panel, parent, name, anchor, position, size);
            panel.color = color;
            return panel;
        }

        public ToonShape BannerInstance(Transform parent, string name, string text, Color color, Vector2 anchor, Vector2 position, Vector2 size, float fontSize = 52)
        {
            var banner = Instance<ToonShape>(Banner, parent, name, anchor, position, size);
            banner.color = color;
            var title = banner.GetComponentInChildren<TMP_Text>();
            title.text = text;
            title.fontSize = fontSize;
            return banner;
        }

        public GuiBar BarInstance(Transform parent, string name, Color color, float value, Vector2 anchor, Vector2 position, Vector2 size, float fontSize = 28)
        {
            var bar = Instance<GuiBar>(ProgressBar, parent, name, anchor, position, size);
            bar.bar.color = color;
            bar.value = value;
            bar.bar.FillAmount = value;
            bar.label.fontSize = fontSize;
            bar.label.text = Mathf.RoundToInt(value * 100).ToString();
            return bar;
        }
    }
}
