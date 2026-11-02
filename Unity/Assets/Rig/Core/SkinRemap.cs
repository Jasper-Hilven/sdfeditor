using UnityEngine;

namespace SdfRig
{
    /// <summary>
    /// The skin as a colour swap (ticket 43): the soldier is meshed once in the olive base set, and each team skin moves every vertex's
    /// colour, by how much of each role's material the vertex is, to the skin's colour at the vertex's brightness relative to the base's.
    /// The hue is the skin's alone and the baked shading, occlusion and variation stay.
    /// </summary>
    public static class SkinRemap
    {
        /// <summary>The material roles a skin recolours (soldier_skin, the neck, stays).</summary>
        public static readonly string[] Roles = { "fatigues", "vest", "boots", "gloves", "pack" };

        /// <summary>The brightness of a linear colour.</summary>
        public static float Luma(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

        /// <summary>
        /// One vertex's colour (linear) moved to the skin. <paramref name="weights"/> holds the vertex's share (0 to 1) of each role,
        /// <paramref name="baseLinear"/> and <paramref name="skinLinear"/> the roles' linear colours in the base set and the skin. A vertex of
        /// no role is returned as it is.
        /// </summary>
        public static Vector3 Tint(Vector3 colour, float[] weights, Vector3[] baseLinear, Vector3[] skinLinear)
        {
            var luma = Luma(colour);
            var result = colour;
            for (var r = 0; r < baseLinear.Length; r++)
            {
                var w = weights[r];
                if (w <= 0)
                    continue;
                var target = skinLinear[r] * (luma / Mathf.Max(Luma(baseLinear[r]), 1e-3f));
                result += w * (target - colour);
            }
            return new Vector3(Mathf.Clamp01(result.x), Mathf.Clamp01(result.y), Mathf.Clamp01(result.z));
        }

        /// <summary>The share of a vertex that is the role's material: <paramref name="blend"/> toward the second of its two materials.</summary>
        public static float RoleShare(int firstId, int secondId, float blend, int roleId)
        {
            if (roleId < 0)
                return 0;
            var b = Mathf.Clamp01(blend);
            return Mathf.Clamp01((firstId == roleId ? 1 - b : 0) + (secondId == roleId ? b : 0));
        }
    }
}
