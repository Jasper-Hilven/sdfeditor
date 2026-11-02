using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>What a head burst is, handed to a <see cref="HeadBurstProfile"/> (ticket 46's plug-in point): the head, where and how it burst.</summary>
    public readonly struct HeadBurst
    {
        public HeadBurst(Gore gore, string headId, Vector3 position, Quaternion rotation, Vector3 away, Color blood, int level, string weapon, bool headshot)
        {
            Gore = gore; HeadId = headId; Position = position; Rotation = rotation; Away = away; Blood = blood; Level = level; Weapon = weapon; Headshot = headshot;
        }

        public Gore Gore { get; }
        /// <summary>The head's id in <see cref="SdfHeads.MonsterHeads"/>: yeti, pumpkin, ...</summary>
        public string HeadId { get; }
        /// <summary>The middle of the head, world.</summary>
        public Vector3 Position { get; }
        /// <summary>The head's own pose (forward is the way it faced).</summary>
        public Quaternion Rotation { get; }
        /// <summary>The way the hit pushed, flat unit vector; zero when unknown.</summary>
        public Vector3 Away { get; }
        /// <summary>The head's blood colour (<see cref="GoreRules.BloodOf"/>).</summary>
        public Color Blood { get; }
        /// <summary>The gore level (<see cref="GoreRules"/>); never Off here, the puff has no profile.</summary>
        public int Level { get; }
        public string Weapon { get; }
        public bool Headshot { get; }
        public float Strength => GoreRules.Strength(Weapon);
    }

    /// <summary>
    /// A head's own burst on top of the generic one (ticket 46 plugs its per-head debris and specials in here: ice shards, bats, shamrocks,
    /// mud, tentacles ...): register one per head id with <see cref="Register"/> and <see cref="Gore"/> calls it for every burst of that
    /// head at Splatty and up. The generic burst (the head's own mesh cut into pieces, its blood in its own colour, gobs and splats) needs
    /// none: a head with no profile bursts that way. The body never takes part.
    /// <code>HeadBurstProfile.Register("yeti", new IceShards());   // class IceShards : HeadBurstProfile { public override void Special(HeadBurst burst) {...} }</code>
    /// </summary>
    public abstract class HeadBurstProfile
    {
        private static readonly Dictionary<string, HeadBurstProfile> Profiles = new Dictionary<string, HeadBurstProfile>();

        /// <summary>Whether the head's own cut pieces are left out (the profile throws its debris instead).</summary>
        public virtual bool ReplacesPieces => false;

        /// <summary>Called right after the generic burst started, the same frame.</summary>
        public virtual void Special(HeadBurst burst)
        {
        }

        /// <summary>The profile of a head, or null (the generic burst).</summary>
        public static HeadBurstProfile Of(string headId) => headId != null && Profiles.TryGetValue(headId, out var profile) ? profile : null;

        public static void Register(string headId, HeadBurstProfile profile)
        {
            if (profile == null)
                Profiles.Remove(headId);
            else
                Profiles[headId] = profile;
        }
    }
}
