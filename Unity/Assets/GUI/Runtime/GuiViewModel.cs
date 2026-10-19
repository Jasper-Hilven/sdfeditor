using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The HUD that is part of the world: the first-person minigun with a glowing spore tank on its side that empties as you fire
    /// (its count written under it) and a glow in each floating hand that fades from spore cyan to red as you lose health and pulses
    /// when it is low. At runtime the gun also kicks back and shakes while firing; at rest it sits where the scene has it.
    /// </summary>
    public sealed class GuiViewModel : GuiView
    {
        public Transform gun;
        public ToonShape tank;
        public TMP_Text tankLabel;
        public ToonShape[] handGlows = new ToonShape[0];
        public float kick = 0.03f;

        private Vector3 restPosition;
        private bool rested;

        protected override void OnEnable()
        {
            if (gun != null)
            {
                restPosition = gun.localPosition;
                rested = true;
            }
            base.OnEnable();
        }

        protected override void Render(FakeMatch m)
        {
            var fraction = m.ammo / (float)Mathf.Max(1, m.magazine);
            if (tank != null)
            {
                tank.FillAmount = m.reload > 0 ? m.reload : fraction;
                tank.SetColor(fraction < 0.2f && m.reload <= 0 ? GuiPalette.Amanita : GuiPalette.Spore);
            }
            GuiText.Set(tankLabel, m.reload > 0 ? "..." : m.ammo.ToString());

            var health = m.health / Mathf.Max(1, m.maxHealth);
            var pulse = health < 0.3f && Application.isPlaying ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8) : 1;
            var glowColour = Color.Lerp(GuiPalette.Amanita, GuiPalette.Spore, health);
            foreach (var glow in handGlows)
                glow?.SetColor(GuiPalette.WithAlpha(glowColour, (0.35f + 0.55f * health) * pulse * (m.YouAreAlive ? 1 : 0)));

            if (gun != null && rested && Application.isPlaying)
            {
                var shake = m.spread * kick;
                gun.localPosition = restPosition + new Vector3(Random.Range(-1f, 1f) * shake * 0.3f, Random.Range(-1f, 1f) * shake * 0.3f, -shake);
            }
        }
    }
}
