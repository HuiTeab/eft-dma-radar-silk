// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Resolves the live muzzle (fireport) world pose for the local player's held firearm so the
    /// predicted arc starts at the real bore instead of the camera/eye. Chain:
    /// <c>playerBase + Player._handsController</c> → FirearmController → <c>+ Fireport</c> → Transform
    /// component <c>+0x10</c> → TransformInternal, then <see cref="UnityOffsets.ReadWorldPose"/>.
    /// The TransformInternal pointer is cached per-weapon; the world pose is read fresh each call
    /// (the muzzle moves). Returns false when the chain is unreadable (e.g. non-firearm in hands or a
    /// torn read) so the caller can fall back to the eye-look approximation.
    /// </summary>
    internal sealed class FireportResolver
    {
        /// <summary>Transform-component → TransformInternal pointer offset (m_CachedPtr), as in <c>Skeleton</c>.</summary>
        private const uint TransformInternalOffset = 0x10;

        private ulong _cachedWeapon;
        private ulong _cachedTransformInternal;

        public void Reset()
        {
            _cachedWeapon = 0;
            _cachedTransformInternal = 0;
        }

        /// <summary>
        /// Muzzle world position + unit forward direction for the given held <paramref name="weapon"/>.
        /// </summary>
        public bool TryGetMuzzle(ulong playerBase, ulong weapon, out Vector3 position, out Vector3 forward)
        {
            position = default;
            forward = default;
            if (playerBase == 0)
                return false;

            ulong ti = _cachedTransformInternal;
            if (ti == 0 || weapon == 0 || weapon != _cachedWeapon)
            {
                if (!TryResolveTransformInternal(playerBase, out ti))
                {
                    _cachedTransformInternal = 0;
                    return false;
                }
                _cachedWeapon = weapon;
                _cachedTransformInternal = ti;
            }

            if (!UnityOffsets.ReadWorldPose(ti, out var pos, out var rot))
            {
                // Transform may have been recreated (weapon swap mid-cache) — re-resolve next call.
                _cachedTransformInternal = 0;
                return false;
            }

            Vector3 fwd = Vector3.Transform(new Vector3(0f, 0f, 1f), rot);
            float len = fwd.Length();
            if (len < 1e-4f ||
                !float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
                return false;

            position = pos;
            forward = fwd / len;
            return true;
        }

        /// <summary>
        /// Live muzzle (fireport) world <paramref name="position"/> + <paramref name="rotation"/> for the
        /// local player's held firearm. Returns false when the firearm/transform chain is unreadable.
        /// Used by silent aim, which needs the fireport rotation to convert a world aim direction into the
        /// fireport-local frame stored in <c>ProceduralWeaponAnimation._shotDirection</c>.
        /// </summary>
        public bool TryGetPose(ulong playerBase, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (playerBase == 0)
                return false;
            if (!TryResolveTransformInternal(playerBase, out var ti))
                return false;
            return UnityOffsets.ReadWorldPose(ti, out position, out rotation);
        }

        private static bool TryResolveTransformInternal(ulong playerBase, out ulong transformInternal)
        {
            transformInternal = 0;
            if (!Memory.TryReadPtr(playerBase + Offsets.Player._handsController, out var hands, false)
                || !hands.IsValidVirtualAddress())
                return false;
            if (!Memory.TryReadPtr(hands + Offsets.FirearmController.Fireport, out var fireport, false)
                || !fireport.IsValidVirtualAddress())
                return false;
            // Fireport field → Transform component (+0x10) → TransformInternal / m_CachedPtr (+0x10).
            // Two hops: the Fireport pointer is a holder, not the Transform component directly
            // (reference chain To_FirePortTransformInternal = { Fireport, 0x10, 0x10 }). A single hop
            // resolves a non-Transform pointer, so the world-pose read fails.
            if (!Memory.TryReadPtr(fireport + TransformInternalOffset, out var transformComponent, false)
                || !transformComponent.IsValidVirtualAddress())
                return false;
            if (!Memory.TryReadPtr(transformComponent + TransformInternalOffset, out var ti, false)
                || !ti.IsValidVirtualAddress())
                return false;
            transformInternal = ti;
            return true;
        }
    }
}
