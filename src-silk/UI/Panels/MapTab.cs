// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.DMA;
using eft_dma_radar.Silk.UI.Maps;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static void DrawMapTab()
        {
            ImGui.Spacing();

            int zoom = RadarWindow.Zoom;
            if (UIControls.Stepper("Zoom", ref zoom, 1, 200, 5,
                tooltip: "Radar zoom level (lower = more zoomed in)"))
                RadarWindow.Zoom = zoom;

            bool freeMode = RadarWindow.FreeMode;
            if (UIControls.ToggleRow("Free Mode  [F]", ref freeMode, "Toggle between player-follow and free-pan"))
                RadarWindow.FreeMode = freeMode;

            bool useSatellite = Config.UseSatelliteMap;
            if (UIControls.ToggleRow("Satellite Map", ref useSatellite, "Use satellite imagery from assets.tarkov.dev (falls back to SVG for unsupported maps)"))
            {
                Config.UseSatelliteMap = useSatellite;
                // Mutual exclusion — enabling satellite turns off tarkov.dev SVG (satellite wins).
                if (useSatellite) Config.UseTarkovDevMap = false;
                Config.MarkDirty();
                Maps.MapManager.ReloadCurrent();
            }

            bool useTarkovDevSvg = Config.UseTarkovDevMap;
            if (UIControls.ToggleRow("Tarkov.dev SVG Map", ref useTarkovDevSvg,
                "Download maps from assets.tarkov.dev (Customs.svg, Woods.svg, ...) instead of using bundled SVGs.\n" +
                "Cached persistently under %AppData%\\eft-dma-radar-silk\\tarkov-dev-maps\\.\n" +
                "Ignored while Satellite Map is on. Falls back to bundled SVG on download failure."))
            {
                Config.UseTarkovDevMap = useTarkovDevSvg;
                if (useTarkovDevSvg) Config.UseSatelliteMap = false;
                Config.MarkDirty();
                Maps.MapManager.ReloadCurrent();
            }

            // Rotation control for tarkov.dev SVG maps (only meaningful when active).
            if (Config.UseTarkovDevMap)
            {
                ImGui.Indent(16);
                int rot = Config.TarkovDevMapRotation;
                int rotIdx = rot switch { 90 => 1, 180 => 2, 270 => 3, _ => 0 };
                string[] rotLabels = ["0° (raw)", "90° (left)", "180°", "270° (right)"];
                ImGui.SetNextItemWidth(160);
                if (ImGui.Combo("Rotation##TarkovDevRot", ref rotIdx, rotLabels, rotLabels.Length))
                {
                    int[] rotValues = [0, 90, 180, 270];
                    Config.TarkovDevMapRotation = rotValues[rotIdx];
                    Config.MarkDirty();
                    Maps.MapManager.ReloadCurrent();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Visual rotation applied to tarkov.dev SVG maps. tarkov.dev's site\n" +
                                      "displays them rotated 180° via Leaflet's CRS; pick the orientation\n" +
                                      "you prefer here. Markers re-align automatically.");
                ImGui.Unindent(16);
            }

            DrawMapSetupSection();

            UIControls.Section("Corpses");

            bool showCorpses = Config.ShowCorpses;
            if (UIControls.ToggleRow("Show Corpses", ref showCorpses, "Show corpse X markers on the radar"))
            {
                Config.ShowCorpses = showCorpses;
                Config.MarkDirty();
            }

            UIControls.Section("Loot Markers");

            float dotSize = Config.LootDotSize;
            if (UIControls.StepperFloat("Dot Size", ref dotSize, 1.5f, 8f, 0.5f, "{0:0.0} px",
                "Base radius of loot dots. Tier/important bumps are added on top."))
            {
                Config.LootDotSize = dotSize;
                Config.MarkDirty();
            }

            float labelFont = Config.LootLabelFontSize;
            if (UIControls.StepperFloat("Label Font", ref labelFont, 8f, 22f, 1f, "{0:0} px",
                "Font size of loot labels on the radar."))
            {
                Config.LootLabelFontSize = labelFont;
                Config.MarkDirty();
            }

            bool heightArrows = Config.LootShowHeightArrows;
            if (UIControls.ToggleRow("Height Arrows (▲/▼)", ref heightArrows, "Show an up/down arrow on loot that is above or below your floor."))
            {
                Config.LootShowHeightArrows = heightArrows;
                Config.MarkDirty();
            }

            if (Config.LootShowHeightArrows)
            {
                ImGui.Indent(16);
                float thr = Config.LootHeightArrowThreshold;
                if (UIControls.StepperFloat("Height Threshold", ref thr, 0.5f, 5f, 0.25f, "{0:0.00} m",
                    "Vertical distance (±m) before an arrow is drawn."))
                {
                    Config.LootHeightArrowThreshold = thr;
                    Config.MarkDirty();
                }

                bool showDelta = Config.LootShowHeightDelta;
                if (UIControls.ToggleRow("Show Height (+/-m)", ref showDelta, "Append the exact vertical offset in meters to the loot label."))
                {
                    Config.LootShowHeightDelta = showDelta;
                    Config.MarkDirty();
                }
                ImGui.Unindent(16);
            }

            UIControls.Section("Containers");

            bool showContainers = Config.ShowContainers;
            if (UIControls.ToggleRow("Show Containers", ref showContainers, "Show static loot containers on the radar (duffle bags, toolboxes, etc.)"))
            {
                Config.ShowContainers = showContainers;
                Config.MarkDirty();
            }

            if (Config.ShowContainers)
            {
                ImGui.Indent(16);
                bool showContainerNames = Config.ShowContainerNames;
                if (UIControls.ToggleRow("Show Names", ref showContainerNames, "Show container name labels next to markers"))
                {
                    Config.ShowContainerNames = showContainerNames;
                    Config.MarkDirty();
                }

                bool hideSearched = Config.HideSearchedContainers;
                if (UIControls.ToggleRow("Hide Searched", ref hideSearched, "Hide containers that have been opened/searched"))
                {
                    Config.HideSearchedContainers = hideSearched;
                    Config.MarkDirty();
                }

                ImGui.Spacing();
                DrawContainerSelection();
                ImGui.Unindent(16);
            }

            UIControls.Section("Exfils");

            bool showExfils = Config.ShowExfils;
            if (UIControls.ToggleRow("Show Exfils", ref showExfils, "Show exfiltration points on the radar"))
            {
                Config.ShowExfils = showExfils;
                Config.MarkDirty();
            }

            if (Config.ShowExfils)
            {
                ImGui.Indent(16);

                bool hideInactive = Config.HideInactiveExfils;
                if (UIControls.ToggleRow("Hide Inactive", ref hideInactive, "Hide closed or unavailable exfils"))
                {
                    Config.HideInactiveExfils = hideInactive;
                    Config.MarkDirty();
                }

                ImGui.Unindent(16);
            }

            UIControls.Section("Transits");

            bool showTransits = Config.ShowTransits;
            if (UIControls.ToggleRow("Show Transits", ref showTransits, "Show transit points (map-to-map travel) on the radar"))
            {
                Config.ShowTransits = showTransits;
                Config.MarkDirty();
            }

            if (Config.ShowTransits)
            {
                ImGui.Indent(16);

                bool continuousTransits = Config.TransitContinuousRefresh;
                if (UIControls.ToggleRow("Continuously Refresh", ref continuousTransits,
                    "Re-read transit status every tick like exfils. Off (default): a transit's status is " +
                    "polled only until it opens (~1 min into the raid), then left alone. Transits stay open " +
                    "once available, so continuously updating their status is basically pointless. " +
                    "(Position is always read from memory once at discovery either way.)"))
                {
                    Config.TransitContinuousRefresh = continuousTransits;
                    Config.MarkDirty();
                }

                ImGui.Unindent(16);
            }

            UIControls.Section("Doors");

            bool showDoors = Config.ShowDoors;
            if (UIControls.ToggleRow("Show Doors", ref showDoors, "Show keyed doors on the radar"))
            {
                Config.ShowDoors = showDoors;
                Config.MarkDirty();
            }

            if (Config.ShowDoors)
            {
                ImGui.Indent(16);

                bool showLocked = Config.ShowLockedDoors;
                if (UIControls.ToggleRow("Show Locked", ref showLocked, "Show locked doors (red)"))
                {
                    Config.ShowLockedDoors = showLocked;
                    Config.MarkDirty();
                }

                bool showUnlocked = Config.ShowUnlockedDoors;
                if (UIControls.ToggleRow("Show Unlocked", ref showUnlocked, "Show open or shut doors (green/orange)"))
                {
                    Config.ShowUnlockedDoors = showUnlocked;
                    Config.MarkDirty();
                }

                bool onlyNearLoot = Config.DoorsOnlyNearLoot;
                if (UIControls.ToggleRow("Only Near Valuable Loot", ref onlyNearLoot, "Only show doors near important (high-value) loot items"))
                {
                    Config.DoorsOnlyNearLoot = onlyNearLoot;
                    Config.MarkDirty();
                }

                if (Config.DoorsOnlyNearLoot)
                {
                    ImGui.Indent(16);

                    float proximity = Config.DoorLootProximity;
                    if (UIControls.StepperFloat("Proximity", ref proximity, 5f, 100f, 5f, "{0:0} m",
                        "Max distance from door to valuable loot"))
                    {
                        Config.DoorLootProximity = proximity;
                        Config.MarkDirty();
                    }

                    ImGui.Unindent(16);
                }

                ImGui.Unindent(16);
            }

            UIControls.Section("Explosives & BTR");

            bool showExplosives = Config.ShowExplosives;
            if (UIControls.ToggleRow("Show Explosives", ref showExplosives, "Show grenades, tripwires, and mortar projectiles on the radar"))
            {
                Config.ShowExplosives = showExplosives;
                Config.MarkDirty();
            }

            if (Config.ShowExplosives)
            {
                ImGui.Indent(16);

                bool showTripwireLines = Config.ShowTripwireLines;
                if (UIControls.ToggleRow("Show Tripwire Lines", ref showTripwireLines, "Draw a line between tripwire endpoints"))
                {
                    Config.ShowTripwireLines = showTripwireLines;
                    Config.MarkDirty();
                }

                ImGui.Unindent(16);
            }

            bool showBtr = Config.ShowBTR;
            if (UIControls.ToggleRow("Show BTR", ref showBtr, "Show the BTR armored vehicle on the radar (Streets/Woods)"))
            {
                Config.ShowBTR = showBtr;
                Config.MarkDirty();
            }

            ImGui.Indent(16);
            bool showBtrRoute = Config.ShowBTRRoute;
            if (UIControls.ToggleRow("Show BTR Route Stops", ref showBtrRoute, "Show BTR route stop markers on the radar"))
            {
                Config.ShowBTRRoute = showBtrRoute;
                Config.MarkDirty();
            }
            ImGui.Unindent(16);

            UIControls.Section("Killfeed Overlay");

            bool showKf = Config.ShowKillFeed;
            if (UIControls.ToggleRow("Show Killfeed Overlay", ref showKf, "Draw kill events on the radar canvas (top-right corner).\nOpen the Killfeed panel (\u2620) for the full table and settings."))
            {
                Config.ShowKillFeed = showKf;
                Config.MarkDirty();
            }

            if (Config.ShowKillFeed)
            {
                ImGui.Indent(16);

                int maxEnt = Config.KillFeedMaxEntries;
                if (UIControls.Stepper("Max Entries", ref maxEnt, 1, 10, 1,
                    tooltip: "Maximum number of kill events visible at once"))
                {
                    Config.KillFeedMaxEntries = maxEnt;
                    Config.MarkDirty();
                }

                int ttl = Config.KillFeedTtlSeconds;
                if (UIControls.Stepper("Entry TTL", ref ttl, 5, 600, 5, "{0} s",
                    tooltip: "Seconds before a killfeed entry fades out (5–600)."))
                {
                    Config.KillFeedTtlSeconds = ttl;
                    Config.MarkDirty();
                }

                if (ImGui.Button("Reset Killfeed Position"))
                {
                    Config.KillFeedPosX = -1f;
                    Config.KillFeedPosY = -1f;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Snap the killfeed overlay back to the top-right corner.");

                ImGui.Unindent(16);
            }
        }

        private static void DrawMapSetupSection()
        {
            if (!UIControls.BeginAdvanced("Map Setup (Calibration)"))
                return;

            var map = MapManager.Map;
            if (map is null)
            {
                ImGui.TextDisabled("No map loaded.");
                UIControls.EndAdvanced();
                return;
            }

            var cfg = map.Config;

            // Live player position readout (X / Z / Y in EFT world space — Z&Y swapped for display)
            var lp = Memory.LocalPlayer;
            if (lp is not null)
            {
                var pos = lp.Position;
                ImGui.Text($"Player  X: {pos.X:0.000}   Y: {pos.Z:0.000}   Z: {pos.Y:0.000}");
            }
            else
            {
                ImGui.TextDisabled("Player position unavailable.");
            }

            ImGui.SetNextItemWidth(160);
            float x = cfg.X;
            if (ImGui.DragFloat("Map X", ref x, 1.0f, -10000f, 10000f, "%.2f"))
                cfg.X = x;

            ImGui.SetNextItemWidth(160);
            float y = cfg.Y;
            if (ImGui.DragFloat("Map Y", ref y, 1.0f, -10000f, 10000f, "%.2f"))
                cfg.Y = y;

            ImGui.SetNextItemWidth(160);
            float scale = cfg.Scale;
            if (ImGui.DragFloat("Map Scale", ref scale, 0.001f, 0.001f, 100f, "%.4f"))
                cfg.Scale = scale;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Runtime calibration — adjust until your player marker\naligns with your real world position. Click 'Save Calibration'\nbelow to persist these values across restarts.");

            // Save / Reset buttons: persist (X, Y, Scale) per primary map ID. The web
            // radar serves the live in-memory MapConfig each tick, so saved values flow
            // to web clients on their next update without a manual refresh.
            ImGui.Spacing();
            string? primaryId = cfg.MapID.Count > 0 ? cfg.MapID[0] : null;
            bool hasOverride = !string.IsNullOrWhiteSpace(primaryId)
                && Config.MapCalibrationOverrides.ContainsKey(primaryId!);

            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(primaryId));
            if (ImGui.Button("Save Calibration"))
            {
                Config.MapCalibrationOverrides[primaryId!] = new MapCalibration
                {
                    X = cfg.X,
                    Y = cfg.Y,
                    Scale = cfg.Scale,
                };
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Persist the current Map X / Y / Scale to disk under map id '{primaryId}'.\n" +
                                  "Reloaded automatically on next startup. Web radar clients pick up\n" +
                                  "the new values on their next tick.");
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.BeginDisabled(!hasOverride);
            if (ImGui.Button("Reset to Bundled"))
            {
                Config.MapCalibrationOverrides.Remove(primaryId!);
                Config.MarkDirty();
                // Reload the map so the bundled defaults from the JSON file replace
                // the in-memory overridden values.
                Maps.MapManager.ReloadCurrent();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Remove the saved calibration for '{primaryId}' and reload\n" +
                                  "the bundled defaults from the map JSON file.");
            ImGui.EndDisabled();

            if (hasOverride)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(saved)");
            }

            UIControls.EndAdvanced();
        }
    }
}
