using System.Collections.Generic;
using SdfEffects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfBattle
{
    /// <summary>
    /// The things the boss fight's story beats put on the stage (ticket 33.8), told by the beats' cues through <see cref="Boss"/>: the crown
    /// that flies off Prime's cap onto the wreck, Prime shrinking with pops to scout size and leaving his cape as a puddle, the crown
    /// that rolls to 42 and hops onto Mushroom 7's cap, the arena floor breaking into blue pixel blocks and the white flash of the sky.
    /// Data and a few eases, no animator; each runs on unscaled-free game time from <c>Update</c> and <see cref="Clear"/> puts every
    /// bit back (RESTART FIGHT after a win). The blocks are one instanced draw (<c>Resources/BossPixels</c>), the flash one quad
    /// (<c>Resources/BossFlash</c>); nothing here needs more than WebGL 2.
    /// </summary>
    public sealed class BossStaging : MonoBehaviour
    {
        /// <summary>Prime's size at each pop of the shrink, from the boss scene's scale down to the scout's.</summary>
        public static readonly float[] ShrinkScales = { PrimeBody.BossScale, 3.7f, 3.0f, 2.3f, 1.7f, 1.25f, 1f };

        /// <summary>Seconds between the shrink's pops, and a pop's own length.</summary>
        public const float PopEvery = 0.5f, PopSeconds = 0.14f;

        /// <summary>Metres the crown's rolling and hop end above the ground (the height of a scout's cap).</summary>
        public const float CapHeight = 1.5f;

        private const string PixelShader = "BossPixels", FlashShader = "BossFlash";
        private const float CellSize = 5, PixelRise = 0.4f, PixelFlight = 2.2f, PixelSpread = 1.6f;

        private struct Leg
        {
            public Vector3 from, to;
            public float seconds, arc, scaleFrom, scaleTo, spin;
            public bool roll;
        }

        private struct Cell
        {
            public Vector3 position;
            public float delay;
            public Vector3 axis;
        }

        private Boss boss;
        private GameObject crown;
        private readonly Queue<Leg> legs = new Queue<Leg>();
        private Leg leg;
        private bool legOn;
        private float legClock;
        private float spinAngle;

        private bool shrinking;
        private float shrinkClock;
        private int pops;

        private readonly List<Cell> cells = new List<Cell>();
        private bool pixels;
        private float pixelClock;
        private Mesh cube;
        private Material pixelMaterial;
        private Matrix4x4[] matrices = new Matrix4x4[500];

        private Renderer flash;
        private Material flashMaterial;
        private float flashAlpha;

        /// <summary>Whether the shrink is running.</summary>
        public bool Shrinking => shrinking;

        /// <summary>How many pops of the shrink there have been.</summary>
        public int Pops => pops;

        /// <summary>Whether the floor is breaking up.</summary>
        public bool PixelsOn => pixels;

        /// <summary>The crown's prop, while there is one (flying, lying on the wreck, rolling or on Mushroom 7's cap).</summary>
        public GameObject Crown => crown;

        public static BossStaging Create(Boss boss)
        {
            var staging = boss.gameObject.AddComponent<BossStaging>();
            staging.boss = boss;
            return staging;
        }

        // ---------------------------------------------------------------- What the beats ask for

        /// <summary>The crown flies off Prime's cap in an arc and lands at <paramref name="to"/> (transition 2 to 3).</summary>
        public void CrownFlies(Vector3 from, Vector3 to)
        {
            ClearCrown();
            if (!MakeCrown(from, PrimeBody.BossScale))
                return;
            legs.Enqueue(new Leg { from = from, to = to, seconds = 1.1f, arc = 7, scaleFrom = PrimeBody.BossScale, scaleTo = 3, spin = 540 });
        }

        /// <summary>
        /// The crown rolls over the floor from <paramref name="from"/> to 42's feet, is lifted and looked at, then hops onto the cap at
        /// <paramref name="cap"/> (Mushroom 7's); without one it stays in the air at 42's hand.
        /// </summary>
        public void CrownRolls(Vector3 from, Vector3 feet, Vector3? cap)
        {
            ClearCrown();
            if (!MakeCrown(from, 1.5f))
                return;
            var hand = feet + Vector3.up * 1.2f;
            legs.Enqueue(new Leg { from = from, to = feet + Vector3.up * 0.3f, seconds = 2f, arc = 0.15f, scaleFrom = 1.5f, scaleTo = 1.5f, spin = 900, roll = true });
            legs.Enqueue(new Leg { from = feet + Vector3.up * 0.3f, to = hand, seconds = 0.6f, arc = 0, scaleFrom = 1.5f, scaleTo = 1.5f, spin = 120 });
            if (cap.HasValue)
                legs.Enqueue(new Leg { from = hand, to = cap.Value + Vector3.up * CapHeight, seconds = 0.8f, arc = 1.4f, scaleFrom = 1.5f, scaleTo = 1, spin = 360 });
        }

        /// <summary>The crown prop is gone (phase 3 begins with the crown as the flag, a retry).</summary>
        public void ClearCrown()
        {
            legs.Clear();
            legOn = false;
            if (crown != null)
                Destroy(crown);
            crown = null;
        }

        /// <summary>Prime pops down to scout size, one pop each <see cref="PopEvery"/> seconds, and leaves his cape behind (the victory).</summary>
        public void ShrinkPrime()
        {
            if (shrinking || boss.Body == null)
                return;
            shrinking = true;
            shrinkClock = 0;
            pops = 0;
            boss.Body.SetAct(PrimeAct.Beaten);
            boss.Body.ShutEyes(true);
            boss.Body.LookAt(null);
            boss.Walker?.Stop();
        }

        /// <summary>The floor breaks into blue blocks from the edges in, the sky flashes white.</summary>
        public void PixelBreak()
        {
            if (pixels)
                return;
            if (cells.Count == 0)
                Sample();
            if (pixelMaterial == null)
            {
                var shader = Resources.Load<Shader>(PixelShader);
                if (shader != null)
                    pixelMaterial = new Material(shader) { name = "Boss Pixels", enableInstancing = true };
                else
                    Debug.LogWarning($"Boss: no shader Resources/{PixelShader}; the floor does not break into blocks");
            }
            cube ??= CubeMesh();
            pixels = true;
            pixelClock = 0;
            MakeFlash();
        }

        /// <summary>Puts the stage back as the fight leaves it: Prime full size with his cape on, no blocks, no flash, no crown prop.</summary>
        public void Clear()
        {
            ClearCrown();
            shrinking = false;
            pops = 0;
            if (boss != null && boss.Body != null)
            {
                boss.Body.transform.localScale = Vector3.one * PrimeBody.BossScale;
                boss.Body.TakeCapeBack();
                boss.Body.ShutEyes(false);
            }
            pixels = false;
            pixelClock = 0;
            flashAlpha = 0;
            if (flash != null)
                flash.enabled = false;
        }

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            var dt = Time.deltaTime;
            UpdateCrown(dt);
            UpdateShrink(dt);
            UpdatePixels(dt);
        }

        private void LateUpdate() => UpdateFlash();

        private void UpdateCrown(float dt)
        {
            if (crown == null)
                return;
            if (!legOn)
            {
                if (legs.Count == 0)
                    return;
                leg = legs.Dequeue();
                legOn = true;
                legClock = 0;
            }
            legClock += dt;
            var u = Mathf.Clamp01(legClock / Mathf.Max(0.01f, leg.seconds));
            var e = leg.roll ? u : Mathf.SmoothStep(0, 1, u);
            var position = Vector3.Lerp(leg.from, leg.to, e) + Vector3.up * (leg.arc * 4 * u * (1 - u));
            spinAngle += leg.spin * dt;
            var along = leg.to - leg.from;
            along.y = 0;
            if (leg.roll && along.sqrMagnitude > 0.01f)
            {
                // Rolls on its rim, turning about the axis across its way
                var across = Vector3.Cross(Vector3.up, along.normalized);
                crown.transform.rotation = Quaternion.AngleAxis(spinAngle, across) * Quaternion.LookRotation(along.normalized);
            }
            else
            {
                crown.transform.rotation = Quaternion.Euler(0, spinAngle, 0);
            }
            crown.transform.position = position;
            crown.transform.localScale = Vector3.one * Mathf.Lerp(leg.scaleFrom, leg.scaleTo, e);
            if (u >= 1)
            {
                legOn = false;
                // Landed or arrived: it lies upright from here
                if (legs.Count == 0)
                    crown.transform.rotation = Quaternion.Euler(0, spinAngle, 0);
            }
        }

        private void UpdateShrink(float dt)
        {
            var body = boss.Body;
            if (!shrinking || body == null)
                return;
            shrinkClock += dt;
            var last = ShrinkScales.Length - 1;
            var total = last * PopEvery + PopSeconds + 0.1f;
            // Pop i takes him from scale i-1 to scale i at i * PopEvery; in between he stands at the smaller one
            var index = Mathf.Min(last, Mathf.FloorToInt(shrinkClock / PopEvery));
            var into = shrinkClock - index * PopEvery;
            while (pops < index)
                Pop(pops + 1);
            var scale = ShrinkScales[Mathf.Min(last, index)];
            var squash = 0f;
            if (index >= 1 && into < PopSeconds)
            {
                var u = into / PopSeconds;
                scale = Mathf.Lerp(ShrinkScales[index - 1], ShrinkScales[index], Mathf.SmoothStep(0, 1, u));
                squash = 0.2f * Mathf.Sin(Mathf.PI * u);
            }
            body.transform.localScale = new Vector3(scale * (1 + squash), scale * (1 - squash), scale * (1 + squash));
            if (shrinkClock < total)
            {
                // Sways and the cap wobbles until the last pop
                var yaw = body.transform.eulerAngles.y;
                body.transform.rotation = Quaternion.Euler(Mathf.Sin(shrinkClock * 9) * 4, yaw, Mathf.Sin(shrinkClock * 7) * 6 * (1 - shrinkClock / total));
                return;
            }
            // Done: upright, small, indignant, facing 42
            shrinking = false;
            body.transform.localScale = Vector3.one * ShrinkScales[last];
            var way = boss.Match.You != null ? boss.Match.You.transform.position - body.transform.position : body.transform.forward;
            way.y = 0;
            body.transform.rotation = way.sqrMagnitude > 0.01f ? Quaternion.LookRotation(way) : Quaternion.Euler(0, body.transform.eulerAngles.y, 0);
            body.ShutEyes(false);
            body.SetAct(PrimeAct.Idle);
            body.Settle();
            if (boss.Match.You != null)
                body.LookAt(boss.Match.You.transform.position + Vector3.up * 1.4f);
        }

        // A pop: puffs of Prime blue from his cap, a cheer; the first leaves the cape behind
        private void Pop(int index)
        {
            pops = index;
            var body = boss.Body;
            if (index == 1)
                body.DropCape(body.transform.position, transform);
            var particles = boss.Particles;
            if (particles != null)
            {
                var at = body.CapTop;
                for (var i = 0; i < 18; i++)
                    particles.Emit(FxParticles.Kind.Puff, at, Random.onUnitSphere * Random.Range(2f, 6f), Random.Range(0.5f, 0.9f), Random.Range(0.3f, 0.7f) * Mathf.Max(0.5f, ShrinkScales[index] / 2),
                        new Color(0.35f, 0.6f, 1f), new Color(0.12f, 0.3f, 0.9f), gravity: -0.3f, drag: 3);
            }
            boss.Arena.Cheers(0.7f);
        }

        // The blocks: one batch of instanced cubes, each rising off the floor in its turn, tumbling and shrinking away
        private void UpdatePixels(float dt)
        {
            if (!pixels || pixelMaterial == null || cube == null)
                return;
            pixelClock += dt;
            var n = 0;
            var batch = new RenderParams(pixelMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            foreach (var cell in cells)
            {
                var t = pixelClock - cell.delay;
                if (t < 0 || t > PixelRise + PixelFlight)
                    continue;
                var flight = Mathf.Max(0, t - PixelRise);
                var u = flight / PixelFlight;
                var size = CellSize * 0.92f * (1 - u * u);
                var lift = 0.05f + flight * flight * 5f;
                var rotation = Quaternion.AngleAxis(u * 300, cell.axis);
                matrices[n++] = Matrix4x4.TRS(cell.position + Vector3.up * (lift + size * 0.1f), rotation, new Vector3(size, size * 0.25f + u * size * 0.5f, size));
                if (n == matrices.Length)
                {
                    Graphics.RenderMeshInstanced(batch, cube, 0, matrices, n);
                    n = 0;
                }
            }
            if (n > 0)
                Graphics.RenderMeshInstanced(batch, cube, 0, matrices, n);
            // The white flash: a spike as the break begins, then gone
            flashAlpha = pixelClock < 0.15f ? pixelClock / 0.15f * 0.9f : Mathf.Max(0, 0.9f * (1 - (pixelClock - 0.15f) / 1.1f));
        }

        // The floor to break: a grid over the arena, the cells where a ray from above lands on ground (not on a wall or the seating)
        private void Sample()
        {
            var arena = boss.Arena;
            var red = boss.Match.Arena.Side(SdfPickups.BattleSideId.West).spawn.position;
            var blue = boss.Match.Arena.Side(SdfPickups.BattleSideId.East).spawn.position;
            var centre = (red + blue) * 0.5f;
            var floor = boss.Spots.crash.position.y;
            var halfX = Mathf.Abs(blue.x - red.x) * 0.5f + 20;
            const float halfZ = 52;
            for (var x = -halfX; x <= halfX; x += CellSize)
                for (var z = -halfZ; z <= halfZ; z += CellSize)
                {
                    var from = new Vector3(centre.x + x, floor + 30, centre.z + z);
                    if (!Physics.Raycast(from, Vector3.down, out var hit, 60, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;
                    if (hit.normal.y < 0.7f || Mathf.Abs(hit.point.y - floor) > 3 || hit.collider.GetComponentInParent<Player>() != null)
                        continue;
                    // The edges go first
                    var edge = Mathf.Max(Mathf.Abs(x) / halfX, Mathf.Abs(z) / halfZ);
                    cells.Add(new Cell
                    {
                        position = hit.point,
                        delay = (1 - Mathf.Clamp01(edge)) * PixelSpread + Random.value * 0.2f,
                        axis = new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f).normalized,
                    });
                }
        }

        private static Mesh CubeMesh()
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);
            return mesh;
        }

        // ---------------------------------------------------------------- The flash, the crown prop

        private void MakeFlash()
        {
            if (flash == null)
            {
                var shader = Resources.Load<Shader>(FlashShader);
                if (shader == null)
                {
                    Debug.LogWarning($"Boss: no shader Resources/{FlashShader}; the sky does not flash");
                    return;
                }
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Sky Flash";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                // As wide as the arena, so culling never takes it away; its shader covers the whole view whatever the transform
                quad.transform.localScale = Vector3.one * 600;
                flash = quad.GetComponent<Renderer>();
                flashMaterial = new Material(shader) { name = "Boss Flash" };
                flash.sharedMaterial = flashMaterial;
                flash.shadowCastingMode = ShadowCastingMode.Off;
                flash.receiveShadows = false;
            }
            flash.enabled = true;
        }

        private void UpdateFlash()
        {
            if (flash == null)
                return;
            flash.enabled = flashAlpha > 0.001f;
            if (!flash.enabled)
                return;
            var you = boss.Match.You;
            var camera = you != null ? you.GetComponentInChildren<Camera>() : Camera.main;
            if (camera != null)
                flash.transform.position = camera.transform.position;
            flashMaterial.color = new Color(1, 1, 1, flashAlpha);
        }

        private bool MakeCrown(Vector3 at, float scale)
        {
            var prefab = boss.Spots.crownPrefab;
            if (prefab == null)
                return false;
            crown = Instantiate(prefab, at, Quaternion.identity, transform);
            crown.name = "Crown (story)";
            crown.transform.localScale = Vector3.one * scale;
            foreach (var collider in crown.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            spinAngle = 0;
            return true;
        }

        private void OnDestroy()
        {
            if (pixelMaterial != null)
                Destroy(pixelMaterial);
            if (flashMaterial != null)
                Destroy(flashMaterial);
        }
    }
}
