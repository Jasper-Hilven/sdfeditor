using System;
using SdfGui;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The data pulses of a level (ticket 30.9, the computer's twist): bands of light that run along the buses from the RAM to the CPU,
    /// drawn by one overlay mesh on the SdfPulse shader (built from level.json by the level builder, no work here) and played by
    /// <see cref="PulseRules"/>. Every frame this gives each scout (you and the bots) that is on a bus within reach of a band the speed-up
    /// of <see cref="PulseBoost"/>, as the controller's <c>boostMultiplier</c> (it multiplies with the flag carrier's slow and a blob's), a
    /// gold streak at the feet, and for you the "Overclocked" tag (<see cref="PulseHud"/>). Machines are not boosted. A dead scout loses
    /// it. The clock is the scene's time, which the shader's <c>_Time.y</c> is too, so the boost is where the bands are drawn.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class DataPulses : MonoBehaviour
    {
        /// <summary>A pulse line as the level builder saves it: corners (x, z in Unity's coordinates) and the trace's width.</summary>
        [Serializable]
        public struct Line
        {
            public Vector2[] points;
            public float width;
        }

        [Tooltip("The pulses' numbers, from level.json")]
        public PulseRules rules = new PulseRules();
        public Line[] lines = new Line[0];
        [Tooltip("Seconds between a boosted scout's streaks")]
        public float streakSeconds = 0.05f;

        public static readonly Color StreakColour = GuiPalette.Gold;

        private PulseLine[] pulseLines = new PulseLine[0];
        private PulseBoost[] boosts = new PulseBoost[0];
        private float[] streakClock = new float[0];
        private Vector3[] lastFeet = new Vector3[0];
        private PulseHud hud;
        private bool hudTried;

        /// <summary>The pulses of the active scene; null where there are none.</summary>
        public static DataPulses Current { get; private set; }

        /// <summary>The clock the bands and the boost run by: the scene's time, as the shader's <c>_Time.y</c>.</summary>
        public static float Clock => Time.timeSinceLevelLoad;

        /// <summary>Whether a scout's boost is on, for the tests and the HUD.</summary>
        public bool IsBoosted(int player) => player < boosts.Length && boosts[player].Active(Clock);

        private void OnEnable()
        {
            Current = this;
            pulseLines = new PulseLine[lines.Length];
            for (var i = 0; i < lines.Length; i++)
                pulseLines[i] = new PulseLine(lines[i].points, lines[i].width);
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        private void Update()
        {
            var match = Match.Current;
            if (match == null || match.State == null || pulseLines.Length == 0)
                return;
            var players = match.Players;
            if (boosts.Length != players.Count)
            {
                boosts = new PulseBoost[players.Count];
                streakClock = new float[players.Count];
                lastFeet = new Vector3[players.Count];
            }
            var time = Clock;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.State == null || player.Controller == null)
                    continue;
                if (!player.State.Alive || !player.Controller.enabled)
                {
                    boosts[i].Clear();
                    player.Controller.boostMultiplier = 1;
                    continue;
                }
                var feet = player.transform.position;
                if (rules.IsBoosted(pulseLines, new Vector2(feet.x, feet.z), time))
                    boosts[i].Refresh(time, rules.boostSeconds);
                player.Controller.boostMultiplier = boosts[i].Multiplier(time, rules.boost);
                if (boosts[i].Active(time))
                    Streak(i, feet, Time.deltaTime);
                lastFeet[i] = feet;
            }
            if (hud == null && !hudTried && match.Hud != null)
            {
                hudTried = true;
                hud = PulseHud.Create(match);
            }
            if (hud != null)
            {
                var you = match.You;
                var index = you != null && you.State != null ? you.State.Index : -1;
                hud.Show(index >= 0 && index < boosts.Length && boosts[index].Active(time), rules.boost);
            }
        }

        /// <summary>A short gold streak at the feet, thrown back along the way the scout came.</summary>
        private void Streak(int index, Vector3 feet, float dt)
        {
            streakClock[index] -= dt;
            if (streakClock[index] > 0)
                return;
            streakClock[index] = streakSeconds;
            var fx = Combat.Current != null && Combat.Current.Fx != null ? Combat.Current.Fx.Particles : null;
            if (fx == null)
                return;
            var back = lastFeet[index] - feet;
            back.y = 0;
            back = back.sqrMagnitude > 1e-8f ? back.normalized : Vector3.zero;
            var side = new Vector3(back.z, 0, -back.x) * UnityEngine.Random.Range(-0.15f, 0.15f);
            fx.Emit(SdfEffects.FxParticles.Kind.Spark, feet + side + Vector3.up * 0.06f, back * UnityEngine.Random.Range(2f, 4f) + Vector3.up * 0.3f,
                UnityEngine.Random.Range(0.18f, 0.3f), 0.05f, StreakColour, Color.white);
        }
    }
}
