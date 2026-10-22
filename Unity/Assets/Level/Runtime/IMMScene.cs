using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// What the game needs to know about a battle scene (Meadow, Chessboard, Fungal), on the scene's SceneInfo object. The level builder
    /// (SdfLevel.Editor.LevelBuilder) adds it and fills it from the level's bake, level.json, on every rebuild, so it is never edited by
    /// hand. Everything is in Unity's coordinates: the pipeline's x is mirrored, as for the rest of the level.
    /// </summary>
    public sealed class IMMScene : MonoBehaviour
    {
        public const string ObjectName = "SceneInfo";

        /// <summary>A placed model (castle, big rock, board-game piece; level.json "placements") and its instance in the scene.</summary>
        [Serializable]
        public struct Placement
        {
            public string model;
            public Transform transform;
        }

        /// <summary>
        /// One team's half of the scene. The scenes are roughly mirrored (the meadow's two castles), so each team has a side of its own;
        /// <see cref="elements"/> are its GameObjects that matter to the game: the level builder puts the side's spawn, flag stand,
        /// weapon pods and health pods in it (ticket 17; the <see cref="SdfBattle.BattleSide"/> on Battle/West or Battle/East holds them
        /// by role).
        /// </summary>
        [Serializable]
        public sealed class BattleSide
        {
            public string name;
            public List<GameObject> elements = new List<GameObject>();
        }

        /// <summary>How many teams, and so battle sides, a scene has.</summary>
        public const int SideCount = 2;

        [Tooltip("The level's name, as level.json gives it (meadow, chessboard, fungal)")]
        public string levelName;
        [Tooltip("A closed room (the chessboard cube) rather than an island under a sky")]
        public bool indoor;

        [Tooltip("The playable rectangle's lowest corner, x and z, in metres; the walls stand just outside it")]
        public Vector2 playAreaMin;
        [Tooltip("The playable rectangle's highest corner, x and z, in metres")]
        public Vector2 playAreaMax;
        [Tooltip("The whole ground's lowest corner, x and z, hills and island edge included")]
        public Vector2 extentMin;
        [Tooltip("The whole ground's highest corner, x and z")]
        public Vector2 extentMax;

        [Tooltip("Where the player starts, on the ground")]
        public Vector3 spawnPoint;
        public Quaternion spawnRotation = Quaternion.identity;
        [Tooltip("The camera's height above the ground, in metres")]
        public float eyeHeight;

        public Transform player;
        public Camera playerCamera;
        [Tooltip("The root of the generated level: ground, walls or room, placed models, sky")]
        public Transform levelRoot;
        public Placement[] placements = Array.Empty<Placement>();
        [Tooltip("One side per team, indexed by team: 0 the west side, 1 the east; its elements are the side's spawn, flag stand and pods")]
        public BattleSide[] battleSides = NewBattleSides();
        [Tooltip("The level's Battle object: both sides' spawns, flag stands and pods, and the neutral pods")]
        public SdfBattle.BattleArena battle;

        /// <summary>The scene info of the active battle scene; null where there is none.</summary>
        public static IMMScene Current { get; private set; }

        public Vector2 PlayAreaSize => playAreaMax - playAreaMin;

        public Vector2 PlayAreaCentre => (playAreaMin + playAreaMax) / 2;

        /// <summary>Whether a point lies inside the playable rectangle, seen from above.</summary>
        public bool InPlayArea(Vector3 point) =>
            point.x >= playAreaMin.x && point.x <= playAreaMax.x && point.z >= playAreaMin.y && point.z <= playAreaMax.y;

        /// <summary>Two empty sides, Side 1 and Side 2.</summary>
        public static BattleSide[] NewBattleSides()
        {
            var sides = new BattleSide[SideCount];
            for (var i = 0; i < SideCount; i++)
                sides[i] = new BattleSide { name = $"Side {i + 1}" };
            return sides;
        }

        private void OnValidate()
        {
            // Always one side per team
            if (battleSides == null || battleSides.Length != SideCount)
            {
                var sides = NewBattleSides();
                for (var i = 0; i < Mathf.Min(SideCount, battleSides?.Length ?? 0); i++)
                    sides[i] = battleSides[i] ?? sides[i];
                battleSides = sides;
            }
        }

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }
    }
}
