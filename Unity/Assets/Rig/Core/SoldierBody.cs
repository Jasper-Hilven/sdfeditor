using UnityEngine;

namespace SdfRig
{
    /// <summary>
    /// The numbers and names of the soldier's body (ticket 43), from content/models/characters/soldier/soldier.sdf on the Synty skeleton.
    /// The body is headless: the monster head (ticket 45) goes on <see cref="HeadTopSocket"/> and the <see cref="HeadBone"/>.
    /// <para>
    /// Size: the body keeps the model's own scale (1:1). Its neck ends at <see cref="NeckTop"/>, the collar at <see cref="Shoulders"/>; the
    /// player's capsule is 1.75 m with the eye at about 1.5 m (CombatState.BodyHeight), so a head of about 0.25 m on the neck brings the
    /// body to <see cref="CrownWithHead"/>, within 5 cm of the capsule's top. Combat's headshot rule (a hit above the table's head height,
    /// plus the head ellipsoid at <c>headCentre</c>) is not changed by the body; ticket 45 sizes it per head.
    /// </para>
    /// </summary>
    public static class SoldierBody
    {
        /// <summary>The Synty bone the headshot capsule is on: the capsule's <c>RigHit.Bone</c>.</summary>
        public const string HeadBone = "Head";

        /// <summary>Socket transforms (children of their bone, named socket_&lt;name&gt; by the rig import).</summary>
        public const string GripSocket = "socket_grip", SupportSocket = "socket_support", BackSocket = "socket_back",
            HeadTopSocket = "socket_head_top", ChestSocket = "socket_chest";

        /// <summary>The socket names, as the model file writes them.</summary>
        public static readonly string[] Sockets = { "grip", "support", "back", "head_top", "chest" };

        /// <summary>The bone paths of the sockets below the prefab's root.</summary>
        public const string HandR = "Root/Hips/Spine_01/Spine_02/Spine_03/Clavicle_R/Shoulder_R/Elbow_R/Hand_R",
            HandL = "Root/Hips/Spine_01/Spine_02/Spine_03/Clavicle_L/Shoulder_L/Elbow_L/Hand_L",
            Spine03 = "Root/Hips/Spine_01/Spine_02/Spine_03",
            Head = "Root/Hips/Spine_01/Spine_02/Spine_03/Neck/Head";

        /// <summary>Metres above the soles: the shoulders, the neck's top (the head joint) and the head socket.</summary>
        public const float Shoulders = 1.39f, NeckTop = 1.569f, HeadTopHeight = 1.815f;

        /// <summary>The soldier with a monster head of about this height on the neck, to the crown.</summary>
        public const float CrownWithHead = NeckTop + 0.25f;

        /// <summary>The player capsule (CombatState.BodyHeight, BodyRadius) the body fits.</summary>
        public const float CapsuleHeight = 1.75f, CapsuleRadius = 0.3f;

        /// <summary>The bone path of a socket's parent below the root: where <c>socket_&lt;name&gt;</c> hangs.</summary>
        public static string BonePathOf(string socket)
        {
            switch (socket)
            {
                case "grip": return HandR;
                case "support": return HandL;
                case "back":
                case "chest": return Spine03;
                case "head_top": return Head;
                default: return null;
            }
        }

        /// <summary>The socket <paramref name="name"/> ("grip") of a soldier (its object <c>socket_grip</c>, a child of the bone), or null.</summary>
        public static Transform FindSocket(Transform soldier, string name)
        {
            if (soldier == null || string.IsNullOrEmpty(name))
                return null;
            var path = BonePathOf(name);
            var bone = path == null ? null : soldier.Find(path);
            return bone != null ? bone.Find("socket_" + name) : null;
        }

        /// <summary>The hit capsule of the head (the <see cref="RigHit"/> whose bone is <see cref="HeadBone"/>), or null.</summary>
        public static CapsuleCollider HeadCapsule(Transform soldier)
        {
            if (soldier == null)
                return null;
            foreach (var hit in soldier.GetComponentsInChildren<RigHit>(true))
                if (hit.Bone == HeadBone)
                    return hit.GetComponent<CapsuleCollider>();
            return null;
        }
    }
}
