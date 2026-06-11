// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.IO;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Persistent manual-teammate list — survives radar restarts and re-raids.
    /// Persisted to <c>teammates.json</c> in AppData. Thread-safe.
    ///
    /// <para>Entries are matched by <b>AccountID OR ProfileID</b>. A live ally's
    /// AccountID is only resolved from a corpse dogtag (may never appear while they
    /// are alive), whereas their ProfileID resolves from the alive dogtag during a
    /// gear refresh. Storing and matching both recognises a marked teammate
    /// regardless of which id resolves first, and across PMC / player-scav sessions
    /// when the AccountID is known.</para>
    /// </summary>
    internal sealed class TeammateList
    {
        private static readonly string _dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-silk");

        private static readonly string _savePath = Path.Combine(_dir, "teammates.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private readonly object _lock = new();
        private readonly List<TeammateEntry> _entries = new();

        public TeammateList()
        {
            LoadFromDisk();
        }

        /// <summary>Thread-safe snapshot of saved entries.</summary>
        public IReadOnlyList<TeammateEntry> Entries
        {
            get { lock (_lock) return _entries.ToArray(); }
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        /// <summary>
        /// True when a saved teammate matches either id. Empty ids never match.
        /// </summary>
        public bool Contains(string? accountId, string? profileId)
        {
            if (string.IsNullOrEmpty(accountId) && string.IsNullOrEmpty(profileId))
                return false;
            lock (_lock)
                return FindIndexNoLock(accountId, profileId) >= 0;
        }

        /// <summary>
        /// Adds (or merges into) a saved teammate. Entries with no id are ignored.
        /// Persists to disk on change.
        /// </summary>
        public void Add(TeammateEntry entry)
        {
            try
            {
                if (entry is null ||
                    (string.IsNullOrEmpty(entry.AccountId) && string.IsNullOrEmpty(entry.ProfileId)))
                    return;

                lock (_lock)
                {
                    int idx = FindIndexNoLock(entry.AccountId, entry.ProfileId);
                    if (idx >= 0)
                    {
                        // Merge — fill in any ids/name that were unknown before.
                        var existing = _entries[idx];
                        if (string.IsNullOrEmpty(existing.AccountId) && !string.IsNullOrEmpty(entry.AccountId))
                            existing.AccountId = entry.AccountId;
                        if (string.IsNullOrEmpty(existing.ProfileId) && !string.IsNullOrEmpty(entry.ProfileId))
                            existing.ProfileId = entry.ProfileId;
                        if (!string.IsNullOrEmpty(entry.Name))
                            existing.Name = entry.Name;
                    }
                    else
                    {
                        _entries.Add(entry);
                    }
                }

                SaveToDisk();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TeammateList] Error in Add: {ex.Message}");
            }
        }

        /// <summary>Removes a saved teammate matching either id. Persists on change.</summary>
        public bool Remove(string? accountId, string? profileId)
        {
            try
            {
                bool changed;
                lock (_lock)
                {
                    int idx = FindIndexNoLock(accountId, profileId);
                    changed = idx >= 0;
                    if (changed)
                        _entries.RemoveAt(idx);
                }

                if (changed)
                    SaveToDisk();
                return changed;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TeammateList] Error in Remove: {ex.Message}");
                return false;
            }
        }

        /// <summary>Clears all saved teammates and rewrites the file.</summary>
        public void Clear()
        {
            lock (_lock)
                _entries.Clear();
            SaveToDisk();
        }

        // Caller must hold _lock.
        private int FindIndexNoLock(string? accountId, string? profileId)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!string.IsNullOrEmpty(accountId) &&
                    string.Equals(e.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                    return i;
                if (!string.IsNullOrEmpty(profileId) &&
                    string.Equals(e.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        #region Persistence

        private void SaveToDisk()
        {
            try
            {
                Directory.CreateDirectory(_dir);

                List<TeammateEntry> snapshot;
                lock (_lock)
                    snapshot = [.. _entries];

                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                var tmp = _savePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _savePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TeammateList] Error saving to disk: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_savePath))
                    return;

                var json = File.ReadAllText(_savePath);
                var persisted = JsonSerializer.Deserialize<List<TeammateEntry>>(json);
                if (persisted is not { Count: > 0 })
                    return;

                lock (_lock)
                {
                    foreach (var entry in persisted)
                    {
                        if (entry is null) continue;
                        if (string.IsNullOrEmpty(entry.AccountId) && string.IsNullOrEmpty(entry.ProfileId))
                            continue;
                        _entries.Add(entry);
                    }
                }

                Log.WriteLine($"[TeammateList] Loaded {_entries.Count} saved teammate(s) from disk.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[TeammateList] Error loading from disk: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>A single saved teammate. Matched by <see cref="AccountId"/> or <see cref="ProfileId"/>.</summary>
    internal sealed class TeammateEntry
    {
        /// <summary>BSG Account ID (stable across PMC/Scav; resolved from a corpse dogtag).</summary>
        [JsonPropertyName("accountId")]
        public string AccountId { get; set; } = string.Empty;

        /// <summary>BSG Profile ID (per character mode; resolved from the alive dogtag).</summary>
        [JsonPropertyName("profileId")]
        public string ProfileId { get; set; } = string.Empty;

        /// <summary>Player name at the time they were marked.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>When this teammate was marked.</summary>
        [JsonPropertyName("addedDate")]
        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}
