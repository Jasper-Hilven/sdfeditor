using System.Text.RegularExpressions;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Story;

namespace SDFPipeline.Tests.Story;

/// <summary>
/// Tickets 31.4 and 34: the comics are data, content/story/&lt;comic&gt;.txt (the intro's twelve panels, a story beat's few), read by StoryFile,
/// which the Unity editor's capture compiles from its own copy of the source.
/// </summary>
public class StoryFileTests
{
    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static StoryFile Intro() => StoryFile.Load(StoryFile.PathIn(Content()));

    private static StoryFile Comic(string comic) => StoryFile.Load(StoryFile.PathIn(Content(), comic));

    /// <summary>Every comic of content/story/, for the checks that hold for all of them.</summary>
    public static IEnumerable<object[]> ComicNames() => StoryFile.Comics(Content()).Select(c => new object[] { c });

    /// <summary>The players a panel in a level or the warp may add: the scouts (red "Mushroom N", Chanterelle Prime, blue's players by their cast names) and the blue hands, never a prop.</summary>
    private const string Players = "^(Mushroom \\d+|Chanterelle Prime|Chanterelle Hand \\d|Runner-Up|Honourable Mention|Participant|Also-Ran|Participation Award)$";

    private static IEnumerable<StoryFrame> Frames(StoryFile file) => file.Panels.SelectMany(p => p.Frames);

    /// <summary>A panel with a camera and one more line, to try the line in.</summary>
    private static StoryFile Parse(string line) =>
        StoryFile.Parse("format 1\npanel 1 stage=home\n  camera pos=(0, 1, 2) rot=(0, 180, 0) fov=50\n  " + line + "\n");

    private static void Fails(string text, string message)
    {
        var exception = Assert.Throws<InvalidDataException>(() => StoryFile.Parse(text, "test.txt"));
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void TheIntroHasTwelvePanelsNumberedOneToTwelve()
    {
        var file = Intro();
        Assert.Equal(Enumerable.Range(1, 12), file.Panels.Select(p => p.Number));
    }

    [Fact]
    public void EveryPanelHasAKnownStageAndACameraForEveryFrame()
    {
        foreach (var panel in Intro().Panels)
        {
            Assert.True(StoryFile.Stages.Contains(panel.Stage) || File.Exists(Path.Combine(Content(), "levels", panel.Stage + ".level")), panel.Stage);
            Assert.NotEmpty(panel.Frames);
            foreach (var frame in panel.Frames)
            {
                var camera = panel.CameraOf(frame);
                Assert.True(camera != null, $"panel {panel.Number} has a frame without a camera");
                Assert.InRange(camera.Fov, 5, 120);
            }
        }
        // The arrival, the teammate and the ready panel are in the meadow, the warp in the empty stage, the rest at home
        var stages = Intro().Panels.Select(p => p.Stage).ToArray();
        Assert.Equal(new[] { "home", "home", "home", "home", "home", "home", "home", "home", "warp", "meadow", "meadow", "meadow" }, stages);
    }

    [Fact]
    public void EachFrameHasAtMostTwoShortBalloons()
    {
        foreach (var panel in Intro().Panels)
            foreach (var frame in panel.Frames)
            {
                Assert.True(frame.BalloonCount <= 2, $"panel {panel.Number} has {frame.BalloonCount} balloons in a frame");
                foreach (var text in frame.Texts.Where(t => t.Kind is StoryTextKind.Balloon or StoryTextKind.Sky))
                    Assert.InRange(text.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 1, 13);
            }
    }

    [Fact]
    public void EveryBalloonHasAKnownSpeakerAndATailTarget()
    {
        var actors = KnownActors();
        var balloons = Frames(Intro()).SelectMany(f => f.Texts).Where(t => t.Kind == StoryTextKind.Balloon).ToList();
        Assert.NotEmpty(balloons);
        foreach (var balloon in balloons)
        {
            Assert.Contains(balloon.Speaker, StoryFile.Speakers);
            Assert.True(balloon.HasTail, $"line {balloon.Line}: the balloon has no tail target");
            if (balloon.TailActor != null)
                Assert.Contains(balloon.TailActor, actors);
        }
        // The sky box is Prime's, and is the one lettering without a tail
        var sky = Frames(Intro()).SelectMany(f => f.Texts).Where(t => t.Kind == StoryTextKind.Sky).ToList();
        Assert.Single(sky);
        Assert.Equal("Prime", sky[0].Speaker);
        Assert.False(sky[0].HasTail);
    }

    [Fact]
    public void EveryScreenTextureIsOneOfTheFour()
    {
        var screens = Intro().Panels.Where(p => p.Screen != null).Select(p => p.Screen).ToList();
        Assert.NotEmpty(screens);
        foreach (var screen in screens)
            Assert.Contains(screen.Texture, StoryFile.ScreenTextures);
        Assert.Equal(9, StoryFile.ScreenTextures.Length);
        // Only the room has a monitor
        foreach (var panel in Intro().Panels.Where(p => p.Stage != "home"))
            Assert.Null(panel.Screen);
    }

    [Fact]
    public void TheTextIsTheTicketsTextAndMentionsNoOneWithTheWrongPronoun()
    {
        var texts = Frames(Intro()).SelectMany(f => f.Texts).Select(t => t.Text).ToList();
        Assert.Contains("Mushroom 42. Last year's minigun champion. Still undefeated. (They checked.)", texts);
        Assert.Contains("Still got it. Hi, trophy.", texts);
        Assert.Contains("Hello, champion. Remember me? Second place. *Silver.*", texts);
        Assert.Contains("This year, we play on *my* server.", texts);
        Assert.Contains("That's not how computers wor—", texts);
        Assert.Contains("It was, in fact, how computers work.", texts);
        Assert.Contains("42! You're here! Prime's rigged the whole tournament. Every level. Also, welcome.", texts);
        Assert.Contains("Win it again, champion. If you can.", texts);
        Assert.Contains("Fine. Bring the minigun.", texts);
        Assert.Equal(15, texts.Count);
        // The balloons carry no names; the narrator's side notes do, short and each pointing at someone
        foreach (var note in Frames(Intro()).SelectMany(f => f.Texts).Where(t => t.Kind == StoryTextKind.Note))
        {
            Assert.InRange(note.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 3, 8);
            Assert.True(note.HasTail);
        }
        Assert.Contains("Mushroom 7. Teammate. Talks fast.", texts);
        // 42 is they, Prime is he: nothing in the lettering calls 42 he or she
        foreach (var text in texts)
            Assert.DoesNotMatch(@"\b(he|she|him|his|her)\b", text);
    }

    [Fact]
    public void ActorsAreTheHomeFilesActorsPropsOrThePanelsScouts()
    {
        var known = KnownActors();
        foreach (var pose in Intro().Panels.SelectMany(p => p.Actors))
        {
            Assert.Contains(pose.Actor, known);
            if (pose.Part != null)
                Assert.Matches("^[a-z0-9_-]+$", pose.Part);
        }
        // Panel 8: the squash and stretch is on the body, the chair's seat flies off
        var eight = Intro().Panels[7];
        Assert.Contains(eight.Actors, a => a.Actor == "Mushroom 42" && a.Part == "body" && a.Scale.HasValue);
        Assert.Contains(eight.Actors, a => a.Actor == "chair" && a.Part == "seat");
    }

    [Fact]
    public void FramesAreInsideTheImage()
    {
        // Panel 3 is the one split in two
        Assert.Equal(2, Intro().Panels[2].Frames.Count);
        foreach (var frame in Frames(Intro()))
        {
            Assert.InRange(frame.X, 0, 1);
            Assert.InRange(frame.Y, 0, 1);
            Assert.InRange(frame.X + frame.W, 0, 1.0001f);
            Assert.InRange(frame.Y + frame.H, 0, 1.0001f);
        }
    }

    [Fact]
    public void UnityReadsTheSameSource()
    {
        // The editor's capture can only compile what is under Unity/Assets, so it holds a copy of the reader, byte for byte
        var root = Path.GetDirectoryName(Content())!;
        var source = File.ReadAllText(Path.Combine(root, "SDFPipeline", "Story", "StoryFile.cs"));
        var copy = File.ReadAllText(Path.Combine(root, "Unity", "Assets", "Story", "Editor", "StoryFile.cs"));
        Assert.True(source == copy, "Unity/Assets/Story/Editor/StoryFile.cs differs from SDFPipeline/Story/StoryFile.cs: copy the pipeline's over it");
        Assert.True(File.Exists(Path.Combine(root, "Unity", "Assets", "Story", "Editor", "StoryFile.cs.meta")));
    }

    // What a panel may name: the actors of home.txt, the props (home models) and the scouts of the meadow and warp stages
    private static List<string> KnownActors()
    {
        var names = Regex.Matches(File.ReadAllText(Path.Combine(Content(), "scenes", "home.txt")), "^actor \\S+ name=\"([^\"]*)\"", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        names.AddRange(Directory.GetFiles(Path.Combine(Content(), "models", "home"), "*.sdf").Select(Path.GetFileNameWithoutExtension).OfType<string>());
        Assert.Contains("Mushroom 42", names);
        return names;
    }

    // ---- Every comic (ticket 34) ----

    [Fact]
    public void TheComicsAreTheIntroFirstAndTheBeatsByTheirStepIds()
    {
        var comics = StoryFile.Comics(Content());
        Assert.Equal(StoryFile.Intro, comics[0]);
        Assert.Contains("beat-fungal", comics);
        foreach (var comic in comics.Skip(1))
            Assert.Matches("^(beat-[a-z0-9]+|ending)$", comic);
        Assert.Equal(StoryFile.PathIn(Content()), StoryFile.PathIn(Content(), StoryFile.Intro));
        Assert.Equal("beat-fungal", Comic("beat-fungal").Comic);
    }

    [Theory]
    [MemberData(nameof(ComicNames))]
    public void EveryComicHasAKnownStageACameraPerFrameAndShortBalloons(string comic)
    {
        var file = Comic(comic);
        Assert.Equal(Enumerable.Range(1, file.Panels.Count), file.Panels.Select(p => p.Number));
        foreach (var panel in file.Panels)
        {
            // home, warp, or a battle level that has its file
            Assert.True(StoryFile.Stages.Contains(panel.Stage) || File.Exists(Path.Combine(Content(), "levels", panel.Stage + ".level")), $"{comic} panel {panel.Number}: stage {panel.Stage}");
            foreach (var frame in panel.Frames)
            {
                Assert.NotNull(panel.CameraOf(frame));
                Assert.True(frame.BalloonCount <= 2, $"{comic} panel {panel.Number} has {frame.BalloonCount} balloons in a frame");
                foreach (var text in frame.Texts.Where(t => t.Kind is StoryTextKind.Balloon or StoryTextKind.Sky))
                    Assert.InRange(text.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 1, 13);
                foreach (var note in frame.Texts.Where(t => t.Kind == StoryTextKind.Note))
                {
                    Assert.InRange(note.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 3, 8);
                    Assert.True(note.HasTail, $"{comic} line {note.Line}: a note points at someone");
                }
                foreach (var text in frame.Texts.Where(t => t.Kind == StoryTextKind.Balloon))
                    Assert.Contains(text.Speaker, StoryFile.Speakers);
                Assert.InRange(frame.X + frame.W, 0, 1.0001f);
                Assert.InRange(frame.Y + frame.H, 0, 1.0001f);
            }
        }
        // 42 is they, Prime is he: nothing in the lettering calls 42 he or she, in any comic
        foreach (var text in Frames(file).SelectMany(f => f.Texts))
            Assert.DoesNotMatch(@"\b(he|she|him|his|her)\b", text.Text);
    }

    [Theory]
    [MemberData(nameof(ComicNames))]
    public void ALevelOrTheWarpGetsOnlyPlayersAndNoMonitorOrRoomLights(string comic)
    {
        // One theme per scene: a battle level keeps its own, so a panel there adds the scouts and nothing else
        var home = KnownActors();
        foreach (var panel in Comic(comic).Panels)
        {
            foreach (var pose in panel.Actors)
                if (panel.Stage == "home")
                    Assert.Contains(pose.Actor, home);
                else
                    Assert.Matches(Players, pose.Actor);
            foreach (var text in panel.Frames.SelectMany(f => f.Texts).Where(t => t.TailActor != null))
                Assert.Contains(text.TailActor, panel.Actors.Select(a => a.Actor).Concat(panel.Stage == "home" ? home : new List<string>()));
            if (panel.Stage != "home")
            {
                Assert.Null(panel.Screen);
                Assert.Empty(panel.Lights);
            }
        }
    }

    [Fact]
    public void TheFungalBeatIsFourPanelsInFungalWithTheTicketsLinesAndANoteForEachFirstAppearance()
    {
        var file = Comic("beat-fungal");
        Assert.InRange(file.Panels.Count, 3, 4);
        Assert.All(file.Panels, p => Assert.Equal("fungal", p.Stage));
        var texts = Frames(file).SelectMany(f => f.Texts).ToList();
        var lines = texts.Select(t => t.Text).ToList();
        // B1 "Nightfall" (ticket 32, section 3), tightened for lettering
        Assert.Contains("Congratulations on winning a *meadow*.", lines);
        Assert.Contains("Regulation update: *machines.*", lines);
        Assert.Contains("Machines aren't in the rulebook.", lines);
        Assert.Contains("They are now. I wrote the rulebook. This morning.", lines);
        Assert.Contains("Fill your spores. Activate Mycelium Rush. Trust me.", lines);
        Assert.Equal(new[] { "Prime", "42", "Mushroom 7" }, texts.Where(t => t.Kind == StoryTextKind.Balloon).Select(t => t.Speaker).Distinct());
        // The balloons carry no names: whoever speaks has a note pointing at them in their first panel
        foreach (var actor in new[] { "Chanterelle Prime", "Mushroom 42", "Mushroom 7" })
        {
            var first = file.Panels.First(p => p.Frames.SelectMany(f => f.Texts).Any(t => t.Kind == StoryTextKind.Balloon && t.TailActor == actor));
            var noted = file.Panels.TakeWhile(p => p.Number <= first.Number).SelectMany(p => p.Frames).SelectMany(f => f.Texts);
            Assert.Contains(noted, t => t.Kind == StoryTextKind.Note && t.TailActor == actor);
        }
        Assert.Equal(3, texts.Count(t => t.Kind == StoryTextKind.Note));
    }

    // ---- The grammar ----

    [Fact]
    public void ParsesEveryKindOfLine()
    {
        var file = StoryFile.Parse(
            """
            format 1
            # a comment
            panel 4 stage=meadow     # and another
              camera pos=(1, 2, 3) rot=(10, 20, 30) fov=45
              actor "Mushroom 42" pos=(1, 0, 0) scale=2
              actor "Mushroom 42" part=hand-left rot=(0, 0, 90)
              lamp intensity=0.5
              light Monitor color=(0.3, 0.5, 1)
              screen texture=screen-prime glitch=0.2 bulge=1
              speedlines radial at=(0.5, 0.5) density=2
              motionlines at=(0.1, 0.2) angle=45 length=0.3
              burst at=(0.9, 0.9)
              spores at=(1, 1, 1) count=5 radius=2 style=gold
              balloon speaker=42 text="Hi # there, (really)." anchor=(0.2, 0.2) tail="Mushroom 42"
              sky speaker=Prime text="Boo" anchor=(0.5, 0.1)
              caption text="A caption" anchor=(0.5, 0.9)
              note text="Mushroom 42. Champion." anchor=(0.8, 0.5) at="Mushroom 42"
              note text="Over there." anchor=(0.2, 0.5) at=(0.4, 0.6)
              sfx text="BOING" anchor=(0.5, 0.5) size=2 angle=-5
              sfx text="ZAP" anchor=(0.5, 0.5) color=pink
            """);
        var panel = Assert.Single(file.Panels);
        Assert.Equal(4, panel.Number);
        Assert.Equal("meadow", panel.Stage);
        Assert.Equal(20, panel.Camera.Rot.Y);
        Assert.Equal(45, panel.Camera.Fov);
        Assert.Equal(2, panel.Actors[0].Scale!.Value.Y);
        Assert.Equal("hand-left", panel.Actors[1].Part);
        Assert.Equal(90, panel.Actors[1].Rot!.Value.Z);
        Assert.Equal(new[] { "Lamp", "Monitor" }, panel.Lights.Select(l => l.Name));
        Assert.Equal(1, panel.Screen!.Bulge);
        var frame = Assert.Single(panel.Frames);
        Assert.Equal(new[] { "speedlines", "motionlines", "burst", "spores" }, frame.Devices.Select(d => d.Kind));
        Assert.Equal("gold", frame.Devices[3].Style);
        Assert.Equal(5, frame.Devices[3].Count);
        Assert.Equal("Hi # there, (really).", frame.Texts[0].Text);
        Assert.Equal("Mushroom 42", frame.Texts[0].TailActor);
        Assert.Equal(2, frame.BalloonCount);
        Assert.Equal("narrator", frame.Texts[2].Speaker);
        Assert.Equal(StoryTextKind.Note, frame.Texts[3].Kind);
        Assert.Equal("Mushroom 42", frame.Texts[3].TailActor);
        Assert.Equal(0.4f, frame.Texts[4].TailAt!.Value.X);
        Assert.Equal("narrator", frame.Texts[3].Speaker);
        Assert.Null(frame.Texts[5].Color);
        Assert.Equal("pink", frame.Texts[6].Color);
    }

    [Fact]
    public void SfxColorIsStableVariedAndKnown()
    {
        Assert.Equal(StoryFile.SfxColorFor("POP!"), StoryFile.SfxColorFor(" pop! "));
        var words = new[] { "POP!", "KRRK KRRK KRRK", "TAK TAK TAK", "BZZRT", "SHLORP", "WHOOOOSH", "BOING", "HMMMMM", "PTANG!", "TSSSS", "CLUNK", "ZAP" };
        var colours = words.Select(StoryFile.SfxColorFor).ToList();
        Assert.All(colours, c => Assert.Contains(c, StoryFile.SfxColors));
        Assert.True(colours.Distinct().Count() >= 4);
    }

    [Fact]
    public void FrameLinesSplitThePanelAndTheirContentBelongsToThem()
    {
        var file = StoryFile.Parse(
            """
            panel 1 stage=home
              camera pos=(0, 0, 0) rot=(0, 0, 0) fov=50
              frame x=0 y=0 w=0.5 h=1
                caption text="left" anchor=(0.5, 0.5)
              frame x=0.5 y=0 w=0.5 h=1
                camera pos=(1, 1, 1) rot=(0, 90, 0) fov=30
                caption text="right" anchor=(0.5, 0.5)
            """);
        var panel = file.Panels[0];
        Assert.Equal(2, panel.Frames.Count);
        Assert.Equal("left", panel.Frames[0].Texts[0].Text);
        Assert.Equal(50, panel.CameraOf(panel.Frames[0]).Fov);
        Assert.Equal(30, panel.CameraOf(panel.Frames[1]).Fov);
    }

    [Fact]
    public void RejectsMalformedLinesAndNamesTheLine()
    {
        Fails("camera pos=(0, 0, 0) rot=(0, 0, 0) fov=50", "test.txt line 1: 'camera' comes after a panel line");
        Fails("panel x stage=home", "panel needs a number");
        Fails("panel 1 stage=Fungal", "stage=Fungal is not a stage name");
        Fails("panel 1", "stage= is missing");
        Fails("panel 1 stage=home\n  camera pos=(0, 0, 0) rot=(0, 0, 0) fov=50\npanel 1 stage=home", "panel 1 is there twice");
        Fails("format 2\npanel 1 stage=home", "format 2 is not known");
        Fails("panel 1 stage=home\n  camera pos=(0, 0, 0) rot=(0, 0, 0) fov=50\n  camera pos=(0, 0, 0) rot=(0, 0, 0) fov=50", "line 3: the panel has its camera already");
        Fails("panel 1 stage=home\n  speedlines radial at=(0.5, 0.5)", "the panel has no camera");
        Fails("", "no panel in the file");
    }

    [Theory]
    [InlineData("camera pos=(0, 0) rot=(0, 0, 0) fov=50", "pos needs 3 numbers")]
    [InlineData("camera pos=(0, 0, 0) rot=(0, 0, 0)", "fov= is missing")]
    [InlineData("camera pos=(0, 0, 0) rot=(0, 0, 0) fov=500", "is not between 5 and 120")]
    [InlineData("camera pos=(0, 0, 0 rot=(0, 0, 0) fov=50", "a bracket is not closed")]
    [InlineData("camera pos=(0, 0, a) rot=(0, 0, 0) fov=50", "is not a number")]
    [InlineData("actor \"Mushroom 42\"", "needs pos=, rot= or scale=")]
    [InlineData("actor pos=(0, 0, 0)", "needs 1 name")]
    [InlineData("actor \"Mushroom 42\" part=Body pos=(0, 0, 0)", "part names are lower case")]
    [InlineData("actor \"Mushroom 42\" pos=(0, 0, 0) pos=(1, 1, 1)", "pos= is given twice")]
    [InlineData("actor \"Mushroom 42\" colour=(0, 0, 0)", "has no colour=")]
    [InlineData("screen texture=screen-purple", "is not one of")]
    [InlineData("screen texture=screen-meadow glitch=2", "is not between 0 and 1")]
    [InlineData("light Sun intensity=1", "is not one of")]
    [InlineData("light Lamp", "needs intensity= or color=")]
    [InlineData("lamp intensity=-1", "must not be negative")]
    [InlineData("speedlines wavy", "radial or parallel")]
    [InlineData("burst at=(2, 0.5)", "is not in the frame")]
    [InlineData("frame x=0.6 y=0 w=0.6 h=1", "leaves the image")]
    [InlineData("balloon speaker=42 text=\"Hi\" anchor=(0.5, 0.5)", "needs tail=")]
    [InlineData("balloon speaker=Bob text=\"Hi\" anchor=(0.5, 0.5) tail=(0.5, 0.5)", "is not one of")]
    [InlineData("balloon speaker=42 text=\"Hi\" tail=(0.5, 0.5)", "anchor= is missing")]
    [InlineData("balloon speaker=42 text=\"Hi anchor=(0.5, 0.5) tail=(0.5, 0.5)", "a quote is not closed")]
    [InlineData("balloon speaker=42 text=\"\" anchor=(0.5, 0.5) tail=(0.5, 0.5)", "the text is empty")]
    [InlineData("caption text=\"Hi\" anchor=(0.5, 0.5) speaker=42", "has no speaker=")]
    [InlineData("note text=\"Hi\" anchor=(0.5, 0.5)", "needs at=")]
    [InlineData("note text=\"Hi\" anchor=(0.5, 0.5) at=\"Mushroom 42\" speaker=42", "has no speaker=")]
    [InlineData("sfx anchor=(0.5, 0.5)", "text= is missing")]
    [InlineData("sfx text=\"POP!\" anchor=(0.5, 0.5) color=beige", "is not one of")]
    [InlineData("balloon speaker=42 text=\"Hi\" anchor=(0.5, 0.5) tail=(0.5, 0.5) color=red", "has no color=")]
    [InlineData("zoom 3", "unknown command 'zoom'")]
    public void RejectsBadArguments(string line, string message) => Fails("format 1\npanel 1 stage=home\n  camera pos=(0, 1, 2) rot=(0, 180, 0) fov=50\n  " + line, message);

    [Fact]
    public void FrameLinesAfterContentAreRejected() =>
        Fails("panel 1 stage=home\n  camera pos=(0, 0, 0) rot=(0, 0, 0) fov=50\n  burst at=(0.5, 0.5)\n  frame x=0 y=0 w=1 h=1", "frame lines come before any device or lettering");

    [Fact]
    public void AStageIsHomeWarpOrALevelByItsFileName()
    {
        const string text = "format 1\npanel 1 stage={0}\n  camera pos=(0, 1, 2) rot=(0, 180, 0) fov=50\n";
        Func<string, bool> isLevel = stage => stage is "fungal" or "toybox";
        Assert.Equal("fungal", StoryFile.Parse(string.Format(text, "fungal"), "test.txt", isLevel).Panels[0].Stage);
        Assert.Equal("warp", StoryFile.Parse(string.Format(text, "warp"), "test.txt", isLevel).Panels[0].Stage);
        Assert.True(StoryFile.IsLevelStage("toybox"));
        Assert.False(StoryFile.IsLevelStage("home"));
        var exception = Assert.Throws<InvalidDataException>(() => StoryFile.Parse(string.Format(text, "sky"), "test.txt", isLevel));
        Assert.Contains("test.txt line 2: stage=sky is not home, warp or a level (there is no content/levels/sky.level)", exception.Message);
        // Without the check any lower-case name is a level; a file read from content/ checks content/levels/
        Assert.Equal("sky", StoryFile.Parse(string.Format(text, "sky")).Panels[0].Stage);
        foreach (var level in new[] { "meadow", "fungal", "chessboard", "toybox", "kitchen", "bathroom", "computer", "arena" })
            Assert.True(File.Exists(Path.Combine(Content(), "levels", level + ".level")), level);
    }

    [Fact]
    public void ASingleScaleNumberScalesAllThreeAxes()
    {
        var scale = Parse("actor \"chair\" scale=1.5").Panels[0].Actors[0].Scale!.Value;
        Assert.Equal((1.5f, 1.5f, 1.5f), (scale.X, scale.Y, scale.Z));
    }
}
