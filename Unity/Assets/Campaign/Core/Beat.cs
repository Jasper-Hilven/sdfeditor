using System;
using System.Collections.Generic;
using SdfBattle;

namespace SdfCampaign
{
    /// <summary>Where in the battle scene a beat's actors and cameras are put: a spawn zone, a flag stand, or the middle between the spawns.</summary>
    public enum Anchor { RedSpawn, BlueSpawn, Centre, RedFlag, BlueFlag }

    /// <summary>
    /// A scout standing in a beat (ticket 32.7): a puppet of the team's scout, put <see cref="right"/> and <see cref="forward"/> metres from
    /// the <see cref="anchor"/> (forward is the way the anchor faces, the field), turned <see cref="facing"/> degrees from that way. The
    /// real players are hidden while a beat plays, so what the shots show is exactly what the data says.
    /// </summary>
    [Serializable]
    public sealed class BeatActor
    {
        public string name;
        public Team team;
        public Anchor anchor;
        public float right;
        public float forward;
        public float facing;

        public BeatActor(string name, Team team, Anchor anchor, float right, float forward, float facing)
        {
            this.name = name;
            this.team = team;
            this.anchor = anchor;
            this.right = right;
            this.forward = forward;
            this.facing = facing;
        }
    }

    /// <summary>
    /// A slow camera move round something: the camera stands <see cref="heightFrom"/> metres up and <see cref="distanceFrom"/> back from the
    /// focus, seen from <see cref="orbitFrom"/> degrees round it (0 behind it, 180 in front of its face), and looks at the focus raised by
    /// <see cref="lookHeight"/>; over the shot each of the three goes from its "from" to its "to" value. The focus is an actor (by name)
    /// or, with no actor, the anchor. Plain numbers, so the same data serves any scene that has two sides.
    /// </summary>
    [Serializable]
    public sealed class CameraMove
    {
        public string actor = "";
        public Anchor anchor;
        public float orbitFrom, orbitTo;
        public float distanceFrom, distanceTo;
        public float heightFrom, heightTo;
        public float lookHeight = 1.4f;

        /// <summary>A move on an actor.</summary>
        public static CameraMove On(string actor, float orbitFrom, float orbitTo, float distanceFrom, float distanceTo, float height = 1.6f, float heightTo = -1, float lookHeight = 1.5f) =>
            new CameraMove
            {
                actor = actor, orbitFrom = orbitFrom, orbitTo = orbitTo, distanceFrom = distanceFrom, distanceTo = distanceTo,
                heightFrom = height, heightTo = heightTo < 0 ? height : heightTo, lookHeight = lookHeight,
            };

        /// <summary>A move on a place: a wide, higher shot.</summary>
        public static CameraMove At(Anchor anchor, float orbitFrom, float orbitTo, float distanceFrom, float distanceTo, float heightFrom, float heightTo, float lookHeight = 3) =>
            new CameraMove
            {
                anchor = anchor, orbitFrom = orbitFrom, orbitTo = orbitTo, distanceFrom = distanceFrom, distanceTo = distanceTo,
                heightFrom = heightFrom, heightTo = heightTo, lookHeight = lookHeight,
            };
    }

    /// <summary>One shot of a beat: a caption line (no line, no caption), who says it, how long it lasts, and the camera move.</summary>
    [Serializable]
    public sealed class BeatShot
    {
        public string speaker;
        public string line;
        public float seconds;
        public CameraMove move;

        public BeatShot(string speaker, string line, float seconds, CameraMove move)
        {
            this.speaker = speaker ?? "";
            this.line = line ?? "";
            this.seconds = seconds;
            this.move = move;
        }
    }

    /// <summary>
    /// A story beat or any short scene told as shots (ticket 32.7): its actors and its shots, data only. <see cref="BeatSequencer"/> runs
    /// the timing and the skip rules; the campaign's beat player puts the actors in the loaded battle scene and moves the camera. The
    /// ending (32.8) is the same kind of thing.
    /// </summary>
    public sealed class Beat
    {
        public readonly string id;
        public readonly string title;
        public readonly BeatActor[] actors;
        public readonly BeatShot[] shots;

        public Beat(string id, string title, BeatActor[] actors, BeatShot[] shots)
        {
            this.id = id;
            this.title = title;
            this.actors = actors;
            this.shots = shots;
        }

        public float Seconds
        {
            get
            {
                var total = 0f;
                foreach (var shot in shots) total += shot.seconds;
                return total;
            }
        }

        public BeatActor Actor(string name)
        {
            foreach (var actor in actors)
                if (actor.name == name) return actor;
            return null;
        }
    }

    /// <summary>The beats of the campaign by step id (ticket 32, section 3). Later beats are added here, one entry each (32.9).</summary>
    public static class Beats
    {
        public const string Prime = "Chanterelle Prime", Ref = "Ref Puffball";

        private static Dictionary<string, Beat> all;

        // A shot that does not move the camera (the ending has no scene behind it)
        private static CameraMove Still => CameraMove.At(Anchor.Centre, 0, 0, 1, 1, 1, 1);

        public static Beat Find(string stepId)
        {
            all ??= Build();
            return all.TryGetValue(stepId, out var beat) ? beat : null;
        }

        public static bool Has(string stepId) => Find(stepId) != null;

        private static Dictionary<string, Beat> Build()
        {
            var you = Cast.You;
            var seven = Cast.Red[1];
            var thirteen = Cast.Red[2];
            var list = new[]
            {
                // B1: the red castle at night; Prime steps out of a pixel pop and changes the rules
                new Beat("beat-fungal", "Nightfall",
                    new[]
                    {
                        new BeatActor(you, Team.Red, Anchor.RedSpawn, 0, 0, 5),
                        new BeatActor(seven, Team.Red, Anchor.RedSpawn, -2.3f, -0.6f, 20),
                        new BeatActor(Prime, Team.Blue, Anchor.RedSpawn, 1.1f, 4.2f, 190),
                    },
                    new[]
                    {
                        new BeatShot("", "Fungal. Nightfall.", 5.5f, CameraMove.At(Anchor.RedSpawn, 175, 150, 20, 15, 6, 4.5f, 3.5f)),
                        new BeatShot(Prime, "Congratulations on winning a meadow. Regulation update: machines.", 6.5f, CameraMove.On(Prime, 170, 200, 3.6f, 3f)),
                        new BeatShot(you, "Machines aren't in the rulebook.", 4.5f, CameraMove.On(you, 160, 185, 3.2f, 2.6f)),
                        new BeatShot(Prime, "They are now. I wrote the rulebook. This morning.", 5.5f, CameraMove.On(Prime, 205, 170, 3f, 3.8f)),
                        new BeatShot(seven, "Fill your spores and press Q. Trust me.", 5f, CameraMove.On(seven, 165, 190, 3f, 2.5f)),
                        new BeatShot("", "", 3f, CameraMove.At(Anchor.RedSpawn, 200, 160, 9, 14, 3.5f, 5.5f, 2)),
                    }),
                // B2: the red back rank of the giant chessboard; 42 knows the board
                new Beat("beat-chessboard", "That's my chess set",
                    new[]
                    {
                        new BeatActor(you, Team.Red, Anchor.RedSpawn, 0, 0, 0),
                        new BeatActor(thirteen, Team.Red, Anchor.RedSpawn, 2.4f, -0.4f, -20),
                        new BeatActor(seven, Team.Red, Anchor.RedSpawn, -2.4f, -0.8f, 15),
                        new BeatActor(Prime, Team.Blue, Anchor.RedSpawn, -0.6f, 5.5f, 175),
                    },
                    new[]
                    {
                        new BeatShot("", "The Chessboard.", 5f, CameraMove.At(Anchor.RedSpawn, 190, 165, 22, 15, 7, 5, 2.5f)),
                        new BeatShot(you, "That's my chess set. That's the coffee ring I made in March.", 6.5f, CameraMove.On(you, 15, -10, 4.2f, 3.4f, 1.7f, 1.8f)),
                        new BeatShot(thirteen, "Is it normal for the tournament to be inside your house?", 5.5f, CameraMove.On(thirteen, 150, 175, 3f, 2.5f)),
                        new BeatShot(Prime, "Board games are a sport now.", 4f, CameraMove.On(Prime, 160, 195, 3.6f, 3f)),
                        new BeatShot(Prime, "The minigun? Banned. Board-game reasons.", 5.5f, CameraMove.On(Prime, 200, 170, 2.6f, 3.4f)),
                        new BeatShot("", "", 3f, CameraMove.At(Anchor.RedSpawn, 200, 160, 8, 13, 3, 5, 2)),
                    }),
                // The ending: no scene to move through (it plays over Boot's backdrop, the GUI's Ending screen), so no actors and a still camera
                new Beat(CampaignDefinition.Ending, "Ending", new BeatActor[0],
                    new[]
                    {
                        new BeatShot("", "Home. A desk. A very small trophy.", 4f, Still),
                        new BeatShot(Ref, "By the power of the actual rulebook: Mushroom 42. Champion. Again.", 6f, Still),
                        new BeatShot(Prime, "Best of three?", 3.5f, Still),
                        new BeatShot(you, "...Bring the minigun.", 4.5f, Still),
                        new BeatShot("", "", 2f, Still),
                    }),
            };
            var map = new Dictionary<string, Beat>();
            foreach (var beat in list) map[beat.id] = beat;
            return map;
        }
    }
}
