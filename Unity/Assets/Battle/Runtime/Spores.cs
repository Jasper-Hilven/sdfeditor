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
    /// <item>Your keys by their place on the keyboard (<see cref="GameKeys"/>): Q Mycelium Rush, F Regrowth, C Puffball Armour (A, F, C
    /// on AZERTY), only in play with the pointer locked; with the meter not full a press does nothing.</item>
    /// <item>A bot with a full meter looks every half second: Rush on a flag run, Regrowth when hurt, Armour fighting close
    /// (<see cref="SporeRules.ChooseForBot"/>).</item>
    /// <item>The look, through the combat's effect particles: a puff of spores from the cap on sporulating; Rush leaves glowing
    /// mycelium motes on the ground behind the scout, Regrowth sends green spores up from it, Armour swells its cap.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-85)]
    public sealed class Spores : MonoBehaviour
    {
        public static readonly Color RushColour = GuiPalette.Gold;
        public static readonly Color RegrowthColour = GuiPalette.Moss;
        public static readonly Color ArmourColour = GuiPalette.Cream;

        /// <summary>Your keys, in the order of the powers (<see cref="SporeRules.All"/>).</summary>
        public static readonly KeyPlace[] Keys = { KeyPlace.Q, KeyPlace.F, KeyPlace.C };

        [Tooltip("The spores' numbers")]
        public SporeRules rules = new SporeRules();
        [Tooltip("Seconds between a bot's looks at whether to sporulate")]
        public float botThinkSeconds = 0.5f;
        [Tooltip("How much Puffball Armour swells the scout: across, and up")]
        public Vector2 armourSwell = new Vector2(1.18f, 1.08f);

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
            SporePower.Rush => RushColour,
            SporePower.Regrowth => RegrowthColour,
            _ => ArmourColour,
        };

        private FxParticles Particles => combat != null && combat.Fx != null ? combat.Fx.Particles : null;

        /// <summary>A puff of spores from the cap, in the power's colour.</summary>
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
                particles.Emit(FxParticles.Kind.Spore, cap + direction * 0.2f, direction * Random.Range(1f, 2.6f), Random.Range(0.8f, 1.4f),
                    Random.Range(0.03f, 0.06f), colour, Color.white, gravity: -0.3f, drag: 2.5f);
            }
            for (var i = 0; i < 6; i++)
                particles.Emit(FxParticles.Kind.Puff, cap, Random.insideUnitSphere * 0.8f, Random.Range(0.4f, 0.7f), Random.Range(0.25f, 0.4f),
                    colour, Color.white, gravity: -0.4f, drag: 3);
        }

        /// <summary>A player's power as others see it: Rush's motes behind, Regrowth's rising spores, Armour's swollen cap.</summary>
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
                    if (meter.Power == SporePower.Rush)
                    {
                        trailClock[index] = 0.04f;
                        var side = player.transform.right * Random.Range(-0.2f, 0.2f);
                        particles.Emit(FxParticles.Kind.Spore, feet + side + Vector3.up * 0.05f, Vector3.up * 0.05f, Random.Range(0.9f, 1.3f),
                            Random.Range(0.04f, 0.06f), RushColour, Color.white);
                    }
                    else if (meter.Power == SporePower.Regrowth)
                    {
                        trailClock[index] = 0.12f;
                        var around = Random.insideUnitCircle * 0.35f;
                        particles.Emit(FxParticles.Kind.Spore, feet + new Vector3(around.x, Random.Range(0.3f, 1.2f), around.y),
                            Vector3.up * Random.Range(0.4f, 0.8f), Random.Range(0.8f, 1.2f), Random.Range(0.03f, 0.05f), RegrowthColour, Color.white,
                            gravity: -0.2f, drag: 1);
                    }
                }
            }
            // Armour swells the scout, in and out over a fifth of a second
            var target = on && meter.Power == SporePower.Armour ? 1 : 0;
            swell[index] = Mathf.MoveTowards(swell[index], target, dt * 5);
            if (player.body != null)
            {
                var s = Vector2.Lerp(Vector2.one, armourSwell, swell[index] * swell[index] * (3 - 2 * swell[index]));
                var scale = new Vector3(s.x, s.y, s.x);
                if (player.body.transform.localScale != scale)
                    player.body.transform.localScale = scale;
            }
        }
    }
}
