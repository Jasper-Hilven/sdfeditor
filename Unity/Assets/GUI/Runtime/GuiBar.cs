using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>A bar that fills from the left, with its value written on it.</summary>
    [ExecuteAlways]
    public sealed class GuiBar : MonoBehaviour
    {
        public ToonShape bar;
        public TMP_Text label;
        [Range(0, 1)] public float value = 0.7f;
        public string format = "{0:0}";
        public float scale = 100;

        public void Set(float fraction)
        {
            value = Mathf.Clamp01(fraction);
            Apply();
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void Apply()
        {
            if (bar != null)
                bar.FillAmount = value;
            GuiText.Set(label, string.Format(format, value * scale));
        }
    }
}
