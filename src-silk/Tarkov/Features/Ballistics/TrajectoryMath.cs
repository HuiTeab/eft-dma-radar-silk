// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Forward-step trajectory sampler used by the debug overlay. Fills
    /// <paramref name="outPoints"/> with world-space positions along the predicted arc,
    /// stopping at <paramref name="maxDistance"/> or after the bullet hits the ground.
    /// </summary>
    public static class TrajectoryMath
    {
        /// <summary>
        /// Returns the number of samples actually written. Steps through
        /// <see cref="BallisticsIntegrator"/> so the drawn arc is identical to the drop table and the
        /// aim solver (same dt, same drag, same ½·a·dt² term).
        /// </summary>
        public static int BuildTrajectoryPoints(in ShotState shot, Span<Vector3> outPoints, float maxDistance)
        {
            if (outPoints.IsEmpty || !shot.IsValid) return 0;

            var drag = new BallisticsIntegrator.DragParams(shot.Ballistics);
            Vector3 pos = shot.SourcePosition;
            Vector3 vel = Vector3.Normalize(shot.InitialDirection) * shot.MuzzleSpeed;
            outPoints[0] = pos;
            int count = 1;

            float maxDistSq = maxDistance * maxDistance;
            for (int i = 1; i < outPoints.Length; i++)
            {
                if (vel.Length() < 1f) break;

                BallisticsIntegrator.Step(ref pos, ref vel, drag);
                outPoints[i] = pos;
                count = i + 1;

                if ((pos - shot.SourcePosition).LengthSquared() >= maxDistSq) break;
            }
            return count;
        }
    }
}
