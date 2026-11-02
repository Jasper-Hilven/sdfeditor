using SdfEffects;
using SdfGui;
using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The spores in a battle scene (ticket 25), on the Battle object next to the <see cref="Match"/>, which makes it at the start: the
    /// rules (<see cref="State"/>, <see cref="SporeState"/>), your keys, the bots' choice, what everyone sees of a power, and the HUD
    /// (<see cref="SporeHud"/>).
    /// <list type="bullet">
    /// <item>Your keys by their place on the keyboard (<see cref="GameKeys"/>): Q Frenzy, F Regenerate, C Thick Hide (A, F, C
    /// on AZERTY), only in play with the pointer locked; with the meter not full a press does nothing.</item>
    /// <item>A bot with a full meter looks every half second: Frenzy on a flag run, Regenerate when hurt, Thick Hide fighting close
    /// (<see cref="SporeRules.ChooseForBot"/>).</item>
    /// <item>The look, through the combat's effect particles: a burst of rage steam from the head on use; Frenzy leaves hot
    /// speed streaks behind the monster, Regenerate sends a green pulse ring up from its feet, Thick Hide swells it with grey scale glints.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-85)]
    public sealed class Spores : MonoBehaviour
    {
        public static readonly Color FrenzyColour = GuiPalette.Rage;
        public static readonly Color RegenerateColour = GuiPalette.Moss;
        public static readonly Color ThickHideColour = GuiPalette.Stone;

        /// <summary>Your keys, in the order of the powers (<see cref="SporeRules.All"/>).</summary>
        public static readonly KeyPlace[] Keys = { KeyPlace.Q, KeyPlace.F, KeyPlace.C };

        [Tooltip("The spores' numbers")]
        public SporeRules rules = new SporeRules();
        [Tooltip("Seconds between a bot's looks at whether to sporulate")]
        public float botThinkSeconds = 0.5f;
        [Tooltip("How much Thick Hide swells the scout: across, and up")]
        public Vector2 thickHideSwell = new Vector2(1.18f, 1.08f);

        private Match match;
        private Combat combat;
        private float botClock;
        private float[] trailClock = new float[0];
        private float[] swell = new float[0];

        /// <summary>The spores of the active battle scene; null where there is none.</summary>
        public static Spores Current { get; private set; }

        public SporeState State { get; private set; }

        public Match Match => match;

        /// <summary>Makes the match's spores, after combat and the flags, before the first round.</summary>
        public static Spores Create(Match match, Combat combat)
        {
            var spores = match.GetComponent<Spores>();
            if (spores == null)
                spores = match.gameObject.AddComponent<Spores>();
            spores.match = match;
            spores.combat = combat;
            var flags = CaptureTheFlag.Current != null ? CaptureTheFlag.Current.State : null;
            spores.State = new SporeState(match.State, combat.State, flags, spores.rules);
            spores.State.Enabled = match.Setup == null || match.Setup.sporesOn;
            spores.State.Sporulated += spores.OnSporulated;
            spores.trailClock = new float[match.Players.Count];
            spores.swell = new float[match.Players.Count];
            // Made with the spores off too: it hides the HUD's spore pieces then (the GUI's HUD shows them unless told)
            SporeHud.Create(spores);
            Current = spores;
            return spores;
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
            if (State != null)
                State.Sporulated -= OnSporulated;
        }

        private void Update()
        {
            if (State == null)
                return;
            var dt = Time.deltaTime;
            State.Tick(dt);
            ReadYourKeys();
            botClock -= dt;
            if (botClock <= 0)
            {
                botClock = botThinkSeconds;
                ThinkForBots();
            }
            foreach (var player in match.Players)
                Show(player, dt);
        }

        private void ReadYourKeys()
        {
            var you = match.You;
            // As firing: only with the pointer locked, which a menu lets go
            if (you == null || Cursor.lockState != CursorLockMode.Locked || !State.CanSporulate(you.State))
                return;
            for (var i = 0; i < Keys.Length; i++)
                if (GameKeys.Pressed(Keys[i]))
                {
                    State.Sporulate(you.State, SporeRules.All[i]);
                    return;
                }
        }

        private void ThinkForBots()
        {
            var bots = Bots.Current;
            if (bots == null)
                return;
            var flags = CaptureTheFlag.Current != null ? CaptureTheFlag.Current.State : null;
            foreach (var bot in bots.All)
            {
                var player = bot.Player;
                if (player == null || !State.CanSporulate(player.State))
                    continue;
                var goal = bot.Decision.goal;
                var flagRun = (flags != null && flags.IsCarrying(player.State)) || goal == BotGoal.TakeFlag || goal == BotGoal.BringFlagHome;
                var enemy = bot.Target != null && bot.Target.State.Alive
                    ? Vector3.Distance(bot.Target.transform.position, player.transform.position)
                    : float.PositiveInfinity;
                var power = rules.ChooseForBot(flagRun, player.State.Health, player.State.MaxHealth, enemy);
                if (power.HasValue)
                    State.Sporulate(player.State, power.Value);
            }
        }

        // ---------------------------------------------------------------- The look

        public static Color ColourOf(SporePower power) => power switch
        {
            SporePower.Frenzy => FrenzyColour,
            SporePower.Regenerate => RegenerateColour,
            _ => ThickHideColour,
        };

        private FxParticles Particles => combat != null && combat.Fx != null ? combat.Fx.Particles : null;

        /// <summary>A burst of steam and sparks from the head, in the power's colour.</summary>
        private void OnSporulated(PlayerState state, SporePower power)
        {
            var player = match.PlayerOf(state);
            var particles = Particles;
            if (player == null || particles == null)
                return;
            var colour = ColourOf(power);
            var cap = player.transform.position + Vector3.up * 1.4f;
            for (var i = 0; i < 24; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f + 0.2f;
                particles.Emit(FxParticles.Kind.Spark, cap + direction * 0.2f, direction * Random.Range(2f, 4.5f), Random.Range(0.3f, 0.6f),
                    Random.Range(0.03f, 0.06f), colour, Color.white, gravity: 2f, drag: 1.5f);
            }
            for (var i = 0; i < 6; i++)
                particles.Emit(FxParticles.Kind.Puff, cap, Random.insideUnitSphere * 0.8f, Random.Range(0.4f, 0.7f), Random.Range(0.25f, 0.4f),
                    colour, Color.white, gravity: -0.4f, drag: 3);
        }

        /// <summary>A player's power as others see it: Frenzy's streaks behind, Regenerate's rising green pulse, Thick Hide's swollen, glinting hide.</summary>
        private void Show(Player player, float dt)
        {
            var index = player.State.Index;
            var meter = State.MeterOf(player.State);
            var on = player.State.Alive && meter.Active;
            var particles = Particles;
            if (on && particles != null)
            {
                trailClock[index] -= dt;
                if (trailClock[index] <= 0)
                {
                    var feet = player.transform.position;
                    if (meter.Power == SporePower.Frenzy)
                    {
                        // A speed streak: a spark stretched along the way back, and now and then a puff of steam
                        trailClock[index] = 0.04f;
                        var side = player.transform.right * Random.Range(-0.3f, 0.3f);
                        var back = -player.transform.forward;
                        particles.Emit(FxParticles.Kind.Spark, feet + side + Vector3.up * Random.Range(0.2f, 1.4f), back * Random.Range(3f, 5f),
                            Random.Range(0.25f, 0.4f), Random.Range(0.05f, 0.08f), FrenzyColour, Color.white, drag: 1);
                        if (Random.value < 0.25f)
                            particles.Emit(FxParticles.Kind.Puff, feet + Vector3.up * 1.6f, Vector3.up * 0.6f, Random.Range(0.5f, 0.8f),
                                Random.Range(0.2f, 0.3f), Color.Lerp(FrenzyColour, Color.white, 0.5f), Color.white, gravity: -0.3f, drag: 2);
                    }
                    else if (meter.Power == SporePower.Regenerate)
                    {
                        // A green pulse: a ring of motes spreading from the feet, then rising
                        trailClock[index] = 0.6f;
                        for (var i = 0; i < 14; i++)
                        {
                            var angle = i / 14f * Mathf.PI * 2;
                            var ring = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                            particles.Emit(FxParticles.Kind.Spore, feet + ring * 0.2f + Vector3.up * 0.1f, ring * 1.2f + Vector3.up * 0.6f,
                                Random.Range(0.7f, 1f), Random.Range(0.04f, 0.06f), RegenerateColour, Color.white, gravity: -0.2f, drag: 1.5f);
                        }
                    }
                    else if (meter.Power == SporePower.ThickHide)
                    {
                        // A stony sheen: grey glints flashing over the hide
                        trailClock[index] = 0.1f;
                        var around = Random.onUnitSphere * 0.45f;
                        particles.Emit(FxParticles.Kind.Spark, feet + new Vector3(around.x, 0.9f + around.y, around.z), around.normalized * 0.4f,
                            Random.Range(0.15f, 0.3f), Random.Range(0.04f, 0.07f), ThickHideColour, Color.white);
                    }
                }
            }
            // Thick Hide swells the monster, in and out over a fifth of a second
            var target = on && meter.Power == SporePower.ThickHide ? 1 : 0;
            swell[index] = Mathf.MoveTowards(swell[index], target, dt * 5);
            if (player.BodyRoot != null)
            {
                var s = Vector2.Lerp(Vector2.one, thickHideSwell, swell[index] * swell[index] * (3 - 2 * swell[index]));
                var scale = new Vector3(s.x, s.y, s.x);
                if (player.BodyRoot.localScale != scale)
                    player.BodyRoot.localScale = scale;
            }
        }
    }
}
