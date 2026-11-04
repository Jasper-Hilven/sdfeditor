using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The LOD0 meshes of a streamed level (ticket 60). The web build's level bundles hold, in place of each LOD0 mesh that only a LOD
    /// group's finest level uses, a three-vertex stub named <c>model_body#stub</c> with the real mesh's bounds; the real meshes (named
    /// <c>model_body</c>) are in the level's <c>-lod0</c> bundles, fetched once the match is running. Until they are swapped in the LOD0 slot
    /// shows the LOD1 mesh (<c>model_body_LOD1</c>); in the editor and in builds without streaming there are no stubs and none of this runs.
    /// </summary>
    public static class Lod0Meshes
    {
        public const string StubSuffix = "#stub";
        private const string Lod1Suffix = "_LOD1";

        /// <summary>The real LOD0 meshes of the level in play by name, once they have arrived.</summary>
        public static readonly Dictionary<string, Mesh> Loaded = new Dictionary<string, Mesh>();

        public static bool IsStub(Mesh mesh) => mesh != null && mesh.name.EndsWith(StubSuffix, StringComparison.Ordinal);

        /// <summary>The name of the real mesh a stub stands for (the name itself for any other mesh).</summary>
        public static string RealName(string name) => name.EndsWith(StubSuffix, StringComparison.Ordinal) ? name.Substring(0, name.Length - StubSuffix.Length) : name;

        /// <summary>The part a mesh is a level of: model_body_LOD2 is model_body.</summary>
        public static string PartOf(string mesh)
        {
            var lod = mesh.LastIndexOf("_LOD", StringComparison.Ordinal);
            return lod > 0 && int.TryParse(mesh.Substring(lod + 4), out _) ? mesh.Substring(0, lod) : mesh;
        }

        /// <summary>
        /// The real mesh for what sits in a LOD group's LOD0 slot (a stub, or the LOD1 mesh standing in for one), when it has arrived;
        /// null when there is nothing to replace it with.
        /// </summary>
        public static Mesh RealFor(Mesh inSlot)
        {
            if (inSlot == null || Loaded.Count == 0)
                return null;
            var name = inSlot.name;
            string key = null;
            if (name.EndsWith(StubSuffix, StringComparison.Ordinal))
                key = RealName(name);
            else if (name.EndsWith(Lod1Suffix, StringComparison.Ordinal))
                key = name.Substring(0, name.Length - Lod1Suffix.Length);
            return key != null && Loaded.TryGetValue(key, out var real) && real != inSlot ? real : null;
        }

        /// <summary>
        /// <paramref name="levels"/> with the stubs of level 0 replaced by the same part's mesh at level 1 (the real mesh where it has
        /// arrived); the same array when there is no stub in it.
        /// </summary>
        public static ScatterLevel[] WithFallback(ScatterLevel[] levels)
        {
            if (levels == null || levels.Length < 2)
                return levels;
            var any = false;
            foreach (var part in levels[0].parts)
                any |= IsStub(part.mesh);
            if (!any)
                return levels;
            var copy = (ScatterLevel[])levels.Clone();
            var parts = new ScatterPart[levels[0].parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var part = levels[0].parts[i];
                parts[i] = part;
                if (!IsStub(part.mesh))
                    continue;
                var key = RealName(part.mesh.name);
                Mesh use = null;
                if (Loaded.TryGetValue(key, out var real))
                    use = real;
                else
                    foreach (var finer in levels[1].parts)
                        if (finer.mesh != null && PartOf(finer.mesh.name) == key)
                        {
                            use = finer.mesh;
                            break;
                        }
                if (use != null)
                    parts[i] = new ScatterPart { mesh = use, transform = part.transform, outline = part.outline, shadows = part.shadows };
            }
            copy[0] = new ScatterLevel { parts = parts };
            return copy;
        }
    }
}
