// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.UI.Presets;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        // ── Input state for "Save current as new preset" form ──
        private static string _newPresetName = string.Empty;
        private static string _newPresetDescription = string.Empty;
        private static string? _saveError;

        // ── Per-row deletion confirmation ──
        private static string? _pendingDeleteId;

        // Display order of toggle groups in the per-preset editor. The toggles themselves are the
        // single source of truth in PresetManager.Toggles (each tagged with its Group).
        private static readonly string[] _presetGroupOrder = ["General", "Players", "World", "Doors", "Loot", "Combat", "Aimview", "ESP", "Performance"];

        private static void DrawPresetsTab()
        {
            ImGui.Spacing();
            PresetManager.EnsureSeeded(Config);

            // ── Header: active preset summary ──
            string activeName = PresetManager.DisplayNameFor(Config, Config.ActivePresetId);
            ImGui.TextColored(UITheme.Cyan, $"Active: {activeName}");
            if (Config.ActivePresetId == PresetManager.CustomId)
                ImGui.TextDisabled("(drift — current toggles don't match any saved preset)");
            else
                ImGui.TextDisabled("Click Apply on any preset to switch. Edit toggles below to tweak.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ── Preset list ──
            // Iterate over a snapshot since deletes mutate the list mid-loop.
            var snapshot = new List<RadarPresetEntry>(Config.Presets);
            foreach (var preset in snapshot)
                DrawPresetCard(preset);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawSaveCurrentForm();
        }

        private static void DrawPresetCard(RadarPresetEntry preset)
        {
            bool isActive = Config.ActivePresetId == preset.Id;
            bool isBuiltin = PresetManager.IsBuiltin(preset);

            ImGui.PushID(preset.Id);

            // Header row
            string title = isActive ? $"◉ {preset.DisplayName}" : preset.DisplayName;
            string subtitle = isBuiltin ? "built-in" : "user";
            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UITheme.Cyan);
                ImGui.TextUnformatted(title);
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.TextUnformatted(title);
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"  ({subtitle})");

            // Description preview
            if (!string.IsNullOrEmpty(preset.Description))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.68f, 0.72f, 1f));
                ImGui.TextWrapped(preset.Description);
                ImGui.PopStyleColor();
            }

            // Buttons row
            float btnH = 26f * Config.UIScale;
            if (!isActive)
            {
                if (ImGui.Button("Apply", new Vector2(80f * Config.UIScale, btnH)))
                    PresetManager.Apply(preset.Id, Config);
                ImGui.SameLine();
            }
            else
            {
                // Active preset — offer "Bake current toggles into this preset" if drifted
                if (!PresetManager.MatchesActive(Config))
                {
                    if (ImGui.Button("Save current toggles", new Vector2(160f * Config.UIScale, btnH)))
                    {
                        PresetManager.UpdateInPlace(Config, preset.Id);
                        Shell.ToastManager.Info($"Saved current toggles into '{preset.DisplayName}'");
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Overwrite this preset's stored toggle values\nwith the radar's current live state.");
                    ImGui.SameLine();
                }
            }

            if (isBuiltin)
            {
                if (ImGui.Button("Reset to defaults", new Vector2(140f * Config.UIScale, btnH)))
                {
                    PresetManager.ResetToDefaults(Config, preset.Id);
                    if (isActive)
                        PresetManager.Apply(preset.Id, Config);
                    Shell.ToastManager.Info($"Reset '{preset.DisplayName}' to defaults");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Restore name, description, and toggle values\nto the hardcoded baseline for this preset.");
                ImGui.SameLine();
            }
            else
            {
                if (_pendingDeleteId == preset.Id)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1f));
                    if (ImGui.Button("Confirm delete", new Vector2(120f * Config.UIScale, btnH)))
                    {
                        var name = preset.DisplayName;
                        PresetManager.Delete(Config, preset.Id);
                        _pendingDeleteId = null;
                        Shell.ToastManager.Info($"Deleted preset '{name}'");
                        ImGui.PopStyleColor();
                        ImGui.PopID();
                        ImGui.Separator();
                        return;
                    }
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel", new Vector2(80f * Config.UIScale, btnH)))
                        _pendingDeleteId = null;
                }
                else
                {
                    if (ImGui.Button("Delete", new Vector2(80f * Config.UIScale, btnH)))
                        _pendingDeleteId = preset.Id;
                }
                ImGui.SameLine();
            }

            // Foldout for details (name / description / toggles)
            bool open = ImGui.TreeNodeEx("##details", ImGuiTreeNodeFlags.None, "Edit");
            if (open)
            {
                ImGui.Spacing();
                ImGui.PushItemWidth(280f * Config.UIScale);

                string name = preset.DisplayName;
                if (ImGui.InputText("Name", ref name, 64) && name != preset.DisplayName)
                    PresetManager.Rename(Config, preset.Id, name);

                string desc = preset.Description;
                if (ImGui.InputTextMultiline("Description", ref desc, 256,
                        new Vector2(280f * Config.UIScale, 60f * Config.UIScale))
                    && desc != preset.Description)
                {
                    PresetManager.SetDescription(Config, preset.Id, desc);
                }
                ImGui.PopItemWidth();

                ImGui.Spacing();
                ImGui.TextDisabled("Toggles (saved in this preset):");
                DrawPresetToggleGroups(preset, isActive);

                ImGui.TreePop();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PopID();
        }

        /// <summary>Renders the preset's settings grouped: bool toggles as checkboxes, ints as sliders.</summary>
        private static void DrawPresetToggleGroups(RadarPresetEntry preset, bool isActive)
        {
            foreach (var group in _presetGroupOrder)
            {
                // Integer-valued groups (e.g. Performance / FPS) render as sliders, not checkboxes.
                if (group == "Performance")
                {
                    DrawPresetValueGroup(preset, isActive, group);
                    continue;
                }

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.55f, 0.78f, 0.92f, 1f), group);

                // Per-group All / None — handy now the toggle list is long.
                ImGui.SameLine();
                if (ImGui.SmallButton($"All##all_{preset.Id}_{group}"))
                    SetGroupAll(preset, isActive, group, true);
                ImGui.SameLine();
                if (ImGui.SmallButton($"None##none_{preset.Id}_{group}"))
                    SetGroupAll(preset, isActive, group, false);

                if (!ImGui.BeginTable($"##pt_{preset.Id}_{group}", 2, ImGuiTableFlags.SizingStretchProp))
                    continue;

                int col = 0;
                foreach (var t in PresetManager.Toggles)
                {
                    if (t.Group != group)
                        continue;
                    if (col % 2 == 0)
                        ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    bool v = t.GetEntry(preset);
                    if (ImGui.Checkbox($"{t.Label}##{preset.Id}_{t.Label}", ref v))
                    {
                        t.SetEntry(preset, v);
                        Config.MarkDirty();
                        // Re-apply live if this preset is active so the radar reflects the change now.
                        if (isActive)
                            PresetManager.Apply(preset.Id, Config);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(t.Tooltip);
                    col++;
                }
                ImGui.EndTable();
            }
        }

        /// <summary>Sets every toggle in a group on/off for the preset (the "All" / "None" buttons).</summary>
        private static void SetGroupAll(RadarPresetEntry preset, bool isActive, string group, bool value)
        {
            foreach (var t in PresetManager.Toggles)
                if (t.Group == group)
                    t.SetEntry(preset, value);
            Config.MarkDirty();
            if (isActive)
                PresetManager.Apply(preset.Id, Config);
        }

        /// <summary>Renders the integer (slider) preset settings for a group — e.g. Performance / FPS caps.</summary>
        private static void DrawPresetValueGroup(RadarPresetEntry preset, bool isActive, string group)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.55f, 0.78f, 0.92f, 1f), group);
            ImGui.PushItemWidth(200f * Config.UIScale);
            foreach (var s in PresetManager.Values)
            {
                if (s.Group != group)
                    continue;
                int val = s.GetEntry(preset);
                if (ImGui.SliderInt($"{s.Label}##{preset.Id}_{s.Label}", ref val, s.Min, s.Max))
                {
                    s.SetEntry(preset, val);
                    Config.MarkDirty();
                    if (isActive)
                        PresetManager.Apply(preset.Id, Config);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(s.Tooltip);
            }
            ImGui.PopItemWidth();
        }

        private static void DrawSaveCurrentForm()
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UITheme.Cyan);
            ImGui.TextUnformatted("Save current radar state as a new preset");
            ImGui.PopStyleColor();
            ImGui.TextDisabled("Captures every radar toggle from the live radar into a new named preset.");
            ImGui.Spacing();

            ImGui.PushItemWidth(280f * Config.UIScale);
            ImGui.InputText("Name##newPreset", ref _newPresetName, 64);
            ImGui.InputText("Description##newPreset", ref _newPresetDescription, 256);
            ImGui.PopItemWidth();

            bool canSave = !string.IsNullOrWhiteSpace(_newPresetName);
            if (!canSave) ImGui.BeginDisabled();
            if (ImGui.Button("Save as new preset", new Vector2(180f * Config.UIScale, 30f * Config.UIScale)))
            {
                var entry = PresetManager.SaveCurrentAsNew(Config, _newPresetName.Trim(), _newPresetDescription.Trim());
                if (entry is null)
                {
                    _saveError = "Name is required.";
                }
                else
                {
                    _saveError = null;
                    Shell.ToastManager.Info($"Saved preset '{entry.DisplayName}'");
                    _newPresetName = string.Empty;
                    _newPresetDescription = string.Empty;
                }
            }
            if (!canSave) ImGui.EndDisabled();

            if (_saveError is not null)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.90f, 0.45f, 0.45f, 1f));
                ImGui.TextUnformatted(_saveError);
                ImGui.PopStyleColor();
            }
        }
    }
}
