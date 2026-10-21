using SdfGui;
using TMPro;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The capture-the-flag HUD (ticket 21), added to the match's GUI by <see cref="CaptureTheFlag"/> and built at runtime inside the HUD
    /// screen, in its look (its shapes' material and its fonts are borrowed from the HUD's own elements), so the GUI's prefabs and
    /// builders stay as they are:
    /// <list type="bullet">
    /// <item>beside the clock, on its row, the team score: RED ●●○ 2 [clock] 1 ●○○ BLUE, mirrored, the pips filling outward from the
    /// clock; it replaces the battle-royale "edge" rings and the players-left panel, which are hidden;</item>
    /// <item>under the clock a status per team's flag: at home, carried by a name, or dropped with the seconds until it goes home;</item>
    /// <item>a marker over each flag that is not at home (not the one on your back), pinned to the screen's edge with an arrow when the
    /// flag is off screen, with its distance;</item>
    /// <item>banners: "You have the flag", "Blue took your flag", "Red scored", "Your flag was returned" and the like;</item>
    /// <item>the round's result as a team result ("Red wins 3 – 1") instead of last mushroom standing.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FlagHud : MonoBehaviour
    {
        public float bannerSeconds = 3;
        [Tooltip("Markers keep this far inside the screen's edge")]
        public float markerMargin = 70;

        private CaptureTheFlag ctf;
        private MatchHud matchHud;
        private GuiHud hud;
        private RectTransform root;
        private Canvas canvas;

        // Borrowed from the HUD: the look of its shapes and texts
        private Material shapeMaterial;
        private TMP_FontAsset titleFont, bodyFont;
        private Material titleMaterial, bodyMaterial;

        private readonly TMP_Text[] scores = new TMP_Text[Teams.Count];
        private readonly TMP_Text[] names = new TMP_Text[Teams.Count];
        // The team names' outer edge from the middle; they give way to the kill feed when the screen is narrow (4:3 and the like)
        private float namesReach;
        private float shownWidth = -1;
        private Camera view;
        // What the texts show, so they are only rebuilt (and their strings made) when it changes
        private int shownScore = -1;
        private readonly int[] shownStatus = { -1, -1 };
        private readonly int[] shownMetres = { -1, -1 };
        private readonly ToonShape[,] pips = new ToonShape[Teams.Count, 3];
        private readonly TMP_Text[] status = new TMP_Text[Teams.Count];
        private readonly RectTransform[] markers = new RectTransform[Teams.Count];
        private readonly RectTransform[] markerArrows = new RectTransform[Teams.Count];
        private readonly TMP_Text[] markerLabels = new TMP_Text[Teams.Count];
        private ToonShape banner;
        private TMP_Text bannerText;
        private float bannerLeft;
        private GuiRoundResult[] results = new GuiRoundResult[0];

        private Team YourTeam => ctf.Match.You != null ? ctf.Match.You.Team : ctf.Match.yourTeam;

        /// <summary>Builds the flag HUD into the GUI that <paramref name="matchHud"/> feeds; null when the GUI has no HUD screen.</summary>
        public static FlagHud Create(CaptureTheFlag ctf, MatchHud matchHud)
        {
            var hud = matchHud.view != null ? matchHud.view.GetComponentInChildren<GuiHud>(true) : null;
            if (hud == null)
            {
                Debug.LogWarning("CaptureTheFlag: the GUI has no HUD screen (GuiHud); no flag HUD");
                return null;
            }
            var flagHud = hud.gameObject.AddComponent<FlagHud>();
            flagHud.ctf = ctf;
            flagHud.matchHud = matchHud;
            flagHud.hud = hud;
            flagHud.Build();
            var state = ctf.State;
            state.Taken += flagHud.OnTaken;
            state.Dropped += flagHud.OnDropped;
            state.Returned += flagHud.OnReturned;
            state.Captured += flagHud.OnCaptured;
            ctf.Match.State.RoundReset += flagHud.OnRoundReset;
            return flagHud;
        }

        private void OnDestroy()
        {
            if (ctf == null || ctf.State == null)
                return;
            ctf.State.Taken -= OnTaken;
            ctf.State.Dropped -= OnDropped;
            ctf.State.Returned -= OnReturned;
            ctf.State.Captured -= OnCaptured;
            if (ctf.Match != null && ctf.Match.State != null)
                ctf.Match.State.RoundReset -= OnRoundReset;
        }

        // ---------------------------------------------------------------- Building

        private void Build()
        {
            root = (RectTransform)hud.transform;
            canvas = hud.GetComponentInParent<Canvas>();
            var anyShape = hud.ringPips != null && hud.ringPips.Length > 0 && hud.ringPips[0] != null ? hud.ringPips[0] : hud.GetComponentInChildren<ToonShape>(true);
            shapeMaterial = anyShape != null ? anyShape.material : null;
            var title = hud.ringLabel != null ? hud.ringLabel : hud.clock;
            if (title != null)
                (titleFont, titleMaterial) = (title.font, title.fontSharedMaterial);
            var body = hud.pickupLabel != null ? hud.pickupLabel : title;
            if (body != null)
                (bodyFont, bodyMaterial) = (body.font, body.fontSharedMaterial);

            // Battle royale's players left and island edge make way; your kills move into the corner
            HideHolder(hud.alive);
            HideHolder(hud.ringLabel);
            if (hud.kills != null && hud.kills.transform.parent != root && hud.kills.transform.parent is RectTransform kills)
                kills.anchoredPosition = new Vector2(50, kills.anchoredPosition.y);

            // The clock's panel: the score sits on its row, either side, and the flag statuses under it
            var clockCentreY = -85f;
            var clockHalfWidth = 115f;
            var clockBottom = -130f;
            if (hud.clock != null && hud.clock.transform.parent is RectTransform clockPanel && clockPanel != root
                && clockPanel.anchorMin == clockPanel.anchorMax && Mathf.Approximately(clockPanel.anchorMin.y, 1))
            {
                var size = clockPanel.rect.size;
                clockCentreY = clockPanel.anchoredPosition.y + (0.5f - clockPanel.pivot.y) * size.y;
                clockHalfWidth = size.x * 0.5f;
                clockBottom = clockCentreY - size.y * 0.5f;
            }

            // Beside the clock, mirrored: RED ●●○ 2 [ 2:14 ] 1 ●○○ BLUE
            var row = Holder("Team Score", root, new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 60));
            row.pivot = new Vector2(0.5f, 0.5f);
            row.anchoredPosition = new Vector2(0, clockCentreY);
            var edge = clockHalfWidth + 20;
            namesReach = edge + 292;
            foreach (var team in Teams.All)
            {
                var sign = team == Team.Red ? -1 : 1;
                scores[(int)team] = Text(row, $"{team.Name()} Score", "0", true, 52, Colour(team), new Vector2(sign * (edge + 25), 2), new Vector2(50, 60));
                for (var i = 0; i < 3; i++)
                {
                    var pip = Shape(row, $"{team.Name()} Pip {i + 1}", ToonShapeKind.Pill, Dark, new Vector2(sign * (edge + 78 + i * 34), 0), new Vector2(28, 28));
                    pip.Outline = 3.5f;
                    pip.Shadow = 3;
                    pips[(int)team, i] = pip;
                }
                names[(int)team] = Text(row, $"{team.Name()} Name", team.Name().ToUpperInvariant(), true, 32, Colour(team), new Vector2(sign * (edge + 237), 0),
                    new Vector2(110, 44), null, team == Team.Red ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);

                // Under the clock: the flag's status, in a panel each side of the middle
                var panel = Shape(root, $"{team.Name()} Flag Status", ToonShapeKind.Box, GuiPalette.WithAlpha(GuiPalette.Night, 0.85f),
                    new Vector2(sign * 8, clockBottom - 16), new Vector2(330, 52), new Vector2(0.5f, 1), new Vector2(team == Team.Red ? 1 : 0, 1));
                panel.CornerRadius = 18;
                panel.Outline = 4;
                panel.Shadow = 5;
                var pole = Shape(panel.transform, "Pole", ToonShapeKind.Box, GuiPalette.Cream, new Vector2(22, 0), new Vector2(6, 34), new Vector2(0, 0.5f));
                pole.CornerRadius = 3;
                pole.Outline = 2;
                pole.Shadow = 0;
                var cloth = Shape(panel.transform, "Cloth", ToonShapeKind.Box, Colour(team), new Vector2(27, 7), new Vector2(26, 18), new Vector2(0, 0.5f));
                cloth.CornerRadius = 4;
                cloth.Outline = 2.5f;
                cloth.Shadow = 0;
                status[(int)team] = Text(panel.transform, "Status", "At home", false, 26, GuiPalette.Cream, new Vector2(64, 0), new Vector2(256, 44),
                    new Vector2(0, 0.5f), TextAlignmentOptions.Left);

                // The marker over the flag when it is away
                var marker = Holder($"{team.Name()} Flag Marker", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
                var dot = Shape(marker, "Dot", ToonShapeKind.Pill, Colour(team), Vector2.zero, new Vector2(30, 30));
                dot.Outline = 3.5f;
                dot.Shadow = 3;
                var arrow = Shape(marker, "Arrow", ToonShapeKind.Arrow, Colour(team), Vector2.zero, new Vector2(30, 34));
                arrow.CornerRadius = 4;
                arrow.Outline = 3;
                arrow.Shadow = 0;
                arrow.rectTransform.pivot = new Vector2(-0.6f, 0.5f);
                markerArrows[(int)team] = arrow.rectTransform;
                markerLabels[(int)team] = Text(marker, "Distance", "", false, 24, GuiPalette.Cream, new Vector2(0, -34), new Vector2(120, 30));
                markers[(int)team] = marker;
                marker.gameObject.SetActive(false);
            }

            // The banner for what happens to the flags, like the crumble warning
            if (hud.crumbleBanner != null)
            {
                var copy = Instantiate(hud.crumbleBanner, root);
                copy.name = "Flag Banner";
                banner = copy.GetComponent<ToonShape>();
                foreach (var text in copy.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "Hint")
                        text.gameObject.SetActive(false);
                    else if (bannerText == null)
                        bannerText = text;
                }
                var rect = (RectTransform)copy.transform;
                rect.anchoredPosition = new Vector2(0, BannerY(rect, clockBottom));
                rect.sizeDelta = new Vector2(900, 92);
            }
            else
            {
                banner = Shape(root, "Flag Banner", ToonShapeKind.Box, GuiPalette.Amanita, new Vector2(0, clockBottom - 86), new Vector2(900, 92), new Vector2(0.5f, 1));
                banner.CornerRadius = 26;
                bannerText = Text(banner.transform, "Title", "", true, 46, GuiPalette.Cream, Vector2.zero, new Vector2(880, 80));
            }
            if (bannerText != null)
                bannerText.fontSize = 46;
            banner.gameObject.SetActive(false);

            results = matchHud.view.GetComponentsInChildren<GuiRoundResult>(true);
        }

        /// <summary>The banner's y so its top sits under the status panels (which end 68 under the clock), whatever its pivot.</summary>
        private static float BannerY(RectTransform rect, float clockBottom) => clockBottom - 86 - (1 - rect.pivot.y) * 92;

        private void HideHolder(Component element)
        {
            if (element == null)
                return;
            var holder = element.transform.parent;
            (holder != null && holder != root ? holder.gameObject : element.gameObject).SetActive(false);
        }

        private static RectTransform Holder(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>A shape in the HUD's look; anchored to the parent's middle unless told otherwise.</summary>
        private ToonShape Shape(Transform parent, string name, ToonShapeKind kind, Color colour, Vector2 position, Vector2 size,
            Vector2? anchor = null, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var shape = go.AddComponent<ToonShape>();
            if (shapeMaterial != null)
                shape.material = shapeMaterial;
            shape.color = colour;
            shape.Kind = kind;
            shape.raycastTarget = false;
            Place(shape.rectTransform, anchor ?? new Vector2(0.5f, 0.5f), position, size, pivot);
            return shape;
        }

        private TMP_Text Text(Transform parent, string name, string text, bool title, float size, Color colour, Vector2 position, Vector2 box,
            Vector2? anchor = null, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            var font = title ? titleFont : bodyFont;
            var material = title ? titleMaterial : bodyMaterial;
            if (font != null)
                label.font = font;
            if (material != null)
                label.fontSharedMaterial = material;
            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.richText = true;
            Place(label.rectTransform, anchor ?? new Vector2(0.5f, 0.5f), position, box, anchor);
            return label;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // ---------------------------------------------------------------- Every frame

        private static readonly Color Dark = GuiPalette.WithAlpha(GuiPalette.Night, 0.9f);

        private static Color Colour(Team team) => team == Team.Red ? GuiPalette.Amanita : GuiPalette.Hex(0x3D7BEA);

        private static string Hex(Team team) => GuiPalette.HexOf(Colour(team));

        private static string Named(PlayerState player) => player == null ? "?" : $"<color=#{Hex(player.Team)}>{player.Name}</color>";

        private void LateUpdate()
        {
            if (ctf == null || ctf.State == null)
                return;
            var match = ctf.Match.State;

            var score = match.Score(Team.Red) * 1000 + match.Score(Team.Blue);
            if (score != shownScore)
            {
                shownScore = score;
                foreach (var team in Teams.All)
                    GuiText.Set(scores[(int)team], match.Score(team).ToString());
            }
            var width = root.rect.width;
            if (width != shownWidth)
            {
                // The kill feed reaches about 510 in from the right edge: on a narrow screen (4:3) the names make way, pips and scores stay
                shownWidth = width;
                var roomy = width * 0.5f >= namesReach + 460;
                foreach (var label in names)
                    GuiText.Show(label, roomy);
            }
            var toWin = match.Rules.capturesToWin;
            foreach (var team in Teams.All)
                for (var i = 0; i < 3; i++)
                {
                    var pip = pips[(int)team, i];
                    GuiText.Show(pip, i < toWin);
                    pip.SetColor(i < match.Score(team) ? Colour(team) : Dark);
                }

            var you = ctf.Match.You;
            foreach (var flag in ctf.State.Flags)
            {
                var t = (int)flag.Team;
                var key = (int)flag.Status * 100000 + (flag.Carrier != null ? flag.Carrier.Index + 1 : 0) * 1000 + Mathf.CeilToInt(flag.ReturnIn);
                if (key != shownStatus[t])
                {
                    shownStatus[t] = key;
                    GuiText.Set(status[t], flag.Status switch
                    {
                        FlagStatus.Carried => flag.Carrier != null && you != null && flag.Carrier == you.State ? "On your back!" : $"Carried by {Named(flag.Carrier)}",
                        FlagStatus.Dropped => $"Dropped · home in {Mathf.CeilToInt(flag.ReturnIn)} s",
                        _ => "At home",
                    });
                }
                Mark(flag, you);
            }

            if (bannerLeft > 0)
            {
                bannerLeft -= Time.deltaTime;
                // Pops in big and settles, like the elimination pop-up
                var t = Mathf.Clamp01((bannerSeconds - bannerLeft) / 0.25f);
                var scale = Vector3.one * Mathf.Lerp(1.4f, 1, 1 - (1 - t) * (1 - t));
                if (banner.transform.localScale != scale)
                    banner.transform.localScale = scale;
                if (bannerLeft <= 0)
                    banner.gameObject.SetActive(false);
            }

            ShowResult(match);
        }

        /// <summary>The marker over a flag that is away from home: over it on screen, or at the edge pointing to it.</summary>
        private void Mark(TeamFlag flag, Player you)
        {
            var marker = markers[(int)flag.Team];
            var flagObject = ctf.FlagObject(flag.Team);
            if (view == null || !view.isActiveAndEnabled)
                view = you != null ? you.GetComponentInChildren<Camera>() : null;
            if (view == null)
                view = Camera.main;
            var camera = view;
            var show = flag.Status != FlagStatus.Home && flagObject != null && camera != null
                       && !(flag.Status == FlagStatus.Carried && you != null && flag.Carrier == you.State);
            GuiText.Show(marker, show);
            if (!show)
                return;

            var world = flagObject.position + Vector3.up * 2.7f;
            var screen = camera.WorldToScreenPoint(world);
            var behind = screen.z < 0;
            if (behind)
                screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0);
            var uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, uiCamera, out var local);
            var rect = root.rect;
            var centre = rect.center;
            var half = rect.size * 0.5f - Vector2.one * markerMargin;
            var offset = local - centre;
            var outside = behind || Mathf.Abs(offset.x) > half.x || Mathf.Abs(offset.y) > half.y;
            if (outside)
            {
                if (behind && offset.sqrMagnitude < 1)
                    offset = Vector2.down;
                var fit = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(offset.x), 1e-3f), half.y / Mathf.Max(Mathf.Abs(offset.y), 1e-3f));
                offset *= fit;
            }
            // Local points are from the root's pivot, which the marker is anchored to
            marker.anchorMin = marker.anchorMax = root.pivot;
            marker.anchoredPosition = centre + offset;
            var arrow = markerArrows[(int)flag.Team];
            GuiText.Show(arrow, outside);
            if (outside)
                arrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg);
            var metres = Mathf.RoundToInt(Vector3.Distance(camera.transform.position, flagObject.position));
            if (metres != shownMetres[(int)flag.Team])
            {
                shownMetres[(int)flag.Team] = metres;
                GuiText.Set(markerLabels[(int)flag.Team], $"{metres} m");
            }
        }

        /// <summary>The round's end as a team result, over the GUI's last-mushroom-standing words.</summary>
        private void ShowResult(MatchState match)
        {
            if (match.Phase != RoundPhase.RoundEnd || match.Winner == null)
                return;
            var winner = match.Winner.Value;
            var title = winner == YourTeam ? "YOUR TEAM WINS!" : $"{winner.Name().ToUpperInvariant()} WINS";
            var detail = $"<color=#{Hex(Team.Red)}>Red {match.Score(Team.Red)}</color>  –  <color=#{Hex(Team.Blue)}>{match.Score(Team.Blue)} Blue</color>";
            foreach (var result in results)
            {
                if (result == null || result.death || !result.isActiveAndEnabled)
                    continue;
                GuiText.Set(result.title, title);
                GuiText.Set(result.detail, detail);
            }
        }

        // ---------------------------------------------------------------- Banners

        private void Banner(string text, Team colour)
        {
            if (banner == null)
                return;
            banner.SetColor(Colour(colour));
            banner.Spots = colour == Team.Red ? 0.55f : 0;
            GuiText.Set(bannerText, text);
            banner.gameObject.SetActive(true);
            bannerLeft = bannerSeconds;
        }

        private bool IsYou(PlayerState player) => player != null && ctf.Match.You != null && ctf.Match.You.State == player;

        private void OnTaken(TeamFlag flag, PlayerState by)
        {
            if (IsYou(by))
                Banner("YOU HAVE THE FLAG!", YourTeam);
            else if (flag.Team == YourTeam)
                Banner($"{by.Team.Name().ToUpperInvariant()} TOOK YOUR FLAG!", by.Team);
            else
                Banner($"{by.Name.ToUpperInvariant()} HAS THE {flag.Team.Name().ToUpperInvariant()} FLAG", by.Team);
        }

        private void OnDropped(TeamFlag flag, PlayerState by) =>
            Banner(flag.Team == YourTeam ? "YOUR FLAG WAS DROPPED" : $"THE {flag.Team.Name().ToUpperInvariant()} FLAG WAS DROPPED", flag.Team);

        private void OnReturned(TeamFlag flag, FlagReturn why, PlayerState by)
        {
            if (why == FlagReturn.Captured || why == FlagReturn.RoundReset)
                return;
            Banner(flag.Team == YourTeam ? "YOUR FLAG WAS RETURNED" : $"THE {flag.Team.Name().ToUpperInvariant()} FLAG WAS RETURNED", flag.Team);
        }

        private void OnCaptured(TeamFlag flag, PlayerState by)
        {
            var team = by.Team;
            Banner(IsYou(by) ? "YOU SCORED!" : $"{team.Name().ToUpperInvariant()} SCORED!", team);
        }

        private void OnRoundReset()
        {
            bannerLeft = 0;
            if (banner != null)
                banner.gameObject.SetActive(false);
        }
    }
}
