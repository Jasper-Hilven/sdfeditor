using System;
using System.Collections.Generic;
using SdfBattle;
using SdfLevel;
using SdfPickups;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// What a <see cref="BeatPlayer"/> needs of the scene it plays in (ticket 32.7), so the player does not care whether it is a battle
    /// scene or another one: the camera to move, where each <see cref="Anchor"/> is and which way it faces, the scouts to put on the stage,
    /// and the things to hide meanwhile (the real players' bodies, your weapon in the camera's corner). <see cref="Of"/> builds it for a
    /// battle scene from its match; the ending (32.8) builds its own for another scene.
    /// </summary>
    public sealed class BeatWorld
    {
        public Camera camera;
        /// <summary>The position on the ground and the yaw (degrees) an anchor faces.</summary>
        public Func<Anchor, (Vector3 position, float yaw)> frame;
        /// <summary>The scout prefab of a team; null for none (the shot is then framed on the place alone).</summary>
        public Func<Team, GameObject> scout;
        /// <summary>Switched off while the beat plays and on again after (those that were on).</summary>
        public readonly List<GameObject> hide = new List<GameObject>();

        /// <summary>The world of a battle scene: red is the west side, blue the east, and the camera is yours.</summary>
        public static BeatWorld Of(Match match)
        {
            var arena = match.Arena;
            var world = new BeatWorld
            {
                camera = match.You != null ? match.You.GetComponentInChildren<Camera>() : Camera.main,
                scout = match.ScoutOf,
            };
            world.frame = anchor =>
            {
                var red = arena.Side(BattleSideId.West);
                var blue = arena.Side(BattleSideId.East);
                var redYaw = red.spawn.eulerAngles.y;
                var blueYaw = blue.spawn.eulerAngles.y;
                switch (anchor)
                {
                    case Anchor.BlueSpawn:
                        return (blue.spawn.position, blueYaw);
                    case Anchor.Centre:
                        return ((red.spawn.position + blue.spawn.position) * 0.5f, redYaw);
                    case Anchor.RedFlag:
                        return (red.flagStand != null ? red.flagStand.transform.position : red.spawn.position, redYaw);
                    case Anchor.BlueFlag:
                        return (blue.flagStand != null ? blue.flagStand.transform.position : blue.spawn.position, blueYaw);
                    default:
                        return (red.spawn.position, redYaw);
                }
            };
            foreach (var player in match.Players)
                if (player.body != null)
                    world.hide.Add(player.body.gameObject);
            // Your weapon sits in the camera's corner
            if (world.camera != null)
            {
                var weapons = world.camera.transform.Find("Weapons");
                if (weapons != null)
                    world.hide.Add(weapons.gameObject);
            }
            return world;
        }
    }
}
