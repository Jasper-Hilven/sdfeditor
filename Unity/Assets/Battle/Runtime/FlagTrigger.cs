using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A capture-the-flag trigger (ticket 21): a flag stand's (take the enemy flag, capture with it) or a dropped flag's (take it, or
    /// return your own). It reacts to players only, by their capsule (<see cref="Player.Of"/>); anything else that walks in, a machine
    /// (ticket 24) or a thrown object, is ignored. On entering and while standing in it, so a flag that comes home under a waiting
    /// carrier's feet is captured at once. <see cref="CaptureTheFlag"/> adds it and decides what a touch does.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class FlagTrigger : MonoBehaviour
    {
        public CaptureTheFlag owner;
        [Tooltip("The team whose stand or flag this is")]
        public Team team;
        [Tooltip("The stand's trigger; otherwise the flag's own, live only while it lies on the ground")]
        public bool stand;

        public CapsuleCollider Collider { get; private set; }

        /// <summary>Adds a trigger capsule standing on <paramref name="parent"/>'s origin, as a child object.</summary>
        public static FlagTrigger Create(CaptureTheFlag owner, Transform parent, Team team, bool stand, float radius, float height)
        {
            var go = new GameObject(stand ? "Stand Trigger" : "Flag Trigger");
            go.transform.SetParent(parent, false);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.radius = radius;
            capsule.height = Mathf.Max(height, 2 * radius);
            capsule.center = new Vector3(0, capsule.height / 2, 0);
            capsule.direction = 1;
            var trigger = go.AddComponent<FlagTrigger>();
            trigger.owner = owner;
            trigger.team = team;
            trigger.stand = stand;
            trigger.Collider = capsule;
            return trigger;
        }

        private void Awake() => Collider = GetComponent<CapsuleCollider>();

        /// <summary>Whether touches count: off for a flag at home or carried.</summary>
        public bool Live
        {
            get => Collider != null && Collider.enabled;
            set
            {
                if (Collider != null)
                    Collider.enabled = value;
            }
        }

        private void OnTriggerEnter(Collider other) => Touch(other);

        private void OnTriggerStay(Collider other) => Touch(other);

        private void Touch(Collider other)
        {
            if (owner == null)
                return;
            var player = Player.Of(other);
            if (player != null && player.State != null)
                owner.Touch(this, player);
        }
    }
}
