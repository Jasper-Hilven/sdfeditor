using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The scout drawn for a player (ticket 20): an instance of the team's scout prefab (amanita-scout red, chanterelle-scout blue) at
    /// the player's feet, turning with its yaw. The floating eyes and hands (the prefab's eye.0, eye.1, hand.0, hand.1) follow the aim:
    /// the eyes look up and down with the pitch, the hands swing round the eyes' height with it, and all four bob a little, out of step,
    /// as the scouts float them. Purely visual: no collider of its own (the player's capsule is what is hit).
    /// </summary>
    public sealed class ScoutBody : MonoBehaviour
    {
        /// <summary>Where the eyes float in the scout's model, the point the hands swing round.</summary>
        private static readonly Vector3 Pivot = new Vector3(0, 0.86f, 0);

        [Tooltip("Whose aim the eyes and hands follow")]
        public FirstPersonController controller;
        [Tooltip("Metres the eyes and hands bob up and down")]
        public float bob = 0.025f;
        [Tooltip("Bobs per second")]
        public float bobRate = 0.7f;
        [Tooltip("How much of the pitch the hands follow")]
        [Range(0, 1)] public float handFollow = 0.7f;

        private Transform[] eyes;
        private Transform[] hands;
        private Vector3[] eyeRest;
        private Vector3[] handRest;
        private float phase;

        /// <summary>The team's scout at the player's feet, its eyes and hands on <paramref name="controller"/>'s aim.</summary>
        public static ScoutBody Create(GameObject prefab, FirstPersonController controller)
        {
            var instance = Instantiate(prefab, controller.transform, false);
            instance.name = "Body";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            // Only the player's capsule is hit or collides; a stray collider on the model would push the controller about
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            var body = instance.AddComponent<ScoutBody>();
            body.controller = controller;
            return body;
        }

        private void Awake()
        {
            eyes = new[] { Find("eye.0"), Find("eye.1") };
            hands = new[] { Find("hand.0"), Find("hand.1") };
            eyeRest = System.Array.ConvertAll(eyes, t => t != null ? t.localPosition : Vector3.zero);
            handRest = System.Array.ConvertAll(hands, t => t != null ? t.localPosition : Vector3.zero);
            phase = Random.value * 10;
        }

        private Transform Find(string part)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == part)
                    return t;
            return null;
        }

        private void LateUpdate()
        {
            var pitch = controller != null ? controller.Pitch : 0;
            var look = Quaternion.Euler(pitch, 0, 0);
            var swing = Quaternion.Euler(pitch * handFollow, 0, 0);
            var t = (Time.time + phase) * bobRate * 2 * Mathf.PI;
            for (var i = 0; i < eyes.Length; i++)
            {
                if (eyes[i] == null)
                    continue;
                eyes[i].localPosition = eyeRest[i] + Vector3.up * (bob * Mathf.Sin(t + i * 0.6f));
                eyes[i].localRotation = look;
            }
            for (var i = 0; i < hands.Length; i++)
            {
                if (hands[i] == null)
                    continue;
                hands[i].localPosition = Pivot + swing * (handRest[i] - Pivot) + Vector3.up * (bob * Mathf.Sin(t * 0.8f + 1.7f + i * 2.1f));
                hands[i].localRotation = swing;
            }
        }
    }
}
