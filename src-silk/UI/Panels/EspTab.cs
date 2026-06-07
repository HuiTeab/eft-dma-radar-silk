// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static readonly string[] _espRenderModes = ["None", "Bones", "Box", "Head Dot"];
        private static readonly string[] _espCrosshairTypes = ["Plus", "Cross", "Circle", "Dot", "Square", "Diamond"];

        private static List<MonitorInfo>? _monitors;
        private static string[]? _monitorNames;

        private static void RefreshMonitors()
        {
            _monitors = MonitorInfo.GetAllMonitors();
            _monitorNames = _monitors.Select(m => m.DisplayName).ToArray();
        }

        private static void DrawEspTab()
        {
            ImGui.Spacing();

            // ── Window state ──
            bool open = eft_dma_radar.Silk.UI.ESP.EspWindow.IsOpen;
            if (UIControls.ToggleRow("ESP Window Open", ref open))
            {
                eft_dma_radar.Silk.UI.ESP.EspWindow.Toggle();
                Config.ShowEspWidget = eft_dma_radar.Silk.UI.ESP.EspWindow.IsOpen;
                Config.MarkDirty();
            }

            int espFps = Config.EspTargetFps;
            if (UIControls.Stepper("ESP Target FPS", ref espFps, 0, 360, 5,
                tooltip: "Render rate of the ESP window (0 = unlimited).\nIndependent of the radar FPS."))
            {
                Config.EspTargetFps = espFps;
                eft_dma_radar.Silk.UI.ESP.EspWindow.ApplyTargetFps();
                Config.MarkDirty();
            }

            UIControls.Section("Monitor");

            if (_monitors is null || _monitorNames is null)
                RefreshMonitors();

            int targetScreen = Config.EspTargetScreen;
            if (UIControls.ComboRow("Target Monitor", ref targetScreen, _monitorNames!,
                "Which monitor the ESP window opens on.\nUse 'Move ESP to Monitor' to reposition a running window."))
            {
                Config.EspTargetScreen = targetScreen;
                Config.MarkDirty();
            }

            if (ImGui.SmallButton("Refresh Monitors"))
                RefreshMonitors();

            if (eft_dma_radar.Silk.UI.ESP.EspWindow.IsOpen)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Move ESP to Monitor"))
                    eft_dma_radar.Silk.UI.ESP.EspWindow.ApplyTargetMonitor();
            }

            UIControls.Section("Players");

            bool showPlayers = Config.EspShowPlayers;
            if (UIControls.ToggleRow("Show Players", ref showPlayers))
            {
                Config.EspShowPlayers = showPlayers;
                Config.MarkDirty();
            }

            int mode = Config.EspRenderMode;
            if (UIControls.ComboRow("Render Mode", ref mode, _espRenderModes,
                "How each player is drawn.\nAlso cyclable via hotkey."))
            {
                Config.EspRenderMode = mode;
                Config.MarkDirty();
            }

            if (mode == 2) // Box
            {
                bool bones = Config.EspShowBones;
                if (UIControls.ToggleRow("Show Bones Inside Box", ref bones))
                {
                    Config.EspShowBones = bones;
                    Config.MarkDirty();
                }
            }

            float pDist = Config.EspPlayerDistance;
            if (UIControls.StepperFloat("Max Distance", ref pDist, 10f, 2000f, 10f, "{0:0}m",
                "Players beyond this distance are not drawn"))
            {
                Config.EspPlayerDistance = pDist;
                Config.MarkDirty();
            }

            UIControls.Section("Loot");

            bool showLoot = Config.EspShowLoot;
            if (UIControls.ToggleRow("Show Loot", ref showLoot))
            {
                Config.EspShowLoot = showLoot;
                Config.MarkDirty();
            }

            float lDist = Config.EspLootDistance;
            if (UIControls.StepperFloat("Max Distance ", ref lDist, 10f, 500f, 5f, "{0:0}m",
                "Loot beyond this distance is not drawn"))
            {
                Config.EspLootDistance = lDist;
                Config.MarkDirty();
            }

            UIControls.Section("Crosshair");

            bool crosshair = Config.EspShowCrosshair;
            if (UIControls.ToggleRow("Show Crosshair", ref crosshair))
            {
                Config.EspShowCrosshair = crosshair;
                Config.MarkDirty();
            }

            if (Config.EspShowCrosshair)
            {
                ImGui.Indent(16);

                int cType = Config.EspCrosshairType;
                if (UIControls.ComboRow("Style", ref cType, _espCrosshairTypes,
                    "Crosshair shape drawn at screen center"))
                {
                    Config.EspCrosshairType = cType;
                    Config.MarkDirty();
                }

                float cScale = Config.EspCrosshairScale;
                if (UIControls.StepperFloat("Scale", ref cScale, 0.5f, 5f, 0.1f, "{0:0.0}x",
                    "Crosshair size multiplier"))
                {
                    Config.EspCrosshairScale = cScale;
                    Config.MarkDirty();
                }

                ImGui.Unindent(16);
            }

            UIControls.Section("HUD");

            bool showFps = Config.EspShowFps;
            if (UIControls.ToggleRow("Show FPS", ref showFps))
            {
                Config.EspShowFps = showFps;
                Config.MarkDirty();
            }

            bool showStatus = Config.EspShowStatusText;
            if (UIControls.ToggleRow("Show Status Text", ref showStatus, "Banner listing active memory-write features (LEAN, 3P, NV, THERMAL, ...)"))
            {
                Config.EspShowStatusText = showStatus;
                Config.MarkDirty();
            }

            bool showEnergyHydration = Config.EspShowEnergyHydration;
            if (UIControls.ToggleRow("Show Energy / Hydration", ref showEnergyHydration, "Bottom-right bars showing local player energy + hydration"))
            {
                Config.EspShowEnergyHydration = showEnergyHydration;
                Config.MarkDirty();
            }
        }
    }
}
