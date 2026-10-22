using UnityEngine;

namespace SdfMachines
{
    /// <summary>
    /// What the Prime-copter prefab (Assets/Machines/Prefabs/primecopter.prefab, ticket 33.5) is made of, set by the editor's
    /// PrimeCopterBuilder, so the boss's behaviour finds its pieces without looking them up by name. Data only: nothing here moves
    /// anything. It is 24's helicopter at seven times (see <see cref="MachineRig"/> for the way pieces hang from pivot objects with the
    /// piece's model under them as "Model"): <see cref="body"/> is the root piece (its origin on the ground under the middle, the
    /// skids' underside), <see cref="rotor"/> spins about local y, <see cref="tailRotor"/> about local x, and the four
    /// <see cref="sacs"/> hang from the rim's sockets (their pivots are their tops, so scaling one scales it about its socket).
    /// The two <see cref="eyes"/> are the machines' eye part, each looking along its local +z at rest, one mirrored (negative x scale).
    /// </summary>
    public sealed class PrimeCopterRig : MonoBehaviour
    {
        /// <summary>The body: the piece that carries the rest, and the one that leans into the flight.</summary>
        public Transform body;

        /// <summary>The rotor's pivot, the mast's top: spins about its local y.</summary>
        public Transform rotor;

        /// <summary>The tail rotor's pivot: spins about its local x.</summary>
        public Transform tailRotor;

        /// <summary>The four spore sacs' pivots (Sac0 to Sac3), under the rim, 2.5 m apart.</summary>
        public Transform[] sacs;

        /// <summary>The two floating eyes (eye_machine.0, eye_machine.1), under the body's model.</summary>
        public Transform[] eyes;

        /// <summary>Where Prime's left and right hand hold the runners: the middle of each runner, a little over it.</summary>
        public Transform gripLeft, gripRight;

        /// <summary>The hatch the tank drop opens: under the tail, behind Prime.</summary>
        public Transform hatch;

        /// <summary>Where the cap cannon's shots leave from: under the nose.</summary>
        public Transform muzzle;
    }
}
