using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    public enum ToonShapeKind
    {
        Box = 0,
        Pill = 1,
        Ring = 2,
        Cap = 3,
        Splat = 4,
        Burst = 5,
        Glow = 6,
        Vignette = 7,
        Arrow = 8,
        Cross = 9,
        Check = 10,
    }

    public enum ToonFillMode
    {
        None = 0,
        Horizontal = 1,
        Vertical = 2,
        Radial = 3,
    }

    /// <summary>
    /// A shape of the GUI drawn by SdfGui/Toon Shape: a distance field on one quad the size of the rect, grown by what reaches outside
    /// it (drop shadow, glow, wobble). Its parameters go in the vertices (see the shader), so every shape shares the one material and a
    /// canvas batches them; the canvas is told to pass the extra texture coordinates. The fill colour is the graphic's colour.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("UI/Toon Shape")]
    public sealed class ToonShape : MaskableGraphic
    {
        private const AdditionalCanvasShaderChannels Channels =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;

        [SerializeField] private ToonShapeKind kind = ToonShapeKind.Box;
        [Tooltip("Corner radius of a box, rounding of an arrow")]
        [SerializeField] private float cornerRadius = 24;
        [Tooltip("Width of the ink line inside the edge; 0 leaves it out")]
        [SerializeField] private float outline = 5;
        [Tooltip("How far the hard ink shadow falls down (and a little to the right); 0 leaves it out")]
        [SerializeField] private float shadow = 6;
        [SerializeField] private ToonFillMode fillMode = ToonFillMode.None;
        [Range(0, 1)] [SerializeField] private float fillAmount = 1;
        [Tooltip("How strongly the amanita spots show")]
        [Range(0, 1)] [SerializeField] private float spots;
        [Tooltip("How far the edge wobbles, animated")]
        [SerializeField] private float wobble;
        [Tooltip("Width of the soft glow outside the edge, in the fill colour")]
        [SerializeField] private float glow;
        [Tooltip("Varies splats, bursts and the spot pattern")]
        [SerializeField] private float seed;
        [Tooltip("How much of the lower shade band and the gloss spot show")]
        [Range(0, 1)] [SerializeField] private float shading = 1;
        [Tooltip("Line width of a ring, cross or check")]
        [SerializeField] private float thickness = 10;

        public ToonShapeKind Kind { get => kind; set => Set(ref kind, value); }
        public float CornerRadius { get => cornerRadius; set => Set(ref cornerRadius, value); }
        public float Outline { get => outline; set => Set(ref outline, value); }
        public float Shadow { get => shadow; set => Set(ref shadow, value); }
        public ToonFillMode FillMode { get => fillMode; set => Set(ref fillMode, value); }
        public float FillAmount { get => fillAmount; set => Set(ref fillAmount, Mathf.Clamp01(value)); }
        public float Spots { get => spots; set => Set(ref spots, value); }
        public float Wobble { get => wobble; set => Set(ref wobble, value); }
        public float Glow { get => glow; set => Set(ref glow, value); }
        public float Seed { get => seed; set => Set(ref seed, value); }
        public float Shading { get => shading; set => Set(ref shading, value); }
        public float Thickness { get => thickness; set => Set(ref thickness, value); }

        private void Set<T>(ref T field, T value)
        {
            if (Equals(field, value))
                return;
            field = value;
            SetVerticesDirty();
        }

        /// <summary>Sets the colour only when it changes, so views that set it every frame cost nothing while it stays.</summary>
        public void SetColor(Color value)
        {
            if (color != value)
                color = value;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (canvas != null)
                canvas.additionalShaderChannels |= Channels;
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            if (canvas != null)
                canvas.additionalShaderChannels |= Channels;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            var half = rect.size * 0.5f;
            var centre = rect.center;
            var pad = Mathf.Max(0, shadow) * 1.4f + Mathf.Max(0, glow) + Mathf.Abs(wobble) + 2;
            if (kind == ToonShapeKind.Glow || kind == ToonShapeKind.Vignette)
                pad = 0;

            var uv1 = new Vector4((float)kind, cornerRadius, outline, shadow);
            var uv2 = new Vector4(fillAmount, (float)fillMode, spots, wobble);
            var uv3 = new Vector4(glow, seed, shading, thickness);
            Color32 tint = color;
            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector2(i == 1 || i == 2 ? 1 : -1, i >= 2 ? -1 : 1);
                var offset = Vector2.Scale(corner, half + Vector2.one * pad);
                vh.AddVert(new UIVertex
                {
                    position = centre + offset,
                    color = tint,
                    uv0 = new Vector4(offset.x, offset.y, Mathf.Max(half.x, 0.01f), Mathf.Max(half.y, 0.01f)),
                    uv1 = uv1,
                    uv2 = uv2,
                    uv3 = uv3,
                    normal = Vector3.back,
                    tangent = new Vector4(1, 0, 0, -1),
                });
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}
