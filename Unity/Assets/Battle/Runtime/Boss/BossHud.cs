using System.Collections.Generic;
using SdfGui;
using TMPro;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The boss fight's HUD (ticket 33.7), added to the match's GUI by <see cref="Boss"/> like <see cref="FlagHud"/>. It is built in code
    /// from the HUD prefab's own fonts and shape material, so the GUI prefabs need no rebuild, and all of it sits 5 px or more from the
    /// screen's edges:
    /// <list type="bullet">
    /// <item>on the clock's row, where the clock and the team score were (neither means anything here), 42's lives as cap pips; a pip pops
    /// on a death;</item>
    /// <item>under it Prime's name and the boss bar in three segments in proportion to the three bars, the current one bright, the spent
    /// ones grey, the coming ones dim, a white chip trailing behind damage for half a second and a gold flash on weak-point damage; a
    /// shield over the bar while nothing can hurt him, a half shield for Royal Immunity, and STUNNED with the seconds left while a
    /// window is open;</item>
    /// <item>under the bar the objective line (<see cref="Boss.Objective"/>);</item>
    /// <item>Prime's name card once, at the fight's start (a blue banner sliding in from the right), and a phase card at every phase
    /// start with the objective; the damage you do shows as a number where you hit him (gold and bigger on a weak point, grey when
    /// blocked);</item>
    /// <item>in phase 3 the blue flag's status reads as the crown's ("the crown: on the wreck / carried by ... / dropped"), and in phase
    /// 2 the flag statuses go.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class BossHud : MonoBehaviour
    {
        [Tooltip("The boss bar's width on a wide screen; narrower screens take half their width at the most")]
        public float barWidth = 640;
        public float barHeight = 34;
        [Tooltip("Seconds the white chip waits behind damage before it catches up")]
        public float chipHold = 0.5f;
        public float nameCardSeconds = 2.5f;
        public float phaseCardSeconds = 1.5f;

        public static readonly Color PrimeBlue = GuiPalette.Hex(0x3D7BEA);
        private static readonly Color Spent = GuiPalette.Hex(0x6B6B73);
        private static readonly Color Coming = GuiPalette.Hex(0x27447E);
        private static readonly Color Silver = GuiPalette.Hex(0xC9D3DD);

        private const float SegmentGap = 6, Inset = 4, NameRow = 36;

        private Boss boss;
        private BossState state;
        private GuiHud hud;
        private RectTransform root;
        private Canvas canvas;
        private FlagHud flagHud;
        private Camera view;

        // Borrowed from the HUD prefab
        private Material shapeMaterial;
        private TMP_FontAsset titleFont, bodyFont;
        private Material titleFontMaterial, bodyFontMaterial;

        private readonly List<GameObject> made = new List<GameObject>();
        private readonly List<GameObject> hidden = new List<GameObject>();

        // Lives
        private RectTransform pipRow;
        private ToonShape[] pips = new ToonShape[0];
        private float[] pop = new float[0];

        // The bar
        private RectTransform barRoot;
        private TMP_Text nameText, stunText, objectiveText;
        private ToonShape[] back = new ToonShape[3], chipShape = new ToonShape[3], fill = new ToonShape[3];
        private readonly float[] chip = new float[3], lastFill = new float[3], hold = new float[3];
        private RectTransform shieldRoot;
        private ToonShape shieldBack, shieldFront;
        private TMP_Text shieldLabel;
        private float shownBarWidth = -1;
        private float flash, shieldPulse;

        // Cards
        private RectTransform nameCard;
        private float nameLeft;
        private bool nameShown;
        private RectTransform phaseCard;
        private CanvasGroup phaseGroup;
        private TMP_Text phaseTitle, phaseLine;
        private float phaseLeft, phaseDelay;
        private BossPhase cardPhase;

        // Damage numbers
        private struct Pop
        {
            public TMP_Text text;
            public Vector3 world;
            public float age;
            public bool live;
        }
        private readonly Pop[] pops = new Pop[8];
        private int nextPop;
        private float blockedAt = -1;

        public static BossHud Create(Boss boss)
        {
            var matchHud = boss.Match != null ? boss.Match.Hud : null;
            var hud = matchHud != null && matchHud.view != null ? matchHud.view.GetComponentInChildren<GuiHud>(true) : null;
            if (hud == null || hud.clock == null || hud.flagBanner == null || hud.fps == null)
            {
                Debug.LogWarning("Boss: the GUI's HUD lacks its clock, banner or text pieces; no boss HUD");
                return null;
            }
            var bossHud = hud.gameObject.AddComponent<BossHud>();
            bossHud.boss = boss;
            bossHud.state = boss.State;
            bossHud.hud = hud;
            bossHud.Build();
            bossHud.Hook();
            return bossHud;
        }

        private void OnDestroy()
        {
            if (state != null)
            {
                state.PhaseChanged -= OnPhaseChanged;
                state.Damaged -= OnDamaged;
                state.LifeLost -= OnLifeLost;
            }
            if (flagHud != null)
                flagHud.ReserveBelowClock(0);
            foreach (var piece in made)
                if (piece != null)
                    Destroy(piece);
            foreach (var piece in hidden)
                if (piece != null)
                    piece.SetActive(true);
        }

        private void Hook()
        {
            state.PhaseChanged += OnPhaseChanged;
            state.Damaged += OnDamaged;
            state.LifeLost += OnLifeLost;
        }

        // ---------------------------------------------------------------- Building

        private void Build()
        {
            root = (RectTransform)hud.transform;
            canvas = hud.GetComponentInParent<Canvas>();
            shapeMaterial = hud.flagBanner.material;
            titleFont = hud.clock.font;
            titleFontMaterial = hud.clock.fontSharedMaterial;
            bodyFont = hud.fps.font;
            bodyFontMaterial = hud.fps.fontSharedMaterial;
            flagHud = boss.Flags != null ? boss.Flags.Hud : null;

            // The clock and the team score mean nothing here: the clock's panel carries the lives, the score is gone
            Hide(hud.clock);
            foreach (var score in hud.teamScores)
                Hide(score);

            BuildLives();
            BuildBar();
            BuildCards();
            BuildPops();
            // The flag HUD's banner and narrow-screen statuses clear the bar and the objective line
            flagHud?.ReserveBelowClock(NameRow + barHeight + 60);
        }

        private void Hide(Component piece)
        {
            if (piece == null || !piece.gameObject.activeSelf)
                return;
            piece.gameObject.SetActive(false);
            hidden.Add(piece.gameObject);
        }

        private RectTransform Rect(string label, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var piece = new GameObject(label, typeof(RectTransform));
            piece.transform.SetParent(parent, false);
            if (parent == root)
                made.Add(piece);
            var rect = (RectTransform)piece.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private ToonShape Shape(string label, Transform parent, ToonShapeKind kind, Color colour, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
            float corner = 10, float outline = 4, float shadow = 4, float shading = 0.6f)
        {
            var rect = Rect(label, parent, anchor, pivot, position, size);
            var shape = rect.gameObject.AddComponent<ToonShape>();
            shape.material = shapeMaterial;
            shape.Kind = kind;
            shape.color = colour;
            shape.CornerRadius = corner;
            shape.Outline = outline;
            shape.Shadow = shadow;
            shape.Shading = shading;
            shape.raycastTarget = false;
            return shape;
        }

        private TMP_Text Text(string label, Transform parent, string text, bool title, float size, Color colour, Vector2 anchor, Vector2 pivot, Vector2 position,
            Vector2 boxSize, TextAlignmentOptions align)
        {
            var rect = Rect(label, parent, anchor, pivot, position, boxSize);
            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = title ? titleFont : bodyFont;
            tmp.fontSharedMaterial = title ? titleFontMaterial : bodyFontMaterial;
            tmp.fontSize = size;
            tmp.color = colour;
            tmp.alignment = align;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.text = text;
            return tmp;
        }

        private static readonly Vector2 TopCentre = new Vector2(0.5f, 1), Middle = new Vector2(0.5f, 0.5f);

        // 42's lives: one cap per life on the clock's panel, which is where the clock was
        private void BuildLives()
        {
            var panel = hud.clock.transform.parent as RectTransform;
            if (panel == null || panel == root)
                panel = Rect("Lives", root, TopCentre, TopCentre, new Vector2(0, -5), new Vector2(230, 90));
            var count = Mathf.Max(1, boss.Rules.lives);
            pipRow = Rect("Lives Row", panel, Middle, Middle, Vector2.zero, new Vector2(panel.rect.width, 40));
            made.Add(pipRow.gameObject);
            pips = new ToonShape[count];
            pop = new float[count];
            const float width = 36, gap = 6;
            var total = count * width + (count - 1) * gap;
            for (var i = 0; i < count; i++)
                pips[i] = Shape($"Life {i + 1}", pipRow, ToonShapeKind.Cap, GuiPalette.Amanita, Middle, Middle, new Vector2(-total * 0.5f + width * 0.5f + i * (width + gap), 0),
                    new Vector2(width, 30), corner: 10, outline: 3, shadow: 2, shading: 0.8f);
        }

        // Prime's name, the bar in three segments with its icons, STUNNED, and the objective line
        private void BuildBar()
        {
            // Under the clock's row: its panel is 90 high and 5 from the top
            barRoot = Rect("Boss Bar", root, TopCentre, TopCentre, new Vector2(0, -5 - 90 - 6), new Vector2(barWidth, NameRow + barHeight + 50));
            nameText = Text("Name", barRoot, "CHANTERELLE PRIME", true, 34, GuiPalette.Cream, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                new Vector2(barWidth * 0.6f, NameRow), TextAlignmentOptions.Left);
            stunText = Text("Stunned", barRoot, "STUNNED", true, 34, GuiPalette.Gold, new Vector2(1, 1), new Vector2(1, 1), Vector2.zero,
                new Vector2(barWidth * 0.4f, NameRow), TextAlignmentOptions.Right);
            for (var i = 0; i < 3; i++)
            {
                var corner = i == 1 ? 4 : 10;
                back[i] = Shape($"Segment {i + 1}", barRoot, ToonShapeKind.Box, GuiPalette.WithAlpha(GuiPalette.Night, 0.92f), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(0, -NameRow), new Vector2(100, barHeight), corner, 4, 4, 0);
                chipShape[i] = Shape($"Chip {i + 1}", back[i].transform, ToonShapeKind.Box, Color.white, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(Inset, 0), new Vector2(90, barHeight - 2 * Inset), 5, 0, 0, 0);
                fill[i] = Shape($"Fill {i + 1}", back[i].transform, ToonShapeKind.Box, Coming, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(Inset, 0), new Vector2(90, barHeight - 2 * Inset), 5, 0, 0, 0.7f);
                foreach (var bar in new[] { chipShape[i], fill[i] })
                {
                    bar.FillMode = ToonFillMode.Horizontal;
                    bar.FillAmount = 1;
                }
                chip[i] = lastFill[i] = 1;
            }
            // The shield over the bar's middle: a dark shield with a pale front, full for the spore shield, half for Royal Immunity
            shieldRoot = Rect("Shield", barRoot, TopCentre, Middle, new Vector2(0, -NameRow - barHeight * 0.5f), new Vector2(46, 54));
            shieldBack = Shape("Back", shieldRoot, ToonShapeKind.Box, GuiPalette.Night, Middle, Middle, Vector2.zero, new Vector2(46, 54), 18, 4, 4, 0);
            shieldFront = Shape("Front", shieldRoot, ToonShapeKind.Box, GuiPalette.Spore, Middle, Middle, Vector2.zero, new Vector2(34, 42), 13, 0, 0, 0.8f);
            shieldFront.FillMode = ToonFillMode.Horizontal;
            shieldLabel = Text("Label", shieldRoot, "", true, 22, GuiPalette.Ink, Middle, Middle, Vector2.zero, new Vector2(46, 40), TextAlignmentOptions.Center);
            objectiveText = Text("Objective", barRoot, "", false, 30, GuiPalette.Cream, TopCentre, TopCentre, new Vector2(0, -NameRow - barHeight - 10),
                new Vector2(barWidth * 1.6f, 40), TextAlignmentOptions.Center);
            LayoutBar(barWidth);
        }

        private void LayoutBar(float width)
        {
            shownBarWidth = width;
            barRoot.sizeDelta = new Vector2(width, barRoot.sizeDelta.y);
            nameText.rectTransform.sizeDelta = new Vector2(width * 0.6f, NameRow);
            stunText.rectTransform.sizeDelta = new Vector2(width * 0.4f, NameRow);
            var usable = width - 2 * SegmentGap;
            var total = Mathf.Max(1, boss.Rules.TotalHealth);
            var x = -width * 0.5f;
            for (var i = 0; i < 3; i++)
            {
                var w = usable * boss.Rules.BarMax(i) / total;
                back[i].rectTransform.anchorMin = back[i].rectTransform.anchorMax = new Vector2(0.5f, 1);
                back[i].rectTransform.pivot = new Vector2(0, 1);
                back[i].rectTransform.anchoredPosition = new Vector2(x, -NameRow);
                back[i].rectTransform.sizeDelta = new Vector2(w, barHeight);
                foreach (var bar in new[] { chipShape[i], fill[i] })
                    bar.rectTransform.sizeDelta = new Vector2(w - 2 * Inset, barHeight - 2 * Inset);
                x += w + SegmentGap;
            }
            // Name and STUNNED sit on the bar's two ends
            nameText.rectTransform.anchorMin = nameText.rectTransform.anchorMax = new Vector2(0.5f, 1);
            nameText.rectTransform.pivot = new Vector2(0, 1);
            nameText.rectTransform.anchoredPosition = new Vector2(-width * 0.5f, 0);
            stunText.rectTransform.anchorMin = stunText.rectTransform.anchorMax = new Vector2(0.5f, 1);
            stunText.rectTransform.pivot = new Vector2(1, 1);
            stunText.rectTransform.anchoredPosition = new Vector2(width * 0.5f, 0);
        }

        private void BuildCards()
        {
            // The name card: a blue banner, 5 px from the right edge, vertically a little above the middle
            nameCard = Rect("Name Card", root, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 150), new Vector2(560, 150));
            var banner = Shape("Banner", nameCard, ToonShapeKind.Box, PrimeBlue, Middle, Middle, Vector2.zero, new Vector2(560, 150), 26, 5, 6, 0.8f);
            Text("Title", nameCard, "CHANTERELLE PRIME", true, 46, GuiPalette.Cream, Middle, Middle, new Vector2(40, 24), new Vector2(480, 56), TextAlignmentOptions.Center);
            Text("Sub", nameCard, "Last year's runner-up. Rigged all of this.", false, 24, GuiPalette.Cream, Middle, Middle, new Vector2(40, -22), new Vector2(480, 36), TextAlignmentOptions.Center);
            var medal = Shape("Medal", nameCard, ToonShapeKind.Burst, Silver, Middle, Middle, new Vector2(-222, 0), new Vector2(84, 84), 0, 4, 4, 0.7f);
            medal.Wobble = 2;
            Text("2nd", medal.transform, "2nd", true, 30, GuiPalette.Ink, Middle, Middle, Vector2.zero, new Vector2(80, 40), TextAlignmentOptions.Center);
            banner.name = "Banner";
            nameCard.gameObject.SetActive(false);

            // The phase card: just above the crosshair, under the flag statuses and the banner, a dark banner with the phase and its objective
            phaseCard = Rect("Phase Card", root, new Vector2(0.5f, 0.6f), Middle, Vector2.zero, new Vector2(900, 120));
            phaseGroup = phaseCard.gameObject.AddComponent<CanvasGroup>();
            phaseGroup.blocksRaycasts = false;
            Shape("Back", phaseCard, ToonShapeKind.Box, GuiPalette.WithAlpha(GuiPalette.Night, 0.9f), Middle, Middle, Vector2.zero, new Vector2(900, 120), 26, 5, 6, 0.4f);
            phaseTitle = Text("Title", phaseCard, "PHASE 1", true, 48, GuiPalette.Gold, Middle, Middle, new Vector2(0, 22), new Vector2(860, 56), TextAlignmentOptions.Center);
            phaseLine = Text("Line", phaseCard, "", false, 28, GuiPalette.Cream, Middle, Middle, new Vector2(0, -26), new Vector2(860, 40), TextAlignmentOptions.Center);
            phaseCard.gameObject.SetActive(false);
        }

        private void BuildPops()
        {
            for (var i = 0; i < pops.Length; i++)
            {
                var text = Text($"Hit Number {i + 1}", root, "", true, 40, GuiPalette.Cream, Middle, Middle, Vector2.zero, new Vector2(300, 60), TextAlignmentOptions.Center);
                text.gameObject.SetActive(false);
                pops[i].text = text;
            }
        }

        // ---------------------------------------------------------------- Events

        private void OnPhaseChanged(BossPhase phase)
        {
            if (!phase.IsFight())
                return;
            // The name card once, at the fight's start; the phase card after it (and at once on a retry or the next phase)
            phaseDelay = 0;
            if (!nameShown && phase == BossPhase.Phase1)
            {
                nameShown = true;
                nameLeft = nameCardSeconds;
                phaseDelay = nameCardSeconds + 0.2f;
            }
            cardPhase = phase;
            phaseLeft = phaseCardSeconds;
            phaseCard.gameObject.SetActive(false);
            // A new phase's bar is full: the chips start there
            for (var i = 0; i < 3; i++)
                chip[i] = lastFill[i] = Mathf.Clamp01(state.BarHealth(i) / state.BarMax(i));
        }

        private void OnDamaged(BossHit hit)
        {
            if (hit.Blocked)
                shieldPulse = 0.2f;
            else if (hit.WeakPoint)
                flash = 0.25f;
        }

        private void OnLifeLost(int livesLeft)
        {
            if (livesLeft >= 0 && livesLeft < pop.Length)
                pop[livesLeft] = 0.4f;
        }

        // A hit on Prime, where it landed (Boss.PrimeHurt): the number you did, gold and bigger on a weak point, grey when blocked
        public void ShowHit(BossHit hit, Vector3 at)
        {
            // A minigun blocked many times a second says it once in a while
            if (hit.Blocked)
            {
                if (Time.unscaledTime < blockedAt + 0.4f)
                    return;
                blockedAt = Time.unscaledTime;
            }
            var slot = pops[nextPop = (nextPop + 1) % pops.Length];
            if (slot.text == null)
                return;
            string label;
            Color colour;
            float size;
            if (hit.Blocked)
            {
                shieldPulse = 0.2f;
                label = "BLOCKED";
                colour = Spent;
                size = 28;
            }
            else
            {
                label = Mathf.RoundToInt(hit.Dealt).ToString();
                colour = hit.WeakPoint ? GuiPalette.Gold : GuiPalette.Cream;
                size = hit.WeakPoint ? 54 : 36;
            }
            if (!hit.Blocked && hit.Dealt < 0.5f)
                return;
            slot.text.text = label;
            slot.text.color = colour;
            slot.text.fontSize = size;
            slot.world = at;
            slot.age = 0;
            slot.live = true;
            slot.text.gameObject.SetActive(true);
            pops[nextPop] = slot;
        }

        // ---------------------------------------------------------------- Every frame

        private void LateUpdate()
        {
            if (state == null || root == null)
                return;
            var dt = Time.unscaledDeltaTime;
            var width = Mathf.Clamp(root.rect.width * 0.4f, 480, barWidth);
            if (!Mathf.Approximately(width, shownBarWidth))
                LayoutBar(width);
            Lives(dt);
            Bar(dt);
            Cards(dt);
            Pops(dt);
            Flags();
        }

        private void Lives(float dt)
        {
            // Before the fight starts all of them show
            var lives = state.Phase == BossPhase.Intro ? boss.Rules.lives : state.Lives;
            for (var i = 0; i < pips.Length; i++)
            {
                var alive = i < lives;
                var colour = alive ? GuiPalette.Amanita : Spent;
                var scale = 1f;
                if (!alive && pop[i] > 0)
                {
                    // The pip that went pops: big and white, then dim
                    pop[i] = Mathf.Max(0, pop[i] - dt);
                    colour = Color.Lerp(Spent, Color.white, pop[i] / 0.4f);
                    scale = 1 + 0.6f * pop[i] / 0.4f;
                }
                pips[i].SetColor(colour);
                var vector = Vector3.one * scale;
                if (pips[i].rectTransform.localScale != vector)
                    pips[i].rectTransform.localScale = vector;
            }
        }

        // The bar the fight is on now: the next one during a transition, none after the last
        private int CurrentBar()
        {
            switch (state.Phase)
            {
                case BossPhase.Transition12:
                    return 1;
                case BossPhase.Transition23:
                    return 2;
                case BossPhase.Victory:
                    return -1;
                case BossPhase.Intro:
                    return 0;
                default:
                    return Mathf.Clamp(state.Bar, 0, 2);
            }
        }

        private void Bar(float dt)
        {
            var current = CurrentBar();
            flash = Mathf.Max(0, flash - dt);
            for (var i = 0; i < 3; i++)
            {
                var amount = Mathf.Clamp01(state.BarHealth(i) / Mathf.Max(1, state.BarMax(i)));
                // A bar that lost health holds its white chip for a moment, then it catches up; a refilled bar has no chip
                if (amount < lastFill[i])
                    hold[i] = chipHold;
                lastFill[i] = amount;
                if (amount >= chip[i])
                    chip[i] = amount;
                else if (hold[i] > 0)
                    hold[i] -= dt;
                else
                    chip[i] = Mathf.MoveTowards(chip[i], amount, 1.5f * dt);
                fill[i].FillAmount = amount;
                chipShape[i].FillAmount = chip[i];
                var colour = i == current ? PrimeBlue : i < current || current < 0 ? Spent : Coming;
                if (i == current && flash > 0)
                    colour = Color.Lerp(PrimeBlue, GuiPalette.Gold, flash / 0.25f);
                fill[i].SetColor(colour);
                chipShape[i].SetColor(GuiPalette.WithAlpha(Color.white, i == current ? 0.9f : 0));
            }

            // The shield over the bar: the spore shield (nothing can hurt him), or the crown's half shield
            var shield = state.ShieldUp;
            var immune = state.Immune;
            GuiText.Show(shieldRoot, shield || immune);
            if (shield || immune)
            {
                shieldFront.FillAmount = shield ? 1 : 0.5f;
                shieldFront.SetColor(shield ? GuiPalette.Spore : GuiPalette.Gold);
                GuiText.Set(shieldLabel, shield ? "" : "½");
                shieldPulse = Mathf.Max(0, shieldPulse - dt);
                var vector = Vector3.one * (1 + 0.35f * shieldPulse / 0.2f);
                if (shieldRoot.localScale != vector)
                    shieldRoot.localScale = vector;
            }

            // STUNNED with the whole seconds left while a window is open
            var stunned = state.Vulnerable && state.Phase.IsFight();
            GuiText.Show(stunText, stunned);
            if (stunned)
                GuiText.Set(stunText, $"{(state.StunKind == BossStunKind.Daze ? "DAZED" : "STUNNED")}  {Mathf.CeilToInt(state.VulnerableLeft)}");

            var line = state.Phase.IsFight() ? boss.Objective : "";
            GuiText.Show(objectiveText, line.Length > 0);
            GuiText.Set(objectiveText, line);
        }

        private void Cards(float dt)
        {
            if (nameLeft > 0)
            {
                nameLeft -= dt;
                nameCard.gameObject.SetActive(nameLeft > 0);
                // Slides in from the right edge over 0.4 s, stays, slides out over the last 0.3 s; 5 px from the edge when in
                var slide = Mathf.Clamp01((nameCardSeconds - nameLeft) / 0.4f);
                var slideOut = Mathf.Clamp01(nameLeft / 0.3f);
                var t = Mathf.Min(1 - (1 - slide) * (1 - slide), slideOut);
                var offscreen = nameCard.rect.width + 20;
                nameCard.anchoredPosition = new Vector2(Mathf.Lerp(offscreen, -5, t), nameCard.anchoredPosition.y);
            }
            if (phaseDelay > 0)
            {
                phaseDelay -= dt;
                if (phaseDelay > 0)
                    return;
                phaseLeft = phaseCardSeconds;
            }
            if (phaseLeft > 0 && cardPhase.IsFight())
            {
                if (!phaseCard.gameObject.activeSelf)
                {
                    phaseCard.gameObject.SetActive(true);
                    GuiText.Set(phaseTitle, $"PHASE {cardPhase.BarOf() + 1} — {cardPhase.TitleOf()}");
                    GuiText.Set(phaseLine, boss.Objective);
                }
                phaseLeft -= dt;
                // Pops in big and settles, fades over the last half second
                var age = phaseCardSeconds - phaseLeft;
                var settle = Mathf.Clamp01(age / 0.2f);
                var vector = Vector3.one * Mathf.Lerp(1.3f, 1, 1 - (1 - settle) * (1 - settle));
                if (phaseCard.localScale != vector)
                    phaseCard.localScale = vector;
                phaseGroup.alpha = Mathf.Clamp01(phaseLeft / 0.5f);
                if (phaseLeft <= 0)
                    phaseCard.gameObject.SetActive(false);
            }
        }

        private void Pops(float dt)
        {
            if (view == null || !view.isActiveAndEnabled)
            {
                var you = boss.Match.You;
                view = you != null ? you.GetComponentInChildren<Camera>() : null;
                if (view == null)
                    view = Camera.main;
            }
            var uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            for (var i = 0; i < pops.Length; i++)
            {
                if (!pops[i].live)
                    continue;
                pops[i].age += dt;
                var life = pops[i].age / 0.8f;
                if (life >= 1 || view == null)
                {
                    pops[i].live = false;
                    pops[i].text.gameObject.SetActive(false);
                    continue;
                }
                var screen = view.WorldToScreenPoint(pops[i].world);
                if (screen.z <= 0)
                {
                    pops[i].text.gameObject.SetActive(false);
                    continue;
                }
                pops[i].text.gameObject.SetActive(true);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, uiCamera, out var local);
                // Rises while it fades; kept inside the screen's edges
                var centre = root.rect.center;
                var half = root.rect.size * 0.5f - new Vector2(155, 35);
                var at = local - centre + Vector2.up * (30 + 60 * life);
                at = new Vector2(Mathf.Clamp(at.x, -half.x, half.x), Mathf.Clamp(at.y, -half.y, half.y));
                pops[i].text.rectTransform.anchoredPosition = at;
                var colour = pops[i].text.color;
                colour.a = 1 - life * life;
                pops[i].text.color = colour;
            }
        }

        // The flag statuses: gone in phase 2 (no flags), the crown's words in phase 3
        private void Flags()
        {
            if (flagHud == null)
                return;
            var phase = state.Phase;
            var show = phase != BossPhase.Phase2 && phase != BossPhase.Transition12 && phase != BossPhase.Transition23;
            foreach (var panel in hud.flagStatusPanels)
                GuiText.Show(panel, show);
            if (phase != BossPhase.Phase3 || hud.flagStatus.Length <= (int)Team.Blue)
                return;
            foreach (var flag in boss.Flags.State.Flags)
            {
                if (flag.Team != Team.Blue)
                    continue;
                string text;
                if (state.CrownCaptured)
                    text = "The crown: on your stand";
                else if (flag.Status == FlagStatus.Carried && flag.Carrier != null)
                    text = $"The crown: carried by <color=#{GuiPalette.HexOf(FlagHud.Colour(flag.Carrier.Team))}>{flag.Carrier.Name}</color>";
                else if (flag.Status == FlagStatus.Dropped)
                    text = $"The crown: dropped · home in {Mathf.CeilToInt(flag.ReturnIn)} s";
                else
                    text = "The crown: on the wreck";
                GuiText.Set(hud.flagStatus[(int)Team.Blue], text);
            }
        }
    }
}
