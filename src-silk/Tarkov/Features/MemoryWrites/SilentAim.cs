// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Numerics;
using System.Threading;
using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.GameWorld;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins;
using eft_dma_radar.Silk.Tarkov.Features.Ballistics;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    /// <summary>
    /// Silent aim ("bullet redirect"). Picks the hostile/AI target nearest the crosshair and steers
    /// the next fired round onto it while the player's view stays put.
    ///
    /// Unlike the other mem-write features, the hot path runs on its <b>own dedicated thread</b> in a
    /// tight, DMA-paced loop rather than the shared ~100 Hz FeatureManager tick. The write that
    /// actually steers the bullet (the <c>get_WeaponDirection</c> patch) is what benefits most from a
    /// high refresh rate: against a fast, close target the dominant error is how stale the aim
    /// direction is between the bone read and the shot, so updating it as fast as the card allows is
    /// the single biggest accuracy lever. The loop is paced by its own DMA reads (no busy-spin), and
    /// it does direct writes instead of queueing onto the shared scatter handle.
    ///
    /// The aim direction is computed muzzle → target bone in world space and applied one of two ways
    /// (see <see cref="MemWritesConfig.SilentAimMethod"/>):
    ///   • WeaponDirection — patch <c>FirearmController.get_WeaponDirection()</c> to return the world
    ///     aim vector. That is the direction the game fires along, so the round lands on it.
    ///   • ShotDirection — write <c>ProceduralWeaponAnimation._shotDirection</c> directly, in the
    ///     fireport's local space. A lighter-weight fallback.
    ///
    /// In both cases <c>ShotNeedsFovAdjustments</c> is cleared so the game uses our direction
    /// verbatim, and — when Perfect Accuracy is on — <c>FirearmController.TotalCenterOfImpact</c> is
    /// pinned near zero so the weapon's spread doesn't scatter the redirected round.
    ///
    /// Gated behind the master mem-writes toggle, this feature's toggle, in-raid state, and — when
    /// the "SilentAim" hotkey is bound — its engage key (hold or toggle).
    /// </summary>
    public sealed class SilentAim : MemWriteFeature<SilentAim>
    {
        public SilentAim()
        {
            // Dedicated worker — the hot aim loop lives here, not on the shared FeatureManager tick.
            new Thread(Worker)
            {
                IsBackground = true,
                Name = "SilentAim",
                Priority = ThreadPriority.AboveNormal,
            }.Start();
        }

        // The dedicated thread drives everything; keep this feature out of the FeatureManager tick.
        public override bool CanRun => false;
        public override void TryApply(ScatterWriteHandle writes) { }

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.SilentAim;
            set => SilkProgram.Config.MemWrites.SilentAim = value;
        }

        private ulong _handsController;
        private ulong _fireportTI;

        /// <summary>
        /// Engage state driven by the "SilentAim" hotkey (hold or toggle, see HotkeyManager). Only
        /// consulted when that hotkey is bound; unbound → silent aim is always-on.
        /// </summary>
        public static bool Engaged;

        /// <summary>Player currently flagged <see cref="Player.IsAimbotLocked"/>, tracked so the flag clears when the target changes.</summary>
        private Player? _lockedTarget;

        // Per-ammo ballistics cache — refreshed when the held weapon changes (by Item.Version).
        private BallisticsInfo? _ballistics;
        private int _lastWeaponVersion = -1;

        // Local player's view-rotation write address (MovementContext._rotation), cached per raid.
        private ulong _localRotAddr;

        // State for the get_WeaponDirection() code patch: target address, saved original bytes, and
        // whether the patch is currently live.
        private ulong _getterAddr;
        private byte[]? _getterOriginal;
        private bool _getterPatched;
        private const int GetterPatchLen = 24;
        // Last direction actually written + when, so we can skip re-writing an unchanged patch.
        private Vector3 _lastPatchedDir;
        private long _lastPatchMs;

        // Bones considered when the aim-bone setting is "Auto" (nearest to the crosshair wins).
        private static readonly Bones[] AutoBones =
        [
            Bones.HumanHead, Bones.HumanNeck, Bones.HumanSpine3, Bones.HumanSpine2,
            Bones.HumanSpine1, Bones.HumanPelvis, Bones.HumanLThigh2, Bones.HumanRThigh2,
        ];

        // Projectile gravity (m/s²) used by the flat-velocity fallback when real ballistics are off.
        private const float Gravity = 9.81f;

        // Value the cone-of-impact is pinned to while aiming (FirearmController.TotalCenterOfImpact).
        // Near zero so the redirected round leaves dead-centre instead of scattering with spread.
        private const float PerfectCoi = 0.0003f;

        // Throttle for the optional debug line (gated on Log.EnableDebugLogging — toggle in-app).
        private long _lastDiagMs;
        // Aim-refresh rate counter (full passes/sec while engaged) for the debug line.
        private int _rateCount;
        private long _rateWindowMs;

        // Position-derived velocity fallback for the locked target. The game's observed Velocity is
        // only populated for ObservedPlayerView entities (online enemies); offline/local-class AI
        // report zero, so we differentiate their root position over time to still lead them.
        private Player? _velTarget;
        private Vector3 _velLastPos;
        private long _velLastMs;
        private Vector3 _derivedVel;

        // Random-bone-per-shot state: re-rolled when the weapon's shot counter advances.
        private sbyte _lastShotIndex = -1;
        private int _randomBoneChoice;
        private static readonly int[] RandomBoneChoices = [0, 1, 2, 4]; // Head, Chest, Pelvis, Neck

        /// <summary>
        /// Dedicated aim loop. While engaged it runs back-to-back (paced by its own DMA reads, no
        /// sleep) so the steering write refreshes as fast as the card allows; while idle it sleeps.
        /// </summary>
        private void Worker()
        {
            while (true)
            {
                try
                {
                    if (!ShouldRun(out var game, out var local))
                    {
                        // Not aiming: undo the code patch and drop the lock, then idle.
                        if (_getterPatched)
                            RestoreGetter();
                        if (_lockedTarget is not null)
                            SetLocked(null);
                        Thread.Sleep(15);
                        continue;
                    }

                    RunOnce(game, local);
                    Thread.Yield(); // stay friendly to the realtime worker; reads already pace us
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[SilentAim]: {ex.Message}");
                    try { RestoreGetter(); } catch { }
                    _handsController = 0;
                    _fireportTI = 0;
                    Thread.Sleep(50);
                }
            }
        }

        /// <summary>Gate for the worker loop — true only when silent aim should actively redirect.</summary>
        private bool ShouldRun(out LocalGameWorld game, out LocalPlayer local)
        {
            game = null!;
            local = null!;
            if (!SilkProgram.Config.MemWritesEnabled || !Enabled || !IsEngaged() || !Memory.InRaid)
                return false;
            if (Memory.Game is not LocalGameWorld g)
                return false;
            if (Memory.LocalPlayer is not LocalPlayer lp || !lp.Base.IsValidVirtualAddress() || !lp.PWA.IsValidVirtualAddress())
                return false;
            game = g;
            local = lp;
            return true;
        }

        /// <summary>One pass of the aim pipeline: resolve target + aim point, then write directly to memory.</summary>
        private void RunOnce(LocalGameWorld game, LocalPlayer local)
        {
            ulong pwa = local.PWA;

            if (!ResolveFireport(local))
                return;
            if (!UnityOffsets.ReadWorldPose(_fireportTI, out var fireportPos, out var fireportRot))
                return;

            bool safe;
            try { safe = Memory.InRaid && game.IsSafeToWriteMem; }
            catch { safe = false; }
            if (!safe)
                return;

            var cfg = SilkProgram.Config.MemWrites;

            // Pin the weapon's cone-of-impact near zero so the redirected round isn't scattered by
            // spread. Read-guarded: only write when the current value is sane and not already pinned.
            float coiVal = float.NaN;
            if (cfg.SilentAimPerfectAccuracy && _handsController.IsValidVirtualAddress() &&
                Memory.TryReadValue<float>(_handsController + Offsets.FirearmController.TotalCenterOfImpact, out var coi, false))
            {
                coiVal = coi;
                if (coi > 0f && coi < 1f && MathF.Abs(coi - PerfectCoi) > 1e-6f)
                {
                    try { Memory.WriteValue(_handsController + Offsets.FirearmController.TotalCenterOfImpact, PerfectCoi); }
                    catch { }
                }
            }

            // Sticky lock: keep the current target while it's still valid; otherwise pick a new one.
            Player? target = (cfg.SilentAimStickyTarget && _lockedTarget is not null
                              && IsStillValidTarget(_lockedTarget, local, cfg))
                ? _lockedTarget
                : GetBestTarget(local, cfg);
            if (target is null)
            {
                SetLocked(null);
                RestoreGetter();
                return;
            }
            SetLocked(target);

            // Resolve a usable target velocity for leading (observed value, or position-derived).
            Vector3 leadVel = ResolveLeadVelocity(target);

            // Pick the bone to aim: Headshot-AI override, then random-bone, else the configured bone.
            int boneChoice = EffectiveBoneChoice(target, cfg);

            // Live re-read on the locked target so a moving enemy's aim point isn't a few frames stale.
            if (!TryResolveAimPoint(target, boneChoice, true, out var aimPoint))
                return;

            // Prediction: lead the target over the round's travel time (+ read→impact latency) and
            // raise the aim point to cancel bullet drop.
            float predLead = 0f, predDrop = 0f; // for the debug line below
            if (cfg.SilentAimPredict)
            {
                float travelTime;
                if (cfg.SilentAimRealBallistics && TryGetBallistics(local, out var info) && info.IsAmmoValid)
                {
                    var sim = BallisticsSimulation.Run(ref fireportPos, ref aimPoint, info);
                    travelTime = sim.TravelTime;
                    predDrop = sim.DropCompensation;
                }
                else
                {
                    float v = cfg.SilentAimMuzzleVelocity > 1f ? cfg.SilentAimMuzzleVelocity : 850f;
                    travelTime = Vector3.Distance(fireportPos, aimPoint) / v;
                    predDrop = 0.5f * Gravity * travelTime * travelTime;
                }
                predLead = travelTime + MathF.Max(0f, cfg.SilentAimLatencyMs) / 1000f;
                aimPoint += leadVel * predLead; // lead
                aimPoint.Y += predDrop;         // aim higher to cancel drop
            }

            Vector3 worldDir = aimPoint - fireportPos;
            if (worldDir.LengthSquared() < 1e-4f)
                return;
            worldDir = Vector3.Normalize(worldDir);
            _rateCount++; // one full aim pass — counted for the Hz readout below

            // Optional diagnostic (toggle debug logging in-app) — one line/sec while engaged. The
            // hz field is the aim-refresh rate this dedicated thread is actually achieving (the whole
            // point of moving off the shared ~100Hz tick).
            if (Log.EnableDebugLogging)
            {
                long now = Environment.TickCount64;
                if (now - _lastDiagMs >= 1000)
                {
                    long winMs = now - _rateWindowMs;
                    int hz = (_rateWindowMs > 0 && winMs > 0) ? (int)(_rateCount * 1000L / winMs) : 0;
                    _rateCount = 0;
                    _rateWindowMs = now;
                    _lastDiagMs = now;
                    float dist = Vector3.Distance(fireportPos, aimPoint);
                    float spd = leadVel.Length();
                    Log.WriteLine(
                        $"[SilentAim] tgt='{target.Name}' bone={boneChoice} dist={dist:F1}m " +
                        $"spd={spd:F1}m/s lead={predLead * 1000f:F0}ms drop={predDrop:F3}m hz={hz} " +
                        $"coi={coiVal:F5} method={cfg.SilentAimMethod} dir=<{worldDir.X:F2},{worldDir.Y:F2},{worldDir.Z:F2}>");
                }
            }

            try
            {
                // Make the game fire along our direction verbatim instead of re-aiming to the crosshair.
                Memory.WriteValue(pwa + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments, false);

                if (cfg.SilentAimMethod == 1)
                {
                    // Patch get_WeaponDirection() to return the world aim vector (authoritative).
                    ApplyWeaponDirectionPatch(worldDir);
                }
                else
                {
                    // Direct shot-direction write. The field lives in fireport-local space, so
                    // transform the world vector into it unless the world-space override is set.
                    RestoreGetter();
                    Vector3 localDir = Vector3.Transform(worldDir, Quaternion.Conjugate(fireportRot));
                    Memory.WriteValue(pwa + Offsets.ProceduralWeaponAnimation._shotDirection,
                        cfg.SilentAimWorldSpace ? worldDir : localDir);
                }

                // Optional: also snap the view rotation onto the target (very accurate, moves the view).
                if (cfg.SilentAimMemoryAim && TryResolveLocalRotAddr(local, out var rotAddr))
                    Memory.WriteValue(rotAddr, WorldDirToYawPitch(worldDir));
            }
            catch
            {
                // Transient write failure — leave the patch as-is and retry next pass.
            }
        }

        /// <summary>
        /// Resolves and caches the fireport (muzzle) transform from
        /// LocalPlayer._handsController → FirearmController.Fireport → transformInternal. Re-resolved
        /// whenever the held weapon changes.
        /// </summary>
        private bool ResolveFireport(LocalPlayer local)
        {
            if (!Memory.TryReadPtr(local.Base + Offsets.Player._handsController, out var hc, false) || hc == 0)
                return false;

            if (hc != _handsController || !_fireportTI.IsValidVirtualAddress())
            {
                _handsController = hc;
                _fireportTI = 0;

                // handsController → Fireport(0x150) → +0x10 → +0x10 → transformInternal
                Span<uint> chain = [Offsets.FirearmController.Fireport, 0x10, 0x10];
                if (!Memory.TryReadPtrChain(hc, chain, out var ti, false) || !ti.IsValidVirtualAddress())
                    return false;
                _fireportTI = ti;
            }
            return _fireportTI.IsValidVirtualAddress();
        }

        /// <summary>
        /// Picks the best hostile/AI target within distance and FOV. Priority is either nearest to
        /// the crosshair (mode 0) or nearest by distance (mode 1); both require the aim bone to be
        /// inside the FOV cone. Honours the visible-only filter and the configured aim bone.
        /// </summary>
        private static Player? GetBestTarget(LocalPlayer local, MemWritesConfig cfg)
        {
            var players = Memory.Players;
            if (players is null)
                return null;

            bool closest = cfg.SilentAimTargetMode == 1;
            bool visOnly = cfg.SilentAimVisibleOnly;
            float maxDistSq = cfg.SilentAimDistance * cfg.SilentAimDistance;
            Player? best = null;
            float bestScore = float.MaxValue;
            foreach (var p in players)
            {
                if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive)
                    continue;
                if (p.Type == PlayerType.Teammate)
                    continue;
                if (visOnly && !p.IsVisible)
                    continue;
                float distSq = Vector3.DistanceSquared(local.Position, p.Position);
                if (distSq > maxDistSq)
                    continue;
                if (!TryResolveAimPoint(p, cfg.SilentAimBone, false, out var aim))
                    continue;
                if (!CameraManager.WorldToScreen(ref aim, out var scr, true))
                    continue;
                float fov = CameraManager.GetFovMagnitude(scr);
                if (fov > cfg.SilentAimFov)
                    continue;
                // distSq is monotonic in distance, so it orders "closest" identically without the sqrt.
                float score = closest ? distSq : fov;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>Maps a bone choice to a skeleton bone (0=Head, 1=Chest, 2=Pelvis, 4=Neck, 5=Legs). Auto (3) is handled separately.</summary>
        private static Bones BoneFor(int choice) => choice switch
        {
            1 => Bones.HumanSpine3, // Chest
            2 => Bones.HumanPelvis, // Pelvis
            4 => Bones.HumanNeck,   // Neck
            5 => Random.Shared.Next(0, 2) == 0 ? Bones.HumanLThigh2 : Bones.HumanRThigh2, // Legs (random thigh)
            _ => Bones.HumanHead,   // Head (0) + fallback
        };

        /// <summary>True if the target is AI-controlled (scav/raider/boss/BTR), not a human player.</summary>
        private static bool IsAiTarget(Player p) => !p.IsHuman;

        /// <summary>
        /// Effective aim-bone choice for the locked target: Head on AI when Headshot-AI is on, else a
        /// per-shot random bone when Random Bone is on, otherwise the configured bone.
        /// </summary>
        private int EffectiveBoneChoice(Player target, MemWritesConfig cfg)
        {
            if (cfg.SilentAimHeadshotAI && IsAiTarget(target))
                return 0; // Head

            if (cfg.SilentAimRandomBone)
            {
                // Re-roll only when the weapon's shot counter changes (i.e. a round was actually fired).
                if (_handsController.IsValidVirtualAddress()
                    && Memory.TryReadValue<sbyte>(_handsController + Offsets.ClientFirearmController.ShotIndex, out var si, false)
                    && si != _lastShotIndex)
                {
                    _lastShotIndex = si;
                    _randomBoneChoice = RandomBoneChoices[Random.Shared.Next(RandomBoneChoices.Length)];
                }
                return _randomBoneChoice;
            }

            return cfg.SilentAimBone;
        }

        /// <summary>
        /// True while <paramref name="p"/> remains a usable lock: active, alive, hostile (not teammate),
        /// within distance, the aim bone resolves, and it's inside the FOV cone (visible too if required).
        /// Drives the sticky-target hold and the safe-unlock once the target leaves the cone.
        /// </summary>
        private static bool IsStillValidTarget(Player p, LocalPlayer local, MemWritesConfig cfg)
        {
            if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive || p.Type == PlayerType.Teammate)
                return false;
            if (cfg.SilentAimVisibleOnly && !p.IsVisible)
                return false;
            if (Vector3.DistanceSquared(local.Position, p.Position) > cfg.SilentAimDistance * cfg.SilentAimDistance)
                return false;
            if (!TryResolveAimPoint(p, cfg.SilentAimBone, false, out var aim))
                return false;
            if (!CameraManager.WorldToScreen(ref aim, out var scr, true))
                return false;
            return CameraManager.GetFovMagnitude(scr) <= cfg.SilentAimFov;
        }

        /// <summary>
        /// Resolves a player's world aim point for the given bone choice (3 = Auto picks the bone
        /// nearest the crosshair). Selection always uses the cheap cached bone positions; when
        /// <paramref name="live"/> is set the chosen bone is re-read fresh so the locked target's
        /// aim point doesn't lag a few frames behind a moving enemy.
        /// </summary>
        private static bool TryResolveAimPoint(Player p, int boneChoice, bool live, out Vector3 aimPoint)
        {
            aimPoint = default;
            var skel = p.Skeleton;
            if (skel is null)
                return false;

            Bones chosen;
            if (boneChoice == 3) // Auto — bone nearest the crosshair, from cached positions
            {
                chosen = Bones.HumanHead;
                float best = float.MaxValue;
                bool found = false;
                foreach (var b in AutoBones)
                {
                    if (skel.GetBonePosition(b) is not Vector3 bp)
                        continue;
                    if (!CameraManager.WorldToScreen(ref bp, out var s, true))
                        continue;
                    float fov = CameraManager.GetFovMagnitude(s);
                    if (fov < best)
                    {
                        best = fov;
                        chosen = b;
                        aimPoint = bp;
                        found = true;
                    }
                }
                if (!found)
                    return false;
            }
            else
            {
                chosen = BoneFor(boneChoice);
                if (skel.GetBonePosition(chosen) is not Vector3 pos)
                    return false;
                aimPoint = pos;
            }

            if (live && skel.TryGetBonePositionLive(chosen, out var livePos))
                aimPoint = livePos;
            return true;
        }

        /// <summary>
        /// Reads the held weapon's loaded-ammo ballistics (muzzle velocity + drag inputs), cached and
        /// refreshed when the weapon's item version changes. Returns false if no ammo can be read.
        /// </summary>
        private bool TryGetBallistics(LocalPlayer local, out BallisticsInfo info)
        {
            // Only (re)read the ammo when the weapon's item version changes and every read succeeds.
            // On a transient read miss we keep the last good values rather than dropping to the flat
            // fallback, which would otherwise make the predicted drop/lead jitter frame-to-frame.
            ulong itemBase = HandsManager.GetCachedItem(local.Base);
            if (itemBase.IsValidVirtualAddress()
                && Memory.TryReadValue<int>(itemBase + Offsets.LootItem.Version, out int ver)
                && (_ballistics is null || ver != _lastWeaponVersion)
                && FirearmManager.TryGetAmmoTemplate(itemBase, out var ammo) && ammo != 0)
            {
                var bi = new BallisticsInfo();
                Memory.TryReadValue<float>(ammo + Offsets.AmmoTemplate.InitialSpeed, out var spd, false);
                Memory.TryReadValue<float>(ammo + Offsets.AmmoTemplate.BallisticCoeficient, out var bc, false);
                Memory.TryReadValue<float>(ammo + Offsets.AmmoTemplate.BulletMassGram, out var mass, false);
                Memory.TryReadValue<float>(ammo + Offsets.AmmoTemplate.BulletDiameterMilimeters, out var diam, false);
                // Scale the base round speed by the weapon barrel + attachment velocity modifiers.
                bi.BulletSpeed = spd * FirearmManager.GetVelocityModifier(itemBase);
                bi.BallisticCoefficient = bc;
                bi.BulletMassGrams = mass;
                bi.BulletDiameterMillimeters = diam;
                if (bi.IsAmmoValid)
                {
                    _ballistics = bi;
                    _lastWeaponVersion = ver;
                }
            }

            info = _ballistics!;
            return _ballistics is not null;
        }

        /// <summary>Resolves and caches the local player's view-rotation write address (MovementContext._rotation, a yaw/pitch Vector2).</summary>
        private bool TryResolveLocalRotAddr(LocalPlayer local, out ulong rotAddr)
        {
            if (_localRotAddr.IsValidVirtualAddress())
            {
                rotAddr = _localRotAddr;
                return true;
            }
            rotAddr = 0;
            if (!Memory.TryReadPtr(local.Base + Offsets.Player.MovementContext, out var mc, false) || mc == 0)
                return false;
            _localRotAddr = mc + Offsets.MovementContext._rotation;
            rotAddr = _localRotAddr;
            return true;
        }

        /// <summary>
        /// Returns the velocity to lead <paramref name="target"/> by. Prefers the game's observed
        /// velocity (accurate, server-side) when present; otherwise differentiates the target's root
        /// position over time — observed velocity is zero for local-class AI (e.g. offline raids), so
        /// without this they'd never be led. The derived value is EMA-smoothed and clamped to ignore
        /// position jitter and re-init teleports.
        /// </summary>
        private Vector3 ResolveLeadVelocity(Player target)
        {
            if (target.Velocity.LengthSquared() > 0.01f) // observed velocity available
            {
                _velTarget = null; // drop derived state so a later switch to derived restarts clean
                return target.Velocity;
            }

            long nowMs = Environment.TickCount64;
            Vector3 pos = target.Position;
            if (!ReferenceEquals(_velTarget, target))
            {
                _velTarget = target;
                _velLastPos = pos;
                _velLastMs = nowMs;
                _derivedVel = Vector3.Zero;
            }
            else if (Vector3.DistanceSquared(pos, _velLastPos) > 1e-6f) // root actually advanced
            {
                float dt = (nowMs - _velLastMs) / 1000f;
                if (dt > 0.005f && dt < 0.5f)
                {
                    Vector3 inst = (pos - _velLastPos) / dt;
                    if (inst.LengthSquared() < 225f) // ignore >15 m/s jumps (teleport / skeleton re-init)
                        _derivedVel = _derivedVel == Vector3.Zero ? inst : Vector3.Lerp(_derivedVel, inst, 0.4f);
                }
                _velLastPos = pos;
                _velLastMs = nowMs;
            }
            return _derivedVel;
        }

        /// <summary>Converts a world direction to EFT's view rotation (yaw [0..360], pitch).</summary>
        private static Vector2 WorldDirToYawPitch(Vector3 dir)
        {
            float yaw = MathF.Atan2(dir.X, dir.Z) * (180f / MathF.PI);
            if (yaw < 0f) yaw += 360f;
            float pitch = MathF.Asin(Math.Clamp(-dir.Y, -1f, 1f)) * (180f / MathF.PI);
            return new Vector2(yaw, pitch);
        }

        /// <summary>
        /// Overwrites <c>FirearmController.get_WeaponDirection()</c> with a stub that returns the
        /// given world direction. On the IL2CPP x64 ABI a 12-byte struct return is passed via an
        /// sret buffer in RCX, so the stub writes the three floats to [rcx]/[rcx+4]/[rcx+8], sets
        /// rax = rcx, and returns. The original prologue is saved once and put back by
        /// <see cref="RestoreGetter"/> when disengaged. Skips the write when the direction is
        /// effectively unchanged so a steady aim doesn't spam identical DMA writes.
        /// </summary>
        private void ApplyWeaponDirectionPatch(Vector3 worldDir)
        {
            if (_getterAddr == 0)
            {
                ulong ga = Memory.GameAssemblyBase;
                uint rva = Offsets.FirearmController.get_WeaponDirection_RVA;
                if (ga == 0 || rva == 0)
                    return;
                _getterAddr = ga + rva;
            }

            // Save the real prologue once; never capture an already-patched copy as the "original".
            if (_getterOriginal is null)
            {
                var orig = new byte[GetterPatchLen];
                if (!Memory.TryReadBuffer<byte>(_getterAddr, orig, false))
                    return;
                if (orig[0] == 0xC7 && orig[1] == 0x01)
                    return;
                _getterOriginal = orig;
            }

            // Skip redundant writes: if already patched to (essentially) this direction recently,
            // there's nothing to refresh. Re-assert at least every ~250ms as cheap insurance.
            long now = Environment.TickCount64;
            if (_getterPatched && now - _lastPatchMs < 250 &&
                (worldDir - _lastPatchedDir).LengthSquared() < 1e-6f)
                return;

            Span<byte> patch = stackalloc byte[GetterPatchLen];
            patch[0] = 0xC7; patch[1] = 0x01;                         // mov dword [rcx], X
            BitConverter.TryWriteBytes(patch.Slice(2, 4), worldDir.X);
            patch[6] = 0xC7; patch[7] = 0x41; patch[8] = 0x04;        // mov dword [rcx+4], Y
            BitConverter.TryWriteBytes(patch.Slice(9, 4), worldDir.Y);
            patch[13] = 0xC7; patch[14] = 0x41; patch[15] = 0x08;     // mov dword [rcx+8], Z
            BitConverter.TryWriteBytes(patch.Slice(16, 4), worldDir.Z);
            patch[20] = 0x48; patch[21] = 0x89; patch[22] = 0xC8;     // mov rax, rcx
            patch[23] = 0xC3;                                         // ret

            try
            {
                Memory.WriteBuffer<byte>(_getterAddr, patch);
                _getterPatched = true;
                _lastPatchedDir = worldDir;
                _lastPatchMs = now;
            }
            catch { }
        }

        /// <summary>Restores the original <c>get_WeaponDirection()</c> bytes if the patch is live; no-op otherwise.</summary>
        private void RestoreGetter()
        {
            if (!_getterPatched)
                return;
            try
            {
                if (_getterOriginal is not null && _getterAddr != 0)
                    Memory.WriteBufferEnsure<byte>(_getterAddr, _getterOriginal);
            }
            catch { }
            finally
            {
                _getterPatched = false;
                _lastPatchedDir = Vector3.Zero;
                _lastPatchMs = 0;
            }
        }

        /// <summary>
        /// True when silent aim should redirect this tick. If the "SilentAim" hotkey is bound
        /// (enabled with a real key) the engage state gates it (hold or toggle); otherwise it's
        /// always-on so users who never bind a key keep that behaviour.
        /// </summary>
        private static bool IsEngaged()
        {
            var hotkeys = SilkProgram.Config.Hotkeys;
            if (hotkeys.TryGetValue("SilentAim", out var hk) && hk.Enabled && hk.Key > 0)
                return Engaged;
            return true;
        }

        /// <summary>Flags <paramref name="target"/> as the locked target and clears the previous one.</summary>
        private void SetLocked(Player? target)
        {
            if (ReferenceEquals(_lockedTarget, target))
                return;
            if (_lockedTarget is not null)
                _lockedTarget.IsAimbotLocked = false;
            _lockedTarget = target;
            if (target is not null)
                target.IsAimbotLocked = true;
        }

        public override void OnRaidStart() => ResetState();

        public override void OnRaidEnd()
        {
            ResetState();
            Engaged = false;
        }

        /// <summary>Undoes the code patch and clears all cached state between raids.</summary>
        private void ResetState()
        {
            RestoreGetter();
            _handsController = 0;
            _fireportTI = 0;
            _localRotAddr = 0;
            _ballistics = null;
            _lastWeaponVersion = -1;
            _getterAddr = 0;
            _getterOriginal = null;
            _velTarget = null;
            _derivedVel = Vector3.Zero;
            _lastShotIndex = -1;
            _randomBoneChoice = 0;
            SetLocked(null);
        }
    }
}
