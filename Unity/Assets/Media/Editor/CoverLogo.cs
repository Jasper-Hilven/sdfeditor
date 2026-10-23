using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SdfMedia.Editor
{
    /// <summary>
    /// The game's real logo for a cover: the Logo object of the title screen prefab (Assets/GUI/Prefabs/Screens/Title.prefab: the spotted
    /// cap, MINIGUN and MUSHROOMS on arches and the Second Place ribbon, but not the tagline plate under it) taken out of that prefab, with everything else of the screen (the
    /// buttons, the progress line, the version, the dim) left behind, sized and placed per format, and rendered on its own. It sits on a
    /// world-space canvas of the cover's own size (one canvas unit a pixel) far below the stage, drawn by an orthographic camera that sees
    /// only the UI layer, at <see cref="CoverCapture.Super"/> times the size, once over black and once over white; the two give the logo's
    /// colour and its transparency (the way GuiSnapshot gets a GUI over the world), and <see cref="CoverCapture"/> lays it over the scene.
    /// The title's own behaviours never run: the prefab is unpacked while its parent is switched off, and only the logo is moved on to
    /// the canvas.
    /// </summary>
    internal static class CoverLogo
    {
        /// <summary>The UI layer, which the logo's objects are on and only its camera sees.</summary>
        public const int UiLayer = 5;
        public const string TitlePrefab = "Assets/GUI/Prefabs/Screens/Title.prefab";
        /// <summary>Where the canvas hangs, far from anything else in the scenes.</summary>
        private static readonly Vector3 CanvasPosition = new Vector3(0, -5000, 0);

        /// <summary>The logo of a format at the format's size, over black and over white, rows from the bottom.</summary>
        public static void Draw(Scene scene, CoverFormat format, out Color32[] overBlack, out Color32[] overWhite)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TitlePrefab);
            if (prefab == null)
                throw new System.InvalidOperationException($"{TitlePrefab} is missing (Tools > SDF GUI builds the screens)");

            var canvasObject = new GameObject("Cover Logo Canvas", typeof(RectTransform), typeof(Canvas));
            var holder = new GameObject("Cover Logo Holder");
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Cover Logo Camera", HideFlags.HideAndDontSave, typeof(Camera));
            RenderTexture target = null;
            try
            {
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                SceneManager.MoveGameObjectToScene(holder, scene);
                canvasObject.layer = UiLayer;
                canvasObject.transform.position = CanvasPosition;
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                // The shape and text shaders read these
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
                var canvasRect = (RectTransform)canvasObject.transform;
                canvasRect.sizeDelta = new Vector2(format.Width, format.Height);
                canvasRect.localScale = Vector3.one;

                // The title screen, unpacked under a parent that is switched off (so none of its behaviours runs), then only the logo moved out
                holder.SetActive(false);
                var title = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
                PrefabUtility.UnpackPrefabInstance(title, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var logo = title.transform.Find("Logo") as RectTransform;
                if (logo == null)
                    throw new System.InvalidOperationException($"{TitlePrefab} has no Logo (GuiScreens.Title builds it)");
                var design = logo.sizeDelta;
                logo.SetParent(canvasRect, false);
                Object.DestroyImmediate(holder);

                // The campaign's gold crown is the title's business, and the ribbon is optional
                var crown = logo.Find("Crown");
                if (crown != null)
                    crown.gameObject.SetActive(false);
                // The tagline plate ("Last year's champion. Probably still.") is text other than the title: CrazyGames allows none
                var tagline = logo.Find("Tagline Plate");
                if (tagline != null)
                    tagline.gameObject.SetActive(false);
                var subtitle = logo.Find("Subtitle");
                if (subtitle != null)
                    subtitle.gameObject.SetActive(CoverCapture.ShowSubtitle);

                // Centred where the format says, as wide as it says
                logo.anchorMin = logo.anchorMax = logo.pivot = new Vector2(0.5f, 0.5f);
                logo.sizeDelta = design;
                logo.anchoredPosition = new Vector2((format.LogoCentre.x - 0.5f) * format.Width, (0.5f - format.LogoCentre.y) * format.Height);
                logo.localScale = Vector3.one * (format.Width * format.LogoShare / design.x);
                foreach (var child in canvasObject.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = UiLayer;

                // The shapes and the lettering build their meshes now
                Canvas.ForceUpdateCanvases();
                foreach (var text in canvasObject.GetComponentsInChildren<TMP_Text>(true))
                    text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();

                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = format.Height / 2f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 50;
                camera.cullingMask = 1 << UiLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.transform.SetPositionAndRotation(CanvasPosition + new Vector3(0, 0, -10), Quaternion.identity);
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;

                target = new RenderTexture(format.Width * CoverCapture.Super, format.Height * CoverCapture.Super, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
                camera.aspect = format.Aspect;
                camera.backgroundColor = Color.black;
                camera.Render();
                overBlack = CoverCapture.Downscale(target, format.Width, format.Height);
                camera.backgroundColor = Color.white;
                camera.Render();
                overWhite = CoverCapture.Downscale(target, format.Width, format.Height);
                camera.targetTexture = null;
            }
            finally
            {
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                Object.DestroyImmediate(cameraObject);
                if (holder != null)
                    Object.DestroyImmediate(holder);
                Object.DestroyImmediate(canvasObject);
            }
        }
    }
}
