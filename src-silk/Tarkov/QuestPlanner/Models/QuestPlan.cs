// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.QuestPlanner.Models
{
    /// <summary>
    /// Per-objective data within a quest plan, carrying completion state from memory.
    /// </summary>
    internal sealed record ObjectiveInfo(
        string Id,
        string Description,
        bool IsCompleted,
        int CurrentCount = 0,
        int TargetCount = 0,
        string Type = "")
    {
        public bool HasProgress => TargetCount > 1;
        public string ProgressText => HasProgress ? $"{CurrentCount}/{TargetCount}" : string.Empty;
    }

    /// <summary>
    /// A find-in-raid item pair collapsed into a single progress row for the "Find in raid" category.
    /// </summary>
    internal sealed record FirItemInfo(
        string QuestName,
        string ItemShortName,
        int CurrentCount,
        int TargetCount)
    {
        public string ProgressText => $"{CurrentCount}/{TargetCount}";
    }

    /// <summary>
    /// A quest where the only remaining incomplete objectives are giveQuestItem —
    /// the player has the item but hasn't handed it to the trader yet.
    /// </summary>
    internal sealed record HandOverItemInfo(
        string QuestName,
        string ItemShortName)
    {
        public string DisplayText => $"{QuestName} \u2014 Hand over {ItemShortName}";
    }

    /// <summary>
    /// Per-quest data within a map plan: objectives on this map and items to bring.
    /// </summary>
    internal sealed class QuestPlan
    {
        /// <summary>BSG task id — used to drive radar selection (Config.QuestSelectedId).</summary>
        public string TaskId { get; init; } = string.Empty;

        public string QuestName { get; init; } = string.Empty;
        public IReadOnlyList<ObjectiveInfo> Objectives { get; init; } = [];

        /// <summary>Items to bring in — excludes FIR items that must be found/handed during raid.</summary>
        public IReadOnlyList<BringItem> BringItems { get; init; } = [];

        // ── Metadata (from tarkov.dev) ──────────────────────────────────────
        public string TraderName { get; init; } = string.Empty;
        public bool KappaRequired { get; init; }
        public bool LightkeeperRequired { get; init; }

        /// <summary>Minimum PMC level to start the quest (0 = none).</summary>
        public int MinPlayerLevel { get; init; }

        /// <summary>Wiki URL for the quest, if known.</summary>
        public string WikiLink { get; init; } = string.Empty;

        /// <summary>Names of quests this quest unlocks on completion (dependency chain).</summary>
        public IReadOnlyList<string> Unlocks { get; init; } = [];
    }
}
