using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>Where the menus' camera stands in 42's room (ticket 35): the camera moves, the scene does not change.</summary>
    public enum MenuShot
    {
        /// <summary>The Title (and the cards over it: Settings, the difficulty card, Confirm): desk, monitor and the wall behind, all in sight.</summary>
        Wide,
        /// <summary>Level Select: pushed in toward the monitor.</summary>
        Monitor,
        /// <summary>The Story gallery, the credits: close on the wall of comic panels.</summary>
        Wall,
    }

    /// <summary>A camera's place: where it stands, what it looks at and its vertical field of view.</summary>
    public readonly struct ShotPose
    {
        public readonly Vector3 position, target;
        public readonly float fov;

        public ShotPose(Vector3 position, Vector3 target, float fov)
        {
            this.position = position;
            this.target = target;
            this.fov = fov;
        }

        public static ShotPose Lerp(ShotPose a, ShotPose b, float t) =>
            new ShotPose(Vector3.Lerp(a.position, b.position, t), Vector3.Lerp(a.target, b.target, t), Mathf.Lerp(a.fov, b.fov, t));
    }

    /// <summary>The camera poses (Unity's frame: the room's back wall is at z = -1.5, the desk at x about +0.6), the slow drift and the move between them.</summary>
    public static class MenuShots
    {
        /// <summary>Half the way there in this many seconds.</summary>
        public const float HalfLife = 0.28f;

        public static ShotPose Pose(MenuShot shot)
        {
            switch (shot)
            {
                case MenuShot.Monitor:
                    return new ShotPose(new Vector3(0.5f, 1.0f, -0.25f), new Vector3(0.45f, 0.9f, -1.3f), 40);
                case MenuShot.Wall:
                    return new ShotPose(new Vector3(-0.25f, 1.45f, 0.5f), new Vector3(-0.25f, 1.45f, -1.5f), 46);
                default:
                    return new ShotPose(new Vector3(-1.15f, 1.6f, 3.1f), new Vector3(-0.95f, 1.0f, -1.4f), 42);
            }
        }

        /// <summary>
        /// The slow drift at <paramref name="seconds"/> (unscaled time): a few centimetres sideways and up, a few degrees' worth of target
        /// sway, never repeating in an obvious beat. Added to both the position and the target so the view slides rather than turns.
        /// </summary>
        public static Vector3 Drift(float seconds) =>
            new Vector3(Mathf.Sin(seconds * 0.17f) * 0.07f + Mathf.Sin(seconds * 0.071f + 1f) * 0.04f, Mathf.Sin(seconds * 0.13f + 2f) * 0.025f, 0);

        /// <summary>How much of the way to the target pose a frame of <paramref name="deltaSeconds"/> covers (frame rate independent).</summary>
        public static float Follow(float deltaSeconds) => 1 - Mathf.Pow(0.5f, Mathf.Max(0, deltaSeconds) / HalfLife);
    }

    /// <summary>One thing 42 puts on the desk after an arena is won.</summary>
    public sealed class DeskProp
    {
        /// <summary>The battle step that earns it.</summary>
        public readonly string arena;
        /// <summary>The home-room model's prefab name (Assets/Mushrooms/&lt;model&gt;.prefab).</summary>
        public readonly string model;
        /// <summary>Where it stands on the desk, Unity's frame, metres.</summary>
        public readonly Vector3 at;
        public readonly float yaw;
        /// <summary>The height it is scaled to, metres (the models are made for their own levels' scales).</summary>
        public readonly float height;

        public DeskProp(string arena, string model, Vector3 at, float yaw, float height)
        {
            this.arena = arena;
            this.model = model;
            this.at = at;
            this.yaw = yaw;
            this.height = height;
        }
    }

    /// <summary>What the desk shows of the campaign: a small prop per arena won (only models that exist), and the trophy's gold once it is complete.</summary>
    public static class DeskProgress
    {
        // The desk's top is y = 0.55 (the desk is stretched to 1.8 m, x -0.3 to 1.5, so that there is room). The monitor stands at x +0.05 to +0.85, the lamp
        // and the snack bowl at x +1.0; the free spots are two corners of two props deep, either side of them
        public static readonly IReadOnlyList<DeskProp> Props = new[]
        {
            new DeskProp(CampaignDefinition.Meadow, "flower0", new Vector3(-0.22f, 0.55f, -1.32f), 0, 0.2f),
            new DeskProp(CampaignDefinition.Fungal, "puffball0", new Vector3(-0.08f, 0.55f, -1.32f), 0, 0.1f),
            new DeskProp(CampaignDefinition.Chessboard, "knight-light", new Vector3(-0.22f, 0.55f, -1.02f), 200, 0.18f),
            new DeskProp(CampaignDefinition.ToyBox, "alpha-block0", new Vector3(-0.08f, 0.55f, -1.02f), 25, 0.08f),
            new DeskProp(CampaignDefinition.Kitchen, "cookie0", new Vector3(1.28f, 0.55f, -1.02f), 0, 0.04f),
            new DeskProp(CampaignDefinition.Bathroom, "rubber-duck0", new Vector3(1.42f, 0.55f, -1.02f), 160, 0.09f),
            new DeskProp(CampaignDefinition.Computer, "ram-bank-red", new Vector3(1.28f, 0.55f, -1.32f), 90, 0.1f),
            new DeskProp(CampaignDefinition.Boss, "prime-crown", new Vector3(1.42f, 0.55f, -1.32f), 15, 0.1f),
        };

        /// <summary>The props of the arenas won, in the order of <see cref="Props"/>.</summary>
        public static List<DeskProp> Earned(CampaignProgress progress)
        {
            var earned = new List<DeskProp>();
            if (progress == null)
                return earned;
            foreach (var prop in Props)
                if (progress.Won(prop.arena))
                    earned.Add(prop);
            return earned;
        }

        /// <summary>Whether the trophy is gold: the campaign is complete (the title's crown shows on the same fact). Before, it is silver, second place's colour.</summary>
        public static bool TrophyGold(CampaignProgress progress, bool previewComplete = false) => previewComplete || progress != null && progress.Completed;
    }

    /// <summary>The colour of the wall's light and of the flat fallback's tint, warmer with every arena won.</summary>
    public static class WallTint
    {
        private static readonly Color Start = new Color(0.55f, 0.5f, 0.85f), Middle = new Color(0.8f, 0.7f, 0.85f), End = new Color(1f, 0.85f, 0.5f);

        /// <summary>From cool violet at the start through lilac to gold at <paramref name="fraction"/> 1 (arenas won over arenas).</summary>
        public static Color For(float fraction)
        {
            var t = Mathf.Clamp01(fraction);
            return t < 0.5f ? Color.Lerp(Start, Middle, t * 2) : Color.Lerp(Middle, End, t * 2 - 1);
        }

        public static float Fraction(CampaignDefinition definition, CampaignProgress progress)
        {
            var total = 0;
            var won = 0;
            foreach (var battle in definition.Battles())
            {
                total++;
                if (progress.Won(battle.id))
                    won++;
            }
            return total == 0 ? 0 : won / (float)total;
        }
    }

    /// <summary>Which of the menu's worlds is drawn.</summary>
    public enum BackdropMode
    {
        /// <summary>The 3D desk diorama.</summary>
        Desk,
        /// <summary>The panel wall alone as flat UI with a slow parallax: the cheap one.</summary>
        FlatWall,
    }

    /// <summary>The player's choice of backdrop and the rule behind Auto.</summary>
    public static class BackdropChoice
    {
        public const int Auto = 0, ForceDesk = 1, ForceFlat = 2;
        public static readonly string[] Names = { "Auto", "3D desk", "Flat wall" };

        /// <summary>
        /// Auto is the desk, except on the lowest quality level (ultra low) and where the desk's models are not there (a build made without
        /// the meshes); the others are what they say, though the desk is still only drawn where it exists.
        /// </summary>
        public static BackdropMode Resolve(int choice, bool lowestQuality, bool deskExists)
        {
            if (!deskExists || choice == ForceFlat)
                return BackdropMode.FlatWall;
            if (choice == ForceDesk)
                return BackdropMode.Desk;
            return lowestQuality ? BackdropMode.FlatWall : BackdropMode.Desk;
        }
    }

    /// <summary>One card on the wall: a comic panel, or a blank sheet where a comic is not seen yet.</summary>
    public readonly struct WallCard
    {
        /// <summary>The comic's name, or null for a blank sheet.</summary>
        public readonly string comic;
        /// <summary>The panel, from 0.</summary>
        public readonly int panel;
        /// <summary>The card's centre on the wall, metres from the wall's centre (x right, y up), and its tilt in degrees.</summary>
        public readonly Vector2 centre;
        public readonly float tilt;

        public WallCard(string comic, int panel, Vector2 centre, float tilt)
        {
            this.comic = comic;
            this.panel = panel;
            this.centre = centre;
            this.tilt = tilt;
        }

        public bool Blank => comic == null;
    }

    /// <summary>Which comics the player has seen, in the story's order, from the save (the intro and the beats; the ending too).</summary>
    public static class WallContent
    {
        /// <summary>The comics' names (steps' ids) seen in the campaign, in order, that have panels (<paramref name="panelCount"/> says how many; 0 = none).</summary>
        public static List<string> SeenComics(CampaignDefinition definition, CampaignProgress progress, Func<string, int> panelCount)
        {
            var seen = new List<string>();
            foreach (var step in definition.Steps)
                if (!step.IsBattle && step.kind != StepKind.Credits && progress.Seen(step.id) && panelCount(step.id) > 0)
                    seen.Add(step.id);
            return seen;
        }
    }

    /// <summary>
    /// Lays the cards out on the wall: a loose grid, every card a little off its place and tilted a few degrees, deterministic (the same
    /// save always gives the same wall). The seen panels go first, in story order; blank sheets fill the places left, so the wall is never bare
    /// and never tells how much story is still to come, only that there is room.
    /// </summary>
    public static class WallLayout
    {
        public const int Columns = 8, Rows = 6;
        public const float CardWidth = 0.42f, CardHeight = CardWidth * 9 / 16f;
        public const float CellWidth = 0.485f, CellHeight = 0.275f;
        /// <summary>Panels seen from this many on are thinned to the capacity, evenly.</summary>
        public const int Capacity = Columns * Rows;
        /// <summary>The fewest sheets the wall shows (blank ones make them up).</summary>
        public const int MinimumSheets = 18;

        /// <summary>
        /// <paramref name="comics"/> are (name, panel count) in story order. <paramref name="free"/> says whether a place (its centre, metres)
        /// may take a card (false behind the monitor, say). Panels beyond the wall's room are left out evenly from each comic.
        /// </summary>
        public static List<WallCard> Build(IReadOnlyList<KeyValuePair<string, int>> comics, Func<Vector2, bool> free = null)
        {
            var places = new List<Vector2>();
            for (var row = 0; row < Rows; row++)
            for (var column = 0; column < Columns; column++)
            {
                var centre = new Vector2((column - (Columns - 1) / 2f) * CellWidth, ((Rows - 1) / 2f - row) * CellHeight);
                if (free == null || free(centre))
                    places.Add(centre);
            }

            var panels = new List<KeyValuePair<string, int>>();
            var total = 0;
            foreach (var comic in comics)
                total += comic.Value;
            var room = places.Count;
            var keep = total <= room ? 1f : room / (float)total;
            foreach (var comic in comics)
            {
                var take = total <= room ? comic.Value : Mathf.Max(1, Mathf.RoundToInt(comic.Value * keep));
                for (var i = 0; i < take; i++)
                    panels.Add(new KeyValuePair<string, int>(comic.Key, take == comic.Value ? i : Mathf.Min(comic.Value - 1, Mathf.RoundToInt(i * (comic.Value - 1) / (float)Mathf.Max(1, take - 1)))));
            }
            if (panels.Count > room)
                panels.RemoveRange(room, panels.Count - room);

            var sheets = Mathf.Min(room, Mathf.Max(panels.Count, MinimumSheets));
            var cards = new List<WallCard>(sheets);
            // The places in a fixed shuffle, so seen panels are scattered over the wall rather than filling it from the top
            var order = Shuffled(places.Count);
            var chosen = new List<int>();
            for (var i = 0; i < sheets; i++)
                chosen.Add(order[i]);
            chosen.Sort();
            for (var i = 0; i < chosen.Count; i++)
            {
                var place = places[chosen[i]];
                var h1 = Hash(chosen[i] * 2 + 1);
                var h2 = Hash(chosen[i] * 2 + 2);
                var h3 = Hash(chosen[i] * 2 + 3);
                var centre = place + new Vector2((h1 - 0.5f) * 0.05f, (h2 - 0.5f) * 0.035f);
                var tilt = (h3 - 0.5f) * 14f;
                if (i < panels.Count)
                    cards.Add(new WallCard(panels[i].Key, panels[i].Value, centre, tilt));
                else
                    cards.Add(new WallCard(null, 0, centre, tilt));
            }
            return cards;
        }

        /// <summary>0 to 1, the same for the same number.</summary>
        public static float Hash(int n)
        {
            unchecked
            {
                var x = (uint)n * 2654435761u + 0x9E3779B9u;
                x ^= x >> 15;
                x *= 2246822519u;
                x ^= x >> 13;
                x *= 3266489917u;
                x ^= x >> 16;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static int[] Shuffled(int count)
        {
            var order = new int[count];
            for (var i = 0; i < count; i++)
                order[i] = i;
            for (var i = count - 1; i > 0; i--)
            {
                var j = (int)(Hash(i + 977) * (i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }
    }

    /// <summary>The thumbnails the wall draws: one atlas per comic (a grid of panels, four across) and the index that says how many panels each holds.</summary>
    public static class WallAtlas
    {
        public const string Folder = "Wall";
        public const string IndexName = "wall-index";
        public const int Columns = 4, ThumbWidth = 384, ThumbHeight = 216;

        /// <summary>The index file's text: "comic panels" per line.</summary>
        public static Dictionary<string, int> ParseIndex(string text)
        {
            var counts = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(text))
                return counts;
            foreach (var raw in text.Split('\n'))
            {
                var parts = raw.Trim().Split(' ');
                if (parts.Length == 2 && int.TryParse(parts[1], out var count) && count > 0)
                    counts[parts[0]] = count;
            }
            return counts;
        }

        public static string WriteIndex(IReadOnlyList<KeyValuePair<string, int>> counts)
        {
            var text = "";
            foreach (var pair in counts)
                text += pair.Key + " " + pair.Value + "\n";
            return text;
        }

        /// <summary>A panel's rectangle in its comic's atlas, in UV (x, y, width, height; v up, so the first row is the top).</summary>
        public static Rect Cell(int panel, int count)
        {
            var rows = Mathf.Max(1, (count + Columns - 1) / Columns);
            var column = panel % Columns;
            var row = panel / Columns;
            return new Rect(column / (float)Columns, (rows - 1 - row) / (float)rows, 1f / Columns, 1f / rows);
        }

        public static int Rows(int count) => Mathf.Max(1, (count + Columns - 1) / Columns);
    }
}
