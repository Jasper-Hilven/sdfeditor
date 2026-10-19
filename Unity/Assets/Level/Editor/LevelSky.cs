using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The sky of a level scene, from level.json "sky" (the resolved preset, content/skies/*.sky): for <see cref="LevelBuilder"/>.
    /// <list type="bullet">
    /// <item>The lighting (<see cref="Lighting"/>, for <see cref="MushroomScene.BuildLighting"/>): the main light from the sky's light, a
    /// skybox material on SdfSky.shader (Assets/Scenes/&lt;Level&gt;/Sky.mat, stars and the moon's halo on it), an ambient from the shadow
    /// tint, and the post-processing in Assets/Scenes/&lt;Level&gt;/Post Processing.asset.</item>
    /// <item>Level/Sky (<see cref="Add"/>): a <see cref="SkySettings"/> with the tone, bands and fade, which sets the toon shaders'
    /// globals; the far layer, laid out round the origin (the camera, see SdfToonFar.shader) <see cref="SkySettings.farRadius"/> away:
    /// the moon at the light, its eye and lids the eye-moon prefab's meshes, and the floaters, each the coarsest level of its model, as
    /// big as the sky says; and a mesh of quads per spore set on SdfSpores.shader. None of it is static (it moves), casts shadows or
    /// collides.</item>
    /// </list>
    /// Directions and positions in level.json are the pipeline's; x is mirrored here, so azimuths flip sign.
    /// </summary>
    public static class LevelSky
    {
        public const string SkyName = "Sky";
        public const string FarShaderPath = "Assets/Mushrooms/Shaders/SdfToonFar.shader";
        public const string SkyShaderPath = "Assets/Mushrooms/Shaders/SdfSky.shader";
        public const string SporesShaderPath = "Assets/Mushrooms/Shaders/SdfSpores.shader";

        /// <summary>The eye part of the moon model is 12 cm in radius; its diameter is what the moon's size spans.</summary>
        private const float EyeDiameter = 0.24f;

        /// <summary>
        /// The moon's levels of detail, about 160 pixels tall at 1080p: the eye's finest, since its rings are vertex colours that a coarser
        /// level smears; the lids' LOD1, whose error (under a millimetre on 24 cm) is half a pixel.
        /// </summary>
        private const int EyeLevel = 0;
        private const int LidLevel = 1;

        /// <summary>The far layer draws after the skybox; its depth sorts it among itself, the queue only saves overdraw: the moon first.</summary>
        private const int MoonQueue = 2600;
        private const int LidQueue = 2601;
        private const int FloaterQueue = 2602;

        /// <summary>The distances from the camera the far layer's depth slab spans: round <see cref="SkySettings.farRadius"/>.</summary>
        private const float FarDepthFrom = 60;
        private const float FarDepthTo = 140;

        /// <summary>How much brighter than lit the floaters glow; the moon's comes from the sky.</summary>
        private const float FloaterGlow = 0.4f;

        /// <summary>The moon's halo in the skybox, as a share of the light's colour times the moon's glow.</summary>
        private const float HaloStrength = 0.12f;

        /// <summary>The level's lighting from its sky; null for a level baked without one, which is lit like the showcase.</summary>
        public static MushroomScene.SkyLighting Lighting(string name, JObject data)
        {
            if (!(data["sky"] is JObject sky))
                return null;
            var light = sky["light"];
            var tone = sky["tone"];
            var tint = Vector(tone["shadowTint"]);
            var brightness = tone.Value<float>("brightness");
            var post = sky["post"];
            var folder = SceneAssetFolder(name);
            return new MushroomScene.SkyLighting
            {
                lightDirection = Mirrored(Vector(light["direction"])).normalized,
                // Light colours are given in sRGB; the sky's are linear
                lightColor = Colour(light["color"]).gamma,
                skybox = SkyboxMaterial(folder, sky),
                // For anything still on URP Lit: the showcase's ambient under the sky's tone
                ambient = new Color(0.17f * tint.x * brightness, 0.14f * tint.y * brightness, 0.1f * tint.z * brightness),
                exposure = post.Value<float>("exposure"),
                bloom = post.Value<float>("bloom"),
                volumeProfilePath = $"{folder}/Post Processing.asset",
            };
        }

        /// <summary>Level/Sky: the sky's settings, far layer and spores, see above. Returns how many objects the sky draws.</summary>
        public static int Add(Transform levelRoot, string name, JObject data)
        {
            if (!(data["sky"] is JObject sky))
            {
                // No sky data: no sky object, material or mesh. Only the identity tone and no fade on the level's own root, so the shaders'
                // globals are known (no distance fade: a far wall is not washed toward any colour)
                levelRoot.gameObject.AddComponent<SkySettings>();
                return 0;
            }
            var root = new GameObject(SkyName);
            root.transform.SetParent(levelRoot, false);
            var settings = root.AddComponent<SkySettings>();

            var tone = sky["tone"];
            settings.brightness = tone.Value<float>("brightness");
            settings.shadowTint = Vector(tone["shadowTint"]);
            settings.glowKeep = tone.Value<float>("glowKeep");
            var bands = (JArray)sky["bands"];
            settings.bandColors = bands.Select(b => (Vector4)Vector(b["color"])).ToArray();
            settings.bandFrom = bands.Select(b => b.Value<float>("from")).ToArray();
            settings.softness = sky["gradient"].Value<float>("softness");
            if (sky["abyss"]?["glow"] is JObject glow)
            {
                settings.abyssGlowElevation = glow.Value<float>("elevation");
                settings.abyssGlowWidth = glow.Value<float>("width");
                settings.abyssGlowColor = Vector(glow["color"]);
            }
            else
            {
                settings.abyssGlowColor = Vector4.zero;
            }
            if (sky["fade"] is JObject fade)
            {
                settings.fadeStart = fade.Value<float>("start");
                settings.fadeEnd = fade.Value<float>("end");
                settings.fadeAmount = fade.Value<float>("amount");
            }
            else
            {
                settings.fadeAmount = 0;
            }

            var far = new List<Renderer>();
            var drawn = 0;
            var style = MushroomStyle.Load();
            if (sky["moon"] is JObject moon)
                drawn += AddMoon(root.transform, settings, name, sky, moon, style, far);
            if (sky["floaters"] is JObject floaters)
                drawn += AddFloaters(root.transform, settings, name, floaters, style, far);
            if (sky["spores"] is JObject spores)
                drawn += AddSpores(root.transform, name, data, spores, far);
            settings.farRenderers = far.ToArray();
            settings.Apply();
            return drawn;
        }

        /// <summary>The moon at the light, facing the camera, its size in degrees spanning the eye: the eye and its two lids.</summary>
        private static int AddMoon(Transform root, SkySettings settings, string name, JObject sky, JObject moon, MushroomStyle style, List<Renderer> far)
        {
            var prefab = Prefab(moon.Value<string>("model"));
            if (prefab == null)
                return 0;
            var direction = Mirrored(Vector(sky["light"]["direction"])).normalized;
            var moonObject = new GameObject("Moon");
            moonObject.transform.SetParent(root, false);
            // Its front (+z) towards the camera, upright
            moonObject.transform.SetLocalPositionAndRotation(direction * settings.farRadius, Quaternion.LookRotation(-direction, Vector3.up));
            var size = moon.Value<float>("size");
            moonObject.transform.localScale = Vector3.one * (2 * settings.farRadius * Mathf.Tan(size * 0.5f * Mathf.Deg2Rad) / EyeDiameter);

            var glow = moon.Value<float>("glow");
            var eyeMaterial = FarMaterial($"{name} Sky Moon", MoonQueue, glow, style);
            var lidMaterial = FarMaterial($"{name} Sky Moon Lids", LidQueue, glow, style);
            var drawn = 0;
            foreach (var part in PartsOf(prefab))
            {
                var lid = part.StartsWith("moon_lid");
                var mesh = PartMesh(prefab, part, lid ? LidLevel : EyeLevel);
                if (mesh == null)
                    continue;
                var partObject = FarObject(part, moonObject.transform, mesh, lid ? lidMaterial : eyeMaterial, far);
                if (part == "eye")
                    settings.moonEye = partObject.transform;
                else if (part == "moon_lid_upper")
                    settings.moonUpperLid = partObject.transform;
                else if (part == "moon_lid_lower")
                    settings.moonLowerLid = partObject.transform;
                drawn++;
            }
            var turn = moon["turn"];
            settings.turnAmount = turn.Value<float>("amount");
            settings.turnPeriod = turn.Value<float>("period");
            var blink = moon["blink"];
            var every = blink["every"].ToObject<float[]>();
            settings.blinkEvery = new Vector2(every[0], every[1]);
            settings.blinkDuration = blink.Value<float>("duration");
            settings.seed = moon.Value<int?>("seed") ?? 0;
            return drawn;
        }

        /// <summary>The floaters where the bake put them, each its model's coarsest level, as big as the model scale times would look from there.</summary>
        private static int AddFloaters(Transform root, SkySettings settings, string name, JObject floaters, MushroomStyle style, List<Renderer> far)
        {
            var prefab = Prefab(floaters.Value<string>("model"));
            var layout = floaters["layout"] as JArray;
            if (prefab == null || layout == null)
                return 0;
            var material = FarMaterial($"{name} Sky Floaters", FloaterQueue, FloaterGlow, style);
            var scale = floaters.Value<float>("scale");
            var objects = new List<Transform>();
            var places = new List<Vector4>();
            var drawn = 0;
            for (var i = 0; i < layout.Count; i++)
            {
                var place = layout[i].ToObject<float[]>();
                // Unity mirrors x: the azimuth turns the other way
                var (azimuth, elevation, distance, phase) = (-place[0], place[1], place[2], place[3]);
                var floater = new GameObject($"Floater {i}");
                floater.transform.SetParent(root, false);
                floater.transform.SetLocalPositionAndRotation(SkySettings.Direction(azimuth, elevation) * settings.farRadius,
                    Quaternion.Euler(8 * Mathf.Sin(phase * 17), phase * 360, 8 * Mathf.Cos(phase * 23)));
                floater.transform.localScale = Vector3.one * (scale * settings.farRadius / Mathf.Max(distance, 1));
                foreach (var part in PartsOf(prefab))
                {
                    var mesh = PartMesh(prefab, part, int.MaxValue);
                    if (mesh != null)
                    {
                        FarObject(part, floater.transform, mesh, material, far);
                        drawn++;
                    }
                }
                objects.Add(floater.transform);
                places.Add(new Vector4(azimuth, elevation, distance, phase));
            }
            settings.floaters = objects.ToArray();
            settings.floaterPlaces = places.ToArray();
            settings.drift = floaters.Value<float>("drift");
            settings.bob = floaters.Value<float>("bob");
            return drawn;
        }

        /// <summary>A mesh of quads per spore set, from the bake's seeded layout; the shader moves them.</summary>
        private static int AddSpores(Transform root, string name, JObject data, JObject spores, List<Renderer> far)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SporesShaderPath)
                         ?? throw new InvalidDataException($"The shader {SporesShaderPath} is missing");
            var meshFolder = $"{LevelBuilder.MeshFolder}/{name} Sky";
            var drawn = 0;
            if (spores["abyss"] is JObject abyss && abyss["points"] is JArray abyssPoints && abyssPoints.Count > 0)
            {
                var points = abyssPoints.Select(p => p.ToObject<float[]>()).ToArray();
                var from = abyss.Value<float>("from");
                var to = abyss.Value<float>("to");
                var mesh = SporeMesh($"{meshFolder}/abyss_spores.asset", points.Select(p => (new Vector3(-p[0], 0, p[1]), p[2], p[3], p[4])).ToArray());
                // Where they rise, with room for the sway and their size
                var min = new Vector3(-points.Max(p => p[0]), from, points.Min(p => p[1])) - Vector3.one * 2;
                var max = new Vector3(-points.Min(p => p[0]), to, points.Max(p => p[1])) + Vector3.one * 2;
                mesh.bounds = new Bounds((min + max) / 2, max - min);
                var material = SporeMaterial($"{name} Sky Abyss Spores", shader, 0, abyss["color"], abyss["altColor"], abyss.Value<float>("size"));
                material.SetFloat("_SporeRise", abyss.Value<float>("rise"));
                material.SetVector("_SporeRange", new Vector4(from, to, 0, 0));
                SporeObject("Abyss Spores", root, mesh, material);
                drawn++;
            }
            if (spores["near"] is JObject near && near["points"] is JArray nearPoints && nearPoints.Count > 0)
            {
                var points = nearPoints.Select(p => p.ToObject<float[]>()).ToArray();
                var box = 2 * near.Value<float>("radius");
                var mesh = SporeMesh($"{meshFolder}/near_spores.asset", points.Select(p => (new Vector3(-p[0], p[1], p[2]), p[3], 0f, 1f)).ToArray());
                // The box round the camera; SkySettings moves it there for culling
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (box + 2));
                var material = SporeMaterial($"{name} Sky Motes", shader, 1, near["color"], near["color"], near.Value<float>("size"));
                material.SetFloat("_SporeBox", box);
                material.SetFloat("_SporeDrift", near.Value<float>("drift"));
                material.SetFloat("_SporeMaxPixels", 4);
                far.Add(SporeObject("Motes", root, mesh, material));
                drawn++;
            }
            AssetDatabase.SaveAssets();
            return drawn;
        }

        /// <summary>Four corners per spore, all at its point: the shader turns them into a quad facing the camera.</summary>
        private static Mesh SporeMesh(string path, IReadOnlyList<(Vector3 point, float phase, float alt, float size)> spores)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                MushroomBuilder.EnsureFolder(Path.GetDirectoryName(path));
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }
            var positions = new List<Vector3>();
            var corners = new List<Vector4>();
            var sizes = new List<Vector2>();
            var indices = new List<int>();
            var corner = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            foreach (var (point, phase, alt, size) in spores)
            {
                var first = positions.Count;
                for (var c = 0; c < 4; c++)
                {
                    positions.Add(point);
                    corners.Add(new Vector4(corner[c].x, corner[c].y, phase, alt));
                    sizes.Add(new Vector2(size, 0));
                }
                indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, sizes);
            mesh.SetTriangles(indices, 0, calculateBounds: false);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Renderer SporeObject(string name, Transform root, Mesh mesh, Material material)
        {
            var spores = new GameObject(name);
            spores.transform.SetParent(root, false);
            spores.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = spores.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            NoLighting(renderer);
            return renderer;
        }

        private static Material SporeMaterial(string name, Shader shader, float mode, JToken color, JToken altColor, float size)
        {
            var material = MaterialAsset(name, shader);
            material.SetFloat("_SporeMode", mode);
            material.SetVector("_SporeColor", Vector(color));
            material.SetVector("_SporeAltColor", Vector(altColor));
            material.SetFloat("_SporeSize", size);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A far-layer material (SdfToonFar.shader) with the toon style, drawn at its place in the queue.</summary>
        private static Material FarMaterial(string name, int queue, float glow, MushroomStyle style)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(FarShaderPath)
                         ?? throw new InvalidDataException($"The shader {FarShaderPath} is missing");
            var material = MaterialAsset(name, shader);
            style.ApplyToSurface(material);
            material.SetFloat("_FarGlow", glow);
            material.SetVector("_FarDepth", new Vector4(FarDepthFrom, FarDepthTo, 0, 0));
            material.renderQueue = queue;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material MaterialAsset(string name, Shader shader)
        {
            var path = $"{LevelBuilder.MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(LevelBuilder.MaterialFolder);
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        private static GameObject FarObject(string name, Transform parent, Mesh mesh, Material material, List<Renderer> far)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            NoLighting(renderer);
            far.Add(renderer);
            return part;
        }

        private static void NoLighting(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>The skybox of the level: SdfSky.shader with the stars and the moon's halo; the bands are the globals SkySettings sets.</summary>
        private static Material SkyboxMaterial(string folder, JObject sky)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SkyShaderPath)
                         ?? throw new InvalidDataException($"The shader {SkyShaderPath} is missing");
            var path = $"{folder}/Sky.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(folder);
                material = new Material(shader) { name = "Sky" };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            if (sky["stars"] is JObject stars)
            {
                material.SetVector("_SkyStars", new Vector4(stars.Value<float>("density"), stars.Value<float>("size"), stars.Value<float>("twinkle"), stars.Value<float>("tint")));
                material.SetVector("_SkyStarsPlace", new Vector4(Mathf.Sin(stars.Value<float>("minElevation") * Mathf.Deg2Rad), (stars.Value<int?>("seed") ?? 0) % 997, 0, 0));
            }
            else
            {
                material.SetVector("_SkyStars", Vector4.zero);
            }
            if (sky["moon"] is JObject moon)
            {
                var direction = Mirrored(Vector(sky["light"]["direction"])).normalized;
                material.SetVector("_SkyMoonDirection", new Vector4(direction.x, direction.y, direction.z, moon.Value<float>("size") * 0.5f * Mathf.Deg2Rad));
                var color = Vector(sky["light"]["color"]) * (moon.Value<float>("glow") * HaloStrength);
                material.SetVector("_SkyMoonHalo", new Vector4(color.x, color.y, color.z, 1));
            }
            else
            {
                material.SetVector("_SkyMoonHalo", Vector4.zero);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Assets/Scenes/Meadow for meadow: the level's own lighting assets, next to its scene.</summary>
        private static string SceneAssetFolder(string name) =>
            $"{LevelBuilder.SceneFolder}/{char.ToUpperInvariant(name[0])}{name.Substring(1)}";

        private static GameObject Prefab(string model)
        {
            var path = $"{MushroomBuilder.PrefabFolder}/{model}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogWarning($"SDF level: there is no prefab {path} for the sky's model {model}; mesh content/models (sdf unity) and let the prefabs build");
            return prefab;
        }

        /// <summary>The parts of a prefab MushroomBuilder built: its children without a level suffix (eye, moon_lid_upper, body).</summary>
        private static IEnumerable<string> PartsOf(GameObject prefab) =>
            prefab.transform.Cast<Transform>().Select(t => t.name).Where(n => !n.Contains("_LOD")).Distinct().OrderBy(n => n, System.StringComparer.Ordinal);

        /// <summary>A part's mesh at a level of detail, or at the coarsest it has below that.</summary>
        private static Mesh PartMesh(GameObject prefab, string part, int level)
        {
            for (var l = Mathf.Min(level, 8); l >= 0; l--)
            {
                var child = prefab.transform.Find(l == 0 ? part : $"{part}_LOD{l}");
                if (child != null && child.GetComponent<MeshFilter>() is { } filter && filter.sharedMesh != null)
                    return filter.sharedMesh;
            }
            return null;
        }

        private static Vector3 Vector(JToken token)
        {
            var v = token.ToObject<float[]>();
            return new Vector3(v[0], v[1], v[2]);
        }

        private static Color Colour(JToken token)
        {
            var v = Vector(token);
            return new Color(v.x, v.y, v.z);
        }

        /// <summary>A direction or point from the pipeline's coordinates into Unity's.</summary>
        private static Vector3 Mirrored(Vector3 v) => new Vector3(-v.x, v.y, v.z);
    }
}
