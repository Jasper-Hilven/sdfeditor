using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The in-round HUD: crosshair and hit marker, health and spores (ammo, reload ring), the round clock with the island's crumbled
    /// rings, players left and your kills, the crumble warning, the elimination pop-up, the damage splats and vignette, the pick-up
    /// prompt, and what you spectate once you are out. All of it from the match, every frame.
    /// </summary>
    public sealed class GuiHud : GuiView
    {
        [Header("Aim")]
        public RectTransform crosshair;
        public RectTransform[] crosshairArms = new RectTransform[4];
        public float crosshairGap = 12;
        public float crosshairSpread = 26;
        public ToonShape hitMarker;

        [Header("Health and spores")]
        public GuiBar health;
        public TMP_Text ammo;
        public TMP_Text magazine;
        public GuiBar ammoBar;
        public ToonShape reloadRing;
        public TMP_Text reloadLabel;

        [Header("Round")]
        public TMP_Text clock;
        public TMP_Text ringLabel;
        public ToonShape[] ringPips = new ToonShape[0];
        public TMP_Text alive;
        public TMP_Text kills;

        [Header("Moments")]
        public GameObject crumbleBanner;
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

        private float smoothedFrame = 1 / 60f;

        protected override void Render(FakeMatch m)
        {
            var you = m.You;
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

            // Health and spores
            health?.Set(m.health / Mathf.Max(1, m.maxHealth));
            if (health != null && health.bar != null)
                health.bar.SetColor(m.health < 30 ? GuiPalette.Amanita : GuiPalette.Moss);
            GuiText.Set(ammo, m.ammo.ToString());
            GuiText.Set(magazine, "/ " + m.magazine);
            if (ammo != null)
                ammo.color = m.ammo < m.magazine * 0.2f ? GuiPalette.Amanita : GuiPalette.Cream;
            ammoBar?.Set(m.ammo / (float)Mathf.Max(1, m.magazine));
            var reloading = m.reload > 0;
            GuiText.Show(reloadRing, reloading);
            GuiText.Show(reloadLabel, reloading);
            if (reloadRing != null)
                reloadRing.FillAmount = m.reload;

            // Round
            GuiText.Set(clock, GuiText.Clock(m.timeLeft));
            if (clock != null)
                clock.color = m.timeLeft < 30 ? GuiPalette.Amanita : GuiPalette.Ink;
            GuiText.Set(ringLabel, $"EDGE {m.ringsFallen}/{m.rings}");
            for (var i = 0; i < ringPips.Length; i++)
                ringPips[i]?.SetColor(i < m.ringsFallen ? GuiPalette.Amanita : GuiPalette.Moss);
            GuiText.Set(alive, m.Alive.ToString());
            GuiText.Set(kills, (you?.kills ?? 0).ToString());

            // Moments
            GuiText.Show(crumbleBanner, m.crumbleWarning > 0);
            GuiText.Show(eliminatedPopup, m.eliminated > 0);
            if (m.eliminated > 0 && eliminatedPopup != null)
            {
                GuiText.Set(eliminatedTitle, m.eliminatedHeadshot ? "CAP POPPED!" : "ELIMINATED");
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
            GuiText.Show(spectating, !aliveYou);
            if (!aliveYou)
                GuiText.Set(spectatingLabel, "SPECTATING  " + GuiText.Name(m.Player(m.spectating)));

            if (fps != null)
            {
                GuiText.Show(fps, GuiSettings.ShowFps);
                if (Application.isPlaying)
                    smoothedFrame = Mathf.Lerp(smoothedFrame, Time.unscaledDeltaTime, 0.05f);
                GuiText.Set(fps, $"{Mathf.RoundToInt(1 / Mathf.Max(smoothedFrame, 1e-4f))} FPS");
            }
        }
    }
}
