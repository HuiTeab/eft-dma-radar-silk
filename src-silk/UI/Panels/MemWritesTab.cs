// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
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

            if (!masterEnabled)
                ImGui.EndDisabled();
        }
    }
}
