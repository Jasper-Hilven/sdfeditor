using System;
using System.Collections.Generic;
using SdfBattle;

namespace SdfCampaign
{
    /// <summary>Where in the battle scene a beat's actors and cameras are put: a spawn zone, a flag stand, or the middle between the spawns.</summary>
    /// <remarks>
    /// <c>Prime</c> and <c>You</c> (ticket 33.8) are the boss fight's: where Chanterelle Prime's body stands now (it moves, and it is
    /// lifted by the copter, so a shot on it follows him) and where you stand; elsewhere they fall back to the red spawn.
    /// </remarks>
    public enum Anchor { RedSpawn, BlueSpawn, Centre, RedFlag, BlueFlag, Prime, You }

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

    /// <summary>
    /// One shot of a beat: a caption line (no line, no caption), who says it, how long it lasts, and the camera move. A <see cref="cue"/>
    /// (ticket 33.8) is a word the scene that plays the beat is told as the shot starts, so the world can change in step with the story
    /// (the balcony falls, Prime shrinks); an empty cue says nothing.
    /// </summary>
    [Serializable]
    public sealed class BeatShot
    {
        public string speaker;
        public string line;
        public float seconds;
        public CameraMove move;
        public string cue;

        public BeatShot(string speaker, string line, float seconds, CameraMove move, string cue = "")
        {
            this.speaker = speaker ?? "";
            this.line = line ?? "";
            this.seconds = seconds;
            this.move = move;
            this.cue = cue ?? "";
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

    /// <summary>The beats of the campaign by step id (ticket 32, section 3). B3-B7 (32.9) are written ahead of their scenes: a beat whose scene is not in the build is skipped
    /// with its battle. A new beat is one more entry in <see cref="Build"/>.</summary>
    public static class Beats
    {
        public const string Prime = "Chanterelle Prime", Ref = "Ref Puffball";

        /// <summary>Where Mushroom 7 stands in the boss victory, from 42: this far to the right and ahead (the scene puts the crown on their cap there).</summary>
        public const float SevenRight = 2.2f, SevenForward = 2f;

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
            var ninetyNine = Cast.Red[3];
            var award = Cast.Blue[4];
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
                // B3: the toy box, red side; the referee announces from the sky, 42 meets an old toy on the other team
                new Beat("beat-toybox", "Off the table",
                    new[]
                    {
                        new BeatActor(you, Team.Red, Anchor.RedSpawn, 0, 0, 0),
                        new BeatActor(seven, Team.Red, Anchor.RedSpawn, -2.3f, -0.6f, 20),
                        new BeatActor(Prime, Team.Blue, Anchor.RedSpawn, 1f, 5f, 180),
                    },
                    new[]
                    {
                        new BeatShot("", "The Toy Box.", 4.5f, CameraMove.At(Anchor.RedSpawn, 190, 160, 20, 14, 6, 4.5f, 2.5f)),
                        new BeatShot(Ref, "Arena four: the toy box. Please do not chew the arena.", 6f, CameraMove.At(Anchor.Centre, 180, 150, 24, 18, 9, 7, 2)),
                        new BeatShot(you, "Sergeant Clank? You were mine.", 5f, CameraMove.On(you, 160, 190, 3.2f, 2.5f, 1.7f)),
                        new BeatShot(Prime, "Your toys work for me now. Better dental.", 5.5f, CameraMove.On(Prime, 165, 200, 3.4f, 2.8f)),
                        new BeatShot(seven, "Is a toy tank allowed to do that?", 4.5f, CameraMove.On(seven, 160, 185, 3f, 2.5f)),
                        new BeatShot("", "", 3f, CameraMove.At(Anchor.RedSpawn, 200, 160, 8, 13, 3, 5, 2)),
                    }),
                // B4: the kitchen; everything is big and hot, and the hob is 42's fault
                new Beat("beat-kitchen", "Down the hall",
                    new[]
                    {
                        new BeatActor(you, Team.Red, Anchor.RedSpawn, 0, 0, 0),
                        new BeatActor(thirteen, Team.Red, Anchor.RedSpawn, 2.4f, -0.4f, -20),
                        new BeatActor(ninetyNine, Team.Red, Anchor.RedSpawn, -2.6f, -0.9f, 15),
                    },
                    new[]
                    {
                        new BeatShot("", "The Kitchen.", 4.5f, CameraMove.At(Anchor.RedSpawn, 185, 160, 21, 15, 7, 5, 2.5f)),
                        new BeatShot(thirteen, "Why is everything so big and so hot?", 5f, CameraMove.On(thirteen, 150, 175, 3f, 2.5f)),
                        new BeatShot(you, "Someone left the hob on. That someone was me.", 6f, CameraMove.On(you, 15, -10, 4f, 3.2f, 1.7f, 1.8f)),
                        new BeatShot(ninetyNine, "Hot.", 3f, CameraMove.On(ninetyNine, 165, 190, 3.2f, 2.4f)),
                        new BeatShot(thirteen, "Nobody touch anything. Especially the toaster.", 5f, CameraMove.On(thirteen, 200, 170, 2.8f, 3.4f)),
                        new BeatShot("", "", 3f, CameraMove.At(Anchor.RedSpawn, 200, 160, 8, 13, 3, 5, 2)),
                    }),
                // B5: the bathroom; Prime brings the fifth blue player
                new Beat("beat-bathroom", "Bath time",
                    new[]
                    {
                        new BeatActor(you, Team.Red, Anchor.RedSpawn, 0, 0, 0),
                        new BeatActor(seven, Team.Red, Anchor.RedSpawn, -2.3f, -0.6f, 20),
                        new BeatActor(Prime, Team.Blue, Anchor.RedSpawn, 1.2f, 5f, 180),
                        new BeatActor(award, Team.Blue, Anchor.RedSpawn, -1f, 5.8f, 170),
                    },
                    new[]
                    {
                        new BeatShot("", "The Bathroom.", 4.5f, CameraMove.At(Anchor.RedSpawn, 190, 165, 20, 14, 6, 4.5f, 2.5f)),
                        new BeatShot(Prime, "You're getting close, champion. Have a bath. Meet my fifth player.", 6.5f, CameraMove.On(Prime, 165, 200, 3.6f, 3f)),
                        new BeatShot(award, "Hi! I got a sticker!", 4f, CameraMove.On(award, 160, 190, 3f, 2.4f)),
                        new BeatShot(seven, "Five against four? That's rigged!", 4.5f, CameraMove.On(seven, 165, 190, 3f, 2.5f)),
                        new BeatShot(Prime, "That's strategy.", 3.5f, CameraMove.On(Prime, 205, 170, 3f, 3.8f)),
                        new BeatShot("", "", 3f, CameraMove.At(Anchor.RedSpawn, 200, 160, 8, 13, 3, 5, 2)),
                    }),
                // B6: inside the computer; the scoreboard in the sky says Prime won, and the referee reads out the timers
                new Beat("beat-computer", "The results file",
                    new[]
                    {
                        new BeatActor(you, Team.Red, Anchor.RedSpawn, 0, 0, 0),
                        new BeatActor(ninetyNine, Team.Red, Anchor.RedSpawn, -2.6f, -0.9f, 15),
                        new BeatActor(seven, Team.Red, Anchor.RedSpawn, 2.3f, -0.6f, -20),
                    },
                    new[]
                    {
                        new BeatShot("", "Inside the computer.", 4.5f, CameraMove.At(Anchor.RedSpawn, 185, 160, 21, 15, 7, 8, 5)),
                        new BeatShot(you, "CHAMPION: CHANTERELLE PRIME. They edited the results file.", 6.5f, CameraMove.On(you, 15, -10, 4f, 3.2f, 1.7f, 2.2f, 2.4f)),
                        new BeatShot(ninetyNine, "Delete it.", 3.5f, CameraMove.On(ninetyNine, 165, 190, 3.2f, 2.4f)),
                        new BeatShot(Ref, "Respawn timers for blue: three seconds. Red: five. That seems... fine?", 7f, CameraMove.At(Anchor.Centre, 180, 150, 24, 18, 9, 7, 2)),
                        new BeatShot(seven, "It does not seem fine.", 3.5f, CameraMove.On(seven, 160, 185, 3f, 2.5f)),
                        new BeatShot("", "", 3f, CameraMove.At(Anchor.RedSpawn, 200, 160, 8, 13, 3, 5, 2)),
                    }),
                // B7: the boss's scene; Prime explains himself in person, from his dais. In the arena he is the real, giant Prime (Anchor.Prime),
                // so he is no puppet of this beat; 42 stands at the red spawn, 100 m from him, and the camera crosses the field
                new Beat("beat-boss", "Second place",
                    new[]
                    {
                        new BeatActor(you, Team.Red, Anchor.RedSpawn, 0, 0, 0),
                    },
                    new[]
                    {
                        new BeatShot("", "The final.", 4.5f, CameraMove.At(Anchor.RedSpawn, 190, 160, 22, 15, 7, 5, 2.5f)),
                        new BeatShot(Prime, "Last year I lost by one capture. One! So I fixed the results. And the game. And your house.", 8f, CameraMove.At(Anchor.Prime, 165, 200, 20, 15, 5, 6.5f, 5.5f)),
                        new BeatShot(you, "You can't fix a house.", 4f, CameraMove.On(you, 160, 190, 3.2f, 2.5f, 1.7f)),
                        new BeatShot(Prime, "I reorganised your cutlery.", 4.5f, CameraMove.At(Anchor.Prime, 205, 170, 15, 20, 6, 5, 5.5f)),
                        new BeatShot(you, "...Now it's personal.", 4f, CameraMove.On(you, 15, -10, 3.4f, 2.6f, 1.7f)),
                        new BeatShot("", "", 3f, CameraMove.At(Anchor.RedSpawn, 200, 160, 8, 13, 3, 5, 2)),
                    }),
                // The boss fight's transitions (ticket 33, section 6) and its victory (section 8): played in the arena over the fight, which is held
                // (nobody can be hurt), and not campaign steps. Each transition is 6 s with no arrival, so skipping goes straight back to the
                // fight. A shot's cues (BossBeats) tell the scene to change as the story does. Prime is the real, giant Prime (Anchor.Prime), the
                // Ref speaks from the sky (a wide shot over the middle of the field), and 42 is the view from their own eyes (Anchor.You).
                new Beat(BossBeats.Transition12, "Timeout", new BeatActor[0],
                    new[]
                    {
                        new BeatShot(Prime, "Fine! FINE. Ref, I'm calling a timeout.", 2f, CameraMove.At(Anchor.Prime, 170, 190, 18, 14, 5, 6, 5.5f)),
                        new BeatShot(Ref, "Timeouts are not in the rulebook.", 1.8f, CameraMove.At(Anchor.Centre, 180, 160, 70, 60, 30, 26, 3)),
                        new BeatShot(Prime, "The rulebook says helicopter.", 2.2f, CameraMove.At(Anchor.Prime, 190, 150, 14, 22, 4, 10, 6),
                            BossBeats.CopterRises + " " + BossBeats.BalconyFalls),
                    }),
                new Beat(BossBeats.Transition23, "Rule four", new BeatActor[0],
                    new[]
                    {
                        new BeatShot("", "", 1.1f, CameraMove.At(Anchor.Prime, 150, 190, 26, 18, 9, 5, 3), BossBeats.CopterDown + " " + BossBeats.CrownFlies),
                        new BeatShot(Prime, "My crown! Nobody touch the crown. That's an order.", 2f, CameraMove.At(Anchor.Prime, 175, 200, 14, 12, 5, 6, 5.5f), BossBeats.PrimeClimbs),
                        new BeatShot(Ref, "A loose crown is a capturable object. Rule four.", 1.7f, CameraMove.At(Anchor.Centre, 180, 160, 70, 60, 30, 26, 3)),
                        new BeatShot(you, "There's a rule four?", 1.2f, CameraMove.At(Anchor.You, 0, 0, 0.3f, 0.3f, 1.65f, 1.65f, 2.2f)),
                    }),
                // The victory (about 22 s): Prime shrinks with pops to scout size, the recount, the crown rolls to 42 and goes on Mushroom 7's cap,
                // the arena breaks into pixel blocks
                new Beat(BossBeats.Victory, "Still second",
                    new[]
                    {
                        new BeatActor(seven, Team.Red, Anchor.You, SevenRight, SevenForward, 200),
                    },
                    new[]
                    {
                        new BeatShot("", "", 3.5f, CameraMove.At(Anchor.Prime, 160, 200, 30, 8, 9, 3, 2.5f), BossBeats.Shrink),
                        new BeatShot(Prime, "I demand a recount.", 2.4f, CameraMove.At(Anchor.Prime, 175, 190, 4.5f, 3.5f, 1.4f, 1.2f, 0.9f)),
                        new BeatShot(Ref, "Recount complete. Still second.", 2.8f, CameraMove.At(Anchor.Prime, 180, 140, 40, 28, 20, 14, 0.5f)),
                        new BeatShot(Prime, "I'd like to speak to the tournament's manager.", 2.8f, CameraMove.At(Anchor.Prime, 195, 170, 3.8f, 3, 1.2f, 1.1f, 0.9f)),
                        new BeatShot(Ref, "I am the manager.", 2.2f, CameraMove.At(Anchor.Prime, 180, 140, 40, 28, 20, 14, 0.5f)),
                        new BeatShot(you, "Hold this. I've got a desk to get back to.", 4.5f, CameraMove.At(Anchor.You, 160, 200, 4.5f, 3.5f, 1.6f, 1.5f, 0.9f), BossBeats.CrownRolls),
                        new BeatShot("", "", 3.5f, CameraMove.At(Anchor.Centre, 200, 150, 70, 55, 30, 26, 4), BossBeats.Cheer + " " + BossBeats.PixelBreak),
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
