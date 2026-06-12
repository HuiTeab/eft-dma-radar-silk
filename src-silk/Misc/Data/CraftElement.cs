// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Misc.Data
{
    /// <summary>
    /// A hideout craft recipe from tarkov.dev. Shared shape between the GraphQL
    /// response and the data.json disk cache (same pattern as <see cref="TaskElement"/>).
    /// Cross-referenced with live DMA station levels by the Hideout panel's Crafts section.
    /// </summary>
    internal sealed class CraftElement
    {
        [JsonPropertyName("station")]
        public StationRef? Station { get; set; }

        /// <summary>Station level required to run this craft.</summary>
        [JsonPropertyName("level")]
        public int Level { get; set; }

        /// <summary>Craft duration in seconds.</summary>
        [JsonPropertyName("duration")]
        public int DurationSeconds { get; set; }

        [JsonPropertyName("requiredItems")]
        public List<CraftItemElement>? RequiredItems { get; set; }

        [JsonPropertyName("rewardItems")]
        public List<CraftItemElement>? RewardItems { get; set; }

        internal sealed class StationRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;
        }

        internal sealed class CraftItemElement
        {
            [JsonPropertyName("item")]
            public ItemRef? Item { get; set; }

            /// <summary>
            /// GraphQL Float — fractional for drained resources, e.g. the Water
            /// Collector's purified-water craft uses 0.66 of a Water filter.
            /// An int here breaks deserialization of the whole payload.
            /// </summary>
            [JsonPropertyName("count")]
            public double Count { get; set; }

            [JsonPropertyName("attributes")]
            public List<AttributeElement>? Attributes { get; set; }

            /// <summary>Tools are required but returned after the craft — excluded from cost.</summary>
            [JsonIgnore]
            public bool IsTool
            {
                get
                {
                    if (Attributes is null) return false;
                    for (int i = 0; i < Attributes.Count; i++)
                        if (string.Equals(Attributes[i].Type, "tool", StringComparison.OrdinalIgnoreCase))
                            return true;
                    return false;
                }
            }
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

        internal sealed class AttributeElement
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("value")]
            public string Value { get; set; } = string.Empty;
        }
    }
}
