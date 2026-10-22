using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfLevel
{
    /// <summary>
    /// How <see cref="InstancedScatterRenderer"/> draws a group of instances (foliage, trees, ...), from the level file's <c>draw</c>
    /// section (level.json "drawGroups"): rings of distance round the camera, ring k out to <see cref="rings"/>[k] metres drawn at level
    /// of detail k (or the model's coarsest), nothing beyond the last. The nearest <see cref="outlineRings"/> rings get the ink outline and
    /// the nearest <see cref="shadowRings"/> cast shadows (<see cref="lowShadowRings"/> on the low preset). Neighbouring rings cross-fade
    /// over <see cref="blend"/> metres round their border and the last fades out over its last <see cref="fade"/> metres, by dither in
    /// the shader. <see cref="wind"/> is metres of sway at <see cref="windHeight"/> above the base (0 for none: a static group), and the
    /// lowest <see cref="tintHeight"/> metres take the instance's tint. The integrated GPU / web preset draws <see cref="lowDensity"/> of
    /// the instances (by their hash, so the same ones each frame) with every distance times <see cref="lowDistance"/>.
    /// </summary>
    [Serializable]
    public sealed class ScatterDrawGroup
    {
        public string name;
        public float[] rings = { 6, 15, 35 };
        public int outlineRings;
        public int shadowRings;
        public int lowShadowRings;
        public float blend = 1;
        public float fade;
        public float wind;
        public float windHeight = 1;
        public float tintHeight;
        [Range(0, 1)] public float lowDensity = 1;
        [Range(0.1f, 1)] public float lowDistance = 1;
    }

    /// <summary>
    /// One mesh of a prototype at one level of detail, placed in the prototype's space; drawn with the outline when the ring has one and
    /// <see cref="outline"/> is on, casting shadows when the ring does and <see cref="shadows"/> is on (a castle part's <c>shadows</c>
    /// setting: off, or near, which leaves it on at LOD0 only).
    /// </summary>
    [Serializable]
    public sealed class ScatterPart
    {
        public Mesh mesh;
        public Matrix4x4 transform = Matrix4x4.identity;
        public bool outline = true;
        public bool shadows = true;
    }

    [Serializable]
    public sealed class ScatterLevel
    {
        public ScatterPart[] parts = Array.Empty<ScatterPart>();
    }

    /// <summary>
    /// A model the renderer draws many of, by <see cref="name"/> (the model's, as instances.bin names it), in draw group
    /// <see cref="group"/>: its levels of detail, each a set of parts. Taken from <see cref="prefab"/> (one of MushroomBuilder's, read,
    /// never instantiated) when <see cref="levels"/> is empty; given directly otherwise, e.g. a castle's part meshes.
    /// </summary>
    [Serializable]
    public sealed class ScatterPrototype
    {
        public string name;
        public string group;
        public GameObject prefab;
        public ScatterLevel[] levels = Array.Empty<ScatterLevel>();
        [Tooltip("Not drawn beyond this many metres (a detail piece's cull setting); 0 for as far as its group's rings reach")]
        public float cull;
    }

    /// <summary>
    /// An instance given in the scene rather than in instances.bin: a prototype at a position, turned and scaled (Unity's coordinates).
    /// Give a mirror image as a mirrored mesh, not a negative scale: Graphics.RenderMeshInstanced does not turn the triangles round
    /// for an instance whose matrix mirrors, so it would show its back faces (the culling and radii take the scale's size either way).
    /// </summary>
    [Serializable]
    public struct ScatterPlacement
    {
        public int prototype;
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
    }

    /// <summary>
    /// Draws everything a level holds in quantity (grass turfs and flowers, later trees and crest clusters, a castle's repeated parts)
    /// with GPU instancing, never as GameObjects: draw calls grow with prototypes × parts × rings, not with instances. One per level
    /// scene, written by LevelBuilder; its GameObject is not static (static batching would merge the meshes) and has no colliders.
    /// <para>
    /// Instances come from <see cref="instances"/> (the level's instances.bin, see <see cref="ScatterInstances"/>) and from
    /// <see cref="placements"/>; each names a prototype, each prototype a draw group with its own rings (<see cref="ScatterDrawGroup"/>).
    /// Every camera that renders (the game's, the scene view's, a snapshot's), just before it does: the cells of the instance grid that
    /// are in reach and in view are gone through, each instance in reach is checked against the view (with a margin for its shadow) and
    /// put into each ring it is in (two near a border, where the rings cross-fade), and each prototype's parts are drawn per ring with
    /// <see cref="Graphics.RenderMeshInstanced{T}(in RenderParams, Mesh, int, T[], int, int)"/>, at most <see cref="BatchSize"/> at a
    /// time, with the outline material too in the outlined rings. The shader (SdfScatter.hlsl, the toon shaders' SDF_SCATTER variant)
    /// sways them in the wind, tints their base and dithers the rings' borders, so the CPU never fades anything itself.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public sealed class InstancedScatterRenderer : MonoBehaviour
    {
        /// <summary>
        /// Instances per draw: under the 1023 Unity allows, and within the 500 its instancing arrays hold on most platforms (WebGL among
        /// them), so a draw is never split behind our back and the per-instance tint array always lines up.
        /// </summary>
        public const int BatchSize = 500;

        /// <summary>How far round an instance its shadow may fall into view, in metres: it is drawn when that close to the view.</summary>
        private const float ShadowMargin = 2;

        public const string ScatterKeyword = "SDF_SCATTER";

        private static readonly int TintId = Shader.PropertyToID("_ScatterTint");
        private static readonly int RingId = Shader.PropertyToID("_ScatterRing");
        private static readonly int CameraId = Shader.PropertyToID("_ScatterCamera");
        private static readonly int WindId = Shader.PropertyToID("_ScatterWind");

        /// <summary>The draw group that cheers with the global <c>_CrowdCheer</c> (ticket 33.6): the boss arena's crowd.</summary>
        public const string CheeringGroup = "crowd";

        [Tooltip("The level's instances.bin (copied as a .bytes asset by LevelBuilder)")]
        public TextAsset instances;
        public ScatterDrawGroup[] groups = Array.Empty<ScatterDrawGroup>();
        public ScatterPrototype[] prototypes = Array.Empty<ScatterPrototype>();
        public ScatterPlacement[] placements = Array.Empty<ScatterPlacement>();
        [Tooltip("The toon material the models share (SdfToon.shader); the renderer draws with a copy in its own variant")]
        public Material toonMaterial;
        [Tooltip("The ink outline's material (SdfToonOutline.shader); none for no outlines")]
        public Material outlineMaterial;
        [Tooltip("The highest quality level that draws with each group's low settings; the levels below it do too")]
        public string lowQualityLevel = "Integrated GPU / Web";

        /// <summary>What one camera's frame drew, for snapshots and profiling.</summary>
        public struct FrameStats
        {
            public Camera Camera;
            public int Instances;
            public int Draws;
            public int OutlineDraws;
            public int Triangles;
            public int OutlineTriangles;
            /// <summary>Triangles drawn into the shadow map (once per cascade they reach, which is not counted).</summary>
            public int ShadowTriangles;
            public int[] InstancesPerRing;
            /// <summary>Per draw group (in the order of <see cref="groups"/>): draws, outlines included, and triangles, outlines included.</summary>
            public int[] DrawsPerGroup;
            public int[] TrianglesPerGroup;
        }

        public FrameStats LastFrame { get; private set; }

        // What was loaded: per instance its transform, tint, prototype and thinning share; per cell its instances and bounds
        private Matrix4x4[] matrices;
        private Vector3[] positions;
        private float[] scales;
        private Vector4[] tints;
        private float[] shares;
        private int[] instancePrototypes;
        private int[][] cells;
        private Bounds[] cellBounds;
        private ScatterLevel[][] levels;
        private int[] prototypeGroups;
        private Vector3[] prototypeCentres;
        private float[] prototypeRadii;
        private Material surface;
        private Material outline;
        private bool loaded;

        // Per frame, reused: the instances of each prototype in each ring, and what a draw is filled from
        private int[][][] ringInstances;
        private int[][] ringCounts;
        private readonly Matrix4x4[] batchMatrices = new Matrix4x4[BatchSize];
        private readonly Vector4[] batchTints = new Vector4[BatchSize];
        private readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        private readonly Plane[] planes = new Plane[6];

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += Draw;
            loaded = false;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Draw;
            Unload();
        }

        private void OnValidate() => loaded = false;

        private void Unload()
        {
            if (surface != null)
                DestroyImmediate(surface);
            if (outline != null)
                DestroyImmediate(outline);
            surface = outline = null;
            loaded = false;
        }

        /// <summary>The instances and the prototypes' meshes, once; again after the component changes (OnValidate).</summary>
        private bool Load()
        {
            if (loaded)
                return matrices != null;
            Unload();
            loaded = true;
            matrices = null;
            if (toonMaterial == null || groups.Length == 0 || prototypes.Length == 0)
                return false;

            surface = new Material(toonMaterial) { name = toonMaterial.name + " (scatter)", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            surface.EnableKeyword(ScatterKeyword);
            if (outlineMaterial != null)
            {
                outline = new Material(outlineMaterial) { name = outlineMaterial.name + " (scatter)", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
                outline.EnableKeyword(ScatterKeyword);
            }

            levels = new ScatterLevel[prototypes.Length][];
            prototypeGroups = new int[prototypes.Length];
            prototypeCentres = new Vector3[prototypes.Length];
            prototypeRadii = new float[prototypes.Length];
            for (var p = 0; p < prototypes.Length; p++)
            {
                var prototype = prototypes[p];
                levels[p] = prototype.levels != null && prototype.levels.Length > 0 ? prototype.levels : LevelsOf(prototype.prefab);
                prototypeGroups[p] = Array.FindIndex(groups, g => g.name == prototype.group);
                if (prototypeGroups[p] < 0 && levels[p].Length > 0)
                    Debug.LogWarning($"SDF scatter: {prototype.name} is in draw group '{prototype.group}', which {name} has no settings for; it is not drawn");
                var bounds = new Bounds();
                var first = true;
                foreach (var part in levels[p].Length > 0 ? levels[p][0].parts : Array.Empty<ScatterPart>())
                {
                    if (part.mesh == null)
                        continue;
                    var partBounds = TransformBounds(part.mesh.bounds, part.transform);
                    if (first)
                        bounds = partBounds;
                    else
                        bounds.Encapsulate(partBounds);
                    first = false;
                }
                prototypeCentres[p] = bounds.center;
                prototypeRadii[p] = bounds.extents.magnitude;
            }

            // Every instance: those of the file, then those placed in the scene
            var file = instances != null ? ScatterInstances.Read(instances.bytes) : null;
            var fileCount = file?.Records.Length ?? 0;
            var count = fileCount + placements.Length;
            matrices = new Matrix4x4[count];
            positions = new Vector3[count];
            scales = new float[count];
            tints = new Vector4[count];
            shares = new float[count];
            instancePrototypes = new int[count];
            var fileToPrototype = new int[file?.Prototypes.Length ?? 0];
            for (var p = 0; p < fileToPrototype.Length; p++)
            {
                fileToPrototype[p] = Array.FindIndex(prototypes, q => q.name == file.Prototypes[p]);
                if (fileToPrototype[p] < 0)
                    Debug.LogWarning($"SDF scatter: instances.bin scatters {file.Prototypes[p]}, which {name} has no prototype for; it is not drawn");
            }
            for (var i = 0; i < fileCount; i++)
            {
                var record = file.Records[i];
                Set(i, fileToPrototype[record.Prototype], record.Position, record.Rotation, record.Scale, record.Tint, record.Share);
            }
            for (var i = 0; i < placements.Length; i++)
            {
                var placement = placements[i];
                // Placed instances take no tint and are never thinned: they are parts of something whole
                Set(fileCount + i, placement.prototype, placement.position, placement.rotation, placement.scale, new Color32(0, 0, 0, 0), 0);
            }

            // Into a grid of cells, the file's when there is one, else one of 12.5 m round the instances
            var cellSize = file?.CellSize ?? 12.5f;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (var i = 0; i < count; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }
            var origin = file != null ? new Vector2(-(file.Origin.x + file.CellsX * file.CellSize), file.Origin.y) : new Vector2(min.x, min.z);
            var cellsX = file?.CellsX ?? Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / cellSize));
            var cellsZ = file?.CellsZ ?? Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / cellSize));
            var lists = new List<int>[cellsX * cellsZ];
            cellBounds = new Bounds[lists.Length];
            for (var i = 0; i < count; i++)
            {
                var p = instancePrototypes[i];
                if (p < 0)
                    continue;
                var x = Mathf.Clamp(Mathf.FloorToInt((positions[i].x - origin.x) / cellSize), 0, cellsX - 1);
                var z = Mathf.Clamp(Mathf.FloorToInt((positions[i].z - origin.y) / cellSize), 0, cellsZ - 1);
                var cell = z * cellsX + x;
                var sphere = new Bounds(matrices[i].MultiplyPoint3x4(prototypeCentres[p]), Vector3.one * (2 * prototypeRadii[p] * Mathf.Abs(scales[i])));
                if (lists[cell] == null)
                {
                    lists[cell] = new List<int>();
                    cellBounds[cell] = sphere;
                }
                lists[cell].Add(i);
                cellBounds[cell].Encapsulate(sphere);
            }
            cells = Array.ConvertAll(lists, l => l?.ToArray() ?? Array.Empty<int>());

            ringInstances = new int[prototypes.Length][][];
            ringCounts = new int[prototypes.Length][];
            for (var p = 0; p < prototypes.Length; p++)
            {
                var rings = prototypeGroups[p] >= 0 ? groups[prototypeGroups[p]].rings.Length : 0;
                ringInstances[p] = new int[rings][];
                ringCounts[p] = new int[rings];
                for (var k = 0; k < rings; k++)
                    ringInstances[p][k] = new int[64];
            }
            return true;
        }

        private void Set(int i, int prototype, Vector3 position, Quaternion rotation, float scale, Color32 tint, float share)
        {
            instancePrototypes[i] = prototype >= 0 && prototype < prototypes.Length && levels[prototype].Length > 0 && prototypeGroups[prototype] >= 0 ? prototype : -1;
            positions[i] = position;
            scales[i] = scale;
            matrices[i] = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
            var linear = ((Color)tint).linear;
            tints[i] = new Vector4(linear.r, linear.g, linear.b, tint.a / 255f);
            shares[i] = share;
        }

        /// <summary>
        /// A prefab's levels of detail as the renderer draws them, read from its renderers: each LOD group's level k (the coarsest it has
        /// where it has fewer), plus renderers in no LOD group at every level; a prefab without LOD groups is one level. Each surface
        /// renderer (not an ink outline) is a part, placed as it is under the prefab's root; it gets the outline where the prefab draws
        /// that mesh's part (named like body, body_LOD1, ...) with one at any level, as MushroomBuilder leaves it off the coarsest.
        /// </summary>
        public static ScatterLevel[] LevelsOf(GameObject prefab)
        {
            if (prefab == null)
                return Array.Empty<ScatterLevel>();
            var root = prefab.transform.worldToLocalMatrix;
            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            bool IsOutline(Renderer r) => r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.EndsWith("Outline", StringComparison.Ordinal);
            var outlined = new HashSet<string>();
            foreach (var renderer in renderers)
                if (IsOutline(renderer) && renderer.GetComponent<MeshFilter>()?.sharedMesh is { } mesh)
                    outlined.Add(PartName(mesh.name));

            ScatterPart Part(Renderer renderer)
            {
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                return mesh == null ? null : new ScatterPart { mesh = mesh, transform = root * renderer.transform.localToWorldMatrix, outline = outlined.Contains(PartName(mesh.name)) };
            }

            var groupsOfLods = prefab.GetComponentsInChildren<LODGroup>(true);
            var inGroups = new HashSet<Renderer>();
            var count = 1;
            foreach (var group in groupsOfLods)
            {
                var lods = group.GetLODs();
                count = Mathf.Max(count, lods.Length);
                foreach (var lod in lods)
                    foreach (var renderer in lod.renderers)
                        if (renderer != null)
                            inGroups.Add(renderer);
            }
            var levels = new ScatterLevel[count];
            for (var level = 0; level < count; level++)
            {
                var parts = new List<ScatterPart>();
                foreach (var renderer in renderers)
                    if (!IsOutline(renderer) && !inGroups.Contains(renderer) && Part(renderer) is { } part)
                        parts.Add(part);
                foreach (var group in groupsOfLods)
                {
                    var lods = group.GetLODs();
                    if (lods.Length == 0)
                        continue;
                    foreach (var renderer in lods[Mathf.Min(level, lods.Length - 1)].renderers)
                        if (renderer is MeshRenderer && !IsOutline(renderer) && Part(renderer) is { } part)
                            parts.Add(part);
                }
                levels[level] = new ScatterLevel { parts = parts.ToArray() };
            }
            return levels;
        }

        /// <summary>The part a mesh is a level of: model_body_LOD2 is model_body.</summary>
        private static string PartName(string mesh)
        {
            var lod = mesh.LastIndexOf("_LOD", StringComparison.Ordinal);
            return lod > 0 && int.TryParse(mesh.Substring(lod + 4), out _) ? mesh.Substring(0, lod) : mesh;
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 transform)
        {
            var result = new Bounds(transform.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (var corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                result.Encapsulate(transform.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, offset)));
            }
            return result;
        }

        private bool IsLowPreset()
        {
            var names = QualitySettings.names;
            var level = QualitySettings.GetQualityLevel();
            // The quality levels go best first, so every level from the low one down is low
            var low = Array.IndexOf(names, lowQualityLevel);
            return low >= 0 && level >= low;
        }

        /// <summary>A group's rings for this preset: each ring's fade in (from, to) and fade out (from, to), in metres from the camera.</summary>
        private static Vector4[] Rings(ScatterDrawGroup group, bool low)
        {
            var scale = low ? group.lowDistance : 1;
            var count = group.rings.Length;
            var result = new Vector4[count];
            var half = group.blend * scale / 2;
            for (var k = 0; k < count; k++)
            {
                var end = group.rings[k] * scale;
                var fadeIn = k == 0 ? new Vector2(-1, -1) : new Vector2(group.rings[k - 1] * scale - half, group.rings[k - 1] * scale + half);
                var fadeOut = k < count - 1 ? new Vector2(end - half, end + half) : new Vector2(end - group.fade * scale, end);
                result[k] = new Vector4(fadeIn.x, fadeIn.y, fadeOut.x, fadeOut.y);
            }
            return result;
        }

        private void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType is CameraType.Preview or CameraType.Reflection || !isActiveAndEnabled || !Load())
                return;
            var low = IsLowPreset();
            var eye = camera.transform.position;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);

            // Each group's rings, and how far the furthest reaches
            var groupRings = new Vector4[groups.Length][];
            var reach = 0f;
            for (var g = 0; g < groups.Length; g++)
            {
                groupRings[g] = Rings(groups[g], low);
                if (groupRings[g].Length > 0)
                    reach = Mathf.Max(reach, groupRings[g][groupRings[g].Length - 1].w);
            }

            for (var p = 0; p < prototypes.Length; p++)
                Array.Clear(ringCounts[p], 0, ringCounts[p].Length);
            var stats = new FrameStats
            {
                Camera = camera, InstancesPerRing = new int[8], DrawsPerGroup = new int[groups.Length], TrianglesPerGroup = new int[groups.Length],
            };
            var drawn = new Bounds();
            var anyDrawn = false;
            for (var c = 0; c < cells.Length; c++)
            {
                var members = cells[c];
                if (members.Length == 0 || cellBounds[c].SqrDistance(eye) > reach * reach)
                    continue;
                var grown = cellBounds[c];
                grown.Expand(2 * ShadowMargin);
                if (!GeometryUtility.TestPlanesAABB(planes, grown))
                    continue;
                foreach (var i in members)
                {
                    var p = instancePrototypes[i];
                    var g = prototypeGroups[p];
                    var group = groups[g];
                    if (low && shares[i] >= group.lowDensity)
                        continue;
                    var distance = Vector3.Distance(eye, positions[i]);
                    var rings = groupRings[g];
                    if (distance > rings[rings.Length - 1].w || prototypes[p].cull > 0 && distance > prototypes[p].cull)
                        continue;
                    var shadowRings = low ? group.lowShadowRings : group.shadowRings;
                    var centre = matrices[i].MultiplyPoint3x4(prototypeCentres[p]);
                    var radius = prototypeRadii[p] * Mathf.Abs(scales[i]);
                    var margin = shadowRings > 0 && distance <= rings[Mathf.Min(shadowRings, rings.Length) - 1].w ? ShadowMargin : 0;
                    if (!InView(centre, radius + margin))
                        continue;
                    var counted = false;
                    for (var k = 0; k < rings.Length; k++)
                    {
                        // From where it starts fading in (the first ring from the camera) to where it has faded out
                        if ((k > 0 && distance < rings[k].x) || distance > rings[k].w)
                            continue;
                        Add(p, k, i);
                        if (k < stats.InstancesPerRing.Length)
                            stats.InstancesPerRing[k]++;
                        counted = true;
                    }
                    if (counted)
                    {
                        stats.Instances++;
                        var sphere = new Bounds(centre, Vector3.one * (2 * radius + 2 * ShadowMargin));
                        if (anyDrawn)
                            drawn.Encapsulate(sphere);
                        else
                            drawn = sphere;
                        anyDrawn = true;
                    }
                }
            }

            var block = 0;
            for (var p = 0; p < prototypes.Length; p++)
            {
                for (var k = 0; k < ringCounts[p].Length; k++)
                {
                    var count = ringCounts[p][k];
                    if (count == 0)
                        continue;
                    var g = prototypeGroups[p];
                    var group = groups[g];
                    var level = levels[p][Mathf.Min(k, levels[p].Length - 1)];
                    var shadows = k < (low ? group.lowShadowRings : group.shadowRings);
                    var outlined = k < group.outlineRings && outline != null;
                    foreach (var part in level.parts)
                    {
                        if (part.mesh == null)
                            continue;
                        var identity = part.transform.isIdentity;
                        var triangles = Triangles(part.mesh);
                        for (var start = 0; start < count; start += BatchSize)
                        {
                            var n = Mathf.Min(BatchSize, count - start);
                            var members = ringInstances[p][k];
                            for (var b = 0; b < n; b++)
                            {
                                var i = members[start + b];
                                batchMatrices[b] = identity ? matrices[i] : matrices[i] * part.transform;
                                batchTints[b] = tints[i];
                            }
                            var properties = Block(block++);
                            properties.SetVectorArray(TintId, batchTints);
                            properties.SetVector(RingId, groupRings[prototypeGroups[p]][k]);
                            properties.SetVector(CameraId, eye);
                            properties.SetVector(WindId, new Vector4(group.wind, group.windHeight, group.tintHeight, group.name == CheeringGroup ? 1 : 0));
                            var castShadows = shadows && part.shadows;
                            var parameters = new RenderParams(surface)
                            {
                                camera = camera,
                                layer = gameObject.layer,
                                worldBounds = drawn,
                                matProps = properties,
                                shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                                receiveShadows = true,
                                lightProbeUsage = LightProbeUsage.Off,
                                reflectionProbeUsage = ReflectionProbeUsage.Off,
                            };
                            Graphics.RenderMeshInstanced(parameters, part.mesh, 0, batchMatrices, n);
                            stats.Draws++;
                            stats.Triangles += n * triangles;
                            stats.DrawsPerGroup[g]++;
                            stats.TrianglesPerGroup[g] += n * triangles;
                            if (castShadows)
                                stats.ShadowTriangles += n * triangles;
                            if (!outlined || !part.outline)
                                continue;
                            parameters.material = outline;
                            parameters.shadowCastingMode = ShadowCastingMode.Off;
                            parameters.receiveShadows = false;
                            Graphics.RenderMeshInstanced(parameters, part.mesh, 0, batchMatrices, n);
                            stats.OutlineDraws++;
                            stats.OutlineTriangles += n * triangles;
                            stats.DrawsPerGroup[g]++;
                            stats.TrianglesPerGroup[g] += n * triangles;
                        }
                    }
                }
            }
            LastFrame = stats;
        }

        /// <summary>
        /// Every instance that casts a shadow, for <see cref="StaticSunShadow"/>: those of the groups with shadow rings (on the low
        /// preset, low shadow rings, and only the instances it keeps), at LOD0, with no wind and no fade. Hands each part's matrices to
        /// <paramref name="draw"/> at most <see cref="BatchSize"/> at a time (the array is reused), and gives each instance's bounding
        /// sphere to <paramref name="bounds"/> once.
        /// </summary>
        public void ShadowCasters(bool low, Action<Mesh, Matrix4x4[], int> draw, Action<Vector3, float> bounds)
        {
            if (!Load())
                return;
            var casting = new List<int>[prototypes.Length];
            for (var i = 0; i < instancePrototypes.Length; i++)
            {
                var p = instancePrototypes[i];
                if (p < 0)
                    continue;
                var group = groups[prototypeGroups[p]];
                if ((low ? group.lowShadowRings : group.shadowRings) == 0 || low && shares[i] >= group.lowDensity)
                    continue;
                (casting[p] ??= new List<int>()).Add(i);
                bounds(matrices[i].MultiplyPoint3x4(prototypeCentres[p]), prototypeRadii[p] * Mathf.Abs(scales[i]));
            }
            for (var p = 0; p < prototypes.Length; p++)
            {
                if (casting[p] == null)
                    continue;
                foreach (var part in levels[p][0].parts)
                {
                    if (part.mesh == null || !part.shadows)
                        continue;
                    var members = casting[p];
                    for (var start = 0; start < members.Count; start += BatchSize)
                    {
                        var n = Mathf.Min(BatchSize, members.Count - start);
                        for (var b = 0; b < n; b++)
                            batchMatrices[b] = part.transform.isIdentity ? matrices[members[start + b]] : matrices[members[start + b]] * part.transform;
                        draw(part.mesh, batchMatrices, n);
                    }
                }
            }
        }

        private bool InView(Vector3 centre, float radius)
        {
            for (var plane = 0; plane < planes.Length; plane++)
                if (planes[plane].GetDistanceToPoint(centre) < -radius)
                    return false;
            return true;
        }

        private void Add(int prototype, int ring, int instance)
        {
            var list = ringInstances[prototype][ring];
            var count = ringCounts[prototype][ring];
            if (count == list.Length)
                ringInstances[prototype][ring] = list = ResizeDouble(list);
            list[count] = instance;
            ringCounts[prototype][ring] = count + 1;
        }

        private static int[] ResizeDouble(int[] list)
        {
            var larger = new int[list.Length * 2];
            Array.Copy(list, larger, list.Length);
            return larger;
        }

        /// <summary>
        /// A property block per draw of the frame, kept for the next frames: each draw's tints and ring stay its own until the camera has
        /// drawn, whether or not Unity copies the block when the draw is queued.
        /// </summary>
        private MaterialPropertyBlock Block(int index)
        {
            while (blocks.Count <= index)
                blocks.Add(new MaterialPropertyBlock());
            return blocks[index];
        }

        private readonly Dictionary<Mesh, int> triangleCounts = new Dictionary<Mesh, int>();

        private int Triangles(Mesh mesh)
        {
            if (!triangleCounts.TryGetValue(mesh, out var count))
                triangleCounts[mesh] = count = (int)(mesh.GetIndexCount(0) / 3);
            return count;
        }
    }
}
