using System.Collections.Generic;
using SdfEffects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfBattle
{
    /// <summary>
    /// The look of combat (ticket 22), reusing ticket 8's effects: every streak, ball and particle in one <see cref="FxBuffer"/> (one
    /// draw for all of them) and one <see cref="FxParticles"/> pool. Each shooter (a player, or a machine, ticket 24) gets its own
    /// <see cref="TracerGun"/> per bullet weapon (pistol, minigun, sniper, in their colours) and two <see cref="LightningGun"/>s (the
    /// electro-gun's bolt, and one per jump), fired at the hits <see cref="Combat"/> found. Made the same way: the flamethrower's flame
    /// (glowing motes and smoke puffs streaming along the aim, stopped by walls), the poison cloud (lumpy toon balls hugging the ground
    /// and motes rising through it) and its ball in flight, and the blob-gun's gobs and splats (the goop models, pooled). Nothing is
    /// made per shot; the effects are stepped here once a frame, after everyone fired.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class CombatFx : MonoBehaviour
    {
        private const int GobPool = 12;
        private const int SplatPool = 16;
        private const int CloudLumps = 11;
        private const int CloudMotes = 16;

        private static readonly Color Poison = new Color(0.46f, 0.8f, 0.2f);
        private static readonly Color PoisonGlow = new Color(0.16f, 0.4f, 0.04f);
        private static readonly Color PoisonMote = new Color(0.7f, 1f, 0.35f);
        private static readonly Color Goo = new Color(0.85f, 0.18f, 0.65f);
        private static readonly Color GooGlow = new Color(0.4f, 0.06f, 0.3f);
        private static readonly Color FlameBand = new Color(1f, 0.42f, 0.06f);
        private static readonly Color FlameCore = new Color(1f, 0.9f, 0.45f);
        private static readonly Color Smoke = new Color(0.3f, 0.26f, 0.24f);
        private static readonly Color SteamBand = new Color(0.9f, 0.88f, 0.84f);
        private static readonly Color SteamCore = new Color(1f, 1f, 0.97f);

        private sealed class Shooter
        {
            public readonly Dictionary<string, TracerGun> tracers = new Dictionary<string, TracerGun>();
            public LightningGun bolt;
            public LightningGun[] jumps = new LightningGun[0];
            public Transform[] jumpFrom = new Transform[0];
        }

        private sealed class Splat
        {
            public Area area;
            public Transform model;
            public float size;
            public Quaternion rotation;
        }

        private readonly Dictionary<Object, Shooter> shooters = new Dictionary<Object, Shooter>();
        private readonly List<FxEffect> effects = new List<FxEffect>();
        private readonly List<Splat> splats = new List<Splat>();
        private Combat combat;
        private WeaponTableAsset assets;
        private FxBuffer buffer;
        private FxParticles particles;
        private FxRandom random;
        private Transform[] gobs;
        private Transform[] splatModels;
        private float[] splatSizes;
        private int nextGob;
        private int nextSplat;

        /// <summary>The effects of a match's combat: one object at the world's origin (the buffer's bounds are round it).</summary>
        public static CombatFx Create(Combat combat)
        {
            var root = new GameObject("Combat Effects");
            root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var assets = combat.Assets;
            if (assets != null && assets.effectMaterial != null)
                renderer.sharedMaterial = assets.effectMaterial;
            else
                Debug.LogWarning("Combat: the weapon table has no effect material (Assets/Effects/Materials/Effect.mat); shots draw no effects");
            var fx = root.AddComponent<CombatFx>();
            fx.combat = combat;
            fx.assets = assets;
            fx.buffer = root.AddComponent<FxBuffer>();
            fx.particles = root.AddComponent<FxParticles>();
            // The level's ground is not one plane: no bounce, the particles are short-lived
            fx.particles.groundHeight = -10000;
            fx.random = new FxRandom(22);
            fx.MakePools(root.transform);
            combat.State.AreaAdded += fx.OnAreaAdded;
            combat.State.AreaEnded += fx.OnAreaEnded;
            return fx;
        }

        private void OnDestroy()
        {
            if (combat != null && combat.State != null)
            {
                combat.State.AreaAdded -= OnAreaAdded;
                combat.State.AreaEnded -= OnAreaEnded;
            }
        }

        private void MakePools(Transform root)
        {
            gobs = Pool(root, "Gobs", assets != null ? assets.blobModels : null, GobPool, out _);
            splatModels = Pool(root, "Splats", assets != null ? assets.splatModels : null, SplatPool, out splatSizes);
        }

        /// <summary>Inactive copies of the models, round-robin; with each copy's radius across (x, z) at scale 1.</summary>
        private static Transform[] Pool(Transform root, string name, GameObject[] prefabs, int count, out float[] radii)
        {
            radii = null;
            if (prefabs == null || prefabs.Length == 0)
                return null;
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            var pool = new List<Transform>();
            var sizes = new List<float>();
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
                var bounds = new Bounds();
                var first = true;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    if (first)
                        bounds = renderer.bounds;
                    else
                        bounds.Encapsulate(renderer.bounds);
                    first = false;
                }
                sizes.Add(Mathf.Max(0.05f, Mathf.Max(bounds.extents.x, bounds.extents.z)));
                instance.SetActive(false);
                pool.Add(instance.transform);
            }
            radii = sizes.ToArray();
            return pool.Count > 0 ? pool.ToArray() : null;
        }

        /// <summary>The particle pool every effect draws into (ticket 24's machine bursts emit into it too).</summary>
        public FxParticles Particles => particles;

        // ---------------------------------------------------------------- Shots

        private Shooter ShooterOf(Object owner)
        {
            if (shooters.TryGetValue(owner, out var shooter))
                return shooter;
            shooter = new Shooter();
            shooters[owner] = shooter;
            return shooter;
        }

        private T Add<T>() where T : FxEffect
        {
            var effect = gameObject.AddComponent<T>();
            effect.Initialize(buffer, particles, random);
            effect.ResetEffect();
            effects.Add(effect);
            return effect;
        }

        /// <summary>
        /// A bullet's tracer from <paramref name="muzzle"/> to <paramref name="end"/>, in the colours of <paramref name="weapon"/>'s look,
        /// landing there on a surface facing <paramref name="normal"/> (or flying on), with the muzzle flash. <paramref name="owner"/> is
        /// the shooter: a player, or a machine's component.
        /// </summary>
        public void Tracer(Object owner, string weapon, Transform muzzle, Vector3 end, bool landed, Vector3 normal)
        {
            if (muzzle == null || owner == null)
                return;
            var shooter = ShooterOf(owner);
            var model = WeaponIds.ModelOf(weapon);
            if (!shooter.tracers.TryGetValue(model, out var gun))
            {
                gun = Add<TracerGun>();
                var look = assets != null ? assets.LookOf(model) : null;
                if (look != null)
                {
                    gun.band = look.tracer;
                    gun.tracerWidth = look.tracerWidth;
                    gun.tracerLength = look.tracerLength;
                    gun.speed = look.tracerSpeed;
                    gun.trail = look.tracerTrail;
                    gun.flashSize = look.flashSize;
                }
                // Your own shots leave from the small view model right by the camera: a smaller flash and thinner streaks
                if (owner is Player { IsYou: true })
                {
                    gun.flashSize *= 0.35f;
                    gun.tracerWidth *= 0.5f;
                }
                shooter.tracers[model] = gun;
            }
            gun.muzzle = muzzle;
            gun.ShootAt(end, landed, normal);
        }

        /// <summary>The electro-gun's bolt from the muzzle to <paramref name="end"/>.</summary>
        public void Bolt(Object owner, Transform muzzle, Vector3 end, Vector3 normal)
        {
            if (muzzle == null || owner == null)
                return;
            var shooter = ShooterOf(owner);
            if (shooter.bolt == null)
            {
                shooter.bolt = Add<LightningGun>();
                if (owner is Player { IsYou: true })
                    shooter.bolt.width *= 0.6f;
            }
            shooter.bolt.muzzle = muzzle;
            shooter.bolt.Strike(end, normal);
        }

        /// <summary>The bolt's <paramref name="index"/>th jump, from one enemy to the next: a thinner bolt, thinner with each jump.</summary>
        public void Jump(Object owner, int index, Vector3 from, Vector3 to)
        {
            if (owner == null || index < 0)
                return;
            var shooter = ShooterOf(owner);
            while (shooter.jumps.Length <= index)
            {
                var n = shooter.jumps.Length;
                var gun = Add<LightningGun>();
                gun.width *= 0.65f * Mathf.Pow(0.8f, n);
                var point = new GameObject($"Jump {n + 1}").transform;
                point.SetParent(transform, false);
                gun.muzzle = point;
                shooter.jumps = Append(shooter.jumps, gun);
                shooter.jumpFrom = Append(shooter.jumpFrom, point);
            }
            shooter.jumpFrom[index].position = from;
            shooter.jumps[index].Strike(to, (from - to).normalized);
        }

        private static T[] Append<T>(T[] array, T item)
        {
            var longer = new T[array.Length + 1];
            array.CopyTo(longer, 0);
            longer[array.Length] = item;
            return longer;
        }

        /// <summary>
        /// The flame for one frame of <paramref name="dt"/>: hot motes and a little smoke streaming from the muzzle along the aim,
        /// spreading as they go, dying where the flame ends (<paramref name="length"/>: its range, or the wall in the way). Each mote
        /// grows from a fifth of its size over its first 0.2 s, so the flame is small at the muzzle and does not hide the view.
        /// </summary>
        public void Flame(Transform muzzle, Vector3 aim, float length, float dt, bool firstPerson)
        {
            const float speed = 11;
            var start = muzzle.position;
            var life = Mathf.Max(0.05f, length / speed);
            var count = Mathf.Max(1, Mathf.RoundToInt(dt * 70));
            var scale = firstPerson ? 0.8f : 1;
            for (var i = 0; i < count; i++)
            {
                var direction = random.Around(aim, 0.13f);
                particles.Emit(FxParticles.Kind.Spore, start + direction * random.Range(0, 0.15f), direction * speed * random.Range(0.85f, 1.1f),
                    life * random.Range(0.8f, 1f), random.Range(0.12f, 0.24f) * scale, Hdr(FlameBand, 1.3f), Hdr(FlameCore, 2.4f), gravity: -1.5f, drag: 0.3f,
                    growIn: 0.2f, growFrom: 0.2f);
            }
            if (random.Value < dt * 12)
                particles.Emit(FxParticles.Kind.Puff, start + aim * length * random.Range(0.5f, 0.9f), random.Around(aim + Vector3.up, 0.4f) * 1.2f,
                    random.Range(0.5f, 0.9f), random.Range(0.18f, 0.3f), Smoke, Color.black, gravity: -1.2f, drag: 1.5f);
        }

        /// <summary>
        /// The toy train's chimney puff (ticket 27.10) at <paramref name="at"/>, drifting back along <paramref name="back"/> as it rises: a
        /// few pale toon puffs; the whistle's (<paramref name="whistle"/>) are more, bigger and rise higher, to read from across the room.
        /// </summary>
        public void TrainPuff(Vector3 at, Vector3 back, bool whistle)
        {
            var count = whistle ? 7 : 3;
            for (var i = 0; i < count; i++)
                particles.Emit(FxParticles.Kind.Puff, at + random.Around(Vector3.up, 0.6f) * random.Range(0, 0.25f),
                    random.Around(Vector3.up * (whistle ? 4.5f : 2.6f) + back * 1.5f, 0.35f), random.Range(1.2f, 1.8f) * (whistle ? 1.3f : 1),
                    random.Range(0.35f, 0.55f) * (whistle ? 1.5f : 1), SteamBand, SteamCore, gravity: -0.5f, drag: 0.8f);
        }

        /// <summary>A lobbed shot left the muzzle: a little puff in its colour.</summary>
        public void Lob(FireMode mode, Transform muzzle)
        {
            if (muzzle == null)
                return;
            var colour = mode == FireMode.Cloud ? Poison : Goo;
            var glow = mode == FireMode.Cloud ? PoisonGlow : GooGlow;
            for (var i = 0; i < 4; i++)
                particles.Emit(FxParticles.Kind.Drop, muzzle.position, random.Around(muzzle.forward, 0.5f) * random.Range(1, 2.5f), random.Range(0.15f, 0.3f),
                    random.Range(0.01f, 0.02f), colour, glow, gravity: 9.8f);
        }

        /// <summary>Where a lobbed shot lands: a splash of drops (the blob) or a burst of poison puffs.</summary>
        public void Burst(FireMode mode, Vector3 at, Vector3 normal)
        {
            if (mode == FireMode.Cloud)
            {
                for (var i = 0; i < 10; i++)
                    particles.Emit(FxParticles.Kind.Puff, at + normal * 0.2f, random.Around(normal, 1) * random.Range(1.5f, 3.5f), random.Range(0.4f, 0.8f),
                        random.Range(0.2f, 0.35f), Poison, PoisonGlow, gravity: -0.5f, drag: 3);
                return;
            }
            var along = Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized;
            for (var i = 0; i < 8; i++)
                particles.Emit(FxParticles.Kind.Drop, at + normal * 0.03f, random.Around(normal + along * 0.6f, 0.8f) * random.Range(1.2f, 2.8f),
                    random.Range(0.35f, 0.6f), random.Range(0.015f, 0.03f), Goo, GooGlow, gravity: 9.8f);
        }

        // ---------------------------------------------------------------- The blob's gobs and splats

        /// <summary>A gob from the pool for a blob in flight; -1 when there are no goop models (a ball is drawn instead).</summary>
        public int TakeGob()
        {
            if (gobs == null)
                return -1;
            var i = nextGob;
            nextGob = (nextGob + 1) % gobs.Length;
            gobs[i].gameObject.SetActive(true);
            return i;
        }

        /// <summary>A gob flies on: stretched along its flight with speed, wobbling.</summary>
        public void MoveGob(int gob, Vector3 position, Vector3 velocity, float age)
        {
            if (gobs == null || gob < 0)
                return;
            var stretch = 1 + Mathf.Clamp(velocity.magnitude * 0.045f, 0, 0.5f);
            var wobble = Mathf.Sin(age * 38 + gob) * 0.08f;
            var across = 1 / Mathf.Sqrt(stretch);
            gobs[gob].SetPositionAndRotation(position, Quaternion.LookRotation(velocity.sqrMagnitude > 1e-6f ? velocity : Vector3.forward));
            gobs[gob].localScale = new Vector3(across * (1 + wobble), across * (1 - wobble), stretch) * 1.4f;
        }

        public void EndGob(int gob)
        {
            if (gobs != null && gob >= 0)
                gobs[gob].gameObject.SetActive(false);
        }

        private void OnAreaAdded(Area area)
        {
            if (area.Kind != FireMode.Blob || splatModels == null)
                return;
            var i = nextSplat;
            nextSplat = (nextSplat + 1) % splatModels.Length;
            // The oldest splat's model goes round to the new one
            splats.RemoveAll(s => s.model == splatModels[i]);
            var model = splatModels[i];
            model.gameObject.SetActive(true);
            splats.Add(new Splat
            {
                area = area,
                model = model,
                size = area.Radius / splatSizes[i],
                rotation = Quaternion.AngleAxis(Random.Range(0, 360f), Vector3.up),
            });
        }

        private void OnAreaEnded(Area area)
        {
            for (var i = splats.Count - 1; i >= 0; i--)
            {
                if (splats[i].area != area)
                    continue;
                splats[i].model.gameObject.SetActive(false);
                splats.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- Every frame

        private void LateUpdate()
        {
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            buffer.Begin();
            foreach (var effect in effects)
                effect.Tick(dt);
            foreach (var area in combat.State.Areas)
                if (area.Kind == FireMode.Cloud)
                    DrawCloud(area);
            foreach (var splat in splats)
                PlaceSplat(splat);
            foreach (var projectile in combat.Projectiles)
                DrawProjectile(projectile);
            particles.Tick(dt, buffer);
            buffer.End();
        }

        private void DrawProjectile(Combat.Projectile projectile)
        {
            if (projectile.stats.mode == FireMode.Cloud)
            {
                buffer.Lit(projectile.position, 0.09f, Poison, PoisonGlow);
                buffer.Glow(projectile.position - projectile.velocity.normalized * 0.12f, 0.06f, Hdr(Poison, 1.2f), Hdr(PoisonMote, 2), 0.5f, 0.7f);
            }
            else if (projectile.gob < 0)
            {
                buffer.Lit(projectile.position, 0.11f, Goo, GooGlow);
            }
        }

        /// <summary>
        /// A poison cloud: lumps of toon-shaded green gas low over the ground round its middle, slowly turning, and motes rising through
        /// it; it swells in at the start and thins away at the end.
        /// </summary>
        private void DrawCloud(Area area)
        {
            var age = area.Duration - area.TimeLeft;
            var grow = Mathf.SmoothStep(0.2f, 1, age / 0.4f);
            var fade = Mathf.Clamp01(area.TimeLeft / 0.8f);
            var seed = area.GetHashCode() & 0xFFFF;
            var r = area.Radius;
            var time = Time.time;
            for (var i = 0; i < CloudLumps; i++)
            {
                var angle = Hash(seed, i) * 6.283f + time * 0.15f * (i % 2 == 0 ? 1 : -1);
                var distance = Mathf.Sqrt(Hash(seed, i + 40)) * r * 0.7f * grow;
                var height = -0.6f + Hash(seed, i + 80) * 0.9f + 0.08f * Mathf.Sin(time * 1.3f + i);
                var position = area.Centre + new Vector3(Mathf.Cos(angle) * distance, height, Mathf.Sin(angle) * distance);
                var size = Mathf.Lerp(0.45f, 0.85f, Hash(seed, i + 120)) * r / 3 * grow * Mathf.Lerp(0.5f, 1, fade);
                buffer.Lit(position, size, Poison, PoisonGlow, 0.85f * fade);
            }
            for (var i = 0; i < CloudMotes; i++)
            {
                var angle = Hash(seed, i + 200) * 6.283f;
                var distance = Mathf.Sqrt(Hash(seed, i + 240)) * r * 0.9f;
                var rise = Mathf.Repeat(time * 0.25f + Hash(seed, i + 280), 1);
                var position = area.Centre + new Vector3(Mathf.Cos(angle) * distance, -0.8f + rise * r * 0.9f, Mathf.Sin(angle) * distance);
                buffer.Glow(position, 0.05f + 0.04f * Hash(seed, i + 320), Hdr(Poison, 1.1f), Hdr(PoisonMote, 1.8f), 0.45f, fade * (1 - rise * rise));
            }
        }

        /// <summary>A splat on the ground: pops out, stays, then shrinks back into the ground at its end.</summary>
        private static void PlaceSplat(Splat splat)
        {
            var area = splat.area;
            var age = area.Duration - area.TimeLeft;
            var pop = age < 0.12f ? Mathf.Lerp(0.55f, 1.12f, age / 0.12f) : Mathf.Lerp(1.12f, 1, Mathf.Clamp01((age - 0.12f) / 0.1f));
            var shrink = Mathf.SmoothStep(0, 1, area.TimeLeft / 1.2f);
            var size = splat.size * pop * Mathf.Max(0.02f, shrink);
            splat.model.SetPositionAndRotation(area.Centre + Vector3.up * 0.01f, splat.rotation);
            splat.model.localScale = new Vector3(size, splat.size * pop * Mathf.Sqrt(Mathf.Max(0.02f, shrink)), size);
        }

        private static float Hash(int seed, int i)
        {
            var x = Mathf.Sin(seed * 0.1373f + i * 12.9898f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        private static Color Hdr(Color color, float intensity) => new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1);
    }
}
