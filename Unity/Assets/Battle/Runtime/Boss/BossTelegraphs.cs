using SdfEffects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfBattle
{
    /// <summary>
    /// What the boss fight draws that is not a model (ticket 33.6, section 9): the ground markers of every wind-up (flat rings, an inner
    /// ring growing to the outer, pulsing faster as the hit nears, in hard toon bands, on the floor), the glare's aim line and beam, the
    /// slam's shockwave, the spore balls in flight, the cannon's tracers, the eyes' glow, the stars round a stunned Prime's cap and the glow
    /// of the gills while they are open, the tank drop's hatch light and the muzzle's glow, and the floodlights' halos. All of it into
    /// <b>one</b> <see cref="FxBuffer"/> (one draw call, vertex and fragment shaders only, no compute: WebGL 2), rebuilt every frame from
    /// what <see cref="BossAttacks"/> and <see cref="BossState"/> say; nothing is allocated per frame. The effect material is combat's
    /// (<c>Effect.mat</c>). The shield dome is <see cref="BossShield"/>'s, the particles are combat's.
    /// </summary>
    [DefaultExecutionOrder(190)]
    public sealed class BossTelegraphs : MonoBehaviour
    {
        [Tooltip("Metres a marker's outer ring is wide; the least a line is drawn, so the low preset's blur keeps it")]
        public float lineWidth = 0.4f;
        [Tooltip("Metres over the floor the markers float, over the moss and tufts of the wall's strip")]
        public float lift = 0.22f;

        private static readonly Color White = new Color(1f, 1f, 0.95f);
        private static readonly Color Star = new Color(1f, 0.92f, 0.5f);

        private Boss boss;
        private FxBuffer buffer;
        private float clock;

        /// <summary>Makes the drawing of a fight: one object at the world's origin (the buffer's bounds are round it).</summary>
        public static BossTelegraphs Create(Boss owner)
        {
            var root = new GameObject("Boss Effects");
            root.transform.SetParent(owner.transform, false);
            root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var assets = owner.Combat != null ? owner.Combat.Assets : null;
            if (assets != null && assets.effectMaterial != null)
                renderer.sharedMaterial = assets.effectMaterial;
            else
                Debug.LogWarning("Boss: the weapon table has no effect material (Assets/Effects/Materials/Effect.mat); the boss's markers and beams draw nothing");
            var telegraphs = root.AddComponent<BossTelegraphs>();
            telegraphs.boss = owner;
            telegraphs.buffer = root.AddComponent<FxBuffer>();
            telegraphs.buffer.capacity = 2048;
            return telegraphs;
        }

        private void LateUpdate()
        {
            if (boss == null || boss.State == null)
                return;
            clock += Time.deltaTime;
            buffer.Begin();
            var attacks = boss.Attacks;
            if (attacks != null)
            {
                foreach (var marker in attacks.Markers)
                    DrawMarker(marker);
                DrawGlare(attacks);
                DrawShock(attacks);
                DrawBalls(attacks);
                foreach (var tracer in attacks.Tracers)
                    buffer.Streak(tracer.from, tracer.to, 0.09f, 0.09f, new Color(1f, 0.55f, 0.2f), White, 0.4f, Mathf.Clamp01(tracer.life * 8));
                DrawCopterLights(attacks);
            }
            DrawEyesAndWindows();
            DrawFloodlights();
            buffer.End();
        }

        // ---------------------------------------------------------------- Markers

        private void DrawMarker(BossAttacks.Marker marker)
        {
            // Pulsing faster the nearer the hit: a hard on-off of the dither between full and half
            var pulse = Mathf.Sin(clock * (5 + 22 * marker.progress)) > -0.2f ? 1f : 0.45f;
            var centre = marker.centre + Vector3.up * lift;
            Ring(centre, marker.radius, Mathf.Max(lineWidth, marker.radius * 0.025f), marker.colour, White, 0.28f, pulse);
            // The inner ring grows to the outer as the wind-up runs out
            if (marker.progress < 1)
                Ring(centre + Vector3.up * 0.02f, Mathf.Max(0.1f, marker.radius * marker.progress), lineWidth * 0.7f, Color.Lerp(marker.colour, White, 0.4f), White, 0.2f, pulse);
        }

        // A ring of streaks on the floor: segments enough that it reads as round
        private void Ring(Vector3 centre, float radius, float width, Color band, Color core, float coreShare, float opacity)
        {
            var segments = Mathf.Clamp(Mathf.CeilToInt(radius * 4f), 20, 72);
            var previous = centre + new Vector3(radius, 0, 0);
            for (var i = 1; i <= segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2;
                var next = centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                buffer.Streak(previous, next, width, width, band, core, coreShare, opacity);
                previous = next;
            }
        }

        // ---------------------------------------------------------------- Glare, shockwave, balls

        private void DrawGlare(BossAttacks attacks)
        {
            if (!attacks.GlareOn)
                return;
            var ice = new Color(0.55f, 0.85f, 1f);
            for (var i = 0; i < 2; i++)
            {
                var from = boss.Body.EyePosition(i);
                if (attacks.GlareThin)
                    buffer.Streak(from, attacks.GlareEnd, 0.07f, 0.07f, ice, White, 0.4f, 0.8f);
                else
                    buffer.Streak(from, attacks.GlareEnd, 0.45f, 0.5f, ice, White, 0.45f);
            }
            if (!attacks.GlareThin)
                buffer.Glow(attacks.GlareEnd, 0.9f + 0.2f * Mathf.Sin(clock * 30), ice, White, 0.5f);
        }

        private void DrawShock(BossAttacks attacks)
        {
            if (attacks.ShockRadius < 0)
                return;
            var gold = new Color(1f, 0.78f, 0.2f);
            var fade = Mathf.Clamp01(1.2f - attacks.ShockRadius / 24f);
            var centre = attacks.ShockCentre;
            // A band 0.6 m high: two rings, the top the thinner
            Ring(centre + Vector3.up * 0.2f, attacks.ShockRadius, 0.8f, gold, White, 0.35f, fade);
            Ring(centre + Vector3.up * 0.55f, attacks.ShockRadius, 0.35f, Color.Lerp(gold, White, 0.5f), White, 0.5f, fade);
        }

        private void DrawBalls(BossAttacks attacks)
        {
            foreach (var ball in attacks.Balls)
            {
                var t = Mathf.Clamp01(ball.age / ball.flight);
                var position = Vector3.Lerp(ball.from, ball.to, t) + Vector3.up * (6 * 4 * t * (1 - t));
                buffer.Lit(position, 0.45f, new Color(0.46f, 0.8f, 0.2f), new Color(0.16f, 0.4f, 0.04f));
            }
        }

        private void DrawCopterLights(BossAttacks attacks)
        {
            var copter = boss.Copter;
            if (copter == null || !copter.Active)
                return;
            var flicker = 0.75f + 0.25f * Mathf.Sin(clock * 18);
            if (attacks.HatchGlow)
                buffer.Glow(copter.HatchPosition, 0.8f * flicker, new Color(0.35f, 0.7f, 1f), White, 0.5f);
            if (attacks.MuzzleGlow)
                buffer.Glow(copter.MuzzlePosition, 0.75f * flicker, new Color(1f, 0.4f, 0.25f), White, 0.5f);
        }

        // ---------------------------------------------------------------- Prime: eyes, stars, gills

        private void DrawEyesAndWindows()
        {
            var body = boss.Body;
            if (body == null || !body.gameObject.activeInHierarchy)
                return;
            if (body.GlowAmount > 0)
            {
                var size = 0.5f * body.GlowAmount * (0.8f + 0.2f * Mathf.Sin(clock * 14));
                for (var i = 0; i < 2; i++)
                    buffer.Glow(body.EyePosition(i), size, body.GlowColour, White, 0.45f);
            }
            if (!boss.State.Vulnerable)
                return;
            // The gills glow while the window is open, and spores circle the cap: the weak point is there
            buffer.Glow(body.GillsPosition, 0.7f * (0.8f + 0.2f * Mathf.Sin(clock * 9)), new Color(1f, 0.72f, 0.2f), White, 0.4f);
            var top = body.CapTop + Vector3.up * 1.4f;
            for (var i = 0; i < 6; i++)
            {
                var angle = clock * 2.5f + i * Mathf.PI * 2 / 6;
                var at = top + new Vector3(Mathf.Cos(angle), 0.25f * Mathf.Sin(angle * 2 + i), Mathf.Sin(angle)) * 2.4f;
                buffer.Glow(at, 0.26f, Star, White, 0.5f);
            }
        }

        // ---------------------------------------------------------------- Floodlights

        private void DrawFloodlights()
        {
            var arena = boss.Arena;
            if (arena == null)
                return;
            var colour = arena.FloodlightColour;
            foreach (var position in arena.FloodlightPositions)
                buffer.Glow(position, 4.6f, colour, Color.Lerp(colour, White, 0.5f), 0.55f, 0.55f);
        }
    }
}
