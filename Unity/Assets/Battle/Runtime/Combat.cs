using System;
using System.Collections.Generic;
using SdfPickups;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SdfBattle
{
    /// <summary>
    /// Combat in a battle scene (ticket 22), on the Battle object next to the <see cref="Match"/>, which makes it at the start: the
    /// rules (<see cref="State"/>, <see cref="CombatState"/>: inventories, health, burns, slows, clouds and splats), every player's
    /// <see cref="Arsenal"/>, and what only the world knows: what a shot hits.
    /// <list type="bullet">
    /// <item>Hitscan (pistol, minigun, sniper): a ray from the eye against the players' capsules and the level's colliders (triggers,
    /// like the pods', are passed through), and against each player's head: the scout's cap, an ellipsoid over the capsule tested here
    /// rather than a collider of its own (so no trigger of the flags or pods ever meets it). The damage by distance and head from the
    /// weapon table.</item>
    /// <item>The electro-gun's bolt jumps from an enemy it hits on to up to two more, each the nearest enemy not hit yet within its jump
    /// range of the last one, in sight.</item>
    /// <item>The flamethrower hurts every enemy in its cone and in sight, ten times a second, and sets them burning.</item>
    /// <item>The poison- and blob-gun lob projectiles, stepped here with gravity: a cloud where the poison lands, a hit and a slow where a
    /// blob hits, and a slowing splat on the ground under it.</item>
    /// <item>The pods: a weapon pod is not taken by a player who holds its weapon, a health pod not by one at the overheal limit (200)
    /// (<see cref="WeaponPickup.CanPickUp"/>, <see cref="HealthPickup.CanPickUp"/>); taking one fills the inventory or heals (the table's <see cref="WeaponTable.healthPack"/>, 40, not the pod's own amount: one health a
    /// point to 100, half above, up to 200).</item>
    /// <item>The blob's slow, as the controller's <see cref="SdfLevel.FirstPersonController.slowMultiplier"/>.</item>
    /// </list>
    /// Machines (ticket 24) are <see cref="IDamageable"/>s the shots find behind their colliders; they hurt players through
    /// <see cref="Hurt"/> with a <see cref="Damage.ByMachine"/>, and draw their tracers with <see cref="CombatFx.Tracer"/>.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class Combat : MonoBehaviour
    {
        [Tooltip("The weapons' numbers and looks; empty: Resources/Weapons, or the first balance without models")]
        public WeaponTableAsset weapons;
        [Tooltip("Each player's head (the scout's cap), where a sniper shot is a headshot: an ellipsoid, its middle this high over the feet")]
        public float headCentre = 1.41f;
        [Tooltip("The head's radii: across, and up")]
        public Vector2 headRadii = new Vector2(0.6f, 0.22f);
        [Tooltip("Radius of a lobbed projectile, for what it hits")]
        public float projectileRadius = 0.12f;

        /// <summary>A lobbed shot in flight: the poison-gun's ball, the blob-gun's blob.</summary>
        public struct Projectile
        {
            public Player owner;
            public WeaponStats stats;
            public Vector3 position;
            public Vector3 velocity;
            public float age;
            /// <summary>The blob's model in the effects' pool, or -1.</summary>
            public int gob;
        }

        /// <summary>What a shot hit: the nearest collider along it that counts, and the player or machine behind it.</summary>
        public struct ShotHit
        {
            public bool hit;
            public Vector3 point;
            public Vector3 normal;
            public float distance;
            public Player player;
            public IDamageable target;
            public bool head;
        }

        private sealed class NearestFirst : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[48];
        private static readonly NearestFirst ByDistance = new NearestFirst();

        private readonly List<Projectile> projectiles = new List<Projectile>();
        private readonly List<IDamageable> targets = new List<IDamageable>();
        private readonly List<Arsenal> arsenals = new List<Arsenal>();

        /// <summary>The combat of the active battle scene; null where there is none.</summary>
        public static Combat Current { get; private set; }

        public Match Match { get; private set; }

        public CombatState State { get; private set; }

        public WeaponTable Table => State.Weapons;

        public WeaponTableAsset Assets => weapons;

        public CombatFx Fx { get; private set; }

        public IReadOnlyList<Projectile> Projectiles => projectiles;

        /// <summary>A hit landed: who shot, the player hit (null for a machine), whether it killed, whether it was the head.</summary>
        public event Action<Player, Player, bool, bool> HitLanded;

        /// <summary>Makes the match's combat; <see cref="Match"/> calls it after making its players and before the first round.</summary>
        public static Combat Create(Match match)
        {
            var combat = match.GetComponent<Combat>();
            if (combat == null)
                combat = match.gameObject.AddComponent<Combat>();
            combat.Attach(match);
            return combat;
        }

        private void Attach(Match match)
        {
            Match = match;
            if (weapons == null)
                weapons = WeaponTableAsset.Load();
            if (weapons == null)
                Debug.LogWarning("Combat: no weapon table (Assets/Battle/Resources/Weapons.asset); the first balance, and no weapon models");
            State = new CombatState(match.State, weapons != null ? weapons.table : WeaponTable.Default());
            State.PositionOf = p => match.PlayerOf(p) is { } body ? body.transform.position : new Vector3(0, -10000, 0);
            Fx = CombatFx.Create(this);
            CombatHud.Create(this);

            foreach (var player in match.Players)
            {
                var arsenal = player.gameObject.AddComponent<Arsenal>();
                arsenal.Bind(this, player);
                arsenals.Add(arsenal);
            }

            match.State.Spawned += OnSpawned;
            match.State.Died += OnDied;
            match.State.RoundReset += OnRoundReset;
            WeaponPickup.CanPickUp = CanTakeWeapon;
            WeaponPickup.PickedUp += OnWeaponPickedUp;
            HealthPickup.CanPickUp = CanTakeHealth;
            HealthPickup.PickedUp += OnHealthPickedUp;
            Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
            if (WeaponPickup.CanPickUp == (Func<WeaponPickup, GameObject, bool>)CanTakeWeapon)
                WeaponPickup.CanPickUp = null;
            if (HealthPickup.CanPickUp == (Func<HealthPickup, GameObject, bool>)CanTakeHealth)
                HealthPickup.CanPickUp = null;
            WeaponPickup.PickedUp -= OnWeaponPickedUp;
            HealthPickup.PickedUp -= OnHealthPickedUp;
            if (Match != null && Match.State != null)
            {
                Match.State.Spawned -= OnSpawned;
                Match.State.Died -= OnDied;
                Match.State.RoundReset -= OnRoundReset;
            }
        }

        /// <summary>The arsenal of a player.</summary>
        public Arsenal ArsenalOf(Player player) => player != null ? player.GetComponent<Arsenal>() : null;

        /// <summary>Adds a machine (ticket 24) to what the electro-gun's jump and the flamethrower's cone look for.</summary>
        public void Register(IDamageable target)
        {
            if (target != null && !targets.Contains(target))
                targets.Add(target);
        }

        public void Unregister(IDamageable target) => targets.Remove(target);

        /// <summary>Hurts a player with a damage from anything (a machine's shot, ticket 24); returns the health it took.</summary>
        public float Hurt(Player victim, Damage damage) => victim != null ? State.Hurt(victim.State, damage) : 0;

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (State == null)
                return;
            var dt = Time.deltaTime;
            State.Tick(dt);
            foreach (var player in Match.Players)
                player.Controller.slowMultiplier = State.SpeedFactor(player.State);
            StepProjectiles(dt);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // A test key, as the bots do not shoot yet: F9 hurts you for 30, from in front, so the health pod can be tried
            if (Input.GetKeyDown(KeyCode.F9))
                HurtYou();
#endif
        }

        /// <summary>Hurts you for 30 from a few metres in front (a test of health, the damage HUD and the health pod).</summary>
        [ContextMenu("Test: Hurt You 30")]
        public void HurtYou()
        {
            var you = Match != null ? Match.You : null;
            if (you == null || State == null)
                return;
            var from = you.transform.position + you.transform.forward * 5 + Vector3.up;
            State.Hurt(you.State, Damage.ByMachine("Test", you.Team.Other(), "test", 30, from));
        }

        // ---------------------------------------------------------------- Firing (the arsenals call it)

        /// <summary>One shot of the arsenal's weapon from <paramref name="eye"/> along <paramref name="aim"/>, spread up to <paramref name="spread"/> degrees.</summary>
        public void Fire(Arsenal arsenal, Vector3 eye, Vector3 aim, float spread)
        {
            var stats = arsenal.Weapon;
            if (stats == null)
                return;
            switch (stats.mode)
            {
                case FireMode.Hitscan:
                    FireHitscan(arsenal, stats, eye, Spread(aim, spread));
                    break;
                case FireMode.Chain:
                    FireBolt(arsenal, stats, eye, Spread(aim, spread));
                    break;
                case FireMode.Cone:
                    FireCone(arsenal, stats, eye, aim);
                    break;
                case FireMode.Cloud:
                case FireMode.Blob:
                    Launch(arsenal, stats, eye, Spread(aim, spread));
                    break;
            }
        }

        private void FireHitscan(Arsenal arsenal, WeaponStats stats, Vector3 eye, Vector3 direction)
        {
            var shooter = arsenal.Player;
            var hit = Trace(eye, direction, stats.range, shooter);
            if (hit.hit)
                Strike(shooter, stats.id, hit, stats.DamageAt(hit.distance, hit.head), eye, hit.head && stats.headDamage > 0);
            var end = hit.hit ? hit.point : eye + direction * stats.range;
            Fx.Tracer(shooter, stats.id, arsenal.Muzzle, end, hit.hit, hit.normal);
        }

        private void FireBolt(Arsenal arsenal, WeaponStats stats, Vector3 eye, Vector3 direction)
        {
            var shooter = arsenal.Player;
            var hit = Trace(eye, direction, stats.range, shooter);
            var end = hit.hit ? hit.point : eye + direction * stats.range;
            Fx.Bolt(shooter, arsenal.Muzzle, end, hit.hit ? hit.normal : -direction);
            if (!hit.hit)
                return;
            Strike(shooter, stats.id, hit, stats.DamageAt(hit.distance), eye, false);

            // The jumps: only off an enemy, each to the nearest enemy not hit yet within reach of the last one hit and in its sight
            var enemyHit = (hit.player != null && hit.player.Team != shooter.Team) || (hit.target != null && hit.target.Team != shooter.Team);
            if (!enemyHit || stats.chainRange <= 0)
                return;
            var lastPlayer = hit.player;
            var from = hit.point + hit.normal * 0.05f;
            for (var jump = 0; jump < stats.chainJumps; jump++)
            {
                Player nextPlayer = null;
                IDamageable nextTarget = null;
                var nearest = stats.chainRange;
                foreach (var player in Match.Players)
                {
                    if (player == shooter || player == hit.player || Struck(player, null) || !State.CanHurt(player.State, shooter.Team))
                        continue;
                    var centre = Centre(player);
                    var distance = Vector3.Distance(from, centre);
                    if (distance < nearest && InSight(from, centre, lastPlayer, player, null))
                        (nearest, nextPlayer, nextTarget) = (distance, player, null);
                }
                foreach (var target in targets)
                {
                    if (target == hit.target || Struck(null, target) || !target.Alive || !State.CanHurt(target.Team, shooter.Team))
                        continue;
                    var distance = Vector3.Distance(from, target.Center);
                    if (distance < nearest && InSight(from, target.Center, lastPlayer, null, target))
                        (nearest, nextPlayer, nextTarget) = (distance, null, target);
                }
                if (nextPlayer == null && nextTarget == null)
                    break;
                var to = nextPlayer != null ? Centre(nextPlayer) : nextTarget.Center;
                var next = new ShotHit { hit = true, point = to, normal = (from - to).normalized, player = nextPlayer, target = nextTarget };
                Strike(shooter, stats.id, next, stats.chainDamage, from, false);
                Fx.Jump(shooter, jump, from, to);
                if (nextPlayer != null)
                    jumpedPlayers.Add(nextPlayer);
                else
                    jumpedTargets.Add(nextTarget);
                (lastPlayer, from) = (nextPlayer, to);
            }
            jumpedPlayers.Clear();
            jumpedTargets.Clear();
        }

        private readonly List<Player> jumpedPlayers = new List<Player>();
        private readonly List<IDamageable> jumpedTargets = new List<IDamageable>();

        /// <summary>Whether this bolt already jumped to a player or a machine.</summary>
        private bool Struck(Player player, IDamageable target) =>
            player != null ? jumpedPlayers.Contains(player) : jumpedTargets.Contains(target);

        private void FireCone(Arsenal arsenal, WeaponStats stats, Vector3 eye, Vector3 aim)
        {
            var shooter = arsenal.Player;
            var cos = Mathf.Cos(stats.coneAngle * Mathf.Deg2Rad);
            var amount = stats.ConeDamagePerShot;
            foreach (var player in Match.Players)
            {
                if (player == shooter || !State.CanHurt(player.State, shooter.Team))
                    continue;
                if (!InCone(eye, aim, player.transform.position, stats.range, cos, out var point) || !InSight(eye, point, shooter, player, null))
                    continue;
                var hit = new ShotHit { hit = true, point = point, normal = -aim, player = player, distance = Vector3.Distance(eye, point) };
                Strike(shooter, stats.id, hit, amount, eye, false);
                State.Burn(player.State, Damage.By(shooter.State, stats.id, stats.burnPerSecond, eye), stats.burnSeconds);
            }
            foreach (var target in targets)
            {
                if (!target.Alive || !State.CanHurt(target.Team, shooter.Team))
                    continue;
                var centre = target.Center;
                var to = centre - eye;
                if (to.magnitude > stats.range || Vector3.Dot(to.normalized, aim) < cos || !InSight(eye, centre, shooter, null, target))
                    continue;
                HurtTarget(shooter, stats.id, target, amount, centre, eye);
            }
        }

        /// <summary>
        /// Whether a player standing at <paramref name="feet"/> is in the cone: some point of their body (feet, middle, head) within
        /// its range and angle; that point.
        /// </summary>
        private static bool InCone(Vector3 eye, Vector3 aim, Vector3 feet, float range, float cos, out Vector3 point)
        {
            for (var i = 0; i < 3; i++)
            {
                point = feet + Vector3.up * (0.35f + i * 0.55f);
                var to = point - eye;
                var distance = to.magnitude;
                // Within the body's radius of the cone counts too, so a player right in front is never missed
                if (distance <= range + 0.3f && (distance < 0.6f || Vector3.Dot(to / distance, aim) >= cos - 0.3f / Mathf.Max(1, distance)))
                    return true;
            }
            point = default;
            return false;
        }

        /// <summary>How far the flame reaches along the aim before a wall: its range, or less.</summary>
        public float FlameLength(Vector3 eye, Vector3 aim, float range)
        {
            var count = Physics.RaycastNonAlloc(eye, aim, Hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var length = range;
            for (var i = 0; i < count; i++)
                if (Hits[i].distance < length && Player.Of(Hits[i].collider) == null)
                    length = Hits[i].distance;
            return length;
        }

        private void Launch(Arsenal arsenal, WeaponStats stats, Vector3 eye, Vector3 direction)
        {
            // Lifted over the aim, so even a level shot arcs
            var right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude < 1e-4f)
                right = arsenal.transform.right;
            direction = Quaternion.AngleAxis(-stats.projectileLift, right.normalized) * direction;
            var muzzle = arsenal.Muzzle;
            // From the muzzle unless a wall stands between it and the eye
            var start = eye;
            if (muzzle != null && !Physics.Linecast(eye, muzzle.position, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                start = muzzle.position;
            projectiles.Add(new Projectile
            {
                owner = arsenal.Player,
                stats = stats,
                position = start,
                velocity = direction * stats.projectileSpeed,
                gob = stats.mode == FireMode.Blob ? Fx.TakeGob() : -1,
            });
            Fx.Lob(stats.mode, muzzle);
        }

        private void StepProjectiles(float dt)
        {
            for (var i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                p.age += dt;
                var velocity = p.velocity + Vector3.down * (p.stats.projectileGravity * dt);
                var step = (p.velocity + velocity) * (0.5f * dt);
                var length = step.magnitude;
                var hit = length > 1e-5f ? Trace(p.position, step / length, length, p.owner, projectileRadius) : default;
                if (hit.hit)
                {
                    Land(p, hit, step / length);
                    Fx.EndGob(p.gob);
                    projectiles.RemoveAt(i);
                    continue;
                }
                p.position += step;
                p.velocity = velocity;
                if (p.age > 8 || p.position.y < -500)
                {
                    Fx.EndGob(p.gob);
                    projectiles.RemoveAt(i);
                    continue;
                }
                Fx.MoveGob(p.gob, p.position, p.velocity, p.age);
                projectiles[i] = p;
            }
        }

        private void Land(Projectile p, ShotHit hit, Vector3 direction)
        {
            var stats = p.stats;
            var owner = p.owner;
            if (stats.mode == FireMode.Cloud)
            {
                // The cloud's middle about a metre over the ground it lands on (or the player it hits), so it wraps whoever stands there
                var centre = hit.player != null ? hit.player.transform.position + Vector3.up
                    : hit.normal.y > 0.5f ? hit.point + Vector3.up * 0.9f
                    : hit.point + hit.normal * 0.6f;
                State.AddArea(new Area(FireMode.Cloud, centre, stats.areaRadius, stats.areaSeconds, Damage.By(owner.State, stats.id, stats.damage, centre),
                    stats.slow, stats.slowSeconds, stats.burnPerSecond, stats.burnSeconds));
                Fx.Burst(FireMode.Cloud, hit.point, hit.normal);
                return;
            }

            // The blob: a hit and a slow, then a splat on the ground under where it struck
            if (hit.player != null || hit.target != null)
            {
                Strike(owner, stats.id, hit, stats.damage, hit.point - direction * 2, false);
                if (hit.player != null)
                    State.Slow(hit.player.State, owner.Team, stats.slow, stats.slowSeconds);
            }
            Fx.Burst(FireMode.Blob, hit.point, hit.normal);
            if (Ground(hit.point + hit.normal * 0.1f, out var ground))
                State.AddArea(new Area(FireMode.Blob, ground, stats.areaRadius, stats.areaSeconds, Damage.By(owner.State, stats.id, 0, ground), stats.slow));
        }

        /// <summary>The ground under a point (not a player), within 4 m and flat enough to stand on.</summary>
        private static bool Ground(Vector3 from, out Vector3 ground)
        {
            var count = Physics.RaycastNonAlloc(from + Vector3.up * 0.2f, Vector3.down, Hits, 4.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, count, ByDistance);
            for (var i = 0; i < count; i++)
            {
                if (Player.Of(Hits[i].collider) != null)
                    continue;
                ground = Hits[i].point;
                return Hits[i].normal.y > 0.6f;
            }
            ground = default;
            return false;
        }

        // ---------------------------------------------------------------- Hits

        /// <summary>
        /// The nearest thing along a ray (or a ball of <paramref name="radius"/> swept along it) that a shot stops at: a solid collider
        /// (triggers are passed through), or a player's head; not <paramref name="ignore"/> (the shooter) or <paramref name="ignoreToo"/>,
        /// not the dead. A hit on the head, or high on the capsule (above the table's head height), has <see cref="ShotHit.head"/>.
        /// </summary>
        public ShotHit Trace(Vector3 origin, Vector3 direction, float range, Player ignore, float radius = 0, Player ignoreToo = null)
        {
            var count = radius > 0
                ? Physics.SphereCastNonAlloc(origin, radius, direction, Hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, direction, Hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, count, ByDistance);
            var result = default(ShotHit);
            for (var i = 0; i < count; i++)
            {
                var h = Hits[i];
                var player = Player.Of(h.collider);
                if (player != null && (player == ignore || player == ignoreToo || player.State == null || !player.State.Alive))
                    continue;
                // A swept ball reports what it started in at distance 0 with no point: not a hit along the way
                if (radius > 0 && h.distance <= 0 && h.point == Vector3.zero)
                    continue;
                result = new ShotHit { hit = true, point = h.point, normal = h.normal, distance = h.distance, player = player };
                if (player != null)
                    result.head = h.point.y - player.transform.position.y >= Table.headHeight;
                else
                    result.target = h.collider.GetComponentInParent<IDamageable>();
                break;
            }

            // The heads, wider than the capsule, before whatever the ray hit
            var reach = result.hit ? result.distance : range;
            foreach (var player in Match.Players)
            {
                if (player == ignore || player == ignoreToo || player.State == null || !player.State.Alive)
                    continue;
                var centre = player.transform.position + Vector3.up * headCentre;
                if (!Shots.HitEllipsoid(origin, direction, centre, headRadii.x + radius, headRadii.y + radius, out var t) || t > reach)
                    continue;
                reach = t;
                var point = origin + direction * t;
                var local = point - centre;
                var normal = new Vector3(local.x / Sq(headRadii.x + radius), local.y / Sq(headRadii.y + radius), local.z / Sq(headRadii.x + radius)).normalized;
                result = new ShotHit { hit = true, point = point - normal * radius, normal = normal, distance = t, player = player, head = true };
            }
            return result;
        }

        private static float Sq(float x) => x * x;


        /// <summary>Whether nothing solid stands between two points, but the players or machine named.</summary>
        private bool InSight(Vector3 from, Vector3 to, Player ignore, Player player, IDamageable target)
        {
            var along = to - from;
            var distance = along.magnitude;
            if (distance < 1e-3f)
                return true;
            var hit = Trace(from, along / distance, distance, ignore);
            return !hit.hit || (player != null && hit.player == player) || (target != null && hit.target == target);
        }

        /// <summary>A player's middle, for the bolt's jump.</summary>
        private static Vector3 Centre(Player player) => player.transform.position + Vector3.up * 1.0f;

        /// <summary>Hurts what a shot hit, a player or a machine; tells the HUD of a hit that took health. Returns the health taken.</summary>
        private float Strike(Player shooter, string weapon, ShotHit hit, float amount, Vector3 from, bool headshot)
        {
            if (hit.player != null)
            {
                var victim = hit.player.State;
                var taken = State.Hurt(victim, Damage.By(shooter.State, weapon, amount, from, headshot));
                if (taken > 0)
                    HitLanded?.Invoke(shooter, hit.player, !victim.Alive, headshot);
                return taken;
            }
            return hit.target != null ? HurtTarget(shooter, weapon, hit.target, amount, hit.point, from) : 0;
        }

        private float HurtTarget(Player shooter, string weapon, IDamageable target, float amount, Vector3 point, Vector3 from)
        {
            if (!target.Alive || !State.CanHurt(target.Team, shooter.Team))
                return 0;
            var taken = target.TakeDamage(Damage.By(shooter.State, weapon, amount, from), point);
            if (taken > 0)
                HitLanded?.Invoke(shooter, null, !target.Alive, false);
            return taken;
        }

        /// <summary>A direction at most <paramref name="degrees"/> off <paramref name="aim"/>, spread evenly over the disc.</summary>
        public static Vector3 Spread(Vector3 aim, float degrees)
        {
            if (degrees <= 0)
                return aim;
            var side = Vector3.Cross(aim, Mathf.Abs(aim.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            var off = Quaternion.AngleAxis(degrees * Mathf.Sqrt(Random.value), side) * aim;
            return Quaternion.AngleAxis(Random.value * 360, aim) * off;
        }

        // ---------------------------------------------------------------- Pods and the match

        private bool CanTakeWeapon(WeaponPickup pod, GameObject who)
        {
            var player = Player.Of(who.transform);
            return player != null && player.State != null && State.CanTake(player.State, pod.weaponId);
        }

        private void OnWeaponPickedUp(WeaponPickup pod, GameObject who)
        {
            var player = Player.Of(who.transform);
            if (player != null && player.State != null)
                State.Take(player.State, pod.weaponId);
        }

        private bool CanTakeHealth(HealthPickup pod, GameObject who)
        {
            var player = Player.Of(who.transform);
            return player != null && State.NeedsHealth(player.State);
        }

        private void OnHealthPickedUp(HealthPickup pod, GameObject who)
        {
            var player = Player.Of(who.transform);
            if (player != null)
                State.Heal(player.State, Table.healthPack);
        }

        private void OnSpawned(PlayerState state) => ArsenalOf(Match.PlayerOf(state))?.ResetLife();

        private void OnDied(Death death)
        {
            var player = Match.PlayerOf(death.Victim);
            if (player == null)
                return;
            ArsenalOf(player)?.ResetLife();
            player.Controller.slowMultiplier = 1;
        }

        private void OnRoundReset()
        {
            for (var i = projectiles.Count - 1; i >= 0; i--)
                Fx.EndGob(projectiles[i].gob);
            projectiles.Clear();
        }
    }
}
