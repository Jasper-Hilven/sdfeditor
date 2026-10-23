using SdfGui;
using SdfLevel;
using TMPro;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The spores on the HUD (ticket 25), 5 px above the health panel and as small, 5 px from the screen's left edge, one of three
    /// things at a time:
    /// <list type="bullet">
    /// <item>filling: the spore meter only (a copy of the health panel: its name, then a spore-coloured bar);</item>
    /// <item>full: the three powers only, a card each in key order (the key as the player's layout prints it, Q F C or A F C on
    /// AZERTY, and the power's name), their keys glowing up and down in the power's colour and the cards breathing a little, so a
    /// full meter is noticed; no meter;</item>
    /// <item>a power on: its name and the whole seconds it has left, nothing else; and a faint tint of its colour at the screen's
    /// edge.</item>
    /// </list>
    /// Nothing while you are dead. The pieces are the HUD prefab's (<see cref="GuiHud"/>'s spore fields, laid out by the GUI's
    /// builder, so the scene shows them as play does); this shows one at a time and fills it in.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class SporeHud : MonoBehaviour
    {
        [Tooltip("The full cards' keys' glow, from and to, in pixels")]
        public Vector2 fullGlow = new Vector2(2, 12);
        [Tooltip("How much bigger the full cards grow at the glow's height")]
        public float fullSwell = 0.025f;
        [Tooltip("Glows up and down per second")]
        public float fullPulses = 0.8f;

        private Spores spores;
        private bool bound;
        private GameObject meter;
        private GuiBar meterBar;
        private GameObject[] cards;
        private TMP_Text[] cardLetters;
        private ToonShape[] cardKeys;
        private GameObject active;
        private TMP_Text activeLabel;
        private ToonShape tint;
        private KeyPlace[] labelled;
        private string[] letters;

        public static SporeHud Create(Spores spores)
        {
            var hud = spores.gameObject.AddComponent<SporeHud>();
            hud.spores = spores;
            return hud;
        }

        /// <summary>Finds the match's HUD and its spore pieces.</summary>
        private bool Bind()
        {
            if (bound)
                return true;
            if (spores == null || spores.State == null)
                return false;
            var matchHud = spores.Match != null ? spores.Match.Hud : null;
            var hud = matchHud != null ? matchHud.GetComponentInChildren<GuiHud>(true) : null;
            if (hud == null || hud.sporeMeter == null || hud.sporeCards.Length < SporeRules.All.Length || hud.sporeLetters.Length < SporeRules.All.Length)
                return false;
            meter = hud.sporeMeter;
            meterBar = hud.sporeBar;
            cards = hud.sporeCards;
            cardLetters = hud.sporeLetters;
            cardKeys = new ToonShape[cards.Length];
            for (var i = 0; i < cards.Length; i++)
            {
                var key = cards[i] != null ? cards[i].transform.Find("Key") : null;
                cardKeys[i] = key != null ? key.GetComponent<ToonShape>() : null;
            }
            active = hud.sporePower;
            activeLabel = hud.sporePowerLabel;
            tint = hud.sporeTint;
            labelled = Spores.Keys;
            letters = new string[labelled.Length];
            bound = true;
            return true;
        }

        private void Show(bool showMeter, bool showCards, bool showActive)
        {
            GuiText.Show(meter, showMeter);
            foreach (var card in cards)
                GuiText.Show(card, showCards);
            GuiText.Show(active, showActive);
        }

        /// <summary>The full cards glow up and down together: their keys' glow in the power's colour, and the cards a little bigger.</summary>
        private void Pulse()
        {
            var up = 0.5f - 0.5f * Mathf.Cos(Time.time * fullPulses * 2 * Mathf.PI);
            var glow = Mathf.Lerp(fullGlow.x, fullGlow.y, up);
            var scale = Vector3.one * (1 + fullSwell * up);
            for (var i = 0; i < cards.Length; i++)
            {
                if (cardKeys[i] != null)
                    cardKeys[i].Glow = glow;
                if (cards[i] != null)
                    cards[i].transform.localScale = scale;
            }
        }

        private void LateUpdate()
        {
            if (!Bind())
                return;
            var you = spores.Match.You;
            var state = spores.State;
            // A level with the spores off (the meadow, before 42 has heard of them): no meter, cards or power line at all
            if (!state.Enabled)
            {
                Show(false, false, false);
                return;
            }
            var alive = you != null && you.State.Alive;
            var m = alive ? state.MeterOf(you.State) : null;
            var on = m != null && m.Active;
            var full = m != null && !on && state.IsFull(you.State);
            Show(alive && !on && !full, full, on);

            if (alive && !on && !full && meterBar != null)
                meterBar.Set(state.Share(you.State));
            if (full)
            {
                // The letters as the layout prints them; looked up again as the cards show, so a layout switched mid-match is followed
                for (var i = 0; i < cards.Length; i++)
                {
                    var letter = GameKeys.Label(labelled[i]);
                    if (letter != letters[i])
                    {
                        letters[i] = letter;
                        GuiText.Set(cardLetters[i], letter);
                    }
                }
                Pulse();
            }
            if (on)
            {
                var power = m.Power.Value;
                GuiText.Set(activeLabel, $"{SporeRules.NameOf(power)}  {Mathf.CeilToInt(m.TimeLeft)}");
                if (activeLabel != null)
                    activeLabel.color = Spores.ColourOf(power);
            }
            if (tint != null)
            {
                var alpha = on ? 0.16f + 0.06f * Mathf.Sin(Time.time * 3) : 0;
                tint.SetColor(GuiPalette.WithAlpha(on ? Spores.ColourOf(m.Power.Value) : GuiPalette.Spore, alpha));
            }
        }
    }
}
