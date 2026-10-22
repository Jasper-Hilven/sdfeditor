using System;

namespace SdfBattle
{
    /// <summary>
    /// The parts of the boss that have health of their own (ticket 33): the Prime-copter's four spore sacs in phase 2 (200 each, they grow
    /// back two at a time when the daze ends with the bar not empty) and Prime's two hands in phase 3 (300 each, a burst hand grows back
    /// after 20 s). Plain data like <see cref="Health"/>; the scene registers the sacs and hands with <c>Combat.Register</c> so the
    /// electro-gun's bolt and the flame's cone find them, and <see cref="BossState"/> turns what they lose into the bar's damage.
    /// </summary>
    public sealed class BossParts
    {
        private readonly Health[] sacs;
        private readonly Health[] hands = new Health[2];
        private readonly float[] handRegrow = new float[2];

        public BossParts(BossRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            sacs = new Health[Math.Max(1, rules.sacCount)];
            for (var i = 0; i < sacs.Length; i++)
                sacs[i] = new Health(rules.sacHealth);
            for (var i = 0; i < hands.Length; i++)
                hands[i] = new Health(rules.handHealth);
        }

        public BossRules Rules { get; }

        public int SacCount => sacs.Length;

        public Health Sac(int index) => sacs[index];

        public bool SacAlive(int index) => sacs[index].Alive;

        /// <summary>Sacs still hanging under the copter.</summary>
        public int SacsAlive
        {
            get
            {
                var n = 0;
                foreach (var sac in sacs)
                    if (sac.Alive)
                        n++;
                return n;
            }
        }

        public Health Hand(int index) => hands[index];

        public bool HandAlive(int index) => hands[index].Alive;

        /// <summary>Seconds until a burst hand is back; 0 for a whole one.</summary>
        public float HandRegrowLeft(int index) => handRegrow[index];

        /// <summary>A sac took damage: its index and the amount taken. Raised before <see cref="SacBurst"/>.</summary>
        public event Action<int, float> SacHurt;

        /// <summary>A sac burst.</summary>
        public event Action<int> SacBurst;

        /// <summary>A hand took damage: its index and the amount taken.</summary>
        public event Action<int, float> HandHurt;

        /// <summary>A hand burst.</summary>
        public event Action<int> HandBurst;

        /// <summary>A burst hand grew back.</summary>
        public event Action<int> HandRegrown;

        /// <summary>Hurts a sac; returns what it took (no more than it had left).</summary>
        public float HurtSac(int index, float amount)
        {
            var taken = sacs[index].Hurt(amount);
            if (taken <= 0)
                return 0;
            SacHurt?.Invoke(index, taken);
            if (!sacs[index].Alive)
                SacBurst?.Invoke(index);
            return taken;
        }

        /// <summary>Hurts a hand; returns what it took.</summary>
        public float HurtHand(int index, float amount)
        {
            var taken = hands[index].Hurt(amount);
            if (taken <= 0)
                return 0;
            HandHurt?.Invoke(index, taken);
            if (!hands[index].Alive)
            {
                handRegrow[index] = Rules.handRegrowSeconds;
                HandBurst?.Invoke(index);
            }
            return taken;
        }

        /// <summary>Grows <paramref name="count"/> burst sacs back at full health (the first burst ones); returns how many grew.</summary>
        public int RegrowSacs(int count)
        {
            var grown = 0;
            for (var i = 0; i < sacs.Length && grown < count; i++)
            {
                if (sacs[i].Alive)
                    continue;
                sacs[i].Reset();
                grown++;
            }
            return grown;
        }

        /// <summary>Lets <paramref name="seconds"/> pass: burst hands grow back.</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0)
                return;
            for (var i = 0; i < hands.Length; i++)
            {
                if (hands[i].Alive || handRegrow[i] <= 0)
                    continue;
                handRegrow[i] = Math.Max(0, handRegrow[i] - seconds);
                if (handRegrow[i] <= 0)
                {
                    hands[i].Reset();
                    HandRegrown?.Invoke(i);
                }
            }
        }

        /// <summary>Everything whole again (a phase start, a checkpoint).</summary>
        public void Reset()
        {
            foreach (var sac in sacs)
                sac.Reset();
            foreach (var hand in hands)
                hand.Reset();
            Array.Clear(handRegrow, 0, handRegrow.Length);
        }
    }
}
