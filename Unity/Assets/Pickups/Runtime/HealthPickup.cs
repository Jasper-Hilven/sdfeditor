using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace SdfPickups
{
    /// <summary>
    /// A health pod's behaviour, like <see cref="WeaponPickup"/>: the player (anything with a <see cref="CharacterController"/>) walks
    /// into the trigger, takes <see cref="amount"/> health, and the floating cross disappears. The game has no health system yet, so
    /// the pickup only announces it: <see cref="onPickedUp"/> (a UnityEvent with the amount, for wiring in the inspector) and the static
    /// <see cref="PickedUp"/> event (the pod and the player's object), which a health system will listen to. After
    /// <see cref="respawnSeconds"/> the cross is back for the next taker; 0 or less and the pod stays empty.
    /// The trigger is this object's own collider; the pod's solid collider is on a child, so the player can still walk onto the pod.
    /// </summary>
    public sealed class HealthPickup : MonoBehaviour
    {
        [Tooltip("The health this pod gives")]
        public int amount = 50;
        [Tooltip("The floating cross, hidden while the pod is empty")]
        public GameObject cross;
        [Tooltip("Seconds until the cross is back after it was taken; 0 or less for never")]
        public float respawnSeconds = 20;
        [Tooltip("Called with the amount when a player takes the health")]
        public UnityEvent<int> onPickedUp = new UnityEvent<int>();

        /// <summary>Raised when a player takes the health: the pod and the player's object.</summary>
        public static event Action<HealthPickup, GameObject> PickedUp;

        /// <summary>Whether the cross is on the pod, to be taken.</summary>
        public bool Available { get; private set; } = true;

        /// <summary>
        /// Asked before a player takes it, when set (ticket 22's combat): false leaves the pod as it is, for others. A player who has all
        /// the health a pod can give leaves it.
        /// </summary>
        public static Func<HealthPickup, GameObject, bool> CanPickUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PickedUp = null;
            CanPickUp = null;
        }

        private void OnTriggerEnter(Collider other) => TryPickUp(other);

        // Also while standing in it, so a cross that came back under the player's feet is taken at once
        private void OnTriggerStay(Collider other) => TryPickUp(other);

        private void TryPickUp(Collider other)
        {
            if (!Available)
                return;
            var player = other.GetComponentInParent<CharacterController>();
            if (player != null && (CanPickUp == null || CanPickUp(this, player.gameObject)))
                PickUp(player.gameObject);
        }

        /// <summary>Gives the health to the player's object and starts the respawn; nothing when the pod is empty.</summary>
        public void PickUp(GameObject player)
        {
            if (!Available)
                return;
            SetAvailable(false);
            onPickedUp.Invoke(amount);
            PickedUp?.Invoke(this, player);
            if (respawnSeconds > 0)
                StartCoroutine(Respawn());
        }

        private IEnumerator Respawn()
        {
            yield return new WaitForSeconds(respawnSeconds);
            SetAvailable(true);
        }

        private void SetAvailable(bool available)
        {
            Available = available;
            if (cross != null)
                cross.SetActive(available);
        }
    }
}
