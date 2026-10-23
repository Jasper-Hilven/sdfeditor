using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfLevel
{
    /// <summary>
    /// The level's sun shadow as one shadow map over the whole level, rendered once from the sun and fixed in the world, instead of URP's
    /// cascades round the camera. A level is static and its sun does not move, so the map never needs redrawing; and with nothing
    /// depending on where the camera is, no shadow changes as one walks: no cascade borders where a shadow's outline snaps finer or
    /// coarser, no caster culled away from the view (a giant's long shadow appearing as it comes into view), the same resolution
    /// everywhere. It is lower than the cascades' near the eye, which the soft edge and the toon bands hide. The scene's light casts no
    /// shadows of its own (LevelBuilder turns them off), which also saves URP's shadow pass every frame.
    /// <para>
    /// Into the map go every renderer of the scene that casts shadows (of a LOD group, only LOD0's) and every instance of the
    /// <see cref="scatter"/> renderer that casts them (<see cref="InstancedScatterRenderer.ShadowCasters"/>), through the shadow caster
    /// pass of <see cref="casterMaterial"/> (the toon shader's) with URP's own bias conventions. The map covers the casters' bounds as
    /// the sun sees them; the toon and ground shaders read it (SdfStaticShadow.hlsl) wherever it is on. Things that move (players) cast
    /// none in it. It is rendered before the first camera, and again when the quality preset or the light's direction changes.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public sealed class StaticSunShadow : MonoBehaviour
    {
        private static readonly int MapId = Shader.PropertyToID("_SdfStaticShadowMap");
        private static readonly int MatrixId = Shader.PropertyToID("_SdfStaticShadowMatrix");
        private static readonly int ParamsId = Shader.PropertyToID("_SdfStaticShadowParams");
        private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");

        [Tooltip("The light whose shadow this is")]
        public Light sun;
        [Tooltip("The instanced renderer whose shadow-casting instances go into the map; none for a level without one")]
        public InstancedScatterRenderer scatter;
        [Tooltip("A material with the toon shader, whose ShadowCaster pass draws the map")]
        public Material casterMaterial;
        [Tooltip("Texels along each side of the map")]
        public int resolution = 4096;
        [Tooltip("Texels along each side on the low preset")]
        public int lowResolution = 2048;
        [Tooltip("The highest quality level that uses the low resolution; the levels below it do too")]
        public string lowQualityLevel = "Integrated GPU / Web";
        [Tooltip("Depth bias in texels, as URP's light settings")]
        public float depthBias = 1;
        [Tooltip("Normal bias in texels, as URP's light settings")]
        public float normalBias = 1;

        private RenderTexture map;
        private Material caster;
        private Matrix4x4 worldToMap;
        private float texel;
        private Quaternion renderedRotation;
        private int renderedSize;

        private void OnEnable() => RenderPipelineManager.beginContextRendering += BeforeCameras;

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= BeforeCameras;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Release();
        }

        private void OnValidate() => Release();

        private void Release()
        {
            if (map != null)
            {
                map.Release();
                DestroyImmediate(map);
            }
            if (caster != null)
                DestroyImmediate(caster);
            map = null;
            caster = null;
        }

        private bool IsLowPreset()
        {
            var names = QualitySettings.names;
            var level = QualitySettings.GetQualityLevel();
            // The quality levels go best first, so every level from the low one down is low
            var low = System.Array.IndexOf(names, lowQualityLevel);
            return low >= 0 && level >= low;
        }

        private void BeforeCameras(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (sun == null || casterMaterial == null)
                return;
            var low = IsLowPreset();
            if (map == null || Size(low) != renderedSize || sun.transform.rotation != renderedRotation)
                Render(context, low);
            if (map == null)
                return;
            // Every frame: other scenes or a domain reload may have changed the globals
            Shader.SetGlobalTexture(MapId, map);
            Shader.SetGlobalMatrix(MatrixId, worldToMap);
            Shader.SetGlobalVector(ParamsId, new Vector4(1, texel, 0, 0));
        }

        /// <summary>The map's texels a side: <see cref="lowResolution"/> on the low preset, else <see cref="resolution"/>, larger on Epic and up
        /// (<see cref="SdfMushrooms.QualityExtras.SunShadowResolution"/>), as far as the GPU allows.</summary>
        private int Size(bool low) => low ? lowResolution
            : Mathf.Min(Mathf.RoundToInt(resolution * SdfMushrooms.QualityExtras.SunShadowResolution), SystemInfo.maxTextureSize);

        private void Render(ScriptableRenderContext context, bool low)
        {
            Release();
            renderedSize = Size(low);
            renderedRotation = sun.transform.rotation;

            // The casters: the scene's renderers (LOD0's of a LOD group) and the scattered instances, each with its bounding sphere
            var meshes = new List<(Mesh mesh, Matrix4x4 matrix)>();
            var spheres = new List<(Vector3 centre, float radius)>();
            var finer = new HashSet<Renderer>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var group in root.GetComponentsInChildren<LODGroup>())
                {
                    var lods = group.GetLODs();
                    for (var level = 1; level < lods.Length; level++)
                        foreach (var renderer in lods[level].renderers)
                            if (renderer != null)
                                finer.Add(renderer);
                }
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.Off || finer.Contains(renderer))
                        continue;
                    if (!renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null)
                        continue;
                    var mesh = filter.sharedMesh;
                    meshes.Add((mesh, renderer.localToWorldMatrix));
                    spheres.Add((renderer.bounds.center, renderer.bounds.extents.magnitude));
                }
            var batches = new List<(Mesh mesh, Matrix4x4[] matrices)>();
            if (scatter != null)
                scatter.ShadowCasters(low,
                    (mesh, matrices, count) =>
                    {
                        var copy = new Matrix4x4[count];
                        System.Array.Copy(matrices, copy, count);
                        batches.Add((mesh, copy));
                    },
                    (centre, radius) => spheres.Add((centre, radius)));
            if (spheres.Count == 0)
                return;

            // The sun's view: looking along the light (down -z, as Unity's views do), round all the casters' spheres
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.Rotate(Quaternion.Inverse(sun.transform.rotation));
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var (centre, radius) in spheres)
            {
                var p = view.MultiplyPoint3x4(centre);
                min = Vector3.Min(min, p - Vector3.one * radius);
                max = Vector3.Max(max, p + Vector3.one * radius);
            }
            var projection = Matrix4x4.Ortho(min.x, max.x, min.y, max.y, -max.z - 1, -min.z + 1);
            var size = Size(low);
            texel = 1f / size;

            map = new RenderTexture(size, size, 24, RenderTextureFormat.Shadowmap)
            {
                name = "Static sun shadow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            map.Create();
            caster = new Material(casterMaterial) { name = "Static sun shadow caster", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            caster.shaderKeywords = System.Array.Empty<string>();
            var pass = caster.FindPass("ShadowCaster");

            // URP's bias for a directional light (ShadowUtils.GetShadowBias): texels of the map in metres, twice as much for the soft edge
            var texelMetres = Mathf.Max(max.x - min.x, max.y - min.y) / size;
            var bias = new Vector4(-depthBias * texelMetres * 2, -normalBias * texelMetres * 2, 1, 0);

            var commands = new CommandBuffer { name = "Static sun shadow" };
            commands.SetRenderTarget(map);
            commands.ClearRenderTarget(true, false, Color.clear);
            commands.SetViewProjectionMatrices(view, projection);
            commands.SetGlobalVector(ShadowBiasId, bias);
            commands.SetGlobalVector(LightDirectionId, -sun.transform.forward);
            foreach (var (mesh, matrix) in meshes)
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                    commands.DrawMesh(mesh, matrix, caster, sub, pass);
            foreach (var (mesh, matrices) in batches)
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                    commands.DrawMeshInstanced(mesh, sub, caster, pass, matrices, matrices.Length);
            context.ExecuteCommandBuffer(commands);
            context.Submit();
            commands.Release();

            // World to the map's (u, v, depth), as URP's GetShadowTransform: depth turned round where the depth buffer is reversed
            if (SystemInfo.usesReversedZBuffer)
            {
                projection.m20 = -projection.m20;
                projection.m21 = -projection.m21;
                projection.m22 = -projection.m22;
                projection.m23 = -projection.m23;
            }
            var scaleAndBias = Matrix4x4.identity;
            scaleAndBias.m00 = scaleAndBias.m11 = scaleAndBias.m22 = 0.5f;
            scaleAndBias.m03 = scaleAndBias.m13 = scaleAndBias.m23 = 0.5f;
            worldToMap = scaleAndBias * projection * view;
            Debug.Log($"SDF level: static sun shadow of {gameObject.scene.name}, {size}² over {max.x - min.x:F0} × {max.y - min.y:F0} m " +
                $"({texelMetres * 100:F1} cm a texel), {meshes.Count} renderers and {batches.Count} instanced batches");
        }
    }
}
