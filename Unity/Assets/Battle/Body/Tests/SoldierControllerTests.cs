using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SdfBody.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SdfBody.Tests
{
    /// <summary>The built Soldier.controller, the clips on the soldier prefab, and the SoldierAnimator on a real body (ticket 44).</summary>
    public class SoldierControllerTests
    {
        const string BluePrefab = LocomotionClipSpeeds.RigPrefab;

        static AnimatorController Controller => AssetDatabase.LoadAssetAtPath<AnimatorController>(SoldierControllerBuilder.ControllerPath);

        static GameObject NeedRig()
        {
            var rig = LocomotionClipSpeeds.InstantiateRig();
            if (rig == null)
                Assert.Ignore("the soldier prefab is not built: run `sdf unity`, then SdfRig.Editor.SoldierSkins.ImportAll");
            return rig;
        }

        static IEnumerable<string> StateNames(AnimatorStateMachine m)
        {
            foreach (var s in m.states) yield return s.state.name;
            foreach (var sub in m.stateMachines)
                foreach (var n in StateNames(sub.stateMachine)) yield return sub.stateMachine.name + "." + n;
        }

        [Test]
        public void TheControllerHasTheBaseAndTheUpperBodyLayersAndNoDeathLayer()
        {
            var c = Controller;
            Assert.IsNotNull(c, "run Sdf/Body/Build All");
            CollectionAssert.AreEqual(new[] { SoldierAnimator.Names.BaseLayer, SoldierAnimator.Names.UpperBodyLayer }, c.layers.Select(l => l.name).ToArray());
            Assert.IsNotNull(c.layers[1].avatarMask, "the upper body is masked");
        }

        [Test]
        public void EveryParameterTheAnimatorWritesExists()
        {
            var have = Controller.parameters.ToDictionary(p => p.name, p => p.type);
            var n = typeof(SoldierAnimator.Names);
            foreach (var name in new[] { "MoveX", "MoveY", "Speed", "Stance", "Jump", "Airborne", "Fire", "FireAuto", "Reload", "ReloadSpeed", "Hit", "HitX", "Dead" })
                Assert.IsTrue(have.ContainsKey(name), "no parameter " + name);
            Assert.AreEqual(AnimatorControllerParameterType.Trigger, have[SoldierAnimator.Names.Jump]);
            Assert.AreEqual(AnimatorControllerParameterType.Bool, have[SoldierAnimator.Names.Airborne]);
            Assert.AreEqual(AnimatorControllerParameterType.Float, have[SoldierAnimator.Names.Stance]);
            Assert.IsNotNull(n);
        }

        [Test]
        public void TheBaseLayerHasTheLocomotionTheAirAndTheDances()
        {
            var names = StateNames(Controller.layers[0].stateMachine).ToList();
            foreach (var want in new[] { "Locomotion", "JumpUp", "Fall", "Land", "Emotes.soldier_emote_dance_1", "Emotes.soldier_emote_dance_2" })
                CollectionAssert.Contains(names, want);
            // The roar and the victory have no state until their own clips are in
            CollectionAssert.DoesNotContain(names, "Emotes.body_roar");
            CollectionAssert.DoesNotContain(names, "Emotes.body_victory");
            var upper = StateNames(Controller.layers[1].stateMachine).ToList();
            foreach (var want in new[] { "Empty", "Fire", "FireBurst", "Reload", "Hit" })
                CollectionAssert.Contains(upper, want);
        }

        [Test]
        public void TheLocomotionBlendsThreeStancesOf18Clips()
        {
            var loco = Controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state;
            var root = (BlendTree)loco.motion;
            Assert.AreEqual(3, root.children.Length);
            foreach (var stance in root.children)
            {
                var tree = (BlendTree)stance.motion;
                Assert.AreEqual(18, tree.children.Length, tree.name);
                Assert.IsTrue(tree.children.All(ch => ch.motion is AnimationClip), tree.name);
            }
        }

        [Test]
        public void ABuildIsTheSameControllerAgain()
        {
            Assert.IsFalse(SoldierControllerBuilder.BuildController(), "a second build changes nothing");
        }

        [Test]
        public void TheTuningPointsAtTheController()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<LocomotionTuning>(SoldierControllerBuilder.TuningPath);
            Assert.IsNotNull(tuning);
            Assert.AreSame(Controller, tuning.Controller);
        }

        [Test]
        public void TheLandingStateLeavesByItsClipOnceTheOwnClipIsIn()
        {
            // A map in which the landing has its own clip (what a Mixamo drop makes): the state plays at speed 1 and leaves by exit time
            var map = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapBuilder.AssetPath);
            var own = Object.Instantiate(map);
            var slots = own.Slots.ToList();
            var i = slots.FindIndex(s => s.name == "body_land");
            slots[i] = new SoldierClipMap.Slot { name = "body_land", clip = map.Get("soldier_hit"), standIn = "", holdAt = -1f, loop = false };
            own.SetSlots(slots);
            const string path = "Assets/Battle/Body/Animator/Test.controller";
            AssetDatabase.DeleteAsset(path);
            var temp = AnimatorController.CreateAnimatorControllerAtPath(path);
            try
            {
                SoldierControllerBuilder.Fill(temp, own, AssetDatabase.LoadAssetAtPath<LocomotionTuning>(SoldierControllerBuilder.TuningPath));
                var land = temp.layers[0].stateMachine.states.First(s => s.state.name == "Land").state;
                Assert.AreEqual(1f, land.speed);
                var toLoco = land.transitions.First(t => t.destinationState.name == "Locomotion");
                Assert.IsTrue(toLoco.hasExitTime);
                // And the stand-in's land is the idle of the stance (pistol idle for the pistol, rifle idle otherwise), leaving by the Airborne condition
                foreach (var name in new[] { "Land", "Fall" })
                {
                    var held = Controller.layers[0].stateMachine.states.First(s => s.state.name == name).state;
                    Assert.AreEqual(1f, held.speed, name);
                    var idle = held.motion as BlendTree;
                    Assert.IsNotNull(idle, name + " plays a blend over the stance");
                    Assert.AreEqual("Stance", idle.blendParameter);
                    Assert.AreEqual(3, idle.children.Length);
                    var map0 = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapBuilder.AssetPath);
                    Assert.AreSame(map0.Get("soldier_loco_aim_idle"), idle.children[0].motion, "rifle stance: the rifle idle");
                    Assert.AreSame(map0.Get("body_pistol_idle"), idle.children[1].motion, "pistol stance: the pistol idle");
                    Assert.AreSame(map0.Get("soldier_loco_aim_idle"), idle.children[2].motion, "heavy stance: the rifle idle (no heavy idle yet)");
                }
                var heldLand = Controller.layers[0].stateMachine.states.First(s => s.state.name == "Land").state;
                Assert.IsFalse(heldLand.transitions.First(t => t.destinationState.name == "Locomotion").hasExitTime);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                Object.DestroyImmediate(own);
            }
        }

        // ---------------------------------------------------------------- Clips on the body

        public static IEnumerable<string> CatalogClips() => SoldierClips.All.Select(e => e.Name);

        [TestCaseSource(nameof(CatalogClips))]
        public void ClipBindingsAllResolveOnTheSoldier(string name)
        {
            var rig = NeedRig();
            try
            {
                var clip = SoldierClipAssets.Load(name);
                var unresolved = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct()
                    .Where(p => p.Length > 0 && rig.transform.Find(p) == null).ToList();
                Assert.IsEmpty(unresolved, $"{name}: {unresolved.Count} paths do not resolve on the soldier: {string.Join(", ", unresolved.Take(5))}");
            }
            finally { LocomotionClipSpeeds.Cleanup(rig); }
        }

        [Test]
        public void EveryClipMapSlotHasAClipAndTheStandInsAreTheRifleSet()
        {
            var map = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapBuilder.AssetPath);
            Assert.IsTrue(map.Slots.All(s => s.clip != null));
            foreach (var g in SoldierClips.Gaps)
                Assert.IsTrue(map.IsStandIn(g.Name) || System.IO.File.Exists(SoldierClips.MixamoFolder + g.Name + ".fbx"), g.Name);
        }

        // ---------------------------------------------------------------- The animator on a body

        static SoldierAnimator Body(GameObject rig)
        {
            var a = SoldierAnimator.Attach(rig);
            Assert.IsNotNull(a.Body.runtimeAnimatorController, "the controller is set");
            return a;
        }

        [Test]
        public void WalkingWritesTheTreeAndAPlaybackThatMatchesTheFeet()
        {
            var rig = NeedRig();
            try
            {
                var a = Body(rig);
                a.SetMove(new Vector2(0, 4.4f));
                for (var i = 0; i < 60; i++) a.Step(0.05f);
                Assert.AreEqual(1f, a.Body.GetFloat("MoveY"), 0.01f);
                Assert.AreEqual(0f, a.Body.GetFloat("MoveX"), 0.01f);
                Assert.AreEqual(a.Playback, a.Body.GetFloat("Speed"), 1e-4f);
                a.SetStance(LocomotionClips.Stance.Heavy);
                for (var i = 0; i < 60; i++) a.Step(0.05f);
                Assert.AreEqual(2f, a.Body.GetFloat("Stance"), 0.01f);
            }
            finally { LocomotionClipSpeeds.Cleanup(rig); }
        }

        [Test]
        public void ShotsOfAnAutomaticWeaponLoopTheBurstAndSingleShotsDoNot()
        {
            var rig = NeedRig();
            try
            {
                var a = Body(rig);
                a.Fire(true);
                a.Fire(true);
                Assert.IsTrue(a.FiringAutomatic, "the second shot of an automatic weapon is a burst");
                Assert.IsTrue(a.Body.GetBool("FireAuto"));
                var b = NeedRig();
                try
                {
                    var single = Body(b);
                    single.Fire(false);
                    Assert.IsFalse(single.FiringAutomatic);
                    Assert.IsFalse(single.Body.GetBool("FireAuto"));
                }
                finally { LocomotionClipSpeeds.Cleanup(b); }
            }
            finally { LocomotionClipSpeeds.Cleanup(rig); }
        }

        [Test]
        public void DiedStopsTheBodyHoldsThePoseAndRaisesTheHookWithThePush()
        {
            var rig = NeedRig();
            try
            {
                var a = Body(rig);
                a.SetMove(new Vector2(0, 4.4f));
                a.Step(0.1f);
                Vector3? pushed = null;
                a.WasKilled += (_, d) => pushed = d;
                a.Died(new Vector3(3f, 5f, 0f));
                Assert.IsTrue(a.IsDead);
                Assert.IsFalse(a.Body.enabled, "the Animator is off: the pose holds");
                Assert.IsTrue(pushed.HasValue);
                Assert.AreEqual(new Vector3(1f, 0f, 0f), pushed.Value, "flat and unit");
                Assert.AreEqual(Vector2.zero, a.TreeTarget);
                a.Fire();
                a.Jump();
                Assert.IsFalse(a.FiringAutomatic);
                a.Revive();
                Assert.IsFalse(a.IsDead);
                Assert.IsTrue(a.Body.enabled);
                Assert.AreEqual(Vector3.zero, a.DeathDirection);
            }
            finally { LocomotionClipSpeeds.Cleanup(rig); }
        }
    }
}
