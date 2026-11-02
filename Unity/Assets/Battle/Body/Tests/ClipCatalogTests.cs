using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfBody.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Tests
{
    public class ClipCatalogTests
    {
        public static IEnumerable<TestCaseData> Entries() =>
            SoldierClips.All.Select(e => new TestCaseData(e).SetName("Clip_" + e.Name));

        [TestCaseSource(nameof(Entries))]
        public void Clip_is_well_formed(ClipEntry e)
        {
            var path = SoldierClipAssets.PathOf(e);
            Assert.IsTrue(File.Exists(path), "missing file " + path);
            var clip = SoldierClipAssets.Load(e);
            Assert.IsNotNull(clip, "clip does not load from " + path);
            Assert.AreEqual(e.Name, clip.name, "clip name");
            if (e.IsFbx)
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.IsNotNull(mi);
                Assert.AreEqual(ModelImporterAnimationType.Generic, mi.animationType);
                Assert.AreEqual(ModelImporterAvatarSetup.NoAvatar, mi.avatarSetup);
            }
            Assert.IsFalse(clip.legacy, "legacy");
            Assert.IsFalse(clip.humanMotion, "humanMotion");
            var unknown = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct().Where(p => !SyntySkeletonPaths.IsPath(p)).ToList();
            Assert.IsEmpty(unknown, "curve paths not in the Synty skeleton: " + string.Join(", ", unknown.Take(5)));
            Assert.AreEqual(e.Loop, AnimationUtility.GetAnimationClipSettings(clip).loopTime, "loopTime");
            Assert.Greater(clip.length, 0f, "length");
            Assert.AreEqual(e.Category.ToString(), Path.GetFileName(Path.GetDirectoryName(path)), "folder");
        }

        [Test]
        public void Names_are_unique_convention_names()
        {
            var names = SoldierClips.All.Select(e => e.Name).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count(), "duplicate names");
            foreach (var n in names)
                Assert.IsTrue(SoldierClips.IsConventionName(n), n);
        }

        [Test]
        public void No_stray_files()
        {
            var known = new HashSet<string>(SoldierClips.All.Select(e => e.File));
            var root = SoldierClips.Folder.TrimEnd('/');
            var stray = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/'))
                .Where(f => (f.EndsWith(".anim") || f.EndsWith(".fbx")) && !f.StartsWith(SoldierClips.MixamoFolder, StringComparison.Ordinal))
                .Select(f => f.Substring(SoldierClips.Folder.Length))
                .Where(f => !known.Contains(f)).ToList();
            Assert.IsEmpty(stray, "files not in the catalog: " + string.Join(", ", stray));
        }

        [Test]
        public void No_mesh_in_any_fbx()
        {
            var bad = new List<string>();
            foreach (var f in Directory.GetFiles(SoldierClips.Folder.TrimEnd('/'), "*.fbx", SearchOption.AllDirectories))
            {
                var p = f.Replace('\\', '/');
                if (AssetDatabase.LoadAllAssetsAtPath(p).Any(o => o is Mesh || o is SkinnedMeshRenderer))
                    bad.Add(p);
            }
            Assert.IsEmpty(bad, "FBX with a mesh (re-download from Mixamo with Skin \"Without Skin\"): " + string.Join(", ", bad));
        }

        [Test]
        public void Gaps_are_consistent()
        {
            foreach (var g in SoldierClips.Gaps)
            {
                var stand = SoldierClips.Find(g.StandIn);
                Assert.IsNotNull(stand, $"{g.Name}: stand-in {g.StandIn} not in the catalog");
                Assert.IsFalse(stand.IsAlt, $"{g.Name}: stand-in {g.StandIn} is an alt clip");
                Assert.IsTrue(SoldierClips.IsConventionName(g.Name), g.Name);
                Assert.IsNull(SoldierClips.Find(g.Name), $"{g.Name} is also in All");
            }
        }

        [Test]
        public void Committed_clip_map_matches_the_builder()
        {
            var map = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapBuilder.AssetPath);
            Assert.IsNotNull(map, "missing " + ClipMapBuilder.AssetPath);
            var want = ClipMapBuilder.Slots();
            Assert.IsTrue(ClipMapBuilder.SameSlots(map.Slots, want), "SoldierClipMap.asset differs from what ClipMapBuilder builds: run Sdf/Body/Build Clip Map");
            foreach (var e in SoldierClips.All.Where(x => !x.IsAlt))
                Assert.IsTrue(map.TryGet(e.Name, out _), "no slot for " + e.Name);
            foreach (var g in SoldierClips.Gaps)
                Assert.IsTrue(map.TryGet(g.Name, out _), "no slot for " + g.Name);
            foreach (var s in map.Slots)
                Assert.IsNotNull(s.clip, "slot without clip: " + s.name);
        }

        [Test]
        public void Category_loop_rules()
        {
            foreach (var e in SoldierClips.All)
            {
                if (e.Category == ClipCategory.Locomotion)
                    Assert.IsTrue(e.Loop, e.Name);
            }
        }
    }
}
