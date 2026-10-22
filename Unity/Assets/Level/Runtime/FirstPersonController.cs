using UnityEngine;
using UnityEngine.EventSystems;

namespace SdfLevel
{
    /// <summary>
    /// Moves a player by the game's rules (ticket 20): walking at <see cref="walkSpeed"/> times <see cref="speedMultiplier"/> (the flag
    /// carrier's 0.9, ticket 21), slopes and steps as the <see cref="CharacterController"/> allows, gravity, and a jump of
    /// <see cref="jumpHeight"/> from the ground only (no double jump). No sprint, no crouch.
    /// <para>
    /// Two kinds of driver use the same movement API, so both move by the same rules: with <see cref="readsInput"/> (you) the keyboard
    /// and mouse feed it, WASD or the arrow keys to walk, Space to jump, the mouse to look once the view is clicked (the pointer is then
    /// locked, which is what a click asks of the web build too; Escape lets it go); without it (a bot, ticket 23) its brain calls
    /// <see cref="SetMove"/>, <see cref="SetAim"/> and <see cref="Jump"/>. <see cref="frozen"/> holds it in place (the match's
    /// countdown). Walks by the keys' places (<see cref="GameKeys"/>, ticket 25: ZQSD on AZERTY); the rest from the classic input
    /// manager.
    /// </para>
    /// The camera, if any, is a child at eye height and takes the pitch; the body turns with the yaw.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        /// <summary>The player's capsule, for every player: about a scout's height, its stem's width.</summary>
        public const float Height = 1.75f, Radius = 0.3f;

        [Tooltip("Walking speed in metres per second")]
        public float walkSpeed = 4.4f;
        [Tooltip("Set by other systems: the flag carrier walks at 0.9 (ticket 21)")]
        public float speedMultiplier = 1;
        [Tooltip("Set by combat every frame: a blob's slow (ticket 22); multiplies with speedMultiplier")]
        public float slowMultiplier = 1;
        [Tooltip("Set by the data pulses every frame (ticket 30.9): the speed-up of a scout with a pulse; multiplies with the others")]
        public float boostMultiplier = 1;
        [Tooltip("Height of a jump in metres")]
        public float jumpHeight = 1.1f;
        [Tooltip("Degrees of turn per unit of mouse movement")]
        public float lookSensitivity = 2;
        public float gravity = 20;
        [Tooltip("Driven by the keyboard and mouse (you); off for a bot, whose brain calls SetMove, SetAim and Jump")]
        public bool readsInput = true;
        [Tooltip("Held in place: no walking, no jumping (gravity still pulls); looking about is allowed")]
        public bool frozen;

        private CharacterController controller;
        private Transform eye;
        private float pitch;
        private float verticalSpeed;
        private Vector2 move;
        private bool jumpWanted;
        private PathMoverCar standingOn;
        private int standingFrame = -1;

        /// <summary>Where the view looks up or down, degrees, positive down (as a camera's x rotation).</summary>
        public float Pitch => pitch;

        /// <summary>Where the body faces, degrees about y.</summary>
        public float Yaw => transform.eulerAngles.y;

        public bool IsGrounded => controller != null && controller.isGrounded;

        /// <summary>The way the player aims: the yaw and the pitch together.</summary>
        public Vector3 AimDirection => Quaternion.Euler(pitch, Yaw, 0) * Vector3.forward;

        /// <summary>Gives a new player's CharacterController the capsule every player has, its slope and step limits.</summary>
        public static void Configure(CharacterController controller)
        {
            controller.height = Height;
            controller.radius = Radius;
            controller.center = new Vector3(0, Height / 2, 0);
            controller.slopeLimit = 45;
            controller.stepOffset = 0.4f;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            var camera = GetComponentInChildren<Camera>();
            eye = camera != null ? camera.transform : null;
        }

        // ---------------------------------------------------------------- The movement API (bots call it; your input feeds it)

        /// <summary>Where to walk this frame and on: x sideways, y forward, in the body's frame; longer than 1 is cut to 1.</summary>
        public void SetMove(Vector2 input) => move = Vector2.ClampMagnitude(input, 1);

        /// <summary>Turns the body to <paramref name="yaw"/> and the view to <paramref name="pitchDegrees"/> (positive down).</summary>
        public void SetAim(float yaw, float pitchDegrees)
        {
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            pitch = Mathf.Clamp(pitchDegrees, -85, 85);
            if (eye != null)
                eye.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        /// <summary>Jumps on the next update, if on the ground then; in the air it is forgotten (no double jump).</summary>
        public void Jump() => jumpWanted = true;

        /// <summary>Metres a second squared a shove slows by.</summary>
        public const float ShoveDrag = 20;

        // A shove still going (ticket 33.5: the dive's push, later the slam's and the grab's throw): metres a second, flat, slowing to 0
        private Vector3 shove;

        /// <summary>
        /// Pushes the player <paramref name="metres"/> along <paramref name="direction"/> (flat) over a short moment: a speed that slows
        /// by <see cref="ShoveDrag"/> and walks on top of the player's own, through the same CharacterController, so walls stop it.
        /// </summary>
        public void Shove(Vector3 direction, float metres)
        {
            direction.y = 0;
            if (direction.sqrMagnitude < 1e-6f || metres <= 0)
                return;
            shove = direction.normalized * Mathf.Sqrt(2 * ShoveDrag * metres);
        }

        /// <summary>Puts the player at <paramref name="position"/> (its feet) facing <paramref name="yaw"/>, level, standing still.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            // A CharacterController keeps its own idea of where it is: off while moved, so the move holds
            var enabledBefore = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;
            transform.position = position;
            if (controller != null)
                controller.enabled = enabledBefore;
            SetAim(yaw, 0);
            verticalSpeed = 0;
            standingOn = null;
            shove = Vector3.zero;
            move = Vector2.zero;
            jumpWanted = false;
        }

        // ---------------------------------------------------------------- Updates

        private void Update()
        {
            if (readsInput)
                ReadInput();
            Move();
        }

        private void ReadInput()
        {
            // A click on the view takes the pointer (one on a button of the GUI does not); Escape lets it go
            if (Input.GetMouseButtonDown(0) && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Cursor.lockState == CursorLockMode.Locked)
                SetAim(Yaw + Input.GetAxis("Mouse X") * lookSensitivity, pitch - Input.GetAxis("Mouse Y") * lookSensitivity);

            SetMove(GameKeys.Walk());
            if (Input.GetButtonDown("Jump"))
                Jump();
        }

        /// <summary>
        /// A moving platform (ticket 27.3): when the last move ended on a <see cref="PathMoverCar"/>, the car's step this frame carries the
        /// player along and turns them with it; on anything else nothing changes.
        /// </summary>
        private void Ride()
        {
            if (standingOn == null || standingFrame != Time.frameCount - 1 || !controller.isGrounded)
                return;
            var carried = standingOn.Carry(transform.position, out var yawChange);
            if (carried != Vector3.zero)
                controller.Move(carried);
            if (yawChange != 0)
                SetAim(Yaw + yawChange, pitch);
        }

        // The ground the last move ended on: a car of a path mover, if it was
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y < 0.45f)
                return;
            standingOn = hit.collider.GetComponentInParent<PathMoverCar>();
            standingFrame = Time.frameCount;
        }

        private void Move()
        {
            if (controller == null || !controller.enabled)
                return;
            Ride();
            var input = frozen ? Vector2.zero : move;
            var speed = walkSpeed * Mathf.Max(0, speedMultiplier) * Mathf.Max(0, slowMultiplier) * Mathf.Max(0, boostMultiplier);
            var velocity = transform.TransformDirection(new Vector3(input.x, 0, input.y)) * speed;

            if (controller.isGrounded)
            {
                // A small push down keeps the controller on the ground going down slopes
                verticalSpeed = -2;
                if (jumpWanted && !frozen)
                    verticalSpeed = Mathf.Sqrt(2 * gravity * jumpHeight);
            }
            else
            {
                verticalSpeed -= gravity * Time.deltaTime;
            }
            jumpWanted = false;
            velocity += shove;
            shove = Vector3.MoveTowards(shove, Vector3.zero, ShoveDrag * Time.deltaTime);
            velocity.y = verticalSpeed;
            var flags = controller.Move(velocity * Time.deltaTime);
            // A head against a ceiling (a gate's arch, a stair above) ends the rise
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0)
                verticalSpeed = 0;
        }
    }
}
