using UnityEngine;
using UnityEngine.Rendering;

namespace SdfBattle
{
    /// <summary>
    /// Prime's spore shield (ticket 33.6, phase 1): a dome of <see cref="BossSpots.shieldRadius"/> metres over his dais, a sphere with a
    /// collider (so every shot and lob stops on it: <see cref="Combat.Trace"/> finds <see cref="PrimeHit"/> of kind Shield behind it, which
    /// hurts nothing and shows the spark) and a toon look (<c>Resources/BossShield.shader</c>: a hard rim where it is seen edge-on, hexagonal
    /// spore cells, a ripple at each blocked hit, dithered, not blended). Up while the state's shield is; it shatters into sixty shards on a
    /// capture and regrows in a second as a rising dome. The sphere is Unity's own primitive, so the collider is a SphereCollider.
    /// </summary>
    public sealed class BossShield : MonoBehaviour
    {
        public const string ShaderName = "BossShield";

        [Tooltip("Seconds the dome takes to grow back")]
        public float growSeconds = 1;
        [Tooltip("Shards a shattering shield throws")]
        public int shards = 60;

        private static readonly int GrowId = Shader.PropertyToID("_Grow");
        private static readonly int[] RippleIds =
        {
            Shader.PropertyToID("_Ripple0"), Shader.PropertyToID("_Ripple1"), Shader.PropertyToID("_Ripple2"), Shader.PropertyToID("_Ripple3"),
        };

        private Boss boss;
        private Material material;
        private Renderer dome;
        private Collider hitSphere;
        private float radius;
        private float grow;
        private bool up;
        private readonly Vector3[] ripplePoints = new Vector3[4];
        private readonly float[] rippleAges = { -1, -1, -1, -1 };
        private int nextRipple;

        /// <summary>Whether the dome is standing (growing counts: shots stop on it).</summary>
        public bool Up => up;

        /// <summary>Makes the dome over the boss's spot; null without the shader.</summary>
        public static BossShield Create(Boss owner, Vector3 centre, float radius)
        {
            var shader = Resources.Load<Shader>(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Boss: no shader Resources/{ShaderName}; the spore shield is not drawn");
            }
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Spore Shield";
            sphere.transform.SetParent(owner.transform, false);
            sphere.transform.position = centre;
            sphere.transform.localScale = Vector3.one * radius * 2;
            var shield = sphere.AddComponent<BossShield>();
            shield.boss = owner;
            shield.radius = radius;
            shield.hitSphere = sphere.GetComponent<Collider>();
            shield.dome = sphere.GetComponent<Renderer>();
            if (shader != null)
            {
                shield.material = new Material(shader) { name = "Spore Shield" };
                shield.dome.sharedMaterial = shield.material;
            }
            shield.dome.shadowCastingMode = ShadowCastingMode.Off;
            shield.dome.receiveShadows = false;
            shield.dome.lightProbeUsage = LightProbeUsage.Off;
            shield.dome.reflectionProbeUsage = ReflectionProbeUsage.Off;
            sphere.AddComponent<PrimeHit>().Bind(owner, PrimeHit.Part.Shield);
            shield.up = true;
            shield.SetUp(false, false);
            return shield;
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }

        /// <summary>The dome goes up (growing from its foot) or comes down; coming down shatters it when <paramref name="shatter"/>.</summary>
        public void SetUp(bool value, bool shatter)
        {
            if (value == up)
                return;
            up = value;
            if (up)
            {
                grow = 0;
            }
            else if (shatter)
            {
                Shatter();
            }
            dome.enabled = up;
            hitSphere.enabled = up;
        }

        /// <summary>A shot stopped on the dome at <paramref name="point"/>: a ripple spreads from it.</summary>
        public void Hit(Vector3 point)
        {
            if (!up)
                return;
            ripplePoints[nextRipple] = point;
            rippleAges[nextRipple] = 0;
            nextRipple = (nextRipple + 1) % rippleAges.Length;
            var particles = boss.Particles;
            if (particles != null)
            {
                var normal = (point - transform.position).normalized;
                for (var i = 0; i < 3; i++)
                    particles.Emit(SdfEffects.FxParticles.Kind.Spark, point, (normal + Random.insideUnitSphere * 0.6f).normalized * Random.Range(2f, 5f), 0.3f, 0.05f,
                        new Color(0.6f, 0.9f, 1f), Color.white, gravity: 6);
            }
        }

        private void Shatter()
        {
            var particles = boss.Particles;
            if (particles == null)
                return;
            // Shards from the dome's surface, flying outwards
            var centre = transform.position;
            for (var i = 0; i < shards; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y);
                particles.Emit(SdfEffects.FxParticles.Kind.Spark, centre + direction * radius, direction * Random.Range(4f, 10f), Random.Range(0.6f, 1.1f),
                    Random.Range(0.08f, 0.16f), new Color(0.55f, 0.88f, 1f), Color.white, gravity: 9, drag: 0.5f);
            }
        }

        private void Update()
        {
            if (!up || material == null)
                return;
            var dt = Time.deltaTime;
            grow = Mathf.MoveTowards(grow, 1, dt / Mathf.Max(0.1f, growSeconds));
            material.SetFloat(GrowId, Mathf.SmoothStep(0, 1, grow));
            for (var i = 0; i < rippleAges.Length; i++)
            {
                if (rippleAges[i] >= 0)
                {
                    rippleAges[i] += dt;
                    if (rippleAges[i] > 0.9f)
                        rippleAges[i] = -1;
                }
                material.SetVector(RippleIds[i], new Vector4(ripplePoints[i].x, ripplePoints[i].y, ripplePoints[i].z, rippleAges[i]));
            }
        }
    }
}
