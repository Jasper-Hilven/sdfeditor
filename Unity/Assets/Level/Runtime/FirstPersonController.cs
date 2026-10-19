using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// Walks a level in first person: WASD or the arrow keys to walk, Shift to run, Space to jump, the mouse to look once the view is
    /// clicked (Escape lets the cursor go). A <see cref="CharacterController"/> keeps the player on the ground and out of the colliders
    /// (the chunks' ground, the walls around the playable area); the camera is a child at eye height. Reads the classic input manager,
    /// which the project uses, so it needs no input package.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Tooltip("Walking speed in metres per second")]
        public float walkSpeed = 4;
        [Tooltip("Running speed (Shift) in metres per second")]
        public float runSpeed = 7;
        [Tooltip("Height of a jump in metres")]
        public float jumpHeight = 1;
        [Tooltip("Degrees of turn per unit of mouse movement")]
        public float lookSensitivity = 2;
        public float gravity = 20;

        private CharacterController controller;
        private Transform eye;
        private float pitch;
        private float verticalSpeed;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            var camera = GetComponentInChildren<Camera>();
            eye = camera != null ? camera.transform : transform;
        }

        private void Update()
        {
            Look();
            Move();
        }

        private void Look()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            transform.Rotate(0, Input.GetAxis("Mouse X") * lookSensitivity, 0, Space.Self);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -85, 85);
            eye.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        private void Move()
        {
            var input = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")), 1);
            var speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? runSpeed : walkSpeed;
            var velocity = transform.TransformDirection(input) * speed;

            if (controller.isGrounded)
            {
                // A small push down keeps the controller on the ground going down slopes
                verticalSpeed = -2;
                if (Input.GetButtonDown("Jump"))
                    verticalSpeed = Mathf.Sqrt(2 * gravity * jumpHeight);
            }
            else
            {
                verticalSpeed -= gravity * Time.deltaTime;
            }
            velocity.y = verticalSpeed;
            controller.Move(velocity * Time.deltaTime);
        }
    }
}
