using NUnit.Framework;

namespace SdfAudio.Tests
{
    /// <summary>The sound's rules: the music's intensity and its layers, the footsteps' stride, the heartbeat.</summary>
    public sealed class AudioCoreTests
    {
        [Test]
        public void ACalmMoment_HasNoIntensity_AndOnlyTheFirstLayer()
        {
            var level = MusicIntensity.Target(BattleMoment.Calm);
            Assert.AreEqual(0, level, 1e-6f);
            Assert.AreEqual(1, MusicIntensity.LayerShare(0, level), 1e-6f);
            for (var layer = 1; layer < MusicIntensity.Layers; layer++)
                Assert.AreEqual(0, MusicIntensity.LayerShare(layer, level), 1e-6f);
        }

        [Test]
        public void MoreHappening_MeansAHigherTarget()
        {
            var one = new BattleMoment(true, 0, 1, 999, 999, false, false);
            var shooting = new BattleMoment(true, 1, 2, 1, 999, false, false);
            var brawl = new BattleMoment(true, 3, 4, 0, 0, false, false);
            Assert.Less(MusicIntensity.Target(BattleMoment.Calm), MusicIntensity.Target(one));
            Assert.Less(MusicIntensity.Target(one), MusicIntensity.Target(shooting));
            Assert.Less(MusicIntensity.Target(shooting), MusicIntensity.Target(brawl));
            Assert.AreEqual(1, MusicIntensity.Target(brawl), 1e-6f);
        }

        [Test]
        public void CarryingTheFlag_BringsInTheThirdLayer()
        {
            var carrying = new BattleMoment(true, 0, 0, 999, 999, true, false);
            Assert.GreaterOrEqual(MusicIntensity.Target(carrying), 0.7f);
            Assert.AreEqual(1, MusicIntensity.LayerShare(2, MusicIntensity.Target(carrying)), 1e-6f);
        }

        [Test]
        public void ADeadPlayer_HearsTheMusicSettle()
        {
            var dead = new BattleMoment(false, 3, 4, 0, 0, true, true);
            Assert.Less(MusicIntensity.Target(dead), MusicIntensity.Enter[2]);
        }

        [Test]
        public void TheLevel_RisesFast_AndFallsSlowly()
        {
            var intensity = new MusicIntensity();
            var brawl = new BattleMoment(true, 3, 4, 0, 0, false, false);
            intensity.Tick(1, brawl);
            Assert.AreEqual(0.5f, intensity.Level, 1e-4f);
            intensity.Tick(10, brawl);
            Assert.AreEqual(1, intensity.Level, 1e-4f);
            intensity.Tick(1, BattleMoment.Calm);
            Assert.AreEqual(0.95f, intensity.Level, 1e-4f);
            intensity.Tick(30, BattleMoment.Calm);
            Assert.AreEqual(0, intensity.Level, 1e-4f);
        }

        [Test]
        public void TheLayers_FadeInOneAfterAnother()
        {
            for (var level = 0f; level < 1f; level += 0.05f)
                for (var layer = 1; layer < MusicIntensity.Layers - 1; layer++)
                    Assert.GreaterOrEqual(MusicIntensity.LayerShare(layer, level), MusicIntensity.LayerShare(layer + 1, level) - 1e-6f);
            Assert.AreEqual(1, MusicIntensity.LayerShare(3, 1), 1e-6f);
        }

        [Test]
        public void TheStride_GivesAStepEveryStride_AndTheFirstOneEarly()
        {
            var stride = new StrideCounter(2);
            Assert.IsFalse(stride.Advance(0.5f));
            Assert.IsTrue(stride.Advance(0.5f));
            var steps = 0;
            for (var i = 0; i < 100; i++)
                if (stride.Advance(0.1f))
                    steps++;
            Assert.AreEqual(5, steps);
            stride.Rest();
            Assert.IsTrue(stride.Advance(1));
        }

        [Test]
        public void TheHeartbeat_IsSilentAboveTheThreshold_AndLouderAsHealthFalls()
        {
            Assert.AreEqual(0, HeartbeatRules.Volume(0.8f), 1e-6f);
            Assert.AreEqual(0, HeartbeatRules.Volume(HeartbeatRules.Threshold), 1e-6f);
            Assert.Less(HeartbeatRules.Volume(0.3f), HeartbeatRules.Volume(0.1f));
            Assert.AreEqual(1, HeartbeatRules.Volume(0), 1e-6f);
            Assert.Less(HeartbeatRules.Pitch(0.3f), HeartbeatRules.Pitch(0.05f));
        }
    }
}
