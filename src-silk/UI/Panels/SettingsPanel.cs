// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.UI.Presets;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>Live filter for the cross-tab settings search box. Empty = normal tab view.</summary>
        private static string _search = string.Empty;

        /// <summary>
        /// Settings categories shown in the left nav. The order here drives the visible order.
        /// Glyphs are bold letters so they render with the default ImGui font (no extra glyph ranges needed).
        /// </summary>
        private static readonly (string Glyph, string Label, Action Draw)[] _categories =
        [
            ("G", "General",     DrawGeneralTab),
            ("R", "Presets",     DrawPresetsTab),
            ("P", "Players",     DrawPlayersTab),
            ("T", "Teammates",   DrawTeammatesTab),
            ("B", "Boss Guards", DrawGuardsTab),
            ("E", "ESP",         DrawEspTab),
            ("M", "Map",         DrawMapTab),
            ("Q", "Quest Zones", DrawQuestZonesTab),
            ("K", "Hotkeys",     DrawHotkeysTab),
            ("W", "Mem Writes",  DrawMemWritesTab),
        ];

        /// <summary>
        /// Currently selected category index. Persists for the session (not saved to disk —
        /// the user almost always wants to land on General first).
        /// </summary>
        private static int _activeCategory;

        /// <summary>
        /// Whether the settings panel is open.
        /// </summary>
        public static bool IsOpen { get; set; }

        /// <summary>
        /// When Settings is open, the category-letter glyphs (G/P/E/M/Q/K/W)
        /// switch tabs instead of triggering global panel toggles. Returns
        /// true if the key was consumed.
        /// </summary>
        public static bool HandleCategoryShortcuts()
        {
            if (!IsOpen)
                return false;

            for (int i = 0; i < _categories.Length; i++)
            {
                var glyph = _categories[i].Glyph;
                if (glyph.Length != 1)
                    continue;

                // Map the single-letter glyph to its ImGuiKey enum value.
                char c = char.ToUpperInvariant(glyph[0]);
                if (c < 'A' || c > 'Z')
                    continue;

                var key = (ImGuiKey)((int)ImGuiKey.A + (c - 'A'));
                if (ImGui.IsKeyPressed(key, false))
                {
                    _activeCategory = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Draw the settings panel using the Phase 3 shell:
        ///   [ left nav (icon + label, one per category) ] [ scrolling content pane ]
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            using (var scope = PanelWindow.Begin("\u2699 Settings", ref isOpen, new Vector2(720, 640)))
            {
                IsOpen = isOpen;
                if (!isOpen)
                    _search = string.Empty; // don't greet the next open with stale results
                if (!scope.Visible)
                    return;

                DrawSearchBar();

                if (!string.IsNullOrWhiteSpace(_search))
                    DrawSearchResults();
                else
                    DrawCategoryShell();

                // Config auto-saves via SilkConfig.MarkDirty + FlushIfDirty —
                // no "Save" button needed. A small footer hint reinforces this.
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.TextColored(UITheme.AccentGreen, "\u2713 Changes auto-saved");

                // Surface the category letter shortcuts (see HandleCategoryShortcuts).
                string hint = "Letter key: jump tab";
                float hintW = ImGui.CalcTextSize(hint).X;
                float hintX = ImGui.GetWindowWidth() - hintW - ImGui.GetStyle().WindowPadding.X;
                if (hintX > ImGui.GetCursorPosX())
                {
                    ImGui.SameLine(hintX);
                    ImGui.TextDisabled(hint);
                }
            }
        }

        /// <summary>
        /// Cross-tab search box. Filters the preset toggle registry (the bulk of the
        /// radar's on/off settings) plus category names, so "where was that toggle?"
        /// is one keystroke instead of a tab hunt.
        /// </summary>
        private static void DrawSearchBar()
        {
            float clearW = 60f * Config.UIScale;
            ImGui.SetNextItemWidth(string.IsNullOrEmpty(_search)
                ? -1f
                : ImGui.GetContentRegionAvail().X - clearW - ImGui.GetStyle().ItemSpacing.X);
            ImGui.InputTextWithHint("##settings-search", "Search settings\u2026", ref _search, 96);

            if (!string.IsNullOrEmpty(_search))
            {
                ImGui.SameLine();
                if (ImGui.Button("Clear", new Vector2(clearW, 0)))
                    _search = string.Empty;
            }
            ImGui.Spacing();
        }

        private static void DrawSearchResults()
        {
            // Same footer reservation as the category shell.
            float footerH = ImGui.GetFrameHeightWithSpacing() + 8f;
            float contentH = ImGui.GetContentRegionAvail().Y - footerH;
            if (contentH < 100f) contentH = 100f;

            if (ImGui.BeginChild("##settings-search-results", new Vector2(0, contentH), ImGuiChildFlags.Borders))
            {
                string q = _search.Trim();
                int hits = 0;

                // Matching categories \u2014 quick jump buttons.
                bool anyCategory = false;
                for (int i = 0; i < _categories.Length; i++)
                {
                    var (_, label, _) = _categories[i];
                    if (!label.Contains(q, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!anyCategory)
                    {
                        UIControls.Section("Tabs");
                        anyCategory = true;
                    }
                    if (ImGui.Button($"Open: {label}##searchcat{i}"))
                    {
                        _activeCategory = i;
                        _search = string.Empty;
                    }
                    hits++;
                }

                // Matching toggles \u2014 live, flippable right here.
                string? lastGroup = null;
                foreach (var t in Presets.PresetManager.Toggles)
                {
                    if (!t.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        && !t.Group.Contains(q, StringComparison.OrdinalIgnoreCase)
                        && !t.Tooltip.Contains(q, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (t.Group != lastGroup)
                    {
                        UIControls.Section(t.Group);
                        lastGroup = t.Group;
                    }

                    bool v = t.GetCfg(Config);
                    if (UIControls.ToggleRow($"{t.Label}  ({t.Group})", ref v, t.Tooltip))
                    {
                        t.SetCfg(Config, v);
                        Config.MarkDirty();
                    }
                    hits++;
                }

                // Matching integer settings (FPS caps etc.).
                lastGroup = null;
                foreach (var s in Presets.PresetManager.Values)
                {
                    if (!s.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        && !s.Group.Contains(q, StringComparison.OrdinalIgnoreCase)
                        && !s.Tooltip.Contains(q, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (s.Group != lastGroup)
                    {
                        UIControls.Section(s.Group);
                        lastGroup = s.Group;
                    }

                    int v = s.GetCfg(Config);
                    if (UIControls.Stepper($"{s.Label}  ({s.Group})", ref v, s.Min, s.Max, tooltip: s.Tooltip))
                    {
                        s.SetCfg(Config, v);
                        Config.MarkDirty();
                    }
                    hits++;
                }

                if (hits == 0)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(UITheme.Grey, $"No matches for \"{q}\".");
                }

                ImGui.Spacing();
                ImGui.TextDisabled("Searches radar toggles and tab names. Sliders, colors and lists live in their tabs.");
            }
            ImGui.EndChild();
        }

        private static void DrawCategoryShell()
        {
            float scale = Config.UIScale;
            float navW = 160f * scale;
            float rowH = 36f * scale;

            // Footer (separator + "auto-saved") needs room — reserve it.
            float footerH = ImGui.GetFrameHeightWithSpacing() + 8f;
            float contentH = ImGui.GetContentRegionAvail().Y - footerH;
            if (contentH < 100f) contentH = 100f;

            // Left nav child
            if (ImGui.BeginChild("##settings-nav", new Vector2(navW, contentH), ImGuiChildFlags.Borders))
            {
                for (int i = 0; i < _categories.Length; i++)
                {
                    var (glyph, label, _) = _categories[i];
                    bool active = i == _activeCategory;

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

                    ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0f, 0.5f));
                    if (ImGui.Button($"  {glyph}   {label}##cat{i}", new Vector2(-1, rowH)))
                        _activeCategory = i;
                    ImGui.PopStyleVar();

                    // The glyph doubles as a keyboard shortcut while Settings is open —
                    // not guessable when it doesn't match the label (R = Presets), so say it.
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Shortcut: press {glyph}");

                    ImGui.PopStyleColor(3);
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            // Content pane
            if (ImGui.BeginChild("##settings-content", new Vector2(0, contentH), ImGuiChildFlags.Borders))
            {
                int idx = _activeCategory;
                if (idx < 0 || idx >= _categories.Length) idx = 0;
                _categories[idx].Draw();
            }
            ImGui.EndChild();
        }
    }
}
