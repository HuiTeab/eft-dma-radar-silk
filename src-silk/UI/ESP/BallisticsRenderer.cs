// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Tarkov.Features.Ballistics;
using eft_dma_radar.Silk.Tarkov.GameWorld.Ballistics;

namespace eft_dma_radar.Silk.UI.ESP
{
    /// <summary>
    /// Skia ESP overlay for the ballistics debug view. Draws:
    ///   <list type="bullet">
    ///     <item>Predicted shot polyline (red) sampled from <see cref="BallisticsFeature.LocalTrajectory"/>.</item>
    ///     <item>Live in-flight bullet trails (green) from <see cref="LiveShotTracker.GetSnapshot"/>.</item>
    ///   </list>
    /// All world points are projected via <see cref="CameraManager.WorldToScreen"/> in viewport space —
    /// the caller is expected to have already applied the viewport→window scale.
    /// </summary>
    internal static class BallisticsRenderer
    {
        public static void Draw(SKCanvas canvas)
        {
            var cfg = SilkProgram.Config?.Ballistics;
            if (cfg is null || !cfg.Enabled) { ResetAimLine(); return; }
            if (!CameraManager.IsActive) { ResetAimLine(); return; }

            var feature = BallisticsFeature.Instance;

            // Configure paint widths from config (cheap — Skia caches the dirty flag itself).
            EspPaints.PredictedTrajectory.StrokeWidth = cfg.LineWidth;
            EspPaints.LiveShotTrail.StrokeWidth = cfg.LineWidth;

            if (cfg.DrawPredictedTrajectory)
                DrawPredictedTrajectory(canvas, feature.LocalTrajectory);

            if (cfg.DrawLiveShots)
                DrawLiveShots(canvas, feature.Tracker.GetSnapshot());

            if (cfg.DrawAimPoint)
                DrawAimPoint(canvas, feature.LocalAimSolution);

            if (cfg.ShowFireportAim)
                DrawFireportAimLine(canvas, feature.LocalPredicted, cfg);
            else
                ResetAimLine();
        }

        #region Fireport aim line (straight bore indicator)

        // Smoothing state. BallisticsRenderer.Draw only ever runs on the ESP render thread,
        // so these statics need no synchronization.
        private static SKPoint _aimSmoothStart, _aimSmoothEnd, _aimPrevRawEnd;
        private static bool _aimHasHistory;
        private static long _aimLastTicks;
        private static long _aimLastValidTicks;

        // Smoothing tunables (ported from the reference fireport-aim).
        private const float AimSmoothTime = 0.085f; // exp smoothing time constant — lower = snappier
        private const float AimMaxStepPx  = 180f;   // clamp per-frame teleport jumps (viewport px)
        private const float AimPredictSec = 0.012f; // lead the raw endpoint ~12 ms so flicks don't lag
        private const float AimHoldSec    = 0.20f;  // keep the last good line this long through dropouts

        /// <summary>
        /// Smoothed aim-line tip in viewport space, or <c>null</c> when the line isn't drawn this
        /// frame. Read by <see cref="EspWindow"/> to optionally anchor the crosshair to the bore tip.
        /// </summary>
        public static SKPoint? AimLineTipViewport { get; private set; }

        private static void ResetAimLine()
        {
            _aimHasHistory = false;
            AimLineTipViewport = null;
        }

        /// <summary>
        /// Draw the straight bore line from the muzzle along the barrel direction. On a transient
        /// resolve/projection miss, hold the last good frame for <see cref="AimHoldSec"/> to avoid
        /// flicker through recoil / ADS transitions.
        /// </summary>
        private static void DrawFireportAimLine(SKCanvas canvas, ShotState? shotN, BallisticsConfig cfg)
        {
            EspPaints.FireportAim.StrokeWidth = cfg.LineWidth;
            long now = Stopwatch.GetTimestamp();

            if (shotN is ShotState shot && shot.IsValid &&
                IsFinite(shot.SourcePosition) && IsFinite(shot.InitialDirection))
            {
                Vector3 start = shot.SourcePosition;
                Vector3 dir = Vector3.Normalize(shot.InitialDirection);
                float maxDist = Math.Clamp(cfg.AimlineMaxDistance, 5f, 1000f);
                Vector3 endW = start + dir * maxDist;

                if (CameraManager.WorldToScreen(ref start, out var rawStart, onScreenCheck: false) &&
                    CameraManager.WorldToScreen(ref endW, out var rawEnd, onScreenCheck: false))
                {
                    if (cfg.SmoothFireportAim)
                        SmoothAndDrawAimLine(canvas, rawStart, rawEnd, now);
                    else
                        DrawRawAimLine(canvas, rawStart, rawEnd);
                    _aimLastValidTicks = now;
                    return;
                }
            }

            // Dropout — hold the last good line briefly, then clear.
            if (_aimHasHistory &&
                (now - _aimLastValidTicks) < (long)(AimHoldSec * Stopwatch.Frequency))
            {
                canvas.DrawLine(_aimSmoothStart, _aimSmoothEnd, EspPaints.FireportAim);
                AimLineTipViewport = _aimSmoothEnd;
            }
            else
            {
                ResetAimLine();
            }
        }

        /// <summary>Raw (un-smoothed) aim line. Resets smoothing history so re-enabling snaps cleanly.</summary>
        private static void DrawRawAimLine(SKCanvas canvas, SKPoint rawStart, SKPoint rawEnd)
        {
            _aimHasHistory = false; // no hold/smoothing state in raw mode
            AimLineTipViewport = rawEnd;
            canvas.DrawLine(rawStart, rawEnd, EspPaints.FireportAim);
        }

        private static void SmoothAndDrawAimLine(SKCanvas canvas, SKPoint rawStart, SKPoint rawEnd, long now)
        {
            float dt = _aimLastTicks == 0
                ? 1f / 144f
                : (float)(now - _aimLastTicks) / Stopwatch.Frequency;
            _aimLastTicks = now;
            dt = Math.Clamp(dt, 0.001f, 0.05f);

            if (!_aimHasHistory)
            {
                _aimSmoothStart = rawStart;
                _aimSmoothEnd = rawEnd;
                _aimPrevRawEnd = rawEnd;
                _aimHasHistory = true;
            }

            // Velocity-predict the endpoint a few ms ahead so fast flicks lead instead of lag.
            var velEnd = new SKPoint((rawEnd.X - _aimPrevRawEnd.X) / dt, (rawEnd.Y - _aimPrevRawEnd.Y) / dt);
            var predictedEnd = new SKPoint(rawEnd.X + velEnd.X * AimPredictSec, rawEnd.Y + velEnd.Y * AimPredictSec);
            _aimPrevRawEnd = rawEnd;

            float alpha = Math.Clamp(1f - MathF.Exp(-dt / AimSmoothTime), 0.02f, 0.95f);

            var targetStart = StepClamp(_aimSmoothStart, rawStart, AimMaxStepPx);
            var targetEnd = StepClamp(_aimSmoothEnd, predictedEnd, AimMaxStepPx);

            _aimSmoothStart = LerpPoint(_aimSmoothStart, targetStart, alpha);
            _aimSmoothEnd = LerpPoint(_aimSmoothEnd, targetEnd, alpha);

            AimLineTipViewport = _aimSmoothEnd;
            canvas.DrawLine(_aimSmoothStart, _aimSmoothEnd, EspPaints.FireportAim);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPoint LerpPoint(SKPoint a, SKPoint b, float t) =>
            new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        private static SKPoint StepClamp(SKPoint from, SKPoint to, float maxStep)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float d2 = dx * dx + dy * dy;
            if (d2 <= maxStep * maxStep) return to;
            float s = maxStep / MathF.Sqrt(d2);
            return new SKPoint(from.X + dx * s, from.Y + dy * s);
        }

        #endregion

        private static void DrawAimPoint(SKCanvas canvas, AimSolution? solutionN)
        {
            if (solutionN is not AimSolution sol || !sol.Valid)
                return;

            Vector3 aim = sol.AimPoint;
            Vector3 bone = sol.TargetBonePos;
            if (!IsFinite(aim) || !IsFinite(bone))
                return;

            if (!CameraManager.WorldToScreen(ref aim, out var aimScr, onScreenCheck: false))
                return;

            // Connector from the target bone to the aim point (visualises the holdover + lead vector).
            if (CameraManager.WorldToScreen(ref bone, out var boneScr, onScreenCheck: false))
                canvas.DrawLine(boneScr.X, boneScr.Y, aimScr.X, aimScr.Y, EspPaints.AimPointLine);

            // Aim marker: ring + center dot — place your reticle here.
            canvas.DrawCircle(aimScr.X, aimScr.Y, 6f, EspPaints.AimPointRing);
            canvas.DrawCircle(aimScr.X, aimScr.Y, 1.5f, EspPaints.AimPointDot);
        }

        private static void DrawPredictedTrajectory(SKCanvas canvas, Vector3[] points)
        {
            if (points is null || points.Length < 2) return;

            using var path = new SKPath();
            bool started = false;
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (!IsFinite(p)) continue;
                if (!CameraManager.WorldToScreen(ref p, out var scr, onScreenCheck: false)) continue;

                if (!started) { path.MoveTo(scr.X, scr.Y); started = true; }
                else          { path.LineTo(scr.X, scr.Y); }
            }
            if (!started) return;

            canvas.DrawPath(path, EspPaints.PredictedTrajectory);

            // Muzzle origin marker.
            var origin = points[0];
            if (IsFinite(origin) && CameraManager.WorldToScreen(ref origin, out var muzzle))
                canvas.DrawCircle(muzzle.X, muzzle.Y, 3.5f, EspPaints.MuzzleDot);
        }

        private static void DrawLiveShots(SKCanvas canvas, LiveShot[] shots)
        {
            if (shots is null || shots.Length == 0) return;

            using var path = new SKPath();
            foreach (var shot in shots)
            {
                var trail = shot.Trail;
                if (trail.Count < 2) continue;
                path.Reset();

                bool started = false;
                for (int i = 0; i < trail.Count; i++)
                {
                    var p = trail[i];
                    if (!IsFinite(p)) continue;
                    if (!CameraManager.WorldToScreen(ref p, out var scr, onScreenCheck: false)) continue;
                    if (!started) { path.MoveTo(scr.X, scr.Y); started = true; }
                    else          { path.LineTo(scr.X, scr.Y); }
                }
                if (!started) continue;

                canvas.DrawPath(path, EspPaints.LiveShotTrail);

                // Bullet head dot.
                var head = shot.CurrentPosition;
                if (IsFinite(head) && CameraManager.WorldToScreen(ref head, out var headScr))
                    canvas.DrawCircle(headScr.X, headScr.Y, 2.5f, EspPaints.LiveShotHead);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
