using UnityEngine;
using UnityEngine.EventSystems;

namespace SdfGui
{
    /// <summary>
    /// The squishy feel of a toon button: it swells and its edge wobbles under the pointer, squashes when pressed and springs back
    /// with an overshoot. Only the scale and the wobble change, at runtime only, so the button looks in the scene as it does at rest.
    /// </summary>
    public sealed class ToonButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public ToonShape shape;
        public float hoverScale = 1.07f;
        public Vector2 pressScale = new Vector2(1.1f, 0.88f);
        public float hoverWobble = 2.5f;
        public float stiffness = 420;
        public float damping = 16;

        private bool hovered;
        private bool pressed;
        private Vector2 scale = Vector2.one;
        private Vector2 velocity;
        private float restWobble;

        private void Awake()
        {
            if (shape == null)
                shape = GetComponent<ToonShape>();
            if (shape != null)
                restWobble = shape.Wobble;
        }

        private void OnDisable()
        {
            hovered = pressed = false;
            scale = Vector2.one;
            velocity = Vector2.zero;
            transform.localScale = Vector3.one;
            if (shape != null)
                shape.Wobble = restWobble;
        }

        private void Update()
        {
            var target = pressed ? pressScale : hovered ? Vector2.one * hoverScale : Vector2.one;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 1 / 30f);
            velocity += (target - scale) * (stiffness * dt);
            velocity *= Mathf.Exp(-damping * dt);
            scale += velocity * dt;
            transform.localScale = new Vector3(scale.x, scale.y, 1);
            if (shape != null)
                shape.Wobble = hovered ? hoverWobble : restWobble;
        }

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;
        public void OnPointerExit(PointerEventData eventData) => hovered = pressed = false;
        public void OnPointerDown(PointerEventData eventData) => pressed = true;

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            // A kick outwards on release, for the overshoot
            velocity += new Vector2(-1.5f, 2.5f);
        }

        public void OnSelect(BaseEventData eventData) => hovered = true;
        public void OnDeselect(BaseEventData eventData) => hovered = false;
    }
}
