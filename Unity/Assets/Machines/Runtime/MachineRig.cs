using UnityEngine;

namespace SdfMachines
{
    /// <summary>
    /// What a machine prefab (Assets/Machines/Prefabs/tank-red.prefab, ...) is made of, set by the editor's MachineBuilder, so the
    /// behaviour (ticket 24.3) finds its moving pieces without looking them up by name. Data only: nothing here moves anything.
    /// <para>
    /// Each moving piece is an empty pivot object with the piece's model prefab under it (child "Model"), so turning the pivot's
    /// local rotation turns the piece about its own hinge:
    /// tank: <see cref="turn"/> = Body/Turret (about local y), <see cref="tilt"/> = Body/Turret/Barrel (about local x);
    /// helicopter: <see cref="turn"/> = Body/Rotor (spins about local y), <see cref="tilt"/> = Body/TailRotor (spins about local x);
    /// turret: <see cref="turn"/> = Foot/Head (about local y), <see cref="tilt"/> = Foot/Head/Barrel (about local x).
    /// The eyes are the scouts' eye at a lower detail (part eye_machine), two per machine, children of the piece that turns with them (the tank's turret, the
    /// turret's head, the helicopter's body); each looks along its local +z at rest, and one of them is mirrored (negative x scale).
    /// </para>
    /// </summary>
    public sealed class MachineRig : MonoBehaviour
    {
        /// <summary>tank, helicopter or turret.</summary>
        public string machine;

        /// <summary>red or blue.</summary>
        public string team;

        /// <summary>The machine set whose models it is made of (ticket 27.2): mushroom (turret-red, ...), toys (turret-toys-red, ...).</summary>
        public string set = "mushroom";

        /// <summary>The piece that does not move relative to the machine: the tank's and helicopter's Body, the turret's Foot.</summary>
        public Transform body;

        /// <summary>The turret (tank), the rotor (helicopter) or the head (turret): turns or spins about its local y.</summary>
        public Transform turn;

        /// <summary>The barrel (tank, turret: tilts about its local x) or the tail rotor (helicopter: spins about its local x).</summary>
        public Transform tilt;

        /// <summary>The two floating eyes (eye_machine.0, eye_machine.1), each looking along its local +z at rest.</summary>
        public Transform[] eyes;
    }
}
