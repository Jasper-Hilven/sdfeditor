using System;
using System.Collections.Generic;
using SdfBattle;
using SdfLevel;
using PlayerInput = SdfLevel.PlayerInput;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace SdfGui
{
    /// <summary>One finger on the screen: an id that stays the same until it lifts, and where it is in pixels.</summary>
    public struct TouchSample
    {
        public int id;
        public Vector2 position;
    }

    /// <summary>
    /// The touch controls' brain (ticket 56): on a touch device it is a <see cref="PlayerInputSource"/> of <see cref="PlayerInput"/>, fed by
    /// the fingers on the screen and the <see cref="TouchLayer"/>'s buttons. Each finger belongs to the control it started on until it lifts
    /// (Enhanced Touch, not the mouse emulation):
    /// <list type="bullet">
    /// <item>on a button: that button (fire, jump, a weapon slot, a power, zoom, scoreboard, pause); a drag that starts on a fire button also looks;</item>
    /// <item>else on the left half: the floating joystick, which appears where the finger landed and follows it past its rim;</item>
    /// <item>else on the right half: the look.</item>
    /// </list>
    /// Auto-fire (Settings) holds the trigger while the crosshair is on an enemy in range, and the aim assist slows the look near an
    /// enemy, both through <see cref="TouchAssist"/> and so <c>Combat</c>'s own trace. Lives on the GUI's root, which outlives the matches.
    /// A check drives it with <see cref="Feed"/> in place of the touchscreen.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class TouchControls : MonoBehaviour
    {
        /// <summary>The look the controller multiplies by at rest (<see cref="SdfLevel.FirstPersonController.lookSensitivity"/>'s default); the touch look is divided by it so a swipe turns what <see cref="TouchRules"/> says.</summary>
        public const float ControllerSensitivity = 2;

        /// <summary>The fingers on the screen, set by a check in place of the touchscreen (null: the real one).</summary>
        public static Func<IReadOnlyList<TouchSample>> Feed;

        public static TouchControls Current { get; private set; }

        private enum Role { Button, Stick, Look }

        private sealed class Finger
        {
            public Role role;
            public Vector2 last;
            public TouchButton button;
        }

        private readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        private readonly List<TouchSample> samples = new List<TouchSample>();
        private readonly List<int> gone = new List<int>();
        private readonly Source source;
        private TouchLayer layer;
        private GuiFlow flow;
        private bool registered, enhanced;

        // The state the source answers from
        private Vector2 move, look;
        private bool jump, pause, zoomOn, scoreboard, skip;
        private int fire;
        private readonly bool[] slots = new bool[SdfBattle.Inventory.SlotCount];
        private readonly bool[] powers = new bool[3];
        private int stickFinger = -1;
        private Vector2 stickBase;
        private readonly TouchRules.AutoFire autoFire = new TouchRules.AutoFire();
        private float lookScale = 1;

        public TouchControls() => source = new Source(this);

        /// <summary>The joystick's push, -1 to 1 each way.</summary>
        public Vector2 Move => move;

        /// <summary>The auto-fire has the trigger (a check reads it).</summary>
        public bool AutoFiring => autoFire.Firing;

        /// <summary>The share of the look the aim assist leaves (1: none).</summary>
        public float LookScale => lookScale;

        public bool ZoomedIn => zoomOn;

        public TouchLayer Layer => layer != null ? layer : (layer = GetComponentInChildren<TouchLayer>(true));

        /// <summary>The fingers' state as the touch layer's buttons know it: whether the button of this kind is shown (a check asks where to put a finger).</summary>
        public TouchButton Button(TouchButtonKind kind, int index = 0) => Layer != null ? Layer.Find(kind, index) : null;

        /// <summary>The screen point at the middle of a button (where a finger would press it).</summary>
        public static Vector2 CentreOf(TouchButton button)
        {
            var corners = new Vector3[4];
            button.Rect.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        private void OnEnable()
        {
            Current = this;
            flow = GetComponent<GuiFlow>();
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
            Unregister();
        }

        private void Unregister()
        {
            if (!registered)
                return;
            registered = false;
            PlayerInput.Remove(source);
#if ENABLE_INPUT_SYSTEM
            if (enhanced)
                EnhancedTouchSupport.Disable();
            enhanced = false;
#endif
        }

        private void Update()
        {
            // The scene's own copy of the GUI goes when the persistent one takes over: the one that updates is the current one
            Current = this;
            // Last frame's one-frame presses are gone
            jump = pause = skip = false;
            Array.Clear(slots, 0, slots.Length);
            Array.Clear(powers, 0, powers.Length);
            look = Vector2.zero;

            var touch = SdfPlatform.Device.IsTouch;
            if (touch && !registered)
            {
                registered = true;
                PlayerInput.Add(source);
#if ENABLE_INPUT_SYSTEM
                if (Feed == null)
                {
                    EnhancedTouchSupport.Enable();
                    enhanced = true;
                }
#endif
            }
            else if (!touch && registered)
                Unregister();
            if (!touch)
                return;

            Sample();
            var shown = Layer != null && Layer.isActiveAndEnabled;
            if (!shown)
            {
                ReleaseAll();
                SkipByTap();
                return;
            }
            Track();
            Assist(Time.deltaTime);
        }

        // ---------------------------------------------------------------- The fingers

        private void Sample()
        {
            samples.Clear();
            if (Feed != null)
            {
                foreach (var sample in Feed())
                    samples.Add(sample);
                return;
            }
#if ENABLE_INPUT_SYSTEM
            foreach (var t in ETouch.activeTouches)
                if (t.phase != UnityEngine.InputSystem.TouchPhase.Ended && t.phase != UnityEngine.InputSystem.TouchPhase.Canceled)
                    samples.Add(new TouchSample { id = t.touchId, position = t.screenPosition });
            // A desktop browser forced to a touch device (?device=mobile) or the editor: the left mouse button is one finger
            if (samples.Count == 0 && Touchscreen.current == null && Mouse.current != null && Mouse.current.leftButton.isPressed)
                samples.Add(new TouchSample { id = -7, position = Mouse.current.position.ReadValue() });
#endif
        }

        private void Track()
        {
            // The fingers that lifted
            gone.Clear();
            foreach (var id in fingers.Keys)
            {
                var present = false;
                foreach (var s in samples)
                    if (s.id == id)
                        present = true;
                if (!present)
                    gone.Add(id);
            }
            foreach (var id in gone)
                Lift(id);

            var width = Mathf.Max(1, Screen.width);
            foreach (var s in samples)
            {
                if (!fingers.TryGetValue(s.id, out var finger))
                {
                    Begin(s, width);
                    continue;
                }
                var delta = s.position - finger.last;
                finger.last = s.position;
                switch (finger.role)
                {
                    case Role.Stick:
                        Steer(s.position);
                        break;
                    case Role.Look:
                        look += delta;
                        break;
                    case Role.Button:
                        if (finger.button != null && (finger.button.kind == TouchButtonKind.Fire || finger.button.kind == TouchButtonKind.FireLeft))
                            look += delta;
                        break;
                }
            }
        }

        private void Begin(TouchSample s, int width)
        {
            var button = Layer.ButtonAt(s.position);
            var finger = new Finger { last = s.position, button = button };
            if (button != null)
            {
                finger.role = Role.Button;
                button.Press(true);
                switch (button.kind)
                {
                    case TouchButtonKind.Fire:
                    case TouchButtonKind.FireLeft:
                        fire++;
                        break;
                    case TouchButtonKind.Jump:
                        jump = true;
                        break;
                    case TouchButtonKind.Zoom:
                        zoomOn = !zoomOn;
                        break;
                    case TouchButtonKind.Slot:
                        if (button.index >= 0 && button.index < slots.Length)
                            slots[button.index] = true;
                        break;
                    case TouchButtonKind.Power:
                        if (button.index >= 0 && button.index < powers.Length)
                            powers[button.index] = true;
                        break;
                    case TouchButtonKind.Scoreboard:
                        scoreboard = true;
                        break;
                    case TouchButtonKind.Pause:
                        pause = true;
                        break;
                }
            }
            else if (s.position.x < width * 0.5f && stickFinger < 0)
            {
                finger.role = Role.Stick;
                stickFinger = s.id;
                stickBase = s.position;
                Steer(s.position);
            }
            else
                finger.role = Role.Look;
            fingers[s.id] = finger;
        }

        // The stick: pushed to the rim is full speed; past the rim the base follows the finger
        private void Steer(Vector2 position)
        {
            var radius = Mathf.Max(1, Layer.StickPixels);
            var offset = position - stickBase;
            if (offset.magnitude > radius)
            {
                stickBase = position - offset.normalized * radius;
                offset = position - stickBase;
            }
            const float dead = 0.1f;
            var push = offset / radius;
            var length = push.magnitude;
            move = length < dead ? Vector2.zero : push / length * Mathf.Clamp01((length - dead) / (1 - dead));
            Layer.Stick(true, stickBase, position);
        }

        private void Lift(int id)
        {
            if (!fingers.TryGetValue(id, out var finger))
                return;
            fingers.Remove(id);
            switch (finger.role)
            {
                case Role.Stick:
                    stickFinger = -1;
                    move = Vector2.zero;
                    if (Layer != null && Application.isPlaying)
                        Layer.Stick(false);
                    break;
                case Role.Button:
                    if (finger.button != null)
                    {
                        // Another finger may still be on it
                        var other = false;
                        foreach (var f in fingers.Values)
                            if (f.button == finger.button)
                                other = true;
                        finger.button.Press(other);
                        if (finger.button.kind == TouchButtonKind.Fire || finger.button.kind == TouchButtonKind.FireLeft)
                            fire = Mathf.Max(0, fire - 1);
                        else if (finger.button.kind == TouchButtonKind.Scoreboard && !other)
                            scoreboard = false;
                    }
                    break;
            }
        }

        private void ReleaseAll()
        {
            if (fingers.Count > 0)
            {
                gone.Clear();
                gone.AddRange(fingers.Keys);
                foreach (var id in gone)
                    Lift(id);
            }
            fire = 0;
            autoFire.Reset();
            lookScale = 1;
            scoreboard = false;
            move = Vector2.zero;
            stickFinger = -1;
            zoomOn = false;
        }

        // The end card and the credits (no touch layer): a finger on the screen but not on a button is the skip, as Space is
        private void SkipByTap()
        {
            var screen = flow != null ? flow.screen : GuiScreen.Hud;
            if (samples.Count == 0 || (screen != GuiScreen.Victory && screen != GuiScreen.Defeat && screen != GuiScreen.Credits))
                return;
            foreach (var s in samples)
                if (!OverButton(s.position))
                    skip = true;
        }

        private static readonly List<RaycastResult> hits = new List<RaycastResult>();

        private static bool OverButton(Vector2 position)
        {
            if (EventSystem.current == null)
                return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            foreach (var hit in hits)
                if (hit.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null)
                    return true;
            return false;
        }

        // ---------------------------------------------------------------- Auto-fire and aim assist

        private void Assist(float dt)
        {
            var combat = Combat.Current;
            var you = combat != null && combat.Match != null ? combat.Match.You : null;
            var arsenal = you != null ? combat.ArsenalOf(you) : null;
            // The finger's own turn rate (before the assist scales it), so the assist cannot lower it under the swipe threshold
            var width = Mathf.Max(1, Screen.width);
            var turned = new Vector2(TouchRules.SwipeDegrees(look.x, width, GuiSettings.TouchLook), TouchRules.SwipeDegrees(look.y, width, GuiSettings.TouchLook)).magnitude;
            var rate = dt > 0 ? turned / dt : 0;
            // A swipe is not an aim: no slowdown (so no lock-on), and the auto-fire waits for the crosshair to settle on an enemy
            lookScale = arsenal != null && rate <= TouchRules.SwipeRate ? TouchAssist.LookScale(combat, arsenal) : 1;
            if (arsenal == null || !GuiSettings.AutoFire)
                autoFire.Reset();
            else
                autoFire.Update(TouchAssist.EnemyUnder(combat, arsenal), rate, dt, TouchAssist.DwellFor(arsenal.Weapon));
            // The zoom is the sniper's: another weapon in hand zooms out
            if (zoomOn && (arsenal == null || arsenal.Weapon == null || arsenal.Weapon.zoomFieldOfView <= 0))
                zoomOn = false;
        }

        // ---------------------------------------------------------------- The source

        private sealed class Source : PlayerInputSource
        {
            private readonly TouchControls c;

            public Source(TouchControls controls) => c = controls;

            public override Vector2 Move => c.move;

            // In degrees at the controller's own rest sensitivity: the controller multiplies by its sensitivity (smaller when zoomed in)
            public override Vector2 Look
            {
                get
                {
                    if (c.look == Vector2.zero)
                        return Vector2.zero;
                    var width = Mathf.Max(1, Screen.width);
                    var degrees = new Vector2(TouchRules.SwipeDegrees(c.look.x, width, GuiSettings.TouchLook), TouchRules.SwipeDegrees(c.look.y, width, GuiSettings.TouchLook));
                    return degrees * c.lookScale / ControllerSensitivity;
                }
            }

            public override bool FireHeld => c.fire > 0 || c.autoFire.Firing;
            public override bool ZoomHeld => c.zoomOn;
            public override bool JumpPressed => c.jump;
            public override bool SlotPressed(int number) => number >= 1 && number <= c.slots.Length && c.slots[number - 1];
            public override bool PowerPressed(SporePower power) => (int)power >= 0 && (int)power < c.powers.Length && c.powers[(int)power];
            public override bool ScoreboardHeld => c.scoreboard;
            public override bool PausePressed => c.pause;
            public override bool SkipHeld => c.skip;
        }
    }
}
