using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// Scales a rect laid out at a fixed size (the 1920 x 1080 reference) down to fit inside its parent, keeping its proportions, so a
    /// screen laid out for 16:9 keeps all of its content on screen at any aspect (a 16:10 laptop, a wide browser window with its
    /// toolbars). It never scales up: at 16:9 and wider-and-taller canvases the layout is as built.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class GuiFitInside : MonoBehaviour
    {
        private RectTransform rect;

        private void OnEnable()
        {
            rect = (RectTransform)transform;
            Fit();
        }

        private void LateUpdate() => Fit();

        /// <summary>Fits again now, even if the parent's size has not changed (a snapshot calls it after sizing the canvas).</summary>
        public void Refit()
        {
            Fit();
        }

        private void Fit()
        {
            rect ??= (RectTransform)transform;
            if (rect.parent is not RectTransform parent)
                return;
            var size = parent.rect.size;
            var own = rect.sizeDelta;
            if (own.x <= 0 || own.y <= 0 || size.x <= 0 || size.y <= 0)
                return;
            // Read every frame (a touch layout may have resized or reset the card): setting an unchanged scale costs nothing
            var scale = Mathf.Min(1, Mathf.Min(size.x / own.x, size.y / own.y));
            if (!Mathf.Approximately(rect.localScale.x, scale))
                rect.localScale = new Vector3(scale, scale, 1);
        }
    }
}
