using SdfBattle;
using UnityEngine;
using UnityEngine.AI;

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

        private Bounds? localBounds;
        private NavMeshObstacle obstacle;

        /// <summary>Metres a car's carved hole is longer than the car at each end, as the hole follows a moving car in steps.</summary>
        public const float CarveLead = 0.4f;

        /// <summary>
        /// The box round the car's solid colliders in the car's own frame (the engine of the toy train: 1.8 wide, 2.6 tall, 4.8 long);
        /// the renderers' if it has no collider. Computed on first use, from the car as built.
        /// </summary>
        public Bounds LocalBounds
        {
            get
            {
                if (localBounds == null)
                    localBounds = ComputeLocalBounds();
                return localBounds.Value;
            }
        }

        private Bounds ComputeLocalBounds()
        {
            var found = false;
            var bounds = new Bounds();
            void Add(Vector3 worldPoint)
            {
                var local = transform.InverseTransformPoint(worldPoint);
                if (!found)
                    bounds = new Bounds(local, Vector3.zero);
                else
                    bounds.Encapsulate(local);
                found = true;
            }
            void AddBox(Transform from, Vector3 centre, Vector3 size)
            {
                for (var i = 0; i < 8; i++)
                    Add(from.TransformPoint(centre + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger)
                    continue;
                if (collider is MeshCollider mesh && mesh.sharedMesh != null)
                    AddBox(mesh.transform, mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.size);
                else if (collider is BoxCollider box)
                    AddBox(box.transform, box.center, box.size);
            }
            if (!found)
                foreach (var renderer in GetComponentsInChildren<Renderer>())
                    AddBox(renderer.transform, renderer.localBounds.center, renderer.localBounds.size);
            return found ? bounds : new Bounds(new Vector3(0, 1, 0), new Vector3(2, 2, 4));
        }

        /// <summary>
        /// A carving <see cref="NavMeshObstacle"/> round the car while the mover drives it (ticket 27.10): the navmeshes (the scouts' and
        /// the tanks') are cut where it is, so no path leads through a car; the train itself is not in the baked mesh. Longer than the
        /// car at both ends (<see cref="CarveLead"/>), as the hole is cut again only after the car has moved a bit.
        /// </summary>
        public void Carve(bool on)
        {
            if (obstacle == null)
            {
                if (!on)
                    return;
                obstacle = GetComponent<NavMeshObstacle>();
                if (obstacle == null)
                    obstacle = gameObject.AddComponent<NavMeshObstacle>();
                var bounds = LocalBounds;
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = bounds.center;
                obstacle.size = bounds.size + new Vector3(0, 0, CarveLead * 2);
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                obstacle.carvingMoveThreshold = 0.3f;
            }
            obstacle.enabled = on;
        }

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
