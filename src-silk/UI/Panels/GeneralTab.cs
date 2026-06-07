// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static async Task ToggleWebRadarAsync(bool enable)
        {
            try
            {
                if (enable)
                {
                    await eft_dma_radar.Silk.Web.WebRadarServer.StartAsync(
                        Config.WebRadarPort,
                        TimeSpan.FromMilliseconds(Config.WebRadarTickMs),
                        Config.WebRadarUPnP);
                }
                else
                {
                    await eft_dma_radar.Silk.Web.WebRadarServer.StopAsync();
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[WebRadar] Toggle error: {ex.Message}");
            }
        }

        private static void DrawGeneralTab()
        {
            ImGui.Spacing();

            UIControls.Section("Display");

            float uiScale = Config.UIScale;
            if (UIControls.StepperFloat("UI Scale", ref uiScale, 0.5f, 2.0f, 0.1f, "{0:0.0}x",
                "Scale the radar canvas rendering"))
            {
                Config.UIScale = uiScale;
                Config.MarkDirty();
            }

            int fps = Config.TargetFps;
            if (UIControls.Stepper("Target FPS", ref fps, 0, 360, 5,
                tooltip: "Max frames per second (0 = unlimited). Hold +/- to ramp quickly."))
            {
                Config.TargetFps = fps;
                RadarWindow.Window.FramesPerSecond = fps;
                Config.MarkDirty();
            }

            int rtFps = Config.RealtimeTargetFps;
            if (UIControls.Stepper("Realtime FPS (RT)", ref rtFps,
                SilkConfig.RealtimeTargetFpsMin, SilkConfig.RealtimeTargetFpsMax, 5,
                tooltip: "Tick rate of the realtime DMA worker reading player positions (shown as \"RT\" in the stats overlay).\nHigher = smoother movement but more DMA/CPU load. Applies live. Default 125."))
            {
                Config.RealtimeTargetFps = rtFps;
                Config.MarkDirty();
            }

            UIControls.Section("Modes");

            bool battleMode = Config.BattleMode;
            if (UIControls.ToggleRow("Battle Mode  [B]", ref battleMode,
                "Hide loot and clutter; focus on players only"))
            {
                Config.SetBattleMode(battleMode);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Hideout");

            bool hideoutEnabled = Config.HideoutEnabled;
            if (ImGui.Checkbox("Enable Hideout", ref hideoutEnabled))
            {
                Config.HideoutEnabled = hideoutEnabled;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Read stash items and area upgrades when entering the hideout");

            if (Config.HideoutEnabled)
            {
                ImGui.Indent(16);
                bool autoRefresh = Config.HideoutAutoRefresh;
                if (ImGui.Checkbox("Auto Refresh", ref autoRefresh))
                {
                    Config.HideoutAutoRefresh = autoRefresh;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically refresh stash and area data on hideout entry");
                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Match Dump");

            bool matchDump = Config.EnableMatchDump;
            if (ImGui.Checkbox("Enable Match Dump", ref matchDump))
            {
                Config.EnableMatchDump = matchDump;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Serialize all radar data (players, loot, corpses, exfils\u2026) to a JSON file in the dumps\\ folder.\nUse the button below to trigger a snapshot manually.");

            if (Config.EnableMatchDump)
            {
                ImGui.Indent(16);
                bool canDump = Memory.InRaid;
                if (!canDump)
                    ImGui.BeginDisabled();
                if (ImGui.Button("\u21a7 Dump Match Now"))
                    Memory.Game?.DumpMatchNow();
                    //Memory.Game?.DumpContainersNow();
                if (!canDump)
                    ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(canDump
                        ? "Write a full match snapshot to dumps\\ right now"
                        : "Only available during an active raid");
                ImGui.Unindent(16);
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Radar");

            {
                bool canRestart = Memory.InRaid || Memory.InHideout;
                if (!canRestart)
                    ImGui.BeginDisabled();

                if (ImGui.Button("\u21bb Restart Radar"))
                    Memory.RestartRadar = true;

                if (!canRestart)
                    ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(canRestart
                        ? "Restart the radar (re-detect game world, players, loot)"
                        : "Only available during a raid or in the hideout");

                ImGui.SameLine();
                if (ImGui.Button("\u2728 Show Welcome Tour"))
                    eft_dma_radar.Silk.UI.Shell.FirstRunTour.Open();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Replay the first-run UX tour (sidebar, status bar, presets, palette).");
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Web Radar");

            bool webEnabled = Config.WebRadarEnabled;
            if (ImGui.Checkbox("Enable Web Radar", ref webEnabled))
            {
                Config.WebRadarEnabled = webEnabled;
                Config.MarkDirty();
                _ = ToggleWebRadarAsync(webEnabled);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start/stop the web radar HTTP server.\nAccess from a browser on any device on your network.");

            if (Config.WebRadarEnabled)
            {
                ImGui.Indent(16);

                ImGui.SetNextItemWidth(120);
                int port = Config.WebRadarPort;
                if (ImGui.InputInt("Port", ref port, 0, 0))
                {
                    if (port is >= 1024 and <= 65535)
                    {
                        Config.WebRadarPort = port;
                        Config.MarkDirty();
                    }
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("HTTP port (requires restart to take effect)");

                ImGui.SetNextItemWidth(120);
                int tickMs = Config.WebRadarTickMs;
                if (ImGui.SliderInt("Tick (ms)", ref tickMs, 20, 200))
                {
                    Config.WebRadarTickMs = tickMs;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Update interval for the web radar data");

                bool upnp = Config.WebRadarUPnP;
                if (ImGui.Checkbox("UPnP / NAT-PMP", ref upnp))
                {
                    Config.WebRadarUPnP = upnp;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically forward the port on your router via UPnP.\nEnables access from outside your network.\nTakes effect on next server start.");

                if (eft_dma_radar.Silk.Web.WebRadarServer.IsRunning)
                {
                    ImGui.TextColored(new Vector4(0.26f, 0.84f, 0.50f, 1f),
                        $"\u25cf Running on port {Config.WebRadarPort}");

                    // Private address
                    ImGui.Spacing();
                    var privateAddr = eft_dma_radar.Silk.Web.WebRadarServer.PrivateAddress;
                    if (!string.IsNullOrEmpty(privateAddr))
                    {
                        ImGui.Text("Private:");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.55f, 0.83f, 1f, 1f), privateAddr);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\uf0c5 Copy##private"))
                            ImGui.SetClipboardText(privateAddr);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Copy private (LAN) address to clipboard");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\u2197 Open##private"))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(privateAddr) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Open in default browser");
                    }

                    // Public address
                    var publicAddr = eft_dma_radar.Silk.Web.WebRadarServer.PublicAddress;
                    if (!string.IsNullOrEmpty(publicAddr))
                    {
                        ImGui.Text("Public: ");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0.40f, 1f), publicAddr);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\uf0c5 Copy##public"))
                            ImGui.SetClipboardText(publicAddr);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Copy public (WAN) address to clipboard");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("\u2197 Open##public"))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(publicAddr) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Open in default browser");
                    }
                    else if (string.IsNullOrEmpty(publicAddr) && !string.IsNullOrEmpty(privateAddr))
                    {
                        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                            "Public:  Detecting...");
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                        "\u25cb Stopped");
                }

                ImGui.Unindent(16);
            }
        }
    }
}
