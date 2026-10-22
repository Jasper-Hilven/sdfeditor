using SdfBattle;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// One car of a <see cref="PathMover"/>, on the car's root with its kinematic <see cref="Rigidbody"/>. Remembers the step it took this
    /// frame, so whoever stands on it can be carried the same way.
    /// </summary>
    public sealed class PathMoverCar : MonoBehaviour
    {
        /// <summary>The longest step in one frame that still counts as driving, in metres.</summary>
        public const float MaxStep = 6;

        private PathPose previous;
        private PathPose current;
        private bool placed;
        private int placedFrame = -1;

        /// <summary>The step the car took in the frame it last moved: from where it was to where it is.</summary>
        public bool HasStep => placed && placedFrame == Time.frameCount;

        public PathPose Pose => current;

        /// <summary>Puts the car at <paramref name="pose"/>; the step from where it was is what <see cref="Carry"/> reads.</summary>
        public void MoveTo(PathPose pose)
        {
            previous = placed ? current : pose;
            current = pose;
            placed = true;
            placedFrame = Time.frameCount;
            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            var body = GetComponent<Rigidbody>();
            if (body != null && body.isKinematic)
            {
                body.position = pose.Position;
                body.rotation = pose.Rotation;
            }
        }

        /// <summary>
        /// How far a player at <paramref name="standing"/> on this car is carried by its last step, and how many degrees about y it
        /// turns them; zero if the car did not move this frame.
        /// </summary>
        public Vector3 Carry(Vector3 standing, out float yawChange)
        {
            yawChange = 0;
            if (!HasStep)
                return Vector3.zero;
            var carried = PlatformCarry.Displacement(previous, current, standing);
            // A step this long is the clock going back (a new round), not the car driving: nobody is thrown with it
            if (carried.sqrMagnitude > MaxStep * MaxStep)
                return Vector3.zero;
            yawChange = PlatformCarry.YawChange(previous, current);
            return carried;
        }
    }
}
