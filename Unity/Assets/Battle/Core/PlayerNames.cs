using System;
using System.Collections.Generic;
using System.Text;

namespace SdfBattle
{
    /// <summary>
    /// Thousands of online handles for the bots, so a match feels like strangers off a server and not the same eight friends every time.
    /// A few hundred are written by hand; the rest are made from word lists (an adjective and a noun, a title and a noun, a noun and a
    /// suffix), each combination given one style by a fixed hash (CamelCase, underscores, a number, a space, now and then an xX_tag_Xx), so
    /// the pool is the same on every run. Every name fits <see cref="MaxLength"/> (the scoreboard, the kill feed, the tags over heads), and no
    /// two read alike once case, spaces and underscores are ignored. Monsters, food and animals; nothing rude, nobody real, no brands.
    /// </summary>
    public static class PlayerNames
    {
        /// <summary>The longest name the HUD is laid out for.</summary>
        public const int MaxLength = 16;

        /// <summary>The share of draws that come from the hand-written handles (else the whole pool), as they read best.</summary>
        public const double HandleShare = 0.3;

        /// <summary>The hand-written handles (also in <see cref="All"/>); the cast's old names are among them.</summary>
        public static IReadOnlyList<string> Handles => HandleList;

        /// <summary>Every name, in a fixed order: the handles, then the made ones.</summary>
        public static IReadOnlyList<string> All => all ??= Build();

        private static string[] all;

        /// <summary>The key two names are the same by: letters and digits, lower case ("Sneaky_Waffle" is "SneakyWaffle").</summary>
        public static string Key(string name)
        {
            var key = new StringBuilder(name.Length);
            foreach (var c in name)
                if (char.IsLetterOrDigit(c))
                    key.Append(char.ToLowerInvariant(c));
            return key.ToString();
        }

        private static readonly string[] HandleList =
        {
            "ToastyMage", "Zed_13", "Pixelpuss", "Big Gerald", "SuzyTwoShoes", "Captain Nope", "xX_Vlad_Xx", "Grandma Gunner",
            "NoodleArms", "SirLagsAlot", "PotatoAim", "CampfireKev", "OopsAllHeadshots", "WaitWhatHuh", "Respawnin", "lagmonster",
            "GG_EZ_NOT", "SirSnackalot", "BunnyHopper", "xX_Toast_Xx", "Mr_Nobody", "JustAGoose", "404_Skill", "afk_again",
            "brb_snacks", "Dad_Reflexes", "MomSaysBedtime", "CertifiedNoob", "NotABot", "TotallyHuman", "Beep_Boop", "Pew_Pew_Pew",
            "Sir_Pewsalot", "FlagThief", "FlagBandit", "Grabby McFlag", "OneMoreGame", "Bedtime_Boss", "Snack_Attack", "NapQueen",
            "Kev_The_Brave", "BarryBlaster", "Dorothy99", "Nana_Noscope", "GrandpaGlitch", "UncleLag", "AuntieAim", "CousinCarl",
            "LilJimmy", "Big_Lou", "Steve_From_IT", "Linda_Lurks", "Moe_Mentum", "Wobblebottom", "Fizzlewick", "Bumblebutton",
            "Puddlejump", "Snorkelface", "Gigglefritz", "Crumpet_King", "Sprocket", "Widget", "Gizmo_Jr", "Doohickey",
            "Thingamabob", "Whatsisname", "NoScopeNancy", "QuickscopeQuinn", "Turbo_Tina", "Rocket_Rita", "Dizzy_Dean", "Spinny_Sam",
            "CaptainCrumbs", "MajorMayhem", "CorporalChaos", "SgtSprinkles", "Private_Pickle", "Admiral_Nap", "GeneralGoof", "Colonel_Kettle",
            "Zappo", "Kaboomer", "Splatburger", "Wham_Whammo", "Kersplat", "Yoinks42", "Yeet_Lord", "BoomBoxBob",
            "Gloopy", "Splotch", "Smudge", "Blip", "Bloop", "Zorp", "Glorp", "Fwoosh",
            "Kraken_Kid", "SlimeTime", "GoblinMode", "Gremlin_Hour", "TrollToll99", "OgreLord", "Ghoulish_Gail", "SpookyLuke",
            "Mummy_Dearest", "VampyrVic", "FangFrank", "Howl_Raiser", "YetiSpaghetti", "BigfootBetty", "NessieNess", "Moth_Man_Mike",
            "Chupacabruh", "Count_Snackula", "FrankenFrank", "Igor_Assistant", "Lady_Lurker", "SirSnoozalot", "DameDumpling", "LordFluffington",
            "Baron_Von_Nap", "Duke_Of_Dorks", "Earl_Grey_Tea", "Viscount_Vroom", "Professor_Pew", "DrWafflestein", "Dr_Sprocket", "Nurse_Nugget",
            "ChefBoomBoom", "Waiter_Wally", "Butler_Bob", "Mailman_Mo", "Plumber_Pete", "Farmer_Fran", "Janitor_Jim", "Pilot_Pam",
            "CosmicKat", "AstroAndy", "Rocket_Ron", "Moonbeam_Mo", "StarDust_Stu", "Comet_Carl", "Nebula_Neil", "Galaxy_Gus",
            "Wiggles_42", "Jiggles", "Snickerdoodle", "Butterscotch", "Gumdrop_Gwen", "Lollipop_Lou", "Jellybean_Jo", "Taffy_Tom",
            "Pancake_Pam", "Waffle_Wendy", "Muffin_Mel", "Bagel_Bert", "Pretzel_Pat", "Biscuit_Bill", "Crouton_Cal", "Nacho_Nate",
            "Turnip_Ted", "Radish_Ray", "Parsnip_Paul", "Cabbage_Cate", "Leek_Lenny", "Kale_Kyle", "Pea_Pod_Pete", "Onion_Ollie",
            "HamsterHuey", "Gerbil_Gary", "Ferret_Fred", "Otter_Otto", "Badger_Barb", "Wombat_Walt", "Sloth_Sid", "Llama_Larry",
            "Alpaca_Al", "Penguin_Percy", "Walrus_Wes", "Narwhal_Ned", "Puffin_Pip", "Toucan_Tim", "Pelican_Pete", "Goose_Gus",
            "Duckface_Dan", "Pigeon_Paul", "Owl_Oscar", "Raccoon_Rex", "Possum_Pat", "Skunk_Stan", "Moose_Mabel", "Yak_Yolanda",
            "xX_Sn1per_Xx", "xXGoblinXx", "xX_Pickle_Xx", "Xx_Shadow_xX", "xX_Mummy_Xx", "xX_Noodle_Xx", "xXx_Yeti_xXx", "xX_Moth_Xx",
            "N00bSlayer", "Pr0_Gam3r", "L33tLlama", "H4mster", "Gh0stie", "Z0mbie_Z0e", "B0rk", "W1zard_Wes",
            "QQ_More", "LOL_Nope", "ROFL_Copter", "OMG_Waffles", "BRB_Pizza", "TTYL_Tom", "IDK_Dave", "GG_Gary",
            "ItsMeAgain", "NotYourMom", "YourNeighbor", "TheMailman", "Somebody", "Anonymouse", "Mystery_Meat", "UnknownUncle",
            "CrispyBacon7", "Hashbrowny", "Toast_Boss", "Butterflaps", "Saucy_Boss", "Spud_Muffin", "Tater_Tom", "Mashed_Max",
            "Kettlecorn", "Popcorn_Pop", "Cornbread_Cal", "Gravy_Train", "Meatloaf_Mo", "Stew_Pot", "Goulash_Gus", "Chowder_Champ",
            "Zigzag", "Zipzap", "Flipflop", "Pingpong", "Hippity", "Bonkers", "Kooky", "Loopy_Lou",
            "Clanker", "Rustbucket", "Boltz", "Gearhead_Gina", "Sparkplug", "Circuit_Sue", "Servo_Sam", "Cogwheel_Jr",
            "Frostbite", "Snowcone", "Icicle_Ike", "Blizzard_Bea", "Hailstorm", "Slushie", "Sleet_Pete", "Igloo_Iggy",
            "Thunderbuns", "Stormy_Stan", "Drizzle", "Rainbow_Ray", "Foggy_Fran", "Breezy", "Twister_Tom", "Puddles",
            "Sk8_Steve99", "Skater_Sue", "Board_Brad", "Kickflip_Kim", "Grind_Gil", "Ollie_Ollie", "Halfpipe_Hal", "Wipeout_Wes",
            "Kilowatt", "Megabyte", "Gigabite", "Terabyte_Tim", "Pixel_Pete", "Bitcrusher", "Lowpoly_Lu", "Polygon_Pam",
            "Hammerhead_Hal", "Wrench_Wendy", "Screwball", "Nailbiter", "Sawdust", "Toolbox_Ted", "Drill_Sgt", "Duct_Tape_Dan",
            "Clumsy_Carl", "Oopsie_Daisy", "Whoopsie", "Tripwire", "Faceplant", "Stumbles", "Butterflop", "Splatty",
            "Quackers", "Honkers", "Moo_Moo", "Oinkers", "Baa_Baa_Bob", "Neigh_Neigh", "Cluck_Cluck", "Woof_Woof",
            "Grumbles", "Mumbles", "Snores", "Yawns", "Sniffles", "Hiccups", "Burpington", "Achoo",
        };

        private static readonly string[] Adjectives =
        {
            "Sneaky", "Grumpy", "Sleepy", "Fuzzy", "Spicy", "Crispy", "Soggy", "Salty", "Angry", "Happy",
            "Lazy", "Crazy", "Cosmic", "Atomic", "Turbo", "Mega", "Ultra", "Hyper", "Super", "Tiny",
            "Giant", "Mighty", "Silent", "Rapid", "Fluffy", "Wobbly", "Jolly", "Funky", "Groovy", "Cranky",
            "Bouncy", "Shiny", "Rusty", "Frosty", "Toasty", "Stealthy", "Sassy", "Spooky", "Creepy", "Glitchy",
            "Neon", "Shadow", "Electric", "Furious", "Feral", "Wild", "Lucky", "Brave", "Clumsy", "Dizzy",
            "Goofy", "Nervous", "Noble", "Royal", "Rogue", "Swift", "Zesty", "Tangy", "Chunky", "Chonky",
            "Smol", "Epic", "Feisty", "Grim", "Gloomy", "Haunted", "Cursed", "Iron", "Golden", "Silver",
            "Crimson", "Purple", "Sparkly", "Squishy", "Speedy", "Sticky", "Stinky", "Wiggly", "Moody", "Quirky",
        };

        private static readonly string[] Nouns =
        {
            "Waffle", "Pancake", "Noodle", "Pickle", "Taco", "Burrito", "Nacho", "Muffin", "Cupcake", "Donut",
            "Pretzel", "Biscuit", "Toast", "Bagel", "Potato", "Turnip", "Radish", "Carrot", "Broccoli", "Mushroom",
            "Truffle", "Meatball", "Dumpling", "Pudding", "Jelly", "Cookie", "Cheese", "Bacon", "Mango", "Banana",
            "Pumpkin", "Popcorn", "Sprout", "Gnome", "Goblin", "Troll", "Ogre", "Zombie", "Ghost", "Ghoul",
            "Vampire", "Werewolf", "Mummy", "Yeti", "Kraken", "Cyclops", "Gremlin", "Imp", "Banshee", "Wraith",
            "Golem", "Dragon", "Wyvern", "Hydra", "Bigfoot", "Slime", "Blob", "Alien", "Robot", "Cyborg",
            "Pirate", "Wizard", "Knight", "Viking", "Penguin", "Llama", "Alpaca", "Badger", "Otter", "Ferret",
            "Hamster", "Gerbil", "Panda", "Koala", "Sloth", "Walrus", "Narwhal", "Platypus", "Wombat", "Moose",
            "Goose", "Duck", "Pigeon", "Owl", "Raccoon", "Possum", "Weasel", "Gecko", "Iguana", "Turtle",
            "Lobster", "Shrimp", "Squid", "Octopus", "Hedgehog", "Bunny", "Kitten", "Puppy", "Hippo", "Rhino",
            "Yak", "Goat", "Toad", "Frog", "Newt", "Beetle", "Moth", "Bat", "Crab", "Shark",
            "Cactus", "Toaster", "Spoon", "Sock", "Teapot", "Kettle", "Pebble", "Comet", "Rocket", "Tornado",
        };

        private static readonly string[] Titles =
        {
            "Captain", "Sir", "Lord", "Lady", "Dr", "Mr", "Mrs", "Lil", "Big", "Agent",
            "Count", "Baron", "King", "Queen", "Uncle", "Auntie", "Grandma", "Grandpa", "Major", "General",
            "Chef", "Officer", "Professor", "Duke", "Sgt",
        };

        private static readonly string[] Suffixes =
        {
            "Master", "Slayer", "Lord", "Zilla", "Tron", "Bot", "Juggler", "Wizard", "Hunter", "Gamer",
            "Boss", "Tamer", "Fan", "Hugger", "Smash", "Blaster", "Rider", "Champ", "Wrangler", "Whisperer",
        };

        /// <summary>Pets nobody hunts or slays, even in a name: no "PuppySlayer".</summary>
        private static readonly string[] Cute = { "Puppy", "Kitten", "Bunny", "Hamster", "Gerbil", "Panda", "Koala", "Penguin", "Otter", "Hedgehog" };

        private static readonly string[] Fierce = { "Slayer", "Hunter", "Smash", "Blaster" };

        /// <summary>The numbers handles carry; none with a meaning beyond being a number someone liked.</summary>
        private static readonly string[] Numbers =
        {
            "7", "9", "11", "12", "13", "21", "22", "23", "24", "27", "33", "42", "47", "55", "64", "77", "99",
            "101", "123", "202", "303", "360", "404", "500", "707", "777", "808", "909", "999", "1000", "1234",
            "2000", "3000", "9000", "1337", "007", "2k", "x2",
        };

        private static readonly string[] Tags = { "GG", "TV", "YT", "HD", "Pro", "Jr", "OG", "XD", "Live", "Plays" };

        /// <summary>Real characters, brands and the like the word lists could spell by chance (as <see cref="Key"/>s, matched anywhere in a name).</summary>
        private static readonly string[] Blocked =
        {
            "greengoblin", "droctopus", "doctoroctopus", "cookiemonster", "captaintoad", "crazyfrog", "ninjaturtle", "mrpotato",
            "mrpickle", "mrspotato", "shadowhedgehog", "burgerking", "hungryhippo", "sonichedgehog", "ironman", "kingshark", "captainhook",
            "ghostrider", "mrrobot", "mightyduck", "goldenknight", "atomickitten", "toastmaster",
        };

        private static string[] Build()
        {
            var names = new List<string>();
            var keys = new HashSet<string>();

            void Add(string name)
            {
                if (name.Length == 0 || name.Length > MaxLength || name == Cast.You)
                    return;
                var key = Key(name);
                foreach (var blocked in Blocked)
                    if (key.Contains(blocked))
                        return;
                if (keys.Add(key))
                    names.Add(name);
            }

            foreach (var handle in HandleList)
                Add(handle);
            for (var a = 0; a < Adjectives.Length; a++)
                for (var n = 0; n < Nouns.Length; n++)
                    Add(Styled(Adjectives[a], Nouns[n], Hash(1, a, n)));
            for (var t = 0; t < Titles.Length; t++)
                for (var n = 0; n < Nouns.Length; n++)
                    Add(Titled(Titles[t], Nouns[n], Hash(2, t, n)));
            for (var n = 0; n < Nouns.Length; n++)
                for (var s = 0; s < Suffixes.Length; s++)
                    if (!(Array.IndexOf(Cute, Nouns[n]) >= 0 && Array.IndexOf(Fierce, Suffixes[s]) >= 0))
                        Add(Styled(Nouns[n], Suffixes[s], Hash(3, n, s)));
            // A noun on its own, with a number, or (rarely) dressed up as a tag
            for (var n = 0; n < Nouns.Length; n++)
            {
                var hash = Hash(4, n, 0);
                Add(Nouns[n] + Pick(Numbers, hash));
                Add(Nouns[n].ToLowerInvariant() + "_" + Pick(Numbers, hash >> 8));
                if (hash % 3 == 0)
                    Add($"xX_{Nouns[n]}_Xx");
            }
            return names.ToArray();
        }

        /// <summary>Two words in one of the styles: mostly CamelCase, else with a number, underscores, a space or a tag.</summary>
        private static string Styled(string first, string second, uint hash)
        {
            var number = Pick(Numbers, hash >> 8);
            switch (hash % 16)
            {
                case 0: case 1: case 2: case 3: case 4: return first + second;
                case 5: case 6: return first + second + number;
                case 7: return first.ToLowerInvariant() + "_" + second.ToLowerInvariant();
                case 8: return first + "_" + second;
                case 9: return first + "_" + second + number;
                case 10: return (first + second).ToLowerInvariant() + number;
                case 11: return first + " " + second;
                case 12: return "The" + first + second;
                case 13: return first + second + Pick(Tags, hash >> 16);
                case 14: return first + second + "_" + number;
                default: return hash % 64 == 15 ? $"xX_{first}{second}_Xx" : first + second;
            }
        }

        /// <summary>A title and a noun: "Captain Waffle", "DrWaffle", "lil_waffle", "SirWaffle7".</summary>
        private static string Titled(string title, string noun, uint hash)
        {
            switch (hash % 6)
            {
                case 0: case 1: return title + " " + noun;
                case 2: return title + noun;
                case 3: return title + "_" + noun;
                case 4: return title + noun + Pick(Numbers, hash >> 8);
                default: return (title + "_" + noun).ToLowerInvariant();
            }
        }

        private static string Pick(string[] from, uint hash) => from[hash % (uint)from.Length];

        /// <summary>A fixed integer hash (not <see cref="string.GetHashCode"/>, which may change between runs).</summary>
        private static uint Hash(int list, int i, int j)
        {
            unchecked
            {
                var h = (uint)list * 0x9E3779B1u ^ (uint)i * 0x85EBCA77u ^ (uint)j * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }
    }

    /// <summary>
    /// The names of one match's players: each bot draws one at random from <see cref="PlayerNames"/>, never one already in the match
    /// (yours and any the setup gives are reserved first). The same <see cref="Random"/> seed draws the same names.
    /// </summary>
    public sealed class NameCast
    {
        private readonly Random random;
        private readonly HashSet<string> used = new HashSet<string>();
        private int fallbacks;

        public NameCast(Random random = null) => this.random = random ?? new Random();

        /// <summary>Takes <paramref name="name"/> out of the draw (it is already someone's in this match).</summary>
        public NameCast Reserve(string name)
        {
            if (!string.IsNullOrEmpty(name))
                used.Add(PlayerNames.Key(name));
            return this;
        }

        public NameCast Reserve(IEnumerable<string> names)
        {
            if (names != null)
                foreach (var name in names)
                    Reserve(name);
            return this;
        }

        /// <summary>A name nobody in the match has yet, now taken.</summary>
        public string Draw()
        {
            var from = random.NextDouble() < PlayerNames.HandleShare ? PlayerNames.Handles : PlayerNames.All;
            // The pool is thousands against a dozen players: a few tries find a free one, the walk after is for an exhausted pool
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var name = from[random.Next(from.Count)];
                if (used.Add(PlayerNames.Key(name)))
                    return name;
            }
            var all = PlayerNames.All;
            var start = random.Next(all.Count);
            for (var i = 0; i < all.Count; i++)
            {
                var name = all[(start + i) % all.Count];
                if (used.Add(PlayerNames.Key(name)))
                    return name;
            }
            string plain;
            do
                plain = $"Player {++fallbacks}";
            while (!used.Add(PlayerNames.Key(plain)));
            return plain;
        }
    }
}
