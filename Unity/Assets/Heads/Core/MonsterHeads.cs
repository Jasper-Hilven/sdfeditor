using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfHeads
{
    /// <summary>One monster head (ticket 45): its id, the month it is the head of, its name, where its prefab is, and the colour of its blood (ticket 51).</summary>
    public readonly struct MonsterHead
    {
        public MonsterHead(string id, int month, string displayName, Color blood, bool built)
        {
            Id = id;
            Month = month;
            DisplayName = displayName;
            Blood = blood;
            Built = built;
        }

        /// <summary>The model's name in content/models/characters/monsters/&lt;id&gt;.sdf: yeti, vampire, ..., santa.</summary>
        public string Id { get; }

        /// <summary>1 (January) to 12 (December): the month it is the head of.</summary>
        public int Month { get; }

        public string DisplayName { get; }

        /// <summary>The colour its burst and blood take (ticket 51's table; never a realistic red, distinct from the other 11).</summary>
        public Color Blood { get; }

        /// <summary>Whether the model exists yet (ticket 45 builds them in pairs, the Yeti and the Jack-o'-Lantern first). The prefab path of one that does not is where it will be.</summary>
        public bool Built { get; }

        /// <summary>The prefab the toon importer makes from the model's mesh (<see cref="MonsterHeads.PrefabFolder"/>); with LODs.</summary>
        public string PrefabPath => $"{MonsterHeads.PrefabFolder}/{Id}.prefab";

        /// <summary>The same model meshed with one level of detail only (the <c>_fast</c> variant).</summary>
        public string FastPrefabPath => $"{MonsterHeads.PrefabFolder}/{Id}_fast.prefab";

        /// <summary>The model file, relative to the repository root.</summary>
        public string ModelPath => $"content/models/characters/monsters/{Id}.sdf";
    }

    /// <summary>
    /// The twelve monster heads, one per month (ticket 45): the one table that tickets 46 (explosions), 47 (selection), 48 (the next-level
    /// card) and 50 (the scoreboard icons) read. Order is the calendar's, January first. Tickets 46 and 51 read the blood colours.
    /// <code>var head = MonsterHeads.ById("yeti");   var october = MonsterHeads.OfMonth(10);</code>
    /// </summary>
    public static class MonsterHeads
    {
        /// <summary>Where the toon importer puts a model's prefab (MushroomBuilder.PrefabFolder).</summary>
        public const string PrefabFolder = "Assets/Mushrooms";

        static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1);

        /// <summary>Every head, January to December. The blood colours are ticket 51's proposals, which the user may change.</summary>
        public static readonly IReadOnlyList<MonsterHead> All = new[]
        {
            new MonsterHead("yeti", 1, "Abominable Snowman", Rgb(0.70f, 0.88f, 1.00f), true),          // pale ice blue
            new MonsterHead("vampire", 2, "Vampire Count", Rgb(0.38f, 0.08f, 0.52f), true),            // deep purple
            new MonsterHead("leprechaun", 3, "Leprechaun Mutant", Rgb(0.07f, 0.72f, 0.35f), true),     // emerald green
            new MonsterHead("swamp", 4, "Swamp Thing", Rgb(0.30f, 0.26f, 0.08f), true),                // dark olive brown
            new MonsterHead("cyclops", 5, "Raging Cyclops", Rgb(0.80f, 0.55f, 0.10f), true),           // ochre
            new MonsterHead("cthulhu", 6, "Deep-Sea Cthulhu", Rgb(0.00f, 0.50f, 0.55f), true),         // ocean teal
            new MonsterHead("alien", 7, "Acid Alien", Rgb(0.85f, 1.00f, 0.10f), true),                 // glowing acid yellow
            new MonsterHead("mummy", 8, "Ancient Mummy", Rgb(0.74f, 0.70f, 0.60f), false),              // sand-grey dust
            new MonsterHead("werewolf", 9, "Feral Werewolf", Rgb(0.12f, 0.12f, 0.14f), false),          // charcoal black
            new MonsterHead("pumpkin", 10, "Glowing Jack-o'-Lantern", Rgb(1.00f, 0.50f, 0.00f), true),  // pumpkin orange
            new MonsterHead("frankenstein", 11, "Frankenstein's Monster", Rgb(0.55f, 0.90f, 0.10f), false), // lime green
            new MonsterHead("santa", 12, "Crazed Evil Santa", Rgb(1.00f, 0.45f, 0.70f), false),         // candy pink
        };

        public static int Count => All.Count;

        /// <summary>The head with this id, or null (a null id too).</summary>
        public static MonsterHead? ById(string id)
        {
            foreach (var head in All)
                if (head.Id == id)
                    return head;
            return null;
        }

        /// <summary>The head of a month, 1 to 12, or null.</summary>
        public static MonsterHead? OfMonth(int month) => month >= 1 && month <= All.Count ? All[month - 1] : (MonsterHead?)null;

        /// <summary>The ids of the heads that are modelled, in calendar order.</summary>
        public static IReadOnlyList<string> BuiltIds()
        {
            var ids = new List<string>();
            foreach (var head in All)
                if (head.Built)
                    ids.Add(head.Id);
            return ids;
        }

        /// <summary>
        /// A random modelled head's id: the stand-in until ticket 47 chooses (the head of the month and the ones unlocked for you, random
        /// ones for the bots). <paramref name="random"/> gives a number in [0, count).
        /// </summary>
        public static string RandomBuiltId(Func<int, int> random)
        {
            var ids = BuiltIds();
            return ids.Count == 0 ? null : ids[Mathf.Clamp(random(ids.Count), 0, ids.Count - 1)];
        }
    }
}
