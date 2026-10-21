using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// What a bot can see (ticket 23): an enemy within its sight range and its view cone, or within its hearing range whatever way it
    /// looks; the line of sight is the scene's to check (a ray). Plain math.
    /// </summary>
    public static class BotSight
    {
        /// <summary>Whether <paramref name="target"/> is in the view of an eye at <paramref name="eye"/> looking along <paramref name="forward"/>.</summary>
        public static bool InView(Vector3 eye, Vector3 forward, Vector3 target, BotSkill skill)
        {
            var to = target - eye;
            var distance = to.magnitude;
            if (distance <= skill.HearingRange)
                return true;
            if (distance > skill.SightRange)
                return false;
            // The cone is measured level: looking down at the ground does not blind it to what is ahead
            var level = new Vector3(forward.x, 0, forward.z);
            var flat = new Vector3(to.x, 0, to.z);
            if (level.sqrMagnitude < 1e-6f || flat.sqrMagnitude < 1e-6f)
                return true;
            return Vector3.Angle(level, flat) <= skill.ViewHalfAngle;
        }
    }

    /// <summary>
    /// Where a bot last saw each player (ticket 23), for <see cref="BotSkill.MemorySeconds"/>: it goes after where an enemy was when it
    /// lost sight of them. Indexed by roster index.
    /// </summary>
    public sealed class BotMemory
    {
        private readonly Vector3[] positions;
        private readonly float[] seenAt;

        public BotMemory(int players, float seconds)
        {
            positions = new Vector3[players];
            seenAt = new float[players];
            Seconds = seconds;
            Forget();
        }

        public float Seconds { get; set; }

        public void Seen(int player, Vector3 position, float now)
        {
            if (player < 0 || player >= positions.Length)
                return;
            positions[player] = position;
            seenAt[player] = now;
        }

        /// <summary>Where the player was last seen, if that is no longer ago than the memory lasts.</summary>
        public bool Recall(int player, float now, out Vector3 position)
        {
            position = default;
            if (player < 0 || player >= positions.Length || now - seenAt[player] > Seconds)
                return false;
            position = positions[player];
            return true;
        }

        /// <summary>Seconds since the player was last seen (infinity if never or forgotten).</summary>
        public float SinceSeen(int player, float now) => player < 0 || player >= seenAt.Length ? float.PositiveInfinity : now - seenAt[player];

        public void Forget(int player)
        {
            if (player >= 0 && player < seenAt.Length)
                seenAt[player] = float.NegativeInfinity;
        }

        public void Forget()
        {
            for (var i = 0; i < seenAt.Length; i++)
                seenAt[i] = float.NegativeInfinity;
        }
    }
}
