// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.IO;
using System.Net.Http.Json;

namespace eft_dma_radar.Silk.Misc.Data
{
    /// <summary>Gear slot a boss/follower item maps to for radar identification.</summary>
    public enum GuardGearSlot
    {
        Other = 0,
        Headwear = 1,
        Backpack = 2,
        Weapon = 3,
    }

    /// <summary>A single piece of boss/follower spawn gear (deduped by short-name + slot).</summary>
    public sealed class BossGearItem
    {
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public GuardGearSlot Slot { get; set; }
    }

    /// <summary>A boss or follower ("escort") with the gear it can spawn with.</summary>
    public sealed class BossInfo
    {
        public string NormalizedName { get; set; } = "";
        public string Name { get; set; } = "";
        public List<BossGearItem> Equipment { get; set; } = [];
    }

    /// <summary>A boss spawn on a map, plus the normalized names of its escorts (followers/guards).</summary>
    public sealed class MapBossEntry
    {
        public string BossNorm { get; set; } = "";
        public List<string> EscortNorms { get; set; } = [];
    }

    /// <summary>
    /// Loads boss + follower ("escort") data from the tarkov.dev API: which bosses spawn on
    /// which map, and the gear each can spawn with. Powers the Settings → Boss Guards picker,
    /// where the user turns a follower's real spawn gear into a radar identifier.
    ///
    /// Cached to <c>bossdata.json</c> next to the exe; refreshed in the background when stale.
    /// tarkov.dev <c>shortName</c>s match the radar's own gear reader (same item DB), so an
    /// item added here will match what <see cref="Tarkov.GameWorld.Player.Plugins.GuardManager"/> reads in-raid.
    /// </summary>
    internal static class BossDataManager
    {
        private const string CacheFileName = "bossdata.json";
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromDays(3);
        private static readonly string CachePath = Path.Combine(AppContext.BaseDirectory, CacheFileName);

        private static readonly JsonSerializerOptions _apiOpts = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions _cacheOpts = new() { WriteIndented = false };

        // Lock-free snapshots (UI thread reads; background fetch swaps).
        private static volatile IReadOnlyDictionary<string, BossInfo> _bosses =
            new Dictionary<string, BossInfo>(StringComparer.OrdinalIgnoreCase);
        private static volatile IReadOnlyDictionary<string, List<MapBossEntry>> _mapBosses =
            new Dictionary<string, List<MapBossEntry>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True once any data (cache or live) has been applied.</summary>
        public static bool Loaded { get; private set; }

        /// <summary>Human-readable status for the settings UI.</summary>
        public static string Status { get; private set; } = "Not loaded";

        /// <summary>All known bosses/followers keyed by normalized name.</summary>
        public static IReadOnlyDictionary<string, BossInfo> Bosses => _bosses;

        public static bool TryGetBoss(string normalizedName, out BossInfo? boss)
        {
            if (!string.IsNullOrEmpty(normalizedName) && _bosses.TryGetValue(normalizedName, out var b))
            {
                boss = b;
                return true;
            }
            boss = null;
            return false;
        }

        /// <summary>Boss spawns (with escorts) for the given radar map id, or null if unknown.</summary>
        public static List<MapBossEntry>? GetMapEntries(string? mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return null;
            return _mapBosses.TryGetValue(mapId, out var list) ? list : null;
        }

        /// <summary>Loads the disk cache (if any) and schedules a background refresh when stale.</summary>
        internal static void ModuleInit()
        {
            try
            {
                if (File.Exists(CachePath))
                {
                    using var fs = File.OpenRead(CachePath);
                    var cached = JsonSerializer.Deserialize<CacheRoot>(fs, _cacheOpts);
                    if (cached?.Bosses is { Count: > 0 })
                    {
                        Apply(cached);
                        Log.WriteLine($"[BossDataManager] Loaded {cached.Bosses.Count} bosses from disk cache.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[BossDataManager] Disk cache invalid: {ex.Message}");
            }

            bool stale = !File.Exists(CachePath)
                || (DateTime.Now - File.GetLastWriteTime(CachePath)) > UpdateInterval;
            if (stale)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(4000).ConfigureAwait(false); // let startup settle
                    await RefreshAsync().ConfigureAwait(false);
                });
            }
        }

        /// <summary>Fetches fresh boss data from tarkov.dev and persists it. Safe to call from the UI.</summary>
        public static async Task RefreshAsync()
        {
            try
            {
                Status = "Updating…";
                var query = new Dictionary<string, string>
                {
                    ["query"] =
                    """
                    {
                      bosses {
                        name
                        normalizedName
                        equipment { item { name shortName categories { name } } }
                      }
                      maps {
                        nameId
                        bosses {
                          boss { normalizedName }
                          escorts { boss { normalizedName } }
                        }
                      }
                    }
                    """
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var resp = await SilkProgram.HttpClient.PostAsJsonAsync(
                    "https://api.tarkov.dev/graphql", query, cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                var root = await JsonSerializer.DeserializeAsync<ApiRoot>(
                    await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false),
                    _apiOpts, cts.Token).ConfigureAwait(false);

                var cache = BuildCache(root?.Data);
                if (cache.Bosses.Count == 0)
                {
                    Status = "Update returned no data";
                    return;
                }

                Apply(cache);
                try
                {
                    await File.WriteAllTextAsync(CachePath,
                        JsonSerializer.Serialize(cache, _cacheOpts)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[BossDataManager] Cache write failed: {ex.Message}");
                }

                Log.WriteLine($"[BossDataManager] Updated: {cache.Bosses.Count} bosses, {cache.MapBosses.Count} maps.");
            }
            catch (Exception ex)
            {
                Status = "Update failed (offline?)";
                Log.WriteLine($"[BossDataManager] Refresh failed: {ex.Message}");
            }
        }

        private static void Apply(CacheRoot cache)
        {
            var bosses = new Dictionary<string, BossInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in cache.Bosses)
                if (!string.IsNullOrEmpty(b.NormalizedName))
                    bosses[b.NormalizedName] = b;

            var maps = new Dictionary<string, List<MapBossEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mapId, entries) in cache.MapBosses)
                if (!string.IsNullOrEmpty(mapId))
                    maps[mapId] = entries;

            _bosses = bosses;
            _mapBosses = maps;
            Loaded = true;
            Status = $"{bosses.Count} boss/follower kits (all maps)";
        }

        /// <summary>Projects the raw API response into the deduped cache model.</summary>
        private static CacheRoot BuildCache(ApiData? data)
        {
            var cache = new CacheRoot();
            if (data is null)
                return cache;

            if (data.Bosses is not null)
            {
                foreach (var b in data.Bosses)
                {
                    if (string.IsNullOrWhiteSpace(b.NormalizedName))
                        continue;

                    var info = new BossInfo { NormalizedName = b.NormalizedName, Name = b.Name };
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in b.Equipment ?? [])
                    {
                        var item = e.Item;
                        if (item is null || string.IsNullOrWhiteSpace(item.ShortName))
                            continue;
                        var slot = Classify(item.Categories);
                        if (slot == GuardGearSlot.Other)
                            continue; // only keep gear the radar can actually match
                        // Dedupe by slot + short-name (bosses list many spawn variants).
                        if (!seen.Add(((int)slot) + "|" + item.ShortName))
                            continue;
                        info.Equipment.Add(new BossGearItem
                        {
                            Name = item.Name,
                            ShortName = item.ShortName,
                            Slot = slot,
                        });
                    }
                    cache.Bosses.Add(info);
                }
            }

            if (data.Maps is not null)
            {
                foreach (var m in data.Maps)
                {
                    if (string.IsNullOrWhiteSpace(m.NameId) || m.Bosses is null)
                        continue;
                    var list = new List<MapBossEntry>();
                    var seenBoss = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var mb in m.Bosses)
                    {
                        var bossNorm = mb.Boss?.NormalizedName;
                        if (string.IsNullOrWhiteSpace(bossNorm) || !seenBoss.Add(bossNorm))
                            continue;
                        var entry = new MapBossEntry { BossNorm = bossNorm };
                        if (mb.Escorts is not null)
                        {
                            var seenEsc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var esc in mb.Escorts)
                            {
                                var en = esc.Boss?.NormalizedName;
                                if (!string.IsNullOrWhiteSpace(en) && seenEsc.Add(en))
                                    entry.EscortNorms.Add(en);
                            }
                        }
                        list.Add(entry);
                    }
                    if (list.Count > 0)
                        cache.MapBosses[m.NameId] = list;
                }
            }

            return cache;
        }

        private static GuardGearSlot Classify(List<ApiCategory>? categories)
        {
            if (categories is null)
                return GuardGearSlot.Other;
            foreach (var c in categories)
            {
                switch (c.Name)
                {
                    case "Headwear": return GuardGearSlot.Headwear;
                    case "Backpack": return GuardGearSlot.Backpack;
                }
            }
            // Weapon is a broad parent category — check after the specific slots above.
            foreach (var c in categories)
                if (c.Name == "Weapon")
                    return GuardGearSlot.Weapon;
            return GuardGearSlot.Other;
        }

        // ── Disk cache model ──────────────────────────────────────────────────
        private sealed class CacheRoot
        {
            public List<BossInfo> Bosses { get; set; } = [];
            public Dictionary<string, List<MapBossEntry>> MapBosses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        // ── tarkov.dev API response shapes ────────────────────────────────────
        private sealed class ApiRoot { public ApiData? Data { get; set; } }
        private sealed class ApiData
        {
            public List<ApiBoss>? Bosses { get; set; }
            public List<ApiMap>? Maps { get; set; }
        }
        private sealed class ApiBoss
        {
            public string Name { get; set; } = "";
            public string NormalizedName { get; set; } = "";
            public List<ApiEquip>? Equipment { get; set; }
        }
        private sealed class ApiEquip { public ApiEquipItem? Item { get; set; } }
        private sealed class ApiEquipItem
        {
            public string Name { get; set; } = "";
            public string ShortName { get; set; } = "";
            public List<ApiCategory>? Categories { get; set; }
        }
        private sealed class ApiCategory { public string Name { get; set; } = ""; }
        private sealed class ApiMap
        {
            public string NameId { get; set; } = "";
            public List<ApiMapBoss>? Bosses { get; set; }
        }
        private sealed class ApiMapBoss
        {
            public ApiBossRef? Boss { get; set; }
            public List<ApiEscort>? Escorts { get; set; }
        }
        private sealed class ApiEscort { public ApiBossRef? Boss { get; set; } }
        private sealed class ApiBossRef { public string NormalizedName { get; set; } = ""; }
    }
}
