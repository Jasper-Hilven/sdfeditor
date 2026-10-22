using System;
using System.IO;
using System.Linq;
using SDFPipeline.Story;
using SdfGui;
using SdfLevel.Editor;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SdfStory.Editor
{
    /// <summary>
    /// The pictures on 42's monitor in the story intro's panels (ticket 31.7), written to Assets/Story/Screens/&lt;name&gt;.png at 1024 × 576:
    /// <c>screen-meadow</c> (the meadow from eye height at the red spawn with a blue scout in the sights and a first-person HUD), <c>screen-victory</c>
    /// (the same under a VICTORY! burst), <c>screen-glitch</c> (the picture torn into blue pixel blocks) and <c>screen-prime</c> (Chanterelle Prime
    /// and his too-small crown on a blue backdrop with scanlines, a smug grin drawn on). The meadow is shot through the level's own player camera
    /// (<see cref="MushroomSnapshot.RenderView"/>, as <see cref="LevelSnapshot"/> does); the HUD is drawn here in the GUI's palette, not by
    /// GuiSnapshot, which shoots whole GUI screens, so that the shot does not depend on the game's HUD layout. They are only read by the capture.
    /// </summary>
    public static class StoryScreens
    {
        public const int Width = 1024;
        public const int Height = 576;

        public static string PathOf(string name) => $"{StoryCapture.ScreenFolder}/{name}.png";

        /// <summary>Whether every screen of the story file has its picture.</summary>
        public static bool AllExist() => StoryFile.ScreenTextures.All(name => File.Exists(PathOf(name)));

        /// <summary>Makes the four pictures (the missing ones only, unless <paramref name="force"/>).</summary>
        public static void Make(bool force)
        {
            if (!force && AllExist())
                return;
            Directory.CreateDirectory(StoryCapture.ScreenFolder);
            var meadow = ShootMeadow();
            DrawHud(meadow);
            Save("screen-meadow", meadow);

            var victory = (Color32[])meadow.Clone();
            Dim(victory, 0.45f);
            // The banner reuses the lettering: a gold burst with the word in it
            var banner = new StoryFrame();
            banner.Texts.Add(new StoryText { Kind = StoryTextKind.Sfx, Text = "VICTORY!", Anchor = new StoryVec2(0.5f, 0.46f), Size = 0.75f, Angle = -4 });
            Save("screen-victory", Lettered(victory, banner));

            Save("screen-glitch", Glitch(meadow));
            Save("screen-prime", ShootPrime());
            AssetDatabase.Refresh();
            foreach (var name in StoryFile.ScreenTextures)
                ApplyImportSettings(PathOf(name));
            Debug.Log($"SDF story: screens written to {StoryCapture.ScreenFolder}");
        }

        /// <summary>Editor-only pictures that no build loads: plain, no mipmaps, kept as they are.</summary>
        private static void ApplyImportSettings(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void Save(string name, Color32[] pixels)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(PathOf(name), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static Color32[] Lettered(Color32[] pixels, StoryFrame frame)
        {
            var colours = pixels.Select(p => (Color)p).ToArray();
            StoryLettering.Letter(colours, Width, Height, frame, 77, name => null);
            return colours.Select(c => (Color32)c).ToArray();
        }

        // ---------------------------------------------------------------------------------------------------------------- the meadow

        /// <summary>The meadow from the red spawn at eye height, a blue scout of the other team in front of the crosshair.</summary>
        private static Color32[] ShootMeadow()
        {
            LevelBuilder.BuildStale();
            var scene = EditorSceneManager.OpenScene(StoryCapture.MeadowScenePath, OpenSceneMode.Single);
            var player = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.PlayerName);
            var camera = player.GetComponentInChildren<Camera>();
            var eye = camera.transform.position;
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            // Down the field towards the other castle: the player's own view
            var right = Vector3.Cross(Vector3.up, forward);
            var foe = eye + forward * 10f - right * 2f;
            Physics.SyncTransforms();
            foe.y = Physics.Raycast(foe + Vector3.up * 60, Vector3.down, out var hit, 200) ? hit.point.y : eye.y - 1.5f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/chanterelle-scout.prefab");
            var scout = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            scout.transform.SetPositionAndRotation(foe, Quaternion.LookRotation(-forward));
            foreach (var group in scout.GetComponentsInChildren<LODGroup>())
                group.ForceLOD(0);
            try
            {
                var shot = EditorUtility.CreateGameObjectWithHideFlags("Story Screen Camera", HideFlags.HideAndDontSave, typeof(Camera));
                try
                {
                    var view = shot.GetComponent<Camera>();
                    StoryCapture.Stage.CopyCamera(camera, view);
                    // The scout is in the sights: the view turns to the middle of him
                    view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(foe + Vector3.up * 0.8f - eye));
                    view.aspect = Width / (float)Height;
                    return Render(view);
                }
                finally
                {
                    Object.DestroyImmediate(shot);
                }
            }
            finally
            {
                Object.DestroyImmediate(scout);
            }
        }

        /// <summary>One render of a camera into the picture's size, with anti-aliasing, as its pixels (y up).</summary>
        private static Color32[] Render(Camera camera)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                RenderTexture.active = active;
                var pixels = texture.GetPixels32();
                Object.DestroyImmediate(texture);
                return pixels;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- Prime

        private static Color32[] ShootPrime()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MushroomScene.BuildLighting(scene, null, new MushroomScene.IndoorLighting
            {
                lightDirection = new Vector3(0.35f, 0.55f, -1).normalized,
                lightColor = new Color(1f, 0.96f, 0.88f),
                ambient = new Color(0.42f, 0.44f, 0.6f),
            });
            var cameraObject = MushroomScene.CreateCamera(scene, "Main Camera", 32).gameObject;
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.22f, 0.7f);
            camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/chanterelle-scout.prefab");
            var scout = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            scout.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var group in scout.GetComponentsInChildren<LODGroup>())
                group.ForceLOD(0);
            Physics.SyncTransforms();
            // The cap and stem, not the floating eyes and hands
            var body = BoundsOf(scout, r => !IsPartOf(r, scout, "eye") && !IsPartOf(r, scout, "hand"));

            // The crown on the cap, a size too small for it: about two thirds of the cap's width, tipped a little
            var crownPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/prime-crown.prefab");
            var crown = (GameObject)PrefabUtility.InstantiatePrefab(crownPrefab, scene);
            foreach (var group in crown.GetComponentsInChildren<LODGroup>())
                group.ForceLOD(0);
            crown.transform.localScale = Vector3.one;
            var crownSize = BoundsOf(crown).size;
            var factor = body.size.x * CrownShare / Mathf.Max(crownSize.x, crownSize.z);
            crown.transform.localScale = Vector3.one * factor;
            crown.transform.SetPositionAndRotation(new Vector3(body.center.x, body.max.y - CrownSink * factor, body.center.z), Quaternion.Euler(0, 0, 8));

            var eyes = BoundsOf(scout, r => IsPartOf(r, scout, "eye"));
            var crownBounds = BoundsOf(crown);
            // The head: from just under the eyes to the crown's points, as wide as the cap
            var bottom = eyes.min.y - 0.42f;
            var top = crownBounds.max.y + 0.2f;
            var aim = new Vector3(body.center.x, (bottom + top) * 0.5f, body.center.z);
            var halfHeight = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var distance = Mathf.Max((top - bottom) * 0.5f / halfHeight, body.size.x * 0.5f * 1.08f / (halfHeight * Width / Height));
            camera.transform.SetPositionAndRotation(aim + new Vector3(0, 0, distance), Quaternion.LookRotation(-Vector3.forward));
            Debug.Log($"SDF story: prime framing: cap {body.size:F2} at {body.center:F2}, crown {crownSize:F2} x{factor:F2}, eyes {eyes.center:F2} {eyes.size:F2}, camera {camera.transform.position:F2}");

            var pixels = Render(camera);
            DrawBackdrop(pixels);
            // A smug grin under the eyes, from the eyes' projection
            var mouth = ToPicture(camera, new Vector3(eyes.center.x, eyes.min.y - eyes.size.y * 0.55f, eyes.center.z));
            var span = ToPicture(camera, new Vector3(eyes.max.x, eyes.center.y, eyes.center.z)).x - ToPicture(camera, new Vector3(eyes.min.x, eyes.center.y, eyes.center.z)).x;
            DrawGrin(pixels, mouth.x, mouth.y, Mathf.Abs(span) * 0.34f);
            DrawScanlines(pixels);
            return pixels;
        }

        /// <summary>A world point's pixel in the picture (y up), for a camera that has been placed.</summary>
        private static Vector2 ToPicture(Camera camera, Vector3 point)
        {
            camera.aspect = Width / (float)Height;
            var clip = camera.projectionMatrix * camera.worldToCameraMatrix * new Vector4(point.x, point.y, point.z, 1);
            return new Vector2((clip.x / clip.w * 0.5f + 0.5f) * Width, (clip.y / clip.w * 0.5f + 0.5f) * Height);
        }

        /// <summary>Whether a renderer is in a child of the scout whose name starts with <paramref name="prefix"/> (eye.0, hand.1 ...).</summary>
        private static bool IsPartOf(Renderer renderer, GameObject root, string prefix)
        {
            for (var t = renderer.transform; t != null && t != root.transform; t = t.parent)
                if (t.name.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>The crown's width as a share of the cap's: smaller than the cap, which is the joke.</summary>
        private const float CrownShare = 0.62f;
        /// <summary>How far the crown's foot sits below the cap's top, as a share of its own size.</summary>
        private const float CrownSink = 0.06f;

        private static Bounds BoundsOf(GameObject root, Func<Renderer, bool> filter = null)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && (filter == null || filter(r))).ToList();
            if (renderers.Count == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.1f);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void DrawBackdrop(Color32[] pixels)
        {
            // Only where the camera drew the plain clear colour: a gradient of the blue, and a faint grid of pixels as a screen has
            var clear = (Color32)new Color(0.09f, 0.22f, 0.7f);
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var p = pixels[y * Width + x];
                if (Mathf.Abs(p.r - clear.r) > 6 || Mathf.Abs(p.g - clear.g) > 6 || Mathf.Abs(p.b - clear.b) > 6)
                    continue;
                var t = y / (float)Height;
                var radial = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x / (float)Width, t), new Vector2(0.5f, 0.55f)) * 1.3f);
                var c = Color.Lerp(new Color(0.05f, 0.1f, 0.42f), new Color(0.22f, 0.55f, 1f), radial * radial);
                if ((x / 16 + y / 16) % 2 == 0)
                    c = Color.Lerp(c, Color.white, 0.05f);
                pixels[y * Width + x] = c;
            }
        }

        private static void DrawGrin(Color32[] pixels, float cx, float cy, float halfWidth)
        {
            // A wide smile, a little higher on one side, in ink with two dimples; y is up in a texture
            var ink = GuiPalette.Ink;
            var thickness = Mathf.Max(5f, halfWidth * 0.07f);
            for (var s = 0; s <= 400; s++)
            {
                var t = s / 400f * 2 - 1;
                var x = cx + t * halfWidth;
                var y = cy - (1 - t * t) * halfWidth * 0.32f + t * halfWidth * 0.1f;
                Disc(pixels, x, y, thickness, ink);
            }
            Disc(pixels, cx - halfWidth * 1.02f, cy - halfWidth * 0.1f, thickness * 1.2f, ink);
            Disc(pixels, cx + halfWidth * 1.02f, cy + halfWidth * 0.1f + halfWidth * 0.06f, thickness * 1.2f, ink);
        }

        private static void DrawScanlines(Color32[] pixels)
        {
            for (var y = 0; y < Height; y++)
            {
                var k = y % 4 < 1 ? 0.78f : 1f;
                if (k >= 1f)
                    continue;
                for (var x = 0; x < Width; x++)
                {
                    var p = pixels[y * Width + x];
                    pixels[y * Width + x] = new Color32((byte)(p.r * k), (byte)(p.g * k), (byte)(p.b * k), 255);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- HUD, glitch

        /// <summary>A first-person HUD in the GUI's palette: crosshair, health, ammo, the two teams' scores; 5 px from the picture's edges.</summary>
        private static void DrawHud(Color32[] pixels)
        {
            const int margin = 5;
            var cx = Width / 2f;
            var cy = Height / 2f;
            var white = new Color(1, 1, 1, 1);
            Ring(pixels, cx, cy, 14, 2.5f, white);
            Disc(pixels, cx, cy, 2.5f, white);
            foreach (var dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                for (var d = 20; d <= 30; d += 2)
                    Disc(pixels, cx + dir.x * d, cy + dir.y * d, 1.8f, white);

            // Health, bottom left: an ink pill with a red fill
            Pill(pixels, margin, margin, 230, 30, GuiPalette.Ink, 0.75f);
            Pill(pixels, margin + 5, margin + 5, 150, 20, GuiPalette.Amanita, 1f);
            Pill(pixels, margin + 5, margin + 5, 150, 7, Color.Lerp(GuiPalette.Amanita, Color.white, 0.5f), 0.6f);
            // Ammo, bottom right: a row of gold pips
            Pill(pixels, Width - margin - 190, margin, 190, 30, GuiPalette.Ink, 0.75f);
            for (var i = 0; i < 10; i++)
                Pill(pixels, Width - margin - 182 + i * 18, margin + 6, 11, 18, i < 7 ? GuiPalette.Gold : GuiPalette.Dusk, 1f);
            // The teams' scores, top middle: red and blue pills
            Pill(pixels, (int)cx - 90, Height - margin - 28, 84, 28, GuiPalette.Amanita, 0.9f);
            Pill(pixels, (int)cx + 6, Height - margin - 28, 84, 28, new Color(0.24f, 0.51f, 0.94f), 0.9f);
        }

        /// <summary>The meadow's picture torn up: rows shifted sideways, blocks of blue, a darker, bluer base.</summary>
        private static Color32[] Glitch(Color32[] meadow)
        {
            var random = new SdfStory.Layout.StoryRandom(5);
            var result = new Color32[meadow.Length];
            for (var y = 0; y < Height; y++)
            {
                var shift = y / 12 % 7 == 3 || y / 30 % 5 == 1 ? (int)random.Range(-160, 160) : 0;
                if (y % 12 == 0)
                    random.Value();
                for (var x = 0; x < Width; x++)
                {
                    var source = meadow[y * Width + ((x + shift) % Width + Width) % Width];
                    var c = (Color)source;
                    c = new Color(c.r * 0.35f, c.g * 0.55f, Mathf.Min(1, c.b * 0.7f + 0.25f), 1);
                    result[y * Width + x] = c;
                }
            }
            // Blocks of every blue, on a coarse grid
            const int cell = 32;
            for (var by = 0; by < Height / cell; by++)
            for (var bx = 0; bx < Width / cell; bx++)
            {
                if (random.Value() > 0.3f)
                    continue;
                var w = 1 + (int)(random.Value() * 3);
                var blue = Color.Lerp(new Color(0.06f, 0.18f, 0.7f), new Color(0.45f, 0.8f, 1f), random.Value());
                for (var y = by * cell; y < Mathf.Min(Height, (by + 1) * cell); y++)
                for (var x = bx * cell; x < Mathf.Min(Width, (bx + w) * cell); x++)
                    result[y * Width + x] = blue;
            }
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------------- drawing

        private static void Dim(Color32[] pixels, float amount)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = (Color)pixels[i];
                pixels[i] = Color.Lerp(c, new Color(0.1f, 0.07f, 0.2f), amount);
            }
        }

        private static void Blend(Color32[] pixels, int x, int y, Color colour, float alpha)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || alpha <= 0)
                return;
            var index = y * Width + x;
            pixels[index] = Color.Lerp(pixels[index], colour, Mathf.Clamp01(alpha * colour.a));
        }

        private static void Disc(Color32[] pixels, float cx, float cy, float radius, Color colour)
        {
            if (radius <= 0)
                return;
            for (var y = Mathf.FloorToInt(cy - radius - 1); y <= Mathf.CeilToInt(cy + radius + 1); y++)
            for (var x = Mathf.FloorToInt(cx - radius - 1); x <= Mathf.CeilToInt(cx + radius + 1); x++)
                Blend(pixels, x, y, colour, Mathf.Clamp01(radius + 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy))));
        }

        private static void Ring(Color32[] pixels, float cx, float cy, float radius, float width, Color colour)
        {
            for (var y = Mathf.FloorToInt(cy - radius - width - 1); y <= Mathf.CeilToInt(cy + radius + width + 1); y++)
            for (var x = Mathf.FloorToInt(cx - radius - width - 1); x <= Mathf.CeilToInt(cx + radius + width + 1); x++)
            {
                var distance = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius);
                Blend(pixels, x, y, colour, Mathf.Clamp01(width * 0.5f + 0.5f - distance));
            }
        }

        /// <summary>A rounded bar from its bottom left corner (y up).</summary>
        private static void Pill(Color32[] pixels, int x0, int y0, int w, int h, Color colour, float alpha)
        {
            var radius = h * 0.5f;
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                var dx = Mathf.Max(0, Mathf.Max(x0 + radius - (x + 0.5f), x + 0.5f - (x0 + w - radius)));
                var dy = y + 0.5f - (y0 + radius);
                var inside = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                Blend(pixels, x, y, colour, inside * alpha);
            }
        }
    }
}
