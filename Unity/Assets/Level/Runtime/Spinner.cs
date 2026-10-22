using SdfBattle;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// Turns the object it is on about an axis at a steady pace, with no physics (ticket 30.5: a case fan's blades, the cooler's and the graphics
    /// card's fans). A model's part is meshed in the model's space, so its object sits at the model's origin; the turn is about <see cref="pivot"/>, the
    /// middle of its mesh (a disc of blades is the same all round, so the middle of its bounds is its hub), and the object is moved to keep that point still.
    /// The angle comes from the clock (<see cref="SpinRules.Angle"/>), so the levels of detail of one part, each an object of its own, turn together.
    /// <see cref="LevelSpinners"/> puts one on every part the naming rule (<see cref="SpinRules"/>) knows.
    /// </summary>
    public sealed class Spinner : MonoBehaviour
    {
        [Tooltip("The turning axis in the object's own space")]
        public Vector3 axis = Vector3.forward;
        [Tooltip("Degrees a second; negative turns the other way")]
        public float degreesPerSecond = 45;
        [Tooltip("Degrees it is turned by at clock 0")]
        public float phase;
        [Tooltip("The point that stays still, in the object's own space; set from the mesh's bounds when it is zero and there is a mesh")]
        public Vector3 pivot;

        private Vector3 restPosition;
        private Quaternion restRotation;

        /// <summary>Sets the spin from a rule of <see cref="SpinRules"/>.</summary>
        public void Apply(SpinRule rule)
        {
            axis = new Vector3(rule.axisX, rule.axisY, rule.axisZ);
            degreesPerSecond = rule.degreesPerSecond;
            phase = rule.phaseDegrees;
        }

        private void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            if (pivot == Vector3.zero && GetComponent<MeshFilter>() is MeshFilter filter && filter.sharedMesh != null)
                pivot = filter.sharedMesh.bounds.center;
        }

        private void Update()
        {
            var angle = SpinRules.Angle(new SpinRule(axis.x, axis.y, axis.z, degreesPerSecond, phase), Time.time);
            var turn = Quaternion.AngleAxis(angle, axis.normalized);
            transform.localRotation = restRotation * turn;
            transform.localPosition = restPosition + restRotation * (pivot - turn * pivot);
        }
    }
}
