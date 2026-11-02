using SdfHeads;
using SdfRig;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// A 3D preview of a monster head on your team's soldier (ticket 47), turning slowly, drawn by its own camera into a render texture that this
    /// RawImage shows. The stage is built far from the level when the preview is enabled and removed when it is disabled.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class GuiHeadPreview : MonoBehaviour
    {
        public const float DegreesPerSecond = 28f;
        static readonly Vector3 StageOrigin = new Vector3(0, 3000, 0);

        public Vector2Int resolution = new Vector2Int(480, 600);

        GameObject stage, body;
        Camera stageCamera;
        RenderTexture texture;
        string id;
        string skin = SoldierModels.RedSkin;

        /// <summary>The head being shown (the id as in the table; a head that is not modelled shows as its stand-in).</summary>
        public string Id => id;

        /// <summary>What is worn on the stage now: the id, or its stand-in; null when nothing could be shown.</summary>
        public string Worn { get; private set; }

        public void Show(string headId, string teamSkin)
        {
            id = headId;
            skin = teamSkin;
            Rebuild();
        }

        /// <summary>Draws the stage now (the camera otherwise draws with the game's frames).</summary>
        public void RenderNow()
        {
            if (stageCamera != null)
                stageCamera.Render();
        }

        void OnEnable() => Rebuild();

        void OnDisable() => Teardown();

        void Update()
        {
            if (body != null)
                body.transform.Rotate(0, DegreesPerSecond * Time.unscaledDeltaTime, 0, Space.World);
        }

        void Rebuild()
        {
            Teardown();
            if (!Application.isPlaying || string.IsNullOrEmpty(id))
                return;
            var prefab = SoldierModels.Prefab(skin);
            if (prefab == null)
                return;
            stage = new GameObject("Head Preview Stage") { hideFlags = HideFlags.HideAndDontSave };
            stage.transform.position = StageOrigin;
            body = Instantiate(prefab, stage.transform, false);
            body.transform.localPosition = Vector3.zero;
            // Colliders are not wanted; the T-pose is fine for a head and shoulders
            foreach (var collider in body.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            var worn = HeadWear.Attach(body.transform, id) ?? HeadWear.Attach(body.transform, HeadStandIn.Of(id));
            Worn = worn != null ? worn.Id : null;

            texture = new RenderTexture(resolution.x, resolution.y, 24, RenderTextureFormat.ARGB32) { name = "Head Preview" };
            var cameraObject = new GameObject("Head Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(stage.transform, false);
            stageCamera = cameraObject.AddComponent<Camera>();
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = new Color(0, 0, 0, 0);
            stageCamera.fieldOfView = 30;
            stageCamera.nearClipPlane = 0.1f;
            stageCamera.farClipPlane = 20;
            stageCamera.targetTexture = texture;
            stageCamera.allowHDR = false;
            stageCamera.allowMSAA = true;
            var eye = new Vector3(0, 1.72f, 2.4f);
            cameraObject.transform.localPosition = eye;
            cameraObject.transform.LookAt(stage.transform.position + new Vector3(0, 1.66f, 0));
            GetComponent<RawImage>().texture = texture;
        }

        void Teardown()
        {
            if (stage != null)
                Destroy(stage);
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
            stage = body = null;
            stageCamera = null;
            texture = null;
            Worn = null;
        }
    }
}
