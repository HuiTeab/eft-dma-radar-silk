// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Tarkov.Features.Ballistics;
using eft_dma_radar.Silk.UI.Widgets;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Settings → Ballistics category. Lives here (not under ESP) so radar-only users — who never
    /// open the ESP tab — can still find the trajectory / aim-solver options.
    /// </summary>
    internal static partial class SettingsPanel
    {
        private static void DrawBallisticsTab()
        {
            ImGui.Spacing();

            UIControls.Section("Ballistics");

            var bcfg = Config.Ballistics ??= new BallisticsConfig();

            bool ballEnabled = bcfg.Enabled;
            if (UIControls.ToggleRow("Enable Ballistics", ref ballEnabled,
                "Master toggle for ballistics simulation + overlays."))
            {
                bcfg.Enabled = ballEnabled;
                Config.MarkDirty();
            }

            if (bcfg.Enabled)
            {
                ImGui.Indent(16);

                bool drawPredicted = bcfg.DrawPredictedTrajectory;
                if (UIControls.ToggleRow("Predicted Arc (red)", ref drawPredicted,
                    "Simulated trajectory from your muzzle to the predicted impact point."))
                {
                    bcfg.DrawPredictedTrajectory = drawPredicted;
                    Config.MarkDirty();
                }

                bool drawLive = bcfg.DrawLiveShots;
                if (UIControls.ToggleRow("Live Tracers (green)", ref drawLive,
                    "Real-time bullet trails read from the game's BallisticsCalculator.Shots list."))
                {
                    bcfg.DrawLiveShots = drawLive;
                    Config.MarkDirty();
                }

                bool showHud = bcfg.ShowDebugHud;
                if (UIControls.ToggleRow("Debug HUD Window", ref showHud,
                    "Floating window with ammo / muzzle velocity / drop table / G1 source."))
                {
                    bcfg.ShowDebugHud = showHud;
                    BallisticsDebugWidget.IsOpen = showHud;
                    Config.MarkDirty();
                }

                bool liveG1 = bcfg.UseGameG1Table;
                if (UIControls.ToggleRow("Use Live G1 Table", ref liveG1,
                    "Replace the hardcoded G1 table with the game's own once a bullet is observed."))
                {
                    bcfg.UseGameG1Table = liveG1;
                    if (!liveG1) G1Table.Reset();
                    Config.MarkDirty();
                }

                float lineWidth = bcfg.LineWidth;
                if (UIControls.StepperFloat("Line Width", ref lineWidth, 0.5f, 6f, 0.25f, "{0:0.0}px",
                    "Stroke width for predicted + live shot lines."))
                {
                    bcfg.LineWidth = lineWidth;
                    Config.MarkDirty();
                }

                int samples = bcfg.PredictedSamples;
                if (UIControls.Stepper("Predicted Samples", ref samples, 8, 512, 8,
                    tooltip: "Number of points sampled along the predicted arc."))
                {
                    bcfg.PredictedSamples = samples;
                    Config.MarkDirty();
                }

                float maxDist = bcfg.PredictedMaxDistance;
                if (UIControls.StepperFloat("Predicted Max Distance", ref maxDist, 25f, 2000f, 25f, "{0:0}m",
                    "Stop the predicted arc after this many meters from the muzzle."))
                {
                    bcfg.PredictedMaxDistance = maxDist;
                    Config.MarkDirty();
                }

                float lifetime = bcfg.LiveShotLifetime;
                if (UIControls.StepperFloat("Live Shot Lifetime", ref lifetime, 0.5f, 15f, 0.5f, "{0:0.0}s",
                    "How long bullet trails stay visible after the bullet stops moving."))
                {
                    bcfg.LiveShotLifetime = lifetime;
                    BallisticsFeature.Instance.Tracker.Lifetime = TimeSpan.FromSeconds(lifetime);
                    Config.MarkDirty();
                }

                ImGui.SeparatorText("Aim Solver");

                bool useFireport = bcfg.UseFireportMuzzle;
                if (UIControls.ToggleRow("Use Fireport Muzzle", ref useFireport,
                    "Start the predicted arc at the real weapon bore instead of the camera/eye."))
                {
                    bcfg.UseFireportMuzzle = useFireport;
                    Config.MarkDirty();
                }

                bool drawAim = bcfg.DrawAimPoint;
                if (UIControls.ToggleRow("Aim Point (holdover + lead)", ref drawAim,
                    "Draw an 'aim here' marker showing where to place your crosshair to hit the selected target."))
                {
                    bcfg.DrawAimPoint = drawAim;
                    Config.MarkDirty();
                }

                if (bcfg.DrawAimPoint || bcfg.ShowDebugHud)
                {
                    bool aimHead = bcfg.AimBone == BallisticsAimBone.Head;
                    if (UIControls.ToggleRow("Aim at Head", ref aimHead,
                        "On = hold over the head; off = thorax (upper chest)."))
                    {
                        bcfg.AimBone = aimHead ? BallisticsAimBone.Head : BallisticsAimBone.Thorax;
                        Config.MarkDirty();
                    }

                    bool lead = bcfg.LeadMovingTargets;
                    if (UIControls.ToggleRow("Lead Moving Targets", ref lead,
                        "Offset the aim point by target velocity x bullet travel time."))
                    {
                        bcfg.LeadMovingTargets = lead;
                        Config.MarkDirty();
                    }

                    bool targetAI = bcfg.TargetAI;
                    if (UIControls.ToggleRow("Target AI", ref targetAI,
                        "Also solve for AI (scavs / raiders / bosses), not just hostile players."))
                    {
                        bcfg.TargetAI = targetAI;
                        Config.MarkDirty();
                    }

                    bool nearXhair = bcfg.TargetSelection == BallisticsTargetMode.NearestToCrosshair;
                    if (UIControls.ToggleRow("Target Nearest Crosshair", ref nearXhair,
                        "On = nearest enemy within the aim cone (point roughly at them); " +
                        "off = nearest enemy by distance anywhere (ignores where you aim)."))
                    {
                        bcfg.TargetSelection = nearXhair ? BallisticsTargetMode.NearestToCrosshair : BallisticsTargetMode.Nearest;
                        Config.MarkDirty();
                    }

                    float cone = bcfg.AimConeDegrees;
                    if (UIControls.StepperFloat("Aim Cone", ref cone, 1f, 90f, 1f, "{0:0}°",
                        "Crosshair cone half-angle for 'nearest crosshair' target selection."))
                    {
                        bcfg.AimConeDegrees = cone;
                        Config.MarkDirty();
                    }

                    float aimMax = bcfg.MaxAimDistance;
                    if (UIControls.StepperFloat("Max Aim Distance", ref aimMax, 25f, 1000f, 25f, "{0:0}m",
                        "Don't solve for targets beyond this range."))
                    {
                        bcfg.MaxAimDistance = aimMax;
                        Config.MarkDirty();
                    }
                }

                ImGui.SeparatorText("Aim Line");

                bool showFireport = bcfg.ShowFireportAim;
                if (UIControls.ToggleRow("Fireport Aim Line", ref showFireport,
                    "Draw a straight line down the weapon bore showing where the barrel points " +
                    "(not drop-compensated). Smoothed so it stays steady through recoil / ADS."))
                {
                    bcfg.ShowFireportAim = showFireport;
                    Config.MarkDirty();
                }

                if (bcfg.ShowFireportAim)
                {
                    float aimlineDist = bcfg.AimlineMaxDistance;
                    if (UIControls.StepperFloat("Aim Line Distance", ref aimlineDist, 5f, 500f, 5f, "{0:0}m",
                        "Length of the bore aim line."))
                    {
                        bcfg.AimlineMaxDistance = aimlineDist;
                        Config.MarkDirty();
                    }

                    bool smoothAim = bcfg.SmoothFireportAim;
                    if (UIControls.ToggleRow("Smooth Aim Line", ref smoothAim,
                        "Steady the line through recoil / ADS (predict + clamp + smooth + hold). " +
                        "Off = raw line each frame (snappier but jitters)."))
                    {
                        bcfg.SmoothFireportAim = smoothAim;
                        Config.MarkDirty();
                    }

                    bool crosshairAtTip = bcfg.CrosshairAtAimpoint;
                    if (UIControls.ToggleRow("Crosshair At Barrel Tip", ref crosshairAtTip,
                        "Anchor the ESP crosshair to the aim-line tip instead of screen center. " +
                        "Requires the ESP crosshair to be enabled (ESP tab)."))
                    {
                        bcfg.CrosshairAtAimpoint = crosshairAtTip;
                        Config.MarkDirty();
                    }
                }

                ImGui.Unindent(16);
            }
        }
    }
}
