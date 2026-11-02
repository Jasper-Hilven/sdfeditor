using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SdfBody;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Names = SdfBody.SoldierAnimator.Names;

namespace SdfBody.Editor
{
    /// <summary>
    /// Adds layers to <c>Soldier.controller</c> after the base layer: the upper body layer implements this in its own editor file
    /// (any assembly that references this one); <see cref="SoldierControllerBuilder.Build"/> finds every implementation by type, runs them
    /// in <see cref="Order"/>, and rebuilds the whole controller each time, so a layer is code, never a hand edit.
    /// </summary>
    public interface ISoldierLayerBuilder
    {
        /// <summary>Smaller first; the base layer is always layer 0. The upper body is 100.</summary>
        int Order { get; }

        /// <summary>Adds its layers, states and parameters (<see cref="SoldierControllerContext.Parameter"/> is idempotent).</summary>
        void Build(SoldierControllerContext context);
    }

    /// <summary>What a layer builder gets: the controller being built, the clip map, the tuning, and idempotent helpers.</summary>
    public sealed class SoldierControllerContext
    {
        public AnimatorController Controller { get; }
        public SoldierClipMap Map { get; }
        public LocomotionTuning Tuning { get; }

        internal SoldierControllerContext(AnimatorController controller, SoldierClipMap map, LocomotionTuning tuning)
        {
            Controller = controller;
            Map = map;
            Tuning = tuning;
        }

        /// <summary>The map's slot for a convention name (its own clip or its stand-in); throws when the map lacks it.</summary>
        public SoldierClipMap.Slot Slot(string name)
        {
            if (!Map.TryGet(name, out var slot) || slot.clip == null)
                throw new InvalidOperationException($"The clip map has no clip for {name} (run AnimationImportFixer.Fix)");
            return slot;
        }

        /// <summary>The clip that plays for a convention name.</summary>
        public AnimationClip Clip(string name) => Slot(name).clip;

        /// <summary>Adds a parameter unless one of that name exists (then checks the type).</summary>
        public void Parameter(string name, AnimatorControllerParameterType type, float defaultValue = 0f)
        {
            foreach (var p in Controller.parameters)
                if (p.name == name)
                {
                    if (p.type != type) throw new InvalidOperationException($"Parameter {name} is {p.type}, asked for {type}");
                    return;
                }
            var param = new AnimatorControllerParameter { name = name, type = type };
            switch (type)
            {
                case AnimatorControllerParameterType.Float: param.defaultFloat = defaultValue; break;
                case AnimatorControllerParameterType.Int: param.defaultInt = (int)defaultValue; break;
                case AnimatorControllerParameterType.Bool: param.defaultBool = defaultValue != 0f; break;
            }
            Controller.AddParameter(param);
        }

        /// <summary>A state playing a map slot: a held stand-in (slot.holdAt at or above 0) is frozen at that normalized time.</summary>
        public AnimatorState SlotState(AnimatorStateMachine machine, string stateName, string clipName, Vector3 position)
        {
            var slot = Slot(clipName);
            var state = machine.AddState(stateName, position);
            state.motion = slot.clip;
            if (slot.holdAt >= 0f)
            {
                state.speed = 0f;
                state.cycleOffset = slot.holdAt;
            }
            return state;
        }
    }

    /// <summary>
    /// Builds <c>Assets/Battle/Body/Animator/Soldier.controller</c> (ticket 05) from 03's clip map and 05's tuning, then runs every
    /// <see cref="ISoldierLayerBuilder"/> (06). Re-runnable and idempotent: the controller is built aside, compared by
    /// <see cref="Describe"/> with the committed one, and written over it (same GUID) only when something changed; a second run changes
    /// nothing. Also makes <c>Resources/SoldierAnimation.asset</c> (the tuning) when missing and points it at the controller.
    /// <code>
    /// $U -batchmode -nographics -quit -logFile - -projectPath $P -executeMethod SdfBody.Editor.SoldierControllerBuilder.BuildFromCommandLine
    /// </code>
    /// </summary>
    public static class SoldierControllerBuilder
    {
        public const string Folder = "Assets/Battle/Body/Animator/";
        public const string ControllerPath = Folder + "Soldier.controller";
        public const string TuningPath = Folder + "Resources/SoldierAnimation.asset";
        public const string ClipMapPath = "Assets/Battle/Body/Animations/SoldierClipMap.asset";
        const string ScratchPath = Folder + "Soldier.build.controller";

        /// <summary>The emote states the base layer has: the dances, and the roar and the victory once their own clips are in (while a stand-in plays for them, the state is left out).</summary>
        public static IEnumerable<string> EmoteNames(SoldierClipMap map) =>
            map.Slots.Where(s => s.name.StartsWith("soldier_emote_", StringComparison.Ordinal) || ((s.name == "body_roar" || s.name == "body_victory") && string.IsNullOrEmpty(s.standIn)))
                .Select(s => s.name).OrderBy(n => n, StringComparer.Ordinal);

        /// <summary>Rebuild everything (the Rebuild step and the menu). True when the controller changed.</summary>
        [MenuItem("Sdf/Body/Build Soldier Controller")]
        public static void Build() => BuildController();

        /// <summary>As <see cref="Build"/>; returns whether the controller file changed.</summary>
        public static bool BuildController()
        {
            var map = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(ClipMapPath);
            if (map == null) throw new InvalidOperationException($"No clip map at {ClipMapPath} (ticket 03)");
            var tuning = EnsureTuning();

            AssetDatabase.DeleteAsset(ScratchPath);
            var scratch = AnimatorController.CreateAnimatorControllerAtPath(ScratchPath);
            bool changed;
            try
            {
                Fill(scratch, map, tuning);
                EditorUtility.SetDirty(scratch);
                AssetDatabase.SaveAssetIfDirty(scratch);

                var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                changed = existing == null || Describe(existing) != Describe(scratch);
                if (changed)
                {
                    if (existing == null)
                    {
                        AssetDatabase.CopyAsset(ScratchPath, ControllerPath);
                    }
                    else
                    {
                        // Keep the committed GUID (the tuning and any prefab point at it): copy the bytes, keep the .meta.
                        File.Copy(Path.GetFullPath(ScratchPath), Path.GetFullPath(ControllerPath), true);
                    }
                    AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(ScratchPath);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (tuning.Controller != controller)
            {
                tuning.Controller = controller;
                EditorUtility.SetDirty(tuning);
                AssetDatabase.SaveAssetIfDirty(tuning);
            }
            Debug.Log(changed ? $"[SoldierController] built {ControllerPath}" : $"[SoldierController] {ControllerPath} is current: 0 changes");
            return changed;
        }

        /// <summary>Batch entry: exit 0 on success, 1 on an error.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                BuildController();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[SoldierController] failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>The tuning asset, made with the code defaults when missing.</summary>
        public static LocomotionTuning EnsureTuning()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<LocomotionTuning>(TuningPath);
            if (tuning != null) return tuning;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(TuningPath)));
            tuning = ScriptableObject.CreateInstance<LocomotionTuning>();
            AssetDatabase.CreateAsset(tuning, TuningPath);
            AssetDatabase.SaveAssets();
            return tuning;
        }

        /// <summary>Fills an empty controller (asset-backed) with 05's base layer and every layer builder's layers.</summary>
        public static void Fill(AnimatorController controller, SoldierClipMap map, LocomotionTuning tuning)
        {
            var ctx = new SoldierControllerContext(controller, map, tuning);
            Parameters(ctx);
            BaseLayer(ctx);
            foreach (var builder in LayerBuilders())
                builder.Build(ctx);
        }

        /// <summary>Every <see cref="ISoldierLayerBuilder"/> in the editor's assemblies, by order then type name.</summary>
        public static List<ISoldierLayerBuilder> LayerBuilders() =>
            TypeCache.GetTypesDerivedFrom<ISoldierLayerBuilder>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (ISoldierLayerBuilder)Activator.CreateInstance(t))
                .OrderBy(b => b.Order).ThenBy(b => b.GetType().FullName, StringComparer.Ordinal)
                .ToList();

        static void Parameters(SoldierControllerContext ctx)
        {
            const AnimatorControllerParameterType F = AnimatorControllerParameterType.Float, B = AnimatorControllerParameterType.Bool, T = AnimatorControllerParameterType.Trigger;
            ctx.Parameter(Names.MoveX, F);
            ctx.Parameter(Names.MoveY, F);
            ctx.Parameter(Names.Speed, F, 1f);
            ctx.Parameter(Names.Stance, F);
            ctx.Parameter(Names.Jump, T);
            ctx.Parameter(Names.Airborne, B);
            ctx.Parameter(Names.Dead, B);
            // The upper body layer's triggers (CombatLayerBuilder adds FireAuto and ReloadSpeed)
            ctx.Parameter(Names.Fire, T);
            ctx.Parameter(Names.Reload, T);
            ctx.Parameter(Names.Hit, T);
            ctx.Parameter(Names.HitX, F);
        }

        static void BaseLayer(SoldierControllerContext ctx)
        {
            var c = ctx.Controller;
            var t = ctx.Tuning;
            // CreateAnimatorControllerAtPath made "Base Layer": rename it.
            var layers = c.layers;
            layers[0].name = Names.BaseLayer;
            layers[0].stateMachine.name = Names.BaseLayer;
            c.layers = layers;
            var sm = c.layers[0].stateMachine;

            // Locomotion: a 1D blend over Stance between the rifle, pistol and heavy 2D trees; its speed is the Speed parameter.
            var loco = sm.AddState(Names.LocomotionState, new Vector3(300, 0, 0));
            var root = new BlendTree
            {
                name = "Stance blend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = Names.Stance,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(root, c);
            loco.motion = root;
            loco.speedParameter = Names.Speed;
            loco.speedParameterActive = true;
            var stanceNames = new[] { "Rifle", "Pistol", "Heavy" };
            for (var i = 0; i < LocomotionClips.Trees.Length; i++)
                Directional(root.CreateBlendTreeChild(i), stanceNames[i], ctx, LocomotionClips.Trees[i], t);
            sm.defaultState = loco;

            // The air: JumpUp (the take-off), Fall (a loop while Airborne) and Land. Until Mixamo's clips are in, the rifle jump stands in
            // for all three (the fall and the landing as poses of it: the stand-in states are held, so they leave by condition only).
            var up = ctx.SlotState(sm, Names.JumpState, "body_jump_up", new Vector3(600, -120, 0));
            var fall = AirState(ctx, sm, Names.FallState, "body_fall_loop", new Vector3(900, -120, 0));
            var land = AirState(ctx, sm, Names.LandState, "body_land", new Vector3(900, 60, 0));
            // A stand-in landing (a held pose or the idle) never ends by itself: it leaves by the condition.
            var landHeld = ctx.Slot("body_land").holdAt >= 0f || ctx.Map.IsStandIn("body_land");
            var upLength = ctx.Clip("body_jump_up").length;

            Transition(loco, up, 0.08f, null, (AnimatorConditionMode.If, Names.Jump, 0f));
            // Walking off an edge: straight into the fall
            Transition(loco, fall, 0.2f, null, (AnimatorConditionMode.If, Names.Airborne, 0f));
            // The take-off ends: still in the air, the fall; already down, the landing
            Transition(up, fall, 0.15f, ExitFraction(ctx.Slot("body_jump_up"), upLength), (AnimatorConditionMode.If, Names.Airborne, 0f));
            Transition(up, land, 0.1f, ExitFraction(ctx.Slot("body_jump_up"), upLength), (AnimatorConditionMode.IfNot, Names.Airborne, 0f));
            Transition(fall, land, 0.08f, null, (AnimatorConditionMode.IfNot, Names.Airborne, 0f));
            // The landing is over: back to the locomotion (a held stand-in pose leaves at once, after a short fade)
            if (landHeld)
                Transition(land, loco, 0.18f, null, (AnimatorConditionMode.IfNot, Names.Airborne, 0f));
            else
                Transition(land, loco, 0.15f, 0.8f);
            // Jumping again at once
            Transition(land, up, 0.05f, null, (AnimatorConditionMode.If, Names.Jump, 0f));
            Transition(land, fall, 0.1f, null, (AnimatorConditionMode.If, Names.Airborne, 0f));

            // Emotes: one state per emote slot, tagged; SoldierAnimator cross-fades in and out. The one-shots go back by themselves.
            var emotes = sm.AddStateMachine(Names.EmoteMachine, new Vector3(300, 240, 0));
            var y = 0;
            foreach (var name in EmoteNames(ctx.Map))
            {
                var state = ctx.SlotState(emotes, name, name, new Vector3(300, 60 * y++, 0));
                state.tag = Names.EmoteTag;
                if (!ctx.Slot(name).loop)
                    Transition(state, loco, 0.2f, 0.9f);
            }
        }

        /// <summary>
        /// The fall or landing state. With its own clip, that clip; while a stand-in plays, the idle of the weapon in hand: a 1D blend over
        /// Stance of each stance's idle (the pistol idle for the pistol, the rifle idle otherwise, until a heavy idle exists), looping.
        /// </summary>
        static AnimatorState AirState(SoldierControllerContext ctx, AnimatorStateMachine sm, string stateName, string clipName, Vector3 position)
        {
            var state = ctx.SlotState(sm, stateName, clipName, position);
            if (!ctx.Map.IsStandIn(clipName) || ctx.Slot(clipName).holdAt >= 0f)
                return state;
            var tree = new BlendTree
            {
                name = stateName + " idle",
                blendType = BlendTreeType.Simple1D,
                blendParameter = Names.Stance,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, ctx.Controller);
            for (var i = 0; i < LocomotionClips.Trees.Length; i++)
            {
                var centre = LocomotionClips.Trees[i][0];
                tree.AddChild(LocomotionClips.Resolve(ctx.Map, centre) ?? ctx.Clip(centre.Name), i);
            }
            state.motion = tree;
            state.speed = 1f;
            state.cycleOffset = 0f;
            return state;
        }

        /// <summary>Where the take-off clip hands over to the fall: a stand-in jump at its fall pose, an own clip at its end.</summary>
        static float ExitFraction(SoldierClipMap.Slot slot, float length) =>
            string.IsNullOrEmpty(slot.standIn) ? 0.9f : SoldierClips.JumpFallPose;

        static void Directional(BlendTree tree, string name, SoldierControllerContext ctx, IEnumerable<LocomotionClip> clips, LocomotionTuning t)
        {
            tree.name = name;
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = Names.MoveX;
            tree.blendParameterY = Names.MoveY;
            // Each child's time scale brings its measured foot speed to its ring's speed, so the rings match the ground by themselves;
            // the Speed parameter corrects the rest (the clamped clips, the radii between the rings, the sprint).
            var children = new List<ChildMotion>();
            foreach (var clip in clips)
                children.Add(new ChildMotion
                {
                    motion = LocomotionClips.Resolve(ctx.Map, clip) ?? ctx.Clip(clip.Name),
                    position = clip.Position(t.WalkRadius, t.RunRadius, t.SprintRadius),
                    timeScale = t.TimeScaleOf(clip),
                    directBlendParameter = Names.MoveX,
                });
            tree.children = children.ToArray();
        }

        static void Transition(AnimatorState from, AnimatorState to, float seconds, float? exitTime, params (AnimatorConditionMode Mode, string Param, float Threshold)[] conditions)
        {
            var tr = from.AddTransition(to);
            tr.hasFixedDuration = true;
            tr.duration = seconds;
            tr.hasExitTime = exitTime.HasValue;
            tr.exitTime = exitTime ?? 0f;
            tr.interruptionSource = TransitionInterruptionSource.None;
            foreach (var (mode, param, threshold) in conditions)
                tr.AddCondition(mode, threshold, param);
        }

        // ---------------------------------------------------------------- The description (what "the same controller" means)

        /// <summary>
        /// The controller as text: parameters, layers, state machines, states (motion, speed, tags), blend trees (children, positions,
        /// thresholds) and transitions, in a stable order. Two builds are the same controller when their descriptions are equal.
        /// </summary>
        public static string Describe(AnimatorController c)
        {
            var sb = new StringBuilder();
            foreach (var p in c.parameters)
                sb.Append("param ").Append(p.name).Append(' ').Append(p.type).Append(' ')
                  .Append(F(p.defaultFloat)).Append(' ').Append(p.defaultInt).Append(' ').Append(p.defaultBool).Append('\n');
            foreach (var l in c.layers)
            {
                sb.Append("layer ").Append(l.name).Append(" weight=").Append(F(l.defaultWeight)).Append(" blend=").Append(l.blendingMode)
                  .Append(" mask=").Append(l.avatarMask != null ? l.avatarMask.name : "-").Append(" ik=").Append(l.iKPass)
                  .Append(" sync=").Append(l.syncedLayerIndex).Append('\n');
                if (l.stateMachine != null) Machine(sb, l.stateMachine, "  ");
            }
            return sb.ToString();
        }

        static void Machine(StringBuilder sb, AnimatorStateMachine m, string indent)
        {
            sb.Append(indent).Append("machine ").Append(m.name).Append(" default=").Append(m.defaultState != null ? m.defaultState.name : "-").Append('\n');
            foreach (var cs in m.states.OrderBy(s => s.state.name, StringComparer.Ordinal))
            {
                var s = cs.state;
                sb.Append(indent).Append(" state ").Append(s.name).Append(" tag=").Append(s.tag).Append(" speed=").Append(F(s.speed))
                  .Append(" speedParam=").Append(s.speedParameterActive ? s.speedParameter : "-").Append(" offset=").Append(F(s.cycleOffset))
                  .Append(" wd=").Append(s.writeDefaultValues).Append(" mirror=").Append(s.mirror).Append('\n');
                Motion(sb, s.motion, indent + "   ");
                foreach (var tr in s.transitions) TransitionText(sb, tr, indent + "   ");
            }
            foreach (var tr in m.anyStateTransitions) { sb.Append(indent).Append(" any"); TransitionText(sb, tr, indent + "   "); }
            foreach (var cm in m.stateMachines.OrderBy(x => x.stateMachine.name, StringComparer.Ordinal))
                Machine(sb, cm.stateMachine, indent + "  ");
        }

        static void Motion(StringBuilder sb, Motion motion, string indent)
        {
            if (motion == null) { sb.Append(indent).Append("motion -\n"); return; }
            if (motion is BlendTree tree)
            {
                sb.Append(indent).Append("tree ").Append(tree.name).Append(' ').Append(tree.blendType).Append(' ')
                  .Append(tree.blendParameter).Append(' ').Append(tree.blendParameterY).Append('\n');
                foreach (var child in tree.children)
                {
                    sb.Append(indent).Append(" child t=").Append(F(child.threshold)).Append(" pos=(").Append(F(child.position.x)).Append(',')
                      .Append(F(child.position.y)).Append(") ts=").Append(F(child.timeScale)).Append(" mirror=").Append(child.mirror).Append('\n');
                    Motion(sb, child.motion, indent + "  ");
                }
                return;
            }
            sb.Append(indent).Append("clip ").Append(motion.name).Append(" @").Append(AssetDatabase.GetAssetPath(motion)).Append('\n');
        }

        static void TransitionText(StringBuilder sb, AnimatorStateTransition tr, string indent)
        {
            sb.Append(indent).Append("-> ").Append(tr.destinationState != null ? tr.destinationState.name : tr.isExit ? "exit" : "-")
              .Append(" dur=").Append(F(tr.duration)).Append(" fixed=").Append(tr.hasFixedDuration).Append(" exit=")
              .Append(tr.hasExitTime ? F(tr.exitTime) : "-").Append(" int=").Append(tr.interruptionSource);
            foreach (var cond in tr.conditions)
                sb.Append(" [").Append(cond.parameter).Append(' ').Append(cond.mode).Append(' ').Append(F(cond.threshold)).Append(']');
            sb.Append('\n');
        }

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
