using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The final boss's places in the arena scene (ticket 33.3; level.json "boss", the level file's <c>boss</c> section), at the scene's
    /// root as Boss Spots: where Prime stands in phase 1 (on the dais), where the Prime-copter crashes and the wreck stands, the champion's
    /// plinth where 42's golden minigun appears in phase 3, and the middle of the spore shield's dome. Data only, for the boss's runtime
    /// (33.6) to read; each is an empty marker, at its place in Unity's coordinates, turned the way it faces.
    /// </summary>
    public sealed class BossSpots : MonoBehaviour
    {
        public const string ObjectName = "Boss Spots";

        public Transform prime;
        public Transform crash;
        public Transform plinth;
        public Transform shield;

        [Tooltip("The shield dome's radius in metres")]
        public float shieldRadius = 7;
    }
}
