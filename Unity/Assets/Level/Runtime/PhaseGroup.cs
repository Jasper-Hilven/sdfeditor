using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The placed models of the final boss's arena that are there in some phases of the fight only (ticket 33.3; the level file's
    /// <c>phases=1-2</c> on a <c>place</c> line): the builder puts each range under one of these, named Phase 1, Phase 1-2, Phase 2-3,
    /// Phase 3, under Level/Placed, and the boss's arena shows and hides them as the phase changes. The models' colliders go with them,
    /// and the navmesh is baked once per phase layout (<see cref="SdfBattle.Editor.BattleNavmesh"/>). The scene is built in phase 1.
    /// </summary>
    public sealed class PhaseGroup : MonoBehaviour
    {
        /// <summary>The fight's phases: the Royal Box, the Prime-copter and the crown.</summary>
        public const int PhaseCount = 3;

        [Tooltip("The first phase (1 to 3) its models are there in")]
        public int first = 1;
        [Tooltip("The last phase (1 to 3) its models are there in")]
        public int last = 3;

        /// <summary>Whether its models are there in the phase (1 to 3).</summary>
        public bool LiveIn(int phase) => phase >= first && phase <= last;

        /// <summary>Shows the group when it is there in the phase and hides it otherwise.</summary>
        public void Show(int phase) => gameObject.SetActive(LiveIn(phase));

        /// <summary>The name its object has: Phase 3, or Phase 1-2.</summary>
        public static string NameOf(int first, int last) => first == last ? $"Phase {first}" : $"Phase {first}-{last}";

        /// <summary>Shows every phase group under <paramref name="root"/> (inactive ones too) that is there in the phase, hides the others.</summary>
        public static void ShowPhase(Transform root, int phase)
        {
            foreach (var group in root.GetComponentsInChildren<PhaseGroup>(true))
                group.Show(phase);
        }
    }
}
