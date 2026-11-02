using System.Collections.Generic;
using SdfBody.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Tests
{
    public class LocomotionClipSpeedsTests
    {
        [SetUp]
        public void NeedsTheModels()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LocomotionClipSpeeds.RigPrefab) == null)
                Assert.Ignore("the soldier prefab is not built: run `sdf unity`, then SdfRig.Editor.SoldierSkins.ImportAll");
        }

        private static SoldierClipMap Map => AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapBuilder.AssetPath);
        private static readonly Dictionary<string, LocomotionClipSpeeds.Measure> Cache = new();

        private static LocomotionClipSpeeds.Measure Measure(string name)
        {
            if (!Cache.TryGetValue(name, out var m))
                Cache[name] = m = LocomotionClipSpeeds.OfMap(Map, new[] { name })[0];
            return m;
        }

        private const float D = 0.70710678f;

        [TestCase("soldier_loco_walk_f", 0f, 1f)]
        [TestCase("soldier_loco_walk_b", 0f, -1f)]
        [TestCase("soldier_loco_strafe_l", -1f, 0f)]
        [TestCase("soldier_loco_strafe_r", 1f, 0f)]
        [TestCase("soldier_loco_walk_fl", -D, D)]
        [TestCase("soldier_loco_walk_fr", D, D)]
        [TestCase("soldier_loco_walk_bl", -D, -D)]
        [TestCase("soldier_loco_walk_br", D, -D)]
        [TestCase("soldier_loco_run_f", 0f, 1f)]
        [TestCase("soldier_loco_run_b", 0f, -1f)]
        [TestCase("soldier_loco_run_l", -1f, 0f)]
        [TestCase("soldier_loco_run_r", 1f, 0f)]
        public void ClipHasPlausibleSpeedAndDirection(string name, float x, float y)
        {
            var m = Measure(name);
            Assert.That(m.MetresPerSecond, Is.GreaterThan(0.5f).And.LessThan(8f), m.ToString());
            Assert.GreaterOrEqual(m.PlantedSamples, 10, m.ToString());
            Assert.Less(Vector2.Angle(m.Direction, new Vector2(x, y)), 40f, m.ToString());
        }

        [TestCase("soldier_loco_walk_f", "soldier_loco_run_f")]
        [TestCase("soldier_loco_walk_b", "soldier_loco_run_b")]
        [TestCase("soldier_loco_strafe_l", "soldier_loco_run_l")]
        [TestCase("soldier_loco_strafe_r", "soldier_loco_run_r")]
        public void RunIsFasterThanWalk(string walk, string run)
        {
            Assert.Greater(Measure(run).MetresPerSecond, Measure(walk).MetresPerSecond, $"{Measure(run)} vs {Measure(walk)}");
        }

        [Test]
        public void AimIdleIsSlow()
        {
            var m = Measure("soldier_loco_aim_idle");
            Assert.Less(m.MetresPerSecond, 0.3f, m.ToString());
        }

        [Test]
        public void MeasureIsDeterministic()
        {
            var a = LocomotionClipSpeeds.OfMap(Map, new[] { "soldier_loco_walk_f" })[0];
            var b = LocomotionClipSpeeds.OfMap(Map, new[] { "soldier_loco_walk_f" })[0];
            Assert.AreEqual(a.MetresPerSecond, b.MetresPerSecond);
            Assert.AreEqual(a.Direction, b.Direction);
            Assert.AreEqual(a.PlantedSamples, b.PlantedSamples);
        }
    }
}
