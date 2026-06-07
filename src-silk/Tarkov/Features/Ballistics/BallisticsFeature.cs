// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.Misc.Workers;
using eft_dma_radar.Silk.Tarkov.GameWorld.Ballistics;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.Ballistics
{
    /// <summary>
    /// Read-only feature that:
    ///   <list type="bullet">
    ///     <item>Tracks every in-flight <c>EFT.Ballistics.Shot</c> via <see cref="LiveShotTracker"/>.</item>
    ///     <item>Snapshots the game's G1 drag table the first time a Shot is observed.</item>
    ///     <item>Exposes the latest <see cref="ShotState"/> for the local player (populated by
    ///       <see cref="eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins.FirearmManager"/>).</item>
    ///   </list>
    /// Runs on its own ~125Hz worker thread spawned at raid start. Toggling
    /// <see cref="Enabled"/> stops the tracker doing real work but leaves the thread alive.
    /// </summary>
    public sealed class BallisticsFeature : IFeature
    {
        public static BallisticsFeature Instance { get; }

        static BallisticsFeature()
        {
            Instance = new BallisticsFeature();
            IFeature.Register(Instance);
            Log.WriteLine("[Ballistics] Feature registered.");
        }

        public LiveShotTracker Tracker { get; } = new();

        /// <summary>Resolver for the local player's current weapon + aim → predicted ShotState.</summary>
        internal LocalShotResolver Resolver { get; } = new();

        /// <summary>Last-computed predicted shot for the local player. Null when invalid.</summary>
        public ShotState? LocalPredicted { get; private set; }

        /// <summary>Pre-sampled trajectory points for rendering. Length 0 when invalid.</summary>
        public Vector3[] LocalTrajectory { get; private set; } = Array.Empty<Vector3>();

        /// <summary>Latest aim solution (holdover + lead) for the selected target. Null when none.</summary>
        public AimSolution? LocalAimSolution { get; private set; }

        /// <summary>Name of the player the aim solution is computed for (HUD). Null when none.</summary>
        public string? AimTargetName { get; private set; }

        /// <summary>Base pointer of the current aim target (0 when none). Drives the radar + aimview highlight.</summary>
        public ulong AimTargetBase { get; private set; }

        /// <summary>
        /// True when the aim-point indicator is active and <paramref name="playerBase"/> is the player the
        /// solver is currently locked onto. Used by the radar + aimview to mark that target distinctly.
        /// </summary>
        public bool IsAimTarget(ulong playerBase)
        {
            if (playerBase == 0 || playerBase != AimTargetBase)
                return false;
            var cfg = SilkProgram.Config?.Ballistics;
            return cfg is not null && cfg.Enabled && cfg.DrawAimPoint;
        }

        /// <summary>Loaded ammo's short name (for HUD).</summary>
        public string? CurrentAmmoShortName => Resolver.CurrentAmmoShortName;

        /// <summary>Effective muzzle velocity after weapon + attachment modifiers (m/s) — for HUD.</summary>
        public float? EffectiveMuzzleVelocity =>
            Resolver.EffectiveMuzzleVelocity > 0f ? Resolver.EffectiveMuzzleVelocity : null;

        public bool Enabled => SilkProgram.Config.Ballistics?.Enabled ?? false;

        public bool CanRun => Enabled && Memory.InRaid;

        private WorkerThread? _worker;

        private BallisticsFeature() { }

        public void OnGameStart() { }
        public void OnGameStop()  { ShutdownWorker(); }
        public void OnRaidEnd()
        {
            ShutdownWorker();
            Tracker.Clear();
            G1Table.Reset();
            Resolver.Reset();
            ClearLocalShot();
        }

        public void OnRaidStart()
        {
            ShutdownWorker(); // defensive — should be a no-op

            // Apply config to runtime knobs before the worker fires.
            var cfg = SilkProgram.Config?.Ballistics;
            if (cfg is not null)
            {
                Tracker.Lifetime = TimeSpan.FromSeconds(cfg.LiveShotLifetime);
                if (!cfg.UseGameG1Table)
                    G1Table.Reset();
            }

            _worker = new WorkerThread
            {
                Name = "BallisticsTracker",
                SleepDuration = TimeSpan.FromMilliseconds(8),
                SleepMode = WorkerSleepMode.DynamicSleep,
                ThreadPriority = ThreadPriority.AboveNormal,
            };
            _worker.PerformWork += Tick;
            _worker.Start();
            Log.WriteLine("[Ballistics] Worker started.");
        }

        public void OnApply() { /* read-only feature — nothing to apply */ }

        private void Tick(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            if (!CanRun) return;
            if (Memory.Game is not LocalGameWorld game) return;

            var cfg = SilkProgram.Config.Ballistics;
            if (cfg is null) return;

            // Granular gating: only read what a downstream consumer is actually using.
            //   - Live shot tracker → drives green tracers AND the debug HUD's live count
            //   - Predicted trajectory → drives red arc AND the HUD's drop table
            //   - Aim point → drives the holdover/lead indicator AND its HUD readout
            // When nothing needs the data we skip the DMA entirely.
            bool needLiveShots = cfg.DrawLiveShots || cfg.ShowDebugHud;
            bool needPredictedArc = cfg.DrawPredictedTrajectory || cfg.ShowDebugHud;
            bool needAimPoint = cfg.DrawAimPoint || cfg.ShowDebugHud;
            // The fireport aim line only needs the resolved bore pose (source + direction),
            // so it rides on the same shot resolve as the arc / aim solver.
            bool needAimLine = cfg.ShowFireportAim;
            bool needShot = needPredictedArc || needAimPoint || needAimLine;

            // 1) Live shot tracer history from BallisticsCalculator.Shots.
            if (needLiveShots)
                Tracker.Update(game.Base);
            else if (Tracker.TrackedCount > 0)
                Tracker.Clear(); // free memory + stop emitting stale snapshots

            // 2) Resolve the local player's current shot (shared by the arc + the aim solver).
            if (!needShot)
            {
                ClearLocalShot();
                return;
            }

            if (game.LocalPlayer is not LocalPlayer lp || !lp.IsAlive)
            {
                ClearLocalShot();
                return;
            }

            if (!Resolver.TryResolve(lp, out var shot))
            {
                ClearLocalShot();
                return;
            }

            LocalPredicted = shot;

            // 2a) Predicted arc polyline (red).
            if (needPredictedArc)
            {
                int sampleCount = Math.Clamp(cfg.PredictedSamples, 8, 512);
                float maxDist = Math.Clamp(cfg.PredictedMaxDistance, 25f, 2000f);
                var buffer = new Vector3[sampleCount];
                int written = TrajectoryMath.BuildTrajectoryPoints(shot, buffer, maxDist);
                if (written >= 2)
                {
                    if (written != buffer.Length)
                    {
                        var trimmed = new Vector3[written];
                        Array.Copy(buffer, trimmed, written);
                        buffer = trimmed;
                    }
                    LocalTrajectory = buffer;
                }
                else
                {
                    LocalTrajectory = Array.Empty<Vector3>();
                }
            }
            else
            {
                LocalTrajectory = Array.Empty<Vector3>();
            }

            // 2b) Aim solution (holdover + lead) for the selected target.
            if (needAimPoint)
                SolveAimPoint(game, shot, cfg);
            else
            {
                LocalAimSolution = null;
                AimTargetName = null;
            }
        }

        private void ClearLocalShot()
        {
            LocalPredicted = null;
            LocalTrajectory = Array.Empty<Vector3>();
            LocalAimSolution = null;
            AimTargetName = null;
            AimTargetBase = 0;
        }

        /// <summary>
        /// Pick a hostile target (nearest the crosshair, or nearest by distance) and run
        /// <see cref="AimSolver"/> for it, storing the result in <see cref="LocalAimSolution"/>.
        /// </summary>
        private void SolveAimPoint(LocalGameWorld game, ShotState shot, BallisticsConfig cfg)
        {
            LocalAimSolution = null;
            AimTargetName = null;
            ulong prevTarget = AimTargetBase;
            AimTargetBase = 0;

            var bone = cfg.AimBone == BallisticsAimBone.Head ? Bones.HumanHead : Bones.HumanSpine3;
            float maxDist = Math.Clamp(cfg.MaxAimDistance, 25f, 1000f);
            float maxDistSq = maxDist * maxDist;

            Vector3 origin = shot.SourcePosition;
            Vector3 aimDir = Vector3.Normalize(shot.InitialDirection);
            bool nearestToCrosshair = cfg.TargetSelection == BallisticsTargetMode.NearestToCrosshair;
            float cosCone = MathF.Cos(Math.Clamp(cfg.AimConeDegrees, 1f, 90f) * (MathF.PI / 180f));

            Player? best = null;
            Vector3 bestBone = default;
            float bestScore = float.MaxValue; // smaller = better (angular miss, or squared distance)
            bool targetAI = cfg.TargetAI;

            // Diagnostic counters — surfaced (rate-limited) when nothing locks so the exact reason is
            // visible: eligibility vs missing skeleton/bone vs out of range vs outside the aim cone.
            int alive = 0, rejEligible = 0, rejSkeleton = 0, rejBone = 0, rejRange = 0, rejCone = 0;

            foreach (var p in game.RegisteredPlayers)
            {
                if (p is null || !p.IsAlive)
                    continue;
                alive++;
                // Hostile humans always; AI (scavs / raiders / bosses) only when the option is on.
                bool eligible = p.IsHostile
                    || (targetAI && p.Type is PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss);
                if (!eligible)
                {
                    rejEligible++;
                    continue;
                }
                var skel = p.Skeleton;
                if (skel is null)
                {
                    rejSkeleton++;
                    continue;
                }
                if (skel.GetBonePosition(bone) is not Vector3 bonePos)
                {
                    rejBone++;
                    continue;
                }

                Vector3 to = bonePos - origin;
                float distSq = to.LengthSquared();
                if (distSq > maxDistSq || distSq < 1f)
                {
                    rejRange++;
                    continue;
                }

                if (nearestToCrosshair)
                {
                    float cosAng = Vector3.Dot(to / MathF.Sqrt(distSq), aimDir);
                    if (cosAng < cosCone)
                    {
                        rejCone++;
                        continue; // outside the aim cone (FOV filter)
                    }
                    // Inside the cone, pick the NEAREST enemy by distance — don't pass over a close
                    // target for a farther one just because it's a hair better centered on the crosshair.
                    if (distSq < bestScore) { bestScore = distSq; best = p; bestBone = bonePos; }
                }
                else if (distSq < bestScore)
                {
                    bestScore = distSq; best = p; bestBone = bonePos;
                }
            }

            if (best is null)
            {
                Log.WriteRateLimited(AppLogLevel.Info, "ball.notarget", TimeSpan.FromSeconds(2),
                    $"no lockable target: {alive} alive, rejected[eligible={rejEligible} skel={rejSkeleton} " +
                    $"bone={rejBone} range={rejRange} cone={rejCone}] " +
                    $"(maxDist={maxDist:F0}m, ai={targetAI}, mode={(nearestToCrosshair ? "crosshair" : "nearest")})",
                    "Ballistics");
                return;
            }

            var sol = AimSolver.Solve(origin, bestBone, best.Velocity, shot.MuzzleSpeed,
                shot.Ballistics, cfg.LeadMovingTargets);
            if (!sol.Valid)
                return;

            AimTargetBase = best.Base;
            LocalAimSolution = sol;
            AimTargetName = best.Name;

            // Info-log when the locked target changes so the user can see (without enabling debug
            // logging) exactly who the solver picked and what it's holding over.
            if (best.Base != prevTarget)
                Log.WriteLine(
                    $"[Ballistics] Aim target -> {best.Name} @ {sol.Distance:F0}m  holdover={sol.Holdover * 100f:F1}cm  " +
                    $"lead={sol.Lead * 100f:F1}cm  travel={sol.TravelTime * 1000f:F0}ms  conv={sol.Converged}");
        }

        private void ShutdownWorker()
        {
            var w = Interlocked.Exchange(ref _worker, null);
            if (w is null) return;
            try { w.Dispose(); w.Join(TimeSpan.FromSeconds(2)); }
            catch { }
        }
    }
}
