using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Prime hanging under the Prime-copter by his hands (ticket 33.5): a damped pendulum from the grips. Its bob lags behind the
    /// pivot's acceleration (the copter speeding up swings him back, braking swings him forward), swings about in the lazy period of its
    /// length and settles. Plain C#: the displacement is two angles, <see cref="X"/> towards world +x and <see cref="Z"/> towards +z, in
    /// radians; <see cref="Direction"/> and <see cref="Rotation"/> turn them into where the body hangs.
    /// </summary>
    public sealed class HangingPendulum
    {
        private const float StepSeconds = 1f / 60;

        /// <summary>Metres from the grips to Prime's middle; the period is 2 pi sqrt(length / g), about 3.7 s for 3.4 m.</summary>
        public float Length = 3.4f;
        /// <summary>The share of the swing's speed lost a second.</summary>
        public float Damping = 0.7f;
        public float Gravity = 9.8f;
        /// <summary>The most it swings, radians (about 40 degrees: the hands hold, the body does not wrap round the copter).</summary>
        public float MaxAngle = 0.7f;

        public float X { get; private set; }
        public float Z { get; private set; }
        public float VelocityX { get; private set; }
        public float VelocityZ { get; private set; }

        /// <summary>How far it is from hanging straight down, radians.</summary>
        public float Angle => Mathf.Sqrt(X * X + Z * Z);

        /// <summary>The unit vector from the grips to the body's middle: straight down at rest.</summary>
        public Vector3 Direction => new Vector3(Mathf.Sin(X), -Mathf.Cos(X) * Mathf.Cos(Z), Mathf.Sin(Z)).normalized;

        /// <summary>The turn from hanging straight down to hanging as it does now (apply it in world space, after the body's own yaw).</summary>
        public Quaternion Rotation => Quaternion.FromToRotation(Vector3.down, Direction);

        /// <summary>Hanging still, straight down.</summary>
        public void Reset()
        {
            X = Z = VelocityX = VelocityZ = 0;
        }

        /// <summary>A shove: the body swings off at <paramref name="speed"/> metres a second in a direction (a slam's recoil).</summary>
        public void Kick(Vector3 speed)
        {
            VelocityX += speed.x / Length;
            VelocityZ += speed.z / Length;
        }

        /// <summary>Lets <paramref name="dt"/> seconds pass while the grips accelerate by <paramref name="pivotAcceleration"/> (metres a second squared).</summary>
        public void Step(float dt, Vector3 pivotAcceleration)
        {
            // In fixed small steps, so a long frame cannot make it blow up
            while (dt > 0)
            {
                var h = Mathf.Min(dt, StepSeconds);
                dt -= h;
                // The bob is pulled back by gravity and lags the pivot's acceleration (a pivot going +x leaves it behind: -x)
                var ax = -(Gravity / Length) * Mathf.Sin(X) - Damping * VelocityX - pivotAcceleration.x / Length * Mathf.Cos(X);
                var az = -(Gravity / Length) * Mathf.Sin(Z) - Damping * VelocityZ - pivotAcceleration.z / Length * Mathf.Cos(Z);
                VelocityX += ax * h;
                VelocityZ += az * h;
                X += VelocityX * h;
                Z += VelocityZ * h;
                Clamp();
            }
        }

        private void Clamp()
        {
            var angle = Angle;
            if (angle <= MaxAngle)
                return;
            var scale = MaxAngle / angle;
            X *= scale;
            Z *= scale;
            // Stopped against its limit: the speed pointing further out is lost
            var along = (VelocityX * X + VelocityZ * Z) / (MaxAngle * MaxAngle);
            if (along > 0)
            {
                VelocityX -= along * X;
                VelocityZ -= along * Z;
            }
        }
    }
}
