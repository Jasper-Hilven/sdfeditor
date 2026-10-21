using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The game's own information, on the Game object: an instance of Assets/Game/Game.prefab, the same in every battle scene (Meadow,
    /// Chessboard, Fungal). The prefab is edited by hand; the level builder (SdfLevel.Editor.LevelBuilder) only places an instance next
    /// to SceneInfo and points <see cref="scene"/> at it, the one thing that differs per scene.
    /// </summary>
    public sealed class IMMGame : MonoBehaviour
    {
        public const string ObjectName = "Game";
        public const string PrefabPath = "Assets/Game/Game.prefab";

        [Tooltip("The scene's information (its SceneInfo object); set per scene by the level builder")]
        public IMMScene scene;

        /// <summary>The game of the active battle scene; null where there is none.</summary>
        public static IMMGame Current { get; private set; }

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }
    }
}
