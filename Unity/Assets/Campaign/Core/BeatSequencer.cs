using System;
using System.Collections.Generic;

namespace SdfCampaign
{
    /// <summary>Holding a key for a set time (the skip rule of ticket 32: Space, Escape or Start for 0.7 s), as plain timing.</summary>
    public sealed class SkipHold
    {
        public const float Seconds = 0.7f;

        private float held;

        /// <summary>How far the hold has got, 0 to 1.</summary>
        public float Progress => Math.Min(1, held / Seconds);

        /// <summary>Lets <paramref name="dt"/> seconds pass; true on the step the hold completes. Letting go starts it over.</summary>
        public bool Tick(float dt, bool down)
        {
            if (!down)
            {
                held = 0;
                return false;
            }
            var before = held;
            held += dt;
            return before < Seconds && held >= Seconds;
        }

        public void Reset() => held = 0;
    }

    public enum BeatStage { Shots, Arrival, Done }

    /// <summary>
    /// The timing of a beat (ticket 32.7) with no scene in it: one shot after another, each for its seconds; a click or Space press moves
    /// on to the next shot early; holding the skip keys for 0.7 s jumps to the hand-over. After the last shot comes the arrival (the
    /// camera moving into 42's eyes) and then it is done. The scene's beat player reads <see cref="Shot"/> and the progress values every
    /// frame, so the ending (32.8) and any later beat reuse it as it is.
    /// </summary>
    public sealed class BeatSequencer
    {
        /// <summary>Seconds the camera takes to move into the player's eyes after the last shot.</summary>
        public const float ArrivalSeconds = 1.2f;
        /// <summary>A shot cannot be moved on from before this, so a click that was meant for something else does not eat a line.</summary>
        public const float MinShotSeconds = 0.3f;
        /// <summary>Seconds a caption fades in and out at the ends of a shot.</summary>
        public const float FadeSeconds = 0.25f;

        private readonly IReadOnlyList<BeatShot> shots;
        private readonly SkipHold skip = new SkipHold();
        private float arrival;

        public BeatSequencer(Beat beat) : this(beat.shots) { }

        public BeatSequencer(IReadOnlyList<BeatShot> shots)
        {
            this.shots = shots;
            Stage = shots.Count == 0 ? BeatStage.Arrival : BeatStage.Shots;
        }

        public BeatStage Stage { get; private set; }

        public bool Done => Stage == BeatStage.Done;

        /// <summary>The player held the skip keys through to the hand-over.</summary>
        public bool Skipped { get; private set; }

        public int ShotIndex { get; private set; }

        /// <summary>Seconds into the shot.</summary>
        public float ShotTime { get; private set; }

        /// <summary>The shot being played; the last one stays during the arrival.</summary>
        public BeatShot Shot => shots.Count == 0 ? null : shots[Math.Min(ShotIndex, shots.Count - 1)];

        /// <summary>How far into the shot, 0 to 1 (the camera move's time).</summary>
        public float ShotProgress => Stage != BeatStage.Shots || Shot == null || Shot.seconds <= 0 ? 1 : Math.Min(1, ShotTime / Shot.seconds);

        /// <summary>How far the arrival has got, 0 to 1.</summary>
        public float Arrival => Stage == BeatStage.Shots ? 0 : Stage == BeatStage.Done ? 1 : Math.Min(1, arrival / ArrivalSeconds);

        /// <summary>How far the skip hold has got, for the bar.</summary>
        public float SkipProgress => skip.Progress;

        /// <summary>The caption's opacity: faded in at the start of the shot and out at its end; 0 outside the shots or with no line.</summary>
        public float CaptionAlpha
        {
            get
            {
                var shot = Shot;
                if (Stage != BeatStage.Shots || shot == null || string.IsNullOrEmpty(shot.line))
                    return 0;
                var left = shot.seconds - ShotTime;
                return Math.Max(0, Math.Min(1, Math.Min(ShotTime / FadeSeconds, left / FadeSeconds)));
            }
        }

        /// <summary>Lets <paramref name="dt"/> seconds pass. <paramref name="skipHeld"/>: a skip key is down.</summary>
        public void Tick(float dt, bool skipHeld)
        {
            if (Stage == BeatStage.Done)
                return;
            if (skip.Tick(dt, skipHeld) && Stage == BeatStage.Shots)
            {
                Skip();
                return;
            }
            if (Stage == BeatStage.Shots)
            {
                ShotTime += dt;
                if (ShotTime >= Shot.seconds)
                    NextShot();
            }
            else
            {
                arrival += dt;
                if (arrival >= ArrivalSeconds)
                    Stage = BeatStage.Done;
            }
        }

        /// <summary>A click or Space: on to the next shot early (after the shot's first moments); on the last it starts the arrival.</summary>
        public void Advance()
        {
            if (Stage == BeatStage.Shots && ShotTime >= MinShotSeconds)
                NextShot();
        }

        /// <summary>Straight to the hand-over, as a held skip does.</summary>
        public void Skip()
        {
            if (Stage != BeatStage.Shots)
                return;
            Skipped = true;
            BeginArrival();
        }

        private void NextShot()
        {
            ShotTime = 0;
            if (ShotIndex + 1 >= shots.Count)
                BeginArrival();
            else
                ShotIndex++;
        }

        private void BeginArrival()
        {
            Stage = BeatStage.Arrival;
            arrival = 0;
        }
    }
}
