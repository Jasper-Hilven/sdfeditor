using System;

namespace SdfAudio
{
    /// <summary>
    /// What the battle music needs to know about the moment, as plain numbers: the match's component gathers them a few times a second
    /// and hands them to <see cref="MusicIntensity"/>, which turns them into how much of the layered music should be heard.
    /// </summary>
    public readonly struct BattleMoment
    {
        public BattleMoment(bool alive, int enemiesNear, int enemiesInView, float secondsSinceShot, float secondsSinceHit, bool carryingFlag, bool flagChase)
        {
            Alive = alive;
            EnemiesNear = enemiesNear;
            EnemiesInView = enemiesInView;
            SecondsSinceShot = secondsSinceShot;
            SecondsSinceHit = secondsSinceHit;
            CarryingFlag = carryingFlag;
            FlagChase = flagChase;
        }

        /// <summary>You are alive (a dead player hears the music settle).</summary>
        public bool Alive { get; }

        /// <summary>Enemies (players and machines) within <see cref="MusicIntensity.NearMetres"/> of you.</summary>
        public int EnemiesNear { get; }

        /// <summary>Enemies within <see cref="MusicIntensity.ViewMetres"/> in front of you and in sight.</summary>
        public int EnemiesInView { get; }

        /// <summary>Seconds since you last fired.</summary>
        public float SecondsSinceShot { get; }

        /// <summary>Seconds since you were hurt or hurt someone.</summary>
        public float SecondsSinceHit { get; }

        /// <summary>You carry the enemy's flag.</summary>
        public bool CarryingFlag { get; }

        /// <summary>An enemy carries your team's flag (or it lies dropped): the chase is on.</summary>
        public bool FlagChase { get; }

        /// <summary>A quiet moment: nobody near, nothing fired, nothing carried.</summary>
        public static BattleMoment Calm => new BattleMoment(true, 0, 0, 999, 999, false, false);
    }

    /// <summary>
    /// The adaptive battle music's brain (plain C#, tested in EditMode). The music is four layers that play in sync: layer 1 always, layers
    /// 2 to 4 fading in as the fight heats up. <see cref="Target"/> rates a <see cref="BattleMoment"/> from 0 (calm) to 1 (everything at
    /// once); <see cref="Level"/> follows it, rising within a couple of seconds and falling back over about twenty, so the music does not
    /// flicker between shots; <see cref="LayerShare"/> says how loud each layer is at a level.
    /// </summary>
    public sealed class MusicIntensity
    {
        /// <summary>The number of layers a battle track has.</summary>
        public const int Layers = 4;

        /// <summary>Enemies this near (metres) count as near.</summary>
        public const float NearMetres = 20;

        /// <summary>Enemies this near (metres), in front of you and in sight, count as in view.</summary>
        public const float ViewMetres = 45;

        /// <summary>The level a layer starts fading in at (layer 1 is always on).</summary>
        public static readonly float[] Enter = { 0, 0.15f, 0.4f, 0.65f };

        /// <summary>How much of the level a layer takes to go from silent to full.</summary>
        public const float FadeWidth = 0.2f;

        /// <summary>How fast <see cref="Level"/> rises, a second.</summary>
        public float attackPerSecond = 0.5f;

        /// <summary>How fast <see cref="Level"/> falls, a second.</summary>
        public float releasePerSecond = 0.05f;

        /// <summary>The smoothed intensity, 0 to 1.</summary>
        public float Level { get; private set; }

        /// <summary>The intensity a moment asks for, 0 to 1.</summary>
        public static float Target(in BattleMoment moment)
        {
            if (!moment.Alive)
                return 0.1f;
            var target = 0f;
            if (moment.EnemiesInView > 0)
                target += 0.25f + 0.1f * (Math.Min(moment.EnemiesInView, 4) - 1);
            if (moment.EnemiesNear > 0)
                target += 0.15f + 0.05f * (Math.Min(moment.EnemiesNear, 3) - 1);
            target += 0.25f * Fresh(moment.SecondsSinceShot, 4);
            target += 0.3f * Fresh(moment.SecondsSinceHit, 6);
            if (moment.FlagChase)
                target = Math.Max(target, 0.5f);
            if (moment.CarryingFlag)
                target = Math.Max(target, 0.7f);
            return Clamp01(target);
        }

        /// <summary>1 right now, falling to 0 over <paramref name="window"/> seconds.</summary>
        private static float Fresh(float seconds, float window) => Clamp01(1 - seconds / window);

        /// <summary>Moves <see cref="Level"/> towards the moment's target by <paramref name="seconds"/>.</summary>
        public void Tick(float seconds, in BattleMoment moment)
        {
            var target = Target(moment);
            Level = target > Level ? Math.Min(target, Level + attackPerSecond * seconds) : Math.Max(target, Level - releasePerSecond * seconds);
        }

        /// <summary>Back to calm (a new match).</summary>
        public void Reset() => Level = 0;

        /// <summary>How loud layer <paramref name="layer"/> (0 to 3) is at <paramref name="level"/>, 0 to 1: layer 0 always full, the others eased in.</summary>
        public static float LayerShare(int layer, float level)
        {
            if (layer <= 0)
                return 1;
            var t = Clamp01((level - Enter[Math.Min(layer, Layers - 1)]) / FadeWidth);
            return t * t * (3 - 2 * t);
        }

        private static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
