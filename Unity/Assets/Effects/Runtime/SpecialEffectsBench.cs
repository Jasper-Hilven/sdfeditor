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
    /// The bench drives every effect from its <see cref="Step"/> on one clock, so the editor's snapshots step it frame by frame
    /// without Play mode, and a fixed <see cref="seed"/> makes each run play the same way. Reads the classic input manager, like the
    /// level's player.
    /// </summary>
    public sealed class SpecialEffectsBench : MonoBehaviour
    {
        public FxBuffer fx;
        public FxParticles particles;
        public FxEffect[] effects;
        public bool loop = true;
        public uint seed = 1234;

        private FxRandom random;
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
            fx.Begin();
            fx.End();
        }

        private void Update()
        {
            for (var i = 0; i < effects.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    effects[i].Fire();
            if (Input.GetKeyDown(KeyCode.Space))
                foreach (var effect in effects)
                    effect.Fire();
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
            fx.Begin();
            foreach (var effect in effects)
                effect.Tick(dt);
            particles.Tick(dt, fx);
            fx.End();
        }

        private void OnGUI()
        {
            const float width = 96, height = 26;
            GUILayout.BeginArea(new Rect(10, 10, width * 6, height * 3 + 20));
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
            GUILayout.Label($"{fx.LastQuads} quads, {particles.Count} particles");
            GUILayout.EndArea();
        }
    }
}
