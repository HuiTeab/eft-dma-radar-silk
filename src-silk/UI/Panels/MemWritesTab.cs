// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        // Silent-aim combo labels (index matches SilentAimBone / SilentAimTargetMode config ints).
        private static readonly string[] _silentAimBones = ["Head", "Chest", "Pelvis", "Auto (nearest)", "Neck", "Legs"];
        private static readonly string[] _silentAimTargetModes = ["Crosshair (FOV)", "Closest (distance)"];
        private static readonly string[] _silentAimMethods = ["ShotDirection (data)", "WeaponDirection (patch)"];

        private static void DrawMemWritesTab()
        {
            ImGui.Spacing();

            bool masterEnabled = Config.MemWritesEnabled;
            if (UIControls.ToggleRow("Enable Memory Writes", ref masterEnabled, "Master toggle — enables all active memory write features"))
            {
                Config.MemWritesEnabled = masterEnabled;
                Config.MarkDirty();
            }

            if (!masterEnabled)
                ImGui.BeginDisabled();

            UIControls.Section("Camera");

            bool nv = Config.MemWrites.NightVision;
            if (UIControls.ToggleRow("Night Vision", ref nv, "Force NightVision component on (no NVG required)"))
            {
                Config.MemWrites.NightVision = nv;
                Config.MarkDirty();
            }

            bool thermal = Config.MemWrites.ThermalVision;
            if (UIControls.ToggleRow("Thermal Vision", ref thermal, "Force ThermalVision component on (auto-disables while ADS)"))
            {
                Config.MemWrites.ThermalVision = thermal;
                Config.MarkDirty();
            }

            bool noVisor = Config.MemWrites.NoVisor;
            if (UIControls.ToggleRow("No Visor", ref noVisor, "Remove the helmet visor (face-shield) overlay that obscures vision"))
            {
                Config.MemWrites.NoVisor = noVisor;
                Config.MarkDirty();
            }

            UIControls.Section("Silent Aim (experimental)");

            bool silentAim = Config.MemWrites.SilentAim;
            if (UIControls.ToggleRow("Silent Aim", ref silentAim,
                "Bullet redirect — bends fired rounds toward the selected target. The current target is ringed in red on the radar, aimview, and ESP."))
            {
                Config.MemWrites.SilentAim = silentAim;
                Config.MarkDirty();
            }

            // Engage-key hint — reflects the user's actual "Silent Aim (engage)" binding.
            var saKey = HotkeyManager.GetBindingDisplay("SilentAim");
            ImGui.TextDisabled(saKey is null
                ? "Engage: not bound — always-on while a target is in FOV. Bind \"Silent Aim (engage)\" in Hotkeys for hold/toggle."
                : $"Engage key: {saKey}  (hold/toggle — rebind in Hotkeys)");

            if (!silentAim)
                ImGui.BeginDisabled();

            ImGui.SeparatorText("Targeting");

            // Aim Bone is overridden while Random Bone is on, so grey it out to make that clear.
            bool saRandomBone = Config.MemWrites.SilentAimRandomBone;
            if (saRandomBone) ImGui.BeginDisabled();
            int saBone = Config.MemWrites.SilentAimBone;
            if (ImGui.Combo("Aim Bone##sa", ref saBone, _silentAimBones, _silentAimBones.Length))
            {
                Config.MemWrites.SilentAimBone = saBone;
                Config.MarkDirty();
            }
            if (saRandomBone) ImGui.EndDisabled();

            if (UIControls.ToggleRow("Random Bone", ref saRandomBone,
                "Pick a new bone on each shot (Head/Chest/Pelvis/Neck) so the pattern looks less robotic. Overrides Aim Bone while on."))
            {
                Config.MemWrites.SilentAimRandomBone = saRandomBone;
                Config.MarkDirty();
            }

            bool saHeadAI = Config.MemWrites.SilentAimHeadshotAI;
            if (UIControls.ToggleRow("Headshot AI", ref saHeadAI,
                "Always aim the Head on AI/scavs, while keeping your Aim Bone for human players."))
            {
                Config.MemWrites.SilentAimHeadshotAI = saHeadAI;
                Config.MarkDirty();
            }

            int saMode = Config.MemWrites.SilentAimTargetMode;
            if (ImGui.Combo("Target Priority##sa", ref saMode, _silentAimTargetModes, _silentAimTargetModes.Length))
            {
                Config.MemWrites.SilentAimTargetMode = saMode;
                Config.MarkDirty();
            }

            bool saSticky = Config.MemWrites.SilentAimStickyTarget;
            if (UIControls.ToggleRow("Sticky Target", ref saSticky,
                "Stay locked to one target until it dies or leaves your FOV/distance, instead of re-picking every tick. Stops flicking between enemies mid-burst."))
            {
                Config.MemWrites.SilentAimStickyTarget = saSticky;
                Config.MarkDirty();
            }

            bool saVis = Config.MemWrites.SilentAimVisibleOnly;
            if (UIControls.ToggleRow("Visible Only", ref saVis,
                "Only target enemies currently in line of sight."))
            {
                Config.MemWrites.SilentAimVisibleOnly = saVis;
                Config.MarkDirty();
            }

            float saFov = Config.MemWrites.SilentAimFov;
            if (ImGui.SliderFloat("FOV##sa", ref saFov, 5f, 400f, "%.0f px"))
            {
                Config.MemWrites.SilentAimFov = saFov;
                Config.MarkDirty();
            }

            float saDist = Config.MemWrites.SilentAimDistance;
            if (ImGui.SliderFloat("Distance##sa", ref saDist, 10f, 600f, "%.0f m"))
            {
                Config.MemWrites.SilentAimDistance = saDist;
                Config.MarkDirty();
            }

            ImGui.SeparatorText("Accuracy");

            bool saPerfect = Config.MemWrites.SilentAimPerfectAccuracy;
            if (UIControls.ToggleRow("Perfect Accuracy", ref saPerfect,
                "Zero the weapon's spread cone while aiming so the redirected bullet isn't scattered. Strongly recommended."))
            {
                Config.MemWrites.SilentAimPerfectAccuracy = saPerfect;
                Config.MarkDirty();
            }

            bool saPredict = Config.MemWrites.SilentAimPredict;
            if (UIControls.ToggleRow("Prediction", ref saPredict,
                "Lead moving targets + compensate for bullet drop."))
            {
                Config.MemWrites.SilentAimPredict = saPredict;
                Config.MarkDirty();
            }

            bool saReal = Config.MemWrites.SilentAimRealBallistics;
            if (UIControls.ToggleRow("Real Ballistics", ref saReal,
                "Use the loaded round's real muzzle velocity (+ barrel/attachment modifiers) and G1 drag, instead of the flat fallback below. Falls back automatically if the ammo can't be read."))
            {
                Config.MemWrites.SilentAimRealBallistics = saReal;
                Config.MarkDirty();
            }

            float saVel = Config.MemWrites.SilentAimMuzzleVelocity;
            if (ImGui.SliderFloat("Muzzle Velocity (fallback)##sa", ref saVel, 100f, 1200f, "%.0f m/s"))
            {
                Config.MemWrites.SilentAimMuzzleVelocity = saVel;
                Config.MarkDirty();
            }

            float saLatency = Config.MemWrites.SilentAimLatencyMs;
            if (ImGui.SliderFloat("Target Lead (ms)##sa", ref saLatency, 0f, 200f, "%.0f ms"))
            {
                Config.MemWrites.SilentAimLatencyMs = saLatency;
                Config.MarkDirty();
            }
            ImGui.TextDisabled("Extra lead for moving targets (read→impact latency). Raise if you trail fast/close strafers, lower if you over-lead.");

            ImGui.SeparatorText("Advanced");

            int saMethod = Config.MemWrites.SilentAimMethod;
            if (ImGui.Combo("Method##sa", ref saMethod, _silentAimMethods, _silentAimMethods.Length))
            {
                Config.MemWrites.SilentAimMethod = saMethod;
                Config.MarkDirty();
            }
            ImGui.TextDisabled("WeaponDirection = code-patch (accurate, recommended). ShotDirection = data-write fallback.");

            bool saMem = Config.MemWrites.SilentAimMemoryAim;
            if (UIControls.ToggleRow("Memory Aim", ref saMem,
                "Also snap your view rotation to the target (very accurate, but MOVES your screen — not silent). Optional, combines with the redirect."))
            {
                Config.MemWrites.SilentAimMemoryAim = saMem;
                Config.MarkDirty();
            }

            if (!silentAim)
                ImGui.EndDisabled();

            if (!masterEnabled)
                ImGui.EndDisabled();
        }
    }
}
