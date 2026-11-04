using SdfBattle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The touch controls on screen (ticket 56): the floating joystick, the fire, jump and zoom buttons, the four weapon slots, the three
    /// powers, the scoreboard and the pause. A screen over the HUD (<see cref="GuiScreenRoot.touchLayer"/>), on a touch device only.
    /// What the fingers do is <see cref="TouchControls"/>'s; this draws, and mirrors the HUD's own pieces (the weapon slots' colours and
    /// icons, the power cards' showing when the rage is full) so there is one truth. On a touch device it also moves the HUD's pieces
    /// that the thumbs cover: the weapon panel's slots are the layer's buttons, the frame rate goes under the top-left buttons.
    /// </summary>
    public sealed class TouchLayer : GuiView
    {
        public TouchButton[] buttons = new TouchButton[0];
        [Tooltip("The joystick: its ring (the base) and its knob, shown while a thumb is on the left half")]
        public RectTransform stickBase;
        public RectTransform stickKnob;
        [Tooltip("Canvas units from the middle of the base to its rim")]
        public float stickRadius = 192;

        private GuiHud hud;
        private bool laidOut;
        private Vector2 fpsHome;
        private Vector2 weaponsHome;

        /// <summary>Where the editor's preview of the joystick sits when nothing plays, and its knob's push.</summary>
        public static readonly Vector2 PreviewStick = new Vector2(330, 330), PreviewPush = new Vector2(70, 60);

        /// <summary>Radius of the joystick in screen pixels.</summary>
        public float StickPixels => stickRadius * transform.lossyScale.x;

        public TouchButton ButtonAt(Vector2 screenPoint)
        {
            for (var i = buttons.Length - 1; i >= 0; i--)
            {
                var button = buttons[i];
                if (button != null && button.isActiveAndEnabled && button.Contains(screenPoint))
                    return button;
            }
            return null;
        }

        public TouchButton Find(TouchButtonKind kind, int index = 0)
        {
            foreach (var button in buttons)
                if (button != null && button.kind == kind && button.index == index)
                    return button;
            return null;
        }

        /// <summary>Shows the joystick with its base at one screen point and its knob at another, or hides it.</summary>
        public void Stick(bool shown, Vector2 basePoint = default, Vector2 knobPoint = default)
        {
            GuiText.Show(stickBase, shown);
            if (!shown || stickBase == null)
                return;
            stickBase.position = basePoint;
            if (stickKnob != null)
                stickKnob.position = knobPoint;
        }

        protected override void OnEnable()
        {
            LayOutHud();
            base.OnEnable();
        }

        private void OnDisable() => RestoreHud();

        private GuiHud Hud
        {
            get
            {
                if (hud == null)
                {
                    var root = GetComponentInParent<Canvas>(true);
                    hud = root != null ? root.GetComponentInChildren<GuiHud>(true) : null;
                }
                return hud;
            }
        }

        // The HUD's weapon boxes are the layer's slot buttons now (it mirrors them), so the panel keeps only the weapon's name, over them;
        // the power cards are the layer's power buttons; the frame rate and hint go under the top-left buttons
        private void LayOutHud()
        {
            var h = Hud;
            if (h == null || laidOut)
                return;
            laidOut = true;
            foreach (var slot in h.weaponSlots)
                if (slot != null)
                    slot.gameObject.SetActive(false);
            if (h.weaponLabel != null && h.weaponLabel.transform.parent is RectTransform panel)
            {
                weaponsHome = panel.anchoredPosition;
                var shape = panel.GetComponent<ToonShape>();
                if (shape != null)
                    shape.enabled = false;
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0);
                panel.pivot = new Vector2(0.5f, 0);
                panel.anchoredPosition = new Vector2(0, 170);
                panel.sizeDelta = new Vector2(430, 50);
            }
            foreach (var card in h.sporeCards)
            {
                if (card == null)
                    continue;
                var group = card.GetComponent<CanvasGroup>();
                if (group == null)
                    group = card.AddComponent<CanvasGroup>();
                group.alpha = 0;
            }
            if (h.fps != null)
            {
                fpsHome = h.fps.rectTransform.anchoredPosition;
                h.fps.rectTransform.anchoredPosition = new Vector2(10, -170);
            }
        }

        private void RestoreHud()
        {
            var h = hud;
            if (h == null || !laidOut)
                return;
            laidOut = false;
            foreach (var slot in h.weaponSlots)
                if (slot != null)
                    slot.gameObject.SetActive(true);
            if (h.weaponLabel != null && h.weaponLabel.transform.parent is RectTransform panel)
            {
                var shape = panel.GetComponent<ToonShape>();
                if (shape != null)
                    shape.enabled = true;
                panel.anchorMin = panel.anchorMax = Vector2.right;
                panel.pivot = Vector2.right;
                panel.anchoredPosition = weaponsHome;
                panel.sizeDelta = new Vector2(430, 164);
            }
            foreach (var card in h.sporeCards)
            {
                var group = card != null ? card.GetComponent<CanvasGroup>() : null;
                if (group != null)
                    group.alpha = 1;
            }
            if (h.fps != null)
                h.fps.rectTransform.anchoredPosition = fpsHome;
        }

        protected override void Render(MatchView m)
        {
            var h = Hud;
            if (h == null)
                return;
            var playing = Application.isPlaying;
            // The weapon in hand, for the zoom button
            var zoomable = !playing;
            if (playing && Combat.Current != null && Combat.Current.Match != null)
            {
                var arsenal = Combat.Current.ArsenalOf(Combat.Current.Match.You);
                zoomable = arsenal != null && arsenal.Weapon != null && arsenal.Weapon.zoomFieldOfView > 0;
            }
            foreach (var button in buttons)
            {
                if (button == null)
                    continue;
                switch (button.kind)
                {
                    case TouchButtonKind.Slot:
                        Mirror(button, h);
                        break;
                    case TouchButtonKind.Power:
                        // Shown when the rage meter is full, as the key hints are; all three when nothing plays (the editor's preview)
                        var card = button.index < h.sporeCards.Length ? h.sporeCards[button.index] : null;
                        GuiText.Show(button, !playing || (card != null && card.activeSelf));
                        break;
                    case TouchButtonKind.Zoom:
                        GuiText.Show(button, zoomable);
                        break;
                }
            }
            // Nothing plays (the editor, the snapshots): the joystick as it looks with a thumb a little up and right of its middle
            if (!playing && stickBase != null)
            {
                GuiText.Show(stickBase, true);
                stickBase.anchoredPosition = PreviewStick;
                if (stickKnob != null)
                    stickKnob.anchoredPosition = PreviewPush;
            }
        }

        // A slot button looks as the HUD's slot box does: gold for the weapon in hand, paper for a held one, dim when empty
        private static void Mirror(TouchButton button, GuiHud h)
        {
            var i = button.index;
            if (i >= h.weaponSlots.Length || h.weaponSlots[i] == null)
                return;
            var box = h.weaponSlots[i];
            if (button.shape != null)
                button.shape.SetColor(box.color);
            if (button.icon != null && i < h.weaponIcons.Length && h.weaponIcons[i] != null)
            {
                if (button.icon.sprite != h.weaponIcons[i].sprite)
                    button.icon.sprite = h.weaponIcons[i].sprite;
                button.icon.enabled = h.weaponIcons[i].enabled;
            }
            if (button.label != null && i < h.weaponNumbers.Length && h.weaponNumbers[i] != null)
            {
                GuiText.Set(button.label, h.weaponNumbers[i].text);
                button.label.color = h.weaponNumbers[i].color;
            }
        }
    }
}
