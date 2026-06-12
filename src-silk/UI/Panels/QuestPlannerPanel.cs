// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Numerics;

using eft_dma_radar.Silk.Tarkov.QuestPlanner;
using eft_dma_radar.Silk.Tarkov.QuestPlanner.Models;

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Quest Planner Panel — lobby-only view that recommends which map(s) to run next
    /// based on the player's active quests, unlock dependencies, and per-map bring lists.
    /// Consumes <see cref="QuestPlannerWorker.Current"/>.
    /// </summary>
    internal static class QuestPlannerPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        public static bool IsOpen { get; set; }

        // Collapsed sections
        private static readonly HashSet<string> _collapsedMaps = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _collapsedQuests = new(StringComparer.OrdinalIgnoreCase);
        private static bool _collapsedAllMaps = true;
        private static bool _collapsedFir;
        private static bool _collapsedHandOver;

        // Toolbar state
        private static string[]? _traderOptions;        // ["All Traders", "Prapor", …]
        private static int _traderIndex;                // 0 = all
        private static readonly string[] _sortLabels = { "Recommended", "Most objectives", "Most unlocks" };
        private static string _search = string.Empty;

        // Colours — use UITheme
        private static ref readonly Vector4 ColGreen   => ref UITheme.Green;
        private static ref readonly Vector4 ColOrange  => ref UITheme.Orange;
        private static ref readonly Vector4 ColCyan    => ref UITheme.Cyan;
        private static ref readonly Vector4 ColYellow  => ref UITheme.Yellow;
        private static ref readonly Vector4 ColDim     => ref UITheme.Dim;
        private static ref readonly Vector4 ColWhite   => ref UITheme.White;
        private static ref readonly Vector4 ColBlue    => ref UITheme.Blue;
        private static ref readonly Vector4 ColKappa   => ref UITheme.Kappa;
        private static ref readonly Vector4 ColGold    => ref UITheme.Gold;
        private static ref readonly Vector4 ColGrey    => ref UITheme.Grey;

        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u2741 Quest Planner", ref isOpen, new Vector2(560, 640));
            IsOpen = isOpen;
            if (!scope.Visible) return;

            DrawToolbar();
            ImGui.Separator();

            var state = QuestPlannerWorker.State;
            if (state == QuestPlannerState.Disconnected)
            {
                ImGui.TextColored(ColDim, "Game not connected.");
                return;
            }
            if (state == QuestPlannerState.InRaid)
            {
                ImGui.TextColored(ColDim, "In raid \u2014 planner suspended. Extract to refresh.");
                return;
            }

            var summary = QuestPlannerWorker.Current;
            if (summary is null)
            {
                ImGui.TextColored(ColDim, QuestPlannerWorker.IsStale
                    ? "Waiting for profile to become available…"
                    : "Computing plan…");
                return;
            }

            DrawHeader(summary);
            ImGui.Separator();
            DrawTraderBanners(summary);
            DrawHandOverSection(summary);
            DrawFirSection(summary);

            ImGui.Separator();
            DrawMapList(summary);
            DrawAllMapsSection(summary);
        }

        // ── Toolbar ──────────────────────────────────────────────────────────

        private static void DrawToolbar()
        {
            var cfg = Config;

            // Row 1 — progression toggles ("main quest" = Kappa and/or Lightkeeper).
            bool kappa = cfg.QuestKappaFilter;
            if (ImGui.Checkbox("Kappa", ref kappa))
            {
                cfg.QuestKappaFilter = kappa;
                cfg.MarkDirty();
                QuestPlannerWorker.ForceRecompute();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only quests required for the Kappa container.");

            ImGui.SameLine();
            bool lk = cfg.QuestLightkeeperFilter;
            if (ImGui.Checkbox("Lightkeeper", ref lk))
            {
                cfg.QuestLightkeeperFilter = lk;
                cfg.MarkDirty();
                QuestPlannerWorker.ForceRecompute();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only quests on the Lightkeeper progression line.\nEnable both for the combined main-quest path.");

            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
                QuestPlannerWorker.ForceRecompute();

            if (QuestPlannerWorker.IsStale)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColOrange, "(stale)");
            }

            // Row 2 — trader filter + sort order.
            EnsureTraderOptions();
            if (_traderOptions is not null)
            {
                ImGui.SetNextItemWidth(160);
                if (ImGui.Combo("Trader", ref _traderIndex, _traderOptions, _traderOptions.Length))
                {
                    cfg.QuestPlannerTraderFilter = _traderIndex <= 0 ? "" : _traderOptions[_traderIndex];
                    cfg.MarkDirty();
                    QuestPlannerWorker.ForceRecompute();
                }
                ImGui.SameLine();
            }

            int sortMode = cfg.QuestPlannerSortMode;
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Sort", ref sortMode, _sortLabels, _sortLabels.Length))
            {
                cfg.QuestPlannerSortMode = Math.Clamp(sortMode, 0, _sortLabels.Length - 1);
                cfg.MarkDirty();
                QuestPlannerWorker.ForceRecompute();
            }

            // Row 3 — level gate + search box (transient; drives the worker directly).
            int maxLevel = cfg.QuestPlannerMaxLevel;
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("Lvl", ref maxLevel))
            {
                cfg.QuestPlannerMaxLevel = Math.Clamp(maxLevel, 0, 79);
                cfg.MarkDirty();
                QuestPlannerWorker.ForceRecompute();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hide quests that require a higher PMC level. 0 = show all.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##questSearch", "Search quests / objectives…", ref _search, 128))
            {
                QuestPlannerWorker.SearchText = _search;
                QuestPlannerWorker.ForceRecompute();
            }

            // Selected-quest banner — this is what the radar pins (shared with the Quest panel).
            if (!string.IsNullOrEmpty(cfg.QuestSelectedId))
            {
                ImGui.TextColored(ColCyan, "◉");
                ImGui.SameLine();
                ImGui.TextColored(ColKappa, GetSelectedQuestName(cfg.QuestSelectedId));
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear##selQuest"))
                {
                    cfg.QuestSelectedId = "";
                    cfg.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("This quest's zones & items are pinned to the radar.\nRight-click a quest to change.");
            }
        }

        // ── Toolbar helpers ──────────────────────────────────────────────────

        private static void EnsureTraderOptions()
        {
            if (_traderOptions is not null) return;
            if (EftDataManager.AllTraders.Count == 0) return; // wait for data to load

            var names = new List<string>(EftDataManager.AllTraders.Values);
            names.Sort(StringComparer.OrdinalIgnoreCase);

            var opts = new List<string>(names.Count + 1) { "All Traders" };
            opts.AddRange(names);
            _traderOptions = opts.ToArray();

            // Restore the persisted selection.
            var sel = Config.QuestPlannerTraderFilter;
            if (!string.IsNullOrEmpty(sel))
            {
                for (int i = 1; i < _traderOptions.Length; i++)
                {
                    if (string.Equals(_traderOptions[i], sel, StringComparison.OrdinalIgnoreCase))
                    {
                        _traderIndex = i;
                        break;
                    }
                }
            }
        }

        private static string GetSelectedQuestName(string questId)
            => EftDataManager.TaskData.TryGetValue(questId, out var t) && !string.IsNullOrEmpty(t.Name)
                ? t.Name
                : questId;

        // ── Rewards (tarkov.dev finishRewards) ───────────────────────────────

        // Built strings cached per task id; invalidated when the task database
        // instance is swapped by a background data refresh.
        private static readonly Dictionary<string, string> _rewardsCache = new(StringComparer.OrdinalIgnoreCase);
        private static object? _rewardsCacheSource;

        private static string GetRewardsLine(string taskId)
        {
            if (taskId.Length == 0)
                return string.Empty;

            if (!ReferenceEquals(_rewardsCacheSource, EftDataManager.TaskData))
            {
                _rewardsCache.Clear();
                _rewardsCacheSource = EftDataManager.TaskData;
            }

            if (_rewardsCache.TryGetValue(taskId, out var cached))
                return cached;

            string line = BuildRewardsLine(taskId);
            _rewardsCache[taskId] = line;
            return line;
        }

        private static string BuildRewardsLine(string taskId)
        {
            if (!EftDataManager.TaskData.TryGetValue(taskId, out var te))
                return string.Empty;

            var parts = new List<string>(8);
            if (te.Experience > 0)
                parts.Add($"{te.Experience:N0} XP");

            var fr = te.FinishRewards;
            if (fr?.TraderStanding is { } standings)
            {
                foreach (var s in standings)
                    if (s.Trader is { } tr && s.Standing != 0)
                        parts.Add($"{tr.Name} {(s.Standing > 0 ? "+" : "")}{s.Standing:0.00}");
            }
            if (fr?.Items is { } items)
            {
                foreach (var ri in items)
                {
                    if (ri.Item is null) continue;
                    string n = ri.Item.Name;
                    if (n.Equals("Roubles", StringComparison.OrdinalIgnoreCase))
                        parts.Add($"₽{ri.Count:N0}");
                    else if (n.Equals("Dollars", StringComparison.OrdinalIgnoreCase))
                        parts.Add($"${ri.Count:N0}");
                    else if (n.Equals("Euros", StringComparison.OrdinalIgnoreCase))
                        parts.Add($"€{ri.Count:N0}");
                    else
                        parts.Add(ri.Count > 1 ? $"{ri.Item.ShortName} ×{ri.Count:0.##}" : ri.Item.ShortName);
                }
            }

            string line = parts.Count > 0 ? "Rewards: " + string.Join("  ·  ", parts) : string.Empty;

            if (fr?.OfferUnlock is { Count: > 0 } offers)
            {
                var unlocks = new List<string>(offers.Count);
                foreach (var o in offers)
                {
                    if (o.Item is null) continue;
                    unlocks.Add(o.Trader is { } t ? $"{o.Item.ShortName} @ {t.Name} LL{o.Level}" : o.Item.ShortName);
                }
                if (unlocks.Count > 0)
                    line += (line.Length > 0 ? "\n" : "") + "Unlocks purchase: " + string.Join(", ", unlocks);
            }

            return line;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[QuestPlannerPanel] Failed to open URL '{url}': {ex.Message}");
            }
        }

        // ── Header ───────────────────────────────────────────────────────────

        private static void DrawHeader(QuestSummary s)
        {
            ImGui.TextColored(ColWhite,
                $"{s.TotalActiveQuests} active quests  —  {s.TotalCompletableObjectives} completable objectives  —  {s.Maps.Count} maps planned");
            ImGui.TextColored(ColDim, $"Computed {s.ComputedAt.ToLocalTime():HH:mm:ss}");
        }

        private static void DrawTraderBanners(QuestSummary s)
        {
            if (s.AvailableForStartTraders.Count > 0)
            {
                ImGui.TextColored(ColCyan, "Available to start:");
                ImGui.SameLine();
                ImGui.TextUnformatted(string.Join(", ", s.AvailableForStartTraders));
            }
            if (s.AvailableForFinishTraders.Count > 0)
            {
                ImGui.TextColored(ColGreen, "Ready to turn in:");
                ImGui.SameLine();
                ImGui.TextUnformatted(string.Join(", ", s.AvailableForFinishTraders));
            }
        }

        // ── Hand-over ────────────────────────────────────────────────────────

        private static void DrawHandOverSection(QuestSummary s)
        {
            if (s.HandOverItems.Count == 0) return;
            ImGui.Separator();
            if (ImGui.Selectable($"{(_collapsedHandOver ? "\u25B6" : "\u25BC")} Hand over items ({s.HandOverItems.Count})", false))
                _collapsedHandOver = !_collapsedHandOver;
            if (_collapsedHandOver) return;

            foreach (var h in s.HandOverItems)
            {
                ImGui.Bullet();
                ImGui.TextColored(ColYellow, h.QuestName);
                ImGui.SameLine();
                ImGui.TextUnformatted($"— {h.ItemShortName}");
            }
        }

        // ── FIR items ────────────────────────────────────────────────────────

        private static void DrawFirSection(QuestSummary s)
        {
            if (s.FirItems.Count == 0) return;
            ImGui.Separator();
            if (ImGui.Selectable($"{(_collapsedFir ? "\u25B6" : "\u25BC")} Find in raid ({s.FirItems.Count})", false))
                _collapsedFir = !_collapsedFir;
            if (_collapsedFir) return;

            foreach (var fir in s.FirItems)
            {
                ImGui.Bullet();
                ImGui.TextColored(ColYellow, fir.QuestName);
                ImGui.SameLine();
                ImGui.TextUnformatted($"— {fir.ItemShortName}");
                ImGui.SameLine();
                ImGui.TextColored(ColDim, fir.ProgressText);
            }
        }

        // ── Map list ─────────────────────────────────────────────────────────

        private static void DrawMapList(QuestSummary s)
        {
            if (s.Maps.Count == 0)
            {
                ImGui.TextColored(ColDim, "No maps with completable objectives.");
                return;
            }

            foreach (var map in s.Maps)
                DrawMap(map);
        }

        private static void DrawMap(MapPlan map)
        {
            bool collapsed = _collapsedMaps.Contains(map.MapId);
            var arrow = collapsed ? "\u25B6" : "\u25BC";
            var recommended = map.IsRecommended ? " \u2605" : string.Empty;
            var headerColor = map.IsRecommended ? ColGreen : ColWhite;

            var label = $"{arrow} {map.MapName}{recommended}  —  {map.ActiveQuestCount} quests, {map.CompletableObjectiveCount} objectives";
            ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
            bool clicked = ImGui.Selectable(label, false);
            ImGui.PopStyleColor();

            if (clicked)
            {
                if (collapsed) _collapsedMaps.Remove(map.MapId);
                else _collapsedMaps.Add(map.MapId);
            }

            if (collapsed) return;

            ImGui.Indent();

            // Bring list
            if (map.FilteredBringList.Count > 0)
            {
                ImGui.TextColored(ColCyan, "Bring:");
                ImGui.Indent();
                foreach (var b in map.FilteredBringList)
                {
                    var alts = string.Join(" / ", b.Alternatives);
                    ImGui.Bullet();
                    if (b.Type == BringItemType.Key)
                        ImGui.TextColored(ColOrange, alts);
                    else
                        ImGui.TextUnformatted(alts);
                    if (b.Count > 1)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(ColDim, $"x{b.Count}");
                    }
                }
                ImGui.Unindent();
            }

            // Quests
            if (map.Quests.Count > 0)
            {
                ImGui.TextColored(ColCyan, "Quests:");
                ImGui.Indent();
                foreach (var q in map.Quests)
                    DrawQuest(map.MapId, q);
                ImGui.Unindent();
            }

            // Unlocks
            if (map.UnlockedQuests.Count > 0)
            {
                ImGui.TextColored(ColBlue, $"Unlocks ({map.UnlockedQuests.Count}):");
                ImGui.Indent();
                foreach (var u in map.UnlockedQuests)
                {
                    ImGui.Bullet();
                    ImGui.TextUnformatted(u.QuestName);
                    ImGui.SameLine();
                    ImGui.TextColored(ColDim, $"({u.MapName})");
                }
                ImGui.Unindent();
            }

            ImGui.Unindent();
            ImGui.Spacing();
        }

        private static void DrawQuest(string mapId, QuestPlan quest)
        {
            var key = mapId + "\u0001" + quest.QuestName;
            bool collapsed = _collapsedQuests.Contains(key);
            var arrow = collapsed ? "\u25B6" : "\u25BC";
            bool isSelected = quest.TaskId.Length > 0
                && string.Equals(Config.QuestSelectedId, quest.TaskId, StringComparison.OrdinalIgnoreCase);

            ImGui.PushID(key);

            // Header: arrow + progression badges + selection dot + name.
            ImGui.TextColored(ColWhite, arrow);
            ImGui.SameLine();
            if (quest.KappaRequired)
            {
                ImGui.TextColored(ColKappa, "★"); // star = Kappa
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Required for Kappa");
                ImGui.SameLine();
            }
            if (quest.LightkeeperRequired)
            {
                ImGui.TextColored(ColGold, "⚑"); // flag = Lightkeeper
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Required for Lightkeeper");
                ImGui.SameLine();
            }
            if (isSelected)
            {
                ImGui.TextColored(ColCyan, "◉"); // pinned to radar
                ImGui.SameLine();
            }

            var label = $"{quest.QuestName}  ({quest.Objectives.Count})";
            if (ImGui.Selectable(label, isSelected))
            {
                if (collapsed) _collapsedQuests.Remove(key);
                else _collapsedQuests.Add(key);
            }

            DrawQuestContextMenu(quest, isSelected);

            if (collapsed)
            {
                ImGui.PopID();
                return;
            }

            ImGui.Indent();

            // Meta line: trader + min level.
            if (!string.IsNullOrEmpty(quest.TraderName) || quest.MinPlayerLevel > 0)
            {
                var meta = quest.TraderName;
                if (quest.MinPlayerLevel > 0)
                    meta = string.IsNullOrEmpty(meta)
                        ? $"Lv {quest.MinPlayerLevel}"
                        : $"{meta}  ·  Lv {quest.MinPlayerLevel}";
                ImGui.TextColored(ColGrey, meta);
            }

            // Rewards line: XP, trader rep, money/items, unlocked trader offers.
            var rewards = GetRewardsLine(quest.TaskId);
            if (rewards.Length > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ColGrey);
                ImGui.TextWrapped(rewards);
                ImGui.PopStyleColor();
            }

            foreach (var o in quest.Objectives)
            {
                ImGui.Bullet();
                ImGui.TextUnformatted(o.Description);
                if (o.HasProgress)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColDim, o.ProgressText);
                }
            }

            // Dependency chain - what finishing this quest opens up.
            if (quest.Unlocks.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ColBlue);
                ImGui.TextWrapped($"↳ Unlocks: {string.Join(", ", quest.Unlocks)}");
                ImGui.PopStyleColor();
            }

            ImGui.Unindent();
            ImGui.PopID();
        }

        private static void DrawQuestContextMenu(QuestPlan quest, bool isSelected)
        {
            if (!ImGui.BeginPopupContextItem("qp_ctx"))
                return;

            if (quest.TaskId.Length > 0)
            {
                if (isSelected)
                {
                    if (ImGui.MenuItem("Deselect (radar)"))
                    {
                        Config.QuestSelectedId = "";
                        Config.MarkDirty();
                    }
                }
                else if (ImGui.MenuItem("Select on Radar"))
                {
                    Config.QuestSelectedId = quest.TaskId;
                    Config.QuestSelectedOnly = true;
                    Config.MarkDirty();
                }
            }

            if (!string.IsNullOrEmpty(quest.WikiLink) && ImGui.MenuItem("Open Wiki"))
                OpenUrl(quest.WikiLink);

            ImGui.EndPopup();
        }

        // ── All Maps ─────────────────────────────────────────────────────────

        private static void DrawAllMapsSection(QuestSummary s)
        {
            if (s.AllMapsQuests.Count == 0) return;
            ImGui.Separator();
            if (ImGui.Selectable($"{(_collapsedAllMaps ? "\u25B6" : "\u25BC")} All Maps — quests without a specific location ({s.AllMapsQuests.Count})", false))
                _collapsedAllMaps = !_collapsedAllMaps;
            if (_collapsedAllMaps) return;

            foreach (var q in s.AllMapsQuests)
                DrawQuest("_allmaps", q);
        }
    }
}
