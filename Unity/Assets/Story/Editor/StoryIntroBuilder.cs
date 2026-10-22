using System.IO;
using SdfGui;
using SdfGui.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static SdfGui.Editor.GuiFactory;

namespace SdfStory.Editor
{
    /// <summary>
    /// Builds the intro viewer (ticket 31.8) as a prefab, <c>Assets/Story/Resources/StoryIntro.prefab</c>, the one <see cref="StoryIntro.Begin"/>
    /// loads: a canvas with the panel in a 16:9 frame over an ink backdrop, and the GUI's own button prefab for Next (bottom right) and
    /// Skip (top right), 5 canvas units from the screen edges. The panels (<c>Assets/Story/Panels/panel-NN.jpg</c>) and the music
    /// (<c>Assets/Story/Audio/intro-music.ogg</c>, provided by the user; run this again after adding it) are references of the prefab,
    /// which is in Resources so that they are not loaded until the intro starts. Tools > SDF Story > Build Intro Viewer, or in batch:
    /// <c>-executeMethod SdfStory.Editor.StoryIntroBuilder.BuildInBatch</c>.
    /// </summary>
    public static class StoryIntroBuilder
    {
        public const string PrefabPath = "Assets/Story/Resources/StoryIntro.prefab";
        public const string PanelFolder = "Assets/Story/Panels";
        public const string MusicPath = "Assets/Story/Audio/intro-music.ogg";
        public const int PanelCount = 12;

        [MenuItem("Tools/SDF Story/Build Intro Viewer")]
        public static void Build()
        {
            GuiAssets.EnsureTmpEssentials();
            var kit = GuiAssets.Load();
            var f = new GuiFactory(kit);
            var buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{GuiPrefabs.ComponentFolder}/Button.prefab");
            if (buttonPrefab == null)
            {
                Debug.LogError("StoryIntroBuilder: the GUI's Button prefab is missing; run Tools > SDF GUI > Rebuild Prefabs and Scene first");
                return;
            }

            var root = Object("Story Intro", null);
            var intro = root.AddComponent<StoryIntro>();
            intro.source = root.AddComponent<AudioSource>();
            intro.source.playOnAwake = false;
            intro.source.loop = true;
            intro.source.spatialBlend = 0;

            var canvasObject = Object("Canvas", root.transform);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over every other canvas, the GUI's included
            canvas.sortingOrder = 1000;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            Stretch(canvasObject.transform);
            intro.canvasRoot = canvasObject;

            // The bars: the ink of the toon outline, and a click anywhere on the screen is Next
            var backdrop = Object("Backdrop", canvasObject.transform).AddComponent<Image>();
            backdrop.color = GuiPalette.Ink;
            Stretch(backdrop);
            intro.panelClick = backdrop.gameObject.AddComponent<Button>();
            intro.panelClick.transition = Selectable.Transition.None;
            intro.panelClick.targetGraphic = backdrop;
            intro.panelClick.navigation = new Navigation { mode = Navigation.Mode.None };

            var frame = Object("Frame", canvasObject.transform);
            Stretch(frame.transform);
            intro.frame = (RectTransform)frame.transform;
            intro.current = PanelImage(frame.transform, "Current", 1);
            intro.incoming = PanelImage(frame.transform, "Incoming", 0);

            var next = ButtonOf(buttonPrefab, canvasObject.transform, "Next Button", "NEXT", GuiPalette.Moss, BottomRight, new Vector2(-5, 5),
                new Vector2(260, 80), 40, 0.4f);
            intro.next = next;
            intro.nextLabel = next.GetComponentInChildren<TMP_Text>();
            intro.skip = ButtonOf(buttonPrefab, canvasObject.transform, "Skip Button", "SKIP", GuiPalette.Dusk, TopRight, new Vector2(-5, -5),
                new Vector2(200, 76), 36, 0);

            intro.panels = new Texture2D[PanelCount];
            for (var i = 0; i < PanelCount; i++)
            {
                var path = $"{PanelFolder}/panel-{i + 1:00}.jpg";
                intro.panels[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (intro.panels[i] == null)
                    Debug.LogWarning($"StoryIntroBuilder: {path} is missing; the viewer will skip that panel");
            }
            intro.music = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            if (intro.music == null)
                Debug.LogWarning($"StoryIntroBuilder: {MusicPath} does not exist yet (the user provides it); the intro will play without music until this is run again");

            Save(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"StoryIntroBuilder: built {PrefabPath}");
        }

        public static void BuildInBatch() => Build();

        private static RawImage PanelImage(Transform parent, string name, float alpha)
        {
            var image = Object(name, parent).AddComponent<RawImage>();
            image.color = new Color(1, 1, 1, alpha);
            image.raycastTarget = false;
            Stretch(image);
            return image;
        }

        /// <summary>The GUI's button, as the screens use it, but free of its <see cref="GuiCommand"/>, which would act on a GUI in the scene.</summary>
        private static Button ButtonOf(GameObject prefab, Transform parent, string name, string text, Color color, Vector2 anchor, Vector2 position,
            Vector2 size, float fontSize, float spots)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = name;
            UnityEngine.Object.DestroyImmediate(instance.GetComponent<GuiCommand>());
            Place(instance.transform, anchor, position, size);
            var shape = instance.GetComponent<ToonShape>();
            shape.color = color;
            shape.Spots = spots;
            if (size.y < 60)
            {
                shape.CornerRadius = 12;
                shape.Outline = 3.5f;
                shape.Shadow = 4;
            }
            var label = instance.GetComponentInChildren<TMP_Text>();
            label.text = text;
            label.fontSize = fontSize;
            var button = instance.GetComponent<Button>();
            // Keys are the viewer's own: Space and Enter must not also press a selected button
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }
    }
}
