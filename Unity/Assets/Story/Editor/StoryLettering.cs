using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SDFPipeline.Story;
using SdfGui;
using SdfStory.Layout;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SdfStory.Editor
{
    /// <summary>
    /// Draws a frame's comic devices and lettering over its finished render (ticket 31.6): speed lines, motion lines, impact bursts, the
    /// speech balloons with tails, caption boxes, Prime's jagged sky box, the SFX lettering and the frame's irregular ink border. The
    /// shapes are antialiased coverage masks (SdfStory.Layout, pure and tested) painted into the frame's pixels; the text is TextMesh Pro
    /// (the GUI's Fredoka for speech and Lilita One for names and SFX), rendered by an orthographic camera at one unit per pixel and
    /// composited over the shapes. Where things go (the safe area, balloons kept apart, the tails) is decided by <see cref="StoryLetterLayout"/>.
    /// </summary>
    public static class StoryLettering
    {
        private const string FontFolder = "Assets/GUI/Fonts";
        private const int TextLayer = 31;
        private const float OutlineWidth = 4;
        /// <summary>TextMesh Pro's 3D text is a tenth of its font size in world units: the lettering camera sees ten pixels to the unit.</summary>
        private const float Unit = 0.1f;

        private static readonly Color Ink = GuiPalette.Ink;
        private static readonly Color Balloon = GuiPalette.Hex(0xFFFAEE);
        private static readonly Color Caption = GuiPalette.Cream;
        private static readonly Color PrimeBlue = GuiPalette.Hex(0x2F6FDB);
        private static readonly Color PrimeBox = GuiPalette.Hex(0x3C82F0);

        /// <summary>The colour of a speaker's name; null for the narrator.</summary>
        public static Color? SpeakerColour(string speaker)
        {
            switch (speaker)
            {
                case "42": return GuiPalette.Amanita;
                case "Prime": return PrimeBlue;
                case "Mushroom 7": return GuiPalette.Amanita;
                default: return null;
            }
        }

        /// <summary>The name set over a speaker's lines.</summary>
        public static string SpeakerName(string speaker)
        {
            switch (speaker)
            {
                case "Prime": return "PRIME";
                case "Mushroom 7": return "MUSHROOM 7";
                case "42": return "42";
                default: return null;
            }
        }

        /// <summary>
        /// Letters one frame. <paramref name="pixels"/> are the frame's, y up as Texture2D has them; <paramref name="actorPoint"/> gives the frame
        /// pixel (y down) of an actor for a tail that points at one, or null when it is out of view.
        /// </summary>
        public static void Letter(Color[] pixels, int width, int height, StoryFrame frame, int seed, Func<string, Pt?> actorPoint)
        {
            var area = new Box(0, 0, width, height);
            var safe = StoryLetterLayout.SafeArea(area);
            var mask = new CoverageMask(width, height);
            var dark = MeanLuminance(pixels) < 0.38f;
            var lineColour = dark ? GuiPalette.Cream : Ink;

            void Paint(Color colour, float alpha = 1)
            {
                var cover = mask.Cover;
                for (var i = 0; i < cover.Length; i++)
                {
                    var a = cover[i] * alpha;
                    if (a <= 0)
                        continue;
                    var y = i / width;
                    var index = (height - 1 - y) * width + i % width;
                    pixels[index] = Color.Lerp(pixels[index], colour, a);
                }
                mask.Clear();
            }

            // Speed and motion lines go under everything
            var streakSeed = seed * 31;
            foreach (var device in frame.Devices)
            {
                if (device.Kind == "speedlines")
                {
                    var streaks = device.Style == "radial"
                        ? StoryLetterLayout.RadialLines(area, Point(device.At ?? new StoryVec2(0.5f, 0.5f), width, height), Math.Max(0.2f, device.Density), 0.55f, streakSeed++)
                        : StoryLetterLayout.ParallelLines(area, device.Angle, Math.Max(0.2f, device.Density), streakSeed++);
                    foreach (var streak in streaks)
                        mask.Segment(streak.From, streak.To, 0, streak.Width * 0.5f);
                    Paint(lineColour, 0.85f);
                }
                else if (device.Kind == "motionlines" && device.At.HasValue)
                {
                    foreach (var streak in StoryLetterLayout.MotionLines(Point(device.At.Value, width, height), device.Angle, Math.Max(0.05f, device.Length) * width, streakSeed++))
                        mask.Segment(streak.From, streak.To, 0, streak.Width * 0.5f);
                    Paint(lineColour, 0.9f);
                }
            }

            // Text objects, measured and set, before the shapes are known
            var letters = new Letters(width, height);
            try
            {
                var placed = new List<Box>();
                var sfxItems = new List<(StoryText text, Box box, float size)>();
                var bursts = frame.Devices.Where(d => d.Kind == "burst" && d.At.HasValue).ToList();
                var scale = Math.Min(1f, 0.55f + frame.H * 0.45f) * Math.Min(1f, 0.6f + frame.W * 0.4f);

                // SFX first: they sit where the picture says, and the speech is kept clear of them
                foreach (var text in frame.Texts.Where(t => t.Kind == StoryTextKind.Sfx))
                {
                    var size = 112 * Math.Max(0.4f, text.Size) * scale;
                    var measured = letters.Sfx(text.Text, size, text.Angle);
                    var centre = Point(text.Anchor, width, height);
                    var burstMargin = new Pt(60, 46);
                    var box = StoryLetterLayout.ClampInto(Box.Around(centre, measured.X + burstMargin.X * 2, measured.Y + burstMargin.Y * 2), safe);
                    sfxItems.Add((text, box, size));
                    placed.Add(box);
                    letters.PlaceSfx(box);
                }

                // Speech and captions
                var speech = new List<LetteredSpeech>();
                foreach (var text in frame.Texts.Where(t => t.Kind != StoryTextKind.Sfx))
                {
                    var item = letters.Speech(text, scale, area, safe, placed, actorPoint);
                    speech.Add(item);
                    placed.Add(item.Outline);
                }

                // Bursts: the ones the data asks for, and one behind each SFX that has none near it
                var burstSeed = seed * 17;
                foreach (var device in bursts)
                {
                    var at = Point(device.At.Value, width, height);
                    var near = sfxItems.FirstOrDefault(s => Pt.Distance(s.box.Center, at) < 0.18f * width);
                    if (near.text != null)
                        continue;
                    var radius = 150 * Math.Max(0.3f, device.Size) * scale;
                    DrawBurst(mask, Paint, at, radius, radius * 0.85f, burstSeed++);
                }
                foreach (var (text, box, size) in sfxItems)
                {
                    var tint = text.Text.StartsWith("BZZ", StringComparison.Ordinal) ? GuiPalette.Spore : GuiPalette.Gold;
                    DrawBurst(mask, Paint, box.Center, box.W * 0.54f, box.H * 0.6f, burstSeed++, tint);
                }

                foreach (var item in speech)
                    item.Draw(mask, Paint);

                // The text on top of the shapes
                letters.Render(pixels);
            }
            finally
            {
                letters.Dispose();
            }

            // The frame's ink border, irregular
            var outer = new[] { new Pt(0, 0), new Pt(width, 0), new Pt(width, height), new Pt(0, height) };
            mask.Fill(outer, StoryLetterLayout.WobblyRect(area, StoryLetterLayout.BorderWidth, 1.8f, seed * 7 + 3));
            Paint(Ink);
        }

        private static Pt Point(StoryVec2 p, int width, int height) => new Pt(p.X * width, p.Y * height);

        private static float MeanLuminance(Color[] pixels)
        {
            double sum = 0;
            var step = Math.Max(1, pixels.Length / 4000);
            var count = 0;
            for (var i = 0; i < pixels.Length; i += step, count++)
                sum += pixels[i].r * 0.3 + pixels[i].g * 0.59 + pixels[i].b * 0.11;
            return (float)(sum / Math.Max(1, count));
        }

        private static void DrawBurst(CoverageMask mask, Action<Color, float> paint, Pt centre, float rx, float ry, int seed, Color? fill = null)
        {
            var outline = StoryLetterLayout.Burst(centre, rx, ry, 15, seed);
            mask.Fill(outline);
            mask.Stroke(outline, OutlineWidth * 2, true);
            paint(Ink, 1);
            mask.Fill(Scaled(outline, centre, 0.93f));
            paint(fill ?? GuiPalette.Gold, 1);
            mask.Fill(Scaled(outline, centre, 0.62f));
            paint(Color.Lerp(fill ?? GuiPalette.Gold, Color.white, 0.55f), 1);
        }

        private static Pt[] Scaled(Pt[] points, Pt centre, float factor) => points.Select(p => centre + (p - centre) * factor).ToArray();

        // ------------------------------------------------------------------------------------------------------------ speech

        /// <summary>A balloon, caption or sky box laid out and ready to draw (its text is already placed).</summary>
        private sealed class LetteredSpeech
        {
            public Box Outline;
            public Action<CoverageMask, Action<Color, float>> Draw;
        }

        /// <summary>The TextMesh Pro texts of a frame: measuring them, placing them, and rendering them over the shapes.</summary>
        private sealed class Letters : IDisposable
        {
            private readonly int width, height;
            private readonly GameObject root;
            private readonly TMP_FontAsset fredoka, lilita;
            private readonly Material lilitaInk;
            private readonly Material nameMaterial;
            private readonly List<TextMeshPro> texts = new List<TextMeshPro>();
            private readonly TextMeshPro scratch;

            public Letters(int width, int height)
            {
                this.width = width;
                this.height = height;
                fredoka = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontFolder}/Fredoka SDF.asset");
                lilita = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontFolder}/Lilita One SDF.asset");
                lilitaInk = AssetDatabase.LoadAssetAtPath<Material>($"{FontFolder}/Lilita One Ink.mat");
                if (fredoka == null || lilita == null || lilitaInk == null)
                    throw new InvalidOperationException($"the GUI fonts are missing in {FontFolder} (run the GUI assets build)");
                // Far from any stage, where only the lettering camera looks
                root = EditorUtility.CreateGameObjectWithHideFlags("Story Lettering", HideFlags.HideAndDontSave);
                root.transform.position = new Vector3(0, 60000, 0);
                scratch = Make("", fredoka, null, 30, Color.white, 100);
                texts.Remove(scratch);
                // Only for measuring: not on the lettering camera's layer
                scratch.gameObject.layer = 0;
                // Names are small: a thinner ink outline and no shadow than the SFX have
                nameMaterial = new Material(lilitaInk) { hideFlags = HideFlags.HideAndDontSave };
                nameMaterial.DisableKeyword("UNDERLAY_ON");
                nameMaterial.SetFloat("_OutlineWidth", 0.16f);
            }

            public void Dispose()
            {
                Object.DestroyImmediate(nameMaterial);
                Object.DestroyImmediate(root);
            }

            private TextMeshPro Make(string text, TMP_FontAsset font, Material material, float size, Color colour, float columnWidth)
            {
                var item = EditorUtility.CreateGameObjectWithHideFlags("Text", HideFlags.HideAndDontSave, typeof(TextMeshPro));
                item.layer = TextLayer;
                item.transform.SetParent(root.transform, false);
                var tmp = item.GetComponent<TextMeshPro>();
                tmp.font = font;
                if (material != null)
                    tmp.fontSharedMaterial = material;
                tmp.richText = true;
                tmp.fontSize = size;
                // TMP writes vertex colours as linear values into a linear target
                tmp.color = colour.linear;
                tmp.textWrappingMode = TextWrappingModes.Normal;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.rectTransform.sizeDelta = new Vector2(columnWidth, 100) * Unit;
                tmp.text = text;
                texts.Add(tmp);
                return tmp;
            }

            private static Pt Preferred(TextMeshPro tmp, string text, float column)
            {
                var size = tmp.GetPreferredValues(text, column * Unit, 10000);
                return new Pt(size.x / Unit, size.y / Unit);
            }

            private void Put(TextMeshPro tmp, Pt centre, Pt size, float angle = 0)
            {
                tmp.rectTransform.sizeDelta = new Vector2(size.X, size.Y) * Unit;
                tmp.rectTransform.localPosition = new Vector3(centre.X - width * 0.5f, height * 0.5f - centre.Y, 0) * Unit;
                tmp.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
                tmp.ForceMeshUpdate();
            }

            /// <summary>SFX lettering (Lilita One, ink outline), one line, centred where it will be put; returns its size.</summary>
            public Pt Sfx(string text, float size, float angle)
            {
                var tmp = Make(text, lilita, lilitaInk, size, GuiPalette.Cream, 4000);
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.characterSpacing = 4;
                tmp.ForceMeshUpdate();
                var preferred = Preferred(tmp, text, 4000);
                sfx.Add(tmp);
                sfxSizes.Add(preferred);
                sfxAngles.Add(angle);
                return preferred;
            }

            private readonly List<TextMeshPro> sfx = new List<TextMeshPro>();
            private readonly List<Pt> sfxSizes = new List<Pt>();
            private readonly List<float> sfxAngles = new List<float>();
            private int sfxPlaced;

            public LetteredSpeech Speech(StoryText text, float scale, Box area, Box safe, List<Box> placed, Func<string, Pt?> actorPoint)
            {
                var kind = text.Kind;
                var baseSize = 36f * scale;
                var maxColumn = Math.Min(safe.W * 0.62f, (kind == StoryTextKind.Caption ? 1000f : 560f) * Math.Max(0.6f, scale));
                var body = ToRich(text.Text);
                var onBlue = kind == StoryTextKind.Sky;
                var bodyColour = onBlue ? GuiPalette.Cream : Ink;

                Pt Measure(float size, float column)
                {
                    scratch.fontSize = size;
                    scratch.text = body;
                    return Preferred(scratch, body, column);
                }

                var size = StoryLetterLayout.FitSize(Measure, maxColumn, safe.H * 0.32f, 18, baseSize);
                var column = kind == StoryTextKind.Caption
                    ? StoryLetterLayout.ColumnWidth(Measure, size, maxColumn * 0.4f, maxColumn, 0.22f)
                    : StoryLetterLayout.ColumnWidth(Measure, size, maxColumn * 0.35f, maxColumn, 0.42f);
                var measured = Measure(size, column);
                var name = kind == StoryTextKind.Caption ? null : SpeakerName(text.Speaker);
                var nameColour = SpeakerColour(text.Speaker);
                var nameSize = size * 0.82f;
                var nameHeight = name != null ? nameSize * 1.05f : 0;
                var contentW = Math.Max(measured.X, name != null ? nameSize * name.Length * 0.62f : 0);
                var contentH = nameHeight + measured.Y;
                var centre = Point(text.Anchor, (int)area.W, (int)area.H);

                Box outline;
                Pt[] bodyShape = null;
                float jagged = 0;
                switch (kind)
                {
                    case StoryTextKind.Caption:
                        outline = Box.Around(centre, contentW + 64, contentH + 40);
                        break;
                    case StoryTextKind.Sky:
                        jagged = 15;
                        outline = Box.Around(centre, contentW + 80, contentH + 54);
                        break;
                    default:
                        outline = StoryLetterLayout.BodyAround(centre, contentW, contentH);
                        break;
                }
                var allowed = kind == StoryTextKind.Sky ? safe.Inset(jagged + OutlineWidth) : safe.Inset(OutlineWidth);
                var box = StoryLetterLayout.Place(outline, allowed, placed);

                // The text, centred in the box
                var top = box.Center.Y - contentH * 0.5f;
                if (name != null)
                {
                    var label = Make(name, lilita, nameMaterial, nameSize, onBlue ? GuiPalette.Gold : nameColour ?? Ink, 600);
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    Put(label, new Pt(box.Center.X, top + nameHeight * 0.5f), new Pt(contentW + 40, nameHeight));
                }
                var bodyText = Make(body, fredoka, null, size, bodyColour, column);
                Put(bodyText, new Pt(box.Center.X, top + nameHeight + measured.Y * 0.5f), new Pt(column, measured.Y + 8));

                var edge = OutlineWidth;
                Pt[] tail = null;
                if (kind == StoryTextKind.Balloon)
                {
                    Pt? target = text.TailAt.HasValue ? Point(text.TailAt.Value, (int)area.W, (int)area.H) : (text.TailActor != null ? actorPoint(text.TailActor) : null);
                    if (target.HasValue)
                    {
                        // Inside the safe area, so that the tail does not run off the frame
                        var clamped = new Pt(Math.Max(safe.X, Math.Min(safe.Right, target.Value.X)), Math.Max(safe.Y, Math.Min(safe.Bottom, target.Value.Y)));
                        tail = StoryLetterLayout.Tail(box, clamped, 18, 260 * Math.Max(0.6f, scale));
                    }
                    bodyShape = StoryLetterLayout.BalloonBody(box);
                }
                else if (kind == StoryTextKind.Sky)
                {
                    bodyShape = StoryLetterLayout.JaggedBox(box, 46, jagged, (int)(box.X + box.Y));
                }

                var full = tail == null ? box : Union(box, tail);
                var drawn = new LetteredSpeech
                {
                    // The area the lettering takes, including a tail, for keeping the next one apart
                    Outline = full,
                    Draw = (mask, paint) =>
                    {
                        if (kind == StoryTextKind.Caption)
                        {
                            // Cream box, an ink outline and a hard ink shadow
                            var shadow = new Box(box.X + 7, box.Y + 7, box.W, box.H);
                            mask.Fill(Rect(shadow));
                            paint(Ink, 0.8f);
                            mask.Fill(Rect(box.Grow(edge)));
                            paint(Ink, 1);
                            mask.Fill(Rect(box));
                            paint(Caption, 1);
                            return;
                        }
                        var ink = bodyShape;
                        mask.Fill(ink);
                        mask.Stroke(ink, edge * 2, true);
                        if (tail != null)
                        {
                            mask.Fill(tail);
                            mask.Stroke(tail, edge * 2, true);
                        }
                        paint(Ink, 1);
                        mask.Fill(ink);
                        if (tail != null)
                            mask.Fill(tail);
                        paint(onBlue ? PrimeBox : Balloon, 1);
                    },
                };
                return drawn;
            }

            private static Pt[] Rect(Box b) => new[] { new Pt(b.X, b.Y), new Pt(b.Right, b.Y), new Pt(b.Right, b.Bottom), new Pt(b.X, b.Bottom) };

            private static Box Union(Box box, Pt[] points)
            {
                var left = box.X;
                var top = box.Y;
                var right = box.Right;
                var bottom = box.Bottom;
                foreach (var p in points)
                {
                    left = Math.Min(left, p.X);
                    top = Math.Min(top, p.Y);
                    right = Math.Max(right, p.X);
                    bottom = Math.Max(bottom, p.Y);
                }
                return new Box(left, top, right - left, bottom - top);
            }

            /// <summary>*italics* of the story file as TextMesh Pro's tags.</summary>
            private static string ToRich(string text) => Regex.Replace(text, @"\*(.+?)\*", "<i>$1</i>");

            /// <summary>Puts the SFX lettering where its box was placed (called in the order the SFX were measured).</summary>
            public void PlaceSfx(Box box)
            {
                var tmp = sfx[sfxPlaced];
                Put(tmp, box.Center, new Pt(sfxSizes[sfxPlaced].X + 20, sfxSizes[sfxPlaced].Y + 10), sfxAngles[sfxPlaced]);
                sfxPlaced++;
            }

            /// <summary>Renders the texts on white and on black and composites them over the pixels (alpha from the difference).</summary>
            public void Render(Color[] pixels)
            {
                var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Lettering Camera", HideFlags.HideAndDontSave, typeof(Camera));
                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                try
                {
                    var camera = cameraObject.GetComponent<Camera>();
                    camera.orthographic = true;
                    camera.orthographicSize = height * 0.5f * Unit;
                    camera.aspect = width / (float)height;
                    camera.nearClipPlane = 0.1f;
                    camera.farClipPlane = 100;
                    camera.transform.position = root.transform.position + new Vector3(0, 0, -10);
                    camera.transform.rotation = Quaternion.identity;
                    camera.cullingMask = 1 << TextLayer;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.allowHDR = false;
                    camera.allowMSAA = false;
                    var data = camera.GetUniversalAdditionalCameraData();
                    data.renderPostProcessing = false;
                    data.antialiasing = AntialiasingMode.None;
                    data.renderShadows = false;
                    camera.targetTexture = target;

                    Color[] Shot(Color background)
                    {
                        camera.backgroundColor = background;
                        camera.Render();
                        var active = RenderTexture.active;
                        RenderTexture.active = target;
                        readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        readable.Apply();
                        RenderTexture.active = active;
                        return readable.GetPixels();
                    }

                    var onWhite = Shot(Color.white);
                    var onBlack = Shot(Color.black);
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var w = onWhite[i];
                        var b = onBlack[i];
                        var alpha = Mathf.Clamp01(1 - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3f);
                        if (alpha <= 0.002f)
                            continue;
                        // b is the colour times alpha, in linear light; the pixels are display values
                        var colour = new Color(
                            Mathf.LinearToGammaSpace(Mathf.Clamp01(b.r / alpha)),
                            Mathf.LinearToGammaSpace(Mathf.Clamp01(b.g / alpha)),
                            Mathf.LinearToGammaSpace(Mathf.Clamp01(b.b / alpha)));
                        pixels[i] = Color.Lerp(pixels[i], colour, alpha);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(readable);
                    target.Release();
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(cameraObject);
                }
            }
        }
    }
}
