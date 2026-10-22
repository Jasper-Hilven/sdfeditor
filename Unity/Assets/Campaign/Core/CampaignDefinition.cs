using System.Collections.Generic;
using SdfBattle;

namespace SdfCampaign
{
    /// <summary>
    /// The ordered steps of the campaign with the numbers of ticket 32, section 2 (at Normal). Steps whose scenes do not exist yet
    /// (toy box to boss) are named here all the same; whether a scene can be played is decided by a check from outside
    /// (<see cref="CampaignProgress"/> takes it), so adding a level is adding its scene.
    /// </summary>
    public sealed class CampaignDefinition
    {
        public const string Intro = "intro", Meadow = "meadow", Fungal = "fungal", Chessboard = "chessboard", ToyBox = "toybox",
            Kitchen = "kitchen", Bathroom = "bathroom", Computer = "computer", Boss = "boss", Ending = "ending", Credits = "credits";

        public IReadOnlyList<CampaignStep> Steps { get; }

        public CampaignDefinition(IReadOnlyList<CampaignStep> steps)
        {
            Steps = steps;
        }

        public int IndexOf(string id)
        {
            for (var i = 0; i < Steps.Count; i++)
                if (Steps[i].id == id) return i;
            return -1;
        }

        public CampaignStep Find(string id)
        {
            var i = IndexOf(id);
            return i < 0 ? null : Steps[i];
        }

        /// <summary>The battles in order (the eight arenas of Level Select).</summary>
        public IEnumerable<CampaignStep> Battles()
        {
            foreach (var s in Steps)
                if (s.IsBattle) yield return s;
        }

        public static CampaignDefinition Default()
        {
            var steps = new List<CampaignStep>
            {
                // The intro is 31's comic panels, played over Boot: it needs no scene of its own
                new(Intro, StepKind.Cutscene, "Boot", "Intro"),
                Battle(Meadow, "Meadow", "Meadow", 3, 3, 0.15f, 0.35f, 2, 8, sporesOn: false, warmUp: true),
                new("beat-fungal", StepKind.Cutscene, "Fungal", "Nightfall"),
                Battle(Fungal, "Fungal", "Fungal", 4, 4, 0.30f, 0.40f, 3, 10),
                new("beat-chessboard", StepKind.Cutscene, "Chessboard", "That's my chess set"),
                Battle(Chessboard, "Chessboard", "Chessboard", 4, 4, 0.40f, 0.45f, 3, 10),
                new("beat-toybox", StepKind.Cutscene, "ToyBox", "Off the table"),
                Battle(ToyBox, "ToyBox", "Toy box", 4, 4, 0.50f, 0.50f, 3, 10),
                new("beat-kitchen", StepKind.Cutscene, "Kitchen", "Down the hall"),
                Battle(Kitchen, "Kitchen", "Kitchen", 4, 4, 0.55f, 0.50f, 3, 10),
                new("beat-bathroom", StepKind.Cutscene, "Bathroom", "Bath time"),
                Battle(Bathroom, "Bathroom", "Bathroom", 4, 5, 0.60f, 0.55f, 3, 10),
                new("beat-computer", StepKind.Cutscene, "Computer", "The results file"),
                Battle(Computer, "Computer", "Inside the computer", 4, 5, 0.70f, 0.60f, 3, 12, blueRespawn: 3),
                new("beat-boss", StepKind.Cutscene, "Boss", "Second place"),
                BossBattle(),
                // The ending plays over Boot's backdrop (Home.unity is editor-only), like the credits
                new(Ending, StepKind.Cutscene, "Boot", "Ending"),
                new(Credits, StepKind.Credits, "Boot", "Credits"),
            };
            return new CampaignDefinition(steps);
        }

        static CampaignStep Battle(string id, string scene, string title, int red, int blue, float blueSkill, float redSkill, int captures,
            float minutes, bool sporesOn = true, bool warmUp = false, float blueRespawn = 5)
        {
            return new CampaignStep(id, StepKind.Battle, scene, title, new BattleSetup
            {
                redSize = red,
                blueSize = blue,
                blueSkill = blueSkill,
                redSkill = redSkill,
                redNames = Names(Team.Red, red),
                blueNames = Names(Team.Blue, blue),
                capturesToWin = captures,
                timeLimitSeconds = minutes * 60f,
                singleRound = true,
                warmUp = warmUp,
                sporesOn = sporesOn,
                blueRespawnSeconds = blueRespawn,
            });
        }

        // Ticket 33: 42 and three red bots (skill 0.6) against Prime's crew, up to five blue scouts (skill 0.7 at Normal, respawning in 8 s;
        // how many are out in each phase is BossRules'). Captures and the clock do not decide it; 42's lives and the rest of Prime's
        // numbers per difficulty are BossRules.For.
        static CampaignStep BossBattle()
        {
            return new CampaignStep(Boss, StepKind.Battle, "Boss", "Chanterelle Prime", new BattleSetup
            {
                redSize = 4,
                blueSize = 5,
                blueSkill = 0.70f,
                redSkill = 0.60f,
                redNames = Names(Team.Red, 4),
                blueNames = Names(Team.Blue, 5),
                capturesToWin = 3,
                timeLimitSeconds = 0,
                singleRound = true,
                sporesOn = true,
                blueRespawnSeconds = 8,
                objective = MatchObjective.Boss,
            });
        }

        static string[] Names(Team team, int count)
        {
            var names = new string[count];
            for (var i = 0; i < count; i++) names[i] = Cast.Name(team, i + 1);
            return names;
        }
    }
}
