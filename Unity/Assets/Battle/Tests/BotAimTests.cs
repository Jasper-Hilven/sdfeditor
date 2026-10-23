using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>Ticket 23: a reaction time, then an aim error that shrinks while tracking; one skill setting; beatable.</summary>
    public sealed class BotAimTests
    {
        [Test]
        public void ANewTarget_WaitsTheReactionTime()
        {
            var aim = new BotAim(new BotSkill(0.5f), new System.Random(1));
            Assert.IsFalse(aim.Ready);
            aim.Acquire(3);
            Assert.IsFalse(aim.Ready);
            var reaction = aim.ReactionLeft;
            Assert.That(reaction, Is.InRange(aim.Skill.ReactionSeconds * 0.8f, aim.Skill.ReactionSeconds * 1.2f));
            for (var t = 0f; t < reaction - 0.05f; t += 0.02f)
            {
                aim.Tick(0.02f);
                Assert.IsFalse(aim.Ready);
            }
            aim.Tick(0.1f);
            Assert.IsTrue(aim.Ready);
        }

        [Test]
        public void TheError_ShrinksWhileTracking_ButNeverToNothing()
        {
            var aim = new BotAim(new BotSkill(0.5f), new System.Random(2));
            aim.Acquire(1);
            aim.Tick(1);
            var early = aim.Error;
            for (var i = 0; i < 100; i++)
                aim.Tick(0.05f);
            Assert.Less(aim.Error, early);
            Assert.Greater(aim.Error, 0);
            Assert.AreEqual(aim.Skill.SteadyError, aim.Error, 0.1f);
            Assert.LessOrEqual(aim.Offset.magnitude, aim.Error + 1e-4f);
        }

        [Test]
        public void ACrossingTarget_AddsError()
        {
            var still = new BotAim(new BotSkill(0.5f), new System.Random(3));
            var crossing = new BotAim(new BotSkill(0.5f), new System.Random(3));
            still.Acquire(1);
            crossing.Acquire(1);
            for (var i = 0; i < 40; i++)
            {
                still.Tick(0.05f);
                crossing.Tick(0.05f, 30);
            }
            Assert.Greater(crossing.Error, still.Error + 1);
        }

        [Test]
        public void TheSameTarget_KeepsItsTracking_ANewOneStartsOver()
        {
            var aim = new BotAim(new BotSkill(0.5f), new System.Random(4));
            aim.Acquire(1);
            for (var i = 0; i < 40; i++)
                aim.Tick(0.05f);
            var tracked = aim.Tracked;
            aim.Acquire(1);
            Assert.AreEqual(tracked, aim.Tracked);
            Assert.IsTrue(aim.Ready);
            aim.Acquire(2);
            Assert.AreEqual(0, aim.Tracked);
            Assert.IsFalse(aim.Ready);
            aim.Lose();
            Assert.AreEqual(-1, aim.Target);
            Assert.AreEqual(Vector2.zero, aim.Offset);
        }

        [Test]
        public void MoreSkill_ReactsFasterAndAimsTighter()
        {
            var low = new BotSkill(0);
            var high = new BotSkill(1);
            Assert.Greater(low.ReactionSeconds, high.ReactionSeconds);
            Assert.Greater(low.StartError, high.StartError);
            Assert.Greater(low.SteadyError, high.SteadyError);
            Assert.Less(low.TurnSpeed, high.TurnSpeed);
            // Beatable even at the top: a fifth of a second to react, never a perfect aim
            Assert.GreaterOrEqual(high.ReactionSeconds, 0.199f);
            Assert.Greater(high.SteadyError, 0.5f);
        }

        [Test]
        public void AtMidSkill_ASteadyAimHitsABodyAtMidRangeMostOfTheTime_NotAtLongRange()
        {
            // A body is 0.3 m round: the steady error's miss at 12 m is within it, at 60 m it is not (15 m was mid range before the aim error went up 20%)
            var skill = new BotSkill(0.5f);
            var missAt12 = Mathf.Tan(skill.SteadyError * Mathf.Deg2Rad) * 12;
            var missAt60 = Mathf.Tan(skill.SteadyError * Mathf.Deg2Rad) * 60;
            Assert.Less(missAt12, 0.6f);
            Assert.Greater(missAt60, 0.6f);
        }
    }

    /// <summary>Ticket 23: a view cone and a hearing range; the last seen position remembered for a few seconds.</summary>
    public sealed class BotSightTests
    {
        private static readonly BotSkill Skill = new BotSkill(0.5f);

        [Test]
        public void InFront_IsSeen_Behind_IsNot_ButCloseIsHeard()
        {
            var eye = new Vector3(0, 1.5f, 0);
            Assert.IsTrue(BotSight.InView(eye, Vector3.forward, new Vector3(5, 1, 30), Skill));
            Assert.IsFalse(BotSight.InView(eye, Vector3.forward, new Vector3(0, 1, -30), Skill));
            Assert.IsFalse(BotSight.InView(eye, Vector3.forward, new Vector3(40, 1, 1), Skill));
            Assert.IsTrue(BotSight.InView(eye, Vector3.forward, new Vector3(0, 1, -2), Skill));
            Assert.IsFalse(BotSight.InView(eye, Vector3.forward, new Vector3(0, 1, Skill.SightRange + 5), Skill));
        }

        [Test]
        public void LookingDown_StillSeesAhead()
        {
            var eye = new Vector3(0, 1.5f, 0);
            var down = new Vector3(0, -Mathf.Sin(70 * Mathf.Deg2Rad), Mathf.Cos(70 * Mathf.Deg2Rad));
            Assert.IsTrue(BotSight.InView(eye, down, new Vector3(0, 6, 40), Skill));
        }

        [Test]
        public void Memory_LastsItsSeconds()
        {
            var memory = new BotMemory(8, 4);
            Assert.IsFalse(memory.Recall(2, 0, out _));
            memory.Seen(2, new Vector3(1, 2, 3), 10);
            Assert.IsTrue(memory.Recall(2, 13.9f, out var where));
            Assert.AreEqual(new Vector3(1, 2, 3), where);
            Assert.IsFalse(memory.Recall(2, 14.1f, out _));
            memory.Seen(2, Vector3.one, 20);
            memory.Forget();
            Assert.IsFalse(memory.Recall(2, 20, out _));
        }
    }
}
