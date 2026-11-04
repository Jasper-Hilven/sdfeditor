using SdfBattle;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The keyboard-and-mouse source of <see cref="PlayerInput"/> (ticket 55), the only gameplay code that reads <c>UnityEngine.Input</c>
    /// (with <see cref="GameKeys"/>, which reads the keys by place). It does what the readers did before: the pointer-taking rules
    /// (firing, zooming, looking and scrolling only with the pointer locked), the mouse axes at their old scale, the keys by place for
    /// walking, the powers, the head change and P.
    /// </summary>
    public sealed class KeyboardMouseInput : PlayerInputSource
    {
        static bool Locked => Cursor.lockState == CursorLockMode.Locked;

        public override Vector2 Move => GameKeys.Walk();

        public override Vector2 Look => Locked ? new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) : Vector2.zero;

        public override bool FireHeld => Locked && Input.GetMouseButton(0);
        public override bool ZoomHeld => Locked && Input.GetMouseButton(1);
        public override bool JumpPressed => Input.GetButtonDown("Jump");

        public override bool SlotPressed(int number) => Input.GetKeyDown(KeyCode.Alpha0 + number) || Input.GetKeyDown(KeyCode.Keypad0 + number);

        public override float SlotScroll => Locked ? Input.mouseScrollDelta.y : 0;

        public override bool PowerPressed(SporePower power) => Locked && GameKeys.Pressed(PlaceOf(power));

        public override bool ReloadPressed => Input.GetKeyDown(KeyCode.R);
        public override bool UsePressed => Input.GetKeyDown(KeyCode.E);
        public override bool ScoreboardHeld => Input.GetKey(KeyCode.Tab);
        public override bool PausePressed => Input.GetKeyDown(KeyCode.Escape) || GameKeys.Pressed(KeyPlace.P);
        public override bool PauseHeld => Input.GetKey(KeyCode.Escape) || GameKeys.Held(KeyPlace.P);
        public override bool SkipHeld => GameKeys.SkipHeld() || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton7);
        public override bool HeadPreviousHeld => GameKeys.Held(KeyPlace.A);
        public override bool HeadNextHeld => GameKeys.Held(KeyPlace.D);
        public override bool PointerHeld => Input.GetMouseButton(0);
        public override bool PointerPressed => Input.GetMouseButtonDown(0);

        static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public override int SpectatePlayer => Input.GetKeyDown(KeyCode.Tab) ? (Shift ? -1 : 1) : 0;
        public override int SpectateMachine => Input.GetKeyDown(KeyCode.M) ? (Shift ? -1 : 1) : 0;
        public override bool SpectateFreeToggled => Input.GetKeyDown(KeyCode.F);
        public override bool FlyLookHeld => Input.GetMouseButton(1);

        public override Vector3 Fly =>
            new(Input.GetAxisRaw("Horizontal"), (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0), Input.GetAxisRaw("Vertical"));

        public override bool FlyFastHeld => Shift;
        public override bool DebugPressed(KeyCode key) => Input.GetKeyDown(key);

        static KeyPlace PlaceOf(SporePower power) => power switch
        {
            SporePower.Frenzy => KeyPlace.Q,
            SporePower.Regenerate => KeyPlace.F,
            _ => KeyPlace.C,
        };
    }
}
