using System;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SdfMedia.Editor
{
    /// <summary>
    /// The covers' two subjects over the scene behind the title: the screen split in half, the top half tinted red (20 %) with the
    /// minigun, the bottom half tinted blue with the Jack-o'-Lantern head (red against blue, the two teams). Each subject is its own high-detail mesh,
    /// meshed from its SDF at 256 cells with simplification off (no triangle is folded away):
    /// <code>
    /// sdf mesh content/models/characters/monsters/pumpkin.sdf Media/CoverArt/pumpkin_256.glb --resolution 256 --reference-size 0.38 --max-error 0
    /// sdf mesh content/models/weapons/minigun.sdf Media/CoverArt/minigun_256.glb --resolution 256 --reference-size 1 --max-error 0
    /// </code>
    /// (the pumpkin, 0.69 m tall at detail 0.75, gets 256 cells of 2.7 mm over its height, about 530k triangles; the minigun, 1.5 m
    /// long, 5 mm cells, about 210k). The .glb files are in Media/CoverArt, outside Unity's Assets, and are read only here, in an editor
    /// script, with the hero material (Hidden/SdfMedia/CoverHero, also in this Editor folder): none of it can go into a build.
    /// Each half is rendered in a preview scene of its own (nothing else in it), with the subject over a transparent background, and laid
    /// over the cover's scene (CoverStage's soldiers on the level) tinted in the half's colour, with a soft shadow of the subject.
    /// </summary>
    internal static class CoverStudio
    {
        public const string ArtFolder = "Media/CoverArt";
        public const string PumpkinFile = "pumpkin_256.glb";
        public const string MinigunFile = "minigun_256.glb";
        public const string ShaderName = "Hidden/SdfMedia/CoverHero";

        /// <summary>The tint over the scene in the top half and in the bottom half (sRGB), and how much of it (0.2: 20 % alpha).</summary>
        public static readonly Color Red = Hex(0xE2302A), Blue = Hex(0x2E5FE0);
        public const float TintAlpha = 0.2f;

        /// <summary>Where a subject is in its half and how it is turned.</summary>
        internal struct Shot
        {
            /// <summary>The subject's middle, as fractions of the half from its left and top edges.</summary>
            public Vector2 Centre;
            /// <summary>The subject's height on screen, as a fraction of the half's height, and its widest, as a fraction of the half's width.</summary>
            public float Height, Width;
            /// <summary>The subject's turn about +Y and the camera's look down, in degrees.</summary>
            public float Yaw, Pitch, Roll;
        }

        /// <summary>The framing of one format: the two shots and where the title's middle is across the width (0 to 1).</summary>
        internal struct Layout
        {
            public Shot Pumpkin, Minigun;
            public float TitleX;
        }

        /// <summary>The layout by the format's shape: landscape (wider than 1.3), square, portrait (narrower than 0.9).</summary>
        public static Layout LayoutOf(CoverFormat format) => format.Aspect > 1.3f
            ? new Layout
            {
                Minigun = new Shot { Centre = new Vector2(0.85f, 0.46f), Height = 0.62f, Width = 0.22f, Yaw = -24, Pitch = 6, Roll = -4 },
                Pumpkin = new Shot { Centre = new Vector2(0.86f, 0.48f), Height = 0.76f, Width = 0.24f, Yaw = -22, Pitch = 6 },
                TitleX = 0.44f,
            }
            : format.Aspect < 0.9f
                ? new Layout
                {
                    Minigun = new Shot { Centre = new Vector2(0.5f, 0.4f), Height = 0.5f, Width = 0.6f, Yaw = -24, Pitch = 6, Roll = -4 },
                    Pumpkin = new Shot { Centre = new Vector2(0.5f, 0.62f), Height = 0.56f, Width = 0.7f, Yaw = -18, Pitch = 6 },
                    TitleX = 0.5f,
                }
                : new Layout
                {
                    Minigun = new Shot { Centre = new Vector2(0.5f, 0.37f), Height = 0.5f, Width = 0.55f, Yaw = -24, Pitch = 6, Roll = -4 },
                    Pumpkin = new Shot { Centre = new Vector2(0.5f, 0.63f), Height = 0.5f, Width = 0.6f, Yaw = -18, Pitch = 6 },
                    TitleX = 0.5f,
                };

        private const float FieldOfView = 24;
        /// <summary>A layer nothing else uses (unnamed in the project's tags and layers), which only the studio camera sees.</summary>
        private const int SubjectLayer = 31;

        private static Mesh pumpkin, minigun;
        private static Material material;

        /// <summary>
        /// The whole picture of a format over the cover's scene (<paramref name="scene"/>, rows from the bottom): the top half tinted red with
        /// the minigun, the bottom half tinted blue with the pumpkin. Rows from the bottom.
        /// </summary>
        public static Color32[] Render(CoverFormat format, Color32[] scene)
        {
            Load();
            var layout = LayoutOf(format);
            var half = format.Height / 2;
            var sceneBottom = new Color32[format.Width * half];
            var sceneTop = new Color32[format.Width * (format.Height - half)];
            Array.Copy(scene, 0, sceneBottom, 0, sceneBottom.Length);
            Array.Copy(scene, sceneBottom.Length, sceneTop, 0, sceneTop.Length);
            var top = RenderHalf(minigun, layout.Minigun, format.Width, format.Height - half, sceneTop, Red, emission: 0);
            var bottom = RenderHalf(pumpkin, layout.Pumpkin, format.Width, half, sceneBottom, Blue, emission: 0.8f);
            var picture = new Color32[format.Width * format.Height];
            Array.Copy(bottom, 0, picture, 0, bottom.Length);
            Array.Copy(top, 0, picture, bottom.Length, top.Length);
            return picture;
        }

        private static void Load()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ArtFolder));
            if (pumpkin == null)
                pumpkin = BuildMesh(Path.Combine(folder, PumpkinFile));
            if (minigun == null)
                minigun = BuildMesh(Path.Combine(folder, MinigunFile));
            if (material == null)
            {
                var shader = Shader.Find(ShaderName) ?? throw new InvalidOperationException($"{ShaderName} is missing (Assets/Media/Editor/CoverHero.shader)");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        /// <summary>
        /// One mesh of every part of the .glb (glTF's +X mirrored to Unity's, the triangles turned round with it): the albedo as the vertex
        /// colour, the surface (roughness, specular, wetness, subsurface radius) in UV0, thickness, occlusion and emission in UV1.
        /// </summary>
        private static Mesh BuildMesh(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"{path} is missing: mesh it with sdf mesh (see CoverStudio)", path);
            var glb = SdfGlb.Read(path);
            var parts = glb.Meshes.Where(m => m.Lod == 0).ToList();
            if (parts.Any(p => p.Instances.Count > 0))
                throw new InvalidDataException($"{path}: part instances are not supported by the cover studio");
            var count = parts.Sum(p => p.VertexCount);
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var colours = new Color[count];
            var surface = new Vector4[count];
            var extra = new Vector4[count];
            var indices = new System.Collections.Generic.List<int>();
            var offset = 0;
            foreach (var part in parts)
            {
                var n = part.VertexCount;
                var p = part.Get("POSITION", 3);
                var no = part.Get("NORMAL", 3);
                var c = part.Get("COLOR_0", 3) ?? part.Get("_BASE_COLOR", 3);
                var s = part.Get("_SURFACE", 4);
                var t = part.Get("_THICKNESS", 1);
                var o = part.Get("_OCCLUSION", 1);
                var e = part.Get("_EMISSION", 1);
                for (var v = 0; v < n; v++)
                {
                    positions[offset + v] = new Vector3(-p[3 * v], p[3 * v + 1], p[3 * v + 2]);
                    normals[offset + v] = new Vector3(-no[3 * v], no[3 * v + 1], no[3 * v + 2]);
                    colours[offset + v] = new Color(c[3 * v], c[3 * v + 1], c[3 * v + 2], 1);
                    surface[offset + v] = s != null ? new Vector4(s[4 * v], s[4 * v + 1], s[4 * v + 2], s[4 * v + 3]) : new Vector4(0.5f, 0.5f, 0, 0);
                    extra[offset + v] = new Vector4(t != null ? t[v] : 1, o != null ? o[v] : 1, e != null ? e[v] : 0, 0);
                }
                foreach (var primitive in part.Primitives)
                    for (var i = 0; i < primitive.Indices.Length; i += 3)
                    {
                        indices.Add(offset + primitive.Indices[i]);
                        indices.Add(offset + primitive.Indices[i + 2]);
                        indices.Add(offset + primitive.Indices[i + 1]);
                    }
                offset += n;
            }
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path), indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colours);
            mesh.SetUVs(0, surface);
            mesh.SetUVs(1, extra);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        /// <summary>One half: the subject in a scene of its own, laid over the backdrop. Rows from the bottom.</summary>
        private static Color32[] RenderHalf(Mesh mesh, Shot shot, int width, int height, Color32[] backdrop, Color inner, float emission)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            try
            {
                var subject = new GameObject("Cover Subject", typeof(MeshFilter), typeof(MeshRenderer));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(subject, scene);
                subject.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = subject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                // On a layer of its own: the cover's level is loaded too, and URP draws it through a camera in another scene unless the
                // camera's culling mask leaves it out
                subject.layer = SubjectLayer;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                subject.transform.rotation = Quaternion.Euler(0, shot.Yaw, shot.Roll);
                var bounds = renderer.bounds;

                var cameraObject = new GameObject("Cover Studio Camera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.scene = scene;
                camera.cameraType = CameraType.Game;
                camera.cullingMask = 1 << SubjectLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.allowHDR = false;
                camera.allowMSAA = true;
                camera.fieldOfView = FieldOfView;
                camera.aspect = width / (float)height;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;

                // Far enough that the subject's box, seen from the camera, is Height of the frame tall, and no wider than Width of it
                var rotation = Quaternion.Euler(shot.Pitch, 180, 0);
                var halfAngle = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
                // The mesh's own box turned with it (tighter than the world-aligned bounds of a long thing turned), seen along the camera
                var local = mesh.bounds;
                var corners = Enumerable.Range(0, 8).Select(corner => subject.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)))).ToArray();
                var right = rotation * Vector3.right;
                var upward = rotation * Vector3.up;
                float minX = corners.Min(c => Vector3.Dot(c, right)), maxX = corners.Max(c => Vector3.Dot(c, right));
                float minY = corners.Min(c => Vector3.Dot(c, upward)), maxY = corners.Max(c => Vector3.Dot(c, upward));
                var across = (maxX - minX) / 2;
                var up = (maxY - minY) / 2;
                var middle = bounds.center - right * Vector3.Dot(bounds.center, right) - upward * Vector3.Dot(bounds.center, upward)
                             + right * (minX + maxX) / 2 + upward * (minY + maxY) / 2;
                var aspect = width / (float)height;
                var distance = Mathf.Max(up / (halfAngle * shot.Height), across / (halfAngle * aspect * shot.Width)) + bounds.extents.magnitude * 0.3f;
                camera.transform.SetPositionAndRotation(middle - rotation * Vector3.forward * distance, rotation);
                camera.nearClipPlane = Mathf.Max(0.01f, distance - bounds.extents.magnitude * 2);
                camera.farClipPlane = distance + bounds.extents.magnitude * 2;
                // The subject's middle moved to Centre by shifting the lens, not by turning the camera (no keystone)
                camera.ResetProjectionMatrix();
                var projection = camera.projectionMatrix;
                projection.m02 = 1 - 2 * shot.Centre.x;
                projection.m12 = 2 * shot.Centre.y - 1;
                camera.projectionMatrix = projection;

                // Lights in the camera's frame: the key high on the left in front, a cool fill on the right, the rim from behind in the
                // backdrop's colour so the subject sits in it
                Vector3 FromCamera(Vector3 direction) => (camera.transform.rotation * direction).normalized;
                var linearInner = inner.linear;
                material.SetVector("_KeyDir", FromCamera(new Vector3(-0.55f, 0.6f, -0.6f)));
                material.SetColor("_KeyColor", new Color(1f, 0.97f, 0.92f));
                material.SetVector("_FillDir", FromCamera(new Vector3(0.8f, 0.05f, -0.4f)));
                material.SetColor("_FillColor", new Color(0.32f, 0.34f, 0.42f));
                material.SetVector("_RimDir", FromCamera(new Vector3(0.35f, 0.55f, 1f)));
                material.SetColor("_RimColor", linearInner * 2.2f + new Color(0.25f, 0.25f, 0.25f));
                material.SetColor("_SkyColor", new Color(0.22f, 0.22f, 0.26f) + linearInner * 0.15f);
                material.SetColor("_GroundColor", linearInner * 0.12f);
                material.SetFloat("_EmissionBoost", emission);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_OutlinePixels", 5f * CoverCapture.Super * Mathf.Max(1, height / 540f));

                var super = CoverCapture.Super;
                var samples = Mathf.Clamp(SystemInfo.GetRenderTextureSupportedMSAASampleCount(
                    new RenderTextureDescriptor(width * super, height * super, RenderTextureFormat.ARGB32, 24)), 1, 8);
                target = new RenderTexture(width * super, height * super, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = samples };
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                var subjectPixels = CoverCapture.Downscale(target, width, height);
                camera.targetTexture = null;
                if (!subjectPixels.Any(p => p.a > 0))
                    throw new InvalidOperationException($"the cover studio's render of {mesh.name} is empty (see the log for render errors)");
                return Compose(subjectPixels, backdrop, width, height, inner);
            }
            finally
            {
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// The subject (colour already multiplied by its coverage, as the clear to transparent black leaves it) over the scene tinted with
        /// <paramref name="tint"/> at <see cref="TintAlpha"/>, with the subject's soft shadow below and behind it.
        /// </summary>
        private static Color32[] Compose(Color32[] subject, Color32[] scene, int width, int height, Color tint)
        {
            var shadow = SoftAlpha(subject, width, height, Mathf.Max(4, height / 22));
            var shiftY = -Mathf.RoundToInt(height * 0.035f);
            var output = new Color32[subject.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var back = Color.Lerp(scene[i], tint, TintAlpha);
                var sy = Mathf.Clamp(y - shiftY, 0, height - 1);
                back *= 1 - 0.4f * shadow[sy * width + x];
                var s = subject[i];
                var through = 1 - s.a / 255f;
                output[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(s.r + back.r * 255 * through), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(s.g + back.g * 255 * through), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(s.b + back.b * 255 * through), 0, 255), 255);
            }
            return output;
        }

        /// <summary>The subject's coverage blurred (three box passes each way, about a Gaussian), 0 to 1.</summary>
        private static float[] SoftAlpha(Color32[] pixels, int width, int height, int radius)
        {
            var a = pixels.Select(p => p.a / 255f).ToArray();
            var b = new float[a.Length];
            for (var pass = 0; pass < 3; pass++)
            {
                BoxBlur(a, b, width, height, radius, true);
                BoxBlur(b, a, width, height, radius, false);
            }
            return a;
        }

        private static void BoxBlur(float[] source, float[] target, int width, int height, int radius, bool horizontal)
        {
            var lines = horizontal ? height : width;
            var length = horizontal ? width : height;
            for (var line = 0; line < lines; line++)
            {
                int Index(int k) => horizontal ? line * width + Mathf.Clamp(k, 0, length - 1) : Mathf.Clamp(k, 0, length - 1) * width + line;
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                    sum += source[Index(k)];
                for (var k = 0; k < length; k++)
                {
                    target[Index(k)] = sum / (2 * radius + 1);
                    sum += source[Index(k + radius + 1)] - source[Index(k - radius)];
                }
            }
        }

        private static Color Hex(int rgb) => new Color((rgb >> 16 & 0xFF) / 255f, (rgb >> 8 & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
