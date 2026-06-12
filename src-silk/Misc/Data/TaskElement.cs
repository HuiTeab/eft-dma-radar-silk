// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Misc.Data
{
    /// <summary>
    /// Quest/task data from the tarkov.dev API (embedded in DEFAULT_DATA.json).
    /// </summary>
    internal sealed class TaskElement
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;

        [JsonPropertyName("trader")]
        public TraderRef? Trader { get; set; }

        [JsonPropertyName("map")]
        public BasicRef? Map { get; set; }

        [JsonPropertyName("kappaRequired")]
        public bool KappaRequired { get; set; }

        [JsonPropertyName("lightkeeperRequired")]
        public bool LightkeeperRequired { get; set; }

        /// <summary>Minimum PMC level required to start the task (0 = none).</summary>
        [JsonPropertyName("minPlayerLevel")]
        public int MinPlayerLevel { get; set; }

        /// <summary>XP reward for completing the task.</summary>
        [JsonPropertyName("experience")]
        public int Experience { get; set; }

        /// <summary>tarkov.dev wiki URL for the task.</summary>
        [JsonPropertyName("wikiLink")]
        public string WikiLink { get; set; } = string.Empty;

        /// <summary>Faction gate: "Any", "USEC", or "BEAR".</summary>
        [JsonPropertyName("factionName")]
        public string FactionName { get; set; } = string.Empty;

        [JsonPropertyName("objectives")]
        public List<ObjectiveElement>? Objectives { get; set; }

        [JsonPropertyName("taskRequirements")]
        public List<TaskRequirementElement>? TaskRequirements { get; set; }

        /// <summary>What completing the task grants — items, trader rep, offer/trader unlocks.</summary>
        [JsonPropertyName("finishRewards")]
        public FinishRewardsElement? FinishRewards { get; set; }

        internal sealed class FinishRewardsElement
        {
            [JsonPropertyName("items")]
            public List<RewardItemElement>? Items { get; set; }

            [JsonPropertyName("traderStanding")]
            public List<TraderStandingElement>? TraderStanding { get; set; }

            [JsonPropertyName("offerUnlock")]
            public List<OfferUnlockElement>? OfferUnlock { get; set; }

            [JsonPropertyName("traderUnlock")]
            public List<TraderRef>? TraderUnlock { get; set; }
        }

        internal sealed class RewardItemElement
        {
            [JsonPropertyName("item")]
            public ItemRef? Item { get; set; }

            /// <summary>GraphQL Float (ContainedItem.count) — keep double so a fractional
            /// value can't break deserialization of the whole payload.</summary>
            [JsonPropertyName("count")]
            public double Count { get; set; }
        }

        internal sealed class TraderStandingElement
        {
            [JsonPropertyName("trader")]
            public TraderRef? Trader { get; set; }

            [JsonPropertyName("standing")]
            public float Standing { get; set; }
        }

        internal sealed class OfferUnlockElement
        {
            [JsonPropertyName("trader")]
            public TraderRef? Trader { get; set; }

            [JsonPropertyName("level")]
            public int Level { get; set; }

            [JsonPropertyName("item")]
            public ItemRef? Item { get; set; }
        }

        internal sealed class TraderRef
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        internal sealed class BasicRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;
        }

        internal sealed class TaskRequirementElement
        {
            [JsonPropertyName("task")]
            public TaskRef? Task { get; set; }

            [JsonPropertyName("status")]
            public List<string>? Status { get; set; }
        }

        internal sealed class TaskRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;
        }

        internal sealed class ObjectiveElement
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("optional")]
            public bool Optional { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;

            [JsonPropertyName("requiredKeys")]
            public List<List<ItemRef>>? RequiredKeys { get; set; }

            [JsonPropertyName("maps")]
            public List<BasicRef>? Maps { get; set; }

            [JsonPropertyName("zones")]
            public List<ZoneElement>? Zones { get; set; }

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("foundInRaid")]
            public bool FoundInRaid { get; set; }

            [JsonPropertyName("item")]
            public ItemRef? Item { get; set; }

            [JsonPropertyName("questItem")]
            public ItemRef? QuestItem { get; set; }

            [JsonPropertyName("markerItem")]
            public ItemRef? MarkerItem { get; set; }
        }

        internal sealed class ItemRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("shortName")]
            public string ShortName { get; set; } = string.Empty;
        }

        internal sealed class ZoneElement
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("outline")]
            public List<PositionElement>? Outline { get; set; }

            [JsonPropertyName("position")]
            public PositionElement? Position { get; set; }

            [JsonPropertyName("map")]
            public BasicRef? Map { get; set; }
        }

        internal sealed class PositionElement
        {
            [JsonPropertyName("x")]
            public float X { get; set; }

            [JsonPropertyName("y")]
            public float Y { get; set; }

            [JsonPropertyName("z")]
            public float Z { get; set; }
        }
    }
}
