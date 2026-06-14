// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static void DrawHotkeysTab()
        {
            ImGui.Spacing();

            // Bindings live in config and can be reviewed/edited offline; only live
            // capture + execution need the DMA link. Show a hint instead of blanking
            // the whole tab so keys can be set up before the game is connected.
            if (!InputManager.IsReady)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.72f, 0.28f, 1f));
                ImGui.TextWrapped("\u26a0 Not connected to the game yet \u2014 hotkeys activate automatically once it connects. " +
                    "You can still review and edit your bindings below.");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            ImGui.TextWrapped("Manage hotkeys in the dedicated Hotkeys panel.");
            ImGui.Spacing();

            if (ImGui.Button("\u2328 Open Hotkey Manager", new Vector2(200, 0)))
                HotkeyManagerPanel.IsOpen = true;

            UIControls.Section("Active Hotkeys");

            var hotkeys = Config.Hotkeys;
            if (hotkeys.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No hotkeys configured.");
            }
            else
            {
                foreach (var (id, entry) in hotkeys)
                {
                    if (!entry.Enabled || entry.Key < 1)
                        continue;

                    var def = HotkeyManager.GetAction(id);
                    string name = def?.DisplayName ?? id;
                    string mode = entry.Mode == HotkeyMode.Toggle ? "Toggle" : "OnKey";

                    ImGui.BulletText($"{name}  [{VK.GetName(entry.Key)}]  ({mode})");
                }
            }
        }
    }
}
