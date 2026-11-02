using System;
using System.Collections.Generic;

namespace SdfHeads
{
    /// <summary>
    /// What a player has of the twelve heads (ticket 47), the part of the save the unlock rules read and write: the heads unlocked by wins,
    /// the months a match was played in (each keeps that month's head for good) and the head the player picked ("" before the first pick).
    /// Nothing here is ever removed.
    /// </summary>
    [Serializable]
    public sealed class HeadProgress
    {
        /// <summary>Ids of heads unlocked by winning, in the order they were won.</summary>
        public string[] unlocked = Array.Empty<string>();
        /// <summary>Months (1-12) in which a match was played.</summary>
        public int[] monthsSeen = Array.Empty<int>();
        /// <summary>The head the player picked last; empty until they pick.</summary>
        public string current = "";

        public void Normalise()
        {
            unlocked ??= Array.Empty<string>();
            monthsSeen ??= Array.Empty<int>();
            current ??= "";
        }
    }

    /// <summary>
    /// The head unlock rules (ticket 47), plain C# over the table of twelve (<see cref="MonsterHeads"/>), with the date given in (the device's
    /// local date), so a test can set any day. The head of the month is free while its month lasts and stays unlocked for good once a match
    /// was played in it; each win unlocks the next locked head in calendar order after the current month, wrapping at December; with all
    /// twelve unlocked a win unlocks nothing; a clock moved back takes nothing away.
    /// <code>HeadUnlocks.RecordPlayed(now, progress);   var won = HeadUnlocks.OnMatchWon(now, progress);   // the head unlocked, or null</code>
    /// </summary>
    public static class HeadUnlocks
    {
        /// <summary>Whether this head is yours at <paramref name="now"/>: its month is now or was played in, or a win unlocked it.</summary>
        public static bool IsAvailable(DateTime now, HeadProgress progress, string id)
        {
            var head = MonsterHeads.ById(id);
            if (head == null)
                return false;
            progress.Normalise();
            return head.Value.Month == now.Month
                || Array.IndexOf(progress.monthsSeen, head.Value.Month) >= 0
                || Array.IndexOf(progress.unlocked, id) >= 0;
        }

        /// <summary>The ids you can wear, in calendar order (January first).</summary>
        public static List<string> Available(DateTime now, HeadProgress progress)
        {
            var list = new List<string>();
            foreach (var head in MonsterHeads.All)
                if (IsAvailable(now, progress, head.Id))
                    list.Add(head.Id);
            return list;
        }

        /// <summary>The head of the month at <paramref name="now"/>.</summary>
        public static string HeadOfMonth(DateTime now) => MonsterHeads.All[now.Month - 1].Id;

        /// <summary>Notes that a match was played this month, which keeps the month's head for good. Returns whether anything changed.</summary>
        public static bool RecordPlayed(DateTime now, HeadProgress progress)
        {
            progress.Normalise();
            if (Array.IndexOf(progress.monthsSeen, now.Month) >= 0)
                return false;
            var months = new int[progress.monthsSeen.Length + 1];
            progress.monthsSeen.CopyTo(months, 0);
            months[months.Length - 1] = now.Month;
            progress.monthsSeen = months;
            return true;
        }

        /// <summary>
        /// A match was won: the month is recorded and the next locked head after the current month (wrapping) is unlocked. Returns its id, or
        /// null when all twelve were already yours.
        /// </summary>
        public static string OnMatchWon(DateTime now, HeadProgress progress)
        {
            RecordPlayed(now, progress);
            var count = MonsterHeads.Count;
            for (var step = 1; step <= count; step++)
            {
                var head = MonsterHeads.All[(now.Month - 1 + step) % count];
                if (IsAvailable(now, progress, head.Id))
                    continue;
                var list = new List<string>(progress.unlocked) { head.Id };
                progress.unlocked = list.ToArray();
                return head.Id;
            }
            return null;
        }

        /// <summary>The head you wear: the last one picked while it is yours, else the head of the month.</summary>
        public static string Current(DateTime now, HeadProgress progress)
        {
            progress.Normalise();
            return IsAvailable(now, progress, progress.current) ? progress.current : HeadOfMonth(now);
        }

        /// <summary>Picks a head to wear. False (nothing changed) when it is not an unlocked one.</summary>
        public static bool SetCurrent(DateTime now, HeadProgress progress, string id)
        {
            if (!IsAvailable(now, progress, id))
                return false;
            progress.current = id;
            return true;
        }

        /// <summary>The head before <paramref name="id"/> among those you can wear (wrapping); the same one when it is the only one.</summary>
        public static string Previous(DateTime now, HeadProgress progress, string id) => Step(now, progress, id, -1);

        /// <summary>The head after <paramref name="id"/> among those you can wear (wrapping).</summary>
        public static string Next(DateTime now, HeadProgress progress, string id) => Step(now, progress, id, 1);

        static string Step(DateTime now, HeadProgress progress, string id, int by)
        {
            var list = Available(now, progress);
            var at = list.IndexOf(id);
            if (at < 0)
                return list.Count > 0 ? list[0] : id;
            return list[((at + by) % list.Count + list.Count) % list.Count];
        }
    }

    /// <summary>
    /// Which heads the players of one match wear (ticket 47): yours is given, every other player draws at random from all twelve, locked ones
    /// too (which shows what is still to unlock), each different within the match while there are heads left.
    /// </summary>
    public sealed class HeadCast
    {
        private readonly Random random;
        private readonly List<string> used = new List<string>();

        public HeadCast(Random random) => this.random = random ?? new Random();

        /// <summary>The heads handed out so far.</summary>
        public IReadOnlyList<string> Used => used;

        /// <summary>Reserves a given head (yours).</summary>
        public void Reserve(string id)
        {
            if (!string.IsNullOrEmpty(id))
                used.Add(id);
        }

        /// <summary>A random head of the twelve, not yet used while one is left.</summary>
        public string Draw()
        {
            var free = new List<string>();
            foreach (var head in MonsterHeads.All)
                if (!used.Contains(head.Id))
                    free.Add(head.Id);
            string pick;
            if (free.Count > 0)
                pick = free[random.Next(free.Count)];
            else
                pick = MonsterHeads.All[random.Next(MonsterHeads.Count)].Id;
            used.Add(pick);
            return pick;
        }
    }

    /// <summary>The head the game puts on you, set by the campaign (null when there is none: the head of the month).</summary>
    public static class HeadSelection
    {
        public static string Yours { get; set; }
        private static HeadCast cast = new HeadCast(new Random());

        /// <summary>The cast of the match being made; <see cref="BeginMatch"/> starts a new one as a match makes its players.</summary>
        public static HeadCast Cast => cast;

        public static HeadCast BeginMatch(Random random = null) => cast = new HeadCast(random);
    }

    public static class HeadStandIn
    {
        /// <summary>
        /// The head to show for <paramref name="id"/>: itself when it is modelled, else the modelled head of the nearest month (the earlier one
        /// on a tie), until the other heads are built. Null when none is modelled.
        /// </summary>
        public static string Of(string id) => Of(id, h => h.Built);

        public static string Of(string id, Func<MonsterHead, bool> isBuilt)
        {
            var wanted = MonsterHeads.ById(id);
            if (wanted == null)
                return null;
            if (isBuilt(wanted.Value))
                return id;
            string best = null;
            var bestDistance = int.MaxValue;
            foreach (var head in MonsterHeads.All)
            {
                if (!isBuilt(head))
                    continue;
                var d = Math.Abs(head.Month - wanted.Value.Month);
                d = Math.Min(d, MonsterHeads.Count - d);
                if (d < bestDistance)
                {
                    best = head.Id;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>The name for the Settings cycle: the head's, with "(placeholder)" while its model is a stand-in.</summary>
        public static string Label(string id)
        {
            var head = MonsterHeads.ById(id);
            if (head == null)
                return "";
            return head.Value.Built ? head.Value.DisplayName : head.Value.DisplayName + " (placeholder)";
        }
    }
}
