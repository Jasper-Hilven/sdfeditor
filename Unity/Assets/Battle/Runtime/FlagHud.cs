using SdfGui;
using TMPro;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The capture-the-flag HUD (ticket 21), added to the match's GUI by <see cref="CaptureTheFlag"/>; its pieces are the HUD prefab's
    /// (<see cref="GuiHud"/>'s flag fields, laid out by the GUI's builder, so the scene shows them as play does), which this fills in:
    /// <list type="bullet">
    /// <item>beside the clock, on its row, the team score and outside it a status per team's flag (at home, carried by a name, or
    /// dropped with the seconds until it goes home): [red flag] 2 [clock] 1 [blue flag], mirrored. On a screen too narrow for that
    /// beside the kill feed (4:3 and the like) the statuses go under the clock;</item>
    /// <item>a marker over each flag that is not at home (not the one on your back), pinned to the screen's edge with an arrow when the
    /// flag is off screen, with its distance;</item>
    /// <item>banners: "You have the flag", "Blue took your flag", "Red scored", "Your flag was returned" and the like;</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FlagHud : MonoBehaviour
    {
        public float bannerSeconds = 3;
        [Tooltip("Markers keep this far inside the screen's edge, from their middle: the arrow's tip reaches 48 out, so 5 px from the edge")]
        public float markerMargin = 53;

        /// <summary>The team score's room either side of the clock, and the flag statuses' size and room outside the score.</summary>
        public const float ScoreGap = 16, StatusGap = 58, StatusWidth = 280;

        private CaptureTheFlag ctf;
        private MatchHud matchHud;
        private GuiHud hud;
        private RectTransform root;
        private Canvas canvas;

        private TMP_Text[] scores;
        // The flag statuses' outer edge from the middle when beside the score; under the clock instead when the screen is narrow
        private float statusReach;
        private float clockCentreY, clockBottom, edge;
        private float shownWidth = -1;
        private Camera view;
        // What the texts show, so they are only rebuilt (and their strings made) when it changes
        private int shownScore = -1;
        private readonly int[] shownStatus = { -1, -1 };
        private readonly int[] shownMetres = { -1, -1 };
        private RectTransform[] statusPanels;
        private TMP_Text[] status;
        private RectTransform[] markers;
        private RectTransform[] markerArrows;
        private TMP_Text[] markerLabels;
        private ToonShape banner;
        private TMP_Text bannerText;
        private float bannerLeft;

        private Team YourTeam => ctf.Match.You != null ? ctf.Match.You.Team : ctf.Match.yourTeam;

        /// <summary>Builds the flag HUD into the GUI that <paramref name="matchHud"/> feeds; null when the GUI has no HUD screen.</summary>
        public static FlagHud Create(CaptureTheFlag ctf, MatchHud matchHud)
        {
            var hud = matchHud.view != null ? matchHud.view.GetComponentInChildren<GuiHud>(true) : null;
            if (hud == null || hud.teamScores.Length < Teams.Count || hud.flagStatusPanels.Length < Teams.Count || hud.flagMarkers.Length < Teams.Count)
            {
                Debug.LogWarning("CaptureTheFlag: the GUI's HUD has no flag pieces; rebuild it (Tools > SDF GUI > Rebuild Prefabs and Scene); no flag HUD");
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
            scores = hud.teamScores;
            statusPanels = hud.flagStatusPanels;
            status = hud.flagStatus;
            markers = hud.flagMarkers;
            markerArrows = hud.flagMarkerArrows;
            markerLabels = hud.flagMarkerLabels;
            banner = hud.flagBanner;
            bannerText = hud.flagBannerText;

            // The clock's panel: the score and the flag statuses sit on its row, either side
            clockCentreY = -50f;
            var clockHalfWidth = 115f;
            clockBottom = -95f;
            if (hud.clock != null && hud.clock.transform.parent is RectTransform clockPanel && clockPanel != root
                && clockPanel.anchorMin == clockPanel.anchorMax && Mathf.Approximately(clockPanel.anchorMin.y, 1))
            {
                var size = clockPanel.rect.size;
                clockCentreY = clockPanel.anchoredPosition.y + (0.5f - clockPanel.pivot.y) * size.y;
                clockHalfWidth = size.x * 0.5f;
                clockBottom = clockCentreY - size.y * 0.5f;
            }
            edge = clockHalfWidth + ScoreGap;
            statusReach = edge + StatusGap + StatusWidth;
            foreach (var marker in markers)
                GuiText.Show(marker, false);
            if (banner != null)
                banner.gameObject.SetActive(false);
        }

        /// <summary>
        /// The flag statuses beside the score when the screen has room for them left of the kill feed (which reaches about 415 in from
        /// the right edge past them), else under the clock; and the banner under whichever is lowest.
        /// </summary>
        private void Arrange(float width)
        {
            var beside = width * 0.5f >= statusReach + 415;
            foreach (var team in Teams.All)
            {
                var sign = team == Team.Red ? -1 : 1;
                statusPanels[(int)team].anchoredPosition = beside
                    ? new Vector2(sign * (edge + StatusGap), clockCentreY)
                    : new Vector2(sign * 8, clockBottom - 42);
            }
            var bottom = beside ? clockBottom : clockBottom - 68;
            if (banner != null)
            {
                var rect = banner.rectTransform;
                rect.anchoredPosition = new Vector2(0, bottom - 18 - (1 - rect.pivot.y) * 92);
            }
        }

        // ---------------------------------------------------------------- Every frame

        public static Color Colour(Team team) => team == Team.Red ? GuiPalette.Amanita : GuiPalette.Hex(0x3D7BEA);

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
                shownWidth = width;
                Arrange(width);
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
