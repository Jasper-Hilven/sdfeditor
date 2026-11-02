using System.Linq;
using NUnit.Framework;
using SdfRig;
using UnityEditor;
using UnityEngine;

namespace SdfHeads.Tests
{
    /// <summary>
    /// The built heads (ticket 45): the generated registry, the prefabs, their triangle budget, the fair hit shapes, and a head on the
    /// soldier's socket. Needs <c>sdf mesh</c> of the monsters, the soldier skins and the head builder to have run (generated, not in git).
    /// </summary>
    public sealed class BuiltHeadsTests
    {
        const string RedPrefab = "Assets/Rig/Generated/Soldier/soldier_red.prefab";
        const string Registry = "Assets/Heads/Generated/Resources/MonsterHeadModels.asset";

        static MonsterHeadModels Models()
        {
            var models = AssetDatabase.LoadAssetAtPath<MonsterHeadModels>(Registry);
            Assert.IsNotNull(models, "run MonsterHeadBuilder (Sdf/Heads/Build Head Models)");
            return models;
        }

        [Test]
        public void EveryBuiltHeadIsInTheRegistryWithItsPrefabAndFastPrefab()
        {
            var models = Models();
            foreach (var id in MonsterHeads.BuiltIds())
            {
                Assert.IsTrue(models.TryGet(id, out var entry), id);
                Assert.IsNotNull(entry.prefab, id);
                Assert.IsNotNull(entry.fastPrefab, id + " _fast");
                Assert.IsNotNull(entry.prefab.GetComponentInChildren<LODGroup>(true), id + " has levels of detail");
            }
            Assert.IsFalse(models.TryGet("vampire", out _), "the unbuilt are not listed");
        }

        [Test]
        public void TheHeadsStayInTheTriangleBudgetAtTheFinestLevel()
        {
            foreach (var entry in Models().entries)
            {
                Assert.Greater(entry.triangles, 500, entry.id);
                Assert.LessOrEqual(entry.triangles, 5000, $"{entry.id}: {entry.triangles}");
            }
        }

        [Test]
        public void TheHeadsAreOneAndAHalfToTwoTimesAHumanHead()
        {
            // A human head is about 0.23 m tall and 0.16 m wide
            foreach (var entry in Models().entries)
            {
                Assert.GreaterOrEqual(entry.boundsSize.y, 0.23f * 1.5f, entry.id + " height");
                Assert.LessOrEqual(entry.boundsSize.y, 0.23f * 2.8f, entry.id + " height");
                Assert.GreaterOrEqual(Mathf.Max(entry.boundsSize.x, entry.boundsSize.z), 0.16f * 2f, entry.id + " width");
            }
        }

        [Test]
        public void TheHeadsStandOnTheNeckAndClearTheCollar()
        {
            foreach (var entry in Models().entries)
            {
                var low = SoldierBody.HeadTopHeight + entry.boundsCentre.y - entry.boundsSize.y * 0.5f;
                Assert.GreaterOrEqual(low, SoldierBody.NeckTop - 0.1f, entry.id + " underside is not lost in the chest");
                Assert.LessOrEqual(low, SoldierBody.NeckTop + 0.05f, entry.id + " underside is not floating over the neck");
            }
        }

        [Test]
        public void TheBuiltHeadsHitShapesAreFairAndAlike()
        {
            var shapes = Models().entries.Select(MonsterHeadModels.ShapeOf).ToArray();
            Assert.IsTrue(HeadHit.AreFair(shapes), string.Join("; ", shapes.Select(s => s.ToString())));
        }

        [Test]
        public void AHeadSitsOnTheSoldiersSocketFacingForwardAndResizesTheHeadCapsule()
        {
            var models = Models();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RedPrefab);
            Assert.IsNotNull(prefab, "run SoldierSkins");
            foreach (var entry in models.entries)
            {
                var soldier = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var worn = HeadWear.Attach(soldier.transform, entry.id, entry.prefab, MonsterHeadModels.ShapeOf(entry));
                Assert.IsNotNull(worn, entry.id);
                var socket = SoldierBody.FindSocket(soldier.transform, "head_top");
                Assert.AreSame(socket, worn.transform.parent);
                Assert.AreEqual(Vector3.zero, worn.transform.localPosition);
                Assert.Greater(Vector3.Dot(worn.transform.forward, soldier.transform.forward), 0.999f, "faces the soldier's front");
                Assert.Greater(Vector3.Dot(worn.transform.up, Vector3.up), 0.999f, "upright");

                var capsule = SoldierBody.HeadCapsule(soldier.transform);
                var shape = MonsterHeadModels.ShapeOf(entry);
                Assert.AreEqual(shape.RadiusAcross, capsule.radius, 1e-4f);
                Assert.AreEqual(shape.RadiusUp * 2, capsule.height, 0.01f); // the Head bone is a few degrees off upright
                Object.DestroyImmediate(soldier);
            }
        }

        [Test]
        public void AnUnknownHeadOrNoSocketAttachesNothing()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RedPrefab);
            var soldier = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Assert.IsNull(HeadWear.Attach(soldier.transform, "dragon"));
            Assert.IsNull(HeadWear.Attach(soldier.transform, null));
            Assert.IsNull(HeadWear.Attach(null, "yeti"));
            var bare = new GameObject("bare");
            Assert.IsNull(HeadWear.Attach(bare.transform, "yeti", Models().entries[0].prefab, default));
            Object.DestroyImmediate(soldier);
            Object.DestroyImmediate(bare);
        }
    }
}
