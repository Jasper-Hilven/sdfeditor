using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    public enum TouchButtonKind
    {
        /// <summary>The big fire button under the right thumb; a drag that starts on it looks as well.</summary>
        Fire,
        /// <summary>The small fire button on the left, over the joystick.</summary>
        FireLeft,
        Jump,
        /// <summary>The sniper's zoom: a tap zooms in, the next tap out.</summary>
        Zoom,
        /// <summary>The weapon slot <see cref="TouchButton.index"/> + 1.</summary>
        Slot,
        /// <summary>The power <see cref="TouchButton.index"/> (Frenzy, Regenerate, Thick Hide), shown when the rage meter is full.</summary>
        Power,
        /// <summary>Hold for the scoreboard.</summary>
        Scoreboard,
        Pause,
    }

    /// <summary>One round button of the touch layer (ticket 56). Its look is the prefab's; the layer fills in what it shows.</summary>
    public sealed class TouchButton : MonoBehaviour
    {
        public TouchButtonKind kind;
        public int index;
        public ToonShape shape;
        public Image icon;
        public TMP_Text label;
        [Tooltip("The colour at rest; a slot takes the HUD's slot colour instead")]
        public Color rest = Color.white;

        /// <summary>A finger is on it.</summary>
        [System.NonSerialized] public bool down;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>Whether a screen point (pixels, overlay canvas) is on the button: its ellipse, a tenth bigger so a thumb that is a little off still hits.</summary>
        public bool Contains(Vector2 screenPoint)
        {
            var corners = new Vector3[4];
            Rect.GetWorldCorners(corners);
            var centre = (corners[0] + corners[2]) * 0.5f;
            var half = new Vector2(corners[2].x - corners[0].x, corners[2].y - corners[0].y) * 0.5f;
            var off = new Vector2((screenPoint.x - centre.x) / Mathf.Max(half.x, 0.01f), (screenPoint.y - centre.y) / Mathf.Max(half.y, 0.01f));
            return off.sqrMagnitude <= 1.21f;
        }

        /// <summary>A finger is on it or lifted: it glows while pressed.</summary>
        public void Press(bool pressed)
        {
            down = pressed;
            if (shape != null)
                shape.Glow = pressed ? 16 : 0;
        }

        private void OnDisable() => Press(false);
    }
}
