// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Drop / travel-time helper for the HUD table. Fires a flat (horizontal) virtual shot toward the
    /// target's bearing and reports drop (m, positive-down) against a level line plus travel time (s).
    /// Thin wrapper over <see cref="BallisticsIntegrator"/> so the HUD numbers use the exact same physics
    /// as the drawn arc and the aim solver.
    /// </summary>
    public static class BallisticsSimulation
    {
        public static BallisticSimulationOutput Run(ref Vector3 startPosition, ref Vector3 endPosition, BallisticsInfo ballistics)
        {
            // Horizontal (XZ) range to the target — drop is measured against a level line, matching the
            // original behaviour of firing along +Z and reporting |Y|.
            float dx = endPosition.X - startPosition.X;
            float dz = endPosition.Z - startPosition.Z;
            float horizontalRange = MathF.Sqrt(dx * dx + dz * dz);
            if (horizontalRange < 0.01f)
                return new BallisticSimulationOutput(0f, 0f);

            var drag = new BallisticsIntegrator.DragParams(ballistics);
            Vector3 dir = new(dx, 0f, dz);
            if (BallisticsIntegrator.SolveImpactAtRange(
                    startPosition, dir, ballistics.BulletSpeed, drag, horizontalRange,
                    out var impact, out var travelTime))
            {
                return new BallisticSimulationOutput(MathF.Abs(impact.Y - startPosition.Y), travelTime);
            }
            return new BallisticSimulationOutput(0f, 0f);
        }
    }
}
