using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Watching a match of bots only (ticket 23's check): the scene's player stops being a player (its controller and capsule off) and
    /// its camera follows one bot from behind and above, or flies free. Tab follows the next player, Shift+Tab the one before, F
    /// switches to the free camera and back (right mouse button held to look, WASD to fly, Q/E down and up, Shift faster). The match
    /// attaches it when <see cref="Match.spectate"/> is on (in the editor: Tools > SDF Battle > Spectate Bots); the HUD then shows the
    /// first red bot, as it is the match's first player.
    /// </summary>
    public sealed class BotSpectator : MonoBehaviour
    {
        public float distance = 4.5f;
        public float height = 1.2f;
        public float flySpeed = 12;
        public float lookSensitivity = 2;

        private Match match;
        private Camera view;
        private int index;
        private bool free;
        private float yaw;
        private float pitch = 15;

        /// <summary>Turns the scene's player into a spectator of <paramref name="owner"/>.</summary>
        public static BotSpectator Attach(GameObject you, Match owner)
        {
            var controller = you.GetComponent<FirstPersonController>();
            if (controller != null)
                controller.enabled = false;
            var capsule = you.GetComponent<CharacterController>();
            if (capsule != null)
                capsule.enabled = false;
            var spectator = you.GetComponent<BotSpectator>();
            if (spectator == null)
                spectator = you.AddComponent<BotSpectator>();
            spectator.match = owner;
            spectator.view = you.GetComponentInChildren<Camera>();
            spectator.yaw = you.transform.eulerAngles.y;
            return spectator;
        }

        public Player Followed => match != null && match.Players.Count > 0 ? match.Players[Mathf.Clamp(index, 0, match.Players.Count - 1)] : null;

        private void LateUpdate()
        {
            if (match == null || view == null)
                return;
            var count = match.Players.Count;
            if (Input.GetKeyDown(KeyCode.Tab) && count > 0)
            {
                var back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                index = (index + (back ? count - 1 : 1)) % count;
                free = false;
            }
            if (Input.GetKeyDown(KeyCode.F))
                free = !free;
            if (free)
                Fly();
            else
                Follow();
        }

        private void Follow()
        {
            var player = Followed;
            if (player == null)
                return;
            var head = player.transform.position + Vector3.up * 1.6f;
            var behind = Quaternion.Euler(18, player.Controller.Yaw, 0) * Vector3.back * distance + Vector3.up * height;
            var wanted = head + behind;
            // Not through walls: in front of what stands between
            if (Physics.SphereCast(head, 0.2f, behind.normalized, out var hit, behind.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && Player.Of(hit.collider) == null)
                wanted = head + behind.normalized * Mathf.Max(0.5f, hit.distance - 0.1f);
            var t = view.transform;
            t.position = Vector3.Lerp(t.position, wanted, Mathf.Clamp01(Time.deltaTime * 8));
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(head - t.position), Mathf.Clamp01(Time.deltaTime * 10));
            var angles = t.eulerAngles;
            yaw = angles.y;
            pitch = angles.x > 180 ? angles.x - 360 : angles.x;
        }

        private void Fly()
        {
            var t = view.transform;
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -85, 85);
            }
            t.rotation = Quaternion.Euler(pitch, yaw, 0);
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            if (Input.GetKey(KeyCode.E))
                move.y += 1;
            if (Input.GetKey(KeyCode.Q))
                move.y -= 1;
            var speed = flySpeed * (Input.GetKey(KeyCode.LeftShift) ? 2.5f : 1);
            t.position += t.rotation * move * (speed * Time.deltaTime);
        }

        private void OnGUI()
        {
            var player = Followed;
            var bot = player != null ? player.GetComponent<Bot>() : null;
            var text = free ? "Free camera (F: follow, Tab: next player)"
                : $"Following {player?.name}{(bot != null ? $": {bot.goalNow} ({bot.style})" : "")}   Tab: next, F: free camera";
            GUI.Label(new Rect(12, Screen.height - 30, 900, 24), text);
        }
    }
}
