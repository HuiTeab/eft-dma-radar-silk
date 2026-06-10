// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.QuestPlanner
{
    /// <summary>
    /// Runtime settings that influence how the quest planner scores/filters quests.
    /// Persisted in SilkConfig.
    /// </summary>
    /// <summary>Map ordering strategy for the session plan.</summary>
    internal enum QuestPlannerSort
    {
        /// <summary>Dependency-aware recommendation (default).</summary>
        Recommended = 0,
        /// <summary>Most completable objectives first.</summary>
        Objectives = 1,
        /// <summary>Most follow-up quests unlocked first.</summary>
        Unlocks = 2,
    }

    internal sealed class QuestPlannerSettings
    {
        /// <summary>Restrict completable objectives to Kappa-required quests only.</summary>
        public bool KappaFilter { get; set; }

        /// <summary>Restrict completable objectives to Lightkeeper-required quests only.</summary>
        public bool LightkeeperFilter { get; set; }

        /// <summary>
        /// When non-empty, only quests for this trader (by display name) are kept.
        /// Compared case-insensitively.
        /// </summary>
        public string? TraderFilter { get; set; }

        /// <summary>When &gt; 0, hide quests whose <c>minPlayerLevel</c> exceeds this value.</summary>
        public int MaxPlayerLevel { get; set; }

        /// <summary>Free-text filter over quest name and objective description. Null/empty = no filter.</summary>
        public string? SearchText { get; set; }

        /// <summary>Display ordering for the map list.</summary>
        public QuestPlannerSort Sort { get; set; } = QuestPlannerSort.Recommended;
    }
}
