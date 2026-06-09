// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Config;

namespace eft_dma_radar.Silk.UI.Presets
{
    /// <summary>
    /// One preset-controlled toggle — the single source of truth binding a radar setting on
    /// <see cref="SilkConfig"/> to its stored value on a <see cref="RadarPresetEntry"/>.
    /// Apply / match / save / seed all iterate <see cref="PresetManager.Toggles"/>, and the
    /// Presets tab renders its checkboxes from it (grouped by <see cref="Group"/>), so adding a
    /// new preset toggle is a single entry here plus the matching property on the entry.
    /// </summary>
    internal sealed class PresetToggle
    {
        public required string Group { get; init; }
        public required string Label { get; init; }
        public required string Tooltip { get; init; }
        public required Func<SilkConfig, bool> GetCfg { get; init; }
        public required Action<SilkConfig, bool> SetCfg { get; init; }
        public required Func<RadarPresetEntry, bool> GetEntry { get; init; }
        public required Action<RadarPresetEntry, bool> SetEntry { get; init; }
    }

    /// <summary>
    /// An integer preset setting (e.g. an FPS cap), captured alongside the bool
    /// <see cref="PresetToggle"/>s. Same single-source pattern; <see cref="Min"/>/<see cref="Max"/>
    /// bound the editor slider.
    /// </summary>
    internal sealed class PresetValue
    {
        public required string Group { get; init; }
        public required string Label { get; init; }
        public required string Tooltip { get; init; }
        public required int Min { get; init; }
        public required int Max { get; init; }
        public required Func<SilkConfig, int> GetCfg { get; init; }
        public required Action<SilkConfig, int> SetCfg { get; init; }
        public required Func<RadarPresetEntry, int> GetEntry { get; init; }
        public required Action<RadarPresetEntry, int> SetEntry { get; init; }
    }

    /// <summary>
    /// Preset CRUD + apply/cycle logic. Presets are mutable user-facing records
    /// (<see cref="RadarPresetEntry"/>) persisted in <see cref="SilkConfig.Presets"/>.
    /// The 4 built-in baselines (Stealth / Loot Run / PvP / Quests) are seeded on
    /// first load and can be edited, deleted, or reset to defaults; users can
    /// also save the current toggle state as a brand-new named preset.
    /// </summary>
    internal static class PresetManager
    {
        public const string CustomId = "Custom";

        /// <summary>
        /// Single source of truth for which radar settings a preset captures, in display order
        /// (grouped). Apply / <see cref="MatchesActive"/> / save / seed iterate this; the Presets
        /// tab renders it grouped by <see cref="PresetToggle.Group"/>. Add a toggle here plus a
        /// property on <see cref="RadarPresetEntry"/> and every path picks it up automatically.
        /// </summary>
        public static readonly PresetToggle[] Toggles =
        [
            // ── General ──
            new() { Group = "General", Label = "Battle Mode", Tooltip = "Hide non-combat clutter (loot / corpses / containers).",
                GetCfg = c => c.BattleMode, SetCfg = (c, v) => c.BattleMode = v, GetEntry = p => p.BattleMode, SetEntry = (p, v) => p.BattleMode = v },

            // ── Players ──
            new() { Group = "Players", Label = "Aimlines", Tooltip = "Show direction lines on player markers.",
                GetCfg = c => c.ShowAimlines, SetCfg = (c, v) => c.ShowAimlines = v, GetEntry = p => p.ShowAimlines, SetEntry = (p, v) => p.ShowAimlines = v },
            new() { Group = "Players", Label = "Connect Groups", Tooltip = "Draw lines between players in the same group.",
                GetCfg = c => c.ConnectGroups, SetCfg = (c, v) => c.ConnectGroups = v, GetEntry = p => p.ConnectGroups, SetEntry = (p, v) => p.ConnectGroups = v },
            new() { Group = "Players", Label = "High Alert", Tooltip = "Extend aimline when an enemy is aiming at you.",
                GetCfg = c => c.HighAlert, SetCfg = (c, v) => c.HighAlert = v, GetEntry = p => p.HighAlert, SetEntry = (p, v) => p.HighAlert = v },
            new() { Group = "Players", Label = "Players On Top", Tooltip = "Draw players above loot / containers so dots stay visible.",
                GetCfg = c => c.PlayersOnTop, SetCfg = (c, v) => c.PlayersOnTop = v, GetEntry = p => p.PlayersOnTop, SetEntry = (p, v) => p.PlayersOnTop = v },

            // ── World ──
            new() { Group = "World", Label = "Exfils", Tooltip = "Show extraction points.",
                GetCfg = c => c.ShowExfils, SetCfg = (c, v) => c.ShowExfils = v, GetEntry = p => p.ShowExfils, SetEntry = (p, v) => p.ShowExfils = v },
            new() { Group = "World", Label = "Hide Inactive Exfils", Tooltip = "Hide extractions not currently available to you.",
                GetCfg = c => c.HideInactiveExfils, SetCfg = (c, v) => c.HideInactiveExfils = v, GetEntry = p => p.HideInactiveExfils, SetEntry = (p, v) => p.HideInactiveExfils = v },
            new() { Group = "World", Label = "Airdrops", Tooltip = "Show airdrop crates.",
                GetCfg = c => c.ShowAirdrops, SetCfg = (c, v) => c.ShowAirdrops = v, GetEntry = p => p.ShowAirdrops, SetEntry = (p, v) => p.ShowAirdrops = v },
            new() { Group = "World", Label = "Switches", Tooltip = "Show interactable switches / levers / buttons.",
                GetCfg = c => c.ShowSwitches, SetCfg = (c, v) => c.ShowSwitches = v, GetEntry = p => p.ShowSwitches, SetEntry = (p, v) => p.ShowSwitches = v },
            new() { Group = "World", Label = "Transits", Tooltip = "Show map-to-map transit points.",
                GetCfg = c => c.ShowTransits, SetCfg = (c, v) => c.ShowTransits = v, GetEntry = p => p.ShowTransits, SetEntry = (p, v) => p.ShowTransits = v },
            new() { Group = "World", Label = "Quest Objectives", Tooltip = "Show quest zones / objectives on the radar.",
                GetCfg = c => c.ShowQuests, SetCfg = (c, v) => c.ShowQuests = v, GetEntry = p => p.ShowQuests, SetEntry = (p, v) => p.ShowQuests = v },
            new() { Group = "World", Label = "Map Markers", Tooltip = "Show your placed map markers.",
                GetCfg = c => c.ShowMapMarkers, SetCfg = (c, v) => c.ShowMapMarkers = v, GetEntry = p => p.ShowMapMarkers, SetEntry = (p, v) => p.ShowMapMarkers = v },
            new() { Group = "World", Label = "BTR", Tooltip = "Show the BTR vehicle.",
                GetCfg = c => c.ShowBTR, SetCfg = (c, v) => c.ShowBTR = v, GetEntry = p => p.ShowBTR, SetEntry = (p, v) => p.ShowBTR = v },
            new() { Group = "World", Label = "BTR Route", Tooltip = "Show the BTR's route and stops.",
                GetCfg = c => c.ShowBTRRoute, SetCfg = (c, v) => c.ShowBTRRoute = v, GetEntry = p => p.ShowBTRRoute, SetEntry = (p, v) => p.ShowBTRRoute = v },

            // ── Doors ──
            new() { Group = "Doors", Label = "Doors", Tooltip = "Show keyed / mechanical doors.",
                GetCfg = c => c.ShowDoors, SetCfg = (c, v) => c.ShowDoors = v, GetEntry = p => p.ShowDoors, SetEntry = (p, v) => p.ShowDoors = v },
            new() { Group = "Doors", Label = "Locked Doors", Tooltip = "Show locked doors (requires Doors on).",
                GetCfg = c => c.ShowLockedDoors, SetCfg = (c, v) => c.ShowLockedDoors = v, GetEntry = p => p.ShowLockedDoors, SetEntry = (p, v) => p.ShowLockedDoors = v },
            new() { Group = "Doors", Label = "Unlocked Doors", Tooltip = "Show unlocked doors (requires Doors on).",
                GetCfg = c => c.ShowUnlockedDoors, SetCfg = (c, v) => c.ShowUnlockedDoors = v, GetEntry = p => p.ShowUnlockedDoors, SetEntry = (p, v) => p.ShowUnlockedDoors = v },

            // ── Loot ──
            new() { Group = "Loot", Label = "Loot", Tooltip = "Show loose loot items on the radar.",
                GetCfg = c => c.ShowLoot, SetCfg = (c, v) => c.ShowLoot = v, GetEntry = p => p.ShowLoot, SetEntry = (p, v) => p.ShowLoot = v },
            new() { Group = "Loot", Label = "Containers", Tooltip = "Show static loot containers.",
                GetCfg = c => c.ShowContainers, SetCfg = (c, v) => c.ShowContainers = v, GetEntry = p => p.ShowContainers, SetEntry = (p, v) => p.ShowContainers = v },
            new() { Group = "Loot", Label = "Container Names", Tooltip = "Label containers with their name.",
                GetCfg = c => c.ShowContainerNames, SetCfg = (c, v) => c.ShowContainerNames = v, GetEntry = p => p.ShowContainerNames, SetEntry = (p, v) => p.ShowContainerNames = v },
            new() { Group = "Loot", Label = "Hide Searched Containers", Tooltip = "Hide containers you've already searched.",
                GetCfg = c => c.HideSearchedContainers, SetCfg = (c, v) => c.HideSearchedContainers = v, GetEntry = p => p.HideSearchedContainers, SetEntry = (p, v) => p.HideSearchedContainers = v },
            new() { Group = "Loot", Label = "Corpses", Tooltip = "Show dead player corpses (recent kills).",
                GetCfg = c => c.ShowCorpses, SetCfg = (c, v) => c.ShowCorpses = v, GetEntry = p => p.ShowCorpses, SetEntry = (p, v) => p.ShowCorpses = v },
            new() { Group = "Loot", Label = "Corpse Value", Tooltip = "Show the rouble value on corpse markers.",
                GetCfg = c => c.ShowCorpseValue, SetCfg = (c, v) => c.ShowCorpseValue = v, GetEntry = p => p.ShowCorpseValue, SetEntry = (p, v) => p.ShowCorpseValue = v },
            new() { Group = "Loot", Label = "Corpse Inventory", Tooltip = "Show corpse inventory in its tooltip.",
                GetCfg = c => c.ShowCorpseInventory, SetCfg = (c, v) => c.ShowCorpseInventory = v, GetEntry = p => p.ShowCorpseInventory, SetEntry = (p, v) => p.ShowCorpseInventory = v },

            // ── Combat ──
            new() { Group = "Combat", Label = "Kill Feed", Tooltip = "Show the kill feed overlay.",
                GetCfg = c => c.ShowKillFeed, SetCfg = (c, v) => c.ShowKillFeed = v, GetEntry = p => p.ShowKillFeed, SetEntry = (p, v) => p.ShowKillFeed = v },
            new() { Group = "Combat", Label = "Grenades", Tooltip = "Show grenades / explosives.",
                GetCfg = c => c.ShowExplosives, SetCfg = (c, v) => c.ShowExplosives = v, GetEntry = p => p.ShowExplosives, SetEntry = (p, v) => p.ShowExplosives = v },
            new() { Group = "Combat", Label = "Tripwire Lines", Tooltip = "Draw lines for tripwire-triggered explosives.",
                GetCfg = c => c.ShowTripwireLines, SetCfg = (c, v) => c.ShowTripwireLines = v, GetEntry = p => p.ShowTripwireLines, SetEntry = (p, v) => p.ShowTripwireLines = v },
            new() { Group = "Combat", Label = "Boss-Guard ID", Tooltip = "Identify boss guards by gear (configured in the Boss Guards tab).",
                GetCfg = c => c.GuardIdentificationEnabled, SetCfg = (c, v) => c.GuardIdentificationEnabled = v, GetEntry = p => p.GuardIdentificationEnabled, SetEntry = (p, v) => p.GuardIdentificationEnabled = v },

            // ── Aimview (first-person projection widget) ──
            new() { Group = "Aimview", Label = "Aimview Widget", Tooltip = "Show the aimview widget (first-person projection of nearby players).",
                GetCfg = c => c.ShowAimview, SetCfg = (c, v) => c.ShowAimview = v, GetEntry = p => p.ShowAimview, SetEntry = (p, v) => p.ShowAimview = v },
            new() { Group = "Aimview", Label = "Loot", Tooltip = "Show filtered loot in the aimview.",
                GetCfg = c => c.AimviewShowLoot, SetCfg = (c, v) => c.AimviewShowLoot = v, GetEntry = p => p.AimviewShowLoot, SetEntry = (p, v) => p.AimviewShowLoot = v },
            new() { Group = "Aimview", Label = "Corpses", Tooltip = "Show nearby corpses in the aimview.",
                GetCfg = c => c.AimviewShowCorpses, SetCfg = (c, v) => c.AimviewShowCorpses = v, GetEntry = p => p.AimviewShowCorpses, SetEntry = (p, v) => p.AimviewShowCorpses = v },
            new() { Group = "Aimview", Label = "Containers", Tooltip = "Show containers in the aimview.",
                GetCfg = c => c.AimviewShowContainers, SetCfg = (c, v) => c.AimviewShowContainers = v, GetEntry = p => p.AimviewShowContainers, SetEntry = (p, v) => p.AimviewShowContainers = v },
            new() { Group = "Aimview", Label = "Skeleton", Tooltip = "Draw player skeletons in the aimview.",
                GetCfg = c => c.AimviewShowSkeleton, SetCfg = (c, v) => c.AimviewShowSkeleton = v, GetEntry = p => p.AimviewShowSkeleton, SetEntry = (p, v) => p.AimviewShowSkeleton = v },
            new() { Group = "Aimview", Label = "Player Labels", Tooltip = "Label players in the aimview.",
                GetCfg = c => c.AimviewShowPlayerLabels, SetCfg = (c, v) => c.AimviewShowPlayerLabels = v, GetEntry = p => p.AimviewShowPlayerLabels, SetEntry = (p, v) => p.AimviewShowPlayerLabels = v },
            new() { Group = "Aimview", Label = "Item Labels", Tooltip = "Label items in the aimview.",
                GetCfg = c => c.AimviewShowItemLabels, SetCfg = (c, v) => c.AimviewShowItemLabels = v, GetEntry = p => p.AimviewShowItemLabels, SetEntry = (p, v) => p.AimviewShowItemLabels = v },
            new() { Group = "Aimview", Label = "Hide AI Players", Tooltip = "Don't draw AI scavs/bosses in the aimview.",
                GetCfg = c => c.AimviewHideAIPlayers, SetCfg = (c, v) => c.AimviewHideAIPlayers = v, GetEntry = p => p.AimviewHideAIPlayers, SetEntry = (p, v) => p.AimviewHideAIPlayers = v },

            // ── ESP overlay ──
            new() { Group = "ESP", Label = "ESP Overlay", Tooltip = "Open the ESP overlay window.",
                GetCfg = c => c.ShowEspWidget, SetCfg = (c, v) => c.ShowEspWidget = v, GetEntry = p => p.ShowEspWidget, SetEntry = (p, v) => p.ShowEspWidget = v },
            new() { Group = "ESP", Label = "Players", Tooltip = "Draw player boxes/labels on the ESP overlay.",
                GetCfg = c => c.EspShowPlayers, SetCfg = (c, v) => c.EspShowPlayers = v, GetEntry = p => p.EspShowPlayers, SetEntry = (p, v) => p.EspShowPlayers = v },
            new() { Group = "ESP", Label = "Loot", Tooltip = "Draw loot labels on the ESP overlay.",
                GetCfg = c => c.EspShowLoot, SetCfg = (c, v) => c.EspShowLoot = v, GetEntry = p => p.EspShowLoot, SetEntry = (p, v) => p.EspShowLoot = v },
            new() { Group = "ESP", Label = "Bones", Tooltip = "Draw player skeletons on the ESP overlay.",
                GetCfg = c => c.EspShowBones, SetCfg = (c, v) => c.EspShowBones = v, GetEntry = p => p.EspShowBones, SetEntry = (p, v) => p.EspShowBones = v },
            new() { Group = "ESP", Label = "Crosshair", Tooltip = "Draw a center crosshair on the ESP overlay.",
                GetCfg = c => c.EspShowCrosshair, SetCfg = (c, v) => c.EspShowCrosshair = v, GetEntry = p => p.EspShowCrosshair, SetEntry = (p, v) => p.EspShowCrosshair = v },
            new() { Group = "ESP", Label = "FPS Counter", Tooltip = "Show the FPS counter on the ESP overlay.",
                GetCfg = c => c.EspShowFps, SetCfg = (c, v) => c.EspShowFps = v, GetEntry = p => p.EspShowFps, SetEntry = (p, v) => p.EspShowFps = v },
            new() { Group = "ESP", Label = "Status Text", Tooltip = "Show status text on the ESP overlay.",
                GetCfg = c => c.EspShowStatusText, SetCfg = (c, v) => c.EspShowStatusText = v, GetEntry = p => p.EspShowStatusText, SetEntry = (p, v) => p.EspShowStatusText = v },
            new() { Group = "ESP", Label = "Energy/Hydration", Tooltip = "Show your energy/hydration on the ESP overlay.",
                GetCfg = c => c.EspShowEnergyHydration, SetCfg = (c, v) => c.EspShowEnergyHydration = v, GetEntry = p => p.EspShowEnergyHydration, SetEntry = (p, v) => p.EspShowEnergyHydration = v },
        ];

        /// <summary>
        /// Non-bool preset settings (integer sliders), captured alongside <see cref="Toggles"/>.
        /// Same single-source pattern: add an entry here + a property on <see cref="RadarPresetEntry"/>.
        /// </summary>
        public static readonly PresetValue[] Values =
        [
            new() { Group = "Performance", Label = "Radar FPS", Tooltip = "Radar render frame-rate cap.", Min = 30, Max = 300,
                GetCfg = c => c.TargetFps, SetCfg = (c, v) => c.TargetFps = v, GetEntry = p => p.TargetFps, SetEntry = (p, v) => p.TargetFps = v },
            new() { Group = "Performance", Label = "Position FPS", Tooltip = "Realtime worker rate (player-position smoothness vs CPU).",
                Min = SilkConfig.RealtimeTargetFpsMin, Max = SilkConfig.RealtimeTargetFpsMax,
                GetCfg = c => c.RealtimeTargetFps, SetCfg = (c, v) => c.RealtimeTargetFps = v, GetEntry = p => p.RealtimeTargetFps, SetEntry = (p, v) => p.RealtimeTargetFps = v },
            new() { Group = "Performance", Label = "ESP FPS", Tooltip = "ESP overlay frame-rate cap (0 = uncapped).", Min = 0, Max = 360,
                GetCfg = c => c.EspTargetFps, SetCfg = (c, v) => c.EspTargetFps = v, GetEntry = p => p.EspTargetFps, SetEntry = (p, v) => p.EspTargetFps = v },
        ];

        /// <summary>
        /// Hardcoded baselines as fully-specified entries — seeds on first load and the reset
        /// target when the user clicks "Reset to defaults" on a built-in. Each is intentionally
        /// distinct: Stealth = silent extract, Loot Run = max info, PvP = hunter mode, Quests = objectives.
        /// </summary>
        public static readonly RadarPresetEntry[] BuiltInDefaults =
        [
            new()
            {
                Id = "Stealth", DisplayName = "Stealth", BaselineId = "Stealth",
                Description = "Silent extract: clean map, only what's needed to evade and exfil. " +
                    "No loot / corpses / containers, doors and switches off.",
                BattleMode = true, ShowLoot = false, ShowCorpses = false, ShowContainers = false,
                ShowExfils = true, ShowDoors = false, ShowAirdrops = false, ShowSwitches = false,
                ShowTransits = true, ShowAimlines = true, ConnectGroups = true, HighAlert = true, PlayersOnTop = true,
                ShowQuests = false, ShowMapMarkers = true, ShowBTR = true, ShowBTRRoute = false,
                HideInactiveExfils = true, ShowKillFeed = false, ShowExplosives = true, ShowTripwireLines = true,
                GuardIdentificationEnabled = true, ShowLockedDoors = false, ShowUnlockedDoors = false,
                ShowCorpseValue = false, ShowCorpseInventory = false, ShowContainerNames = false, HideSearchedContainers = true,
                ShowAimview = false, RealtimeTargetFps = 90, EspTargetFps = 60,
            },
            new()
            {
                Id = "LootRun", DisplayName = "Loot Run", BaselineId = "LootRun",
                Description = "Max info: every world entity visible. Players drawn below loot " +
                    "so dots stay readable. High Alert off to reduce visual noise.",
                BattleMode = false, ShowLoot = true, ShowCorpses = true, ShowContainers = true,
                ShowExfils = true, ShowDoors = true, ShowAirdrops = true, ShowSwitches = true,
                ShowTransits = true, ShowAimlines = true, ConnectGroups = true, HighAlert = false, PlayersOnTop = false,
                ShowQuests = true, ShowMapMarkers = true, ShowBTR = true, ShowBTRRoute = true,
                HideInactiveExfils = false, ShowKillFeed = true, ShowExplosives = true, ShowTripwireLines = true,
                GuardIdentificationEnabled = true, ShowLockedDoors = true, ShowUnlockedDoors = true,
                ShowCorpseValue = true, ShowCorpseInventory = true, ShowContainerNames = true, HideSearchedContainers = false,
            },
            new()
            {
                Id = "PvP", DisplayName = "PvP", BaselineId = "PvP",
                Description = "Hunter mode. Corpses on (active engagement zones), airdrops on " +
                    "(PvP magnets), doors on (key-room fights). Players on top so " +
                    "combat callouts are always readable.",
                BattleMode = true, ShowLoot = false, ShowCorpses = true, ShowContainers = false,
                ShowExfils = true, ShowDoors = true, ShowAirdrops = true, ShowSwitches = false,
                ShowTransits = false, ShowAimlines = true, ConnectGroups = true, HighAlert = true, PlayersOnTop = true,
                ShowQuests = false, ShowMapMarkers = true, ShowBTR = true, ShowBTRRoute = false,
                HideInactiveExfils = true, ShowKillFeed = true, ShowExplosives = true, ShowTripwireLines = true,
                GuardIdentificationEnabled = true, ShowLockedDoors = false, ShowUnlockedDoors = false,
                ShowCorpseValue = true, ShowCorpseInventory = false, ShowContainerNames = false, HideSearchedContainers = true,
                AimviewShowLoot = false, AimviewShowContainers = false, AimviewShowItemLabels = false, AimviewHideAIPlayers = true,
                TargetFps = 144, RealtimeTargetFps = 144,
            },
            new()
            {
                Id = "Quests", DisplayName = "Quests", BaselineId = "Quests",
                Description = "Objective focus. Loot on (quest items), doors and switches on " +
                    "(mechanics). Corpses and containers off to reduce visual noise. " +
                    "Airdrops off because they pull people away from objectives.",
                BattleMode = false, ShowLoot = true, ShowCorpses = false, ShowContainers = false,
                ShowExfils = true, ShowDoors = true, ShowAirdrops = false, ShowSwitches = true,
                ShowTransits = true, ShowAimlines = true, ConnectGroups = true, HighAlert = true, PlayersOnTop = false,
                ShowQuests = true, ShowMapMarkers = true, ShowBTR = true, ShowBTRRoute = true,
                HideInactiveExfils = true, ShowKillFeed = false, ShowExplosives = true, ShowTripwireLines = true,
                GuardIdentificationEnabled = true, ShowLockedDoors = true, ShowUnlockedDoors = true,
                ShowCorpseValue = false, ShowCorpseInventory = false, ShowContainerNames = true, HideSearchedContainers = true,
            },
        ];

        /// <summary>
        /// Seed <see cref="SilkConfig.Presets"/> with the built-in defaults if empty. A deleted
        /// built-in is NOT auto-restored — deletion is permanent until the whole list is reset.
        /// </summary>
        public static void EnsureSeeded(SilkConfig cfg)
        {
            cfg.Presets ??= [];
            if (cfg.Presets.Count != 0)
                return;
            foreach (var b in BuiltInDefaults)
                cfg.Presets.Add(CloneEntry(b));
        }

        /// <summary>Deep-copies a preset entry (metadata + all toggles via <see cref="Toggles"/>).</summary>
        private static RadarPresetEntry CloneEntry(RadarPresetEntry src)
        {
            var e = new RadarPresetEntry
            {
                Id = src.Id,
                DisplayName = src.DisplayName,
                Description = src.Description,
                BaselineId = src.BaselineId,
            };
            foreach (var t in Toggles)
                t.SetEntry(e, t.GetEntry(src));
            foreach (var v in Values)
                v.SetEntry(e, v.GetEntry(src));
            return e;
        }

        public static RadarPresetEntry? Find(SilkConfig cfg, string id)
        {
            if (cfg.Presets is null) return null;
            for (int i = 0; i < cfg.Presets.Count; i++)
                if (cfg.Presets[i].Id == id) return cfg.Presets[i];
            return null;
        }

        /// <summary>Display name for any id, including <c>Custom</c>.</summary>
        public static string DisplayNameFor(SilkConfig cfg, string id)
            => id == CustomId ? "Custom" : (Find(cfg, id)?.DisplayName ?? id);

        /// <summary>
        /// All selectable preset ids in cycle order (saved presets + Custom).
        /// </summary>
        public static IReadOnlyList<string> AllIds(SilkConfig cfg)
        {
            EnsureSeeded(cfg);
            var ids = new List<string>(cfg.Presets!.Count + 1);
            foreach (var p in cfg.Presets!) ids.Add(p.Id);
            ids.Add(CustomId);
            return ids;
        }

        /// <summary>
        /// Apply the named preset to the given config and persist it.
        /// <paramref name="cfg"/>'s <see cref="SilkConfig.ActivePresetId"/> is updated.
        /// "Custom" is a no-op — it represents the user's current drift state.
        /// </summary>
        public static void Apply(string id, SilkConfig cfg)
        {
            if (id == CustomId)
            {
                cfg.ActivePresetId = CustomId;
                cfg.MarkDirty();
                return;
            }

            var preset = Find(cfg, id);
            if (preset is null)
                return;

            foreach (var t in Toggles)
                t.SetCfg(cfg, t.GetEntry(preset));
            foreach (var v in Values)
                v.SetCfg(cfg, v.GetEntry(preset));
            cfg.ActivePresetId = preset.Id;
            cfg.MarkDirty();
            eft_dma_radar.Silk.UI.Shell.ToastManager.Info($"Preset: {preset.DisplayName}");
        }

        /// <summary>
        /// Cycle the active preset by <paramref name="delta"/> slots
        /// (e.g. +1 for next, -1 for previous). Wraps around.
        /// </summary>
        public static void Cycle(int delta, SilkConfig cfg)
        {
            var ids = AllIds(cfg);
            int idx = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == cfg.ActivePresetId)
                {
                    idx = i;
                    break;
                }
            }
            int next = ((idx + delta) % ids.Count + ids.Count) % ids.Count;
            Apply(ids[next], cfg);
        }

        /// <summary>
        /// Returns true if the current live toggles still match the stored values
        /// of the active preset. If they don't, the user has drifted into "Custom".
        /// </summary>
        public static bool MatchesActive(SilkConfig cfg)
        {
            if (cfg.ActivePresetId == CustomId)
                return true;

            var p = Find(cfg, cfg.ActivePresetId);
            if (p is null)
                return false;

            foreach (var t in Toggles)
                if (t.GetCfg(cfg) != t.GetEntry(p))
                    return false;
            foreach (var v in Values)
                if (v.GetCfg(cfg) != v.GetEntry(p))
                    return false;
            return true;
        }

        /// <summary>
        /// Demotes the active preset to "Custom" if the user has drifted
        /// from the stored values. Called once per frame from the UI.
        /// </summary>
        public static void ReconcileDrift(SilkConfig cfg)
        {
            if (cfg.ActivePresetId == CustomId)
                return;
            if (!MatchesActive(cfg))
            {
                cfg.ActivePresetId = CustomId;
                cfg.MarkDirty();
            }
        }

        // ── Management ────────────────────────────────────────────────────────

        /// <summary>True if the preset originated from a hardcoded baseline (can be reset).</summary>
        public static bool IsBuiltin(RadarPresetEntry p) => !string.IsNullOrEmpty(p.BaselineId);

        /// <summary>
        /// Copy the current radar toggle state into a new preset with the given name.
        /// Returns the new entry (or null if name is empty / collides).
        /// </summary>
        public static RadarPresetEntry? SaveCurrentAsNew(SilkConfig cfg, string name, string description = "")
        {
            EnsureSeeded(cfg);
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            string id = GenerateUniqueId(cfg, name);
            var entry = new RadarPresetEntry
            {
                Id = id,
                DisplayName = name,
                Description = description ?? "",
                BaselineId = null, // user-created — no reset target
            };
            foreach (var t in Toggles)
                t.SetEntry(entry, t.GetCfg(cfg));
            foreach (var v in Values)
                v.SetEntry(entry, v.GetCfg(cfg));
            cfg.Presets!.Add(entry);
            cfg.ActivePresetId = id;
            cfg.MarkDirty();
            return entry;
        }

        /// <summary>
        /// Overwrite the stored toggle values of <paramref name="id"/> with the
        /// current live radar state. Used by "Save changes" after the user
        /// tweaks live toggles and wants to bake them into the active preset.
        /// </summary>
        public static bool UpdateInPlace(SilkConfig cfg, string id)
        {
            var p = Find(cfg, id);
            if (p is null) return false;
            foreach (var t in Toggles)
                t.SetEntry(p, t.GetCfg(cfg));
            foreach (var v in Values)
                v.SetEntry(p, v.GetCfg(cfg));
            cfg.ActivePresetId = id;
            cfg.MarkDirty();
            return true;
        }

        public static bool Rename(SilkConfig cfg, string id, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return false;
            var p = Find(cfg, id);
            if (p is null) return false;
            p.DisplayName = newName.Trim();
            cfg.MarkDirty();
            return true;
        }

        public static bool SetDescription(SilkConfig cfg, string id, string description)
        {
            var p = Find(cfg, id);
            if (p is null) return false;
            p.Description = description ?? "";
            cfg.MarkDirty();
            return true;
        }

        public static bool Delete(SilkConfig cfg, string id)
        {
            if (cfg.Presets is null) return false;
            int idx = cfg.Presets.FindIndex(e => e.Id == id);
            if (idx < 0) return false;
            cfg.Presets.RemoveAt(idx);
            if (cfg.ActivePresetId == id)
                cfg.ActivePresetId = CustomId;
            cfg.MarkDirty();
            return true;
        }

        /// <summary>
        /// Restore a preset's stored values from its hardcoded baseline.
        /// Returns false if the preset has no baseline (i.e. it was user-created).
        /// </summary>
        public static bool ResetToDefaults(SilkConfig cfg, string id)
        {
            var p = Find(cfg, id);
            if (p is null || string.IsNullOrEmpty(p.BaselineId)) return false;
            var b = System.Array.Find(BuiltInDefaults, x => x.Id == p.BaselineId);
            if (b is null) return false;
            p.DisplayName = b.DisplayName;
            p.Description = b.Description;
            foreach (var t in Toggles)
                t.SetEntry(p, t.GetEntry(b));
            foreach (var v in Values)
                v.SetEntry(p, v.GetEntry(b));
            cfg.MarkDirty();
            return true;
        }

        private static string GenerateUniqueId(SilkConfig cfg, string baseName)
        {
            // Slug from name, then append a counter if it collides.
            string slug = new(baseName.Where(char.IsLetterOrDigit).ToArray());
            if (slug.Length == 0) slug = "Preset";
            string id = slug;
            int n = 2;
            while (cfg.Presets!.Any(p => p.Id == id) || id == CustomId)
            {
                id = $"{slug}{n++}";
            }
            return id;
        }
    }
}
