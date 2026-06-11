// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Numerics;
using eft_dma_radar.Silk.DMA;
using eft_dma_radar.Silk.Tarkov.Hideout;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Hideout Stash Panel — stash item table with grouping/sorting/search,
    /// price totals, area upgrade progress, and upgrade requirements display.
    /// </summary>
    internal static class HideoutPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>Whether the hideout panel is open.</summary>
        public static bool IsOpen { get; set; }

        private static string _statusText = "Press Refresh to scan hideout...";
        private static string _searchText = string.Empty;
        private static bool _grouped;
        private static int _sortColumn = -1;
        private static bool _sortAscending = true;
        private static bool _showUpgrades = true;
        private static bool _showPlanner = false;
        private static bool _showStash = true;
        private static bool _showCrafts = false;
        private static bool _refreshing;

        // ── Cached upgrade section data ──────────────────────────────────────
        private static IReadOnlyList<HideoutAreaInfo>? _cachedAreaSource;
        private static List<HideoutAreaInfo>? _cachedSortedAreas;
        private static string _cachedAreaSummary = "";

        // ── Cached planner data ──────────────────────────────────────────────
        private static IReadOnlyList<HideoutAreaInfo>? _cachedPlannerSource;
        private static List<(HideoutAreaInfo Area, bool Blocked)>? _cachedPlannerRows;
        // (TemplateId, Name, Still, Required, FiR, Avail, Unit, Vendor)
        // Avail = needed by at least one upgrade that is actionable now (not blocked).
        // Unit/Vendor = cheapest acquisition price for one (0 / "" = not buyable or unknown).
        private static List<(string TplId, string Name, int Still, int Required, bool FiR, bool Avail, long Unit, string Vendor)>? _cachedShoppingList;
        // key = templateId → list of "StationName lv→lv: need N (have M)"
        private static Dictionary<string, List<string>>? _cachedItemUsages;
        // Money summary for the shopping list, computed at rebuild.
        private static long _shopTotalCost, _shopAvailCost;
        private static int _shopFirCount, _shopUnpricedCount;

        // Shopping list UI state
        private static string _shopSearch = string.Empty;
        private static int _shopSortCol = 2;  // default: Still Need
        private static bool _shopSortAsc = false;
        private static bool _shopHideDone = true;
        private static bool _shopFirOnly = false;
        private static bool _shopAvailOnly = false;

        // ── Cached stash display list ────────────────────────────────────────
        private static IReadOnlyList<StashItem>? _cachedStashSource;
        private static bool _cachedGrouped;
        private static string _cachedSearchText = "";
        private static int _cachedStashSortColumn = -1;
        private static bool _cachedStashSortAsc = true;
        private static List<StashItem>? _cachedDisplayList;
        private static string _cachedStashSummary = "";
        private static long _cachedStashTotalBest = -1;

        // ── Colours — use UITheme ─────────────────────────────────────────────
        private static ref readonly Vector4 ColGreen  => ref UITheme.Green;
        private static ref readonly Vector4 ColOrange => ref UITheme.Orange;
        private static ref readonly Vector4 ColRed    => ref UITheme.Red;
        private static ref readonly Vector4 ColGrey   => ref UITheme.Grey;
        private static ref readonly Vector4 ColSlate  => ref UITheme.Slate;
        private static ref readonly Vector4 ColGold   => ref UITheme.Gold;
        private static ref readonly Vector4 ColDim    => ref UITheme.Dim;

        /// <summary>
        /// Maps BSG trader GUIDs to readable display names.
        /// IDs sourced from the EFT IL2CPP / API dump.
        /// </summary>
        private static readonly Dictionary<string, string> TraderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["5935c25fb3acc3127c3d8cd9"] = "Prapor",
            ["5a7c2eca46aef81a7ca2145d"] = "Therapist",
            ["579dc571d53a0658a154fbec"] = "Fence",
            ["58330581ace78e27b8b10cee"] = "Skier",
            ["5ac3b934156ae10c4430e83c"] = "Ragman",
            ["5c0647fdd443bc2504c2d371"] = "Jaeger",
            ["54cb57776803fa99248b456e"] = "Mechanic",
            ["59b91ca086f77469eef29500"] = "Peacekeeper",
            ["638f541a29ffd1183d187f57"] = "Lightkeeper",
            ["6617beeaa9cfa777ca915b7c"] = "Ref",
        };

        /// <summary>Shared HideoutManager instance from Memory.</summary>
        private static HideoutManager Manager => Memory.Hideout;

        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u2302 Hideout", ref isOpen, new Vector2(600, 650));
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            DrawToolbar();
            ImGui.Separator();

            if (_showStash)
                DrawStashSection();

            if (_showUpgrades)
                DrawUpgradesSection();

            if (_showPlanner)
                DrawPlannerSection();

            if (_showCrafts)
                DrawCraftsSection();
        }

        // ── Toolbar ──────────────────────────────────────────────────────────

        private static void DrawToolbar()
        {
            // Enabled / Auto-refresh toggles
            bool hideoutEnabled = Config.HideoutEnabled;
            if (ImGui.Checkbox("Enabled", ref hideoutEnabled))
            {
                Config.HideoutEnabled = hideoutEnabled;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable hideout stash/area reading");

            ImGui.SameLine();
            if (!Config.HideoutEnabled)
                ImGui.BeginDisabled();

            bool autoRefresh = Config.HideoutAutoRefresh;
            if (ImGui.Checkbox("Auto Refresh", ref autoRefresh))
            {
                Config.HideoutAutoRefresh = autoRefresh;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically refresh on hideout entry");

            if (!Config.HideoutEnabled)
                ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();

            // Refresh button — allowed in hideout or main menu, not in actual raids
            bool canRefresh = !_refreshing && !Memory.InRaid && Config.HideoutEnabled;
            if (!canRefresh)
                ImGui.BeginDisabled();

            if (ImGui.Button(_refreshing ? "Refreshing..." : "\u21bb Refresh"))
            {
                _refreshing = true;
                Task.Run(() =>
                {
                    try { _statusText = Manager.RefreshAll(); }
                    catch (Exception ex) { _statusText = $"Error: {ex.Message}"; }
                    finally { _refreshing = false; }
                });
            }

            if (!canRefresh)
                ImGui.EndDisabled();

            ImGui.SameLine();
            if (Memory.InHideout)
                ImGui.TextColored(ColGreen, _statusText);
            else
                ImGui.TextColored(ColSlate, _statusText);

            // Section toggles
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 350);
            ImGui.Checkbox("Stash", ref _showStash);
            ImGui.SameLine();
            ImGui.Checkbox("Upgrades", ref _showUpgrades);
            ImGui.SameLine();
            ImGui.Checkbox("Planner", ref _showPlanner);
            ImGui.SameLine();
            ImGui.Checkbox("Crafts", ref _showCrafts);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Craft recipes (tarkov.dev) with profit per hour,\nfiltered by your actual station levels.");
            ImGui.SameLine();
            ImGui.Checkbox("Group", ref _grouped);
        }

        // ── Stash Section ────────────────────────────────────────────────────

        private static void DrawStashSection()
        {
            var mgr = Manager;
            var items = mgr.Items;
            if (items.Count == 0)
            {
                ImGui.TextDisabled("No stash data. Press Refresh while in hideout.");
                return;
            }

            // Cache summary string — only rebuild when totals change
            long totalBest = mgr.TotalBestValue;
            if (!ReferenceEquals(items, _cachedStashSource) || totalBest != _cachedStashTotalBest)
            {
                _cachedStashTotalBest = totalBest;
                _cachedStashSummary =
                    $"Stash: {items.Count} items  |  Best: {HideoutManager.FormatPrice(totalBest)}  |  " +
                    $"Trader: {HideoutManager.FormatPrice(mgr.TotalTraderValue)}  |  Flea: {HideoutManager.FormatPrice(mgr.TotalFleaValue)}";
            }

            ImGui.TextColored(ColGold, _cachedStashSummary);

            // Search
            ImGui.SetNextItemWidth(250);
            ImGui.InputTextWithHint("##stashSearch", "Search items...", ref _searchText, 128);
            ImGui.Spacing();

            // Rebuild display list only when inputs change
            bool needsRebuild = !ReferenceEquals(items, _cachedStashSource)
                || _grouped != _cachedGrouped
                || !string.Equals(_searchText, _cachedSearchText, StringComparison.Ordinal);

            if (needsRebuild)
            {
                _cachedStashSource = items;
                _cachedGrouped = _grouped;
                _cachedSearchText = _searchText;
                _cachedDisplayList = BuildDisplayList(items);
                // Force re-sort with current settings
                if (_sortColumn >= 0)
                    SortDisplayList(_cachedDisplayList);
                _cachedStashSortColumn = _sortColumn;
                _cachedStashSortAsc = _sortAscending;
            }

            var displayItems = _cachedDisplayList!;

            // Table
            var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable
                      | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;

            float availH = ImGui.GetContentRegionAvail().Y;
            float tableH = _showUpgrades ? Math.Max(200, availH * 0.45f) : availH;

            if (ImGui.BeginTable("StashTable", 6, flags, new Vector2(0, tableH)))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.DefaultSort, 3f);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.None, 0.5f);
                ImGui.TableSetupColumn("Trader", ImGuiTableColumnFlags.None, 1.2f);
                ImGui.TableSetupColumn("Flea", ImGuiTableColumnFlags.None, 1.2f);
                ImGui.TableSetupColumn("Best", ImGuiTableColumnFlags.DefaultSort, 1.2f);
                ImGui.TableSetupColumn("Sell On", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                // Handle sorting — only re-sort when sort specs change
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    var spec = sortSpecs.Specs;
                    _sortColumn = spec.ColumnIndex;
                    _sortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
                    sortSpecs.SpecsDirty = false;
                }

                if (_sortColumn >= 0 && (_sortColumn != _cachedStashSortColumn || _sortAscending != _cachedStashSortAsc))
                {
                    _cachedStashSortColumn = _sortColumn;
                    _cachedStashSortAsc = _sortAscending;
                    SortDisplayList(displayItems);
                }

                for (int i = 0; i < displayItems.Count; i++)
                {
                    var item = displayItems[i];
                    bool isNeeded = mgr.NeededItemIds.Contains(item.Id);

                    ImGui.TableNextRow();

                    // Name (highlight if needed for upgrade)
                    ImGui.TableNextColumn();
                    if (isNeeded)
                    {
                        ImGui.TextColored(ColGold, item.Name);
                        if (ImGui.IsItemHovered() && mgr.NeededItemCounts.TryGetValue(item.Id, out int needed))
                            ImGui.SetTooltip($"Needed for hideout upgrade: {needed} more");
                    }
                    else
                    {
                        ImGui.TextUnformatted(item.Name);
                    }

                    // Qty
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.StackCount.ToString());

                    // Trader
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(HideoutManager.FormatPrice(item.TraderPrice * item.StackCount));

                    // Flea
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(HideoutManager.FormatPrice(item.FleaPrice * item.StackCount));

                    // Best
                    ImGui.TableNextColumn();
                    ImGui.TextColored(ColGreen, HideoutManager.FormatPrice(item.BestPrice));

                    // Sell On
                    ImGui.TableNextColumn();
                    if (item.SellOnFlea)
                        ImGui.TextColored(ColOrange, "Flea");
                    else
                        ImGui.TextUnformatted("Trader");
                }

                ImGui.EndTable();
            }
        }

        // ── Upgrades Section ─────────────────────────────────────────────────

        private static void DrawUpgradesSection()
        {
            var mgr = Manager;
            var areas = mgr.Areas;
            if (areas.Count == 0)
            {
                ImGui.TextDisabled("No area data. Press Refresh while in hideout.");
                return;
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Rebuild sorted list + summary only when the underlying data changes
            if (!ReferenceEquals(areas, _cachedAreaSource))
            {
                _cachedAreaSource = areas;

                int ready = 0, upgradeable = 0, maxed = 0;
                for (int i = 0; i < areas.Count; i++)
                {
                    if (areas[i].IsMaxLevel) maxed++;
                    else
                    {
                        upgradeable++;
                        if (areas[i].Status is EAreaStatus.ReadyToUpgrade or EAreaStatus.ReadyToConstruct)
                            ready++;
                    }
                }
                _cachedAreaSummary = $"Areas: {ready} ready  \u00b7  {upgradeable} upgradeable  \u00b7  {maxed} maxed";

                if (_cachedSortedAreas is null)
                    _cachedSortedAreas = new List<HideoutAreaInfo>(areas);
                else
                {
                    _cachedSortedAreas.Clear();
                    for (int i = 0; i < areas.Count; i++)
                        _cachedSortedAreas.Add(areas[i]);
                }
                _cachedSortedAreas.Sort(static (a, b) =>
                {
                    int ma = a.IsMaxLevel ? 1 : 0;
                    int mb = b.IsMaxLevel ? 1 : 0;
                    if (ma != mb) return ma.CompareTo(mb);
                    int pa = HideoutManager.GetStatusPriority(a.Status);
                    int pb = HideoutManager.GetStatusPriority(b.Status);
                    if (pa != pb) return pa.CompareTo(pb);
                    return ((int)a.AreaType).CompareTo((int)b.AreaType);
                });
            }

            ImGui.TextColored(ColGold, _cachedAreaSummary);
            ImGui.Spacing();

            for (int i = 0; i < _cachedSortedAreas!.Count; i++)
            {
                var area = _cachedSortedAreas[i];
                DrawAreaCard(area);
            }
        }

        private static void DrawAreaCard(HideoutAreaInfo area)
        {
            var statusColor = GetStatusColor(area.Status);
            string name = HideoutManager.FormatAreaName(area.AreaType.ToString());
            string statusLabel = HideoutManager.FormatStatus(area.Status);
            string levelLabel = area.IsMaxLevel
                ? $"lv{area.CurrentLevel}"
                : $"lv{area.CurrentLevel} → {area.CurrentLevel + 1}";

            // Dim maxed areas
            if (area.IsMaxLevel)
                ImGui.PushStyleColor(ImGuiCol.Text, ColDim);

            // Area header: name + level + status
            bool expanded = !area.IsMaxLevel && area.NextLevelRequirements.Count > 0;
            string headerId = $"{name}  {levelLabel}##area_{area.AreaType}";

            if (expanded)
            {
                if (ImGui.TreeNodeEx(headerId, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // Status badge
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(statusLabel).X - 8);
                    ImGui.TextColored(statusColor, statusLabel);

                    DrawRequirements(area.NextLevelRequirements);
                    ImGui.TreePop();
                }
                else
                {
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(statusLabel).X - 8);
                    ImGui.TextColored(statusColor, statusLabel);
                }
            }
            else
            {
                ImGui.TextUnformatted($"  {name}  {levelLabel}");
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(statusLabel).X - 8);
                ImGui.TextColored(statusColor, statusLabel);
            }

            if (area.IsMaxLevel)
                ImGui.PopStyleColor();
        }

        private static void DrawRequirements(IReadOnlyList<HideoutRequirement> reqs)
        {
            for (int i = 0; i < reqs.Count; i++)
            {
                var req = reqs[i];
                var icon = req.Fulfilled ? "✓" : "✗";
                var color = req.Fulfilled ? ColGreen : ColRed;
                var desc = FormatRequirement(req);

                ImGui.TextColored(color, $"    {icon}");
                ImGui.SameLine();
                ImGui.TextUnformatted(desc);

                if (req.Type is ERequirementType.Item or ERequirementType.Tool && req.FoundInRaid)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColOrange, "[FiR]");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Must be Found in Raid");
                }
            }
        }

        private static void ApplyShopSort()
        {
            if (_cachedShoppingList is null) return;
            _cachedShoppingList.Sort((a, b) =>
            {
                int cmp = _shopSortCol switch
                {
                    0 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                    1 => (a.Required - a.Still).CompareTo(b.Required - b.Still),    // have
                    2 => a.Still.CompareTo(b.Still),
                    3 => (a.Unit * a.Still).CompareTo(b.Unit * b.Still),            // cost to finish
                    4 => a.FiR.CompareTo(b.FiR),
                    _ => b.Still.CompareTo(a.Still)
                };
                return _shopSortAsc ? cmp : -cmp;
            });
        }

        // ── Upgrade Planner Section ──────────────────────────────────────────

        private static void DrawPlannerSection()
        {
            var mgr = Manager;
            var areas = mgr.Areas;
            if (areas.Count == 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextDisabled("No area data. Press Refresh while in hideout.");
                return;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(ColGold, "\u2605 Upgrade Planner");
            ImGui.Spacing();

            // Rebuild planner cache when data changes
            if (!ReferenceEquals(areas, _cachedPlannerSource))
            {
                _cachedPlannerSource = areas;

                var rows = new List<(HideoutAreaInfo Area, bool Blocked)>();
                var shopping = new Dictionary<string, (string TplId, string Name, int Still, int Required, bool FiR, bool Avail)>(StringComparer.Ordinal);
                var itemUsages = new Dictionary<string, List<string>>(StringComparer.Ordinal);

                for (int i = 0; i < areas.Count; i++)
                {
                    var area = areas[i];
                    if (area.IsMaxLevel) continue;

                    // An area is blocked when it has unfulfilled non-item requirements
                    // (area dependency, trader loyalty, skill, quest) that the player
                    // cannot simply buy their way through.
                    bool blocked = area.IsBlocked;

                    rows.Add((area, blocked));

                    // Aggregate unfulfilled item/tool requirements into shopping list
                    for (int r = 0; r < area.NextLevelRequirements.Count; r++)
                    {
                        var req = area.NextLevelRequirements[r];
                        if (req.Fulfilled) continue;
                        if (req.Type is not (ERequirementType.Item or ERequirementType.Tool)) continue;
                        if (req.ItemTemplateId is null) continue;

                        string key = req.ItemTemplateId;
                        string name = req.ItemName ?? req.ItemTemplateId;
                        if (shopping.TryGetValue(key, out var existing))
                            shopping[key] = (key, name, existing.Still + req.StillNeeded, existing.Required + req.RequiredCount, existing.FiR || req.FoundInRaid, existing.Avail || !blocked);
                        else
                            shopping[key] = (key, name, req.StillNeeded, req.RequiredCount, req.FoundInRaid, !blocked);

                        // Per-station usage entry for hover tooltip
                        string stationName = HideoutManager.FormatAreaName(area.AreaType.ToString());
                        string levelLabel2 = $"lv{area.CurrentLevel}\u2192{area.CurrentLevel + 1}";
                        string flastr = req.FoundInRaid ? " [FiR]" : "";
                        string usageLine = $"{stationName} {levelLabel2}: need {req.RequiredCount} (have {req.CurrentCount}){flastr}";
                        if (!itemUsages.TryGetValue(key, out var usageList))
                        {
                            usageList = [];
                            itemUsages[key] = usageList;
                        }
                        usageList.Add(usageLine);
                    }
                }

                // Sort: ready (unblocked) first, then by area name
                rows.Sort(static (a, b) =>
                {
                    if (a.Blocked != b.Blocked) return a.Blocked.CompareTo(b.Blocked);
                    return string.Compare(
                        HideoutManager.FormatAreaName(a.Area.AreaType.ToString()),
                        HideoutManager.FormatAreaName(b.Area.AreaType.ToString()),
                        StringComparison.OrdinalIgnoreCase);
                });

                _cachedPlannerRows = rows;

                // Shopping list — attach acquisition prices and compute the money summary.
                var shoppingList = new List<(string TplId, string Name, int Still, int Required, bool FiR, bool Avail, long Unit, string Vendor)>(shopping.Count);
                _shopTotalCost = 0; _shopAvailCost = 0; _shopFirCount = 0; _shopUnpricedCount = 0;
                foreach (var v in shopping.Values)
                {
                    long unit = 0; string vendor = string.Empty;
                    if (EftDataManager.AllItems.TryGetValue(v.TplId, out var mi))
                    {
                        if (mi.BuyPrice > 0) { unit = mi.BuyPrice; vendor = mi.BuyVendor; }
                        else if (mi.FleaPrice > 0) { unit = mi.FleaPrice; vendor = "Flea (est.)"; }
                    }
                    shoppingList.Add((v.TplId, v.Name, v.Still, v.Required, v.FiR, v.Avail, unit, vendor));

                    if (v.Still <= 0) continue;
                    if (v.FiR) { _shopFirCount++; continue; }       // must be found, can't be bought
                    if (unit <= 0) { _shopUnpricedCount++; continue; }
                    long cost = unit * v.Still;
                    _shopTotalCost += cost;
                    if (v.Avail) _shopAvailCost += cost;
                }
                _cachedShoppingList = shoppingList;
                _cachedItemUsages = itemUsages;
                ApplyShopSort();
            }

            var planRows = _cachedPlannerRows!;
            var shopList = _cachedShoppingList!;

            // ── Station overview ─────────────────────────────────────────────
            if (ImGui.CollapsingHeader($"Station Overview ({planRows.Count} upgradeable)##planner_overview",
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Spacing();
                for (int i = 0; i < planRows.Count; i++)
                {
                    var (area, blocked) = planRows[i];
                    string areaName = HideoutManager.FormatAreaName(area.AreaType.ToString());
                    string levelLabel = $"lv{area.CurrentLevel} \u2192 {area.CurrentLevel + 1}";
                    string statusLabel = HideoutManager.FormatStatus(area.Status);
                    var statusColor = blocked ? ColOrange : ColGreen;

                    // Tree node per station
                    bool open = ImGui.TreeNodeEx(
                        $"{areaName}  {levelLabel}##plan_{area.AreaType}",
                        ImGuiTreeNodeFlags.None);

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(statusLabel).X - 8);
                    ImGui.TextColored(blocked ? ColOrange : ColGreen, blocked ? "Blocked" : statusLabel);

                    if (open)
                    {
                        var reqs = area.NextLevelRequirements;
                        for (int r = 0; r < reqs.Count; r++)
                        {
                            var req = reqs[r];
                            bool ok = req.Fulfilled;
                            string icon = ok ? "\u2714" : "\u2716";
                            Vector4 col = ok ? ColGreen : ColRed;

                            // For items show progress bar style text
                            if (req.Type is ERequirementType.Item or ERequirementType.Tool && !ok)
                            {
                                string itemLabel = $"{req.ItemName ?? req.ItemTemplateId ?? "?"} \u00d7{req.RequiredCount}";
                                ImGui.TextColored(col, $"    {icon} {itemLabel}");
                                ImGui.SameLine();
                                ImGui.TextColored(ColGrey, $"({req.CurrentCount}/{req.RequiredCount})");
                                if (req.FoundInRaid)
                                {
                                    ImGui.SameLine();
                                    ImGui.TextColored(ColOrange, "[FiR]");
                                    if (ImGui.IsItemHovered())
                                        ImGui.SetTooltip("Must be Found in Raid");
                                }
                            }
                            else
                            {
                                ImGui.TextColored(col, $"    {icon} {FormatRequirement(req)}");
                                if (req.Type is ERequirementType.Item or ERequirementType.Tool && req.FoundInRaid)
                                {
                                    ImGui.SameLine();
                                    ImGui.TextColored(ColOrange, "[FiR]");
                                    if (ImGui.IsItemHovered())
                                        ImGui.SetTooltip("Must be Found in Raid");
                                }
                            }
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Spacing();
            }

            // ── Shopping list ────────────────────────────────────────────────
            int stillNeededCount = 0;
            for (int i = 0; i < shopList.Count; i++)
                if (shopList[i].Still > 0) stillNeededCount++;

            if (ImGui.CollapsingHeader($"Shopping List ({stillNeededCount} items still needed)##planner_shop",
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Spacing();

                // ── Filter bar ───────────────────────────────────────────────
                ImGui.SetNextItemWidth(200);
                ImGui.InputTextWithHint("##shopSearch", "Filter items...", ref _shopSearch, 64);
                ImGui.SameLine(0, 12);
                if (ImGui.Checkbox("Hide Done", ref _shopHideDone))
                    ApplyShopSort();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hide fully collected items");
                ImGui.SameLine(0, 12);
                if (ImGui.Checkbox("FiR only", ref _shopFirOnly))
                    ApplyShopSort();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show only Found-in-Raid items");
                ImGui.SameLine(0, 12);
                if (ImGui.Checkbox("Available now", ref _shopAvailOnly))
                    ApplyShopSort();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show only items for upgrades you can do right now.\n" +
                        "Hides items only needed for stations still blocked by another\n" +
                        "requirement (area level, trader loyalty, skill, or quest).");

                ImGui.Spacing();

                if (shopList.Count == 0)
                {
                    ImGui.TextColored(ColGreen, "    All item requirements fulfilled!");
                }
                else
                {
                    var tblFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                                 | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable
                                 | ImGuiTableFlags.SizingStretchProp;

                    // Compute visible row count
                    int visibleCount = 0;
                    for (int i = 0; i < shopList.Count; i++)
                    {
                        var e = shopList[i];
                        if (_shopHideDone && e.Still <= 0) continue;
                        if (_shopFirOnly && !e.FiR) continue;
                        if (_shopAvailOnly && !e.Avail) continue;
                        if (!string.IsNullOrWhiteSpace(_shopSearch) &&
                            !e.Name.Contains(_shopSearch, StringComparison.OrdinalIgnoreCase)) continue;
                        visibleCount++;
                    }

                    float tableH = Math.Min(visibleCount * 22f + 30f, 320f);

                    if (ImGui.BeginTable("PlannerShop", 5, tblFlags, new Vector2(0, tableH)))
                    {
                        ImGui.TableSetupColumn("Item",       ImGuiTableColumnFlags.DefaultSort, 3.5f, 0);
                        ImGui.TableSetupColumn("Have/Need",  ImGuiTableColumnFlags.None,        1.4f, 1);
                        ImGui.TableSetupColumn("Still Need", ImGuiTableColumnFlags.PreferSortDescending, 1.2f, 2);
                        ImGui.TableSetupColumn("Cost",       ImGuiTableColumnFlags.PreferSortDescending, 1.3f, 3);
                        ImGui.TableSetupColumn("FiR",        ImGuiTableColumnFlags.None,        0.55f, 4);
                        ImGui.TableSetupScrollFreeze(0, 1);
                        ImGui.TableHeadersRow();

                        // Handle sort specs
                        var sortSpecs = ImGui.TableGetSortSpecs();
                        if (sortSpecs.SpecsDirty)
                        {
                            var spec = sortSpecs.Specs;
                            _shopSortCol = (int)spec.ColumnUserID;
                            _shopSortAsc = spec.SortDirection == ImGuiSortDirection.Ascending;
                            sortSpecs.SpecsDirty = false;
                            ApplyShopSort();
                        }

                        for (int i = 0; i < shopList.Count; i++)
                        {
                            var (tplId, name, still, required, fir, avail, unit, vendor) = shopList[i];
                            bool done = still <= 0;

                            if (_shopHideDone && done) continue;
                            if (_shopFirOnly && !fir) continue;
                            if (_shopAvailOnly && !avail) continue;
                            if (!string.IsNullOrWhiteSpace(_shopSearch) &&
                                !name.Contains(_shopSearch, StringComparison.OrdinalIgnoreCase)) continue;

                            int have = required - still;

                            ImGui.TableNextRow();

                            // Item name
                            ImGui.TableNextColumn();
                            ImGui.TextColored(done ? ColGreen : ColGold, name);

                            // Hover tooltip — per-station breakdown
                            if (ImGui.IsItemHovered() &&
                                _cachedItemUsages is not null &&
                                _cachedItemUsages.TryGetValue(tplId, out var usages))
                            {
                                ImGui.BeginTooltip();
                                ImGui.TextColored(ColGold, name);
                                ImGui.Separator();
                                for (int u = 0; u < usages.Count; u++)
                                    ImGui.TextUnformatted(usages[u]);
                                ImGui.EndTooltip();
                            }

                            // Have / Need
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted($"{have}/{required}");

                            // Still Need
                            ImGui.TableNextColumn();
                            if (done)
                                ImGui.TextColored(ColGreen, "\u2714 Done");
                            else
                                ImGui.TextColored(ColRed, still.ToString());

                            // Cost to finish this item (unit price \u00d7 still needed)
                            ImGui.TableNextColumn();
                            if (done)
                                ImGui.TextColored(ColGreen, "\u2014");
                            else if (fir)
                                ImGui.TextColored(ColOrange, "FiR");
                            else if (unit <= 0)
                                ImGui.TextColored(ColGrey, "?");
                            else
                            {
                                ImGui.TextUnformatted(HideoutManager.FormatPrice(unit * still));
                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip($"{HideoutManager.FormatPrice(unit)} each \u2014 {vendor}");
                            }

                            // FiR
                            ImGui.TableNextColumn();
                            if (fir)
                            {
                                ImGui.TextColored(ColOrange, "\u2605");
                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip("Must be Found in Raid");
                            }
                        }

                        ImGui.EndTable();
                    }

                    // Money summary — what finishing everything would cost at current prices.
                    if (_shopTotalCost > 0 || _shopFirCount > 0 || _shopUnpricedCount > 0)
                    {
                        ImGui.TextColored(ColGold, $"Cost to finish: {HideoutManager.FormatPrice(_shopTotalCost)}");
                        ImGui.SameLine();
                        ImGui.TextColored(ColGrey, $"·  available now: {HideoutManager.FormatPrice(_shopAvailCost)}");
                        if (_shopFirCount > 0)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(ColOrange, $"·  {_shopFirCount} FiR item(s) must be found");
                        }
                        if (_shopUnpricedCount > 0)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(ColGrey, $"·  {_shopUnpricedCount} unpriced");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("No buy offer found (banned from flea / barter-only),\nor prices haven't downloaded yet.");
                        }
                    }
                }
                ImGui.Spacing();
            }
        }

        // ── Crafts Section ───────────────────────────────────────────────────

        /// <summary>One display row of the crafts table — fully precomputed at rebuild.</summary>
        private sealed record CraftRow(
            string Output, string Station, int ReqLevel, bool Available,
            int DurationSec, long Cost, bool CostIncomplete, long Value, long Profit, double ProfitPerHour,
            string[] Tooltip);

        private static IReadOnlyList<CraftElement>? _craftSource;
        private static IReadOnlyList<HideoutAreaInfo>? _craftAreaSource;
        private static List<CraftRow>? _craftRows;
        private static bool _craftLevelsKnown;
        private static string _craftSearch = string.Empty;
        private static bool _craftAvailOnly = true;
        private static int _craftSortCol = 5;   // default: ₽/h
        private static bool _craftSortAsc = false;

        /// <summary>
        /// tarkov.dev station normalizedName → <see cref="EAreaType"/> name, for the cases
        /// where they differ (both sides letters-only lowercase).
        /// </summary>
        private static readonly Dictionary<string, string> _stationAliases = new(StringComparer.Ordinal)
        {
            ["lavatory"] = "watercloset",
            ["nutritionunit"] = "kitchen",
            ["halloffame"] = "placeoffame",
            ["christmastree"] = "christmasillumination",
        };

        private static string LettersOnly(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsLetter(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        private static void DrawCraftsSection()
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(ColGold, "⚒ Crafts");
            ImGui.Spacing();

            var crafts = EftDataManager.Crafts;
            if (crafts.Count == 0)
            {
                ImGui.TextDisabled("Craft data not downloaded yet — it arrives with the automatic\n" +
                                   "tarkov.dev refresh shortly after start (needs internet).");
                return;
            }

            // Station levels from the live hideout read, falling back to the persistent
            // cache from a previous session. Empty = availability unknown.
            var mgr = Manager;
            var areas = mgr.Areas.Count > 0 ? mgr.Areas : mgr.PersistentAreas;

            if (!ReferenceEquals(crafts, _craftSource) || !ReferenceEquals(areas, _craftAreaSource))
            {
                _craftSource = crafts;
                _craftAreaSource = areas;
                RebuildCraftRows(crafts, areas);
                ApplyCraftSort();
            }

            var rows = _craftRows!;

            if (!_craftLevelsKnown)
                ImGui.TextDisabled("Station levels unknown — Refresh in hideout to filter by what you can craft.");

            // Filter bar
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##craftSearch", "Filter crafts...", ref _craftSearch, 64);
            ImGui.SameLine(0, 12);
            ImGui.Checkbox("Available only", ref _craftAvailOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only crafts your current station levels allow.");
            ImGui.Spacing();

            bool availFilter = _craftAvailOnly && _craftLevelsKnown;

            int visibleCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (availFilter && !r.Available) continue;
                if (!string.IsNullOrWhiteSpace(_craftSearch)
                    && !r.Output.Contains(_craftSearch, StringComparison.OrdinalIgnoreCase)
                    && !r.Station.Contains(_craftSearch, StringComparison.OrdinalIgnoreCase)) continue;
                visibleCount++;
            }

            float tableH = Math.Min(visibleCount * 22f + 30f, 380f);
            var tblFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                         | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable
                         | ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("CraftsTable", 6, tblFlags, new Vector2(0, tableH)))
            {
                ImGui.TableSetupColumn("Craft",   ImGuiTableColumnFlags.None, 2.8f, 0);
                ImGui.TableSetupColumn("Station", ImGuiTableColumnFlags.None, 1.5f, 1);
                ImGui.TableSetupColumn("Time",    ImGuiTableColumnFlags.None, 0.8f, 2);
                ImGui.TableSetupColumn("Cost",    ImGuiTableColumnFlags.PreferSortDescending, 1.1f, 3);
                ImGui.TableSetupColumn("Value",   ImGuiTableColumnFlags.PreferSortDescending, 1.1f, 4);
                ImGui.TableSetupColumn("₽/h", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, 1.1f, 5);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    var spec = sortSpecs.Specs;
                    _craftSortCol = (int)spec.ColumnUserID;
                    _craftSortAsc = spec.SortDirection == ImGuiSortDirection.Ascending;
                    sortSpecs.SpecsDirty = false;
                    ApplyCraftSort();
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    if (availFilter && !r.Available) continue;
                    if (!string.IsNullOrWhiteSpace(_craftSearch)
                        && !r.Output.Contains(_craftSearch, StringComparison.OrdinalIgnoreCase)
                        && !r.Station.Contains(_craftSearch, StringComparison.OrdinalIgnoreCase)) continue;

                    ImGui.TableNextRow();

                    // Craft output
                    ImGui.TableNextColumn();
                    ImGui.TextColored(r.Available ? UITheme.White : ColDim, r.Output);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextColored(ColGold, r.Output);
                        ImGui.TextColored(r.Profit >= 0 ? ColGreen : ColRed,
                            $"Cost {HideoutManager.FormatPrice(r.Cost)}  →  Value {HideoutManager.FormatPrice(r.Value)}   Profit {HideoutManager.FormatPrice(r.Profit)}");
                        ImGui.Separator();
                        for (int t = 0; t < r.Tooltip.Length; t++)
                            ImGui.TextUnformatted(r.Tooltip[t]);
                        ImGui.EndTooltip();
                    }

                    // Station + required level
                    ImGui.TableNextColumn();
                    ImGui.TextColored(r.Available ? ColGrey : ColOrange, $"{r.Station} lv{r.ReqLevel}");
                    if (!r.Available && ImGui.IsItemHovered())
                        ImGui.SetTooltip("Station level too low");

                    // Duration
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FormatDuration(r.DurationSec));

                    // Cost
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r.CostIncomplete
                        ? $"{HideoutManager.FormatPrice(r.Cost)}+?"
                        : HideoutManager.FormatPrice(r.Cost));
                    if (r.CostIncomplete && ImGui.IsItemHovered())
                        ImGui.SetTooltip("Some ingredients have no known buy price — true cost is higher.");

                    // Value
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(HideoutManager.FormatPrice(r.Value));

                    // Profit per hour
                    ImGui.TableNextColumn();
                    ImGui.TextColored(r.ProfitPerHour >= 0 ? ColGreen : ColRed,
                        HideoutManager.FormatPrice((long)r.ProfitPerHour));
                }

                ImGui.EndTable();
            }
            ImGui.Spacing();
        }

        private static void RebuildCraftRows(IReadOnlyList<CraftElement> crafts, IReadOnlyList<HideoutAreaInfo> areas)
        {
            // Live station levels keyed by letters-only enum name ("watercollector" etc.)
            var levels = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < areas.Count; i++)
                levels[LettersOnly(areas[i].AreaType.ToString())] = areas[i].CurrentLevel;
            _craftLevelsKnown = levels.Count > 0;

            var rows = new List<CraftRow>(crafts.Count);
            var tip = new List<string>(8);

            foreach (var c in crafts)
            {
                if (c.Station is null || c.RewardItems is not { Count: > 0 })
                    continue;

                // Output label + total reward value (best sell across flea/trader).
                string output = "?";
                long value = 0;
                bool firstReward = true;
                foreach (var r in c.RewardItems)
                {
                    if (r.Item is null) continue;
                    string nm = DisplayNameFor(r.Item);
                    if (firstReward)
                    {
                        output = r.Count > 1 ? $"{nm} ×{r.Count}" : nm;
                        firstReward = false;
                    }
                    if (EftDataManager.AllItems.TryGetValue(r.Item.Id, out var rmi))
                        value += (long)rmi.BestPrice * r.Count;
                }
                if (firstReward)
                    continue; // no usable reward item

                // Ingredient cost (tools are returned — listed but free).
                tip.Clear();
                long cost = 0;
                bool costIncomplete = false;
                if (c.RequiredItems is not null)
                {
                    foreach (var req in c.RequiredItems)
                    {
                        if (req.Item is null) continue;
                        string nm = DisplayNameFor(req.Item);
                        if (req.IsTool)
                        {
                            tip.Add($"{nm} — tool (returned)");
                            continue;
                        }

                        long unit = 0; string vendor = string.Empty;
                        if (EftDataManager.AllItems.TryGetValue(req.Item.Id, out var mi))
                        {
                            if (mi.BuyPrice > 0) { unit = mi.BuyPrice; vendor = mi.BuyVendor; }
                            else if (mi.FleaPrice > 0) { unit = mi.FleaPrice; vendor = "Flea (est.)"; }
                        }

                        if (unit <= 0)
                        {
                            costIncomplete = true;
                            tip.Add($"{nm} ×{req.Count} — price unknown");
                        }
                        else
                        {
                            cost += unit * req.Count;
                            tip.Add($"{nm} ×{req.Count} — {HideoutManager.FormatPrice(unit * req.Count)} ({vendor})");
                        }
                    }
                }

                string key = LettersOnly(c.Station.NormalizedName);
                if (_stationAliases.TryGetValue(key, out var alias))
                    key = alias;
                bool available = !_craftLevelsKnown
                    || (levels.TryGetValue(key, out int lvl) && lvl >= c.Level);

                long profit = value - cost;
                double hours = Math.Max(c.DurationSeconds, 60) / 3600.0;

                rows.Add(new CraftRow(
                    output, c.Station.Name, c.Level, available,
                    c.DurationSeconds, cost, costIncomplete, value, profit, profit / hours,
                    tip.ToArray()));
            }

            _craftRows = rows;
        }

        private static string DisplayNameFor(CraftElement.ItemRef item)
        {
            if (EftDataManager.AllItems.TryGetValue(item.Id, out var mi) && !string.IsNullOrEmpty(mi.ShortName))
                return mi.ShortName;
            return string.IsNullOrEmpty(item.ShortName) ? item.Name : item.ShortName;
        }

        private static void ApplyCraftSort()
        {
            if (_craftRows is null) return;
            _craftRows.Sort((a, b) =>
            {
                int cmp = _craftSortCol switch
                {
                    0 => string.Compare(a.Output, b.Output, StringComparison.OrdinalIgnoreCase),
                    1 => string.Compare(a.Station, b.Station, StringComparison.OrdinalIgnoreCase),
                    2 => a.DurationSec.CompareTo(b.DurationSec),
                    3 => a.Cost.CompareTo(b.Cost),
                    4 => a.Value.CompareTo(b.Value),
                    5 => a.ProfitPerHour.CompareTo(b.ProfitPerHour),
                    _ => b.ProfitPerHour.CompareTo(a.ProfitPerHour),
                };
                return _craftSortAsc ? cmp : -cmp;
            });
        }

        private static string FormatDuration(int seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:00}m" : $"{ts.Minutes}m";
        }

        private static string FormatRequirement(HideoutRequirement req) => req.Type switch
        {
            ERequirementType.Item or ERequirementType.Tool when !req.Fulfilled
                => $"{req.ItemName ?? req.ItemTemplateId ?? "?"} ×{req.RequiredCount}  ({req.CurrentCount}/{req.RequiredCount})",
            ERequirementType.Item or ERequirementType.Tool
                => $"{req.ItemName ?? req.ItemTemplateId ?? "?"} ×{req.RequiredCount}",
            ERequirementType.Area
                => $"{HideoutManager.FormatAreaName(req.RequiredArea.ToString())} lv{req.RequiredLevel}",
            ERequirementType.Skill
                => $"{req.SkillName ?? "Skill"} lv{req.SkillLevel}",
            ERequirementType.TraderLoyalty
                => $"{ResolveTraderName(req.TraderId)} LL{req.LoyaltyLevel}",
            ERequirementType.TraderUnlock => "Trader unlock",
            ERequirementType.QuestComplete => "Quest complete",
            _ => req.Type.ToString()
        };

        private static string ResolveTraderName(string? id)
        {
            if (id is null) return "Trader";
            return TraderNames.TryGetValue(id, out var name) ? name : id;
        }

        // ── Display list helpers ─────────────────────────────────────────────

        private static List<StashItem> BuildDisplayList(IReadOnlyList<StashItem> items)
        {
            List<StashItem> result;

            if (_grouped)
            {
                var groups = new Dictionary<string, (StashItem First, int TotalQty)>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (groups.TryGetValue(item.Id, out var existing))
                        groups[item.Id] = (existing.First, existing.TotalQty + item.StackCount);
                    else
                        groups[item.Id] = (item, item.StackCount);
                }

                result = new List<StashItem>(groups.Count);
                foreach (var (_, (first, totalQty)) in groups)
                {
                    result.Add(new StashItem(
                        Id: first.Id,
                        Name: first.Name,
                        TraderPrice: first.TraderPrice,
                        FleaPrice: first.FleaPrice,
                        StackCount: totalQty));
                }
            }
            else
            {
                result = new List<StashItem>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    result.Add(items[i]);
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.Trim();
                result.RemoveAll(i =>
                    !i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !i.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return result;
        }

        private static void SortDisplayList(List<StashItem> items)
        {
            items.Sort(_sortColumn switch
            {
                0 => _sortAscending
                    ? static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                    : static (a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase),
                1 => _sortAscending
                    ? static (a, b) => a.StackCount.CompareTo(b.StackCount)
                    : static (a, b) => b.StackCount.CompareTo(a.StackCount),
                2 => _sortAscending
                    ? static (a, b) => (a.TraderPrice * a.StackCount).CompareTo(b.TraderPrice * b.StackCount)
                    : static (a, b) => (b.TraderPrice * b.StackCount).CompareTo(a.TraderPrice * a.StackCount),
                3 => _sortAscending
                    ? static (a, b) => (a.FleaPrice * a.StackCount).CompareTo(b.FleaPrice * b.StackCount)
                    : static (a, b) => (b.FleaPrice * b.StackCount).CompareTo(a.FleaPrice * a.StackCount),
                4 => _sortAscending
                    ? static (a, b) => a.BestPrice.CompareTo(b.BestPrice)
                    : static (a, b) => b.BestPrice.CompareTo(a.BestPrice),
                5 => _sortAscending
                    ? static (a, b) => a.SellOnFlea.CompareTo(b.SellOnFlea)
                    : static (a, b) => b.SellOnFlea.CompareTo(a.SellOnFlea),
                _ => static (_, _) => 0
            });
        }

        private static Vector4 GetStatusColor(EAreaStatus s) => s switch
        {
            EAreaStatus.ReadyToConstruct or EAreaStatus.ReadyToUpgrade or
            EAreaStatus.ReadyToInstallConstruct or EAreaStatus.ReadyToInstallUpgrade => ColGreen,
            EAreaStatus.Constructing or EAreaStatus.Upgrading or
            EAreaStatus.AutoUpgrading => ColOrange,
            EAreaStatus.NoFutureUpgrades => ColSlate,
            _ => ColGrey
        };
    }
}
