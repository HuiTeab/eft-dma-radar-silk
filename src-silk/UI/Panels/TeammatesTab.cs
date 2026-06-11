// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins;
using eft_dma_radar.Silk.UI.Shell;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        /// <summary>
        /// Settings → Teammates. Review and remove the saved teammates marked via Shift+click
        /// on the radar. Marks persist across restarts (see <see cref="TeammateList"/>).
        /// </summary>
        private static void DrawTeammatesTab()
        {
            ImGui.Spacing();

            bool enabled = Config.TeammatesEnabled;
            if (UIControls.ToggleRow("Enable Persistent Teammates", ref enabled,
                "Auto-apply your saved teammate marks each raid. Marked players render as\n" +
                "teammates (friendly) instead of hostile. Turn off to ignore saved marks\n" +
                "without deleting them."))
            {
                Config.TeammatesEnabled = enabled;
                Config.MarkDirty();
            }

            ImGui.TextWrapped("Shift + Left-click a player on the radar to mark or unmark them as a teammate. " +
                "Marks are saved by Account ID and Profile ID, so they persist across restarts and re-raids.");

            ImGui.Spacing();

            // Live gauge — how many on-radar players are flagged right now.
            if (Memory.InRaid && Memory.Players is { } livePlayers)
            {
                int flagged = 0;
                foreach (var p in livePlayers)
                    if (p.IsManualTeammate) flagged++;
                ImGui.TextDisabled($"Currently flagged this raid: {flagged}");
            }

            var teammates = Memory.Teammates;
            var entries = teammates.Entries;

            ImGui.TextDisabled($"Saved teammates: {entries.Count}");
            ImGui.SameLine();
            ImGui.BeginDisabled(entries.Count == 0);
            if (ImGui.SmallButton("Clear all"))
                ImGui.OpenPopup("##confirm_clear_teammates");
            ImGui.EndDisabled();

            // Destructive + irreversible — confirm before wiping the whole list.
            if (ImGui.BeginPopup("##confirm_clear_teammates"))
            {
                ImGui.TextUnformatted($"Remove all {entries.Count} saved teammate(s)?");
                ImGui.TextDisabled("This can't be undone.");
                ImGui.Spacing();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.65f, 0.20f, 0.20f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.78f, 0.25f, 0.25f, 1f));
                if (ImGui.Button("Remove all", new Vector2(100f, 0)))
                {
                    foreach (var e in entries)
                        TeammatesManager.RestoreMatching(e.AccountId, e.ProfileId);
                    teammates.Clear();
                    ToastManager.Success($"Removed {entries.Count} saved teammate(s)");
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopStyleColor(2);

                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(80f, 0)))
                    ImGui.CloseCurrentPopup();

                ImGui.EndPopup();
            }

            ImGui.Spacing();

            if (entries.Count == 0)
            {
                ImGui.TextColored(UITheme.Grey, "No saved teammates yet. Shift+Left-click a player to add one.");
                return;
            }

            float tableH = ImGui.GetContentRegionAvail().Y;
            if (tableH < 120f) tableH = 120f;

            var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                      | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("TeammatesTable", 3, flags, new Vector2(0, tableH)))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2.2f);
                ImGui.TableSetupColumn("Added", ImGuiTableColumnFlags.None, 1.1f);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None, 0.7f);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                TeammateEntry? removeEntry = null;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    ImGui.TableNextRow();

                    // Name leads; the raw ids only matter for debugging, so they live in a tooltip.
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(string.IsNullOrEmpty(e.Name) ? "(unknown)" : e.Name);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(
                            $"Account ID: {(string.IsNullOrEmpty(e.AccountId) ? "—" : e.AccountId)}\n" +
                            $"Profile ID: {(string.IsNullOrEmpty(e.ProfileId) ? "—" : e.ProfileId)}\n" +
                            $"Added: {e.AddedDate:yyyy-MM-dd HH:mm}");

                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(FormatAdded(e.AddedDate));

                    ImGui.TableNextColumn();
                    ImGui.PushID(i);
                    if (ImGui.SmallButton("Remove"))
                        removeEntry = e;
                    ImGui.PopID();
                }

                ImGui.EndTable();

                if (removeEntry is not null)
                {
                    TeammatesManager.RestoreMatching(removeEntry.AccountId, removeEntry.ProfileId);
                    teammates.Remove(removeEntry.AccountId, removeEntry.ProfileId);
                }
            }
        }

        /// <summary>Compact "when was this mark made" label for the Added column.</summary>
        private static string FormatAdded(DateTime added)
        {
            int days = (DateTime.Now.Date - added.Date).Days;
            return days switch
            {
                <= 0 => "today",
                1 => "yesterday",
                < 30 => $"{days}d ago",
                _ => added.ToString("yyyy-MM-dd"),
            };
        }
    }
}
