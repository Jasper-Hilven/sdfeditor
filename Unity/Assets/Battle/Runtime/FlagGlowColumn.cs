using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A tall glow column of a team's colour over its flag (ticket 21), so everyone sees across the field where each flag is: on its stand,
    /// on a carrier's back or where it lies. A child of the flag object, so it goes wherever the flag goes; drawn by
    /// <c>Resources/FlagGlowColumn.shader</c> as one camera-facing quad that stays upright however the flag leans. The pods have short,
    /// dim ones of their own (<see cref="PodBeacon"/>).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FlagGlowColumn : MonoBehaviour
    {
        public const string ShaderName = "FlagGlowColumn";

        private static Mesh quad;
        private Material material;

        /// <summary>Puts a column on <paramref name="flag"/> (or a pod), in <paramref name="colour"/>; null when the shader is missing.</summary>
        public static FlagGlowColumn Create(Transform flag, Color colour, float height, float width, float intensity = 1.6f)
        {
            var shader = Resources.Load<Shader>(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"CaptureTheFlag: no shader Resources/{ShaderName}; the flags have no glow columns");
                return null;
            }
            var go = new GameObject("Glow Column", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(flag, false);
            var column = go.AddComponent<FlagGlowColumn>();
            column.material = new Material(shader) { name = $"Glow Column {flag.name}" };
            column.material.SetColor("_Color", colour.linear);
            column.material.SetFloat("_Height", height);
            column.material.SetFloat("_Width", width);
            column.material.SetFloat("_Intensity", intensity);
            go.GetComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = column.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return column;
        }

        /// <summary>Another colour from now on (the crown's gold on the blue flag's column, ticket 33.6).</summary>
        public void SetColour(Color colour)
        {
            if (material != null)
                material.SetColor("_Color", colour.linear);
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }

        /// <summary>The shared quad: the shader spreads it out, so its bounds are what keep it from being culled.</summary>
        private static Mesh Quad()
        {
            if (quad != null)
                return quad;
            quad = new Mesh { name = "Flag Glow Column" };
            quad.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
            quad.uv = new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // Up to 60 m tall and a few metres wide however it is turned
            quad.bounds = new Bounds(new Vector3(0, 30, 0), new Vector3(8, 62, 8));
            quad.UploadMeshData(true);
            return quad;
        }
    }
}
