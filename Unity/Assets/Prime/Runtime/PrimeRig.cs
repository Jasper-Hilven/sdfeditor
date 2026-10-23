using UnityEngine;

namespace SdfPrime
{
    /// <summary>
    /// What the Chanterelle Prime prefab (Assets/Prime/Prefabs/chanterelle-prime.prefab, ticket 33.1) is made of, set by the editor's
    /// PrimeBuilder, so the boss's behaviour and the story beats find his moving pieces without looking them up by name. Data only:
    /// nothing here moves anything. The prefab is Prime at scout scale, 1.41 m tall; the boss scene scales its root by 4.6.
    /// <para>
    /// Each moving piece is an empty pivot object with the piece's model prefab under it (child "Model"), so turning the pivot's local
    /// rotation turns the piece about its own hinge, as in <c>MachineRig</c>:
    /// <see cref="stem"/> = Stem (the root of the body: squash and stretch, lean, bob), <see cref="cap"/> = Stem/Cap (on the stem's top:
    /// tilts forward 35 degrees to kneel, lifts for the slam), <see cref="crown"/> = Stem/Cap/Crown (detaches in phase 3),
    /// <see cref="cape"/> = Stem/Cap/Cape (on the cap's back rim: swings about x and z), <see cref="medal"/> = Stem/Medal (swings
    /// about x and z). The eyes and hands are the scouts' (parts eye and hand, chanterelle flesh) in the stem's model, each looking or
    /// pointing along its local +z at rest, one of each pair mirrored (negative x scale).
    /// </para>
    /// </summary>
    public sealed class PrimeRig : MonoBehaviour
    {
        /// <summary>The body: the piece that does not hinge on another.</summary>
        public Transform stem;

        /// <summary>The cap's pivot, where its neck meets the stem.</summary>
        public Transform cap;

        /// <summary>The crown's pivot, the middle of the crown's base on the cap's dip.</summary>
        public Transform crown;

        /// <summary>The cape's pivot, the middle of its top edge on the cap's back rim.</summary>
        public Transform cape;

        /// <summary>The medal's pivot, the top of its ribbon on the stem's front.</summary>
        public Transform medal;

        /// <summary>The two floating eyes (eye.0, eye.1), under the stem's model.</summary>
        public Transform[] eyes;

        /// <summary>The two floating hands (hand.0, hand.1), under the stem's model.</summary>
        public Transform[] hands;
    }
}
