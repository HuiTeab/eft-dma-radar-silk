// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.UI.Maps;

namespace eft_dma_radar.Silk.Web.Data
{
    /// <summary>
    /// Flattened shared map-marker snapshot for the buddy web radar. Only shared/global
    /// markers for the current map are broadcast; local markers never leave the host.
    /// </summary>
    public sealed class WebRadarMarker
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Color { get; set; } = "#FFB300";
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }
        public string? CreatedBy { get; set; }

        internal static WebRadarMarker Create(MapMarker m) => new()
        {
            Id = m.Id,
            Label = m.Label,
            Color = m.Color,
            WorldX = m.X,
            WorldY = m.Y,
            WorldZ = m.Z,
            CreatedBy = m.CreatedBy,
        };
    }
}
