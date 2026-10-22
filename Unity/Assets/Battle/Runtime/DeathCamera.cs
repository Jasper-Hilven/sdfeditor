using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Your view when you are killed: the camera leaves your eyes and pulls back, up and behind where you stood, smoothly, and stays
    /// framed on the spot (slowly circling it) so you watch yourself burst (<see cref="Gore"/>), until you respawn; then it is back in
    /// your eyes, first person. Your weapon in the corner of the view is hidden meanwhile. The same with gore off (you see the puff).
    /// A fall keeps the view where it was (the death screen shows over it): there is nothing to watch below the island. The match
    /// attaches it to you at its start.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class DeathCamera : MonoBehaviour
    {
        [Tooltip("Metres back from the spot the camera pulls")]
        public float distance = 4.2f;
        [Tooltip("Metres above the spot")]
        public float height = 2.6f;
        [Tooltip("Seconds the pull back takes, about")]
        public float smoothTime = 0.45f;
        [Tooltip("Degrees per second the camera circles the spot")]
        public float orbit = 7;

        private Match match;
        private Player player;
        private Transform view;
        private Vector3 eyePosition;
        private bool watching;
        private Vector3 focus;
        private float yaw;
        private Vector3 velocity;
        private GameObject weapons;

        /// <summary>Your death camera, on the camera in your player.</summary>
        public static DeathCamera Attach(Player you, Match owner)
        {
            var camera = you.GetComponentInChildren<Camera>();
            if (camera == null)
                return null;
            var deathCamera = you.GetComponent<DeathCamera>();
            if (deathCamera == null)
                deathCamera = you.gameObject.AddComponent<DeathCamera>();
            deathCamera.match = owner;
            deathCamera.player = you;
            deathCamera.view = camera.transform;
            deathCamera.eyePosition = camera.transform.localPosition;
            owner.State.Died += deathCamera.OnDied;
            owner.State.Spawned += deathCamera.OnSpawned;
            return deathCamera;
        }

        private void OnDestroy()
        {
            if (match != null && match.State != null)
            {
                match.State.Died -= OnDied;
                match.State.Spawned -= OnSpawned;
            }
        }

        /// <summary>Whether the camera is out of your eyes, watching where you died.</summary>
        public bool Watching => watching;

        private void OnDied(Death death)
        {
            if (death.Victim != player.State || death.Cause != DeathCause.Killed)
                return;
            watching = true;
            focus = player.transform.position + Vector3.up * 0.8f;
            yaw = player.Controller.Yaw;
            velocity = Vector3.zero;
            var arsenal = player.GetComponent<Arsenal>();
            weapons = arsenal != null && arsenal.View != null ? arsenal.View.gameObject : null;
            if (weapons != null)
                weapons.SetActive(false);
        }

        private void OnSpawned(PlayerState state)
        {
            if (state != player.State || !watching)
                return;
            watching = false;
            // Back in your eyes, looking where the controller now aims (the spawn's way, level)
            view.localPosition = eyePosition;
            view.localRotation = Quaternion.Euler(player.Controller.Pitch, 0, 0);
            if (weapons != null)
                weapons.SetActive(true);
        }

        private void LateUpdate()
        {
            if (!watching)
                return;
            var dt = Time.deltaTime;
            yaw += orbit * dt;
            var back = Quaternion.Euler(0, yaw, 0) * Vector3.back * distance + Vector3.up * height;
            var wanted = focus + back;
            // Not through walls: in front of what stands between (the corpse's pieces are not raycast)
            var length = back.magnitude;
            if (Physics.SphereCast(focus, 0.25f, back / length, out var hit, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && Player.Of(hit.collider) == null)
                wanted = focus + back / length * Mathf.Max(0.8f, hit.distance - 0.15f);
            view.position = Vector3.SmoothDamp(view.position, wanted, ref velocity, smoothTime);
            var look = focus - view.position;
            if (look.sqrMagnitude > 1e-4f)
                view.rotation = Quaternion.Slerp(view.rotation, Quaternion.LookRotation(look), 1 - Mathf.Exp(-dt * 8));
        }
    }
}
