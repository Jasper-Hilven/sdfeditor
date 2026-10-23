using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

#nullable disable

// Pure C# with no dependency on anything else of the pipeline or on Unity, and a language level Unity compiles: Unity/Assets/Story/Editor/StoryFile.cs
// is a byte for byte copy of this file (a test checks it), which is how the editor's capture reads the same format.
namespace SDFPipeline.Story
{
    /// <summary>A point or size in a frame's 2D space: 0..1 across and down the frame, (0, 0) at its top left.</summary>
    public readonly struct StoryVec2
    {
        public readonly float X, Y;
        public StoryVec2(float x, float y) { X = x; Y = y; }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
    }

    /// <summary>A position, euler angles in degrees or a scale, in the stage's frame (the pipeline's, as content/scenes/home.txt).</summary>
    public readonly struct StoryVec3
    {
        public readonly float X, Y, Z;
        public StoryVec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }

    /// <summary>A camera. Rotation is (pitch, yaw, roll) in degrees: yaw counter-clockwise from above, 0 looking along +z (so 90 looks along +x);
    /// pitch positive looks down; roll positive tilts the horizon clockwise on screen.</summary>
    public sealed class StoryCamera
    {
        public StoryVec3 Pos, Rot;
        public float Fov;
    }

    /// <summary>
    /// A pose for one actor or one part of it. Without a part the pose is the actor's own, absolute on the stage (position, rotation as
    /// (pitch, yaw, roll) like a camera's with positive pitch tipping the front down, scale). With a part (<c>body</c>, <c>eye-left</c>,
    /// <c>hand-right</c>, a prop's <c>seat</c>, <c>screen</c>, <c>crisp_1</c> ...) it is a change on the part's rest pose, in the actor's own
    /// space: an offset added to its position, a rotation applied on top and a scale multiplied in. What is not given is not changed.
    /// </summary>
    public sealed class StoryActorPose
    {
        public string Actor, Part;
        public StoryVec3? Pos, Rot, Scale;
        public int Line;
    }

    /// <summary>The picture on the monitor's screen part: one of <see cref="StoryFile.ScreenTextures"/>, with its glitch and bulge, 0..1.</summary>
    public sealed class StoryScreen
    {
        public string Texture;
        public float Glitch, Bulge;
    }

    /// <summary>A light of the stage by its name in the scene (Lamp, Monitor); only what is given is changed.</summary>
    public sealed class StoryLight
    {
        public string Name;
        public float? Intensity;
        public StoryVec3? Color;
    }

    /// <summary>A comic device drawn over a frame by the capture: speed lines, motion lines, an impact burst or pixel spores.</summary>
    public sealed class StoryDevice
    {
        /// <summary>One of <see cref="StoryFile.DeviceKinds"/>.</summary>
        public string Kind;
        /// <summary>"radial" or "parallel" for speed lines, "red", "blue" or "gold" for spores.</summary>
        public string Style;
        /// <summary>Where it is, in frame space (the lines' focus, the burst).</summary>
        public StoryVec2? At;
        /// <summary>Spores are placed in the stage.</summary>
        public StoryVec3? World;
        public float Angle, Length, Size, Density;
        public int Count;
        public int Line;
    }

    /// <summary>The kinds of lettering.</summary>
    public enum StoryTextKind { Balloon, Sky, Caption, Note, Sfx }

    /// <summary>One piece of lettering in a frame.</summary>
    public sealed class StoryText
    {
        public StoryTextKind Kind;
        /// <summary>One of <see cref="StoryFile.Speakers"/>; captions and SFX are the narrator's.</summary>
        public string Speaker;
        public string Text;
        public StoryVec2 Anchor;
        /// <summary>A balloon's tail (or a note's arrow, <c>at=</c>) points at a spot in the frame, or at an actor by name (the capture projects it); a sky box has none.</summary>
        public StoryVec2? TailAt;
        public string TailActor;
        public float Size, Angle;
        /// <summary>An SFX's burst colour, one of <see cref="StoryFile.SfxColors"/>; null when the file leaves it out (then <see cref="StoryFile.SfxColorFor"/> picks one from the text).</summary>
        public string Color;
        public int Line;
        public bool HasTail => TailAt.HasValue || TailActor != null;
    }

    /// <summary>A frame of a panel: the whole image, or an inset rect. It holds its own camera (or uses the panel's), devices and lettering.</summary>
    public sealed class StoryFrame
    {
        /// <summary>The rect in the image, 0..1, from the top left.</summary>
        public float X, Y, W = 1, H = 1;
        public StoryCamera Camera;
        public readonly List<StoryDevice> Devices = new List<StoryDevice>();
        public readonly List<StoryText> Texts = new List<StoryText>();
        public int Line;

        /// <summary>Speech: balloons and the sky box (what the limit of two per frame counts).</summary>
        public int BalloonCount
        {
            get
            {
                var count = 0;
                foreach (var text in Texts)
                    if (text.Kind == StoryTextKind.Balloon || text.Kind == StoryTextKind.Sky)
                        count++;
                return count;
            }
        }
    }

    public sealed class StoryPanel
    {
        public int Number;
        /// <summary>One of <see cref="StoryFile.Stages"/>, or a battle level by its name (<see cref="StoryFile.IsLevelStage"/>).</summary>
        public string Stage;
        public StoryCamera Camera;
        public readonly List<StoryActorPose> Actors = new List<StoryActorPose>();
        public StoryScreen Screen;
        public readonly List<StoryLight> Lights = new List<StoryLight>();
        /// <summary>At least one; a panel without <c>frame</c> lines has one that fills the image.</summary>
        public readonly List<StoryFrame> Frames = new List<StoryFrame>();
        public int Line;

        /// <summary>The camera a frame renders with: its own, or the panel's.</summary>
        public StoryCamera CameraOf(StoryFrame frame) => frame.Camera ?? Camera;
    }

    /// <summary>
    /// A comic of content/story/, read (tickets 31.4, 34): one file per comic, named as the campaign step that plays it: intro.txt, the twelve
    /// panels of the story intro, and beat-&lt;level&gt;.txt, a story beat between battles (beat-fungal.txt is B1, "Nightfall"). The editor's
    /// capture poses, renders and letters the panels. A line is a command with names and name=value arguments; '#' starts a comment; indentation is only for reading; a name or
    /// text with spaces is in double quotes (no quote inside). Points are (x, y, z) in the stage's frame (the pipeline's); frame-space points
    /// are (x, y) from 0 to 1 from the frame's top left. Texts may carry *italics* for the lettering.
    /// <code>
    /// format 1
    /// panel 3 stage=home                       # stage: home (Home.unity), warp (an empty stage) or a level: meadow (Meadow.unity), fungal ...
    ///   camera pos=(x, y, z) rot=(pitch, yaw, roll) fov=40
    ///   actor "Mushroom 42" pos=.. rot=.. scale=..                 # an actor as a whole, absolute (scale a number or a point)
    ///   actor "Mushroom 42" part=hand-left pos=.. rot=.. scale=..  # a part of it, as a change on its rest pose
    ///   screen texture=screen-victory glitch=0 bulge=0             # the monitor's picture (screen-meadow, -cover, -game, -victory, -glitch, -prime, and the ending's -ending-banner, -ending-victory, -ending-title)
    ///   lamp intensity=2.5                                         # the Lamp light; light Monitor intensity=.. color=(r, g, b) for any
    ///   frame x=0 y=0 w=0.5 h=1                                    # an inset frame; devices and lettering below belong to it
    ///     camera ...                                               # a camera after a frame is that frame's own
    ///     speedlines radial at=(0.5, 0.5) density=1                # or: speedlines parallel angle=0
    ///     motionlines at=(x, y) angle=.. length=..
    ///     burst at=(x, y) size=..
    ///     spores at=(x, y, z) count=12 radius=.. style=blue
    ///     balloon speaker=42 text="..." anchor=(x, y) tail=(x, y)   # or tail="Mushroom 42", an actor
    ///     sky speaker=Prime text="..." anchor=(x, y)               # the jagged box in the sky
    ///     caption text="..." anchor=(x, y)
    ///     note text="..." anchor=(x, y) at=(x, y)                  # the narrator's small side note, an arrow at a spot; or at="Mushroom 42", an actor
    ///     sfx text="BOING" anchor=(x, y) size=1 angle=-8 color=orange   # color is one of yellow, orange, red, pink, blue, green, purple;
    ///                                                              # left out, it is picked from the text (the same text always gets the same colour);
    ///                                                              # the lettering's own colour is chosen to contrast with the burst
    /// </code>
    /// 42 is "they" and Prime "he" in every text. Errors name the file and the line.
    /// </summary>
    public sealed class StoryFile
    {
        /// <summary>
        /// The capture's own stages: 42's room and the empty warp stage. Any other stage is a battle level by its file's name
        /// (content/levels/fungal.level is <c>fungal</c>, rendered in Assets/Scenes/Fungal.unity), so a new beat's comic needs no new stage here.
        /// </summary>
        public static readonly string[] Stages = { "home", "warp" };
        public static readonly string[] ScreenTextures = { "screen-meadow", "screen-cover", "screen-game", "screen-victory", "screen-glitch", "screen-prime", "screen-ending-banner", "screen-ending-victory", "screen-ending-title" };
        /// <summary>The cast who letter balloons: 42, Prime, red teammates by name, Prime's fifth player, the Ref (Ref Puffball, a voice from the sky box: no actor), the narrator.</summary>
        public static readonly string[] Speakers = { "42", "Prime", "Mushroom 7", "Mushroom 13", "Mushroom 99", "Participation Award", "Ref", "narrator" };
        public static readonly string[] DeviceKinds = { "speedlines", "motionlines", "burst", "spores" };
        public static readonly string[] SpeedLineStyles = { "radial", "parallel" };
        public static readonly string[] SporeStyles = { "red", "blue", "gold" };
        /// <summary>The colours of an SFX's burst, by name.</summary>
        public static readonly string[] SfxColors = { "yellow", "orange", "red", "pink", "blue", "green", "purple" };

        /// <summary>The burst colour an SFX without <c>color=</c> gets: from a stable hash of its text (case and spaces at the ends do not matter).</summary>
        public static string SfxColorFor(string text)
        {
            uint hash = 2166136261;
            foreach (var c in (text ?? "").Trim().ToUpperInvariant())
                hash = (hash ^ c) * 16777619;
            return SfxColors[(int)(hash % (uint)SfxColors.Length)];
        }

        public static readonly string[] LightNames = { "Lamp", "Monitor" };

        /// <summary>The intro's comic; its panels are the viewer's own (Assets/Story/Panels), every other comic's are loaded by name.</summary>
        public const string Intro = "intro";
        public const string Extension = ".txt";
        public const string FileName = Intro + Extension;

        /// <summary>The comic's name: its file's name without the extension (intro, beat-fungal), null for parsed text.</summary>
        public string Comic;

        public readonly List<StoryPanel> Panels = new List<StoryPanel>();

        /// <summary>A comic's file in the content folder (the one holding models, levels, ...); the intro's when no comic is named.</summary>
        public static string PathIn(string contentFolder, string comic = Intro) => Path.Combine(contentFolder, "story", comic + Extension);

        /// <summary>The comics in the content folder by name, the intro first and then the rest in name order.</summary>
        public static List<string> Comics(string contentFolder)
        {
            var folder = Path.Combine(contentFolder, "story");
            var names = new List<string>();
            if (!Directory.Exists(folder))
                return names;
            foreach (var path in Directory.GetFiles(folder, "*" + Extension))
                names.Add(Path.GetFileNameWithoutExtension(path));
            names.Sort((a, b) => a == Intro ? (b == Intro ? 0 : -1) : b == Intro ? 1 : string.CompareOrdinal(a, b));
            return names;
        }

        /// <summary>Whether a stage is a battle level's (rendered in its scene) rather than one of the capture's own <see cref="Stages"/>.</summary>
        public static bool IsLevelStage(string stage) => Array.IndexOf(Stages, stage) < 0;

        /// <summary>A comic's file; its level stages are checked against the level files next to the story folder (content/levels/&lt;stage&gt;.level).</summary>
        public static StoryFile Load(string path)
        {
            var levels = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path))) ?? "", "levels");
            var file = Parse(File.ReadAllText(path), path, stage => File.Exists(Path.Combine(levels, stage + ".level")));
            file.Comic = Path.GetFileNameWithoutExtension(path);
            return file;
        }

        /// <summary>
        /// Reads a comic's text. <paramref name="isLevel"/> says whether a stage other than <see cref="Stages"/> names a battle level; without
        /// it any lower-case name is taken as one.
        /// </summary>
        public static StoryFile Parse(string text, string source = "story", Func<string, bool> isLevel = null)
        {
            var file = new StoryFile();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            StoryPanel panel = null;
            StoryFrame frame = null;
            var insets = false;     // a frame line has come: devices and lettering belong to the latest frame, not the whole image
            var seenFormat = false;
            for (var i = 0; i < lines.Length; i++)
            {
                try
                {
                    var command = Command.Read(lines[i]);
                    if (command == null)
                        continue;
                    if (command.Word == "format")
                    {
                        if (seenFormat || file.Panels.Count > 0)
                            throw new InvalidDataException("the format line comes once, first");
                        command.Positional(1);
                        if (command.Positionals[0] != "1")
                            throw new InvalidDataException("format " + command.Positionals[0] + " is not known (format 1)");
                        seenFormat = true;
                        continue;
                    }
                    if (command.Word == "panel")
                    {
                        if (panel != null)
                            Finish(panel);
                        command.Positional(1);
                        command.Allow("stage");
                        if (!int.TryParse(command.Positionals[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 1)
                            throw new InvalidDataException("panel needs a number from 1, not '" + command.Positionals[0] + "'");
                        foreach (var other in file.Panels)
                            if (other.Number == number)
                                throw new InvalidDataException("panel " + number + " is there twice");
                        panel = new StoryPanel { Number = number, Line = i + 1, Stage = Stage(command, isLevel) };
                        frame = new StoryFrame { Line = i + 1 };
                        insets = false;
                        panel.Frames.Add(frame);
                        file.Panels.Add(panel);
                        continue;
                    }
                    if (panel == null)
                        throw new InvalidDataException("'" + command.Word + "' comes after a panel line");
                    ParseInPanel(command, i + 1, panel, ref frame, ref insets);
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(source + " line " + (i + 1) + ": " + exception.Message);
                }
            }
            if (panel != null)
            {
                try { Finish(panel); }
                catch (InvalidDataException exception) { throw new InvalidDataException(source + " panel " + panel.Number + ": " + exception.Message); }
            }
            if (file.Panels.Count == 0)
                throw new InvalidDataException(source + ": no panel in the file");
            return file;
        }

        // home, warp or a battle level by its file's name
        private static string Stage(Command command, Func<string, bool> isLevel)
        {
            var stage = command.Text("stage", true);
            if (!IsLevelStage(stage))
                return stage;
            foreach (var c in stage)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9'))
                    throw new InvalidDataException("stage=" + stage + " is not a stage name (lower-case letters and digits: home, warp or a level such as fungal)");
            if (stage.Length == 0 || isLevel != null && !isLevel(stage))
                throw new InvalidDataException("stage=" + stage + " is not home, warp or a level (there is no content/levels/" + stage + ".level)");
            return stage;
        }

        // What every panel needs before the next one starts
        private static void Finish(StoryPanel panel)
        {
            // The panel's camera, or one in every frame
            foreach (var frame in panel.Frames)
                if (panel.CameraOf(frame) == null)
                    throw new InvalidDataException("the panel has no camera" + (panel.Frames.Count > 1 ? " for the frame on line " + frame.Line : ""));
        }

        private static void ParseInPanel(Command command, int line, StoryPanel panel, ref StoryFrame frame, ref bool insets)
        {
            switch (command.Word)
            {
                case "camera":
                {
                    command.Allow("pos", "rot", "fov");
                    var camera = new StoryCamera { Pos = command.Vec3("pos"), Rot = command.Vec3("rot"), Fov = command.Number("fov") };
                    if (camera.Fov < 5 || camera.Fov > 120)
                        throw new InvalidDataException("fov " + camera.Fov.ToString(CultureInfo.InvariantCulture) + " is not between 5 and 120");
                    // Before any frame line it is the panel's camera, after one it is that frame's own
                    if (!insets)
                    {
                        if (panel.Camera != null)
                            throw new InvalidDataException("the panel has its camera already");
                        panel.Camera = camera;
                    }
                    else
                    {
                        if (frame.Camera != null)
                            throw new InvalidDataException("the frame has its camera already");
                        frame.Camera = camera;
                    }
                    break;
                }
                case "actor":
                {
                    command.Positional(1);
                    command.Allow("part", "pos", "rot", "scale");
                    var pose = new StoryActorPose
                    {
                        Actor = command.Positionals[0], Part = command.Text("part", false), Line = line,
                        Pos = command.OptionalVec3("pos"), Rot = command.OptionalVec3("rot"), Scale = command.OptionalScale("scale"),
                    };
                    if (!pose.Pos.HasValue && !pose.Rot.HasValue && !pose.Scale.HasValue)
                        throw new InvalidDataException("actor '" + pose.Actor + "' needs pos=, rot= or scale=");
                    if (pose.Part != null && pose.Part != pose.Part.ToLowerInvariant())
                        throw new InvalidDataException("part names are lower case, not '" + pose.Part + "'");
                    panel.Actors.Add(pose);
                    break;
                }
                case "screen":
                {
                    command.Allow("texture", "glitch", "bulge");
                    if (panel.Screen != null)
                        throw new InvalidDataException("the panel has a screen line already");
                    panel.Screen = new StoryScreen
                    {
                        Texture = command.OneOf("texture", ScreenTextures),
                        Glitch = command.Unit("glitch", 0),
                        Bulge = command.Unit("bulge", 0),
                    };
                    break;
                }
                case "lamp":
                {
                    command.Allow("intensity");
                    panel.Lights.Add(new StoryLight { Name = "Lamp", Intensity = command.NonNegative("intensity") });
                    break;
                }
                case "light":
                {
                    command.Positional(1);
                    command.Allow("intensity", "color");
                    if (Array.IndexOf(LightNames, command.Positionals[0]) < 0)
                        throw new InvalidDataException("light '" + command.Positionals[0] + "' is not one of " + string.Join(", ", LightNames));
                    var light = new StoryLight { Name = command.Positionals[0], Color = command.OptionalVec3("color") };
                    if (command.Has("intensity"))
                        light.Intensity = command.NonNegative("intensity");
                    if (!light.Intensity.HasValue && !light.Color.HasValue)
                        throw new InvalidDataException("light needs intensity= or color=");
                    panel.Lights.Add(light);
                    break;
                }
                case "frame":
                {
                    command.Allow("x", "y", "w", "h");
                    if (!insets)
                    {
                        // The whole-image frame made for the panel gives way, if nothing was put in it yet
                        if (frame.Devices.Count > 0 || frame.Texts.Count > 0)
                            throw new InvalidDataException("frame lines come before any device or lettering");
                        panel.Frames.Clear();
                        insets = true;
                    }
                    var inset = new StoryFrame { Line = line, X = command.Unit("x"), Y = command.Unit("y"), W = command.Unit("w"), H = command.Unit("h") };
                    if (inset.W <= 0 || inset.H <= 0 || inset.X + inset.W > 1.0001f || inset.Y + inset.H > 1.0001f)
                        throw new InvalidDataException("the frame leaves the image");
                    panel.Frames.Add(inset);
                    frame = inset;
                    break;
                }
                case "speedlines":
                {
                    command.Positional(1);
                    command.Allow("at", "angle", "density");
                    var lines = new StoryDevice
                    {
                        Kind = "speedlines", Line = line, Style = command.Positionals[0],
                        At = command.OptionalVec2("at"), Angle = command.Number("angle", 0), Density = command.NonNegative("density", 1),
                    };
                    if (Array.IndexOf(SpeedLineStyles, lines.Style) < 0)
                        throw new InvalidDataException("speedlines are radial or parallel, not '" + lines.Style + "'");
                    frame.Devices.Add(lines);
                    break;
                }
                case "motionlines":
                {
                    command.Allow("at", "angle", "length");
                    frame.Devices.Add(new StoryDevice
                    {
                        Kind = "motionlines", Line = line, At = command.Vec2("at"), Angle = command.Number("angle", 0), Length = command.NonNegative("length", 0.2f),
                    });
                    break;
                }
                case "burst":
                {
                    command.Allow("at", "size");
                    frame.Devices.Add(new StoryDevice { Kind = "burst", Line = line, At = command.Vec2("at"), Size = command.NonNegative("size", 1) });
                    break;
                }
                case "spores":
                {
                    command.Allow("at", "count", "radius", "style");
                    frame.Devices.Add(new StoryDevice
                    {
                        Kind = "spores", Line = line, World = command.Vec3("at"), Size = command.NonNegative("radius", 0.5f),
                        Count = (int)command.NonNegative("count", 12), Style = command.Has("style") ? command.OneOf("style", SporeStyles) : "blue",
                    });
                    break;
                }
                case "balloon":
                case "sky":
                case "caption":
                case "note":
                case "sfx":
                    frame.Texts.Add(ParseText(command, line));
                    break;
                default:
                    throw new InvalidDataException("unknown command '" + command.Word + "'");
            }
        }

        private static StoryText ParseText(Command command, int line)
        {
            switch (command.Word)
            {
                case "balloon": command.Allow("speaker", "text", "anchor", "tail"); break;
                case "sky": command.Allow("speaker", "text", "anchor"); break;
                case "caption": command.Allow("text", "anchor"); break;
                case "note": command.Allow("text", "anchor", "at"); break;
                default: command.Allow("text", "anchor", "size", "angle", "color"); break;
            }
            var text = new StoryText { Line = line, Text = command.Text("text", true), Anchor = command.Vec2("anchor"), Speaker = "narrator" };
            if (text.Text.Trim().Length == 0)
                throw new InvalidDataException("the text is empty");
            switch (command.Word)
            {
                case "balloon":
                    text.Kind = StoryTextKind.Balloon;
                    text.Speaker = command.OneOf("speaker", Speakers);
                    if (!command.Has("tail"))
                        throw new InvalidDataException("a balloon needs tail=(x, y) or tail=\"actor\"");
                    if (command.Value("tail").StartsWith("(", StringComparison.Ordinal))
                        text.TailAt = command.Vec2("tail");
                    else
                        text.TailActor = command.Text("tail", true);
                    break;
                case "sky":
                    text.Kind = StoryTextKind.Sky;
                    text.Speaker = command.OneOf("speaker", Speakers);
                    break;
                case "caption":
                    text.Kind = StoryTextKind.Caption;
                    break;
                case "note":
                    text.Kind = StoryTextKind.Note;
                    if (!command.Has("at"))
                        throw new InvalidDataException("a note needs at=(x, y) or at=\"actor\"");
                    if (command.Value("at").StartsWith("(", StringComparison.Ordinal))
                        text.TailAt = command.Vec2("at");
                    else
                        text.TailActor = command.Text("at", true);
                    break;
                default:
                    text.Kind = StoryTextKind.Sfx;
                    text.Size = command.NonNegative("size", 1);
                    text.Angle = command.Number("angle", 0);
                    text.Color = command.Has("color") ? command.OneOf("color", SfxColors) : null;
                    break;
            }
            return text;
        }

        /// <summary>One line, split: the word, the names after it and the name=value arguments.</summary>
        private sealed class Command
        {
            public string Word;
            public readonly List<string> Positionals = new List<string>();
            private readonly Dictionary<string, string> arguments = new Dictionary<string, string>();

            /// <summary>The command on a line, or null for a blank or comment line.</summary>
            public static Command Read(string text)
            {
                var tokens = Tokens(text);
                if (tokens.Count == 0)
                    return null;
                var command = new Command { Word = tokens[0] };
                for (var i = 1; i < tokens.Count; i++)
                {
                    var token = tokens[i];
                    var equals = token.IndexOf('=');
                    if (equals > 0 && IsName(token.Substring(0, equals)))
                    {
                        var key = token.Substring(0, equals);
                        if (command.arguments.ContainsKey(key))
                            throw new InvalidDataException(key + "= is given twice");
                        command.arguments[key] = token.Substring(equals + 1);
                    }
                    else
                        command.Positionals.Add(Unquote(token));
                }
                return command;
            }

            private static bool IsName(string text)
            {
                foreach (var c in text)
                    if (!char.IsLetterOrDigit(c))
                        return false;
                return true;
            }

            // Whitespace separates tokens except inside "..." and (...); a '#' outside quotes ends the line
            private static List<string> Tokens(string text)
            {
                var tokens = new List<string>();
                var current = new StringBuilder();
                var quoted = false;
                var depth = 0;
                foreach (var c in text)
                {
                    if (c == '"')
                        quoted = !quoted;
                    else if (!quoted && c == '#')
                        break;
                    else if (!quoted && c == '(')
                        depth++;
                    else if (!quoted && c == ')')
                        depth--;
                    if (!quoted && depth <= 0 && char.IsWhiteSpace(c))
                    {
                        if (current.Length > 0)
                            tokens.Add(current.ToString());
                        current.Clear();
                    }
                    else
                        current.Append(c);
                }
                if (quoted)
                    throw new InvalidDataException("a quote is not closed");
                if (depth != 0)
                    throw new InvalidDataException("a bracket is not closed");
                if (current.Length > 0)
                    tokens.Add(current.ToString());
                return tokens;
            }

            private static string Unquote(string token) =>
                token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"' ? token.Substring(1, token.Length - 2) : token;

            public void Positional(int count)
            {
                if (Positionals.Count != count)
                    throw new InvalidDataException("'" + Word + "' needs " + count + " name" + (count == 1 ? "" : "s") + " after it, not " + Positionals.Count);
            }

            public void Allow(params string[] keys)
            {
                foreach (var key in arguments.Keys)
                    if (Array.IndexOf(keys, key) < 0)
                        throw new InvalidDataException("'" + Word + "' has no " + key + "= (it takes " + string.Join(", ", keys) + ")");
            }

            public bool Has(string key) => arguments.ContainsKey(key);
            public string Value(string key) => arguments.TryGetValue(key, out var value) ? value : null;

            private string Required(string key)
            {
                if (!arguments.TryGetValue(key, out var value))
                    throw new InvalidDataException(key + "= is missing");
                return value;
            }

            public string Text(string key, bool required)
            {
                var value = required ? Required(key) : Value(key);
                return value == null ? null : Unquote(value);
            }

            public string OneOf(string key, string[] known)
            {
                var value = Text(key, true);
                if (Array.IndexOf(known, value) < 0)
                    throw new InvalidDataException(key + "=" + value + " is not one of " + string.Join(", ", known));
                return value;
            }

            private static float Float(string key, string text)
            {
                if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || float.IsNaN(number) || float.IsInfinity(number))
                    throw new InvalidDataException(key + "=" + text + " is not a number");
                return number;
            }

            public float Number(string key) => Float(key, Required(key));
            public float Number(string key, float fallback) => Has(key) ? Number(key) : fallback;

            public float NonNegative(string key)
            {
                var number = Number(key);
                if (number < 0)
                    throw new InvalidDataException(key + "=" + Value(key) + " must not be negative");
                return number;
            }

            public float NonNegative(string key, float fallback) => Has(key) ? NonNegative(key) : fallback;

            /// <summary>A number from 0 to 1.</summary>
            public float Unit(string key)
            {
                var number = Number(key);
                if (number < 0 || number > 1)
                    throw new InvalidDataException(key + "=" + Value(key) + " is not between 0 and 1");
                return number;
            }

            public float Unit(string key, float fallback) => Has(key) ? Unit(key) : fallback;

            private static float[] Numbers(string key, string text, int count)
            {
                var trimmed = text.Trim();
                if (trimmed.Length < 2 || trimmed[0] != '(' || trimmed[trimmed.Length - 1] != ')')
                    throw new InvalidDataException(key + "=" + text + " is not a point like (1, 2" + (count == 3 ? ", 3" : "") + ")");
                var parts = trimmed.Substring(1, trimmed.Length - 2).Split(',');
                if (parts.Length != count)
                    throw new InvalidDataException(key + " needs " + count + " numbers, not '" + text + "'");
                var numbers = new float[count];
                for (var i = 0; i < count; i++)
                    numbers[i] = Float(key, parts[i]);
                return numbers;
            }

            public StoryVec3 Vec3(string key)
            {
                var n = Numbers(key, Required(key), 3);
                return new StoryVec3(n[0], n[1], n[2]);
            }

            public StoryVec3? OptionalVec3(string key) => Has(key) ? Vec3(key) : (StoryVec3?)null;

            /// <summary>A scale: one number for all three axes, or a point.</summary>
            public StoryVec3? OptionalScale(string key)
            {
                if (!Has(key))
                    return null;
                var value = Value(key);
                if (value.StartsWith("(", StringComparison.Ordinal))
                    return Vec3(key);
                var s = Float(key, value);
                return new StoryVec3(s, s, s);
            }

            /// <summary>A point in the frame, each number 0 to 1.</summary>
            public StoryVec2 Vec2(string key)
            {
                var n = Numbers(key, Required(key), 2);
                if (n[0] < 0 || n[0] > 1 || n[1] < 0 || n[1] > 1)
                    throw new InvalidDataException(key + "=" + Value(key) + " is not in the frame (0 to 1 each)");
                return new StoryVec2(n[0], n[1]);
            }

            public StoryVec2? OptionalVec2(string key) => Has(key) ? Vec2(key) : (StoryVec2?)null;
        }
    }
}
