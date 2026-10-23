using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The in-round HUD: crosshair and hit marker, health, the round clock, the elimination pop-up, the damage splats and vignette, the
    /// pick-up prompt, and what you spectate once you are out, all of it from the match, every frame. And the battle's pieces, laid out
    /// here so the scene shows what play does, which the battle's own HUD scripts fill in (SdfBattle's CombatHud, SporeHud, FlagHud):
    /// the weapon slots, the spore meter, power cards and power line, the team score and the flags' statuses, markers and banner.
    /// <para>
    /// Where the frame rate shows, a countdown of <see cref="AdjustIn"/> seconds to a change of quality level when the frame rate has
    /// stayed high (over <see cref="FastFps"/> for <see cref="FastFor"/> seconds, with a better level left) or low (under
    /// <see cref="SlowFps"/> for <see cref="SlowFor"/> seconds, with a faster level left), then the change, whether or not the settings
    /// show the frame rate, unless the settings dismiss it.
    /// </para>
    /// </summary>
    public sealed class GuiHud : GuiView
    {
        [Header("Aim")]
        public RectTransform crosshair;
        public RectTransform[] crosshairArms = new RectTransform[4];
        public float crosshairGap = 12;
        public float crosshairSpread = 26;
        public ToonShape hitMarker;

        [Header("Health")]
        public GuiBar health;

        [Header("Round")]
        public TMP_Text clock;

        [Header("Moments")]
        public GameObject eliminatedPopup;
        public TMP_Text eliminatedTitle;
        public TMP_Text eliminatedName;
        public RectTransform damageDirection;
        public ToonShape[] damageSplats = new ToonShape[0];
        public ToonShape vignette;
        public GameObject pickupPrompt;
        public TMP_Text pickupLabel;
        public GameObject spectating;
        public TMP_Text spectatingLabel;
        public TMP_Text fps;

        [Header("Weapons (the battle's CombatHud)")]
        [Tooltip("The weapon in hand, over the slots")]
        public TMP_Text weaponLabel;
        public ToonShape[] weaponSlots = new ToonShape[0];
        public Image[] weaponIcons = new Image[0];
        public TMP_Text[] weaponNumbers = new TMP_Text[0];

        [Header("Spores (the battle's SporeHud)")]
        public GameObject sporeMeter;
        public GuiBar sporeBar;
        [Tooltip("A card per power, in key order")]
        public GameObject[] sporeCards = new GameObject[0];
        public TMP_Text[] sporeLetters = new TMP_Text[0];
        public GameObject sporePower;
        public TMP_Text sporePowerLabel;
        public ToonShape sporeTint;

        [Header("Flags (the battle's FlagHud), red then blue")]
        public TMP_Text[] teamScores = new TMP_Text[0];
        public RectTransform[] flagStatusPanels = new RectTransform[0];
        public TMP_Text[] flagStatus = new TMP_Text[0];
        public RectTransform[] flagMarkers = new RectTransform[0];
        public RectTransform[] flagMarkerArrows = new RectTransform[0];
        public TMP_Text[] flagMarkerLabels = new TMP_Text[0];
        public ToonShape flagBanner;
        public TMP_Text flagBannerText;

        public const float FastFps = 120, FastFor = 10, SlowFps = 40, SlowFor = 6;
        /// <summary>The seconds the HUD counts down before it changes the quality level.</summary>
        public const float AdjustIn = 5;

        private float smoothedFrame = 1 / 60f;
        private float fastTime;
        private float slowTime;
        private float countdown = -1;
        private int countdownStep;

        protected override void Render(MatchView m)
        {
            var aliveYou = m.YouAreAlive;

            // Crosshair: the arms move apart with the spread
            GuiText.Show(crosshair, aliveYou);
            var gap = crosshairGap + crosshairSpread * m.spread;
            for (var i = 0; i < crosshairArms.Length; i++)
            {
                if (crosshairArms[i] == null)
                    continue;
                var direction = i switch { 0 => Vector2.up, 1 => Vector2.right, 2 => Vector2.down, _ => Vector2.left };
                var position = direction * gap;
                if (crosshairArms[i].anchoredPosition != position)
                    crosshairArms[i].anchoredPosition = position;
            }
            if (hitMarker != null)
            {
                hitMarker.SetColor(GuiPalette.WithAlpha(m.hitMarkerKill ? GuiPalette.Amanita : GuiPalette.Cream, m.hitMarker));
                var scale = Vector3.one * (1 + 0.35f * m.hitMarker);
                if (hitMarker.rectTransform.localScale != scale)
                    hitMarker.rectTransform.localScale = scale;
            }

            // Health
            health?.Set(m.health / Mathf.Max(1, m.maxHealth));
            if (health != null && health.bar != null)
                health.bar.SetColor(m.health < 30 ? GuiPalette.Amanita : GuiPalette.Moss);

            // Round
            // Sudden death has no time left to show: the clock says what decides it instead
            GuiText.Set(clock, m.suddenDeath ? (m.suddenDeathOnKills ? "NEXT KILL WINS" : "SUDDEN DEATH") : GuiText.Clock(m.timeLeft));
            if (clock != null)
                clock.color = m.suddenDeath || m.timeLeft < 30 ? GuiPalette.Amanita : GuiPalette.Cream;

            // Moments
            GuiText.Show(eliminatedPopup, m.eliminated > 0);
            if (m.eliminated > 0 && eliminatedPopup != null)
            {
                GuiText.Set(eliminatedTitle, !string.IsNullOrEmpty(m.eliminatedTitle) ? m.eliminatedTitle : m.eliminatedHeadshot ? "CAP POPPED!" : "ELIMINATED");
                GuiText.Set(eliminatedName, GuiText.Name(m.Player(m.eliminatedPlayer)));
                // Pops in big and settles
                var t = Mathf.Clamp01((2.2f - m.eliminated) / 0.25f);
                var scale = Vector3.one * Mathf.Lerp(1.6f, 1, 1 - (1 - t) * (1 - t));
                if (eliminatedPopup.transform.localScale != scale)
                    eliminatedPopup.transform.localScale = scale;
            }
            if (damageDirection != null)
            {
                var rotation = Quaternion.Euler(0, 0, -m.damageAngle);
                if (damageDirection.localRotation != rotation)
                    damageDirection.localRotation = rotation;
            }
            foreach (var splat in damageSplats)
                splat?.SetColor(GuiPalette.WithAlpha(splat.color, m.damage));
            if (vignette != null)
            {
                var low = aliveYou && m.health < 30 ? 0.35f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 6) : 0;
                vignette.SetColor(GuiPalette.WithAlpha(GuiPalette.AmanitaDark, Mathf.Max(m.damage * 0.85f, low)));
            }
            var offered = aliveYou && !string.IsNullOrEmpty(m.pickup);
            GuiText.Show(pickupPrompt, offered);
            GuiText.Set(pickupLabel, $"Pick up <color=#{GuiPalette.HexOf(GuiPalette.Gold)}>{m.pickup}</color>");
            // Only while watching someone: the battle's death camera watches nobody, and "spectating the island" says nothing
            var watched = aliveYou ? null : m.Player(m.spectating);
            GuiText.Show(spectating, watched != null);
            if (watched != null)
                GuiText.Set(spectatingLabel, "SPECTATING  " + GuiText.Name(watched));

            if (fps != null)
            {
                if (Application.isPlaying)
                    smoothedFrame = Mathf.Lerp(smoothedFrame, Time.unscaledDeltaTime, 0.05f);
                var rate = 1 / Mathf.Max(smoothedFrame, 1e-4f);
                string hint = null;
                if (Application.isPlaying && !GuiSettings.DismissQualityHints)
                    hint = QualityCountdown(rate);
                else
                    (fastTime, slowTime, countdown) = (0, 0, -1);
                GuiText.Show(fps, GuiSettings.ShowFps || hint != null);
                GuiText.Set(fps, hint ?? $"{Mathf.RoundToInt(rate)} FPS");
                // The countdown in ink, the frame rate in moss green
                var colour = hint != null ? GuiPalette.Ink : GuiPalette.Moss;
                if (fps.color != colour)
                    fps.color = colour;
            }
        }

        /// <summary>
        /// The quality countdown for this frame rate, or null: towards a better level (quality levels go best first) after a long while
        /// fast, a faster one after a while slow. It counts <see cref="AdjustIn"/> seconds down to 0 in real time, then takes that level and
        /// starts the timing again; it stops if the frame rate leaves the range first.
        /// </summary>
        private string QualityCountdown(float rate)
        {
            var level = QualitySettings.GetQualityLevel();
            var better = level > 0;
            var faster = level < QualitySettings.names.Length - 1;
            var dt = Time.unscaledDeltaTime;
            fastTime = better && rate > FastFps ? fastTime + dt : 0;
            slowTime = faster && rate < SlowFps ? slowTime + dt : 0;
            var step = fastTime >= FastFor ? -1 : slowTime >= SlowFor ? 1 : 0;
            if (step == 0)
            {
                countdown = -1;
                return null;
            }
            if (countdown < 0 || step != countdownStep)
                (countdown, countdownStep) = (AdjustIn, step);
            else
                countdown -= dt;
            if (countdown <= 0)
            {
                QualitySettings.SetQualityLevel(level + step, true);
                (fastTime, slowTime, countdown) = (0, 0, -1);
                return null;
            }
            // 5 to 0, each a second but the first and last half a second
            return $"Adjusting quality in {Mathf.RoundToInt(countdown)}";
        }
    }
}
