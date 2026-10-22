using System;
using System.Collections.Generic;
using SdfLevel;
using SdfPickups;
using SdfPrime;
using Unity.AI.Navigation;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The Champion's Arena as the boss fight changes it (ticket 33.6), on the scene's Boss Spots object (made by <see cref="Boss.Create"/>
    /// when the scene has one; nothing is placed by hand):
    /// <list type="bullet">
    /// <item><b>Phase layouts</b>: the placed models tagged with phases sit under <see cref="PhaseGroup"/> objects (their colliders with them);
    /// <see cref="ShowPhase"/> shows the groups of a phase and hides the rest, and switches the navmesh surfaces baked for that layout
    /// (Navigation Scout / Tank / Boss Phase 1 to 3, one set on at a time). The scene is built in phase 1.</item>
    /// <item><b>The crowd</b>: the shader global <c>_CrowdCheer</c> (0 to 1: a low murmur, high on a capture, a stun, a phase break, the
    /// victory), which the instanced renderer's crowd group sways and hops with: one uniform, nothing per instance on the CPU.</item>
    /// <item><b>The floodlights</b>: the four giant mushrooms' gold, turning to a hotter orange-red in phase 3 (drawn as halos by
    /// <see cref="BossTelegraphs"/> from <see cref="FloodlightPositions"/> and <see cref="FloodlightColour"/>).</item>
    /// <item><b>Pods</b>: the dais' health pod from phase 2, and the two health caps the crowd throws on each phase start, which last 30 s.</item>
    /// <item><b>The plinth</b>: the champion's golden minigun (<c>minigun-gold</c>) that appears on it when the crown is captured;
    /// only 42 can take it.</item>
    /// <item><b>The crown</b>: the flag of phase 3 sits on the wreck's top; after its capture it sits on the red stand in gold.</item>
    /// </list>
    /// </summary>
    public sealed class BossArena : MonoBehaviour
    {
        [Tooltip("The crowd's murmur when nothing happens")]
        public float murmur = 0.12f;
        [Tooltip("Seconds a cheer takes to die down")]
        public float cheerFade = 3;
        [Tooltip("Metres up the wreck's top is, where the crown lies (the wreck model's level top)")]
        public float wreckTop = 2;
        [Tooltip("Seconds a health cap the crowd threw lasts")]
        public float capSeconds = 30;
        [Tooltip("How far the floodlights' gold has turned to orange-red, 0 to 1 (phase 3 sets it)")]
        [Range(0, 1)] public float hot;

        private static readonly int CheerId = Shader.PropertyToID("_CrowdCheer");
        private static readonly Color Gold = new Color(1f, 0.78f, 0.25f);
        private static readonly Color Orange = new Color(1f, 0.32f, 0.1f);

        private BossSpots spots;
        private Transform levelRoot;
        private PhaseGroup[] groups = Array.Empty<PhaseGroup>();
        private readonly List<NavMeshSurface> surfaces = new List<NavMeshSurface>();
        private readonly List<int> surfacePhases = new List<int>();
        private readonly List<Vector3> floodlights = new List<Vector3>();
        private GameObject centrePillar;
        private float cheer;
        private float cheerTarget;
        private HealthPickup daisPod;
        private readonly HealthPickup[] caps = new HealthPickup[2];
        private float capsLeft;
        private GameObject plinthGun;
        private GameObject trophy;
        private Vector3 blueStandHome;
        private Quaternion blueStandRotation;
        private bool blueStandKnown;

        public static BossArena Current { get; private set; }

        public BossSpots Spots => spots;

        /// <summary>The layout shown, 1 to 3.</summary>
        public int Phase { get; private set; } = 1;

        /// <summary>The crowd's cheer now, 0 to 1.</summary>
        public float Cheer => cheer;

        /// <summary>The four floodlights' tops in the world, where their halos are drawn.</summary>
        public IReadOnlyList<Vector3> FloodlightPositions => floodlights;

        /// <summary>The floodlights' colour: gold, turning orange-red with <see cref="hot"/>.</summary>
        public Color FloodlightColour => Color.Lerp(Gold, Orange, hot);

        /// <summary>The champion's minigun was taken by 42 (the plinth is bare).</summary>
        public event Action<Player> ChampionGunTaken;

        /// <summary>Whether the scene is a boss arena: it has Boss Spots.</summary>
        public static bool Present => FindFirstObjectByType<BossSpots>() != null;

        /// <summary>
        /// The match of the boss arena played on its own (not from the campaign, which brings its own setup): 42 and three red bots at
        /// 0.6, five blue scouts at 0.7, Normal difficulty, the boss decides the match.
        /// </summary>
        public static MatchSetup DefaultSetup() => new MatchSetup
        {
            redSize = 4,
            blueSize = 5,
            redSkill = 0.6f,
            blueSkill = 0.7f,
            capturesToWin = 3,
            timeLimitSeconds = 0,
            singleRound = true,
            objective = MatchObjective.Boss,
            sporesOn = true,
            blueRespawnSeconds = 8,
            bossDifficulty = (int)BossDifficulty.Normal,
        };

        /// <summary>Puts the arena's behaviour on the scene's Boss Spots; null when the scene has none.</summary>
        public static BossArena Attach()
        {
            var spots = FindFirstObjectByType<BossSpots>();
            if (spots == null)
                return null;
            var arena = spots.GetComponent<BossArena>();
            if (arena == null)
                arena = spots.gameObject.AddComponent<BossArena>();
            arena.Bind(spots);
            return arena;
        }

        private void Bind(BossSpots bossSpots)
        {
            spots = bossSpots;
            Current = this;
            groups = FindObjectsByType<PhaseGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            levelRoot = groups.Length > 0 ? groups[0].transform.parent : null;
            foreach (var surface in FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var phase = PhaseOfSurface(surface.name);
                if (phase <= 0)
                    continue;
                surfaces.Add(surface);
                surfacePhases.Add(phase);
            }
            foreach (var transform in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (transform.name.StartsWith("floodlight-mushroom", StringComparison.Ordinal) && !(transform.parent != null && transform.parent.name.StartsWith("floodlight-mushroom", StringComparison.Ordinal)))
                    floodlights.Add(transform.position + Vector3.up * 13.4f);
            }
            // The centre pillar: the phase 1-2 model standing where the copter crashes
            if (spots.crash != null)
            {
                var nearest = 3f;
                foreach (var group in groups)
                {
                    if (group.first != 1 || group.last != 2)
                        continue;
                    foreach (Transform child in group.transform)
                    {
                        var flat = child.position - spots.crash.position;
                        flat.y = 0;
                        if (flat.magnitude < nearest)
                            (nearest, centrePillar) = (flat.magnitude, child.gameObject);
                    }
                }
            }
            blueStandKnown = false;
            ShowPhase(1);
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
            Shader.SetGlobalFloat(CheerId, 0);
        }

        /// <summary>The phase a navmesh surface was baked for, from its name (Navigation Scout Phase 2 is 2); 0 for a name that is not one.</summary>
        public static int PhaseOfSurface(string surfaceName)
        {
            const string marker = " Phase ";
            var at = surfaceName != null ? surfaceName.LastIndexOf(marker, StringComparison.Ordinal) : -1;
            return at >= 0 && surfaceName.StartsWith("Navigation ", StringComparison.Ordinal) && int.TryParse(surfaceName.Substring(at + marker.Length), out var phase) ? phase : 0;
        }

        // ---------------------------------------------------------------- Layouts

        /// <summary>Shows the layout of phase 1 to 3: its models and colliders, and the navmeshes baked for it.</summary>
        public void ShowPhase(int phase)
        {
            Phase = Mathf.Clamp(phase, 1, 3);
            foreach (var group in groups)
                if (group != null)
                    group.Show(Phase);
            // A group switched on again has its crushed centre pillar back
            if (centrePillar != null)
                centrePillar.SetActive(true);
            for (var i = 0; i < surfaces.Count; i++)
                if (surfaces[i] != null)
                    surfaces[i].gameObject.SetActive(surfacePhases[i] == Phase);
            Physics.SyncTransforms();
            hot = Phase == 3 ? 1 : 0;
        }

        /// <summary>The crash crushes the centre pillar (phase 2 to 3).</summary>
        public void HideCentrePillar()
        {
            if (centrePillar != null)
                centrePillar.SetActive(false);
        }

        // ---------------------------------------------------------------- The crowd

        /// <summary>The crowd roars: its cheer goes to <paramref name="level"/> (0 to 1) and dies back down to the murmur.</summary>
        public void Cheers(float level)
        {
            cheerTarget = Mathf.Max(cheerTarget, Mathf.Clamp01(level));
            cheer = Mathf.Max(cheer, cheerTarget);
        }

        private void Update()
        {
            cheerTarget = Mathf.MoveTowards(cheerTarget, murmur, Time.deltaTime / Mathf.Max(0.1f, cheerFade));
            cheer = Mathf.MoveTowards(cheer, cheerTarget, Time.deltaTime * 2);
            Shader.SetGlobalFloat(CheerId, cheer);
            if (capsLeft > 0)
            {
                capsLeft -= Time.deltaTime;
                if (capsLeft <= 0)
                    foreach (var cap in caps)
                        if (cap != null)
                            cap.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- Pods

        /// <summary>The dais' health pod, there in phases 2 and 3 (made on first use).</summary>
        public void ShowDaisPod(bool shown)
        {
            if (daisPod == null && shown && spots.healthPodPrefab != null && spots.prime != null)
                daisPod = MakePod(spots.prime.position);
            if (daisPod != null)
            {
                daisPod.gameObject.SetActive(shown);
                if (shown)
                    daisPod.Restock();
            }
        }

        /// <summary>The crowd throws two health caps onto the floor at the foot of the stands' middle, gone after 30 s.</summary>
        public void ThrowHealthCaps()
        {
            if (spots.healthPodPrefab == null)
                return;
            for (var i = 0; i < caps.Length; i++)
            {
                if (caps[i] == null)
                    caps[i] = MakePod(Vector3.zero);
                if (caps[i] == null)
                    continue;
                // File (-10, +-20), x mirrored into Unity's coordinates: before the red side's half, in the north and south lanes
                var spot = new Vector3(10, 0, i == 0 ? 20 : -20);
                if (GroundAt(spot + Vector3.up * 30, out var ground))
                    spot = ground;
                caps[i].transform.position = spot;
                caps[i].gameObject.SetActive(true);
                caps[i].Restock();
            }
            capsLeft = capSeconds;
        }

        private HealthPickup MakePod(Vector3 at)
        {
            var instance = Instantiate(spots.healthPodPrefab, at, Quaternion.identity, transform);
            instance.name = "Health Cap";
            var pod = instance.GetComponentInChildren<HealthPickup>(true);
            if (pod == null)
            {
                Destroy(instance);
                return null;
            }
            PodBeacon.Add(pod);
            Bots.Current?.AddHealthPod(pod);
            return pod;
        }

        // ---------------------------------------------------------------- The plinth's minigun

        /// <summary>The champion's golden minigun appears on the plinth (the crown was captured), for 42 to take.</summary>
        public void ShowChampionGun()
        {
            if (plinthGun != null)
            {
                plinthGun.SetActive(true);
                return;
            }
            if (spots.plinth == null)
                return;
            plinthGun = new GameObject("Champion's Minigun");
            plinthGun.transform.SetParent(spots.plinth, false);
            plinthGun.transform.localPosition = Vector3.zero;
            // The hover puts the gun's middle over the plinth's top (1.2 m); the model's origin is its axis 13 cm up, its length 1 m and more
            var hover = new GameObject("Hover").transform;
            hover.SetParent(plinthGun.transform, false);
            var spin = hover.gameObject.AddComponent<SpinHover>();
            spin.baseHeight = 1.2f;
            spin.hoverHeight = 0.55f;
            spin.revolutionsPerMinute = 10;
            if (spots.goldMinigunPrefab != null)
            {
                var model = Instantiate(spots.goldMinigunPrefab, hover, false);
                model.name = "minigun-gold";
                model.transform.localPosition = new Vector3(0, -0.13f, -0.4f);
                model.transform.localScale = Vector3.one * 1.1f;
                foreach (var collider in model.GetComponentsInChildren<Collider>())
                    collider.enabled = false;
            }
            var trigger = plinthGun.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0, 1.4f, 0);
            trigger.radius = 1.6f;
            plinthGun.AddComponent<ChampionGunPickup>().Taken += player =>
            {
                plinthGun.SetActive(false);
                ChampionGunTaken?.Invoke(player);
            };
            var column = FlagGlowColumn.Create(plinthGun.transform, Gold, 6, 0.4f, 0.9f);
            if (column != null)
                column.transform.localPosition = Vector3.up * 1.2f;
        }

        public void HideChampionGun()
        {
            if (plinthGun != null)
                plinthGun.SetActive(false);
        }

        // ---------------------------------------------------------------- The crown

        /// <summary>
        /// Moves the blue flag's stand to the wreck's top (phase 3: the crown is the flag) and back to under the balcony otherwise; the
        /// flag goes back on it. <paramref name="stand"/> is the blue flag stand.
        /// </summary>
        public void PutBlueStand(FlagStand stand, bool onWreck)
        {
            if (stand == null)
                return;
            if (!blueStandKnown)
            {
                blueStandHome = stand.transform.position;
                blueStandRotation = stand.transform.rotation;
                blueStandKnown = true;
            }
            if (onWreck && spots.crash != null)
                stand.transform.SetPositionAndRotation(spots.crash.position + Vector3.up * wreckTop, blueStandRotation);
            else
                stand.transform.SetPositionAndRotation(blueStandHome, blueStandRotation);
            Physics.SyncTransforms();
        }

        /// <summary>The crown on the red stand in gold, once 42's team has captured it (a copy of the story's crown with a gold glow column); false without a crown prefab.</summary>
        public bool ShowTrophy(Transform redStand)
        {
            if (trophy != null)
            {
                trophy.SetActive(redStand != null);
                return true;
            }
            if (redStand == null || spots.crownPrefab == null)
                return false;
            trophy = Instantiate(spots.crownPrefab, redStand, false);
            trophy.name = "Crown Trophy";
            trophy.transform.localPosition = new Vector3(0, 1.9f, 0);
            trophy.transform.localScale = Vector3.one * 2.4f;
            foreach (var collider in trophy.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            var column = FlagGlowColumn.Create(trophy.transform, Gold, 40, 0.9f);
            column?.SetColour(Gold);
            return true;
        }

        public void HideTrophy()
        {
            if (trophy != null)
                trophy.SetActive(false);
        }

        // ---------------------------------------------------------------- The ground

        private static readonly RaycastHit[] Hits = new RaycastHit[24];
        private static readonly Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);

        /// <summary>
        /// The floor under a point: the first solid surface down from it that is not a player, a machine, the Prime-copter or a part of
        /// Prime himself (the markers are drawn on the floor under him, the dive's circle under the copter).
        /// </summary>
        public static bool GroundAt(Vector3 from, out Vector3 point)
        {
            var count = Physics.RaycastNonAlloc(from, Vector3.down, Hits, 120, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, count, Comparer<RaycastHit>.Create(ByDistance));
            for (var i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (Player.Of(collider) != null || Machines.Of(collider) != null || PrimeCopter.Owns(collider) || collider.GetComponentInParent<PrimeRig>() != null ||
                    collider.GetComponentInParent<PrimeHit>() != null)
                    continue;
                point = Hits[i].point;
                return true;
            }
            point = from;
            return false;
        }
    }

    /// <summary>The trigger of the champion's minigun on its plinth (ticket 33.6): only 42 can take it.</summary>
    public sealed class ChampionGunPickup : MonoBehaviour
    {
        public event Action<Player> Taken;

        private void OnTriggerEnter(Collider other) => Try(other);

        private void OnTriggerStay(Collider other) => Try(other);

        private void Try(Collider other)
        {
            var player = Player.Of(other);
            if (player != null && player.IsYou && player.State != null && player.State.Alive)
                Taken?.Invoke(player);
        }
    }
}
