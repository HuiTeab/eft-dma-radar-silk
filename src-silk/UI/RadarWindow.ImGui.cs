// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.UI.Panels;
using eft_dma_radar.Silk.UI.Shell;
using eft_dma_radar.Silk.UI.Widgets;
using ImGuiNET;
using Silk.NET.Maths;

namespace eft_dma_radar.Silk.UI
{
    internal static partial class RadarWindow
    {
        private static void DrawImGuiUI(ref Vector2D<int> fbSize, double delta)
        {
            _imgui.Update((float)delta);

            try
            {
                HandleLocalShortcuts();
                DrawMainMenuBar();
                DrawStatusBar();
                Sidebar.Draw();
                DrawWindows();
            }
            finally
            {
                _imgui.Render();
            }
        }

        /// <summary>
        /// Ticks down the "Config saved" notification timer.
        /// </summary>
        private static float _saveNotifyTimer;

        /// <summary>
        /// Shows a brief "Saved!" indicator in the status bar after config save.
        /// </summary>
        internal static void NotifyConfigSaved()
        {
            _saveNotifyTimer = 2.0f;
            ToastManager.Success("\u2713 Config saved", 1.5f);
        }

        /// <summary>
        /// Local (radar-PC) keyboard shortcuts driven by ImGui input. Unlike
        /// <see cref="Misc.Input.InputManager"/> (which polls the target game's
        /// kernel via DMA), these fire on the operator's machine whenever the
        /// radar window has OS focus — independent of which sub-window was
        /// last clicked. We bail out while a text input is active so typing
        /// in the command palette / search fields doesn't trigger them.
        /// </summary>
        private static void HandleLocalShortcuts()
        {
            var io = ImGui.GetIO();

            // Suppress letter/number shortcuts while typing in a search/text field.
            if (io.WantTextInput)
                return;

            // Don't hijack modified key combos (Ctrl+S "save", Alt-menu, etc.).
            // Note: we intentionally do NOT check io.WantCaptureKeyboard — that's
            // true whenever any panel has focus and would re-introduce the
            // "only works when the radar background is clicked" bug.
            if (io.KeyCtrl || io.KeyAlt || io.KeySuper)
                return;

            // Settings owns its category-letter shortcuts (G/P/E/M/Q/K/W) so
            // they switch tabs instead of toggling global panels.
            if (SettingsPanel.HandleCategoryShortcuts())
                return;

            // Sidebar slots — work on the radar PC regardless of DMA input state.
            if (ImGui.IsKeyPressed(ImGuiKey._1, false)) Sidebar.Toggle(0);
            else if (ImGui.IsKeyPressed(ImGuiKey._2, false)) Sidebar.Toggle(1);
            else if (ImGui.IsKeyPressed(ImGuiKey._3, false)) Sidebar.Toggle(2);
            else if (ImGui.IsKeyPressed(ImGuiKey._4, false)) Sidebar.Toggle(3);
            else if (ImGui.IsKeyPressed(ImGuiKey._5, false)) Sidebar.Toggle(4);

            // Menu-letter shortcuts (the hints shown in the Windows menu).
            if (ImGui.IsKeyPressed(ImGuiKey.S, false)) SettingsPanel.IsOpen = !SettingsPanel.IsOpen;
            else if (ImGui.IsKeyPressed(ImGuiKey.L, false)) LootFiltersPanel.IsOpen = !LootFiltersPanel.IsOpen;
            else if (ImGui.IsKeyPressed(ImGuiKey.H, false)) HideoutPanel.IsOpen = !HideoutPanel.IsOpen;
            else if (ImGui.IsKeyPressed(ImGuiKey.Q, false)) QuestPanel.IsOpen = !QuestPanel.IsOpen;
            else if (ImGui.IsKeyPressed(ImGuiKey.P, false)) PlayerInfoWidget.IsOpen = !PlayerInfoWidget.IsOpen;
            else if (ImGui.IsKeyPressed(ImGuiKey.T, false)) LootWidget.IsOpen = !LootWidget.IsOpen;
            else if (ImGui.IsKeyPressed(ImGuiKey.A, false)) AimviewWidget.IsOpen = !AimviewWidget.IsOpen;
            else if (ImGui.IsKeyPressed(ImGuiKey.E, false)) EspWindow.Toggle();

            // Tab hides/shows the sidebar.
            if (ImGui.IsKeyPressed(ImGuiKey.Tab, false))
                Sidebar.ToggleVisibility();
        }

        // ── Top command-bar pill colors (matches bottom status chip language) ─
        private static readonly Vector4 PillIdleBg     = new(0.16f, 0.17f, 0.20f, 1.0f);
        private static readonly Vector4 PillIdleHover  = new(0.22f, 0.24f, 0.28f, 1.0f);
        private static readonly Vector4 PillIdleActive = new(0.26f, 0.28f, 0.32f, 1.0f);
        private static readonly Vector4 PillOnBg       = new(0.20f, 0.55f, 0.55f, 1.0f);
        private static readonly Vector4 PillOnHover    = new(0.28f, 0.65f, 0.65f, 1.0f);
        private static readonly Vector4 PillOnActive   = new(0.18f, 0.48f, 0.48f, 1.0f);
        private static readonly Vector4 PillDividerCol = new(0.30f, 0.32f, 0.36f, 1.0f);

        /// <summary>
        /// Pill-style button for the top command bar. Active pills use the cyan
        /// accent; inactive pills use a subtle dark background. Mirrors the chip
        /// language of the bottom status bar so the two bars feel like one system.
        /// </summary>
        private static bool TopBarPill(string label, bool isActive, string? tooltip)
        {
            ImGui.PushStyleColor(ImGuiCol.Button,        isActive ? PillOnBg     : PillIdleBg);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isActive ? PillOnHover  : PillIdleHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  isActive ? PillOnActive : PillIdleActive);
            bool clicked = ImGui.Button(label);
            ImGui.PopStyleColor(3);

            if (tooltip is not null && ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
            return clicked;
        }

        /// <summary>Vertical hairline between top-bar sections.</summary>
        private static void TopBarDivider()
        {
            ImGui.PushStyleColor(ImGuiCol.Text, PillDividerCol);
            ImGui.TextUnformatted("│"); // box drawings light vertical
            ImGui.PopStyleColor();
        }

        /// <summary>
        /// Follow / Free mode pill. Free mode (the non-default state) is the
        /// "active" tint so the user always knows when the camera is detached.
        /// </summary>
        private static void DrawFollowFreePill()
        {
            string label = _freeMode ? "○ Free" : "◉ Follow";
            string desc  = _freeMode
                ? "Free pan — drag to move"
                : "Camera follows your player";
            string tooltip = HotkeyManager.WithHint(desc, "FreeMode");

            if (TopBarPill(label, _freeMode, tooltip))
            {
                _freeMode = !_freeMode;
                if (!_freeMode)
                    _mapPanPosition = Vector2.Zero;
            }
        }

        /// <summary>
        /// Overflow popup with rarely-used layer toggles, player-display toggles,
        /// and secondary panels. The sidebar already covers the primary panels
        /// (Players, Loot, Aimview, Quests, Settings, ESP) so those are omitted.
        /// </summary>
        private static void DrawMorePopup()
        {
            if (TopBarPill("⋯ More", false, "Other layers, panels, and overlays"))
                ImGui.OpenPopup("##TopBarMore");

            if (!ImGui.BeginPopup("##TopBarMore"))
                return;

            ImGui.TextDisabled("Radar Layers");
            bool showDoors = Config.ShowDoors;
            if (ImGui.MenuItem("□ Doors", HotkeyManager.GetBindingDisplay("ToggleDoors"), showDoors))
            { Config.ShowDoors = !Config.ShowDoors; Config.MarkDirty(); }

            bool showAirdrops = Config.ShowAirdrops;
            if (ImGui.MenuItem("✈ Airdrops", null, showAirdrops))
            { Config.ShowAirdrops = !Config.ShowAirdrops; Config.MarkDirty(); }

            bool showSwitches = Config.ShowSwitches;
            if (ImGui.MenuItem("⚡ Switches", null, showSwitches))
            { Config.ShowSwitches = !Config.ShowSwitches; Config.MarkDirty(); }

            bool showMarkers = Config.ShowMapMarkers;
            if (ImGui.MenuItem("◆ Map Markers", null, showMarkers))
            { Config.ShowMapMarkers = !Config.ShowMapMarkers; Config.MarkDirty(); }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show user-placed markers. Right-click the map to add one.");

            ImGui.Separator();
            ImGui.TextDisabled("Player Display");

            bool connectGroups = Config.ConnectGroups;
            if (ImGui.MenuItem("─ Connect Groups", HotkeyManager.GetBindingDisplay("ConnectGroups"), connectGroups))
            { Config.ConnectGroups = !Config.ConnectGroups; Config.MarkDirty(); }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw lines between squad members");

            bool highAlert = Config.HighAlert;
            if (ImGui.MenuItem("⚠ High Alert", null, highAlert))
            { Config.HighAlert = !Config.HighAlert; Config.MarkDirty(); }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Extend aimline when an enemy is looking at you");

            ImGui.Separator();
            ImGui.TextDisabled("Panels");

            if (ImGui.MenuItem("▣ Loot Filters", "L", LootFiltersPanel.IsOpen))
                LootFiltersPanel.IsOpen = !LootFiltersPanel.IsOpen;
            if (ImGui.MenuItem("⌨ Hotkeys", null, HotkeyManagerPanel.IsOpen))
                HotkeyManagerPanel.IsOpen = !HotkeyManagerPanel.IsOpen;
            if (ImGui.MenuItem("⌂ Hideout", "H", HideoutPanel.IsOpen))
                HideoutPanel.IsOpen = !HideoutPanel.IsOpen;
            if (ImGui.MenuItem("❁ Quest Planner", null, QuestPlannerPanel.IsOpen))
                QuestPlannerPanel.IsOpen = !QuestPlannerPanel.IsOpen;
            if (ImGui.MenuItem("☰ Player History", null, PlayerHistoryPanel.IsOpen))
                PlayerHistoryPanel.IsOpen = !PlayerHistoryPanel.IsOpen;
            if (ImGui.MenuItem("⌕ Watchlist", null, PlayerWatchlistPanel.IsOpen))
                PlayerWatchlistPanel.IsOpen = !PlayerWatchlistPanel.IsOpen;
            if (ImGui.MenuItem("⊞ Map Generator", null,
                eft_dma_radar.Silk.Tarkov.Unity.PhysX.MapGenWindow.IsVisible))
                eft_dma_radar.Silk.Tarkov.Unity.PhysX.MapGenWindow.Toggle();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Live preview + tuning for the top-down map export (PhysX snapshot).");

            ImGui.Separator();

            if (ImGui.MenuItem("Show Welcome Tour"))
                FirstRunTour.Open();

            if (ImGui.MenuItem("Close All", "Esc"))
            {
                SettingsPanel.IsOpen = false;
                LootFiltersPanel.IsOpen = false;
                HotkeyManagerPanel.IsOpen = false;
                HideoutPanel.IsOpen = false;
                QuestPanel.IsOpen = false;
                QuestPlannerPanel.IsOpen = false;
                KillfeedPanel.IsOpen = false;
                PlayerHistoryPanel.IsOpen = false;
                PlayerWatchlistPanel.IsOpen = false;
                PlayerInfoWidget.IsOpen = false;
                LootWidget.IsOpen = false;
                AimviewWidget.IsOpen = false;
            }

            ImGui.EndPopup();
        }

        private static void DrawMainMenuBar()
        {
            // Demote to "Custom" if the user has drifted from the active built-in preset.
            PresetManager.ReconcileDrift(Config);

            if (!ImGui.BeginMainMenuBar())
                return;

            // Chunky pill rounding for the modern command-bar look.
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);

            // ── Mode toggle (Follow / Free) ─────────────────────────────────
            DrawFollowFreePill();
            ImGui.SameLine(0, 6);

            // ── Battle Mode quick toggle ────────────────────────────────────
            {
                string battleDesc = Config.BattleMode
                    ? "Battle Mode ON — hide loot / corpses / doors"
                    : "Battle Mode OFF — click for player-only view";
                if (TopBarPill("⚔ Battle", Config.BattleMode,
                    HotkeyManager.WithHint(battleDesc, "BattleMode")))
                {
                    Config.SetBattleMode(!Config.BattleMode);
                }
            }
            ImGui.SameLine(0, 6);

            // ── Preset selector ─────────────────────────────────────────────
            DrawPresetSelector();
            ImGui.SameLine(0, 10);
            TopBarDivider();
            ImGui.SameLine(0, 10);

            // ── Quick radar-layer pills ─────────────────────────────────────
            // Aimlines has no dedicated hotkey action — tooltip stays bare.
            if (TopBarPill("→ Aim", Config.ShowAimlines, "Aimlines on/off"))
            { Config.ShowAimlines = !Config.ShowAimlines; Config.MarkDirty(); }
            ImGui.SameLine(0, 4);

            if (TopBarPill("◆ Loot", Config.ShowLoot,
                HotkeyManager.WithHint("Loot markers on/off", "ToggleLoot")))
            { Config.ShowLoot = !Config.ShowLoot; Config.MarkDirty(); }
            ImGui.SameLine(0, 4);

            if (TopBarPill("▲ Exfils", Config.ShowExfils,
                HotkeyManager.WithHint("Exfil points on/off", "ToggleExfils")))
            { Config.ShowExfils = !Config.ShowExfils; Config.MarkDirty(); }
            ImGui.SameLine(0, 10);
            TopBarDivider();
            ImGui.SameLine(0, 10);

            // ── Restart radar (icon only) ───────────────────────────────────
            {
                bool canRestart = Memory.InRaid || Memory.InHideout;
                if (!canRestart) ImGui.BeginDisabled();
                if (TopBarPill("↻", false, null))
                    Memory.RestartRadar = true;
                if (!canRestart) ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(canRestart
                        ? "Restart the radar (re-detect game world, players, loot)"
                        : "Only available during a raid or in the hideout");
            }
            ImGui.SameLine(0, 4);

            // ── More overflow popup ─────────────────────────────────────────
            DrawMorePopup();

            ImGui.PopStyleVar();
            ImGui.EndMainMenuBar();
        }

        /// <summary>
        /// Draws the preset selector in the main menu bar. AnyDesk users can
        /// switch between bundled radar configs without diving into menus.
        /// Cycle with the "Previous Preset" / "Next Preset" hotkeys.
        /// </summary>
        private static void DrawPresetSelector()
        {
            ImGui.TextColored(ColorMenuBarRight, "Preset");
            ImGui.SameLine(0, 6);

            ImGui.PushStyleColor(ImGuiCol.FrameBg,        PillIdleBg);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, PillIdleHover);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  PillIdleActive);

            ImGui.SetNextItemWidth(120f * UIScale);
            string current = PresetManager.DisplayNameFor(Config, Config.ActivePresetId);
            if (ImGui.BeginCombo("##PresetSelector", current))
            {
                foreach (var id in PresetManager.AllIds(Config))
                {
                    bool selected = id == Config.ActivePresetId;
                    if (ImGui.Selectable(PresetManager.DisplayNameFor(Config, id), selected))
                        PresetManager.Apply(id, Config);
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Switch radar preset.\nUse the 'Previous/Next Preset' hotkeys to cycle.");
        }

        private static void DrawStatusBar()
        {
            if (!InRaid && !Memory.InHideout)
            {
                Sidebar.StatusBarHeight = 0f;
                return;
            }

            var viewport = ImGui.GetMainViewport();
            float chipHeight = 44f * UIScale;
            float padX = 10f * UIScale;
            float padY = 6f * UIScale;
            float barHeight = chipHeight + padY * 2f;

            // Allow user to collapse the status bar to reclaim vertical space.
            if (!Config.ShowStatusBar)
            {
                DrawStatusBarHandle(viewport);
                return;
            }

            // Publish height so the sidebar can size around the real status bar.
            Sidebar.StatusBarHeight = barHeight;

            // Span the entire viewport width so the bottom-left corner under the sidebar
            // is also painted (no black hole). Indent chip content past the sidebar instead.
            float leftInset = Sidebar.ReservedWidth;
            ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X, viewport.Pos.Y + viewport.Size.Y - barHeight));
            ImGui.SetNextWindowSize(new Vector2(viewport.Size.X, barHeight));

            var flags = ImGuiWindowFlags.NoDecoration |
                        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse |
                        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
                        ImGuiWindowFlags.NoFocusOnAppearing;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padX, padY));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * UIScale, 0));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, ColorStatusBarBg);

            if (ImGui.Begin("##StatusBar", flags))
            {
                // Indent first chip past the sidebar.
                if (leftInset > 0f)
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + leftInset);

                if (Memory.InHideout)
                {
                    DrawChip("STATUS", "In Hideout", ColorChipAccent, ColorHideoutDot);

                    var hideout = Memory.Hideout;
                    if (hideout.Items.Count > 0)
                    {
                        int itemCount = hideout.Items.Count;
                        long totalValue = hideout.TotalBestValue;
                        if (itemCount != _cachedHideoutItemCount || totalValue != _cachedHideoutTotalValue)
                        {
                            _cachedHideoutItemCount = itemCount;
                            _cachedHideoutTotalValue = totalValue;
                            _cachedHideoutStashText = $"{itemCount} \u00b7 \u20bd{totalValue:N0}";
                        }
                        ImGui.SameLine();
                        DrawChip("STASH", _cachedHideoutStashText, ColorChipValue);
                    }
                }
                else
                {
                    // Raid: gather player counts — players are the radar's primary signal, so
                    // break them down by team for at-a-glance readability over AnyDesk / TV.
                    var allPlayers = AllPlayers;
                    int playerCount = 0;
                    int pmcCount = 0;
                    int teammateCount = 0;
                    int scavCount = 0;
                    int aiCount = 0;
                    if (allPlayers is not null)
                    {
                        foreach (var p in allPlayers)
                        {
                            if (!p.IsEspVisible)
                                continue;
                            playerCount++;
                            switch (p.Type)
                            {
                                case PlayerType.Teammate:
                                    teammateCount++;
                                    break;
                                case PlayerType.USEC:
                                case PlayerType.BEAR:
                                case PlayerType.Streamer:
                                    pmcCount++;
                                    break;
                                case PlayerType.PScav:
                                    scavCount++;
                                    break;
                                case PlayerType.AIScav:
                                case PlayerType.AIRaider:
                                case PlayerType.AIBoss:
                                    aiCount++;
                                    break;
                            }
                        }
                    }

                    if (playerCount != _cachedStatusPlayerCount
                        || pmcCount != _cachedStatusPmcCount
                        || teammateCount != _cachedStatusTeammateCount
                        || aiCount != _cachedStatusAiCount
                        || scavCount != _cachedStatusScavCount)
                    {
                        _cachedStatusPlayerCount = playerCount;
                        _cachedStatusPmcCount = pmcCount;
                        _cachedStatusTeammateCount = teammateCount;
                        _cachedStatusAiCount = aiCount;
                        _cachedStatusScavCount = scavCount;

                        // Compact breakdown: "{total}  ·  {T}T {P}P {S}S {AI}AI" — skip 0 segments.
                        var sb = new StringBuilder(48);
                        sb.Append(playerCount);
                        if (teammateCount + pmcCount + scavCount + aiCount > 0)
                        {
                            sb.Append("  · ");
                            bool anySegment = false;
                            if (teammateCount > 0) { sb.Append(teammateCount).Append('T'); anySegment = true; }
                            if (pmcCount > 0)      { if (anySegment) sb.Append(' '); sb.Append(pmcCount).Append('P'); anySegment = true; }
                            if (scavCount > 0)     { if (anySegment) sb.Append(' '); sb.Append(scavCount).Append('S'); anySegment = true; }
                            if (aiCount > 0)       { if (anySegment) sb.Append(' '); sb.Append(aiCount).Append("AI"); }
                        }
                        _cachedStatusPlayersText = sb.ToString();
                    }

                    DrawChip("STATUS", "In Raid", ColorChipAccent, ColorRaidDot);

                    ImGui.SameLine();
                    DrawChip("PLAYERS", _cachedStatusPlayersText, ColorChipValue);

                    // Energy / Hydration
                    if (Memory.LocalPlayer is LocalPlayer lp && lp.HealthReady)
                    {
                        int energy = (int)lp.Energy;
                        int hydration = (int)lp.Hydration;

                        if (energy != _cachedEnergy || hydration != _cachedHydration)
                        {
                            _cachedEnergy = energy;
                            _cachedHydration = hydration;
                            _cachedEnergyHydrationText = $"E {energy}  \u00b7  H {hydration}";
                        }

                        int minVal = Math.Min(energy, hydration);
                        var ehColor = minVal > 30 ? ColorEnergyHydrationOk
                            : minVal > 10 ? ColorChipWarn
                            : ColorChipCrit;

                        ImGui.SameLine();
                        DrawChip("VITALS", _cachedEnergyHydrationText, ehColor);
                    }
                }

                // ── Right cluster: FPS / DMA / Map ─────────────────────────
                string mapName = Memory.InHideout ? "Hideout" : MapManager.Map?.Config?.Name ?? "No Map";
                string fpsText = $"{_fps}";

                int   rtFps    = DMA.DmaStats.RealtimeFps;
                float mbpsCur  = DMA.DmaStats.ReadMBpsCurrent;
                float mbpsMax  = DMA.DmaStats.MaxThroughputMBps;
                string dmaText = mbpsMax > 0f
                    ? $"{mbpsCur:F0} MB/s  \u00b7  {rtFps} RT"
                    : $"{mbpsCur:F0} MB/s  \u00b7  {rtFps} RT";

                float fpsChipW = MeasureChipWidth("FPS", fpsText, padX);
                float dmaChipW = MeasureChipWidth("DMA", dmaText, padX);
                float mapChipW = MeasureChipWidth("MAP", mapName, padX);
                float spacing = ImGui.GetStyle().ItemSpacing.X;
                float chevronW = 18f * UIScale;
                float rightCluster = fpsChipW + dmaChipW + mapChipW + chevronW + spacing * 3f;

                // Save notification (left of the right cluster, transient)
                float saveExtra = 0f;
                if (_saveNotifyTimer > 0f)
                    saveExtra = MeasureChipWidth("STATUS", "\u2713 Config saved", padX) + spacing;

                float winW = ImGui.GetWindowWidth();
                float rightStartX = winW - padX - rightCluster - saveExtra;
                ImGui.SameLine(rightStartX);

                if (_saveNotifyTimer > 0f)
                {
                    _saveNotifyTimer -= ImGui.GetIO().DeltaTime;
                    float alpha = Math.Clamp(_saveNotifyTimer, 0f, 1f);
                    var notifyValue = ColorSaveNotify with { W = alpha };
                    DrawChip("STATUS", "\u2713 Config saved", notifyValue);
                    ImGui.SameLine();
                }

                DrawChip("FPS", fpsText, _fps < 30 ? ColorChipWarn : ColorChipValue);
                ImGui.SameLine();
                DrawChip("DMA", dmaText, ColorDmaStats);
                if (ImGui.IsItemHovered())
                    DrawDmaStatsTooltip();
                ImGui.SameLine();
                DrawChip("MAP", mapName, ColorChipAccent);

                // Collapse chevron — small, right edge, hides the status bar.
                ImGui.SameLine();
                if (ImGui.Button("v##statusbar_collapse", new Vector2(chevronW, chipHeight)))
                {
                    Config.ShowStatusBar = false;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide status bar");
            }

            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }

        /// <summary>
        /// Small "^" handle on the bottom-right edge to bring the status bar back when collapsed.
        /// </summary>
        private static void DrawStatusBarHandle(ImGuiViewportPtr viewport)
        {
            float handleH = 14f * UIScale;
            float handleW = 60f * UIScale;
            Sidebar.StatusBarHeight = handleH;

            ImGui.SetNextWindowPos(new Vector2(
                viewport.Pos.X + viewport.Size.X - handleW - 8f,
                viewport.Pos.Y + viewport.Size.Y - handleH));
            ImGui.SetNextWindowSize(new Vector2(handleW, handleH));

            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
                        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoScrollbar |
                        ImGuiWindowFlags.NoScrollWithMouse;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, ColorStatusBarBg with { W = 0.55f });
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 1, 1, 0.10f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1, 1, 1, 0.18f));

            if (ImGui.Begin("##StatusBarHandle", flags))
            {
                if (ImGui.Button("^##statusbar_show", new Vector2(handleW, handleH)))
                {
                    Config.ShowStatusBar = true;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show status bar");
            }
            ImGui.End();
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(2);
        }
        private static float MeasureChipWidth(string label, string value, float padX)
        {
            float labelW = ImGui.CalcTextSize(label).X;
            float valueW = ImGui.CalcTextSize(value).X;
            return Math.Max(labelW, valueW) + padX * 2f;
        }

        /// <summary>
        /// Draws a single status-bar "chip": small bordered box with a dim label
        /// on top and a larger, colored value below. Big-target friendly for
        /// AnyDesk / TV viewing.
        /// </summary>
        private static void DrawChip(string label, string value, Vector4 valueColor, Vector4? dotColor = null)
        {
            float padX = 10f * UIScale;
            float padY = 4f * UIScale;
            float labelW = ImGui.CalcTextSize(label).X;
            float valueW = ImGui.CalcTextSize(value).X;
            float dotW = dotColor.HasValue ? ImGui.CalcTextSize("\u25cf ").X : 0f;
            float chipW = Math.Max(labelW, valueW + dotW) + padX * 2f;
            float chipH = 44f * UIScale;

            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var rectMin = pos;
            var rectMax = new Vector2(pos.X + chipW, pos.Y + chipH);

            uint bg = ImGui.GetColorU32(ColorChipBg);
            uint border = ImGui.GetColorU32(ColorChipBorder);
            float rounding = 4f * UIScale;
            drawList.AddRectFilled(rectMin, rectMax, bg, rounding);
            drawList.AddRect(rectMin, rectMax, border, rounding);

            // Label (top)
            var labelPos = new Vector2(pos.X + padX, pos.Y + padY);
            drawList.AddText(labelPos, ImGui.GetColorU32(ColorChipLabel), label);

            // Value (bottom), with optional leading dot
            float lineH = ImGui.GetTextLineHeight();
            var valuePos = new Vector2(pos.X + padX, pos.Y + chipH - padY - lineH);
            if (dotColor.HasValue)
            {
                drawList.AddText(valuePos, ImGui.GetColorU32(dotColor.Value), "\u25cf");
                valuePos.X += dotW;
            }
            drawList.AddText(valuePos, ImGui.GetColorU32(valueColor), value);

            // Reserve the layout space so SameLine() works.
            ImGui.Dummy(new Vector2(chipW, chipH));
        }

        /// <summary>
        /// Hover breakdown for the DMA chip. Surfaces the per-second bus counters, including
        /// the uncached-direct-read share that now contributes to the MB/s figure.
        /// </summary>
        private static void DrawDmaStatsTooltip()
        {
            float mbpsCur  = DMA.DmaStats.ReadMBpsCurrent;
            float mbpsPeak = DMA.DmaStats.ReadMBpsPeak;
            float mbpsMax  = DMA.DmaStats.MaxThroughputMBps;
            long  executes = DMA.DmaStats.ExecutesPerSecond;
            long  direct   = DMA.DmaStats.DirectReadsPerSecond;
            long  uncached = DMA.DmaStats.UncachedDirectReadsPerSecond;
            long  cached   = Math.Max(0, direct - uncached);
            long  prepared = DMA.DmaStats.PreparedPerSecond;

            ImGui.BeginTooltip();
            ImGui.TextColored(ColorChipAccent, "DMA Throughput");
            ImGui.Separator();

            ImGui.TextUnformatted($"Current:  {mbpsCur:F0} MB/s");
            ImGui.TextUnformatted($"Peak:     {mbpsPeak:F0} MB/s");
            if (mbpsMax > 0f)
                ImGui.TextUnformatted($"HW max:   {mbpsMax:F0} MB/s");

            ImGui.Spacing();
            ImGui.TextColored(ColorChipAccent, "Bus trips / sec");
            ImGui.Separator();
            ImGui.TextUnformatted($"Scatter executes:  {executes:N0}  ({prepared:N0} reads batched)");
            ImGui.TextUnformatted($"Direct reads:      {direct:N0}");

            // Uncached direct reads always hit the bus and ARE counted in MB/s above;
            // cached ones may be served from VMM's page cache and are excluded.
            ImGui.Indent(12);
            ImGui.TextColored(ColorDmaStats, $"• uncached: {uncached:N0}  (in MB/s)");
            ImGui.TextColored(ColorChipLabel, $"• cached:   {cached:N0}  (cache-served, excluded)");
            ImGui.Unindent(12);

            ImGui.EndTooltip();
        }

        private static void DrawWindows()
        {
            HotkeyManagerPanel.ProcessCapture();

            // Map marker create/edit popup (latches a deferred right-click open request).
            DrawMarkerEditorPopup();

            if (SettingsPanel.IsOpen)
                SettingsPanel.Draw();

            if (LootFiltersPanel.IsOpen)
                LootFiltersPanel.Draw();

            if (HotkeyManagerPanel.IsOpen)
                HotkeyManagerPanel.Draw();

            if (HideoutPanel.IsOpen)
                HideoutPanel.Draw();

            // Vischeck windows — own visibility state (toggled via hotkey),
            // unconditionally drawable so they don't depend on Sidebar UI.
            eft_dma_radar.Silk.Tarkov.Unity.PhysX.VisCheckDebugWindow.Draw();
            eft_dma_radar.Silk.Tarkov.Unity.PhysX.CacheViewWindow.Draw();
            eft_dma_radar.Silk.Tarkov.Unity.PhysX.MapGenWindow.Draw();

            // Right-dock layout (Players / Loot / Quests) — must be initialized
            // BEFORE those panels' Draw() is called so SetNextWindowPos/Size land.
            RightDock.BeginFrame();

            if (QuestPanel.IsOpen)
            {
                RightDock.PlaceNext();
                QuestPanel.Draw();
            }

            if (QuestPlannerPanel.IsOpen)
                QuestPlannerPanel.Draw();

            if (KillfeedPanel.IsOpen)
                KillfeedPanel.Draw();

            if (PlayerHistoryPanel.IsOpen)
                PlayerHistoryPanel.Draw();

            if (PlayerWatchlistPanel.IsOpen)
                PlayerWatchlistPanel.Draw();

            if (PlayerInfoWidget.IsOpen && InRaid)
            {
                RightDock.PlaceNext();
                PlayerInfoWidget.Draw();
            }

            if (LootWidget.IsOpen && InRaid)
            {
                RightDock.PlaceNext();
                LootWidget.Draw();
            }

            if (AimviewWidget.IsOpen && InRaid && Config.ShowAimview)
                AimviewWidget.Draw();

            // Clears the one-shot resnap flag set by the "Reset Side Panels" hotkey.
            RightDock.EndFrame();

            // Toasts on top of everything else, but below the radial overlay.
            ToastManager.Draw();

            // First-run welcome tour — auto-opens once per install. Drawn last
            // so its backdrop dims everything else.
            FirstRunTour.Draw();
        }

        private static void ApplyImGuiDarkStyle()
        {
            var style = ImGui.GetStyle();
            // Radii — all derived from UITheme so the desktop UI has one
            // single radius family (small for inner controls, medium for
            // frames/pills, large for windows/popups).
            style.WindowRounding    = UITheme.RadiusLarge;
            style.FrameRounding     = UITheme.RadiusMedium;
            style.GrabRounding      = UITheme.RadiusSmall;
            style.ScrollbarRounding = UITheme.RadiusLarge;
            style.TabRounding       = UITheme.RadiusSmall;
            style.PopupRounding     = UITheme.RadiusMedium;
            style.ChildRounding     = UITheme.RadiusSmall;

            // Borders — one consistent default weight across windows and popups.
            style.WindowBorderSize = UITheme.BorderDefault;
            style.FrameBorderSize  = 0.0f;
            style.PopupBorderSize  = UITheme.BorderDefault;

            style.WindowPadding = new Vector2(10, 10);
            style.FramePadding = new Vector2(6, 4);
            style.ItemSpacing = new Vector2(8, 5);
            style.ItemInnerSpacing = new Vector2(6, 4);
            style.IndentSpacing = 20f;
            style.ScrollbarSize = 12f;
            style.GrabMinSize = 10f;
            style.SeparatorTextBorderSize = 2f;

            // ── Accent palette ──────────────────────────────────────────────────
            // Subtle teal accent for interactive elements
            var accentBase   = new Vector4(0.22f, 0.55f, 0.55f, 1.0f);
            var accentHover  = new Vector4(0.28f, 0.65f, 0.65f, 1.0f);
            var accentActive = new Vector4(0.18f, 0.48f, 0.48f, 1.0f);

            var colors = style.Colors;

            // Window
            colors[(int)ImGuiCol.WindowBg]           = new Vector4(0.08f, 0.08f, 0.10f, 0.96f);
            colors[(int)ImGuiCol.ChildBg]            = new Vector4(0.08f, 0.08f, 0.10f, 0.0f);
            colors[(int)ImGuiCol.PopupBg]            = new Vector4(0.10f, 0.10f, 0.12f, 0.96f);

            // Borders
            colors[(int)ImGuiCol.Border]             = new Vector4(0.25f, 0.28f, 0.30f, 0.60f);
            colors[(int)ImGuiCol.BorderShadow]       = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

            // Title bar
            colors[(int)ImGuiCol.TitleBg]            = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);
            colors[(int)ImGuiCol.TitleBgActive]      = new Vector4(0.14f, 0.14f, 0.17f, 1.0f);
            colors[(int)ImGuiCol.TitleBgCollapsed]    = new Vector4(0.08f, 0.08f, 0.10f, 0.75f);

            // Menu bar
            colors[(int)ImGuiCol.MenuBarBg]          = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);

            // Frame backgrounds
            colors[(int)ImGuiCol.FrameBg]            = new Vector4(0.14f, 0.15f, 0.17f, 1.0f);
            colors[(int)ImGuiCol.FrameBgHovered]     = new Vector4(0.20f, 0.22f, 0.24f, 1.0f);
            colors[(int)ImGuiCol.FrameBgActive]      = new Vector4(0.18f, 0.20f, 0.22f, 1.0f);

            // Buttons
            colors[(int)ImGuiCol.Button]             = new Vector4(0.18f, 0.19f, 0.22f, 1.0f);
            colors[(int)ImGuiCol.ButtonHovered]       = accentHover;
            colors[(int)ImGuiCol.ButtonActive]        = accentActive;

            // Headers (collapsing headers, selectable, etc.)
            colors[(int)ImGuiCol.Header]             = new Vector4(0.16f, 0.17f, 0.20f, 1.0f);
            colors[(int)ImGuiCol.HeaderHovered]       = new Vector4(0.22f, 0.24f, 0.28f, 1.0f);
            colors[(int)ImGuiCol.HeaderActive]        = new Vector4(0.20f, 0.22f, 0.26f, 1.0f);

            // Tabs
            colors[(int)ImGuiCol.Tab]                = new Vector4(0.12f, 0.13f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TabHovered]          = accentHover;
            colors[(int)ImGuiCol.TabSelected]         = accentBase;
            colors[(int)ImGuiCol.TabDimmed]           = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);
            colors[(int)ImGuiCol.TabDimmedSelected]   = new Vector4(0.14f, 0.14f, 0.17f, 1.0f);

            // Sliders & grabs
            colors[(int)ImGuiCol.SliderGrab]          = accentBase;
            colors[(int)ImGuiCol.SliderGrabActive]    = accentHover;

            // Checkboxes
            colors[(int)ImGuiCol.CheckMark]           = new Vector4(0.30f, 0.75f, 0.70f, 1.0f);

            // Scrollbar
            colors[(int)ImGuiCol.ScrollbarBg]        = new Vector4(0.06f, 0.06f, 0.08f, 0.6f);
            colors[(int)ImGuiCol.ScrollbarGrab]      = new Vector4(0.22f, 0.24f, 0.28f, 1.0f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.30f, 0.32f, 0.36f, 1.0f);
            colors[(int)ImGuiCol.ScrollbarGrabActive]  = accentBase;

            // Separators
            colors[(int)ImGuiCol.Separator]          = new Vector4(0.22f, 0.24f, 0.28f, 0.6f);
            colors[(int)ImGuiCol.SeparatorHovered]   = accentHover;
            colors[(int)ImGuiCol.SeparatorActive]    = accentActive;

            // Resize grip
            colors[(int)ImGuiCol.ResizeGrip]         = new Vector4(0.22f, 0.24f, 0.28f, 0.4f);
            colors[(int)ImGuiCol.ResizeGripHovered]  = accentHover;
            colors[(int)ImGuiCol.ResizeGripActive]   = accentActive;

            // Text
            colors[(int)ImGuiCol.Text]               = new Vector4(0.90f, 0.92f, 0.94f, 1.0f);
            colors[(int)ImGuiCol.TextDisabled]       = new Vector4(0.45f, 0.47f, 0.50f, 1.0f);

            // Table
            colors[(int)ImGuiCol.TableHeaderBg]      = new Vector4(0.12f, 0.13f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TableBorderStrong]  = new Vector4(0.22f, 0.24f, 0.28f, 0.8f);
            colors[(int)ImGuiCol.TableBorderLight]   = new Vector4(0.18f, 0.20f, 0.22f, 0.5f);
            colors[(int)ImGuiCol.TableRowBg]         = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            colors[(int)ImGuiCol.TableRowBgAlt]      = new Vector4(1.0f, 1.0f, 1.0f, 0.02f);

            // ── Focus / navigation ──────────────────────────────────────────────
            // Single accent (UITheme.FocusRing) drives the focus indicator so
            // keyboard / remote users see the same active-state
            // color used elsewhere in the UI.
            colors[(int)ImGuiCol.NavCursor]             = UITheme.FocusRing;
            colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(UITheme.FocusRing.X, UITheme.FocusRing.Y, UITheme.FocusRing.Z, 0.70f);
            colors[(int)ImGuiCol.NavWindowingDimBg]     = new Vector4(0.00f, 0.00f, 0.00f, 0.50f);
        }

        /// <summary>
        /// Loads the embedded NeoSansStd font into ImGui's font atlas.
        /// Must be called inside the onConfigureIO callback before the atlas is built.
        /// </summary>
        private static unsafe void LoadImGuiFont(ImGuiIOPtr io)
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("eft_dma_radar.Silk.NeoSansStdRegular.otf");
            if (stream is null)
            {
                Log.WriteLine("[RadarWindow] WARNING: Embedded font not found for ImGui, using default.");
                return;
            }

            var fontData = new byte[stream.Length];
            stream.ReadExactly(fontData);

            // Pin the managed array — must stay pinned for the lifetime of ImGui's font atlas
            _imguiFontHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);

            // Create config with FontDataOwnedByAtlas = false so ImGui won't try to free our pinned memory
            var config = ImGuiNative.ImFontConfig_ImFontConfig();
            config->FontDataOwnedByAtlas = 0;

            io.Fonts.AddFontFromMemoryTTF(
                _imguiFontHandle.AddrOfPinnedObject(),
                fontData.Length,
                13.0f,
                new ImFontConfigPtr(config),
                io.Fonts.GetGlyphRangesDefault());

            ImGuiNative.ImFontConfig_destroy(config);
            Log.WriteLine("[RadarWindow] Custom font loaded for ImGui (13px).");

            // Merge system symbol font for Unicode icon glyphs (geometric shapes, arrows, etc.)
            var symbolFontPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                "seguisym.ttf");

            if (File.Exists(symbolFontPath))
            {
                _iconGlyphRangesHandle = GCHandle.Alloc(_iconGlyphRanges, GCHandleType.Pinned);

                var mergeConfig = ImGuiNative.ImFontConfig_ImFontConfig();
                mergeConfig->MergeMode = 1; // Merge into the previously added font
                mergeConfig->FontDataOwnedByAtlas = 1; // ImGui owns file-loaded data

                io.Fonts.AddFontFromFileTTF(
                    symbolFontPath,
                    13.0f,
                    new ImFontConfigPtr(mergeConfig),
                    _iconGlyphRangesHandle.AddrOfPinnedObject());

                ImGuiNative.ImFontConfig_destroy(mergeConfig);
                Log.WriteLine("[RadarWindow] Symbol font merged for ImGui icons.");
            }
            else
            {
                Log.WriteLine("[RadarWindow] WARNING: seguisym.ttf not found, icons may render as '?'.");
            }

            // Merge Segoe UI for Cyrillic — NeoSansStd has no Cyrillic glyphs, so Russian player
            // names render as '?' without this. Segoe UI ships with Windows and covers U+0400–04FF.
            var cyrillicFontPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                "segoeui.ttf");

            if (File.Exists(cyrillicFontPath))
            {
                _cyrillicGlyphRangesHandle = GCHandle.Alloc(_cyrillicGlyphRanges, GCHandleType.Pinned);

                var cyrConfig = ImGuiNative.ImFontConfig_ImFontConfig();
                cyrConfig->MergeMode = 1; // Merge into the previously added font
                cyrConfig->FontDataOwnedByAtlas = 1; // ImGui owns file-loaded data

                io.Fonts.AddFontFromFileTTF(
                    cyrillicFontPath,
                    13.0f,
                    new ImFontConfigPtr(cyrConfig),
                    _cyrillicGlyphRangesHandle.AddrOfPinnedObject());

                ImGuiNative.ImFontConfig_destroy(cyrConfig);
                Log.WriteLine("[RadarWindow] Cyrillic font (Segoe UI) merged for ImGui.");
            }
            else
            {
                Log.WriteLine("[RadarWindow] WARNING: segoeui.ttf not found, Cyrillic names may render as '?'.");
            }
        }

        /// <summary>
        /// Applies ImGui global font scale based on config UIScale.
        /// </summary>
        private static void ApplyImGuiFontScale()
        {
            ImGui.GetIO().FontGlobalScale = UIScale;
        }
    }
}
