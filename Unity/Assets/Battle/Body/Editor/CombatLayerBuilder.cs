using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Names = SdfBody.SoldierAnimator.Names;

namespace SdfBody.Editor
{
    /// <summary>
    /// The <c>UpperBody</c> layer (Order 100): weight 1, override, masked by <c>UpperBody.mask</c> (<c>Spine_01</c> and
    /// everything under it: the spine, both arms and the weapon hand, the neck and head; the hips and legs keep 05's locomotion).
    /// <list type="bullet">
    /// <item><c>Empty</c> (default, no motion): the locomotion's rifle-up arms show through (the aim idle is the aim; decision 26).</item>
    /// <item><c>Fire</c>: <c>soldier_fire_rifle</c> (one shot, 0.25 s) from its first frame on every <see cref="Names.Fire"/> while the fire
    /// is not automatic (any state, to itself too: a pistol or a sniper restarts it each shot), then back to <c>Empty</c>.</item>
    /// <item><c>FireBurst</c>: <c>soldier_fire_rifle_burst</c> (1.17 s, looped) while <see cref="Names.FireAuto"/> holds
    /// (shots closer than <see cref="CombatTuning.BurstInterval"/>: 12 shots/s reads as a burst, not as a 0.25 s kick restarted every
    /// 0.08 s).</item>
    /// <item><c>Reload</c>: <c>soldier_reload</c> on <see cref="Names.Reload"/> (any state), its speed <see cref="Names.ReloadSpeed"/>
    /// so it lasts the weapon's reload seconds.</item>
    /// <item><c>Hit</c>: the flinch on <see cref="Names.Hit"/> from <c>Empty</c>, <c>Fire</c>, <c>FireBurst</c> and itself (not from
    /// <c>Reload</c>: a flinch does not interrupt a reload); a 1D tree over <see cref="Names.HitX"/> between the clip and its mirror.</item>
    /// </list>
    /// Also makes the mask and 06's tuning asset when missing, and sets the tuning's reload clip length.
    /// </summary>
    public sealed class CombatLayerBuilder : ISoldierLayerBuilder
    {
        public const string MaskPath = "Assets/Battle/Body/Avatar/UpperBody.mask";
        public const string TuningPath = SoldierControllerBuilder.Folder + "Resources/SoldierCombat.asset";
        /// <summary>The first bone of the mask: it and everything under it.</summary>
        public const string MaskRootPath = "Root/Hips/Spine_01";
        /// <summary>
        /// Where the shot of <c>soldier_hit</c> comes from, as the HitX of its tree: the clip's chest swings 3 cm to the body's right at
        /// its peak (measured by <c>CombatControllerTests</c>), i.e. the hit came from the left (−1); its mirror sits at +1, a hit from
        /// the front or back plays half of each (a symmetric flinch).
        /// </summary>
        public const float HitClipFrom = -1f;

        public int Order => 100;

        public void Build(SoldierControllerContext ctx)
        {
            var tuning = EnsureTuning();
            var c = ctx.Controller;
            ctx.Parameter(Names.FireAuto, AnimatorControllerParameterType.Bool);
            ctx.Parameter(Names.ReloadSpeed, AnimatorControllerParameterType.Float, 1f);

            var reloadClip = ctx.Clip("soldier_reload");
            if (!Mathf.Approximately(tuning.ReloadClipSeconds, reloadClip.length))
            {
                tuning.ReloadClipSeconds = reloadClip.length;
                EditorUtility.SetDirty(tuning);
                AssetDatabase.SaveAssetIfDirty(tuning);
            }

            var sm = AddLayer(c, Names.UpperBodyLayer, EnsureMask());
            var empty = sm.AddState(Names.EmptyState, new Vector3(300, 0, 0));
            sm.defaultState = empty;

            var fireClip = ctx.Clip("soldier_fire_rifle");
            var fire = sm.AddState(Names.FireState, new Vector3(600, -120, 0));
            fire.motion = fireClip;
            var burst = sm.AddState(Names.BurstState, new Vector3(600, 0, 0));
            burst.motion = ctx.Clip("soldier_fire_rifle_burst");
            var reload = sm.AddState(Names.ReloadState, new Vector3(600, 120, 0));
            reload.motion = reloadClip;
            reload.speedParameter = Names.ReloadSpeed;
            reload.speedParameterActive = true;
            var hit = sm.AddState(Names.HitState, new Vector3(600, 240, 0));
            hit.motion = HitTree(ctx);

            // Fire: a single shot from its first frame on every call; back to the aim once it has played.
            var anyFire = Any(sm, fire, tuning.FireFadeSeconds, true);
            anyFire.AddCondition(AnimatorConditionMode.If, 0f, Names.Fire);
            anyFire.AddCondition(AnimatorConditionMode.IfNot, 0f, Names.FireAuto);
            Transition(fire, empty, tuning.FireReturnSeconds, ExitTime(fireClip.length, tuning.FireReturnSeconds));
            // Automatic: the burst loops while FireAuto holds.
            Transition(empty, burst, tuning.BurstFadeSeconds, null, (AnimatorConditionMode.If, Names.FireAuto));
            Transition(fire, burst, tuning.BurstFadeSeconds, null, (AnimatorConditionMode.If, Names.FireAuto));
            Transition(burst, empty, tuning.BurstFadeSeconds, null, (AnimatorConditionMode.IfNot, Names.FireAuto));
            // Reload: from anything, stretched; back to the aim at its end.
            var anyReload = Any(sm, reload, tuning.ReloadFadeSeconds, true);
            anyReload.AddCondition(AnimatorConditionMode.If, 0f, Names.Reload);
            Transition(reload, empty, tuning.ReloadFadeSeconds, 0.94f);
            // Hit: interrupts the aim, a shot, the burst and itself; never the reload.
            foreach (var from in new[] { empty, fire, burst, hit })
                Transition(from, hit, tuning.HitFadeSeconds, null, (AnimatorConditionMode.If, Names.Hit));
            Transition(hit, burst, tuning.BurstFadeSeconds, 0.6f, (AnimatorConditionMode.If, Names.FireAuto));
            Transition(hit, empty, tuning.HitReturnSeconds, 0.85f);
        }

        /// <summary>The flinch: a 1D tree over HitX, the clip at <see cref="HitClipFrom"/>, its mirror opposite.</summary>
        static BlendTree HitTree(SoldierControllerContext ctx)
        {
            var tree = new BlendTree
            {
                name = "Hit blend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = Names.HitX,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, ctx.Controller);
            var clip = ctx.Clip("soldier_hit");
            var mirror = CombatClipMirror.Ensure(CombatRules.MirroredHitClip, ctx.Clip);
            if (HitClipFrom < 0f)
            {
                tree.AddChild(clip, -1f);
                tree.AddChild(mirror, 1f);
            }
            else
            {
                tree.AddChild(mirror, -1f);
                tree.AddChild(clip, 1f);
            }
            return tree;
        }

        /// <summary>The exit time that ends a fade of <paramref name="fade"/> seconds at the end of a clip of <paramref name="length"/>.</summary>
        static float ExitTime(float length, float fade) => length > 0f ? Mathf.Clamp(1f - fade / length, 0.5f, 1f) : 1f;

        /// <summary>Adds a layer (weight 1, override) and returns its state machine.</summary>
        internal static AnimatorStateMachine AddLayer(AnimatorController c, string name, AvatarMask mask)
        {
            c.AddLayer(name);
            var layers = c.layers;
            var i = layers.Length - 1;
            layers[i].defaultWeight = 1f;
            layers[i].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[i].avatarMask = mask;
            layers[i].stateMachine.name = name;
            c.layers = layers;
            return c.layers[i].stateMachine;
        }

        internal static AnimatorStateTransition Any(AnimatorStateMachine sm, AnimatorState to, float seconds, bool toSelf)
        {
            var tr = sm.AddAnyStateTransition(to);
            tr.hasFixedDuration = true;
            tr.duration = seconds;
            tr.hasExitTime = false;
            tr.canTransitionToSelf = toSelf;
            tr.interruptionSource = TransitionInterruptionSource.None;
            return tr;
        }

        internal static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float seconds, float? exitTime,
            params (AnimatorConditionMode Mode, string Param)[] conditions)
        {
            var tr = from.AddTransition(to);
            tr.hasFixedDuration = true;
            tr.duration = seconds;
            tr.hasExitTime = exitTime.HasValue;
            tr.exitTime = exitTime ?? 0f;
            tr.interruptionSource = TransitionInterruptionSource.None;
            foreach (var (mode, param) in conditions)
                tr.AddCondition(mode, 0f, param);
            return tr;
        }

        // ---------------------------------------------------------------- The tuning and the mask

        /// <summary>06's tuning asset, made with the code defaults when missing.</summary>
        public static CombatTuning EnsureTuning()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<CombatTuning>(TuningPath);
            if (tuning != null) return tuning;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(TuningPath)));
            tuning = ScriptableObject.CreateInstance<CombatTuning>();
            AssetDatabase.CreateAsset(tuning, TuningPath);
            AssetDatabase.SaveAssets();
            return tuning;
        }

        /// <summary>Whether a bone path is in the upper body: <c>Spine_01</c> and everything under it.</summary>
        public static bool InUpperBody(string path) => path == MaskRootPath || path.StartsWith(MaskRootPath + "/", System.StringComparison.Ordinal);

        /// <summary>
        /// <c>UpperBody.mask</c>: every bone of the Synty skeleton by path (the clips' binding paths), active from <c>Spine_01</c> down.
        /// Written only when it is missing or differs (the file stays byte for byte on a rebuild).
        /// </summary>
        public static AvatarMask EnsureMask()
        {
            var bones = CombatSkeleton.Load().Bones;
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask != null && MaskMatches(mask)) return mask;
            var fresh = mask ?? new AvatarMask();
            fresh.name = "UpperBody";
            fresh.transformCount = bones.Count;
            for (var i = 0; i < bones.Count; i++)
            {
                fresh.SetTransformPath(i, bones[i].Path);
                fresh.SetTransformActive(i, InUpperBody(bones[i].Path));
            }
            // Generic: the humanoid body parts do not apply; leave them all on (Unity's default).
            if (mask == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(MaskPath)));
                AssetDatabase.CreateAsset(fresh, MaskPath);
            }
            else EditorUtility.SetDirty(fresh);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        }

        public static bool MaskMatches(AvatarMask mask)
        {
            var bones = CombatSkeleton.Load().Bones;
            if (mask.transformCount != bones.Count) return false;
            for (var i = 0; i < bones.Count; i++)
                if (mask.GetTransformPath(i) != bones[i].Path || mask.GetTransformActive(i) != InUpperBody(bones[i].Path))
                    return false;
            return true;
        }
    }
}
