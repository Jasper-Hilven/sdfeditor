using System.Collections.Generic;
using SdfEffects;
using SdfGui;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfBattle
{
    /// <summary>
    /// The death burst of a killed scout (the gore), on the Battle object next to the <see cref="Match"/>, which makes it at the start
    /// and hands it every death before the body vanishes. With gore on (the settings' <see cref="GuiSettings.Gore"/>: splatty or extra
    /// splatty) a kill leaves a <see cref="Corpse"/> where the scout stood: its eyes and hands pop off and roll away, its cap breaks off,
    /// its stem slumps, all thrown away from the hit (<see cref="GoreRules"/>). And blood, lots of it, in the victim's team's colour:
    /// gobs thrown out in a spray that splat where they land, a pool growing under the stump, drops and small splats along the rolling
    /// eyes and hands. A fall, or gore off, just vanishes as before (with a little puff for a kill).
    /// <para>
    /// The blood is the blob gun's goop models (ticket 22's gobs and splats, the toon shader's glossy goo) with their colours turned to
    /// the team's: copies of their meshes made once per team (<see cref="Tinted"/>), so the blood draws with the one toon material like
    /// everything else. Gobs and splats are fixed pools, the oldest splat reused when all are out; a gob is stepped here with gravity
    /// and a raycast, no rigidbody. Splats stay a good while and shrink into the ground; corpses go sooner (<see cref="Corpse"/>), at
    /// most <see cref="GoreRules.MaxCorpses"/> at once.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class Gore : MonoBehaviour
    {
        // Room for ultra gore: three times the spray, and the gobs every flying piece trails
        private const int GobPool = 200;
        private const int SplatPool = 300;
        private const string OutlineName = "Outline";

        [Tooltip("Seconds a splat of blood stays before it shrinks into the ground")]
        public float splatStay = 22;
        public float splatShrink = 2.5f;
        [Tooltip("Radius the pool under the stump grows to, metres")]
        public float poolRadius = 0.85f;
        public float poolGrowTime = 3;
        [Tooltip("Thickest a splat stands off the ground, metres, however wide it is")]
        public float splatThickness = 0.035f;
        public float gravity = 9.8f;

        /// <summary>A copy of a goop model in a pool, its meshes swapped for the team's blood.</summary>
        private sealed class Model
        {
            public Transform transform;
            public MeshFilter[] filters;
            public Mesh[] meshes;
            /// <summary>Radius across (x, z) and half-height at scale 1.</summary>
            public float radius;
            public float height;
            public int tint = -1;
        }

        private struct Gob
        {
            public bool flying;
            public Vector3 position;
            public Vector3 velocity;
            public float age;
            public float radius;
            public Team team;
        }

        private struct Puddle
        {
            public bool active;
            public float age;
            public float radius;
            public float grow;
            public Vector3 position;
            public Vector3 normal;
            public Quaternion rotation;
        }

        private static readonly Dictionary<(Mesh, int), Mesh> TintCache = new Dictionary<(Mesh, int), Mesh>();
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private readonly Dictionary<PlayerState, Vector3> lastFrom = new Dictionary<PlayerState, Vector3>();
        private readonly Dictionary<PlayerState, Corpse> corpseOf = new Dictionary<PlayerState, Corpse>();
        private readonly CorpseBudget<Corpse> corpses = new CorpseBudget<Corpse>();
        private Match match;
        private Combat combat;
        private WeaponTableAsset assets;
        private Model[] gobModels;
        private Model[] splatModels;
        private Gob[] gobs;
        private Puddle[] splats;
        private int nextGob;

        /// <summary>The settings' gore level: 0 off, 1 splatty, 2 extra splatty, 3 ultra gore.</summary>
        public static int Level => GuiSettings.Gore;

        public Match Match => match;

        /// <summary>The particle pool of combat's effects, which the drops and puffs go into; null without combat.</summary>
        public FxParticles Particles => combat != null && combat.Fx != null ? combat.Fx.Particles : null;

        /// <summary>The gore of a match; <see cref="Match"/> makes it after combat, before the first round.</summary>
        public static Gore Create(Match match, Combat combat)
        {
            var gore = match.GetComponent<Gore>();
            if (gore == null)
                gore = match.gameObject.AddComponent<Gore>();
            gore.match = match;
            gore.combat = combat;
            gore.assets = combat != null ? combat.Assets : WeaponTableAsset.Load();
            gore.MakePools();
            match.State.Spawned += gore.OnSpawned;
            if (combat != null)
                combat.State.Damaged += gore.OnDamaged;
            return gore;
        }

        private void OnDestroy()
        {
            if (match != null && match.State != null)
                match.State.Spawned -= OnSpawned;
            if (combat != null && combat.State != null)
                combat.State.Damaged -= OnDamaged;
        }

        private void MakePools()
        {
            var root = new GameObject("Blood").transform;
            root.SetParent(transform, false);
            gobModels = Pool(root, "Gobs", assets != null ? assets.blobModels : null, GobPool);
            splatModels = Pool(root, "Splats", assets != null ? assets.splatModels : null, SplatPool);
            gobs = new Gob[gobModels?.Length ?? 0];
            splats = new Puddle[splatModels?.Length ?? 0];
        }

        /// <summary>Inactive copies of the goop models, round-robin, without colliders or shadows.</summary>
        private static Model[] Pool(Transform root, string name, GameObject[] prefabs, int count)
        {
            if (prefabs == null || prefabs.Length == 0)
                return null;
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            var pool = new List<Model>();
            for (var i = 0; i < count; i++)
            {
                var prefab = prefabs[i % prefabs.Length];
                if (prefab == null)
                    continue;
                var instance = Instantiate(prefab, holder, false);
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                var bounds = Corpse.LocalBounds(instance.transform, instance.transform);
                var filters = System.Array.FindAll(instance.GetComponentsInChildren<MeshFilter>(true), f => f.name != OutlineName && f.sharedMesh != null);
                pool.Add(new Model
                {
                    transform = instance.transform,
                    filters = filters,
                    meshes = System.Array.ConvertAll(filters, f => f.sharedMesh),
                    radius = Mathf.Max(0.02f, Mathf.Max(bounds.extents.x, bounds.extents.z)),
                    height = Mathf.Max(0.005f, bounds.extents.y),
                });
                instance.SetActive(false);
            }
            return pool.Count > 0 ? pool.ToArray() : null;
        }

        // ---------------------------------------------------------------- Deaths

        /// <summary>Where the last hit on each player came from, for which way the pieces fly when it killed.</summary>
        private void OnDamaged(PlayerState victim, Damage damage, float taken)
        {
            if (damage.From is { } from)
                lastFrom[victim] = from;
        }

        /// <summary>
        /// A player died: with gore on and a kill, its corpse bursts where it stood (the scout it was drawn as, in the pose it had), and
        /// the blood flies; with gore off a little puff. A fall leaves nothing. <see cref="Match"/> calls it before the body vanishes.
        /// </summary>
        public void Burst(Death death, Player player)
        {
            var level = Level;
            var feet = player.transform.position;
            var middle = feet + Vector3.up * 0.9f;
            if (death.Cause != DeathCause.Killed)
                return;
            if (!GoreRules.Bursts(level, death.Cause))
            {
                Poof(middle);
                return;
            }

            // Away from the hit: from where it came (the shooter's eye), else from the killer, else anywhere
            var away = Vector3.zero;
            if (lastFrom.TryGetValue(death.Victim, out var from))
                away = middle - from;
            else if (match.PlayerOf(death.Killer) is { } killer)
                away = feet - killer.transform.position;
            away.y = 0;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.zero;
            lastFrom.Remove(death.Victim);

            var team = death.Victim.Team;
            var corpse = Corpse.Make(this, player, death, match.ScoutOf(team), assets != null ? assets.scoutPieces : null, away,
                GoreRules.Flings(level));
            if (corpse != null)
            {
                if (corpseOf.TryGetValue(death.Victim, out var earlier) && earlier != null)
                    earlier.Sink();
                corpseOf[death.Victim] = corpse;
                corpses.Add(corpse)?.Sink();
                foreach (var other in match.Players)
                    corpse.IgnorePlayer(other);
            }

            var strength = GoreRules.Strength(death.Weapon);
            Spray(middle, away, team, GoreRules.BloodGobs(level, death.Weapon), strength);
            if (Ground(feet + Vector3.up * 0.5f, out var ground, out var normal))
                Splat(ground, normal, poolRadius * (death.Weapon == WeaponIds.Flamethrower ? 0.5f : 1), team, poolGrowTime);
            Flavour(death.Weapon, middle);
        }

        /// <summary>A corpse shrank away: no longer counted.</summary>
        public void Gone(Corpse corpse)
        {
            corpses.Remove(corpse);
            if (corpse.Victim != null && corpseOf.TryGetValue(corpse.Victim, out var current) && current == corpse)
                corpseOf.Remove(corpse.Victim);
        }

        /// <summary>A player (re)appeared: their old corpse sinks away; the corpses' pieces let the new body through.</summary>
        private void OnSpawned(PlayerState state)
        {
            if (corpseOf.TryGetValue(state, out var corpse) && corpse != null)
                corpse.Sink();
            // A capsule turned off and on again forgets what it ignored
            var player = match.PlayerOf(state);
            foreach (var pair in corpseOf)
                if (pair.Value != null)
                    pair.Value.IgnorePlayer(player);
        }

        /// <summary>Gore off: a small puff of cream and cap colour where the scout was.</summary>
        private void Poof(Vector3 at)
        {
            var particles = Particles;
            if (particles == null)
                return;
            for (var i = 0; i < 10; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f;
                particles.Emit(FxParticles.Kind.Puff, at + direction * 0.3f, direction * Random.Range(0.8f, 2f), Random.Range(0.4f, 0.7f),
                    Random.Range(0.15f, 0.28f), new Color(0.95f, 0.9f, 0.78f), new Color(0.2f, 0.16f, 0.08f), gravity: -0.6f, drag: 4);
            }
        }

        /// <summary>The weapon's touch: the flamethrower's smoke, the electro-gun's sparks.</summary>
        private void Flavour(string weapon, Vector3 at)
        {
            var particles = Particles;
            if (particles == null)
                return;
            if (weapon == WeaponIds.Flamethrower)
            {
                for (var i = 0; i < 8; i++)
                    particles.Emit(FxParticles.Kind.Puff, at + Random.insideUnitSphere * 0.3f, (Vector3.up + Random.insideUnitSphere * 0.5f) * Random.Range(0.6f, 1.4f),
                        Random.Range(0.8f, 1.4f), Random.Range(0.2f, 0.35f), new Color(0.22f, 0.19f, 0.18f), Color.black, gravity: -1.2f, drag: 1.5f);
            }
            else if (weapon == WeaponIds.ElectroGun)
            {
                var band = new Color(0.4f * 1.4f, 0.8f * 1.4f, 1.4f, 1);
                var core = new Color(2.4f, 2.4f, 2.6f, 1);
                for (var i = 0; i < 14; i++)
                {
                    var ray = Random.onUnitSphere;
                    particles.Emit(FxParticles.Kind.Spark, at + ray * 0.3f, ray * Random.Range(6f, 12f), Random.Range(0.08f, 0.16f), Random.Range(0.02f, 0.035f), band, core);
                }
            }
        }

        // ---------------------------------------------------------------- Blood

        /// <summary>The team's blood colour (its side's, as the flags and spawn markers) and the glow in its shade.</summary>
        public static Color BloodOf(Team team) => BattleSide.ColorOf(BattleSide.SideOf(team));

        public static Color BloodGlowOf(Team team)
        {
            var blood = BloodOf(team);
            return new Color(blood.r * 0.35f, blood.g * 0.35f, blood.b * 0.35f, 1);
        }

        /// <summary>A gob count's worth of blood thrown out of <paramref name="from"/>, mostly away from the hit and up.</summary>
        private void Spray(Vector3 from, Vector3 away, Team team, int count, float strength)
        {
            for (var i = 0; i < count; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.9f + 0.35f;
                direction = (direction + away * 0.8f).normalized;
                var velocity = direction * (Random.Range(2.2f, 6.5f) * strength);
                if (!Throw(from + direction * 0.25f, velocity, Random.Range(0.045f, 0.1f), team))
                    break;
            }
            // And a mist of drops for the first moment
            var particles = Particles;
            if (particles == null)
                return;
            var colour = BloodOf(team);
            var glow = BloodGlowOf(team);
            for (var i = 0; i < count; i++)
                particles.Emit(FxParticles.Kind.Drop, from + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.6f + away * 0.5f) * Random.Range(1.5f, 4.5f),
                    Random.Range(0.3f, 0.7f), Random.Range(0.014f, 0.03f), colour, glow, gravity: 9.8f);
        }

        /// <summary>A gob of blood in flight from the pool; false when there are no goop models.</summary>
        public bool Throw(Vector3 position, Vector3 velocity, float radius, Team team)
        {
            if (gobModels == null)
                return false;
            var i = nextGob;
            nextGob = (nextGob + 1) % gobModels.Length;
            gobs[i] = new Gob { flying = true, position = position, velocity = velocity, radius = radius, team = team };
            var model = gobModels[i];
            Tint(model, (int)team);
            model.transform.gameObject.SetActive(true);
            return true;
        }

        /// <summary>
        /// A splat of blood of <paramref name="radius"/> on a surface at <paramref name="at"/> facing <paramref name="normal"/>; with
        /// <paramref name="grow"/> it spreads out over that many seconds (the pool), else it pops out. The oldest splat goes round.
        /// </summary>
        public void Splat(Vector3 at, Vector3 normal, float radius, Team team, float grow = 0)
        {
            if (splatModels == null)
                return;
            var i = 0;
            var oldest = -1f;
            for (var j = 0; j < splats.Length; j++)
            {
                if (!splats[j].active)
                {
                    i = j;
                    break;
                }
                if (splats[j].age > oldest)
                    (i, oldest) = (j, splats[j].age);
            }
            // Each a hair above the last, so splats lying over each other do not flicker
            splats[i] = new Puddle
            {
                active = true,
                radius = radius,
                grow = grow,
                position = at + normal * Random.Range(0.002f, 0.012f),
                normal = normal,
                rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(Random.Range(0, 360f), Vector3.up),
            };
            var model = splatModels[i];
            Tint(model, (int)team);
            model.transform.gameObject.SetActive(true);
            PlaceSplat(i);
        }

        /// <summary>A few drops of blood, for the trails of the rolling eyes and hands.</summary>
        public void Drip(Vector3 at, Vector3 velocity, Team team, int count = 1)
        {
            var particles = Particles;
            if (particles == null)
                return;
            for (var i = 0; i < count; i++)
                particles.Emit(FxParticles.Kind.Drop, at + Random.insideUnitSphere * 0.04f, velocity * 0.3f + Random.insideUnitSphere * 0.6f, Random.Range(0.25f, 0.5f),
                    Random.Range(0.012f, 0.024f), BloodOf(team), BloodGlowOf(team), gravity: 9.8f);
        }

        /// <summary>The ground under a point, not a player; within 3 m.</summary>
        public static bool Ground(Vector3 from, out Vector3 point, out Vector3 normal, float reach = 3)
        {
            var count = Physics.RaycastNonAlloc(from, Vector3.down, Hits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            point = default;
            normal = Vector3.up;
            for (var i = 0; i < count; i++)
            {
                if (Hits[i].distance >= nearest || Player.Of(Hits[i].collider) != null)
                    continue;
                nearest = Hits[i].distance;
                point = Hits[i].point;
                normal = Hits[i].normal;
            }
            return nearest < float.MaxValue;
        }

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            for (var i = 0; i < gobs.Length; i++)
                if (gobs[i].flying)
                    StepGob(i, dt);
            for (var i = 0; i < splats.Length; i++)
            {
                if (!splats[i].active)
                    continue;
                splats[i].age += dt;
                PlaceSplat(i);
            }
        }

        private void StepGob(int i, float dt)
        {
            ref var g = ref gobs[i];
            var model = gobModels[i];
            g.age += dt;
            var velocity = g.velocity + Vector3.down * (gravity * dt);
            var step = (g.velocity + velocity) * (0.5f * dt);
            var length = step.magnitude;
            if (length > 1e-5f && Land(g.position, step / length, length + g.radius * 0.5f, out var hit))
            {
                g.flying = false;
                model.transform.gameObject.SetActive(false);
                if (Machines.Of(hit.collider) != null)
                {
                    Drip(hit.point, -velocity * 0.2f, g.team, 3);
                    return;
                }
                // Splats bigger than the gob, as it spreads; a fast one longer along the way it went
                Splat(hit.point, hit.normal, g.radius * Random.Range(2.2f, 3.4f), g.team);
                Drip(hit.point + hit.normal * 0.03f, Vector3.Reflect(velocity, hit.normal) * 0.4f, g.team, 2);
                return;
            }
            g.position += step;
            g.velocity = velocity;
            if (g.age > 4)
            {
                g.flying = false;
                model.transform.gameObject.SetActive(false);
                return;
            }
            // Stretched along its flight, wobbling (as the blob gun's gobs)
            var stretch = 1 + Mathf.Clamp(g.velocity.magnitude * 0.05f, 0, 0.6f);
            var wobble = Mathf.Sin(g.age * 38 + i) * 0.08f;
            var across = 1 / Mathf.Sqrt(stretch);
            var scale = g.radius / model.radius;
            model.transform.SetPositionAndRotation(g.position, Quaternion.LookRotation(g.velocity.sqrMagnitude > 1e-6f ? g.velocity : Vector3.forward));
            model.transform.localScale = new Vector3(across * (1 + wobble), across * (1 - wobble), stretch) * scale;
        }

        /// <summary>The nearest surface along a step that blood sticks to: not a player (the corpse's pieces are not raycast).</summary>
        private static bool Land(Vector3 from, Vector3 direction, float distance, out RaycastHit hit)
        {
            var count = Physics.RaycastNonAlloc(from, direction, Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            hit = default;
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                if (Hits[i].distance >= nearest || Player.Of(Hits[i].collider) != null)
                    continue;
                nearest = Hits[i].distance;
                hit = Hits[i];
            }
            return nearest < float.MaxValue;
        }

        /// <summary>A splat: pops out (or spreads, the pool), stays, then shrinks back into the surface.</summary>
        private void PlaceSplat(int i)
        {
            ref var s = ref splats[i];
            var model = splatModels[i];
            var age = s.age;
            float spread;
            if (s.grow > 0)
                spread = Mathf.Lerp(0.15f, 1, 1 - Mathf.Pow(1 - Mathf.Clamp01(age / s.grow), 3));
            else
                spread = age < 0.1f ? Mathf.Lerp(0.5f, 1.12f, age / 0.1f) : Mathf.Lerp(1.12f, 1, Mathf.Clamp01((age - 0.1f) / 0.1f));
            var shrink = 1 - Mathf.SmoothStep(0, 1, (age - splatStay) / splatShrink);
            if (shrink <= 0.02f)
            {
                s.active = false;
                model.transform.gameObject.SetActive(false);
                return;
            }
            var across = s.radius / model.radius * spread * shrink;
            var up = Mathf.Min(across, splatThickness / model.height) * Mathf.Sqrt(shrink);
            model.transform.SetPositionAndRotation(s.position - s.normal * (0.01f * (1 - shrink)), s.rotation);
            model.transform.localScale = new Vector3(across, up, across);
        }

        // ---------------------------------------------------------------- Colours

        private static void Tint(Model model, int team)
        {
            if (model.tint == team)
                return;
            model.tint = team;
            Color.RGBToHSV(BloodOf((Team)team), out var hue, out _, out _);
            for (var i = 0; i < model.filters.Length; i++)
                model.filters[i].sharedMesh = Tinted(model.meshes[i], team, hue, 1);
        }

        /// <summary>A scout's piece charred by the flamethrower: its colours darkened, its own hue kept.</summary>
        public static Mesh Charred(Mesh mesh) => Tinted(mesh, 100, null, 0.3f);

        /// <summary>
        /// A copy of a model's mesh with its colours turned to <paramref name="hue"/> (or kept) and darkened (<see cref="GoreRules.Recolour"/>):
        /// the base colour, the variation's, the speckles' and the glowing edge's (vertex colour, UV3, UV4, UV6, as MushroomBuilder packs
        /// them). Made once per mesh and <paramref name="key"/>; a mesh that cannot be read is used as it is.
        /// </summary>
        private static Mesh Tinted(Mesh source, int key, float? hue, float darken)
        {
            if (source == null || !source.isReadable)
                return source;
            if (TintCache.TryGetValue((source, key), out var cached) && cached != null)
                return cached;
            var mesh = Instantiate(source);
            mesh.name = $"{source.name} ({key})";
            var colours = new List<Color>();
            mesh.GetColors(colours);
            for (var v = 0; v < colours.Count; v++)
                colours[v] = GoreRules.Recolour(colours[v], hue, darken);
            if (colours.Count > 0)
                mesh.SetColors(colours);
            var channel = new List<Vector4>();
            foreach (var uv in new[] { 3, 4, 6 })
            {
                mesh.GetUVs(uv, channel);
                if (channel.Count == 0)
                    continue;
                for (var v = 0; v < channel.Count; v++)
                {
                    var c = channel[v];
                    var turned = GoreRules.Recolour(new Color(c.x, c.y, c.z), hue, darken);
                    channel[v] = new Vector4(turned.r, turned.g, turned.b, c.w);
                }
                mesh.SetUVs(uv, channel);
            }
            mesh.UploadMeshData(true);
            TintCache[(source, key)] = mesh;
            return mesh;
        }
    }
}
