using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>
    /// The words for deaths: every pool has lines, gore off shows none of the gory ones, picks do not repeat the last few, every line
    /// formats with its placeholders and fits its box, and deaths get the kind their lines are for.
    /// </summary>
    public sealed class DeathLinesTests
    {
        private static readonly DeathKind[] Kinds = (DeathKind[])Enum.GetValues(typeof(DeathKind));
        private static readonly DeathText[] Texts = (DeathText[])Enum.GetValues(typeof(DeathText));

        /// <summary>The kinds with a killer: their feed lines name both and place the icon.</summary>
        private static bool HasKiller(DeathKind kind) => kind != DeathKind.Fall;

        private static IEnumerable<(DeathKind kind, DeathText text, DeathLine line)> All() =>
            from kind in Kinds from text in Texts from line in DeathLines.Lines(kind, text) select (kind, text, line);

        // ---------------------------------------------------------------- The pools

        [Test]
        public void EveryPool_HasLines_AlsoWithGoreOff()
        {
            foreach (var kind in Kinds)
            foreach (var text in Texts)
            {
                var lines = DeathLines.Lines(kind, text);
                Assert.Greater(lines.Count, 0, $"{kind} {text}");
                Assert.IsTrue(lines.Any(l => l.Gore == GoreRules.Off), $"{kind} {text} has a line for gore off");
            }
        }

        [Test]
        public void TheMainKinds_HaveManyLines()
        {
            foreach (var kind in Kinds)
            {
                Assert.GreaterOrEqual(DeathLines.Lines(kind, DeathText.Feed).Count, 7, $"{kind} feed");
                Assert.GreaterOrEqual(DeathLines.Lines(kind, DeathText.Title).Count + DeathLines.Lines(kind, DeathText.Detail).Count, 8, $"{kind} screen");
            }
            Assert.GreaterOrEqual(All().Count(), 100);
        }

        // ---------------------------------------------------------------- Picking

        [Test]
        public void APick_IsNotOneOfTheLastFew()
        {
            var picker = new DeathLines(7) { GenericShare = 0 };
            foreach (var kind in Kinds)
            foreach (var text in Texts)
            {
                var distinct = DeathLines.Lines(kind, text).Select(l => l.Text).Distinct().Count();
                var window = Math.Min(DeathLines.Recent, distinct - 1);
                var picks = new List<string>();
                for (var i = 0; i < 200; i++)
                {
                    var pick = picker.Pick(kind, text, GoreRules.UltraGore);
                    var recent = picks.Skip(Math.Max(0, picks.Count - window)).ToList();
                    CollectionAssert.DoesNotContain(recent, pick, $"{kind} {text}, pick {i}");
                    picks.Add(pick);
                }
                Assert.Greater(picks.Distinct().Count(), window, $"{kind} {text} varies");
            }
        }

        [Test]
        public void GoreOff_NeverPicksAGoryLine()
        {
            var safe = new HashSet<string>(All().Where(l => l.line.Gore == GoreRules.Off).Select(l => l.line.Text))
            {
                DeathLines.PlainFeed, DeathLines.PlainFallFeed,
            };
            var gory = All().Where(l => l.line.Gore > GoreRules.Off).Select(l => l.line.Text).ToList();
            Assert.IsNotEmpty(gory, "some lines are for gore on");
            var picker = new DeathLines(3);
            foreach (var kind in Kinds)
            foreach (var text in Texts)
                for (var i = 0; i < 100; i++)
                {
                    var pick = picker.Pick(kind, text, GoreRules.Off);
                    Assert.IsTrue(safe.Contains(pick), $"{kind} {text}: \"{pick}\" is for gore off");
                }
        }

        [Test]
        public void UltraGore_SometimesPicksItsOwnLines()
        {
            var ultra = new HashSet<string>(All().Where(l => l.line.Gore == GoreRules.UltraGore).Select(l => l.line.Text));
            Assert.IsNotEmpty(ultra);
            var picker = new DeathLines(11) { GenericShare = 0 };
            var seen = false;
            for (var i = 0; i < 400 && !seen; i++)
                seen = ultra.Contains(picker.Pick(DeathKind.Generic, DeathText.Feed, GoreRules.UltraGore));
            Assert.IsTrue(seen);
        }

        [Test]
        public void TheFeed_TakesAShorterLine_ForLongNames()
        {
            var picker = new DeathLines(5);
            foreach (var kind in Kinds)
                for (var i = 0; i < 50; i++)
                {
                    var line = picker.Feed(kind, GoreRules.UltraGore, 30);
                    Assert.LessOrEqual(DeathLines.PhraseLength(line), Math.Max(DeathLines.FeedRoom - 30, DeathLines.PhraseLength(DeathLines.PlainFallFeed)),
                        $"{kind}: {line}");
                }
        }

        // ---------------------------------------------------------------- The lines themselves

        [Test]
        public void EveryLine_HasOnlyKnownPlaceholders_AndFormatsCompletely()
        {
            foreach (var (kind, text, line) in All())
            {
                CollectionAssert.IsEmpty(DeathLines.UnknownPlaceholders(line.Text).ToList(), $"{kind} {text}: {line}");
                var formatted = DeathLines.Format(line.Text, "Mushroom 7", "Red 2", "flamethrower", 5).Replace("{icon}", "");
                StringAssert.DoesNotContain("{", formatted, $"{kind} {text}: {line}");
                StringAssert.DoesNotContain("}", formatted, $"{kind} {text}: {line}");
            }
        }

        [Test]
        public void FeedLines_NameWhoTheyShould_AndPlaceTheIcon()
        {
            foreach (var kind in Kinds)
            foreach (var line in DeathLines.Lines(kind, DeathText.Feed).Select(l => l.Text))
            {
                StringAssert.Contains("{victim}", line, $"{kind}");
                StringAssert.DoesNotContain("{weapon}", line, $"{kind}: the icon shows it");
                var icons = (line.Length - line.Replace("{icon}", "").Length) / "{icon}".Length;
                if (HasKiller(kind))
                {
                    StringAssert.Contains("{killer}", line, $"{kind}");
                    Assert.AreEqual(1, icons, $"{kind}: {line}");
                    var (before, after) = DeathLines.SplitAtIcon(DeathLines.Format(line, "Mushroom 7", "Red 2"));
                    Assert.IsTrue(before.Length > 0 && after.Length > 0, $"{kind}: {line} has words on both sides of the icon");
                }
                else
                {
                    StringAssert.DoesNotContain("{killer}", line, $"{kind}");
                    Assert.AreEqual(0, icons, $"{kind}: {line}");
                }
            }
        }

        [Test]
        public void FeedLines_FitTheEntry()
        {
            foreach (var kind in Kinds)
            foreach (var line in DeathLines.Lines(kind, DeathText.Feed).Select(l => l.Text))
                Assert.LessOrEqual(DeathLines.PhraseLength(line), DeathLines.MaxFeedPhrase, $"{kind}: {line}");
            Assert.LessOrEqual(DeathLines.MaxFeedPhrase, 32);
        }

        [Test]
        public void Titles_AreShortCapitals_WithoutNames()
        {
            foreach (var kind in Kinds)
            foreach (var title in DeathLines.Lines(kind, DeathText.Title).Select(l => l.Text))
            {
                Assert.LessOrEqual(title.Length, DeathLines.MaxTitle, $"{kind}: {title}");
                Assert.AreEqual(title.ToUpperInvariant(), title, $"{kind}: {title}");
                StringAssert.DoesNotContain("{", title, $"{kind}: {title}");
            }
        }

        [Test]
        public void Details_AreTwoShortLines_AboutTheKiller()
        {
            foreach (var kind in Kinds)
            foreach (var detail in DeathLines.Lines(kind, DeathText.Detail).Select(l => l.Text))
            {
                var lines = DeathLines.Format(detail, "Mushroom 7", weapon: "flamethrower", streak: 10).Split('\n');
                Assert.LessOrEqual(lines.Length, 2, $"{kind}: {detail}");
                foreach (var line in lines)
                    Assert.LessOrEqual(line.Length, DeathLines.MaxDetailLine, $"{kind}: {detail}");
                StringAssert.DoesNotContain("{victim}", detail, $"{kind}: the screen is yours");
                StringAssert.DoesNotContain("{icon}", detail, $"{kind}");
                if (!HasKiller(kind))
                    StringAssert.DoesNotContain("{killer}", detail, $"{kind}");
            }
        }

        [Test]
        public void Format_FillsTheNames_AndSplitsAtTheIcon()
        {
            var line = DeathLines.Format("{killer} sautéed {icon} {victim}", "Mushroom 7", "Red 2");
            Assert.AreEqual(("Mushroom 7 sautéed", "Red 2"), DeathLines.SplitAtIcon(line));
            Assert.AreEqual(("Red 2 fell", ""), DeathLines.SplitAtIcon(DeathLines.Format(DeathLines.PlainFallFeed, victim: "Red 2")));
            Assert.AreEqual("number 5", DeathLines.Format("number {streak}", streak: 5));
            Assert.AreEqual("poison gun", DeathLines.WeaponName(WeaponIds.PoisonGun));
        }

        // ---------------------------------------------------------------- The kind of a death

        private static readonly PlayerState Red = new PlayerState(0, "Red 1", Team.Red, false);
        private static readonly PlayerState Blue = new PlayerState(1, "Blue 1", Team.Blue, true);
        private static readonly PlayerState Blue2 = new PlayerState(2, "Blue 2", Team.Blue, true);

        [Test]
        public void ADeath_GetsItsKind()
        {
            Assert.AreEqual(DeathKind.Fall, DeathLines.KindOf(new Death(Red, DeathCause.Fell)));
            Assert.AreEqual(DeathKind.Tank, DeathLines.KindOf(new Death(Red, DeathCause.Killed, killerName: "Blue tank", killerTeam: Team.Blue, weapon: "cannon")));
            Assert.AreEqual(DeathKind.Helicopter,
                DeathLines.KindOf(new Death(Red, DeathCause.Killed, killerName: MachineRules.NameOf(MachineKind.Helicopter, Team.Blue), killerTeam: Team.Blue, weapon: "cannon")));
            Assert.AreEqual(DeathKind.Turret, DeathLines.KindOf(new Death(Red, DeathCause.Killed, killerName: "Blue turret", killerTeam: Team.Blue, weapon: "cannon")));
            Assert.AreEqual(DeathKind.Cannon, DeathLines.KindOf(new Death(Red, DeathCause.Killed, killerName: "Blue thing", killerTeam: Team.Blue, weapon: "cannon")));
            Assert.AreEqual(DeathKind.Headshot, DeathLines.KindOf(new Death(Red, DeathCause.Killed, Blue, weapon: WeaponIds.Sniper, headshot: true)));
            Assert.AreEqual(DeathKind.Flamethrower, DeathLines.KindOf(new Death(Red, DeathCause.Killed, Blue, weapon: WeaponIds.Flamethrower)));
            Assert.AreEqual(DeathKind.Generic, DeathLines.KindOf(new Death(Red, DeathCause.Killed, Blue, weapon: "spoon")));
            // Dual pistols are pistols to the feed's icon, and dual pistols to its words
            var dual = Damage.By(Blue, WeaponIds.DualPistols, 100).ToDeath(Red);
            Assert.AreEqual(WeaponIds.Pistol, dual.Weapon);
            Assert.AreEqual(DeathKind.DualPistols, DeathLines.KindOf(dual));
            foreach (var id in WeaponIds.Pickups.Append(WeaponIds.DualPistols))
                Assert.AreNotEqual(DeathKind.Generic, DeathLines.KindOfWeapon(id), id);
        }

        [Test]
        public void KillsInARow_MakeAStreak_AndADeathEndsIt()
        {
            var memory = new KillMemory();
            var kinds = new List<DeathKind>();
            for (var i = 0; i < 5; i++)
            {
                kinds.Add(memory.Record(new Death(i % 2 == 0 ? Blue : Blue2, DeathCause.Killed, Red, weapon: WeaponIds.Pistol), out var streak));
                Assert.AreEqual(i + 1, streak);
            }
            CollectionAssert.AreEqual(new[] { DeathKind.Pistol, DeathKind.Pistol, DeathKind.Streak, DeathKind.Pistol, DeathKind.Streak }, kinds);
            memory.Record(new Death(Red, DeathCause.Fell), out _);
            Assert.AreEqual(0, memory.StreakOf(Red));
        }

        [Test]
        public void KillingYourLastKiller_IsRevenge_Once()
        {
            var memory = new KillMemory();
            memory.Record(new Death(Red, DeathCause.Killed, Blue, weapon: WeaponIds.Pistol), out _);
            Assert.AreEqual(DeathKind.Revenge, memory.Record(new Death(Blue, DeathCause.Killed, Red, weapon: WeaponIds.Pistol), out _));
            Assert.AreEqual(DeathKind.Pistol, memory.Record(new Death(Blue2, DeathCause.Killed, Red, weapon: WeaponIds.Pistol), out _));
            memory.Clear();
            Assert.AreEqual(0, memory.StreakOf(Red));
        }
    }
}
