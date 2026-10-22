using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace SdfPickups
{
    /// <summary>
    /// A weapon pickup pod's behaviour: the player (anything with a <see cref="CharacterController"/>) walks into the trigger, takes the
    /// weapon named <see cref="weaponId"/>, and the floating weapon disappears. The game has no inventory yet, so the pickup only
    /// announces it: <see cref="onPickedUp"/> (a UnityEvent with the weapon id, for wiring in the inspector) and the static
    /// <see cref="PickedUp"/> event (the pod and the player's object), which an inventory will listen to. After
    /// <see cref="respawnSeconds"/> the weapon is back for the next taker; 0 or less and the pod stays empty. With
    /// <see cref="staysWhenTaken"/> (the default, the user's decision after ticket 24) the weapon never leaves the pod: whoever walks
    /// onto it gets the weapon, and it stays for the next.
    /// The trigger is this object's own collider; the pod's solid collider is on a child, so the player can still walk onto the pod.
    /// </summary>
    public sealed class WeaponPickup : MonoBehaviour
    {
        [Tooltip("The weapon this pod gives: the name of its model, like minigun")]
        public string weaponId = "";
        [Tooltip("The floating weapon, hidden while the pod is empty")]
        public GameObject weapon;
        [Tooltip("The weapon stays on the pod when taken: everyone who walks onto it gets it (the battle's pods)")]
        public bool staysWhenTaken = true;
        [Tooltip("Seconds until the weapon is back after it was taken (when it does not stay); 0 or less for never")]
        public float respawnSeconds = 15;
        [Tooltip("Called with the weapon id when a player takes the weapon")]
        public UnityEvent<string> onPickedUp = new UnityEvent<string>();

        /// <summary>Raised when a player takes a weapon: the pod and the player's object.</summary>
        public static event Action<WeaponPickup, GameObject> PickedUp;

        /// <summary>Whether the weapon is on the pod, to be taken.</summary>
        public bool Available { get; private set; } = true;

        /// <summary>
        /// Asked before a player takes it, when set (ticket 22's combat): false leaves the pod as it is, for others. A player who
        /// already holds the weapon leaves it.
        /// </summary>
        public static Func<WeaponPickup, GameObject, bool> CanPickUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PickedUp = null;
            CanPickUp = null;
        }

        private void OnTriggerEnter(Collider other) => TryPickUp(other, true);

        // Also while standing in it, so a weapon that came back under the player's feet (or one the player can take now) is taken at
        // once; a weapon that stays is taken only on entering when nothing asks first, so standing on it does not take it every step
        private void OnTriggerStay(Collider other) => TryPickUp(other, false);

        private void TryPickUp(Collider other, bool entering)
        {
            if (!Available || (!entering && staysWhenTaken && CanPickUp == null))
                return;
            var player = other.GetComponentInParent<CharacterController>();
            if (player != null && (CanPickUp == null || CanPickUp(this, player.gameObject)))
                PickUp(player.gameObject);
        }

        /// <summary>Gives the weapon to the player's object and, unless it stays, starts the respawn; nothing when the pod is empty.</summary>
        public void PickUp(GameObject player)
        {
            if (!Available)
                return;
            if (!staysWhenTaken)
                SetAvailable(false);
            onPickedUp.Invoke(weaponId);
            PickedUp?.Invoke(this, player);
            if (!staysWhenTaken && respawnSeconds > 0)
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
            if (weapon != null)
                weapon.SetActive(available);
        }
    }
}
