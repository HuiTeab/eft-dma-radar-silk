// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Numerically integrates a projectile's trajectory (G1 drag + gravity) to find the bullet drop
    /// and travel time over a given shot distance, matching EFT's in-game ballistics.
    /// </summary>
    public static class BallisticsSimulation
    {
        // Plenty of steps to cover any round/range in the game before giving up.
        private const int MaxIterations = 1300;
        // Integration step (seconds).
        private const float TimeStep = 0.01f;
        // Position contribution of acceleration over one step (½·dt²) for semi-implicit Euler.
        private const float HalfStepSquared = 0.5f * TimeStep * TimeStep;
        // Bisection tolerance when pinning the exact crossing of the target distance.
        private const float LerpTolerance = 0.001f;
        // Reference air density (kg/m³) at sea level.
        private const float AirDensity = 1.2f;
        // Folds the grain/inch sectional-density units into SI for the drag term.
        private const float SectionalDensityFactor = 0.0014223f;

        private static readonly Vector3 Gravity = new(0f, -9.81f, 0f);
        private static readonly Vector3 Forward = new(0f, 0f, 1f);

        public static BallisticSimulationOutput Run(ref Vector3 startPosition, ref Vector3 endPosition, BallisticsInfo ballistics)
        {
            float shotDistance = Vector3.Distance(startPosition, endPosition);
            float shotDistanceSq = shotDistance * shotDistance;

            // Per-round constants used by the drag term each step.
            float massKg = ballistics.BulletMassGrams / 1000f;
            float twoMass = massKg * 2f;
            float diameterM = ballistics.BulletDiameterMillimeters / 1000f;
            float bcFactor = massKg * SectionalDensityFactor / (diameterM * diameterM * ballistics.BallisticCoefficient);
            float crossSection = diameterM * diameterM * MathF.PI / 4f;
            float dragArea = AirDensity * crossSection;

            float time = 0f; // elapsed flight time at lastPosition
            Vector3 lastPosition = Vector3.Zero;
            Vector3 lastVelocity = Forward * ballistics.BulletSpeed;

            float bulletDrop = 0f;
            float travelTime = 0f;

            for (int step = 1; step < MaxIterations; step++)
            {
                float speed = lastVelocity.Length();
                float drag = G1.CalculateDragCoefficient(speed) * bcFactor;
                Vector3 dragAccel = dragArea * -drag * speed * speed / twoMass * Vector3.Normalize(lastVelocity);
                Vector3 acceleration = Gravity + dragAccel;

                Vector3 currentPosition = lastPosition + lastVelocity * TimeStep + HalfStepSquared * acceleration;
                Vector3 currentVelocity = lastVelocity + acceleration * TimeStep;
                float currentTime = time + TimeStep;

                // currentPosition is measured from the muzzle at the origin, so its length is the
                // distance travelled — compare squared to skip a sqrt every step.
                if (currentPosition.LengthSquared() >= shotDistanceSq)
                {
                    float t = FindCrossingLerp(lastPosition, currentPosition, shotDistance);
                    bulletDrop = Math.Abs(Vector3.Lerp(lastPosition, currentPosition, t).Y);
                    travelTime = float.Lerp(time, currentTime, t);
                    break;
                }

                time = currentTime;
                lastPosition = currentPosition;
                lastVelocity = currentVelocity;
            }

            return new BallisticSimulationOutput(bulletDrop, travelTime);
        }

        /// <summary>Bisects the segment to find the fraction at which it reaches the target shot distance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FindCrossingLerp(Vector3 posBefore, Vector3 posAfter, float shotDistance)
        {
            float lo = 0f;
            float hi = 1f;
            float mid = 0.5f;

            while (hi - lo > LerpTolerance)
            {
                if (Vector3.Lerp(posBefore, posAfter, mid).Z < shotDistance)
                    lo = mid;
                else
                    hi = mid;
                mid = (lo + hi) / 2f;
            }

            return mid;
        }
    }
}
