using UnityEngine;

namespace SdfPickups
{
    /// <summary>The two battle sides of a level: west (−x) and east (+x).</summary>
    public enum BattleSideId
    {
        West,
        East,
    }

    /// <summary>
    /// A side's flag stand, for capture the flag: which side it belongs to and its flag, the <c>Flag</c> child the stand holds when
    /// the flag is home. There are no capture-the-flag rules yet; this holds the data such a mode needs: the side, the flag, and the
    /// flag's home pose on the stand (recorded by the builder), so the flag can be carried off (re-parented) and put back.
    /// </summary>
    public sealed class FlagStand : MonoBehaviour
    {
        [Tooltip("The side whose flag this is")]
        public BattleSideId side;
        [Tooltip("The flag: the stand's Flag child when it is home; a player may carry it off")]
        public Transform flag;
        [Tooltip("The flag's position on the stand, in the stand's space: the pole's foot in the socket")]
        public Vector3 homePosition;
        [Tooltip("The flag's rotation on the stand, in the stand's space")]
        public Quaternion homeRotation = Quaternion.identity;

        /// <summary>The side's name as the level file writes it: west or east.</summary>
        public string SideName => SideNameOf(side);

        public static string SideNameOf(BattleSideId side) => side == BattleSideId.West ? "west" : "east";

        /// <summary>Whether the flag is on its stand: a child of it, at its home pose.</summary>
        public bool FlagAtHome =>
            flag != null && flag.parent == transform
                         && (flag.localPosition - homePosition).sqrMagnitude < 1e-6f
                         && Quaternion.Angle(flag.localRotation, homeRotation) < 0.1f;

        /// <summary>Puts the flag back on the stand, wherever it was.</summary>
        public void ReturnFlag()
        {
            if (flag == null)
                return;
            flag.SetParent(transform, false);
            flag.localPosition = homePosition;
            flag.localRotation = homeRotation;
            flag.localScale = Vector3.one;
        }
    }
}
