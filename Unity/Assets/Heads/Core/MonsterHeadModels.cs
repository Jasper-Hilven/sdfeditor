using System;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>
    /// The head prefabs the game wears (ticket 45), by id, with the bounds each was measured at. Made by the editor
    /// (SdfHeads.Editor.MonsterHeadBuilder) in Assets/Heads/Generated/Resources/MonsterHeadModels.asset from the prefabs the toon importer
    /// builds out of the meshed models: generated like them, so not in git. Without it (a fresh clone before <c>sdf unity</c>) nothing is worn.
    /// </summary>
    public sealed class MonsterHeadModels : ScriptableObject
    {
        /// <summary>Where it lives under a Resources folder, without the extension.</summary>
        public const string ResourceName = "MonsterHeadModels";

        [Serializable]
        public struct Entry
        {
            public string id;
            [Tooltip("The head with its levels of detail")]
            public GameObject prefab;
            [Tooltip("The bounds of the head's finest level in its own frame (the origin is the soldier's head socket)")]
            public Vector3 boundsCentre;
            public Vector3 boundsSize;
            [Tooltip("The tight fit of the head's mesh (HeadFit): centre of volume, the ellipsoid's radii, the volume; in the head's own frame")]
            public Vector3 fitCentre;
            public Vector3 fitRadii;
            public float fitVolume;
            [Tooltip("The head's underside: the lowest point of its mesh within 12 cm of the neck's axis, in the head's frame")]
            public float underside;
            [Tooltip("The pieces the head bursts into (ticket 51), cut from its own mesh: content/models/characters/head-pieces, in the head's own frame")]
            public GameObject[] pieces;
            [Tooltip("Triangles of the finest level, eyes included")]
            public int triangles;
        }

        public Entry[] entries = new Entry[0];

        static MonsterHeadModels loaded;

        /// <summary>The asset, or null before the heads were imported.</summary>
        public static MonsterHeadModels Load()
        {
            if (loaded == null)
                loaded = Resources.Load<MonsterHeadModels>(ResourceName);
            return loaded;
        }

        /// <summary>The entry of an id; false when the head was not imported.</summary>
        public bool TryGet(string id, out Entry entry)
        {
            if (entries != null && id != null)
                foreach (var candidate in entries)
                    if (candidate.id == id && candidate.prefab != null)
                    {
                        entry = candidate;
                        return true;
                    }
            entry = default;
            return false;
        }

        /// <summary>The hit shape of an entry: the tight fit of its mesh.</summary>
        public static HeadShape ShapeOf(Entry entry) => new HeadShape(entry.fitCentre, entry.fitRadii);

        /// <summary>The tight fit of an entry.</summary>
        public static HeadFit FitOf(Entry entry) => new HeadFit(entry.fitCentre, entry.fitRadii, entry.fitVolume);
    }
}
