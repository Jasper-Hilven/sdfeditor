using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>The bone paths and names of <c>content/skeletons/synty.skeleton</c> (the 50 Synty bones), relative to the rig root (ticket 03).</summary>
    public static class SyntySkeletonPaths
    {
        private static HashSet<string> _paths;
        private static HashSet<string> _names;

        public static string SkeletonFile => Path.GetFullPath(Application.dataPath + "/../../content/skeletons/synty.skeleton");

        /// <summary>Every bone path: <c>Root</c>, <c>Root/Hips</c>, ...</summary>
        public static IReadOnlyCollection<string> Paths
        {
            get { Load(); return _paths; }
        }

        /// <summary>Every bone name (<c>Finger_011</c>).</summary>
        public static IReadOnlyCollection<string> Names
        {
            get { Load(); return _names; }
        }

        public static bool IsPath(string path) { Load(); return _paths.Contains(path); }

        private static void Load()
        {
            if (_paths != null)
                return;
            var file = SkeletonFile;
            if (!File.Exists(file))
                throw new FileNotFoundException("synty.skeleton not found at " + file);
            var paths = new HashSet<string>();
            var names = new HashSet<string>();
            var byName = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(file))
            {
                if (!line.StartsWith("bone ", StringComparison.Ordinal))
                    continue;
                var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 4)
                    throw new InvalidDataException("bad skeleton line: " + line);
                var name = t[1];
                var parent = t[3];
                string path;
                if (parent == "-")
                    path = name;
                else if (byName.TryGetValue(parent, out var pp))
                    path = pp + "/" + name;
                else
                    throw new InvalidDataException($"bone {name}: parent {parent} not listed before it");
                byName[name] = path;
                paths.Add(path);
                names.Add(name);
            }
            if (paths.Count == 0)
                throw new InvalidDataException("no bones in " + file);
            _names = names;
            _paths = paths;
        }
    }
}
