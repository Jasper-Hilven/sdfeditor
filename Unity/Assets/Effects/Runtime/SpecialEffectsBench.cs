using UnityEngine;

namespace SdfEffects
{
    /// <summary>
    /// The test bench of the special-effects scene (Assets/Scenes/SpecialEffects.unity): fires each effect from a key or a button and,
    /// with the loop on, fires them all in turn on their own intervals so they can be watched and recorded.
    /// <list type="bullet">
    /// <item>1 lightning, 2 pistol, 3 minigun, 4 sniper, 5 goop, 6 explode (in <see cref="effects"/> order), Space all at once.</item>
    /// <item>L turns the loop on or off, R puts everything back, P pauses, and the buttons top left do the same.</item>
    /// </list>
    /// Below the guns, a row of the twelve monster heads (ticket 46, <see cref="monsters"/>): M bursts all twelve, ',' and '.' step back and
    /// forward through them and burst one, and with the loop on they take turns; each head's explosion plays at <see cref="goreLevel"/>.
    /// The bench drives every effect from its <see cref="Step"/> on one clock, so the editor's snapshots step it frame by frame
    /// without Play mode, and a fixed <see cref="seed"/> makes each run play the same way. Reads the classic input manager, like the
    /// level's player.
    /// </summary>
    public sealed class SpecialEffectsBench : MonoBehaviour
    {
        public FxBuffer fx;
        public FxParticles particles;
        public FxEffect[] effects;
        [Tooltip("The twelve monster heads, in the heads' calendar order (ticket 46)")]
        public ExplodingMonster[] monsters = new ExplodingMonster[0];
        [Tooltip("The debris pool the monsters' explosions use")]
        public MonsterDebris debris;
        [Tooltip("The lightning Frankenstein's explosion strikes with")]
        public LightningGun[] bolts = new LightningGun[0];
        [Tooltip("Where V puts the camera: in front of the row of monsters")]
        public Transform monsterView;
        [Tooltip("The gore level the monsters burst at: 1 splatty, 2 extra splatty, 3 ultra gore")]
        public int goreLevel = 2;
        [Tooltip("Seconds between one monster and the next when the loop is on")]
        public float monsterInterval = 3.2f;
        public bool loop = true;
        public uint seed = 1234;

        private FxRandom random;
        private BenchWorld world;
        private Vector3 benchPosition;
        private Quaternion benchRotation;
        private bool viewing;
        private int nextMonster;
        private int lastMonsterTurn = -1;
        private float clock;
        private bool paused;
        private readonly float[] lastLoop = new float[16];

        /// <summary>Seconds the bench has run since it was last put back.</summary>
        public float Clock => clock;

        private void Awake() => Restart();

        /// <summary>Everything back as built, the clock and the random source from the start.</summary>
        public void Restart()
        {
            random ??= new FxRandom(seed);
            random.Reset(seed);
            clock = 0;
            for (var i = 0; i < lastLoop.Length; i++)
                lastLoop[i] = float.NegativeInfinity;
            particles.Clear();
            foreach (var effect in effects)
            {
                effect.Initialize(fx, particles, random);
                effect.ResetEffect();
            }
            lastMonsterTurn = -1;
            nextMonster = 0;
            UnityEngine.Random.InitState((int)seed);
            if (debris != null)
            {
                debris.autoRun = false;
                debris.Clear();
            }
            world ??= new BenchWorld(this);
            foreach (var bolt in bolts)
            {
                bolt.Initialize(fx, particles, random);
                bolt.ResetEffect();
            }
            foreach (var monster in monsters)
            {
                monster.Initialize(fx, particles, random);
                monster.ResetEffect();
                monster.world = world;
                monster.goreLevel = goreLevel;
            }
            fx.Begin();
            fx.End();
        }

        /// <summary>Bursts one monster by its place in the row (a head's calendar position, 0 to 11).</summary>
        public void FireMonster(int index)
        {
            if (index >= 0 && index < monsters.Length)
            {
                monsters[index].goreLevel = goreLevel;
                monsters[index].Fire();
            }
        }

        /// <summary>The camera between its place behind the guns and in front of the row of monsters.</summary>
        public void ToggleView()
        {
            var camera = Camera.main;
            if (camera == null || monsterView == null)
                return;
            if (!viewing)
            {
                benchPosition = camera.transform.position;
                benchRotation = camera.transform.rotation;
                camera.transform.SetPositionAndRotation(monsterView.position, monsterView.rotation);
            }
            else
            {
                camera.transform.SetPositionAndRotation(benchPosition, benchRotation);
            }
            viewing = !viewing;
        }

        private void Update()
        {
            for (var i = 0; i < effects.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    effects[i].Fire();
            if (Input.GetKeyDown(KeyCode.Space))
                foreach (var effect in effects)
                    effect.Fire();
            if (Input.GetKeyDown(KeyCode.M))
                for (var i = 0; i < monsters.Length; i++)
                    FireMonster(i);
            if (Input.GetKeyDown(KeyCode.Period) && monsters.Length > 0)
                FireMonster(nextMonster++ % monsters.Length);
            if (Input.GetKeyDown(KeyCode.Comma) && monsters.Length > 0)
                FireMonster(((nextMonster -= 2) % monsters.Length + monsters.Length) % monsters.Length);
            if (Input.GetKeyDown(KeyCode.V))
                ToggleView();
            if (Input.GetKeyDown(KeyCode.L))
                loop = !loop;
            if (Input.GetKeyDown(KeyCode.R))
                Restart();
            if (Input.GetKeyDown(KeyCode.P))
                paused = !paused;
            if (!paused)
                Step(Mathf.Min(Time.deltaTime, 0.05f));
        }

        /// <summary>Moves the bench on by <paramref name="dt"/>: the loop's shots, every effect, the particles, and the draw.</summary>
        public void Step(float dt)
        {
            clock += dt;
            if (loop)
            {
                for (var i = 0; i < effects.Length && i < lastLoop.Length; i++)
                {
                    var effect = effects[i];
                    var due = Mathf.Floor((clock - effect.loopOffset) / effect.loopInterval);
                    if (clock >= effect.loopOffset && due > lastLoop[i])
                    {
                        lastLoop[i] = due;
                        effect.Fire();
                    }
                }
            }
            if (loop && monsters.Length > 0 && clock >= 1)
            {
                var turn = Mathf.FloorToInt((clock - 1) / monsterInterval);
                if (turn > lastMonsterTurn)
                {
                    lastMonsterTurn = turn;
                    FireMonster(turn % monsters.Length);
                }
            }
            fx.Begin();
            foreach (var effect in effects)
                effect.Tick(dt);
            foreach (var monster in monsters)
                monster.Tick(dt);
            foreach (var bolt in bolts)
                bolt.Tick(dt);
            particles.Tick(dt, fx);
            fx.End();
            if (debris != null)
            {
                debris.Step(dt);
                debris.Draw();
            }
        }

        private void OnGUI()
        {
            const float width = 96, height = 26;
            GUILayout.BeginArea(new Rect(10, 10, width * 9, height * 4 + 20));
            GUILayout.BeginHorizontal();
            for (var i = 0; i < effects.Length; i++)
                if (GUILayout.Button($"{i + 1} {effects[i].Label}", GUILayout.Width(width * 0.75f), GUILayout.Height(height)))
                    effects[i].Fire();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(loop ? "L Loop: on" : "L Loop: off", GUILayout.Width(width), GUILayout.Height(height)))
                loop = !loop;
            if (GUILayout.Button("R Reset", GUILayout.Width(width), GUILayout.Height(height)))
                Restart();
            if (GUILayout.Button(paused ? "P Resume" : "P Pause", GUILayout.Width(width), GUILayout.Height(height)))
                paused = !paused;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("V View", GUILayout.Width(width * 0.6f), GUILayout.Height(height)))
                ToggleView();
            if (monsters.Length > 0 && GUILayout.Button("M All 12", GUILayout.Width(width * 0.75f), GUILayout.Height(height)))
                for (var i = 0; i < monsters.Length; i++)
                    FireMonster(i);
            for (var i = 0; i < monsters.Length; i++)
                if (GUILayout.Button(monsters[i].headId.Substring(0, Mathf.Min(5, monsters[i].headId.Length)), GUILayout.Width(width * 0.5f), GUILayout.Height(height)))
                    FireMonster(i);
            GUILayout.EndHorizontal();
            GUILayout.Label($"{fx.LastQuads} quads, {particles.Count} particles{(debris != null ? $", {debris.Live} debris in {debris.Draws} draws" : "")}");
            GUILayout.EndArea();
        }

        /// <summary>The bench's side of a monster explosion: the particles and debris, its lightning, and the ground under the row.</summary>
        private sealed class BenchWorld : IMonsterWorld
        {
            private readonly SpecialEffectsBench bench;

            public BenchWorld(SpecialEffectsBench bench) => this.bench = bench;

            public FxParticles Particles => bench.particles;

            public MonsterDebris Debris => bench.debris;

            // The bench has no goop pool for the head burst's gobs and splats (the match's gore throws those): a gob is a fat drop
            public bool Gob(Vector3 position, Vector3 velocity, float radius, Color colour)
            {
                bench.particles.Emit(FxParticles.Kind.Drop, position, velocity, 0.9f, radius * 0.6f, colour, new Color(colour.r * 0.4f, colour.g * 0.4f, colour.b * 0.4f, 1),
                    gravity: 9.8f);
                return true;
            }

            // ... and nothing stays on the ground
            public void Splat(Vector3 at, Vector3 normal, float radius, Color colour)
            {
            }

            public bool Ground(Vector3 from, out Vector3 point, out Vector3 normal)
            {
                if (Physics.Raycast(from, Vector3.down, out var hit, 6))
                {
                    point = hit.point;
                    normal = hit.normal;
                    return true;
                }
                point = default;
                normal = Vector3.up;
                return false;
            }

            public void Bolt(int index, Vector3 from, Vector3 to)
            {
                if (index >= bench.bolts.Length)
                    return;
                var gun = bench.bolts[index];
                gun.muzzle.position = from;
                gun.Strike(to, (from - to).normalized);
            }
        }
    }
}
