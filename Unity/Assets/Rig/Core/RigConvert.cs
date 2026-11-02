using UnityEngine;

namespace SdfRig
{
    /// <summary>
    /// glTF to Unity. glTF is right-handed, +Z forward, +X the model's left. Unity's import mirrors X so +Z stays forward and the
    /// model's left is Unity -X. Positions and directions: (x, y, z) to (-x, y, z). Rotation quaternions: (x, y, z, w) to (x, -y, -z, w).
    /// A rotation is a pseudovector: a glTF rotation by angle a about axis n is, in Unity, the rotation by -a about (-n.x, n.y, n.z).
    /// </summary>
    public static class RigConvert
    {
        public static Vector3 Position(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        public static Vector3 Direction(Vector3 d) => new Vector3(-d.x, d.y, d.z);

        /// <summary>Mirrors a quaternion; applying it twice gives the original, so it also converts Unity back to glTF.</summary>
        public static Quaternion Rotation(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        /// <summary>The Unity quaternion for a rotation of <paramref name="radians"/> about <paramref name="glTFAxis"/> as glTF (right-handed) reads it.</summary>
        public static Quaternion AxisAngle(Vector3 glTFAxis, float radians)
        {
            var n = glTFAxis.normalized;
            var s = Mathf.Sin(radians * 0.5f);
            return new Quaternion(n.x * s, -n.y * s, -n.z * s, Mathf.Cos(radians * 0.5f));
        }
    }
}
