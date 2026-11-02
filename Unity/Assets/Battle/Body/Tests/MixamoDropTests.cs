using System.Collections.Generic;
using System.Linq;
using SdfBody.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Tests
{
    public class MixamoDropTests
    {
        private const string Source = SoldierClips.Folder + "Misc/rifle jump.fbx";
        private List<string> _before;
        private bool _suppress;

        public static IEnumerable<TestCaseData> DropCases()
        {
            var drops = MixamoDrop.Drops().ToList();
            if (drops.Count == 0)
                yield return new TestCaseData(null).SetName("Drop_none");
            foreach (var d in drops)
                yield return new TestCaseData(d).SetName("Drop_" + System.IO.Path.GetFileNameWithoutExtension(d));
        }

        [SetUp]
        public void SetUp()
        {
            _before = MixamoDrop.Drops().ToList();
            _suppress = MixamoDrop.SuppressMapRebuild;
            MixamoDrop.SuppressMapRebuild = true;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                foreach (var d in MixamoDrop.Drops().Except(_before).ToList())
                    AssetDatabase.DeleteAsset(d);
                AssetDatabase.Refresh();
                CollectionAssert.AreEquivalent(_before, MixamoDrop.Drops().ToList(), "the Mixamo folder changed");
            }
            finally
            {
                MixamoDrop.SuppressMapRebuild = _suppress;
            }
        }

        [TestCaseSource(nameof(DropCases))]
        public void Drop_is_valid(string path)
        {
            if (path == null)
                Assert.Pass("no Mixamo drops yet");
            var problems = MixamoDrop.Validate(path);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        private static string Copy(string name)
        {
            var dest = SoldierClips.MixamoFolder + name + ".fbx";
            Assert.IsTrue(AssetDatabase.CopyAsset(Source, dest), "copy to " + dest);
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            return dest;
        }

        [Test]
        public void Auto_import_round_trip()
        {
            var roar = Copy("body_roar");
            Assert.IsEmpty(MixamoDrop.Validate(roar), string.Join("\n", MixamoDrop.Validate(roar)));
            var clip = SoldierClipAssets.LoadAt(roar, "body_roar");
            Assert.AreEqual("body_roar", clip.name);
            Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(clip).loopTime);

            var heavy = Copy("body_heavy_run_f");
            Assert.IsEmpty(MixamoDrop.Validate(heavy), string.Join("\n", MixamoDrop.Validate(heavy)));
            Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(SoldierClipAssets.LoadAt(heavy, "body_heavy_run_f")).loopTime);

            var bad = Copy("Pistol Run");
            var problems = MixamoDrop.Validate(bad);
            Assert.IsTrue(problems.Any(p => p.Contains("name")), "expected a name problem, got: " + string.Join(" | ", problems));
        }

        [Test]
        public void Builder_resolves_a_valid_drop()
        {
            var before = ClipMapBuilder.Slots().First(s => s.name == "body_victory");
            Assert.AreEqual("soldier_emote_dance_1", before.standIn);
            var victory = Copy("body_victory");
            var slot = ClipMapBuilder.Slots().First(s => s.name == "body_victory");
            Assert.AreEqual("", slot.standIn);
            Assert.AreEqual(-1f, slot.holdAt);
            Assert.AreEqual("body_victory", slot.clip.name);
            Assert.AreEqual(victory, AssetDatabase.GetAssetPath(slot.clip));
            Assert.IsFalse(slot.loop);
        }
    }
}
