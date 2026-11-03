using System.Collections.Generic;
using SdfGui;
using SdfRig;
using TMPro;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Every other player's name over their head, in their team's colour, on the match's HUD. Teammates' names always show (so you
    /// know where your team is); an enemy's only while you can see their head (the level does not hide it; players' capsules do not
    /// count). Tags shrink and fade with distance and are not drawn for the dead or past <see cref="maxDistance"/>. The labels are
    /// copies of the HUD's flag marker text (its font), with a black border round the letters to read against the level, behind the rest of the HUD, so no HUD prefab changes.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class NameTags : MonoBehaviour
    {
        [Tooltip("Names further than this from the camera are not drawn, in metres")]
        public float maxDistance = 70;
        [Tooltip("The name's font size close up and at the furthest distance")]
        public float nearSize = 26, farSize = 15;
        [Tooltip("The black border round the letters, as a share of the font's SDF range (0 to 1)")]
        public float outline = 0.32f;
        [Tooltip("The name sits this far over the top of the soldier's head, in the body's metres")]
        public float overHead = 0.55f;

        private Match match;
        private RectTransform root;
        private RectTransform layer;
        private Canvas canvas;
        private TMP_Text template;
        // The label's font material with a thick black outline round the letters, shared by every tag
        private Material outlined;
        private Camera view;
        private readonly List<TMP_Text> tags = new List<TMP_Text>();
        private readonly List<string> shownNames = new List<string>();
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>Adds the name tags to the match's HUD; null when the GUI has no HUD screen with a flag marker label to copy.</summary>
        public static NameTags Create(Match match)
        {
            var hud = match.Hud != null && match.Hud.view != null ? match.Hud.view.GetComponentInChildren<GuiHud>(true) : null;
            if (hud == null || hud.flagMarkerLabels.Length == 0 || hud.flagMarkerLabels[0] == null)
                return null;
            var tags = hud.gameObject.AddComponent<NameTags>();
            tags.match = match;
            tags.root = (RectTransform)hud.transform;
            tags.canvas = hud.GetComponentInParent<Canvas>();
            tags.template = hud.flagMarkerLabels[0];
            var layer = new GameObject("Name Tags", typeof(RectTransform)).GetComponent<RectTransform>();
            layer.SetParent(tags.root, false);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            // Behind the rest of the HUD: the crosshair, panels and banners draw over the names
            layer.SetAsFirstSibling();
            tags.layer = layer;
            return tags;
        }

        private void OnDestroy()
        {
            // The campaign's GUI outlives the match; its tags go with it
            if (layer != null)
                Destroy(layer.gameObject);
            if (outlined != null)
                Destroy(outlined);
        }

        private TMP_Text Tag(int i)
        {
            while (tags.Count <= i)
            {
                var text = Instantiate(template, layer);
                text.name = "Name";
                if (outlined == null)
                {
                    outlined = new Material(template.fontSharedMaterial) { name = "Name Tag Outline" };
                    outlined.EnableKeyword(ShaderUtilities.Keyword_Outline);
                    outlined.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
                    outlined.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
                    outlined.SetFloat(ShaderUtilities.ID_FaceDilate, outline * 0.6f);
                }
                text.fontSharedMaterial = outlined;
                text.raycastTarget = false;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.alignment = TextAlignmentOptions.Bottom;
                var rect = text.rectTransform;
                rect.sizeDelta = new Vector2(300, 40);
                rect.pivot = new Vector2(0.5f, 0);
                rect.anchorMin = rect.anchorMax = root.pivot;
                text.gameObject.SetActive(false);
                tags.Add(text);
                shownNames.Add(null);
            }
            return tags[i];
        }

        private void LateUpdate()
        {
            if (match == null || match.State == null)
                return;
            var you = match.You;
            if (view == null || !view.isActiveAndEnabled)
                view = you != null ? you.GetComponentInChildren<Camera>() : null;
            if (view == null)
                view = Camera.main;
            var camera = view;
            var yourTeam = you != null ? you.Team : match.yourTeam;
            var uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            var used = 0;
            if (camera != null)
                foreach (var player in match.Players)
                {
                    if (player == null || player == you || player.State == null || !player.State.Alive || player.State.Benched)
                        continue;
                    var body = player.BodyRoot != null ? player.BodyRoot : player.transform;
                    var head = player.WornHead;
                    var centre = body.TransformPoint(Vector3.up * (head != null ? head.CentreHeight : SoldierBody.HeadTopHeight - 0.12f));
                    var top = body.TransformPoint(Vector3.up * (SoldierBody.HeadTopHeight + overHead));
                    var eye = camera.transform.position;
                    var distance = Vector3.Distance(eye, top);
                    if (distance > maxDistance)
                        continue;
                    var screen = camera.WorldToScreenPoint(top);
                    if (screen.z <= camera.nearClipPlane || screen.x < -100 || screen.x > Screen.width + 100 || screen.y < -50 || screen.y > Screen.height + 50)
                        continue;
                    var team = player.Team;
                    if (team != yourTeam && Blocked(eye, centre, player))
                        continue;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, uiCamera, out var local))
                        continue;

                    var text = Tag(used++);
                    if (!text.gameObject.activeSelf)
                        text.gameObject.SetActive(true);
                    var name = player.State.Name;
                    if (shownNames[used - 1] != name)
                    {
                        shownNames[used - 1] = name;
                        GuiText.Set(text, name);
                    }
                    var near = Mathf.InverseLerp(maxDistance, 5, distance);
                    text.fontSize = Mathf.Lerp(farSize, nearSize, near);
                    var colour = FlagHud.Colour(team);
                    colour.a = Mathf.Lerp(0.35f, 1, Mathf.Clamp01(near * 1.5f));
                    text.color = colour;
                    text.rectTransform.anchoredPosition = local;
                }
            for (var i = used; i < tags.Count; i++)
                if (tags[i].gameObject.activeSelf)
                    tags[i].gameObject.SetActive(false);
        }

        /// <summary>Whether the level stands between the camera and <paramref name="target"/>'s head (players' own capsules do not count).</summary>
        private static bool Blocked(Vector3 eye, Vector3 target, Player player)
        {
            var direction = target - eye;
            var length = direction.magnitude;
            if (length < 1e-3f)
                return false;
            var count = Physics.RaycastNonAlloc(eye, direction / length, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (Player.Of(Hits[i].collider) == null)
                    return true;
            return false;
        }
    }
}
