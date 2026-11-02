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
            foreach (var head in MonsterHeads.All.Where(h => !h.Built))
                Assert.IsFalse(models.TryGet(head.Id, out _), head.Id + ": the unbuilt are not listed");
        }

        [Test]
        public void EveryBuiltHeadBurstsIntoItsOwnPieces()
        {
            // Ticket 51: seven pieces cut from the head's own mesh (content/models/characters/head-pieces), found by id: no gore code per head
            foreach (var id in MonsterHeads.BuiltIds())
            {
                Assert.IsTrue(Models().TryGet(id, out var entry), id);
                Assert.AreEqual(7, entry.pieces.Length, id);
                var union = new Bounds();
                var any = false;
                foreach (var piece in entry.pieces)
                {
                    Assert.IsNotNull(piece, id);
                    foreach (var filter in piece.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null || filter.name.Contains("_LOD") || filter.name == "Outline")
                            continue;
                        if (any)
                            union.Encapsulate(filter.sharedMesh.bounds);
                        else
                            union = filter.sharedMesh.bounds;
                        any = true;
                    }
                }
                Assert.IsTrue(any, id + " pieces have meshes");
                // Together they are the head: the pieces' bounds cover the head's (within a centimetre or two)
                Assert.AreEqual(entry.boundsSize.y, union.size.y, 0.05f, id + " height");
                Assert.AreEqual(entry.boundsSize.x, union.size.x, 0.05f, id + " width");
            }
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
                Assert.LessOrEqual(entry.boundsSize.y, 0.23f * 4f, entry.id + " height");
                Assert.GreaterOrEqual(Mathf.Max(entry.boundsSize.x, entry.boundsSize.z), 0.16f * 2f, entry.id + " width");
            }
        }

        [Test]
        public void TheHeadsStandOnTheNeckAndClearTheCollar()
        {
            // 4 cm lower than phase 1, then at least 5 cm more (user review 2: no visible neck; each head's own lowering is in its _head part): the underside at 1.475 m or lower, the neck's top (1.569) inside the head
            foreach (var entry in Models().entries)
            {
                var low = SoldierBody.HeadTopHeight + entry.underside;
                Assert.LessOrEqual(low, 1.475f + 0.001f, entry.id + " underside (at least 5 cm under phase 2's 1.525 m)");
                Assert.Greater(low, 1.15f, entry.id + " underside (not through the chest)");
                Assert.Less(low, SoldierBody.NeckTop - 0.02f, entry.id + " the neck's top is inside the head");
            }
        }

        [Test]
        public void EveryBuiltHeadIsTheCommonSizeWithinTenPercent()
        {
            // The normalized size (user decision): volume and each axis's radius within 10 % of 1.2x the mean of phase 1's Yeti and pumpkin
            foreach (var entry in Models().entries)
            {
                var fit = MonsterHeadModels.FitOf(entry);
                Assert.IsTrue(HeadSize.IsNormalized(fit), $"{entry.id}: {fit}, target {HeadSize.TargetRadii:F3} {HeadSize.TargetVolume:0.0000} m3, off by {HeadSize.Deviation(fit):P1}");
            }
        }

        [Test]
        public void TheHitShapeIsTheTightFitOfTheVisibleHeadAndEveryOneIsAlike()
        {
            foreach (var entry in Models().entries)
            {
                var shape = MonsterHeadModels.ShapeOf(entry);
                var fit = MonsterHeadModels.FitOf(entry);
                Assert.AreEqual(fit.Radii, shape.Radii, entry.id);
                Assert.AreEqual(fit.Centre, shape.Centre, entry.id);
                // The ellipsoid has the volume of the mesh
                Assert.AreEqual(fit.Volume, 4f / 3f * Mathf.PI * shape.Radii.x * shape.Radii.y * shape.Radii.z, fit.Volume * 0.01f, entry.id);
                // Equal within 10 % of the target on each axis, so no head is easier to hit than another by more than 22 %
                var radii = HeadSize.TargetRadii;
                Assert.AreEqual(radii.x, shape.Radii.x, radii.x * HeadSize.Tolerance, entry.id + " x");
                Assert.AreEqual(radii.y, shape.Radii.y, radii.y * HeadSize.Tolerance, entry.id + " y");
                Assert.AreEqual(radii.z, shape.Radii.z, radii.z * HeadSize.Tolerance, entry.id + " z");
            }
        }

        [Test]
        public void AShotJustOutsideTheVisibleHeadMissesAndJustInsideHitsOnEveryBuiltHead()
        {
            // Through HeadHit.Hit, the test Combat.Trace runs for a worn head, on the head's own fitted shape; a player standing at 3, 0, 2 turned 30 degrees
            var feet = new Vector3(3, 0, 2);
            const float yaw = 30;
            var turn = Quaternion.Euler(0, yaw, 0);
            foreach (var entry in Models().entries)
            {
                var shape = MonsterHeadModels.ShapeOf(entry);
                var centre = feet + Vector3.up * SoldierBody.HeadTopHeight + turn * shape.Centre;
                // Shoot along the head's own x, y and z axes from 6 m off, grazing 2 cm inside or outside the ellipsoid at its widest
                foreach (var axis in new[] { 0, 1, 2 })
                {
                    // The ray runs along z for the x and y axes and along x for the z axis, offset on the tested axis
                    var along = axis == 2 ? Vector3.right : Vector3.forward;
                    var offsetAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                    var extent = shape.Radii[axis];
                    var side = turn * offsetAxis;
                    var start = centre - turn * along * 6;
                    var dir = turn * along;
                    Assert.IsTrue(HeadHit.Hit(shape, feet, yaw, start + side * (extent - 0.02f), dir, 0, out var t, out _), $"{entry.id} axis {axis} just inside");
                    Assert.Less(t, 6f);
                    Assert.IsFalse(HeadHit.Hit(shape, feet, yaw, start + side * (extent + 0.02f), dir, 0, out _, out _), $"{entry.id} axis {axis} just outside");
                }
            }
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
                Assert.AreEqual(Mathf.Max(shape.RadiusUp, shape.RadiusAcross) * 2, capsule.height, 0.01f); // a capsule is at least as tall as it is wide; // the Head bone is a few degrees off upright
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
