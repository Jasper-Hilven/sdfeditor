using UnityEngine;

namespace SdfPickups
{
    /// <summary>
    /// Holds an object floating over its parent's origin and turns it about the vertical axis: <see cref="baseHeight"/> (the top of the
    /// pod it floats over) plus <see cref="hoverHeight"/> up, at <see cref="revolutionsPerMinute"/> (15 rpm, the default, is 90 degrees a second),
    /// with a bob on top. The weapon's model is a child placed so its middle is on this object's origin, so it turns on the
    /// spot. The pose comes from the serialized fields every frame, so the inspector changes it live.
    /// </summary>
    public sealed class SpinHover : MonoBehaviour
    {
        [Tooltip("Revolutions per minute about the vertical axis; 15 is one turn in four seconds")]
        public float revolutionsPerMinute = 15;
        [Tooltip("Metres above the pod's top to the middle of the floating object")]
        public float hoverHeight = 1.2f;
        [Tooltip("Metres from the parent's origin to the pod's top; set by the pod builder from the pod's mesh")]
        public float baseHeight = 0.3f;
        [Tooltip("Half the distance of the bob up and down, in metres; 0 for none")]
        public float bobAmplitude = 0.08f;
        [Tooltip("Seconds for one bob")]
        public float bobPeriod = 2;

        private float angle;

        private void OnEnable() => Apply(0);

        private void OnValidate() => Apply(0);

        private void Update()
        {
            angle = (angle + revolutionsPerMinute * 6 * Time.deltaTime) % 360;
            Apply(Time.time);
        }

        private void Apply(float time)
        {
            var bob = bobPeriod > 0 ? bobAmplitude * Mathf.Sin(time * 2 * Mathf.PI / bobPeriod) : 0;
            transform.localPosition = new Vector3(0, baseHeight + hoverHeight + bob, 0);
            transform.localRotation = Quaternion.Euler(0, angle, 0);
        }
    }
}
