using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SdfStory.Editor
{
    /// <summary>
    /// content/scenes/home.txt, read: the models, actors, lights and camera of 42's room (ticket 31.3), in the room's own frame, which is
    /// the pipeline's (x is mirrored when <see cref="HomeBuilder"/> puts them in the scene, as <c>LevelBuilder</c> does). Plain data and
    /// no Unity objects but vectors and colours, so that it can be read without building the scene.
    /// </summary>
    public sealed class HomePlacement
    {
        public sealed class Item
        {
            /// <summary>"model" or "actor".</summary>
            public string Kind;
            public string Model;
            public string Name;
            public Vector3 At;
            public float Yaw;
            public float Scale = 1;
            public int Line;
        }

        public sealed class LightItem
        {
            public string Name;
            public Vector3 At;
            public Color Color;
            public float Intensity, Range;
        }

        public readonly List<Item> Items = new List<Item>();
        public readonly List<LightItem> Lights = new List<LightItem>();
        public Vector3 CameraAt, CameraLook;
        public float CameraFov = 60;
        public Color Ambient = new Color(0.16f, 0.13f, 0.22f);

        private static readonly Regex Command = new Regex(@"^(\w+)\s*(.*)$");
        private static readonly Regex Argument = new Regex(@"(\w+)=(\([^)]*\)|""[^""]*""|[^\s]+)");

        public static HomePlacement Load(string path)
        {
            var placement = new HomePlacement();
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var text = lines[i].Trim();
                if (text.Length == 0 || text[0] == '#')
                    continue;
                try
                {
                    placement.Parse(text, i + 1);
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException($"{path} line {i + 1}: {exception.Message}");
                }
            }
            return placement;
        }

        private void Parse(string text, int line)
        {
            var command = Command.Match(text);
            var word = command.Groups[1].Value;
            var rest = command.Groups[2].Value;
            if (word == "format")
                return;
            // key=value arguments; what is left of the line is the name after the command (model desk, light Lamp point)
            var arguments = new Dictionary<string, string>();
            foreach (Match match in Argument.Matches(rest))
                arguments[match.Groups[1].Value] = match.Groups[2].Value.Trim('"');
            var head = Argument.Replace(rest, "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (word)
            {
                case "model":
                case "actor":
                    Items.Add(new Item
                    {
                        Kind = word,
                        Model = head[0],
                        Name = arguments.TryGetValue("name", out var name) ? name : head[0],
                        At = Vec(arguments, "at"),
                        Yaw = arguments.ContainsKey("yaw") ? Number(arguments["yaw"]) : 0,
                        Scale = arguments.ContainsKey("scale") ? Number(arguments["scale"]) : 1,
                        Line = line,
                    });
                    break;
                case "light":
                    var colour = Vec(arguments, "color");
                    Lights.Add(new LightItem
                    {
                        Name = head[0],
                        At = Vec(arguments, "at"),
                        Color = new Color(colour.x, colour.y, colour.z),
                        Intensity = Number(arguments["intensity"]),
                        Range = Number(arguments["range"]),
                    });
                    break;
                case "camera":
                    CameraAt = Vec(arguments, "at");
                    CameraLook = Vec(arguments, "look");
                    CameraFov = Number(arguments["fov"]);
                    break;
                case "ambient":
                    var ambient = Vec(arguments, "color");
                    Ambient = new Color(ambient.x, ambient.y, ambient.z);
                    break;
                default:
                    throw new InvalidDataException($"unknown command '{word}'");
            }
        }

        private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        private static Vector3 Vec(Dictionary<string, string> arguments, string key)
        {
            if (!arguments.TryGetValue(key, out var text))
                throw new InvalidDataException($"{key}=(x, y, z) is missing");
            var numbers = text.Trim('(', ')').Split(',');
            if (numbers.Length != 3)
                throw new InvalidDataException($"{key} needs three numbers, not '{text}'");
            return new Vector3(Number(numbers[0]), Number(numbers[1]), Number(numbers[2]));
        }
    }
}
