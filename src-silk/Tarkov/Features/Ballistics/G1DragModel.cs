// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>One (Mach, drag-coefficient) sample of the standard G1 projectile drag curve.</summary>
    internal readonly struct G1DragModel
    {
        public readonly float Mach;
        public readonly float Drag;

        public G1DragModel(float mach, float drag)
        {
            Mach = mach;
            Drag = drag;
        }
    }
}
