using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 29.7: the zone mask lookup (<see cref="ZoneMask"/>), the slick's slide (<see cref="ZoneRules.Slide"/>: acceleration x0.25,
    /// braking x0.1, top speed x1.3 for a player, x1.2 for a bot) and the wet scouts (<see cref="WetTracker"/>, the electro gun's x1.5 through
    /// <see cref="CombatState.DamageFactorOf"/>).
    /// </summary>
    public sealed class ZoneRulesTests
    {
        private const float Walk = 4.4f;

        // A 6 x 4 mask from (-3, -2): slick at cells (0, 0) and (1, 0), wet at (4, 2), both at (5, 3)
        private static ZoneMask Mask()
        {
            var cells = new byte[6 * 4];
            cells[0] = ZoneMask.SlickBit;
            cells[1] = ZoneMask.SlickBit;
            cells[2 * 6 + 4] = ZoneMask.WetBit;
            cells[3 * 6 + 5] = ZoneMask.SlickBit | ZoneMask.WetBit;
            return new ZoneMask(new Vector2(-3, -2), 6, 4, cells);
        }

        [Test]
        public void Mask_LooksUpByPosition_AndIsEmptyOutside()
        {
            var mask = Mask();

            Assert.AreEqual(ZoneMask.SlickBit, mask.At(-2.5f, -1.5f));
            Assert.AreEqual(ZoneMask.SlickBit, mask.At(-1.1f, -1.9f));
            Assert.AreEqual(0, mask.At(-0.5f, -1.5f));
            Assert.AreEqual(ZoneMask.WetBit, mask.At(1.5f, 0.5f));
            Assert.AreEqual(ZoneMask.SlickBit | ZoneMask.WetBit, mask.At(2.9f, 1.9f));
            Assert.AreEqual(0, mask.At(-3.1f, -1.5f));
            Assert.AreEqual(0, mask.At(3.1f, 1.5f));
            Assert.AreEqual(0, mask.At(0, 2.1f));
            Assert.AreEqual(0, mask.At(0, -2.1f));
        }

        [Test]
        public void Mask_InTheWorld_MirrorsX()
        {
            var mask = Mask();

            Assert.IsTrue(mask.IsSlick(new Vector3(2.5f, 3, -1.5f)));
            Assert.IsFalse(mask.IsSlick(new Vector3(-2.5f, 3, -1.5f)));
            Assert.IsTrue(mask.IsWet(new Vector3(-1.5f, 0, 0.5f)));
            Assert.IsFalse(mask.IsWet(new Vector3(1.5f, 0, 0.5f)));
        }

        [Test]
        public void Mask_RefusesTheWrongNumberOfBytes()
        {
            Assert.Throws<System.ArgumentException>(() => new ZoneMask(Vector2.zero, 3, 3, new byte[8]));
        }

        private static Vector2 Run(Vector2 start, Vector2 wanted, float seconds, float dt = 0.01f)
        {
            var velocity = start;
            for (var step = 0; step < Mathf.RoundToInt(seconds / dt); step++)
                velocity = ZoneRules.Slide(velocity, wanted, Walk, dt);
            return velocity;
        }

        [Test]
        public void Slide_Accelerates_AtAQuarterOfTheNormalRate()
        {
            var wanted = new Vector2(0, Walk * ZoneRules.SlickTopSpeedPlayer);
            var normalSeconds = ZoneRules.AccelerationTime;

            // After the normal ground's time to top speed, a quarter of the speed; after four times that, all of it
            Assert.AreEqual(wanted.y * 0.25f * (Walk / wanted.y), Run(Vector2.zero, wanted, normalSeconds).y, 0.05f);
            Assert.AreEqual(wanted.y, Run(Vector2.zero, wanted, normalSeconds * 4 * 1.3f + 0.1f).y, 1e-3f);
        }

        [Test]
        public void Slide_Brakes_AtATenthOfTheNormalRate()
        {
            var start = new Vector2(0, Walk * ZoneRules.SlickTopSpeedPlayer);

            // Letting go: it takes a second to lose the walking speed (0.1 s on normal ground x10), so it is still going after half a second
            var half = Run(start, Vector2.zero, 0.5f);
            Assert.Greater(half.y, start.y * 0.4f);
            Assert.AreEqual(0, Run(start, Vector2.zero, 2).magnitude, 1e-3f);
            // ... and brakes faster than it accelerates
            Assert.Less(Run(start, Vector2.zero, 0.5f).magnitude, start.magnitude);
        }

        [Test]
        public void Slide_BrakesTenTimesSlowerThanOnNormalGround()
        {
            var start = new Vector2(0, Walk);
            var seconds = 0f;
            var velocity = start;
            while (velocity.magnitude > 1e-3f && seconds < 20)
            {
                velocity = ZoneRules.Slide(velocity, Vector2.zero, Walk, 0.01f);
                seconds += 0.01f;
            }

            Assert.AreEqual(ZoneRules.BrakingTime / ZoneRules.SlickBraking, seconds, 0.05f);
        }

        [Test]
        public void Slide_SteersSlowly_AndKeepsMomentumSideways()
        {
            var start = new Vector2(0, Walk * 1.3f);
            var wanted = new Vector2(Walk * 1.3f, 0);

            var after = Run(start, wanted, 0.3f);

            // Still mostly going the old way after 0.3 s of asking for a right-angle turn
            Assert.Greater(after.y, after.x);
            Assert.Greater(after.x, 0);
        }

        [Test]
        public void Slide_NeverPassesTheWantedVelocity()
        {
            var wanted = new Vector2(1, 2);

            Assert.AreEqual(wanted, ZoneRules.Slide(new Vector2(0.9f, 1.9f), wanted, Walk, 5));
            Assert.AreEqual(wanted, ZoneRules.Slide(wanted, wanted, Walk, 0.02f));
            Assert.AreEqual(Vector2.zero, ZoneRules.Slide(Vector2.zero, Vector2.zero, Walk, 0.02f));
        }

        [Test]
        public void TopSpeed_PlayerOnSoapIs13_BotIs12_OffSoapIsNormal()
        {
            Assert.AreEqual(1.3f, ZoneRules.TopSpeedFactor(true, false), 1e-6f);
            Assert.AreEqual(1.2f, ZoneRules.TopSpeedFactor(true, true), 1e-6f);
            Assert.AreEqual(1f, ZoneRules.TopSpeedFactor(false, false));
            Assert.AreEqual(1f, ZoneRules.TopSpeedFactor(false, true));
        }

        [Test]
        public void Wet_LastsTwoSecondsAfterLeavingTheWater_AndNotBeforeEntering()
        {
            var tracker = new WetTracker();
            tracker.Tick(false, 5);
            Assert.IsFalse(tracker.Wet);

            tracker.Tick(true, 0.02f);
            Assert.IsTrue(tracker.Wet);
            tracker.Tick(false, 1.9f);
            Assert.IsTrue(tracker.Wet);
            tracker.Tick(false, 0.2f);
            Assert.IsFalse(tracker.Wet);

            tracker.Tick(true, 0.02f);
            tracker.Reset();
            Assert.IsFalse(tracker.Wet);
        }

        [Test]
        public void DamageFactor_IsOnePointFiveForTheElectroGunOnWetScoutsOnly()
        {
            Assert.AreEqual(1.5f, ZoneRules.DamageFactor(WeaponIds.ElectroGun, true));
            Assert.AreEqual(1f, ZoneRules.DamageFactor(WeaponIds.ElectroGun, false));
            Assert.AreEqual(1f, ZoneRules.DamageFactor(WeaponIds.Minigun, true));
        }

        [Test]
        public void Hurt_ElectroGunOnAWetScoutTakesOnePointFiveTimes_OtherHitsAndDryScoutsAreUnchanged()
        {
            var match = new MatchState(new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, true);
            var combat = new CombatState(match, WeaponTable.Default());
            match.Begin();
            for (var left = match.Rules.countdownSeconds + match.Rules.spawnProtectionSeconds + 0.2f; left > 0; left -= 0.05f)
                match.Tick(0.05f);
            var red = match.Players[0];
            var blue = match.Players[4];
            var wet = new WetTracker();
            combat.DamageFactorOf = (victim, damage) => ZoneRules.DamageFactor(damage.Weapon, victim == blue && wet.Wet);

            var dry = combat.Hurt(blue, Damage.By(red, WeaponIds.ElectroGun, 10));
            wet.Tick(true, 0.02f);
            var electro = combat.Hurt(blue, Damage.By(red, WeaponIds.ElectroGun, 10));
            var minigun = combat.Hurt(blue, Damage.By(red, WeaponIds.Minigun, 10));
            wet.Tick(false, 2.5f);
            var later = combat.Hurt(blue, Damage.By(red, WeaponIds.ElectroGun, 10));

            Assert.AreEqual(10, dry, 1e-4f);
            Assert.AreEqual(15, electro, 1e-4f);
            Assert.AreEqual(10, minigun, 1e-4f);
            Assert.AreEqual(10, later, 1e-4f);
        }
    }
}
