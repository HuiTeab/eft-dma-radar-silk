// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Threading;

namespace eft_dma_radar.Silk.UI.Maps
{
    /// <summary>
    /// A user-placed point of interest on a specific map. Persisted across raids in
    /// <see cref="SilkConfig.MapMarkers"/> and keyed per map id, so a marker dropped on
    /// Customs reappears every Customs raid.
    ///
    /// Two scopes:
    ///   • Local  (<see cref="Shared"/> == false) — visible only on this radar.
    ///   • Shared (<see cref="Shared"/> == true)  — broadcast to every web-radar buddy
    ///     in the <c>/api/radar</c> payload and editable by buddies via <c>/api/markers</c>.
    /// </summary>
    public sealed class MapMarker
    {
        /// <summary>Stable unique id (GUID "N" format).</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>Primary map id this marker belongs to (e.g. "bigmap", "interchange").</summary>
        [JsonPropertyName("mapId")]
        public string MapId { get; set; } = "";

        [JsonPropertyName("x")] public float X { get; set; }
        [JsonPropertyName("y")] public float Y { get; set; }
        [JsonPropertyName("z")] public float Z { get; set; }

        /// <summary>User-facing label drawn next to the marker (may be empty).</summary>
        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        /// <summary>Marker colour as an "#RRGGBB" hex string.</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#FFB300";

        /// <summary>True = shared/global (broadcast to web buddies); false = local-only.</summary>
        [JsonPropertyName("shared")]
        public bool Shared { get; set; }

        /// <summary>Origin tag ("host" / "web") — informational only.</summary>
        [JsonPropertyName("createdBy")]
        public string? CreatedBy { get; set; }

        /// <summary>Creation time (unix ms) — used for stable ordering / display.</summary>
        [JsonPropertyName("createdAt")]
        public long CreatedAt { get; set; }

        [JsonIgnore]
        public Vector3 Position => new(X, Y, Z);
    }

    /// <summary>
    /// Thread-safe store for <see cref="MapMarker"/>s, shared between the render thread,
    /// the web-radar worker, and ASP.NET request threads (web buddies adding markers).
    ///
    /// Uses copy-on-write: every mutation publishes a brand-new immutable list reference,
    /// so readers (render loop / web tick) iterate a snapshot lock-free and config
    /// serialization never races against an in-progress mutation. The published list is
    /// also assigned back onto <see cref="SilkConfig.MapMarkers"/> so it persists to disk.
    /// </summary>
    internal static class MapMarkerManager
    {
        /// <summary>Hard cap to keep the config file and web payload bounded.</summary>
        internal const int MaxMarkers = 250;

        private const int MaxLabelLength = 64;
        private const string DefaultColor = "#FFB300";

        private static readonly Lock _lock = new();
        private static volatile List<MapMarker> _all = new();
        private static volatile bool _initialized;

        /// <summary>Lock-free snapshot of all markers (every scope, every map).</summary>
        internal static IReadOnlyList<MapMarker> All
        {
            get
            {
                EnsureInit();
                return _all;
            }
        }

        /// <summary>
        /// Seeds the in-memory list from the loaded config. Safe to call multiple times
        /// (idempotent) and lazily invoked by every accessor so callers never observe an
        /// empty store just because explicit init was skipped.
        /// </summary>
        internal static void Initialize() => EnsureInit();

        private static void EnsureInit()
        {
            if (_initialized)
                return;
            lock (_lock)
            {
                if (_initialized)
                    return;
                var stored = SilkProgram.Config?.MapMarkers;
                _all = stored is { Count: > 0 } ? new List<MapMarker>(stored) : new List<MapMarker>();
                _initialized = true;
            }
        }

        /// <summary>Publishes a new list reference and persists it to config (caller holds <see cref="_lock"/>).</summary>
        private static void Commit(List<MapMarker> next)
        {
            _all = next;
            var cfg = SilkProgram.Config;
            if (cfg is not null)
            {
                cfg.MapMarkers = next;
                cfg.MarkDirty();
            }
        }

        /// <summary>Adds a new marker and returns it.</summary>
        internal static MapMarker Add(string mapId, Vector3 pos, string? label, string? color, bool shared, string? createdBy = null)
        {
            var marker = new MapMarker
            {
                Id = Guid.NewGuid().ToString("N"),
                MapId = mapId ?? "",
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                Label = Sanitize(label),
                Color = NormalizeColor(color),
                Shared = shared,
                CreatedBy = createdBy,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            EnsureInit();
            lock (_lock)
            {
                var next = new List<MapMarker>(_all) { marker };
                // Cap — drop the oldest entries if we somehow exceed the limit.
                if (next.Count > MaxMarkers)
                    next.RemoveRange(0, next.Count - MaxMarkers);
                Commit(next);
            }
            return marker;
        }

        /// <summary>Updates the label / colour / scope of an existing marker.</summary>
        internal static bool Update(string id, string? label, string? color, bool shared)
        {
            EnsureInit();
            lock (_lock)
            {
                int idx = _all.FindIndex(m => m.Id == id);
                if (idx < 0)
                    return false;
                var src = _all[idx];
                var next = new List<MapMarker>(_all);
                next[idx] = new MapMarker
                {
                    Id = src.Id,
                    MapId = src.MapId,
                    X = src.X,
                    Y = src.Y,
                    Z = src.Z,
                    Label = Sanitize(label),
                    Color = NormalizeColor(color),
                    Shared = shared,
                    CreatedBy = src.CreatedBy,
                    CreatedAt = src.CreatedAt,
                };
                Commit(next);
                return true;
            }
        }

        /// <summary>Removes a marker by id.</summary>
        internal static bool Remove(string id)
        {
            EnsureInit();
            lock (_lock)
            {
                var next = new List<MapMarker>(_all);
                if (next.RemoveAll(m => m.Id == id) == 0)
                    return false;
                Commit(next);
                return true;
            }
        }

        /// <summary>
        /// Removes every marker on <paramref name="mapId"/> matching <paramref name="sharedScope"/>
        /// (null = both scopes). Returns the number removed.
        /// </summary>
        internal static int ClearMap(string mapId, bool? sharedScope)
        {
            if (string.IsNullOrEmpty(mapId))
                return 0;
            EnsureInit();
            lock (_lock)
            {
                var next = new List<MapMarker>(_all);
                int removed = next.RemoveAll(m =>
                    m.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase)
                    && (sharedScope is null || m.Shared == sharedScope.Value));
                if (removed == 0)
                    return 0;
                Commit(next);
                return removed;
            }
        }

        /// <summary>Returns a filtered copy of the markers on a given map.</summary>
        internal static List<MapMarker> GetForMap(string mapId, bool includeLocal, bool includeShared)
        {
            var result = new List<MapMarker>();
            if (string.IsNullOrEmpty(mapId))
                return result;
            foreach (var m in All)
            {
                if (!m.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (m.Shared ? includeShared : includeLocal)
                    result.Add(m);
            }
            return result;
        }

        /// <summary>Looks up a marker by id from the current snapshot.</summary>
        internal static bool TryGet(string id, out MapMarker? marker)
        {
            marker = null;
            foreach (var m in All)
            {
                if (m.Id == id)
                {
                    marker = m;
                    return true;
                }
            }
            return false;
        }

        private static string Sanitize(string? label)
        {
            if (string.IsNullOrEmpty(label))
                return "";
            label = label.Trim();
            return label.Length > MaxLabelLength ? label[..MaxLabelLength] : label;
        }

        /// <summary>Validates / normalizes a colour string to "#RRGGBB", falling back to the default.</summary>
        internal static string NormalizeColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color))
                return DefaultColor;
            var c = color.Trim();
            if (!c.StartsWith('#'))
                c = "#" + c;
            if (SKColor.TryParse(c, out _))
                return c.ToUpperInvariant();
            return DefaultColor;
        }
    }
}
