// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Inverse ballistics: given a muzzle, a target, and (optionally) the target's velocity, find the
    /// world-space point to aim at so the bullet hits — i.e. the super-elevation (holdover) for drop plus
    /// the lead for movement. Solved by fixed-point iteration on the aim point: fire a virtual shot,
    /// measure where it lands relative to the (lead-adjusted) target, and shift the aim by that miss.
    /// Because vertical impact moves ≈1:1 with vertical aim, this converges in a handful of iterations.
    /// Reuses <see cref="BallisticsIntegrator"/> so the solution is on the exact same physics as the
    /// drawn arc and the drop table.
    /// </summary>
    internal static class AimSolver
    {
        private const int MaxIterations = 6;
        private const float ConvergenceMeters = 0.05f; // 5 cm — well under a hitbox

        public static AimSolution Solve(
            Vector3 source, Vector3 target, Vector3 targetVelocity,
            float muzzleSpeed, BallisticsInfo info, bool leadTarget)
        {
            if (info is null || !info.IsAmmoValid || muzzleSpeed < 1f)
                return AimSolution.Invalid;

            float range0 = Vector3.Distance(source, target);
            if (range0 < 1f || range0 > 5000f)
                return AimSolution.Invalid;

            var drag = new BallisticsIntegrator.DragParams(info);
            Vector3 lead = leadTarget ? targetVelocity : Vector3.Zero;

            float travelTime = range0 / muzzleSpeed; // straight-line first guess
            Vector3 aimPoint = target;
            Vector3 leadTargetPt = target;
            bool converged = false;

            for (int i = 0; i < MaxIterations; i++)
            {
                leadTargetPt = target + lead * travelTime;

                Vector3 dir = aimPoint - source;
                if (dir.LengthSquared() < 1e-6f)
                    break;

                float hRange = HorizontalDistance(source, leadTargetPt);
                if (!BallisticsIntegrator.SolveImpactAtRange(
                        source, dir, muzzleSpeed, drag, hRange, out var impact, out var t))
                    return AimSolution.Invalid; // round can't reach that range

                travelTime = t;
                Vector3 error = leadTargetPt - impact;
                aimPoint += error;

                if (error.LengthSquared() <= ConvergenceMeters * ConvergenceMeters)
                {
                    converged = true;
                    break;
                }
            }

            if (!float.IsFinite(aimPoint.X) || !float.IsFinite(aimPoint.Y) || !float.IsFinite(aimPoint.Z))
                return AimSolution.Invalid;

            float holdover = aimPoint.Y - target.Y;
            float leadDist = HorizontalDistance(target, leadTargetPt);
            return new AimSolution(aimPoint, target, travelTime, holdover, leadDist, range0, true, converged);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
