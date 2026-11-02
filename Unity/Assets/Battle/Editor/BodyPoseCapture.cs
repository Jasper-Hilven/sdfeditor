using System;
using System.IO;
using SdfBody;
using SdfMushrooms.Editor;
using SdfRig;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfBattle.Editor
{
    /// <summary>
    /// Renders the soldier in some poses of its Animator for a look (ticket 44): <c>body-poses.png</c>, red above blue, each in idle, run,
    /// run with the heavy weapon's clips, the burst of fire (upper body) and the jump's fall. The pose is the real controller driven by
    /// the parameters <see cref="SoldierAnimator"/> writes, stepped to settle. Batch, with the GPU:
    /// <c>tools/unity-batch.sh body-capture -executeMethod SdfBattle.Editor.BodyPoseCapture.CaptureInBatch -captureDir &lt;folder&gt;</c>.
    /// </summary>
    public static class BodyPoseCapture
    {
        const int Width = 400, Height = 600;

        static readonly (string Name, Action<Animator> Set)[] Poses =
        {
            ("idle", a => { }),
            ("run", a => { a.SetFloat("MoveY", 1); a.SetFloat("Speed", 1); }),
            ("run heavy", a => { a.SetFloat("MoveY", 1); a.SetFloat("Speed", 1); a.SetFloat("Stance", 2); }),
            ("fire", a => { a.SetBool("FireAuto", true); }),
            ("jump fall", a => { a.SetTrigger("Jump"); a.SetBool("Airborne", true); }),
        };

        [MenuItem("Sdf/Body/Capture Poses")]
        public static void CaptureFromMenu() => Capture(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "BodyPoses"));

        public static void CaptureInBatch()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-captureDir");
            Capture(i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "body-poses"));
        }

        public static void Capture(string directory)
        {
            Directory.CreateDirectory(directory);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Battle/Body/Animator/Soldier.controller");
            if (controller == null || SoldierModels.Load() == null)
            {
                Debug.LogError("BodyPoseCapture: no controller or no soldier models; run Sdf/Body/Build All and the skin build first");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MushroomScene.BuildLighting(scene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(8, 0.1f, 4);
            floor.transform.position = new Vector3(0, -0.05f, 0);
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.55f, 0.6f, 0.5f) };
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Body Camera", HideFlags.HideAndDontSave, typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.72f, 0.82f, 0.92f);
            camera.fieldOfView = 30;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50;
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var tiles = new Texture2D[Poses.Length * 2];
                var slot = 0;
                foreach (var skin in SoldierModels.SkinKeys)
                {
                    foreach (var (name, set) in Poses)
                    {
                        var soldier = (GameObject)PrefabUtility.InstantiatePrefab(SoldierModels.Prefab(skin));
                        var animator = soldier.GetComponent<Animator>();
                        animator.runtimeAnimatorController = controller;
                        animator.applyRootMotion = false;
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        set(animator);
                        animator.Rebind();
                        set(animator);
                        for (var i = 0; i < 40; i++)
                            animator.Update(1f / 30);
                        // A three-quarter view from the front right of the soldier
                        tiles[slot++] = Shoot(camera, new Vector3(0, 0.95f, 0), new Vector3(-0.55f, 0.1f, 1), 4.6f);
                        UnityEngine.Object.DestroyImmediate(soldier);
                    }
                }
                var atlas = new Texture2D(Poses.Length * Width, 2 * Height, TextureFormat.RGB24, false);
                for (var i = 0; i < tiles.Length; i++)
                {
                    atlas.SetPixels(i % Poses.Length * Width, (1 - i / Poses.Length) * Height, Width, Height, tiles[i].GetPixels());
                    UnityEngine.Object.DestroyImmediate(tiles[i]);
                }
                atlas.Apply();
                File.WriteAllBytes(Path.Combine(directory, "body-poses.png"), atlas.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(atlas);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            Debug.Log($"BodyPoseCapture: written to {directory}");
        }

        static Texture2D Shoot(Camera camera, Vector3 target, Vector3 direction, float distance)
        {
            camera.transform.position = target + direction.normalized * distance;
            camera.transform.LookAt(target);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            camera.targetTexture = rt;
            camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return image;
        }
    }
}
