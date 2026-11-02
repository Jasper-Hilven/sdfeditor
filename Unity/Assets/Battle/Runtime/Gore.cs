using System.Collections.Generic;
using SdfEffects;
using SdfGui;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfBattle
{
    /// <summary>
    /// The gore of a kill (ticket 51), on the Battle object next to the <see cref="Match"/>, which makes it at the start and hands it every
    /// death before the body vanishes. Only the monster head bursts: with gore on (the settings' <see cref="GuiSettings.Gore"/>: splatty,
    /// extra splatty, ultra gore) the head the victim wore breaks into the pieces cut from its own mesh (<see cref="Corpse"/>) and its blood
    /// flies, in the head's own colour (<see cref="GoreRules.BloodOf"/>, from the <see cref="SdfHeads.MonsterHeads"/> table, never the team's
    /// and never a realistic red): gobs thrown out in a spray that splat where they land, drops and small splats along the flying pieces.
    /// With gore off the head pops in a cartoon puff of that colour, no pieces, no gobs. Heads with a <see cref="HeadBurstProfile"/>
    /// (ticket 46) add their own debris and specials.
    /// <para>
    /// The human body never bursts or bleeds at any level, and no blood lands on it: it becomes a <see cref="SoldierRagdoll"/> in the same
    /// frame, pushed by the hit, and lies for the corpse time (<see cref="GoreRules.MaxCorpses"/> at most, the oldest goes first).
    /// </para>
    /// <para>
    /// The blood is the blob gun's goop models (ticket 22's gobs and splats, the toon shader's glossy goo) with their colours turned to
    /// the head's blood colour (<see cref="GoreRules.Tint"/>): copies of their meshes made once per colour (<see cref="Tinted"/>), so the
    /// blood draws with the one toon material like everything else. Gobs and splats are fixed pools, the oldest splat reused when all are
    /// out; a gob is stepped here with gravity and a raycast, no rigidbody. Splats stay a good while and shrink into the ground; corpses go
    /// sooner.
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
        [Tooltip("Thickest a splat stands off the ground, metres, however wide it is")]
        public float splatThickness = 0.035f;
        public float gravity = 9.8f;

        /// <summary>A copy of a goop model in a pool, its meshes swapped for the head's blood.</summary>
        private sealed class Model
        {
            public Transform transform;
            public MeshFilter[] filters;
            public Mesh[] meshes;
            /// <summary>Radius across (x, z) and half-height at scale 1.</summary>
            public float radius;
            public float height;
            public int tint;
        }

        private struct Gob
        {
            public bool flying;
            public Vector3 position;
            public Vector3 velocity;
            public float age;
            public float radius;
            public Color blood;
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
        private CorpseBudget<Corpse> corpses = new CorpseBudget<Corpse>();
        private readonly Dictionary<PlayerState, SoldierRagdoll> ragdollOf = new Dictionary<PlayerState, SoldierRagdoll>();
        private CorpseBudget<SoldierRagdoll> ragdolls = new CorpseBudget<SoldierRagdoll>();
        private readonly Stack<SoldierRagdoll>[] ragdollPool = { new Stack<SoldierRagdoll>(), new Stack<SoldierRagdoll>() };
        private Transform ragdollRoot;
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
            // Epic and up keep more blood and corpses about (SdfMushrooms.QualityExtras), for the match: the pools are made once
            splatModels = Pool(root, "Splats", assets != null ? assets.splatModels : null, SplatPool * SdfMushrooms.QualityExtras.GoreAmount);
            corpses = new CorpseBudget<Corpse>(GoreRules.MaxCorpses * SdfMushrooms.QualityExtras.GoreAmount);
            ragdolls = new CorpseBudget<SoldierRagdoll>(GoreRules.MaxCorpses * SdfMushrooms.QualityExtras.GoreAmount);
            ragdollRoot = new GameObject("Ragdolls").transform;
            ragdollRoot.SetParent(transform, false);
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
        /// A player died: a kill ragdolls the body at every level and bursts the head it wore (pieces and blood with gore on, a puff of the
        /// head's colour with gore off), in the same frame. A fall leaves nothing. <see cref="Match"/> calls it before the body vanishes.
        /// </summary>
        public void Burst(Death death, Player player)
        {
            if (death.Cause != DeathCause.Killed)
                return;
            var level = Level;
            var soldier = player.soldier;
            var feet = player.transform.position;

            // Away from the hit: from where it came (the shooter's eye), else from the killer, else anywhere
            var away = Vector3.zero;
            var head = soldier != null ? soldier.Head : null;
            var middle = head != null ? head.transform.TransformPoint(head.Shape.Centre) : feet + Vector3.up * 1.7f;
            if (lastFrom.TryGetValue(death.Victim, out var from))
                away = middle - from;
            else if (match.PlayerOf(death.Killer) is { } killer)
                away = feet - killer.transform.position;
            away.y = 0;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.zero;
            lastFrom.Remove(death.Victim);

            // The body: a ragdoll, whatever the level, pushed by the hit
            if (soldier != null && GoreRules.Ragdolls(death.Cause))
                Fall(soldier, player, death, away);

            var headId = head != null ? head.Id : null;
            var blood = GoreRules.BloodOf(headId);
            if (head == null)
            {
                // No head to burst (the scout stand-in, or the heads not imported): a puff at the top of the body
                Poof(middle, blood);
                return;
            }
            // The live head is gone from the body in every case
            head.gameObject.SetActive(false);
            if (GoreRules.Puffs(level, death.Cause))
            {
                Poof(middle, blood);
                return;
            }

            var strength = GoreRules.Strength(death.Weapon);
            var profile = HeadBurstProfile.Of(headId);
            var burst = new HeadBurst(this, headId, middle, head.transform.rotation, away, blood, level, death.Weapon, death.Headshot);
            var models = SdfHeads.MonsterHeadModels.Load();
            var pieces = models != null && models.TryGet(headId, out var entry) && !(profile != null && profile.ReplacesPieces) ? entry.pieces : null;
            var corpse = Corpse.Make(this, death.Victim, death, pieces, head.transform, middle, blood, away, GoreRules.Flings(level));
            if (corpse != null)
            {
                // Epic and up let a player's earlier corpses lie (as long as they last); below, one corpse each
                corpse.lifetime *= SdfMushrooms.QualityExtras.GoreStay;
                if (!CorpsesStay && corpseOf.TryGetValue(death.Victim, out var earlier) && earlier != null)
                    earlier.Sink();
                corpseOf[death.Victim] = corpse;
                corpses.Add(corpse)?.Sink();
                foreach (var other in match.Players)
                    corpse.IgnorePlayer(other);
            }
            Spray(middle, away, blood, GoreRules.BloodGobs(level, death.Weapon), strength);
            Flavour(death.Weapon, middle);
            profile?.Special(burst);
        }

        /// <summary>
        /// The dead soldier's body falls: a pooled ragdoll takes its pose and starts, pushed by the hit; the live body is hidden. No blood,
        /// no pieces, no stump.
        /// </summary>
        private void Fall(SoldierPuppet soldier, Player player, Death death, Vector3 away)
        {
            soldier.Animator.Died(away);
            var ragdoll = TakeRagdoll(soldier.SkinIndex);
            if (ragdoll == null)
                return;
            if (!CorpsesStay && ragdollOf.TryGetValue(death.Victim, out var earlier) && earlier != null)
                earlier.Sink();
            ragdollOf[death.Victim] = ragdoll;
            ragdolls.Add(ragdoll)?.Sink();
            var push = away * (GoreRules.RagdollPush * GoreRules.Strength(death.Weapon));
            var inherited = player.Capsule != null ? player.Capsule.velocity * 0.5f : Vector3.zero;
            inherited.y = Mathf.Min(inherited.y, 0);
            ragdoll.lifetime = 10 * SdfMushrooms.QualityExtras.GoreStay;
            ragdoll.Launch(soldier.transform, death.Victim, push, inherited, death.Headshot);
            soldier.HandOverToRagdoll();
            foreach (var other in match.Players)
                ragdoll.IgnorePlayer(other);
        }

        private SoldierRagdoll TakeRagdoll(int skin)
        {
            var pool = ragdollPool[Mathf.Clamp(skin, 0, ragdollPool.Length - 1)];
            while (pool.Count > 0)
            {
                var pooled = pool.Pop();
                if (pooled != null)
                    return pooled;
            }
            var prefab = SdfRig.SoldierModels.Load() is { } models ? models.prefabs[Mathf.Clamp(skin, 0, models.prefabs.Length - 1)] : null;
            return SoldierRagdoll.Create(prefab, skin, ragdollRoot, this);
        }

        /// <summary>A ragdoll lay its time and sank away: back in the pool.</summary>
        public void Release(SoldierRagdoll ragdoll)
        {
            ragdolls.Remove(ragdoll);
            if (ragdoll.Victim != null && ragdollOf.TryGetValue(ragdoll.Victim, out var current) && current == ragdoll)
                ragdollOf.Remove(ragdoll.Victim);
            ragdollPool[Mathf.Clamp(ragdoll.Skin, 0, ragdollPool.Length - 1)].Push(ragdoll);
        }

        /// <summary>The ragdolls and head pieces lying about now (for the tests and captures).</summary>
        public IEnumerable<SoldierRagdoll> Ragdolls => ragdolls.All;

        /// <summary>A corpse shrank away: no longer counted.</summary>
        public void Gone(Corpse corpse)
        {
            corpses.Remove(corpse);
            if (corpse.Victim != null && corpseOf.TryGetValue(corpse.Victim, out var current) && current == corpse)
                corpseOf.Remove(corpse.Victim);
        }

        /// <summary>
        /// A player (re)appeared: their old corpse sinks away (on Epic and up it lies on, <see cref="CorpsesStay"/>); every corpse's
        /// pieces let the new body through.
        /// </summary>
        private void OnSpawned(PlayerState state)
        {
            if (!CorpsesStay && corpseOf.TryGetValue(state, out var corpse) && corpse != null)
                corpse.Sink();
            if (!CorpsesStay && ragdollOf.TryGetValue(state, out var lying) && lying != null)
                lying.Sink();
            // A capsule turned off and on again forgets what it ignored
            var player = match.PlayerOf(state);
            foreach (var each in corpses.All)
                if (each != null)
                    each.IgnorePlayer(player);
            foreach (var each in ragdolls.All)
                if (each != null)
                    each.IgnorePlayer(player);
            // and the latest of each player, which may be sinking away uncounted
            foreach (var pair in corpseOf)
                if (pair.Value != null)
                    pair.Value.IgnorePlayer(player);
        }

        /// <summary>Whether corpses outlast their player's respawn and next death (Epic and up), going only by age or the corpse budget.</summary>
        private static bool CorpsesStay => SdfMushrooms.QualityExtras.GoreAmount > 1;

        /// <summary>Gore off: a cartoon puff of the head's blood colour where it was, no pieces and no gobs.</summary>
        private void Poof(Vector3 at, Color blood)
        {
            var particles = Particles;
            if (particles == null)
                return;
            var end = new Color(blood.r * 0.35f, blood.g * 0.35f, blood.b * 0.35f, 1);
            for (var i = 0; i < 12; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f;
                particles.Emit(FxParticles.Kind.Puff, at + direction * 0.2f, direction * Random.Range(0.8f, 2f), Random.Range(0.4f, 0.7f),
                    Random.Range(0.15f, 0.28f), blood, end, gravity: -0.6f, drag: 4);
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

        /// <summary>The glow of a blood colour: its own shade, dimmer.</summary>
        public static Color BloodGlowOf(Color blood) => new Color(blood.r * 0.35f, blood.g * 0.35f, blood.b * 0.35f, 1);

        /// <summary>A gob count's worth of blood thrown out of <paramref name="from"/>, mostly away from the hit and up.</summary>
        private void Spray(Vector3 from, Vector3 away, Color blood, int count, float strength)
        {
            for (var i = 0; i < count; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.9f + 0.35f;
                direction = (direction + away * 0.8f).normalized;
                var velocity = direction * (Random.Range(2.2f, 6.5f) * strength);
                if (!Throw(from + direction * 0.25f, velocity, Random.Range(0.045f, 0.1f), blood))
                    break;
            }
            // And a mist of drops for the first moment
            var particles = Particles;
            if (particles == null)
                return;
            var colour = blood;
            var glow = BloodGlowOf(blood);
            for (var i = 0; i < count; i++)
                particles.Emit(FxParticles.Kind.Drop, from + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.6f + away * 0.5f) * Random.Range(1.5f, 4.5f),
                    Random.Range(0.3f, 0.7f), Random.Range(0.014f, 0.03f), colour, glow, gravity: 9.8f);
        }

        /// <summary>A gob of blood in flight from the pool; false when there are no goop models.</summary>
        public bool Throw(Vector3 position, Vector3 velocity, float radius, Color blood)
        {
            if (gobModels == null)
                return false;
            var i = nextGob;
            nextGob = (nextGob + 1) % gobModels.Length;
            gobs[i] = new Gob { flying = true, position = position, velocity = velocity, radius = radius, blood = blood };
            var model = gobModels[i];
            Tint(model, blood);
            model.transform.gameObject.SetActive(true);
            return true;
        }

        /// <summary>
        /// A splat of blood of <paramref name="radius"/> on a surface at <paramref name="at"/> facing <paramref name="normal"/>; with
        /// <paramref name="grow"/> it spreads out over that many seconds (the pool), else it pops out. The oldest splat goes round.
        /// </summary>
        public void Splat(Vector3 at, Vector3 normal, float radius, Color blood, float grow = 0)
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
            Tint(model, blood);
            model.transform.gameObject.SetActive(true);
            PlaceSplat(i);
        }

        /// <summary>A few drops of blood, for the trails of the rolling eyes and hands.</summary>
        public void Drip(Vector3 at, Vector3 velocity, Color blood, int count = 1)
        {
            var particles = Particles;
            if (particles == null)
                return;
            for (var i = 0; i < count; i++)
                particles.Emit(FxParticles.Kind.Drop, at + Random.insideUnitSphere * 0.04f, velocity * 0.3f + Random.insideUnitSphere * 0.6f, Random.Range(0.25f, 0.5f),
                    Random.Range(0.012f, 0.024f), blood, BloodGlowOf(blood), gravity: 9.8f);
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
                    Drip(hit.point, -velocity * 0.2f, g.blood, 3);
                    return;
                }
                // Splats bigger than the gob, as it spreads; a fast one longer along the way it went
                Splat(hit.point, hit.normal, g.radius * Random.Range(2.2f, 3.4f), g.blood);
                Drip(hit.point + hit.normal * 0.03f, Vector3.Reflect(velocity, hit.normal) * 0.4f, g.blood, 2);
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
            var shrink = 1 - Mathf.SmoothStep(0, 1, (age - splatStay * SdfMushrooms.QualityExtras.GoreStay) / splatShrink);
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

        /// <summary>A key for a blood colour in the tint cache (8 bits a channel; never 0, which is no tint).</summary>
        private static int KeyOf(Color blood) =>
            1 + (Mathf.RoundToInt(blood.r * 255) << 16 | Mathf.RoundToInt(blood.g * 255) << 8 | Mathf.RoundToInt(blood.b * 255));

        private static void Tint(Model model, Color blood)
        {
            var key = KeyOf(blood);
            if (model.tint == key)
                return;
            model.tint = key;
            for (var i = 0; i < model.filters.Length; i++)
                model.filters[i].sharedMesh = Tinted(model.meshes[i], key, blood, 1);
        }

        /// <summary>A head piece charred by the flamethrower: its colours darkened, its own hues kept.</summary>
        public static Mesh Charred(Mesh mesh) => Tinted(mesh, -1, null, 0.3f);

        /// <summary>
        /// A copy of a model's mesh with its colours turned to <paramref name="blood"/> (or kept) and darkened (<see cref="GoreRules.Tint"/>):
        /// the base colour, the variation's, the speckles' and the glowing edge's (vertex colour, UV3, UV4, UV6, as MushroomBuilder packs
        /// them). Made once per mesh and <paramref name="key"/>; a mesh that cannot be read is used as it is.
        /// </summary>
        private static Mesh Tinted(Mesh source, int key, Color? blood, float darken)
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
                colours[v] = Turn(colours[v], blood, darken);
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
                    var turned = Turn(new Color(c.x, c.y, c.z), blood, darken);
                    channel[v] = new Vector4(turned.r, turned.g, turned.b, c.w);
                }
                mesh.SetUVs(uv, channel);
            }
            mesh.UploadMeshData(true);
            TintCache[(source, key)] = mesh;
            return mesh;
        }

        private static Color Turn(Color colour, Color? blood, float darken)
        {
            if (blood is { } to)
                return GoreRules.Tint(colour, to, darken);
            return new Color(colour.r * darken, colour.g * darken, colour.b * darken, colour.a);
        }
    }
}
