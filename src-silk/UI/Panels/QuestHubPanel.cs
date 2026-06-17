// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Numerics;

using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.QuestPlanner;

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Unified Quests panel. Hosts two views behind a segmented toggle:
    ///   • <b>By Trader</b> — the live quest list (<see cref="QuestPanel"/>), works in-raid.
    ///   • <b>Map Planner</b> — the lobby map-recommendation planner (<see cref="QuestPlannerPanel"/>).
    ///
    /// The two sub-views share <see cref="SilkConfig.QuestKappaFilter"/>,
    /// <see cref="SilkConfig.QuestSelectedId"/> and <see cref="SilkConfig.QuestSelectedOnly"/>,
    /// so those controls are lifted here into a single header instead of being duplicated
    /// (and silently fighting each other) in both panels.
    /// </summary>
    internal static class QuestHubPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        internal enum Tab { ByTrader = 0, Planner = 1 }

        /// <summary>Whether the unified Quests panel is open.</summary>
        public static bool IsOpen { get; set; }

        /// <summary>Active sub-view. Persisted via <see cref="SilkConfig.QuestHubTab"/>.</summary>
        public static Tab ActiveTab { get; set; } = Tab.ByTrader;

        /// <summary>Opens the panel on a specific tab (used by the Windows menu).</summary>
        public static void Open(Tab tab)
        {
            IsOpen = true;
            ActiveTab = tab;
        }

        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("❖ Quests", ref isOpen, new Vector2(560, 640));
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            DrawSharedHeader();
            DrawTabSelector();
            ImGui.Separator();

            if (ActiveTab == Tab.Planner)
                QuestPlannerPanel.DrawEmbedded();
            else
                QuestPanel.DrawEmbedded();
        }

        // ── Shared header ────────────────────────────────────────────────────
        // Controls common to both views: the Kappa progression filter and the
        // selected-quest pin that the radar follows.

        private static void DrawSharedHeader()
        {
            bool kappa = Config.QuestKappaFilter;
            if (ImGui.Checkbox("Kappa Only", ref kappa))
            {
                Config.QuestKappaFilter = kappa;
                Config.MarkDirty();
                QuestPlannerWorker.ForceRecompute();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only show quests required for the Kappa container.\nShared by both views.");

            ImGui.SameLine();
            bool selectedOnly = Config.QuestSelectedOnly;
            if (ImGui.Checkbox("Selected Only", ref selectedOnly))
            {
                Config.QuestSelectedOnly = selectedOnly;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, only the selected quest's items and zones are drawn on the radar.\nRight-click a quest to select it.");

            if (!string.IsNullOrEmpty(Config.QuestSelectedId))
            {
                ImGui.SameLine();
                ImGui.TextColored(UITheme.Cyan, "◉"); // ◉
                ImGui.SameLine();
                ImGui.TextColored(UITheme.Kappa, GetQuestName(Config.QuestSelectedId));
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear##selQuest"))
                {
                    Config.QuestSelectedId = "";
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("This quest's zones & items are pinned to the radar.\nRight-click a quest to change.");
            }
        }

        // ── Tab selector ─────────────────────────────────────────────────────

        private static void DrawTabSelector()
        {
            DrawTabButton("By Trader", Tab.ByTrader);
            ImGui.SameLine();
            DrawTabButton("Map Planner", Tab.Planner);
        }

        private static void DrawTabButton(string label, Tab tab)
        {
            bool active = ActiveTab == tab;
            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.45f, 0.45f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.55f, 0.55f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.65f, 0.65f, 1f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 1, 1, 0.06f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1, 1, 1, 0.10f));
            }

            if (ImGui.Button($"{label}##questtab{(int)tab}"))
                ActiveTab = tab;

            ImGui.PopStyleColor(3);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string GetQuestName(string questId)
            => EftDataManager.TaskData.TryGetValue(questId, out var t) && !string.IsNullOrEmpty(t.Name)
                ? t.Name
                : questId;
    }
}
