using SdfBattle;
using UnityEngine;

namespace SdfMedia.Editor
{
    /// <summary>
    /// Judges how much is going on in front of your camera, for <see cref="PreviewVideoRecorder"/>: it is told when to look (a few times a
    /// second) and answers with a number, the action score, from what is on screen and what just happened to you.
    /// <list type="bullet">
    /// <item>Opponents in view: alive enemy scouts within <see cref="Range"/> metres, inside the part of the picture all three video
    /// shapes share (the portrait cut is the narrowest) and with a clear line of sight. Two or more is a fight (4 points and one more
    /// for each further), one is a lead (1.5), and each one within <see cref="Close"/> metres adds a point (at most two).</item>
    /// <item>What just happened: a kill within 35 metres of you in the last two seconds (3), a hit of yours in the last 1.5 seconds (2),
    /// you hurt in the last 1.5 seconds (2), your trigger held (1).</item>
    /// <item>A flag carrier in view (an enemy of the carrier's, or a friend, any flag carried by a scout in view within 40 metres), or
    /// you carrying one: 2 points.</item>
    /// <item>An enemy machine (tank, helicopter, turret) alive in view within 30 metres: 1.5 points each, at most two.</item>
    /// </list>
    /// A busy moment is about 8 or more, a quiet walk 0 to 2. It listens to the match's events from <see cref="Attach"/> until
    /// <see cref="Detach"/>; nothing here changes the match.
    /// </summary>
    public sealed class PreviewAction
    {
        /// <summary>Metres within which an opponent counts as on screen.</summary>
        public const float Range = 25;

        /// <summary>Metres within which an opponent is close enough to add a point.</summary>
        public const float Close = 12;

        private Match match;
        private Player you;
        private Arsenal arsenal;
        private Camera view;
        private float lastKill = float.NegativeInfinity;
        private float lastHit = float.NegativeInfinity;
        private float lastHurt = float.NegativeInfinity;
        private float halfHorizontal;
        private float halfVertical;

        /// <summary>Opponents counted in view by the last <see cref="Score"/>, for the log.</summary>
        public int OpponentsInView { get; private set; }

        /// <summary>Whether the last <see cref="Score"/> saw a flag carried in view or by you.</summary>
        public bool CarrierInView { get; private set; }

        /// <summary>
        /// Starts listening. <paramref name="halfHorizontalDegrees"/> and <paramref name="halfVerticalDegrees"/> are how far from the
        /// view's middle something may be to count as on screen in every video shape (the narrowest cut horizontally, the shortest
        /// vertically), already shrunk a little so that it is well inside the picture.
        /// </summary>
        public void Attach(Match owner, Camera camera, float halfHorizontalDegrees, float halfVerticalDegrees)
        {
            match = owner;
            you = owner.You;
            view = camera;
            arsenal = Combat.Current != null ? Combat.Current.ArsenalOf(you) : null;
            halfHorizontal = halfHorizontalDegrees;
            halfVertical = halfVerticalDegrees;
            match.State.Died += OnDied;
            if (Combat.Current != null)
            {
                Combat.Current.HitLanded += OnHit;
                Combat.Current.State.Damaged += OnDamaged;
            }
        }

        /// <summary>Stops listening.</summary>
        public void Detach()
        {
            if (match != null && match.State != null)
                match.State.Died -= OnDied;
            if (Combat.Current != null)
            {
                Combat.Current.HitLanded -= OnHit;
                if (Combat.Current.State != null)
                    Combat.Current.State.Damaged -= OnDamaged;
            }
            match = null;
        }

        private void OnDied(Death death)
        {
            if (you == null || death.Cause != DeathCause.Killed)
                return;
            var victim = match.PlayerOf(death.Victim);
            if (victim != null && Vector3.Distance(victim.transform.position, you.transform.position) < 35)
                lastKill = Time.time;
        }

        private void OnHit(Player shooter, Player victim, bool kill, bool head)
        {
            if (shooter == you)
                lastHit = Time.time;
        }

        private void OnDamaged(PlayerState victim, Damage damage, float amount)
        {
            if (you != null && you.State == victim && amount > 0)
                lastHurt = Time.time;
        }

        /// <summary>Whether a point is on screen in every video shape, within <paramref name="range"/> metres, with nothing solid between.</summary>
        private bool Sees(Vector3 eye, Vector3 point, float range)
        {
            var local = view.transform.InverseTransformPoint(point);
            if (local.z < 1 || local.magnitude > range)
                return false;
            if (Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg) > halfHorizontal || Mathf.Abs(Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg) > halfVertical)
                return false;
            return Bots.Current == null || Bots.Current.LineOfSight(eye, point);
        }

        /// <summary>The action score of this moment, 0 when you are not alive; see the class comment.</summary>
        public float Score()
        {
            OpponentsInView = 0;
            CarrierInView = false;
            if (match == null || you == null || view == null || !you.State.Alive)
                return 0;
            var eye = view.transform.position;
            var score = 0f;
            var close = 0;
            foreach (var other in match.Players)
            {
                if (other == you || other.State == null || !other.State.Alive || other.Team == you.Team)
                    continue;
                var point = other.transform.position + Vector3.up * 1.2f;
                if (!Sees(eye, point, Range))
                    continue;
                OpponentsInView++;
                if (Vector3.Distance(eye, point) <= Close)
                    close++;
            }
            score += OpponentsInView >= 2 ? 4 + (OpponentsInView - 2) : OpponentsInView == 1 ? 1.5f : 0;
            score += Mathf.Min(2, close);

            var now = Time.time;
            if (now - lastKill < 2)
                score += 3;
            if (now - lastHit < 1.5f)
                score += 2;
            if (now - lastHurt < 1.5f)
                score += 2;
            if (arsenal != null && arsenal.Firing)
                score += 1;

            var flags = CaptureTheFlag.Current;
            if (flags != null && flags.State != null)
            {
                foreach (var flag in flags.State.Flags)
                {
                    if (flag.Status != FlagStatus.Carried || flag.Carrier == null)
                        continue;
                    var carrier = match.PlayerOf(flag.Carrier);
                    if (carrier == you || (carrier != null && Sees(eye, carrier.transform.position + Vector3.up * 1.2f, 40)))
                        CarrierInView = true;
                }
                if (CarrierInView)
                    score += 2;
            }

            var machines = Machines.Current;
            if (machines != null)
            {
                var seen = 0;
                foreach (var machine in machines.All)
                    if (machine.Alive && machine.Team != you.Team && seen < 2 && Sees(eye, machine.Center, 30))
                        seen++;
                score += 1.5f * seen;
            }
            return score;
        }
    }
}
