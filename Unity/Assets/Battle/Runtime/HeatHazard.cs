using System;
using System.Collections.Generic;
using SdfLevel;
using UnityEngine;
using UnityEngine.AI;

namespace SdfBattle
{
    /// <summary>
    /// The heat of a level (ticket 28.10, the kitchen's hob): rings of floor that heat in turns, played by <see cref="HeatRules"/> on the
    /// round clock. One object at the scene's root, made by the level builder from level.json's "heat" (nothing is set up by hand):
    /// <list type="bullet">
    /// <item>The glow: the models that stand at a ring's centre (the burners) get a per-renderer <c>_HeatGlow</c> (a MaterialPropertyBlock
    /// for the toon shader: ember while warming, orange to yellow while hot, never scarlet; <see cref="HeatGlow"/>) every frame the heat
    /// changes. Nothing else of the models changes.</item>
    /// <item>The burn: a scout or tank standing in a hot ring takes the rules' damage a second, in ticks, as a hazard's
    /// (<see cref="Damage.ByHeat"/>): the ring's name in the kill feed ("the hob"), no weapon, kill credit or spores.</item>
    /// <item>The bots: each group's rings are a navmesh area (<see cref="AreaName"/>, marked in the bake); while the group warms and is
    /// hot, the area costs <see cref="HeatRules.cost"/> times as much for bots and tanks (<see cref="NavAreaCosts"/>), who then path round
    /// it, and a bot or tank standing in one steps off.</item>
    /// </list>
    /// A level without heat has no such object, so nothing of this runs there.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class HeatHazard : MonoBehaviour
    {
        public const string ObjectName = "Heat";

        /// <summary>The renderers of the models that glow with one ring (the same index as <see cref="rings"/>).</summary>
        [Serializable]
        public sealed class RingGlow
        {
            public Renderer[] renderers = new Renderer[0];
        }

        /// <summary>A scout or tank is not burnt jumping over a ring: its feet are no higher than this over the ring's ground.</summary>
        public const float StandingHeight = 0.8f;

        /// <summary>Metres past a ring's edge a bot or tank steps to.</summary>
        public const float StepMargin = 1.2f;

        [Tooltip("The cycle and its damage and cost, from level.json")]
        public HeatRules rules = new HeatRules();
        public HeatRing[] rings = new HeatRing[0];
        [Tooltip("Ground height (y) of each ring's centre")]
        public float[] ringHeights = new float[0];
        [Tooltip("The models that glow with each ring")]
        public RingGlow[] glows = new RingGlow[0];

        private static readonly int GlowId = Shader.PropertyToID("_HeatGlow");

        private readonly Dictionary<object, Burner> burners = new Dictionary<object, Burner>();
        private readonly List<object> gone = new List<object>();
        private MaterialPropertyBlock block;
        private float[] shown = new float[0];
        private bool[] avoided = new bool[0];
        private int[] areas = new int[0];

        private sealed class Burner
        {
            public HeatBurn burn;
            public float replanAt;
        }

        /// <summary>The heat of the active scene; null where there is none.</summary>
        public static HeatHazard Current { get; private set; }

        /// <summary>The navmesh area of a group's rings: "Burner" for the first group, then "Burner 2", "Burner 3", "Burner 4".</summary>
        public static string AreaName(int group) => group <= 0 ? "Burner" : $"Burner {group + 1}";

        /// <summary>The round clock, as the trains' (<see cref="PathMover.Clock"/>).</summary>
        public static float Clock => PathMover.Clock;

        private void OnEnable()
        {
            Current = this;
            block = new MaterialPropertyBlock();
            shown = new float[Mathf.Max(1, rules.groups)];
            for (var i = 0; i < shown.Length; i++)
                shown[i] = -1;
            avoided = new bool[shown.Length];
            areas = new int[shown.Length];
            for (var group = 0; group < areas.Length; group++)
            {
                areas[group] = NavMesh.GetAreaFromName(AreaName(group));
                if (areas[group] < 0)
                    Debug.LogWarning($"Heat: the navmesh area '{AreaName(group)}' is not in the project's navigation settings; bots will walk over the hot rings");
            }
            burners.Clear();
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
            // The costs back to the project's, and the burners cold
            NavAreaCosts.Reset();
            if (block != null)
                for (var ring = 0; ring < glows.Length; ring++)
                    SetGlow(ring, 0);
        }

        private void Update()
        {
            var clock = Clock;
            Glow(clock);
            Costs(clock);
            var match = Match.Current;
            var combat = Combat.Current;
            if (match == null || combat == null || match.State == null || match.State.Phase != RoundPhase.Playing)
            {
                burners.Clear();
                return;
            }
            Burn(match, combat, clock, Time.deltaTime);
        }

        // ---------------------------------------------------------------- Glow

        private void Glow(float clock)
        {
            for (var group = 0; group < shown.Length; group++)
            {
                var heat = rules.Heat(group, clock);
                if (Mathf.Abs(heat - shown[group]) < 0.004f && !((heat == 0) != (shown[group] == 0)))
                    continue;
                shown[group] = heat;
                for (var ring = 0; ring < rings.Length && ring < glows.Length; ring++)
                    if (rings[ring].group == group)
                        SetGlow(ring, heat);
            }
        }

        private void SetGlow(int ring, float heat)
        {
            if (ring >= glows.Length || glows[ring] == null)
                return;
            // The toon shader takes it in linear space, flat over the burner's own colour: xyz the colour, w how much
            var colour = HeatGlow.Of(heat);
            var linear = colour.linear;
            var value = new Vector4(linear.r, linear.g, linear.b, heat <= 0 ? 0 : colour.a);
            foreach (var renderer in glows[ring].renderers)
            {
                if (renderer == null)
                    continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(GlowId, value);
                renderer.SetPropertyBlock(block);
            }
        }

        // ---------------------------------------------------------------- Bots and tanks

        private void Costs(float clock)
        {
            var changed = false;
            for (var group = 0; group < avoided.Length; group++)
            {
                var now = HeatRules.Avoided(rules.PhaseOf(group, clock));
                if (now == avoided[group])
                    continue;
                avoided[group] = now;
                changed = true;
                if (areas[group] >= 0)
                    NavAreaCosts.Set(areas[group], rules.PathCost(group, clock));
            }
            if (!changed)
                return;
            // Everyone finds a way at the new costs, instead of walking on over a ring that has just warmed up
            var match = Match.Current;
            if (match != null)
                foreach (var player in match.Players)
                    if (player != null && player.State != null && player.State.Alive)
                    {
                        var bot = player.GetComponent<Bot>();
                        if (bot != null)
                            bot.Replan();
                    }
            var machines = Machines.Current;
            if (machines != null)
                foreach (var machine in machines.All)
                    if (machine is TankMachine tank && tank.Alive)
                        tank.Replan();
        }

        // ---------------------------------------------------------------- The burn

        private void Burn(Match match, Combat combat, float clock, float dt)
        {
            foreach (var player in match.Players)
            {
                if (player == null || player.State == null || !player.State.Alive || player.Controller == null || !player.Controller.enabled)
                {
                    if (player != null)
                        burners.Remove(player);
                    continue;
                }
                var feet = player.transform.position;
                var ring = RingUnder(feet, clock, hotOnly: true);
                var burner = BurnerOf(player);
                var amount = burner.burn.Step(dt, ring >= 0, rules.damage);
                if (amount > 0)
                    combat.Hurt(player, Damage.ByHeat(rules.cause, amount, Centre(ring)));
                // A bot in a ring that warms or is hot walks out of it
                var bot = player.GetComponent<Bot>();
                if (bot != null)
                {
                    var warm = RingUnder(feet, clock, hotOnly: false);
                    if (warm >= 0)
                    {
                        var away = HeatRules.StepOff(rings[warm], new Vector2(feet.x, feet.z), StepMargin, out _);
                        bot.StepOff(new Vector3(away.x, 0, away.y));
                    }
                }
            }
            var machines = Machines.Current;
            if (machines == null)
                return;
            foreach (var machine in machines.All)
            {
                // Tanks drive over the glass; turrets stand off it and helicopters fly
                if (!(machine is TankMachine tank) || !tank.Alive)
                {
                    if (machine != null)
                        burners.Remove(machine);
                    continue;
                }
                var feet = tank.transform.position;
                var ring = RingUnder(feet, clock, hotOnly: true);
                var burner = BurnerOf(tank);
                var amount = burner.burn.Step(dt, ring >= 0, rules.damage);
                if (amount > 0)
                    tank.TakeDamage(Damage.ByHeat(rules.cause, amount, Centre(ring)), tank.Center);
                var warm = RingUnder(feet, clock, hotOnly: false);
                if (warm >= 0 && Time.time >= burner.replanAt)
                {
                    // Out of the ring by a new path at the ring's cost, tried again in a second if it is still in
                    burner.replanAt = Time.time + 1;
                    tank.Replan();
                }
            }
        }

        private Burner BurnerOf(object key)
        {
            if (!burners.TryGetValue(key, out var burner))
                burners[key] = burner = new Burner();
            return burner;
        }

        private Vector3? Centre(int ring) => ring < 0 ? (Vector3?)null : new Vector3(rings[ring].centre.x, ringHeights.Length > ring ? ringHeights[ring] : 0, rings[ring].centre.y);

        /// <summary>The ring a thing with its feet at <paramref name="feet"/> stands in that hurts (hot) or is avoided (warming or hot), or -1.</summary>
        public int RingUnder(Vector3 feet, float clock, bool hotOnly)
        {
            var ring = rules.RingOn(rings, new Vector2(feet.x, feet.z), clock, hotOnly);
            if (ring < 0)
                return -1;
            var floor = ringHeights.Length > ring ? ringHeights[ring] : 0;
            return feet.y - floor <= StandingHeight ? ring : -1;
        }
    }
}
