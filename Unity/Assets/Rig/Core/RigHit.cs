using UnityEngine;

namespace SdfRig
{
    /// <summary>On a joint that has a hit capsule (a trigger CapsuleCollider on the same object): the bone it belongs to.</summary>
    public sealed class RigHit : MonoBehaviour
    {
        public string Bone;
    }
}
