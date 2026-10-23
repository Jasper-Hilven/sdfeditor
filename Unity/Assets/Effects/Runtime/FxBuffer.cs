using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfEffects
{
    /// <summary>
    /// Every streak and ball the effects draw in a frame, in one mesh drawn with SdfEffect.shader by this object's renderer: one draw
    /// call for all of them. The effects add their quads between <see cref="Begin"/> and <see cref="End"/> each frame; the mesh keeps
    /// only where things are, and the shader turns each quad to the camera, so it draws right from any camera.
    /// <para>
    /// No allocation per frame: the vertex array is made once at <see cref="capacity"/> quads, the index buffer is a fixed pattern of
    /// quads, and only the part in use is uploaded. Quads past the capacity are dropped.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FxBuffer : MonoBehaviour
    {
        [Tooltip("Most quads drawn in one frame")]
        public int capacity = 4096;

        /// <summary>The kind of quad, as SdfEffect.shader reads it from uv0.w.</summary>
        private const float StreakKind = 0, GlowKind = 1, LitKind = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Color color;
            public Vector4 shape;
            public Vector4 core;
        }

        private Mesh mesh;
        private Vertex[] vertices;
        private int quads;
        /// <summary>The quads the mesh holds: <see cref="capacity"/>, times more on Epic and up for the particles' copies.</summary>
        private int room;

        /// <summary>Quads drawn last frame, for the stats.</summary>
        public int LastQuads { get; private set; }

        private void Ensure()
        {
            var wanted = capacity * (1 + SdfMushrooms.QualityExtras.ParticleCopies);
            if (mesh != null && room == wanted)
                return;
            if (mesh != null)
                DestroyImmediate(mesh);
            room = wanted;
            mesh = new Mesh { name = "Effects", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            vertices = new Vertex[room * 4];
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4));
            var indices = new int[room * 6];
            for (var q = 0; q < room; q++)
            {
                indices[q * 6] = q * 4;
                indices[q * 6 + 1] = q * 4 + 1;
                indices[q * 6 + 2] = q * 4 + 2;
                indices[q * 6 + 3] = q * 4;
                indices[q * 6 + 4] = q * 4 + 2;
                indices[q * 6 + 5] = q * 4 + 3;
            }
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices);
            mesh.subMeshCount = 1;
            // The effects stay round the bench; the bounds only have to hold them, so the renderer is never culled
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), Flags);
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices |
                                              MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        private void OnDestroy()
        {
            if (mesh != null)
                DestroyImmediate(mesh);
        }

        public void Begin()
        {
            Ensure();
            quads = 0;
        }

        /// <summary>Uploads this frame's quads.</summary>
        public void End()
        {
            Ensure();
            mesh.SetVertexBufferData(vertices, 0, 0, quads * 4, 0, Flags);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, quads * 6) { bounds = mesh.bounds, vertexCount = quads * 4 }, Flags);
            LastQuads = quads;
        }

        /// <summary>
        /// A streak from <paramref name="from"/> to <paramref name="to"/>, <paramref name="widthFrom"/> and <paramref name="widthTo"/>
        /// wide at its ends (so it can taper), in the band colour with a core of <paramref name="coreShare"/> of its width.
        /// </summary>
        public void Streak(Vector3 from, Vector3 to, float widthFrom, float widthTo, Color band, Color core, float coreShare, float opacity = 1)
        {
            if (quads >= room)
                return;
            var along = to - from;
            if (along.sqrMagnitude < 1e-10f)
                return;
            band.a = opacity;
            var coreData = new Vector4(core.r, core.g, core.b, coreShare);
            var v = quads * 4;
            vertices[v] = new Vertex { position = from, normal = along, color = band, shape = new Vector4(0, -1, widthFrom * 0.5f, StreakKind), core = coreData };
            vertices[v + 1] = new Vertex { position = from, normal = along, color = band, shape = new Vector4(0, 1, widthFrom * 0.5f, StreakKind), core = coreData };
            vertices[v + 2] = new Vertex { position = to, normal = along, color = band, shape = new Vector4(1, 1, widthTo * 0.5f, StreakKind), core = coreData };
            vertices[v + 3] = new Vertex { position = to, normal = along, color = band, shape = new Vector4(1, -1, widthTo * 0.5f, StreakKind), core = coreData };
            quads++;
        }

        /// <summary>A flat glowing disc of radius <paramref name="radius"/>, the band colour round a core.</summary>
        public void Glow(Vector3 center, float radius, Color band, Color core, float coreShare, float opacity = 1) =>
            Ball(center, radius, band, new Vector4(core.r, core.g, core.b, coreShare), GlowKind, opacity);

        /// <summary>A ball shaded as a toon sphere with an ink line, with <paramref name="glow"/> added in its shade.</summary>
        public void Lit(Vector3 center, float radius, Color color, Color glow, float opacity = 1) =>
            Ball(center, radius, color, new Vector4(glow.r, glow.g, glow.b, 0), LitKind, opacity);

        private void Ball(Vector3 center, float radius, Color band, Vector4 core, float kind, float opacity)
        {
            if (quads >= room || radius <= 0)
                return;
            band.a = opacity;
            var v = quads * 4;
            vertices[v] = new Vertex { position = center, normal = Vector3.up, color = band, shape = new Vector4(-1, -1, radius, kind), core = core };
            vertices[v + 1] = new Vertex { position = center, normal = Vector3.up, color = band, shape = new Vector4(-1, 1, radius, kind), core = core };
            vertices[v + 2] = new Vertex { position = center, normal = Vector3.up, color = band, shape = new Vector4(1, 1, radius, kind), core = core };
            vertices[v + 3] = new Vertex { position = center, normal = Vector3.up, color = band, shape = new Vector4(1, -1, radius, kind), core = core };
            quads++;
        }
    }
}
