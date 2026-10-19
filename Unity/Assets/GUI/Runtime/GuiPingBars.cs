using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>Three bars that light up green, orange or red by the ping, like any online shooter.</summary>
    [ExecuteAlways]
    public sealed class GuiPingBars : MonoBehaviour
    {
        public ToonShape[] bars = new ToonShape[0];
        public TMP_Text label;
        public int ping = 40;

        public void Set(int value)
        {
            ping = value;
            Apply();
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void Apply()
        {
            var lit = ping < 60 ? 3 : ping < 120 ? 2 : 1;
            var tone = lit == 3 ? GuiPalette.Moss : lit == 2 ? GuiPalette.Chanterelle : GuiPalette.Amanita;
            for (var i = 0; i < bars.Length; i++)
                if (bars[i] != null)
                    bars[i].SetColor(i < lit ? tone : GuiPalette.WithAlpha(GuiPalette.Dusk, 0.8f));
            GuiText.Set(label, ping.ToString());
        }
    }
}
