// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// The single source of truth for EFT bullet physics. Integrates with semi-implicit (symplectic)
    /// Euler at a fixed step, applying the ½·a·dt² position term — the scheme reverse-engineered to
    /// match <c>EFT.Ballistics.BallisticsCalculator</c>. Every ballistics consumer (predicted arc,
    /// drop / travel-time table, aim solver) steps through here so they can never disagree with each
    /// other. Drag uses <see cref="G1Table.CalculateDragCoefficient"/>.
    /// </summary>
    public static class BallisticsIntegrator
    {
        /// <summary>Fixed integration step (seconds). Matches the RE'd EFT calculator.</summary>
        public const float TimeStep = 0.01f;

        /// <summary>½·dt² — the position correction term (== the original code's 5E-05f at dt=0.01).</summary>
        private const float HalfStepSq = 0.5f * TimeStep * TimeStep;

        /// <summary>Max steps before giving up (1300 × 0.01s ≈ 13s of flight).</summary>
        private const int MaxIterations = 1300;

        private static readonly Vector3 _gravity = new(0f, -9.81f, 0f);

        /// <summary>
        /// Per-ammo drag constants hoisted out of the step loop. Build once per shot, reuse each step.
        /// Mirrors the constants computed inline by the original <see cref="BallisticsSimulation"/>.
        /// </summary>
        public readonly struct DragParams
        {
            public readonly float MassX2;   // 2·m  (kg)
            public readonly float Slowdown; // BC-derived drag scale
            public readonly float AirArea;  // 1.2 · cross-sectional area

            public DragParams(BallisticsInfo info)
            {
                float massKg = info.BulletMassGrams / 1000f;
                MassX2 = massKg * 2f;
                float diameterM = info.BulletDiameterMillimeters / 1000f;
                Slowdown = massKg * 0.0014223f / (diameterM * diameterM * info.BallisticCoefficient);
                float area = diameterM * diameterM * 3.1415927f / 4f;
                AirArea = 1.2f * area;
            }
        }

        /// <summary>Total acceleration (gravity + G1 drag) for the given velocity. Pure function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Acceleration(Vector3 velocity, in DragParams p)
        {
            float magnitude = velocity.Length();
            if (magnitude < 1e-4f)
                return _gravity;
            float dragCoefficient = G1Table.CalculateDragCoefficient(magnitude) * p.Slowdown;
            // gravity + (1.2·area)·(-Cd)·v²/(2m) · v̂
            return _gravity + p.AirArea * -dragCoefficient * magnitude * magnitude / p.MassX2 * (velocity / magnitude);
        }

        /// <summary>
        /// Advance one fixed step in place. Position carries the ½·a·dt² term; velocity is forward-Euler.
        /// Reproduces <see cref="BallisticsSimulation"/>'s original update exactly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref Vector3 pos, ref Vector3 vel, in DragParams p)
        {
            Vector3 a = Acceleration(vel, p);
            pos += vel * TimeStep + HalfStepSq * a;
            vel += a * TimeStep;
        }

        /// <summary>
        /// Fire a virtual shot from <paramref name="source"/> along <paramref name="direction"/> and
        /// return the 3D point where it crosses horizontal (XZ) range <paramref name="horizontalRange"/>
        /// from the source, plus the travel time to reach it. The crossing is refined by a short
        /// bisection so the result lands exactly on the requested range.
        /// </summary>
        /// <returns>False if the shot never reaches that range within the iteration budget.</returns>
        public static bool SolveImpactAtRange(
            Vector3 source, Vector3 direction, float muzzleSpeed, in DragParams p,
            float horizontalRange, out Vector3 impact, out float travelTime)
        {
            impact = source;
            travelTime = 0f;
            if (horizontalRange <= 0.01f)
                return true;

            Vector3 dir = Vector3.Normalize(direction);
            if (!float.IsFinite(dir.X) || !float.IsFinite(dir.Y) || !float.IsFinite(dir.Z))
                return false;

            Vector3 pos = source;
            Vector3 vel = dir * muzzleSpeed;
            float rangeSq = horizontalRange * horizontalRange;
            float time = 0f;

            for (int i = 0; i < MaxIterations; i++)
            {
                Vector3 lastPos = pos;
                float lastTime = time;

                Step(ref pos, ref vel, p);
                time += TimeStep;

                float hx = pos.X - source.X;
                float hz = pos.Z - source.Z;
                if (hx * hx + hz * hz >= rangeSq)
                {
                    float frac = HorizontalLerp(source, lastPos, pos, horizontalRange);
                    impact = Vector3.Lerp(lastPos, pos, frac);
                    travelTime = float.Lerp(lastTime, time, frac);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Bisection: fraction along before→after at which XZ range from source == target.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HorizontalLerp(Vector3 source, Vector3 before, Vector3 after, float range)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector3 pt = Vector3.Lerp(before, after, mid);
                float hx = pt.X - source.X;
                float hz = pt.Z - source.Z;
                if (MathF.Sqrt(hx * hx + hz * hz) < range) lo = mid;
                else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }
    }
}
