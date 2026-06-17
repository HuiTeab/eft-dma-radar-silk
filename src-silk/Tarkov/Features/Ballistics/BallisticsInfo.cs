// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Per-ammo ballistics inputs read from the loaded round's <c>AmmoTemplate</c> (plus weapon /
    /// attachment muzzle-velocity modifiers). Consumed by <see cref="BallisticsSimulation"/>.
    /// </summary>
    public sealed class BallisticsInfo
    {
        /// <summary>Muzzle velocity including weapon + attachment modifiers (m/s).</summary>
        public float BulletSpeed { get; set; }
        /// <summary>Bullet mass (grams).</summary>
        public float BulletMassGrams { get; set; }
        /// <summary>Bullet diameter (millimetres).</summary>
        public float BulletDiameterMillimeters { get; set; }
        /// <summary>G1 ballistic coefficient.</summary>
        public float BallisticCoefficient { get; set; }

        /// <summary>Returns true if the ammo/ballistics values are sane.</summary>
        public bool IsAmmoValid =>
            BulletMassGrams > 0f && BulletMassGrams < 2000f &&
            BulletSpeed > 1f && BulletSpeed < 2500f &&
            BallisticCoefficient >= 0f && BallisticCoefficient <= 3f &&
            BulletDiameterMillimeters > 0f && BulletDiameterMillimeters <= 100f;
    }
}
