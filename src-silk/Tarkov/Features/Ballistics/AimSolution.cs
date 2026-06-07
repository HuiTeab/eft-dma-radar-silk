// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Result of <see cref="AimSolver"/>: the world-space point you must place your crosshair on so the
    /// bullet (accounting for drop and moving-target lead) hits the target, plus the numbers behind it
    /// for the debug HUD.
    /// </summary>
    public readonly struct AimSolution
    {
        /// <summary>World point to cover with the reticle (drop- and lead-compensated).</summary>
        public readonly Vector3 AimPoint;
        /// <summary>The actual target bone position the solve was anchored to.</summary>
        public readonly Vector3 TargetBonePos;
        /// <summary>Bullet flight time to the (lead-adjusted) target (seconds).</summary>
        public readonly float TravelTime;
        /// <summary>Vertical offset of the aim point above the target bone (meters; the super-elevation).</summary>
        public readonly float Holdover;
        /// <summary>Horizontal lead distance applied for target movement (meters).</summary>
        public readonly float Lead;
        /// <summary>Straight-line range from muzzle to target (meters).</summary>
        public readonly float Distance;
        /// <summary>Whether the solve produced a usable result.</summary>
        public readonly bool Valid;
        /// <summary>Whether the iteration reached the convergence tolerance (vs hit the iteration cap).</summary>
        public readonly bool Converged;

        public AimSolution(Vector3 aimPoint, Vector3 targetBonePos, float travelTime,
            float holdover, float lead, float distance, bool valid, bool converged)
        {
            AimPoint = aimPoint;
            TargetBonePos = targetBonePos;
            TravelTime = travelTime;
            Holdover = holdover;
            Lead = lead;
            Distance = distance;
            Valid = valid;
            Converged = converged;
        }

        /// <summary>A non-result (Valid == false).</summary>
        public static AimSolution Invalid => default;
    }
}
