using SdfRig;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>
    /// A monster head worn by a soldier (ticket 45): the head prefab placed on the soldier's <c>head_top</c> socket (ticket 43), on the Head
    /// bone, so it nods and turns with the animation. It is not rigged. Its origin is the socket and it faces the soldier's forward, whatever
    /// the socket's own axes (the rotation is set from the soldier's at rest).
    /// <code>var worn = HeadWear.Attach(soldier.transform, MonsterHeads.RandomBuiltId(count => Random.Range(0, count)));</code>
    /// </summary>
    public sealed class HeadWear : MonoBehaviour
    {
        public string Id { get; private set; }

        /// <summary>Where a shot counts as the head's, in the head's frame (<see cref="HeadHit.Fit(Bounds)"/> of the model's bounds).</summary>
        public HeadShape Shape { get; private set; }

        /// <summary>The height of the shape's centre over the soldier's feet at rest: the head socket's, plus the centre in the head's frame.</summary>
        public float CentreHeight => SoldierBody.HeadTopHeight + Shape.Centre.y;

        /// <summary>
        /// Puts the head <paramref name="id"/> on the soldier's head socket and resizes the soldier's Head hit capsule to it if it has one;
        /// returns the worn head, or null when the soldier has no socket, the id is unknown or the heads were not imported.
        /// </summary>
        public static HeadWear Attach(Transform soldier, string id)
        {
            var models = MonsterHeadModels.Load();
            if (soldier == null || models == null || !models.TryGet(id, out var entry))
                return null;
            return Attach(soldier, id, entry.prefab, MonsterHeadModels.ShapeOf(entry));
        }

        /// <summary>The same with the prefab and shape given (the editor's capture, and the tests).</summary>
        public static HeadWear Attach(Transform soldier, string id, GameObject prefab, HeadShape shape)
        {
            var socket = SoldierBody.FindSocket(soldier, "head_top");
            if (socket == null || prefab == null)
                return null;
            var head = Instantiate(prefab, socket, false);
            head.name = "Head_" + id;
            head.transform.localPosition = Vector3.zero;
            // Face the soldier's front and stand upright, whatever the axes of the bone and socket
            head.transform.rotation = soldier.rotation;
            var worn = head.AddComponent<HeadWear>();
            worn.Id = id;
            worn.Shape = shape;
            var capsule = SoldierBody.HeadCapsule(soldier);
            if (capsule != null)
                HeadHit.FitCapsule(capsule, shape, head.transform);
            return worn;
        }
    }
}
