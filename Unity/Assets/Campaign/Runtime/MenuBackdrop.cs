using System.Collections.Generic;
using SdfGui;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SdfCampaign
{
    /// <summary>
    /// The world behind the menus in Boot (ticket 35): 42's desk, a diorama, with the wall of comic panels the player has seen pinned behind
    /// it. The camera moves between three places (<see cref="MenuShot"/>) as the menu changes, with a slow drift on unscaled time (so a pause
    /// or a time scale of 0 does not freeze it); the scene never changes. What the campaign has done shows: a small prop on the desk per arena
    /// won, the trophy gold once the campaign is complete, the wall's panels (only the comics seen; blank sheets elsewhere) and its light's tint.
    /// <para>
    /// The cheap fallback is the same wall as flat UI with a slow parallax and no 3D at all (<see cref="BackdropChoice"/>: Auto picks it on the
    /// lowest quality level and where the desk's models are not in the build; Settings can force either). <see cref="BootBuilder"/> builds
    /// the desk, this component and its references; the wall's cards are made here, from the save, whenever a menu comes up with a different one.
    /// </para>
    /// </summary>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        [Header("Built by BootBuilder")]
        public Camera cam;
        public Light key;
        [Tooltip("The 3D world: the desk, the room, the wall. Null where the desk's models do not exist: the flat wall is used")]
        public GameObject world;
        [Tooltip("The wall's frame: local x runs to the viewer's right, y up, the cards go in it")]
        public Transform board;
        [Tooltip("One child per arena, named as its step id, switched on once the arena is won")]
        public Transform props;
        public Renderer[] trophy = new Renderer[0];
        [Tooltip("The monitor's screen part and its lower levels of detail")]
        public Renderer[] screens = new Renderer[0];
        public Texture2D screenCover, screenGame, screenComplete;
        [Tooltip("The screen part's mesh bounds in its own space: centre x, centre y, half width, half height (a property block is not saved with the scene)")]
        public Vector4 screenRect = new Vector4(0, 0.59f, 0.34f, 0.191f);
        public Shader cardShader;

        public BackdropMode Mode { get; private set; } = BackdropMode.FlatWall;
        public MenuShot Shot { get; private set; } = MenuShot.Wide;

        private GuiFlow flow;
        private GuiScreen lastScreen = (GuiScreen)(-1);
        private string signature;
        private ShotPose pose;
        private bool posed;
        private Color keyColour;
        private bool keyKnown;
        private GameObject cards;
        private Canvas flatCanvas;
        private RectTransform flatBack, flatFront;
        private readonly List<Object> made = new List<Object>();
        private float seconds;

        // Pixels per metre of the flat wall, and how far it drifts
        private const float FlatScale = 700;
        // TransparentFX: nothing else of the project uses it, and the snapshots leave it on the camera (they take only the UI layer off)
        private const int FlatLayer = 1;

        private static readonly Color Cork = new Color(0.45f, 0.27f, 0.15f), CorkDark = new Color(0.32f, 0.18f, 0.1f), Wood = new Color(0.28f, 0.18f, 0.11f),
            Paper = new Color(0.97f, 0.93f, 0.83f), BlankPaper = new Color(0.99f, 0.97f, 0.9f), Shade = new Color(0.17f, 0.11f, 0.08f), Pin = new Color(0.86f, 0.22f, 0.2f);

        private const float PaperHeight = 0.245f, PictureWidth = 0.396f, PictureHeight = 0.2228f;
        private const float BoardWidth = 3.9f, BoardHeight = 1.62f;

        /// <summary>The shot a screen asks for; null keeps the one there is (a card over the title stays on the title's view).</summary>
        public static MenuShot? ShotFor(GuiScreen screen)
        {
            switch (screen)
            {
                case GuiScreen.Title:
                    return MenuShot.Wide;
                case GuiScreen.LevelSelect:
                    return MenuShot.Monitor;
                case GuiScreen.Story:
                case GuiScreen.Credits:
                case GuiScreen.Gallery:
                    return MenuShot.Wall;
                default:
                    return null;
            }
        }

        private void Start()
        {
            if (!Application.isPlaying)
                return;
            Refresh(true);
            Place(true);
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;
            seconds = Time.unscaledTime;
            var screenNow = Flow() != null ? flow.screen : GuiScreen.Title;
            if (screenNow != lastScreen)
            {
                lastScreen = screenNow;
                var wanted = ShotFor(screenNow);
                if (wanted.HasValue)
                    Shot = wanted.Value;
                Refresh(false);
            }
            Place(false);
        }

        private void OnDestroy()
        {
            foreach (var o in made)
                if (o != null)
                    Destroy(o);
            made.Clear();
        }

        private GuiFlow Flow()
        {
            if (flow == null)
                flow = GuiFlow.Persistent != null ? GuiFlow.Persistent : GuiFlow.Find(this);
            return flow;
        }

        /// <summary>For the snapshots and the editor: shows the backdrop as it is for a screen, at once, <paramref name="at"/> seconds into the drift.</summary>
        public void Snap(GuiScreen screen, float at = 0)
        {
            seconds = at;
            lastScreen = screen;
            var wanted = ShotFor(screen);
            if (wanted.HasValue)
                Shot = wanted.Value;
            Refresh(true);
            Place(true);
        }

        // ---------------------------------------------------------------- What the campaign has done

        private static Dictionary<string, int> PanelCounts()
        {
            var index = Resources.Load<TextAsset>(WallAtlas.Folder + "/" + WallAtlas.IndexName);
            return WallAtlas.ParseIndex(index != null ? index.text : null);
        }

        /// <summary>Redraws what depends on the save, the settings and the quality, when it differs from what is drawn.</summary>
        public void Refresh(bool force)
        {
            var progress = GuiCampaign.Progress;
            var definition = GuiCampaign.Definition;
            var counts = PanelCounts();
            var comics = WallContent.SeenComics(definition, progress, id => counts.TryGetValue(id, out var n) ? n : 0);
            var complete = DeskProgress.TrophyGold(progress, GuiCampaign.PreviewComplete);
            var mode = BackdropChoice.Resolve(GuiSettings.MenuBackdrop, QualitySettings.GetQualityLevel() >= QualitySettings.names.Length - 1 && QualitySettings.names.Length > 1,
                world != null && board != null && cardShader != null);
            var earned = DeskProgress.Earned(progress);
            var sig = $"{mode}|{string.Join(",", comics)}|{complete}|{earned.Count}|{WallTint.Fraction(definition, progress):0.00}";
            if (!force && sig == signature)
                return;
            signature = sig;
            Mode = mode;

            if (world != null)
                world.SetActive(mode == BackdropMode.Desk);
            if (cam != null)
            {
                // The flat wall is a canvas on its own layer that this camera draws, without the tone mapping the desk has (its colours are as made)
                cam.cullingMask = mode == BackdropMode.Desk ? ~0 : 1 << FlatLayer;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = mode == BackdropMode.Desk;
            }

            var tint = WallTint.For(WallTint.Fraction(definition, progress));
            var pairs = new List<KeyValuePair<string, int>>();
            foreach (var comic in comics)
                pairs.Add(new KeyValuePair<string, int>(comic, counts[comic]));
            Cards(WallLayout.Build(pairs, Free), counts, tint);

            if (mode == BackdropMode.Desk)
            {
                if (key != null)
                {
                    if (!keyKnown)
                    {
                        keyColour = key.color;
                        keyKnown = true;
                    }
                    key.color = Color.Lerp(keyColour, tint, 0.3f);
                }
                Props(earned);
                Trophy(complete);
                Screen(complete);
            }
            if (flatCanvas != null)
                flatCanvas.gameObject.SetActive(mode == BackdropMode.FlatWall);
        }

        // The monitor stands in front of the wall's lower left (as the viewer sees it); no card goes where it would hide one
        private static bool Free(Vector2 centre) => !(centre.x > -1.05f && centre.x < 0.15f && centre.y < -0.2f);

        private void Props(List<DeskProp> earned)
        {
            if (props == null)
                return;
            foreach (Transform child in props)
                child.gameObject.SetActive(earned.Exists(p => p.arena == child.name));
        }

        private static readonly int HeatGlow = Shader.PropertyToID("_HeatGlow"), MainTex = Shader.PropertyToID("_MainTex"), ScreenRect = Shader.PropertyToID("_ScreenRect");

        // Silver (second place's colour) until the campaign is complete, then the trophy's own gold
        private void Trophy(bool gold)
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in trophy)
            {
                if (renderer == null)
                    continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(HeatGlow, gold ? Vector4.zero : new Vector4(0.62f, 0.66f, 0.74f, 0.82f));
                renderer.SetPropertyBlock(block);
            }
        }

        private void Screen(bool complete)
        {
            if (screens.Length == 0)
                return;
            var picture = complete ? screenComplete : Shot == MenuShot.Monitor ? screenGame : screenCover;
            if (picture == null)
                return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in screens)
            {
                if (renderer == null)
                    continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(ScreenRect, screenRect);
                block.SetTexture(MainTex, picture);
                renderer.SetPropertyBlock(block);
            }
        }

        // ---------------------------------------------------------------- The camera

        private void Place(bool at)
        {
            var target = MenuShots.Pose(Shot);
            if (!posed || at)
            {
                pose = target;
                posed = true;
            }
            else
                pose = ShotPose.Lerp(pose, target, MenuShots.Follow(Time.unscaledDeltaTime));
            var drift = MenuShots.Drift(seconds);
            if (Mode == BackdropMode.Desk && cam != null)
            {
                var position = pose.position + drift;
                cam.transform.position = position;
                cam.transform.rotation = Quaternion.LookRotation(pose.target + drift * 0.4f - position);
                cam.fieldOfView = pose.fov;
            }
            if (Mode == BackdropMode.FlatWall && flatFront != null)
            {
                // The flat wall: the cards go slightly farther than the cork behind them; the shots become a zoom
                var zoom = Shot == MenuShot.Wide ? 1f : Shot == MenuShot.Monitor ? 1.12f : 1.22f;
                var smooth = Mathf.Lerp(flatFront.localScale.x, zoom, MenuShots.Follow(at ? 10 : Time.unscaledDeltaTime));
                flatFront.localScale = Vector3.one * smooth;
                flatBack.localScale = Vector3.one * (1 + (smooth - 1) * 0.5f);
                var sway = new Vector2(drift.x, drift.y * 3f) * FlatScale * 0.5f;
                flatFront.anchoredPosition = sway;
                flatBack.anchoredPosition = sway * 0.45f;
            }
        }

        // ---------------------------------------------------------------- The wall

        private sealed class Quads
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<Color> colours = new List<Color>();
            public readonly List<int> triangles = new List<int>();

            /// <summary>A rectangle of <paramref name="size"/> at <paramref name="offset"/> from the card's centre, the card turned <paramref name="tilt"/> degrees round it.</summary>
            public void Add(Vector2 centre, float tilt, Vector2 offset, Vector2 size, float z, Color colour, Rect uv)
            {
                var turn = Quaternion.Euler(0, 0, tilt);
                var first = vertices.Count;
                for (var i = 0; i < 4; i++)
                {
                    var corner = new Vector2(i == 0 || i == 3 ? -0.5f : 0.5f, i < 2 ? -0.5f : 0.5f);
                    var local = offset + Vector2.Scale(corner, size);
                    var at = (Vector3)(turn * local) + (Vector3)centre;
                    vertices.Add(new Vector3(at.x, at.y, z));
                    uvs.Add(new Vector2(uv.x + (corner.x + 0.5f) * uv.width, uv.y + (corner.y + 0.5f) * uv.height));
                    // Vertex colours are read as linear numbers by the shader
                    colours.Add(colour.linear);
                }
                // Corners 0 1 2 3 go round: (-,-) (+,-) (+,+) (-,+)
                triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh { name = "Wall Cards", hideFlags = HideFlags.HideAndDontSave };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colours);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static readonly Rect Whole = new Rect(0, 0, 1, 1);

        private void Cards(List<WallCard> list, Dictionary<string, int> counts, Color tint)
        {
            foreach (var o in made)
                if (o != null)
                    DestroyImmediateOrNot(o);
            made.Clear();
            cards = null;
            if (Mode == BackdropMode.Desk)
                Cards3D(list, counts, tint);
            else
                CardsFlat(list, counts, tint);
        }

        private static void DestroyImmediateOrNot(Object o)
        {
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }

        private Texture2D Atlas(string comic) => Resources.Load<Texture2D>(WallAtlas.Folder + "/" + comic);

        private void Cards3D(List<WallCard> list, Dictionary<string, int> counts, Color tint)
        {
            var paper = new Quads();
            var byComic = new Dictionary<string, Quads>();
            var corkTint = Color.Lerp(Cork, Cork * tint * 1.4f, 0.35f);
            // The cork, a little darker towards the bottom, and its wooden frame standing proud of it
            var half = new Vector2(BoardWidth, BoardHeight) / 2;
            paper.Add(Vector2.zero, 0, Vector2.zero, new Vector2(BoardWidth, BoardHeight), 0.03f, corkTint, Whole);
            foreach (var (offset, size) in new[]
            {
                (new Vector2(0, half.y + 0.05f), new Vector2(BoardWidth + 0.2f, 0.1f)), (new Vector2(0, -half.y - 0.05f), new Vector2(BoardWidth + 0.2f, 0.1f)),
                (new Vector2(-half.x - 0.05f, 0), new Vector2(0.1f, BoardHeight)), (new Vector2(half.x + 0.05f, 0), new Vector2(0.1f, BoardHeight)),
            })
                paper.Add(Vector2.zero, 0, offset, size, -0.015f, Wood, Whole);
            for (var i = 0; i < list.Count; i++)
            {
                var card = list[i];
                var z = -0.002f - i * 0.0002f;
                var centre = card.centre;
                // The shadow on the cork, the paper, the pin; then the picture, or the blank sheet's paler middle
                paper.Add(centre, card.tilt, new Vector2(0.008f, -0.011f), new Vector2(WallLayout.CardWidth, PaperHeight), z + 0.0012f, Shade, Whole);
                paper.Add(centre, card.tilt, Vector2.zero, new Vector2(WallLayout.CardWidth, PaperHeight), z, Paper, Whole);
                paper.Add(centre, card.tilt, new Vector2(0, PaperHeight / 2 - 0.014f), new Vector2(0.03f, 0.03f), z - 0.0006f, Pin, Whole);
                if (card.Blank)
                {
                    paper.Add(centre, card.tilt, new Vector2(0, -0.004f), new Vector2(PictureWidth, PictureHeight), z - 0.0003f, BlankPaper, Whole);
                    continue;
                }
                if (!byComic.TryGetValue(card.comic, out var quads))
                    byComic[card.comic] = quads = new Quads();
                quads.Add(centre, card.tilt, new Vector2(0, -0.004f), new Vector2(PictureWidth, PictureHeight), z - 0.0003f, Color.white, WallAtlas.Cell(card.panel, counts[card.comic]));
            }

            cards = new GameObject("Wall Cards") { hideFlags = HideFlags.HideAndDontSave };
            made.Add(cards);
            cards.transform.SetParent(board, false);
            Draw(cards.transform, "Paper", paper, Texture2D.whiteTexture, Color.white);
            foreach (var pair in byComic)
            {
                var atlas = Atlas(pair.Key);
                // Without its atlas a comic's panels are blank sheets of the picture's size
                Draw(cards.transform, pair.Key, pair.Value, atlas != null ? atlas : Texture2D.whiteTexture, atlas != null ? Color.white : BlankPaper);
            }
        }

        private void Draw(Transform parent, string name, Quads quads, Texture texture, Color tint)
        {
            var child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(parent, false);
            var mesh = quads.ToMesh();
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(cardShader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture(MainTex, texture);
            material.SetColor("_Tint", tint);
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            made.Add(mesh);
            made.Add(material);
        }

        // The same wall as flat UI: an overlay canvas under the GUI's, no 3D
        private void CardsFlat(List<WallCard> list, Dictionary<string, int> counts, Color tint)
        {
            if (flatCanvas == null)
            {
                var canvasObject = new GameObject("Menu Wall (flat)", typeof(Canvas), typeof(CanvasScaler)) { layer = FlatLayer };
                canvasObject.transform.SetParent(transform, false);
                flatCanvas = canvasObject.GetComponent<Canvas>();
                flatCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                flatCanvas.worldCamera = cam;
                flatCanvas.planeDistance = 1;
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                flatBack = Holder("Back", canvasObject.transform, new Vector2(3200, 1800));
                flatFront = Holder("Front", canvasObject.transform, new Vector2(3000, 1500));
            }
            Clear(flatBack);
            Clear(flatFront);
            flatCanvas.gameObject.SetActive(true);

            var corkTint = Color.Lerp(Cork, Cork * tint * 1.4f, 0.35f);
            Box(flatBack, "Cork", Vector2.zero, flatBack.sizeDelta, 0, corkTint);
            Box(flatBack, "Dark", Vector2.zero, flatBack.sizeDelta * 0.98f, 0, Color.Lerp(corkTint, CorkDark, 0.25f));
            for (var i = 0; i < list.Count; i++)
            {
                var card = list[i];
                var centre = card.centre * FlatScale;
                var paperSize = new Vector2(WallLayout.CardWidth, PaperHeight) * FlatScale;
                Box(flatFront, "Shadow", centre + new Vector2(6, -8), paperSize, card.tilt, Shade);
                Box(flatFront, "Paper", centre, paperSize, card.tilt, card.Blank ? BlankPaper : Paper);
                var atlas = card.Blank ? null : Atlas(card.comic);
                var pictureSize = new Vector2(PictureWidth, PictureHeight) * FlatScale;
                var pictureOffset = new Vector2(0, -0.004f * FlatScale);
                if (atlas != null && counts.TryGetValue(card.comic, out var count))
                {
                    var rect = Box(flatFront, "Panel", centre, pictureSize, card.tilt, Color.white, pictureOffset);
                    var at = rect.gameObject;
                    DestroyImmediate(rect);
                    var raw = at.AddComponent<RawImage>();
                    raw.texture = atlas;
                    raw.uvRect = WallAtlas.Cell(card.panel, count);
                    raw.raycastTarget = false;
                }
                else
                    Box(flatFront, "Sheet", centre, pictureSize, card.tilt, BlankPaper, pictureOffset);
                Box(flatFront, "Pin", centre, new Vector2(21, 21), card.tilt, Pin, new Vector2(0, (PaperHeight / 2 - 0.014f) * FlatScale));
            }
            Place(true);
        }

        private static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                DestroyImmediateOrNot(parent.GetChild(i).gameObject);
        }

        private RectTransform Holder(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)) { layer = FlatLayer }.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        private static Image Box(RectTransform parent, string name, Vector2 centre, Vector2 size, float tilt, Color colour, Vector2? offset = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)) { layer = FlatLayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = centre + (Vector2)(Quaternion.Euler(0, 0, tilt) * (Vector3)(offset ?? Vector2.zero));
            rect.localRotation = Quaternion.Euler(0, 0, tilt);
            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }
    }
}
