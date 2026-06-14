// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.UI.Controls;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Loot Filters Panel — search, price thresholds, price mode, category toggles,
    /// wishlist/blacklist management, quick presets, and live stats.
    /// </summary>
    internal static class LootFiltersPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>
        /// Whether the loot filters panel is open.
        /// </summary>
        public static bool IsOpen { get; set; }

        // ── Wishlist/Blacklist item search state ─────────────────────────────

        private static string _itemSearchText = string.Empty;
        private static readonly List<TarkovMarketItem> _searchResults = new(20);
        private static bool _searchDirty;
        private const int MaxSearchResults = 20;

        /// <summary>
        /// Draw the loot filters panel.
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u25a3 Loot Filters", ref isOpen, new Vector2(440, 620));
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            DrawStatusBar();
            DrawQuickViewPresets();
            DrawSearchSection();
            DrawPriceSection();
            DrawCategorySection();
            DrawQuestHighlightSection();
            DrawHideoutHighlightSection();
            DrawWishlistSettingsSection();
            DrawIngameWishlistSection();
            DrawWishlistBlacklistSection();
            DrawOptionsSection();
            DrawFooter();
        }

        // ── Status bar ───────────────────────────────────────────────────────

        private static void DrawStatusBar()
        {
            // Big show-loot toggle row — AnyDesk friendly.
            bool showLoot = Config.ShowLoot;
            if (UIControls.ToggleRow("Show Loot", ref showLoot,
                "Master switch for loot rendering on the radar."))
            {
                Config.ShowLoot = showLoot;
                Config.MarkDirty();
            }

            // Live counts as a discrete chip beneath the toggle.
            var visible = LootFilter.VisibleCount;
            var total = LootFilter.TotalCount;
            string stats = total > 0 ? $"{visible} / {total} visible" : "No loot data yet";
            var col = total > 0 && visible == 0
                ? new Vector4(1f, 0.55f, 0.35f, 1f)
                : new Vector4(0.55f, 0.58f, 0.62f, 1f);

            float textWidth = ImGui.CalcTextSize(stats).X;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - textWidth - ImGui.GetStyle().WindowPadding.X);
            ImGui.TextColored(col, stats);
            ImGui.Spacing();
        }

        // ── Quick view presets ───────────────────────────────────────────────

        /// <summary>
        /// One-tap shortcuts that snap a sensible combination of flags. Not a
        /// full preset system — these don't drift back to "Custom" or persist
        /// a preset id; they're just convenience buttons for the four most
        /// common views. The currently-active mode is highlighted in cyan.
        /// </summary>
        private static void DrawQuickViewPresets()
        {
            UIControls.Section("Quick View");

            float avail = ImGui.GetContentRegionAvail().X;
            const float gap = 6f;
            float chipW = (avail - gap * 3f) / 4f;
            var chipSize = new Vector2(chipW, 30f * SilkProgram.Config.UIScale);

            if (DrawQuickChip("All Loot",
                "Show everything that passes the price filter.\nKeeps your existing category overrides.",
                chipSize, IsAllLootMode))
            {
                Config.LootImportantOnly = false;
                Config.MarkDirty();
            }
            ImGui.SameLine(0, gap);
            if (DrawQuickChip("Important+",
                "Hide everything below the Important threshold.\nQuest + wishlist still bypass the filter.",
                chipSize, IsImportantMode))
            {
                Config.LootImportantOnly = true;
                Config.LootShowWishlist = true;
                Config.LootShowQuestItems = true;
                Config.MarkDirty();
            }
            ImGui.SameLine(0, gap);
            if (DrawQuickChip("Wishlist",
                "Wishlist + quest items only.\nClears category overrides so the map stays clean.",
                chipSize, IsWishlistMode))
            {
                Config.LootImportantOnly = true;
                Config.LootShowWishlist = true;
                Config.LootShowQuestItems = true;
                Config.LootShowMeds = false;
                Config.LootShowFood = false;
                Config.LootShowBackpacks = false;
                Config.LootShowKeys = false;
                Config.MarkDirty();
            }
            ImGui.SameLine(0, gap);
            if (DrawQuickChip("Quest",
                "Only quest items + quest-required loot.\nClears all other overrides.",
                chipSize, IsQuestMode))
            {
                Config.LootImportantOnly = true;
                Config.LootShowWishlist = false;
                Config.LootShowQuestItems = true;
                Config.QuestHighlightLootItems = true;
                Config.LootShowMeds = false;
                Config.LootShowFood = false;
                Config.LootShowBackpacks = false;
                Config.LootShowKeys = false;
                Config.MarkDirty();
            }
            ImGui.Spacing();
        }

        // Mode detection — used to highlight the currently-active chip.
        private static bool IsAllLootMode =>
            !Config.LootImportantOnly && !IsWishlistMode && !IsQuestMode;
        private static bool IsImportantMode =>
            Config.LootImportantOnly && Config.LootShowWishlist && Config.LootShowQuestItems
            && (Config.LootShowMeds || Config.LootShowFood || Config.LootShowBackpacks || Config.LootShowKeys);
        private static bool IsWishlistMode =>
            Config.LootImportantOnly && Config.LootShowWishlist && Config.LootShowQuestItems
            && !Config.LootShowMeds && !Config.LootShowFood && !Config.LootShowBackpacks && !Config.LootShowKeys;
        private static bool IsQuestMode =>
            Config.LootImportantOnly && !Config.LootShowWishlist && Config.LootShowQuestItems
            && Config.QuestHighlightLootItems
            && !Config.LootShowMeds && !Config.LootShowFood && !Config.LootShowBackpacks && !Config.LootShowKeys;

        private static readonly Vector4 _quickChipOnBg     = new(0.20f, 0.55f, 0.55f, 1f);
        private static readonly Vector4 _quickChipOnHover  = new(0.28f, 0.65f, 0.65f, 1f);
        private static readonly Vector4 _quickChipOnActive = new(0.18f, 0.48f, 0.48f, 1f);

        private static bool DrawQuickChip(string label, string tooltip, Vector2 size, bool isActive)
        {
            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Button,        _quickChipOnBg);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _quickChipOnHover);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  _quickChipOnActive);
            }
            bool clicked = ImGui.Button(label, size);
            if (isActive)
                ImGui.PopStyleColor(3);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
            return clicked;
        }

        // ── Search ───────────────────────────────────────────────────────────

        private static void DrawSearchSection()
        {
            UIControls.Section("Search");

            ImGui.SetNextItemWidth(-70);
            ImGui.InputTextWithHint("##LootSearch", "Search by name…", ref LootFilter.SearchText, 128);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Filter loot by item name or short name (case-insensitive)");

            ImGui.SameLine();
            if (ImGui.Button("Clear", new Vector2(-1, 0)))
                LootFilter.ClearSearch();
        }

        // ── Price ────────────────────────────────────────────────────────────

        private static void DrawPriceSection()
        {
            UIControls.Section("Price");

            // Quick-set Min chips (row of small pills).
            ImGui.TextDisabled("Quick Set Min:");
            ImGui.SameLine();
            for (int i = 0; i < LootFilter.MinPricePresets.Length; i++)
            {
                var (label, value) = LootFilter.MinPricePresets[i];
                if (i > 0) ImGui.SameLine();

                bool isActive = Config.LootMinPrice == value;
                if (isActive)
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                if (ImGui.SmallButton(label))
                {
                    Config.LootMinPrice = value;
                    Config.MarkDirty();
                }
                if (isActive)
                    ImGui.PopStyleColor();
            }

            // Steppers replace the fiddly DragInt sliders. 5K rouble step is a
            // sensible granularity for raid loot \u2014 fine enough to dial in,
            // coarse enough that you don't burn 200 clicks to reach 1M.
            int minPrice = Config.LootMinPrice;
            if (UIControls.Stepper("Min Price (\u20bd)", ref minPrice, 0, 2_000_000, 5_000, "{0:N0}",
                "Hide loot worth less than this amount.\nClick the +/- buttons or hold to repeat."))
            {
                Config.LootMinPrice = Math.Max(0, minPrice);
                Config.MarkDirty();
            }

            int importantPrice = Config.LootImportantPrice;
            if (UIControls.Stepper("Important (\u20bd)", ref importantPrice, 0, 5_000_000, 10_000, "{0:N0}",
                "Highlight loot worth at least this amount in green."))
            {
                Config.LootImportantPrice = Math.Max(0, importantPrice);
                Config.MarkDirty();
            }

            // Validation hint
            if (Config.LootMinPrice > Config.LootImportantPrice && Config.LootImportantPrice > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.3f, 1f));
                ImGui.TextWrapped("\u26a0 Min price is above important price \u2014 no items will highlight as important.");
                ImGui.PopStyleColor();
            }

            // Full-width toggle row instead of a tiny checkbox.
            bool importantOnly = Config.LootImportantOnly;
            if (UIControls.ToggleRow("Important Only", ref importantOnly,
                "Hide loot below the Important price threshold.\nWishlist, quest, and category items are still shown."))
            {
                Config.LootImportantOnly = importantOnly;
                Config.MarkDirty();
            }
        }

        // ── Categories ───────────────────────────────────────────────────────

        private static void DrawCategorySection()
        {
            UIControls.Section("Always Show");
            ImGui.TextDisabled("These categories bypass the price filter.");
            ImGui.Spacing();

            bool showMeds = Config.LootShowMeds;
            if (UIControls.ToggleRow("\u271a  Meds", ref showMeds,
                "Force medical items visible regardless of price."))
            {
                Config.LootShowMeds = showMeds;
                Config.MarkDirty();
            }

            bool showFood = Config.LootShowFood;
            if (UIControls.ToggleRow("\u2615  Food", ref showFood,
                "Force food / drink items visible regardless of price."))
            {
                Config.LootShowFood = showFood;
                Config.MarkDirty();
            }

            bool showBP = Config.LootShowBackpacks;
            if (UIControls.ToggleRow("\u25c8  Backpacks", ref showBP,
                "Force backpacks visible regardless of price."))
            {
                Config.LootShowBackpacks = showBP;
                Config.MarkDirty();
            }

            bool showKeys = Config.LootShowKeys;
            if (UIControls.ToggleRow("\u26bf  Keys", ref showKeys,
                "Force keys / keycards visible regardless of price."))
            {
                Config.LootShowKeys = showKeys;
                Config.MarkDirty();
            }

            bool showWL = Config.LootShowWishlist;
            if (UIControls.ToggleRow("\u2605  Wishlist", ref showWL,
                "Always show wishlisted items, bypassing price + category filters."))
            {
                Config.LootShowWishlist = showWL;
                Config.MarkDirty();
            }

            bool showUnk = Config.LootShowUnknownItems;
            if (UIControls.ToggleRow("\u2754  Unknown Items", ref showUnk,
                "Show loose items whose BSG ID isn't in the tarkov.dev database\n(e.g. items added by a recent patch). Rendered with a placeholder label\nshowing the BSG ID prefix so they can be identified and reported."))
            {
                Config.LootShowUnknownItems = showUnk;
                Config.MarkDirty();
            }
        }

        // ── Quest Loot Highlighting ───────────────────────────────────────────

        private static void DrawQuestHighlightSection()
        {
            if (!ImGui.CollapsingHeader("Quest Loot Highlighting"))
                return;

            ImGui.TextDisabled("Marks loose loot needed for active quests. Respects Kappa-only and selected-quest filters.");
            ImGui.Spacing();

            bool hlQuest = Config.QuestHighlightLootItems;
            if (ImGui.Checkbox("\u2731 Highlight Quest-Required Loot", ref hlQuest))
            {
                Config.QuestHighlightLootItems = hlQuest;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Mark loose items on the radar that are needed for an active quest (Find in Raid, hand-overs, etc.).");

            ImGui.Indent(16);
            bool showQI = Config.LootShowQuestItems;
            if (ImGui.Checkbox("\u2755 Show Static Quest Items", ref showQI))
            {
                Config.LootShowQuestItems = showQI;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Always show items flagged as quest-only by the game (pocket watches,\nJaeger's letter, etc.) regardless of price or active-quest filter.");
            ImGui.Unindent(16);

            ImGui.Spacing();
        }

        // ── Hideout Upgrade Loot Highlighting ──────────────────────────────────

        private static void DrawHideoutHighlightSection()
        {
            if (!ImGui.CollapsingHeader("Hideout Upgrade Highlighting"))
                return;

            ImGui.TextDisabled("Marks loose loot needed for pending hideout upgrades.\nData is cached — highlighting works during raids using last-known planner state.");
            ImGui.Spacing();

            bool hlAll = Config.HideoutHighlightLootItems;
            if (ImGui.Checkbox("🏠 Highlight Upgrade-Needed Loot", ref hlAll))
            {
                Config.HideoutHighlightLootItems = hlAll;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Highlight any loose item still needed for a pending hideout upgrade.\nUses cached planner data — active even during raids.");

            ImGui.Indent(16);
            bool hlFiR = Config.HideoutHighlightFiRItems;
            if (ImGui.Checkbox("★ FiR Only Highlight", ref hlFiR))
            {
                Config.HideoutHighlightFiRItems = hlFiR;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Highlight items that must be Found-in-Raid.\nWorks independently — you can disable the general highlight and keep only FiR items marked.");

            bool hlCurrent = Config.HideoutHighlightCurrentOnly;
            if (ImGui.Checkbox("⏳ Current Upgrades Only", ref hlCurrent))
            {
                Config.HideoutHighlightCurrentOnly = hlCurrent;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only highlight items for upgrades you can do right now.\n" +
                    "Hides items only needed for stations still locked behind another\n" +
                    "requirement (area level, trader loyalty, skill, or quest).\n" +
                    "Turn off to also mark items for those future upgrades.");
            ImGui.Unindent(16);

            ImGui.Spacing();
        }

        // ── Wishlist Settings (in-game wishlist groups) ────────────────────

        private static readonly string[] _groupLabels =
        [
            "Quests", "Hideout", "Trading", "Equipment", "Other",
        ];

        private static void DrawWishlistSettingsSection()
        {
            if (!ImGui.CollapsingHeader("Wishlist Settings"))
                return;

            // The wishlist visibility master toggle lives in the "Always Show"
            // section above (the Wishlist row). Duplicating the same
            // Config.LootShowWishlist control here made flipping one silently move
            // the other, so this section now only configures the in-game wishlist
            // source and groups.
            ImGui.TextDisabled("Visibility toggle: ★ Wishlist, under \"Always Show\" above.");
            ImGui.Spacing();

            bool useIngame = Config.LootUseIngameWishlist;
            if (ImGui.Checkbox("Use in-game wishlist", ref useIngame))
            {
                Config.LootUseIngameWishlist = useIngame;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Include items you marked as favourites inside Tarkov itself\n(read live from the in-game WishlistManager).");

            if (!Config.LootUseIngameWishlist)
                ImGui.BeginDisabled();

            ImGui.TextDisabled("Include groups:");

            // 3 + 2 layout
            bool gQuests = Config.LootWishlistGroupQuests;
            if (ImGui.Checkbox("Quests", ref gQuests)) { Config.LootWishlistGroupQuests = gQuests; Config.MarkDirty(); }
            ImGui.SameLine(0, 16);
            bool gHideout = Config.LootWishlistGroupHideout;
            if (ImGui.Checkbox("Hideout", ref gHideout)) { Config.LootWishlistGroupHideout = gHideout; Config.MarkDirty(); }
            ImGui.SameLine(0, 16);
            bool gTrading = Config.LootWishlistGroupTrading;
            if (ImGui.Checkbox("Trading", ref gTrading)) { Config.LootWishlistGroupTrading = gTrading; Config.MarkDirty(); }

            bool gEquip = Config.LootWishlistGroupEquipment;
            if (ImGui.Checkbox("Equipment", ref gEquip)) { Config.LootWishlistGroupEquipment = gEquip; Config.MarkDirty(); }
            ImGui.SameLine(0, 16);
            bool gOther = Config.LootWishlistGroupOther;
            if (ImGui.Checkbox("Other", ref gOther)) { Config.LootWishlistGroupOther = gOther; Config.MarkDirty(); }

            if (!Config.LootUseIngameWishlist)
                ImGui.EndDisabled();
        }

        // ── In-game Wishlist (live, read-only, grouped) ──────────────────────

        private static void DrawIngameWishlistSection()
        {
            var wm = Memory.WishlistManager;
            int totalCount = wm?.Items.Count ?? 0;

            if (!ImGui.CollapsingHeader($"In-game Wishlist ({totalCount})"))
                return;

            if (wm is null || totalCount == 0)
            {
                ImGui.TextDisabled("Not in raid or wishlist is empty.");
                return;
            }

            // Bucket items by group for a cleaner display.
            var buckets = new List<string>[5];
            for (int i = 0; i < 5; i++)
                buckets[i] = new List<string>(8);

            foreach (var kvp in wm.Items)
            {
                int g = kvp.Value;
                if (g >= 0 && g < 5)
                    buckets[g].Add(kvp.Key);
            }

            for (int g = 0; g < 5; g++)
            {
                var list = buckets[g];
                if (list.Count == 0)
                    continue;

                bool enabled = g switch
                {
                    0 => Config.LootWishlistGroupQuests,
                    1 => Config.LootWishlistGroupHideout,
                    2 => Config.LootWishlistGroupTrading,
                    3 => Config.LootWishlistGroupEquipment,
                    4 => Config.LootWishlistGroupOther,
                    _ => true,
                };

                string header = $"{_groupLabels[g]} ({list.Count}){(enabled ? string.Empty : " — hidden")}##wlg{g}";
                if (!ImGui.TreeNodeEx(header))
                    continue;

                list.Sort(static (a, b) =>
                    string.Compare(
                        EftDataManager.AllItems.TryGetValue(a, out var ia) ? ia.ShortName : a,
                        EftDataManager.AllItems.TryGetValue(b, out var ib) ? ib.ShortName : b,
                        StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < list.Count; i++)
                {
                    var bsgId = list[i];
                    string name = ResolveItemName(bsgId);
                    if (EftDataManager.AllItems.TryGetValue(bsgId, out var item) && item.BestPrice > 0)
                        ImGui.BulletText($"{name}  ({LootFilter.FormatPrice(item.BestPrice)})");
                    else
                        ImGui.BulletText(name);
                }

                ImGui.TreePop();
            }
        }

        // ── Wishlist / Blacklist ─────────────────────────────────────────────

        private static void DrawWishlistBlacklistSection()
        {
            if (!ImGui.CollapsingHeader("Wishlist & Blacklist"))
                return;

            var filterData = LootFilter.FilterData;

            // Item search input
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##ItemSearch", "Search items to add...", ref _itemSearchText, 128))
                _searchDirty = true;

            // Perform search
            if (_searchDirty && _itemSearchText.Length >= 2)
            {
                _searchDirty = false;
                _searchResults.Clear();
                var allItems = EftDataManager.AllItems;
                int count = 0;
                foreach (var kvp in allItems)
                {
                    if (count >= MaxSearchResults)
                        break;
                    var item = kvp.Value;
                    if (item.ShortName.Contains(_itemSearchText, StringComparison.OrdinalIgnoreCase) ||
                        item.Name.Contains(_itemSearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        _searchResults.Add(item);
                        count++;
                    }
                }
            }
            else if (_itemSearchText.Length < 2)
            {
                _searchResults.Clear();
            }

            // Search results with add buttons
            if (_searchResults.Count > 0)
            {
                ImGui.BeginChild("##ItemSearchResults", new Vector2(-1, Math.Min(_searchResults.Count * 24 + 4, 200)), ImGuiChildFlags.Borders);
                for (int i = 0; i < _searchResults.Count; i++)
                {
                    var item = _searchResults[i];
                    bool isWL = filterData.IsWishlisted(item.BsgId);
                    bool isBL = filterData.IsBlacklisted(item.BsgId);

                    // Wishlist button
                    if (isWL)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0.6f, 0.65f, 1f));
                        if (ImGui.SmallButton($"\u2605##{i}"))
                        {
                            filterData.RemoveFromWishlist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        if (ImGui.SmallButton($"+W##{i}"))
                        {
                            filterData.AddToWishlist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(isWL ? "Remove from wishlist" : "Add to wishlist");

                    ImGui.SameLine();

                    // Blacklist button
                    if (isBL)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.15f, 0.15f, 1f));
                        if (ImGui.SmallButton($"\u2717##{i}"))
                        {
                            filterData.RemoveFromBlacklist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        if (ImGui.SmallButton($"+B##{i}"))
                        {
                            filterData.AddToBlacklist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(isBL ? "Remove from blacklist" : "Add to blacklist");

                    ImGui.SameLine();
                    string priceStr = item.BestPrice > 0 ? $" ({LootFilter.FormatPrice(item.BestPrice)})" : "";
                    ImGui.Text($"{item.ShortName}{priceStr}");
                    if (ImGui.IsItemHovered() && item.Name != item.ShortName)
                        ImGui.SetTooltip(item.Name);
                }
                ImGui.EndChild();
            }

            ImGui.Spacing();

            // Current wishlist
            if (filterData.Wishlist.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0.9f, 1f, 1f));
                ImGui.Text($"\u2605 Wishlist ({filterData.Wishlist.Count})");
                ImGui.PopStyleColor();

                string? removeWL = null;
                for (int i = 0; i < filterData.Wishlist.Count; i++)
                {
                    var bsgId = filterData.Wishlist[i];
                    string name = ResolveItemName(bsgId);
                    if (ImGui.SmallButton($"\u2212##wl{i}"))
                        removeWL = bsgId;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Remove from wishlist");
                    ImGui.SameLine();
                    ImGui.Text(name);
                }
                if (removeWL is not null)
                {
                    filterData.RemoveFromWishlist(removeWL);
                    LootFilter.SaveFilterData();
                }
            }

            // Current blacklist
            if (filterData.Blacklist.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                ImGui.Text($"\u2717 Blacklist ({filterData.Blacklist.Count})");
                ImGui.PopStyleColor();

                string? removeBL = null;
                for (int i = 0; i < filterData.Blacklist.Count; i++)
                {
                    var bsgId = filterData.Blacklist[i];
                    string name = ResolveItemName(bsgId);
                    if (ImGui.SmallButton($"\u2212##bl{i}"))
                        removeBL = bsgId;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Remove from blacklist");
                    ImGui.SameLine();
                    ImGui.Text(name);
                }
                if (removeBL is not null)
                {
                    filterData.RemoveFromBlacklist(removeBL);
                    LootFilter.SaveFilterData();
                }
            }

            if (filterData.Wishlist.Count == 0 && filterData.Blacklist.Count == 0)
            {
                ImGui.TextDisabled("No wishlisted or blacklisted items.\nSearch above to add items.");
            }
        }

        /// <summary>Resolve a BSG ID to a display name.</summary>
        private static string ResolveItemName(string bsgId)
        {
            if (EftDataManager.AllItems.TryGetValue(bsgId, out var item))
                return item.ShortName;
            return bsgId;
        }

        // ── Options ──────────────────────────────────────────────────────────

        private static void DrawOptionsSection()
        {
            if (!ImGui.CollapsingHeader("Options", ImGuiTreeNodeFlags.DefaultOpen))
                return;

            ImGui.Spacing();

            // Price source \u2014 full-width combo row that's clickable end-to-end.
            int priceSource = Config.LootPriceSource;
            if (UIControls.ComboRow("Price Source", ref priceSource, LootFilter.PriceSourceLabels,
                "Which price to use for filtering and display:\n" +
                "\u2022 Best \u2014 highest of flea and trader (default)\n" +
                "\u2022 Flea Market \u2014 flea price (falls back to trader)\n" +
                "\u2022 Trader \u2014 trader price (falls back to flea)"))
            {
                Config.LootPriceSource = priceSource;
                Config.MarkDirty();
            }

            // Price per slot \u2014 toggle row.
            bool pps = Config.LootPricePerSlot;
            if (UIControls.ToggleRow("Price Per Slot", ref pps,
                "Divide price by item grid size.\nUseful for finding high-value-per-slot items."))
            {
                Config.LootPricePerSlot = pps;
                Config.MarkDirty();
            }

            // Show corpses \u2014 toggle row.
            bool showCorpses = Config.ShowCorpses;
            if (UIControls.ToggleRow("Show Corpses", ref showCorpses,
                "Show corpse X markers on the radar."))
            {
                Config.ShowCorpses = showCorpses;
                Config.MarkDirty();
            }

            if (!Config.ShowCorpses)
                ImGui.BeginDisabled();

            ImGui.Indent(16);

            bool showCorpseValue = Config.ShowCorpseValue;
            if (UIControls.ToggleRow("Show Corpse Value", ref showCorpseValue,
                "Show total gear value on the corpse tooltip and aimview label."))
            {
                Config.ShowCorpseValue = showCorpseValue;
                Config.MarkDirty();
            }

            bool showCorpseInv = Config.ShowCorpseInventory;
            if (UIControls.ToggleRow("Show Corpse Inventory", ref showCorpseInv,
                "Show the per-slot equipment list on the corpse tooltip."))
            {
                Config.ShowCorpseInventory = showCorpseInv;
                Config.MarkDirty();
            }

            ImGui.Unindent(16);

            if (!Config.ShowCorpses)
                ImGui.EndDisabled();
        }

        // ── Footer ───────────────────────────────────────────────────────────

        private static void DrawFooter()
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("\u21ba Reset Defaults"))
                LootFilter.ResetAll();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reset all loot filter settings to defaults");

            // No manual save button \u2014 every control here calls Config.MarkDirty() /
            // LootFilter.SaveFilterData() on change, matching the Settings panel's
            // auto-save model. The hint just reassures it's already persisted.
            ImGui.SameLine();
            ImGui.TextColored(UITheme.AccentGreen, "\u2713 Changes auto-saved");
        }
    }
}
