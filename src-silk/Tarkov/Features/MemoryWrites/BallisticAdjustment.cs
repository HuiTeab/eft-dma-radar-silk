// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Features.Ballistics;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    /// <summary>
    /// Silent aim. While the aim-point toggle is on and a target is locked, overwrites the held
    /// weapon's shot direction (<c>ProceduralWeaponAnimation._shotDirection</c>) with the bore
    /// direction that lands the drop-/lead-compensated shot on the locked target — so fired rounds
    /// actually travel at the target regardless of where the crosshair points. The original direction
    /// (and the FOV-adjust flag) are restored whenever we stop aiming, so the gun behaves normally
    /// between locks.
    ///
    /// <para>Mechanism ported from the reference radar: the bullet's real trajectory is fixed at spawn
    /// from the fire direction, so patching an already-spawned <c>Shot.Velocity</c> only moves the
    /// visual tracer. Rewriting <c>_shotDirection</c> at fire time is what actually moves the hit.</para>
    ///
    /// <para>For best accuracy enable <b>Use Fireport Muzzle</b> — that makes the aim solver originate
    /// at the real bore, so the solved aim point matches the muzzle the round leaves from.</para>
    /// </summary>
    public sealed class BallisticAdjustment : MemWriteFeature<BallisticAdjustment>
    {
        /// <summary>Refresh cadence — keep the patched direction fresh as the target / muzzle move.</summary>
        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(20);

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.BallisticAdjustment;
            set => SilkProgram.Config.MemWrites.BallisticAdjustment = value;
        }

        private readonly FireportResolver _fireport = new();

        // Restore state — captured on the first patch, written back when we stop aiming so the
        // weapon's normal behaviour returns between locks (otherwise it stays hijacked).
        private bool _patched;
        private ulong _patchedPwa;
        private Vector3? _origShotDir;
        private bool? _origFovAdjust;

        // Last value we queued to _shotDirection — used next tick to read back and detect whether the
        // game is overwriting our patch (which would explain bullets not redirecting despite a correct write).
        private Vector3? _lastWrittenDir;

        public override void TryApply(ScatterWriteHandle writes)
        {
            const string cat = "BallisticAdjustment";
            var step = TimeSpan.FromSeconds(1);

            try
            {
                var ballistics = BallisticsFeature.Instance;
                if (ballistics is null) { Restore(); return; }

                // Only active while the aim-point toggle is on, a target is locked, and the solve is valid.
                var cfg = SilkProgram.Config?.Ballistics;
                if (cfg is null || !cfg.DrawAimPoint)
                {
                    Restore();
                    Log.WriteRateLimited(AppLogLevel.Info, "ba.aim", step, "idle: Aim Point toggle OFF", cat);
                    return;
                }
                if (ballistics.AimTargetBase == 0)
                {
                    Restore();
                    Log.WriteRateLimited(AppLogLevel.Info, "ba.target", step, "idle: no target locked", cat);
                    return;
                }
                if (ballistics.LocalAimSolution is null or { Valid: false })
                {
                    Restore();
                    Log.WriteRateLimited(AppLogLevel.Info, "ba.solution", step, "idle: aim solution not valid", cat);
                    return;
                }
                var aim = ballistics.LocalAimSolution.Value;

                if (Memory.Game is not LocalGameWorld game) { Restore(); return; }
                var localPlayer = game.LocalPlayer;
                if (localPlayer is null || !localPlayer.IsWeaponInHands)
                {
                    Restore();
                    Log.WriteRateLimited(AppLogLevel.Info, "ba.weapon", step, "idle: no weapon in hands", cat);
                    return;
                }

                // Live muzzle (fireport) pose — position + rotation. Rotation converts the world aim
                // direction into the fireport-local frame the game stores _shotDirection in.
                if (!_fireport.TryGetPose(localPlayer.Base, out var fpPos, out var fpRot))
                {
                    Restore();
                    Log.WriteRateLimited(AppLogLevel.Info, "ba.fireport", step, "blocked: fireport pose not resolved", cat);
                    return;
                }

                // ProceduralWeaponAnimation owns _shotDirection.
                if (!Memory.TryReadPtr(localPlayer.Base + Offsets.Player.ProceduralWeaponAnimation, out var pwa, false)
                    || !pwa.IsValidVirtualAddress())
                {
                    Restore();
                    Log.WriteRateLimited(AppLogLevel.Info, "ba.pwa", step, "blocked: ProceduralWeaponAnimation not resolved", cat);
                    return;
                }

                // Read-back: did last tick's write survive? If the live _shotDirection no longer matches
                // what we wrote (or the FOV flag flipped back to true), the game is overwriting our patch
                // between writes — meaning the value is correct but never reaches the bullet at fire time.
                if (_lastWrittenDir.HasValue)
                {
                    var liveDir = Memory.ReadValue<Vector3>(pwa + Offsets.ProceduralWeaponAnimation._shotDirection, false);
                    var liveFov = Memory.ReadValue<bool>(pwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments, false);
                    float drift = (liveDir - _lastWrittenDir.Value).Length();
                    if (drift > 0.05f || liveFov)
                        Log.WriteRateLimited(AppLogLevel.Info, "ba.revert", TimeSpan.FromSeconds(1),
                            $"patch reverted by game: wrote dir=({_lastWrittenDir.Value.X:F3},{_lastWrittenDir.Value.Y:F3},{_lastWrittenDir.Value.Z:F3}) " +
                            $"read=({liveDir.X:F3},{liveDir.Y:F3},{liveDir.Z:F3}) drift={drift:F3} fovFlag={liveFov}", cat);
                    else
                        Log.WriteRateLimited(AppLogLevel.Info, "ba.hold", TimeSpan.FromSeconds(1),
                            "patch holding: _shotDirection survived since last tick (fovFlag stayed false)", cat);
                }

                // World bore direction that lands the shot on the target, then in the fireport-local frame.
                Vector3 worldDir = aim.AimPoint - fpPos;
                if (worldDir.LengthSquared() < 1e-6f) { Restore(); return; }
                worldDir = Vector3.Normalize(worldDir);
                Vector3 localDir = InverseTransformDirection(fpRot, worldDir);
                if (!float.IsFinite(localDir.X) || !float.IsFinite(localDir.Y) || !float.IsFinite(localDir.Z))
                {
                    Restore();
                    return;
                }

                // Capture originals on the first patch so we can put them back when we stop aiming.
                if (!_patched)
                {
                    _origShotDir = Memory.ReadValue<Vector3>(pwa + Offsets.ProceduralWeaponAnimation._shotDirection, false);
                    _origFovAdjust = Memory.ReadValue<bool>(pwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments, false);
                    _patchedPwa = pwa;
                }

                // Patch: write our direction and disable the game's FOV-based shot adjustment so it sticks.
                writes.AddValueEntry(pwa + Offsets.ProceduralWeaponAnimation._shotDirection, localDir);
                writes.AddValueEntry(pwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments, false);
                _patched = true;
                _lastWrittenDir = localDir;

                var targetName = ballistics.AimTargetName ?? "Unknown";
                Log.WriteRateLimited(AppLogLevel.Info, "ba.active", TimeSpan.FromMilliseconds(500),
                    $"silent-aim ACTIVE → {targetName} ({aim.Distance:F0}m, holdover {aim.Holdover * 100f:F0}cm)", cat);

                // Vector diagnostic — lets us verify the written bore direction is sane. When aiming
                // roughly forward, localDir should be ≈ (0, small, ~1): forward in fireport-local space,
                // slightly up for holdover. A negative Z (firing backward), non-unit length, or a wild
                // axis means the fireport rotation / frame conversion is off — not a tuning issue.
                Log.WriteRateLimited(AppLogLevel.Info, "ba.vec", TimeSpan.FromSeconds(1),
                    $"vec fpPos=({fpPos.X:F2},{fpPos.Y:F2},{fpPos.Z:F2}) aim=({aim.AimPoint.X:F2},{aim.AimPoint.Y:F2},{aim.AimPoint.Z:F2}) " +
                    $"worldDir=({worldDir.X:F3},{worldDir.Y:F3},{worldDir.Z:F3}) localDir=({localDir.X:F3},{localDir.Y:F3},{localDir.Z:F3}) " +
                    $"|local|={localDir.Length():F3}", cat);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[BallisticAdjustment] Error: {ex.Message}");
                Restore();
            }
        }

        /// <summary>Restores the weapon's original shot direction + FOV-adjust flag, if we patched them.</summary>
        private void Restore()
        {
            if (!_patched)
                return;
            try
            {
                if (_patchedPwa.IsValidVirtualAddress())
                {
                    if (_origShotDir.HasValue)
                        Memory.WriteValue(_patchedPwa + Offsets.ProceduralWeaponAnimation._shotDirection, _origShotDir.Value);
                    Memory.WriteValue(_patchedPwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments, _origFovAdjust ?? true);
                }
            }
            catch { /* best-effort restore */ }
            finally
            {
                _patched = false;
                _patchedPwa = 0;
                _origShotDir = null;
                _origFovAdjust = null;
                _lastWrittenDir = null;
            }
        }

        /// <summary>Unity Transform.InverseTransformDirection (world → local). Ported verbatim so the
        /// quaternion convention matches the reference exactly.</summary>
        private static Vector3 InverseTransformDirection(Quaternion rotation, Vector3 direction)
        {
            var conjugate = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, rotation.W);
            return MultiplyQuaternionVector(conjugate, direction);
        }

        private static Vector3 MultiplyQuaternionVector(Quaternion rotation, Vector3 point)
        {
            float num = rotation.X * 2f;
            float num2 = rotation.Y * 2f;
            float num3 = rotation.Z * 2f;
            float num4 = rotation.X * num;
            float num5 = rotation.Y * num2;
            float num6 = rotation.Z * num3;
            float num7 = rotation.X * num2;
            float num8 = rotation.X * num3;
            float num9 = rotation.Y * num3;
            float num10 = rotation.W * num;
            float num11 = rotation.W * num2;
            float num12 = rotation.W * num3;
            Vector3 result;
            result.X = (1f - (num5 + num6)) * point.X + (num7 - num12) * point.Y + (num8 + num11) * point.Z;
            result.Y = (num7 + num12) * point.X + (1f - (num4 + num6)) * point.Y + (num9 - num10) * point.Z;
            result.Z = (num8 - num11) * point.X + (num9 + num10) * point.Y + (1f - (num4 + num5)) * point.Z;
            return result;
        }

        public override void OnRaidStart()
        {
            _patched = false;
            _patchedPwa = 0;
            _origShotDir = null;
            _origFovAdjust = null;
            _lastWrittenDir = null;
            _fireport.Reset();
            Log.WriteLine("[BallisticAdjustment] Raid started.");
        }

        public override void OnRaidEnd()
        {
            Restore();
            _fireport.Reset();
            Log.WriteLine("[BallisticAdjustment] Raid ended.");
        }
    }
}
