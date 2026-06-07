// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// Manages exfiltration points and transit points — reads from the ExfilController
    /// and TransitController, refreshes exfil status and transit active-flags via scatter reads.
    /// Initialized lazily by <see cref="LocalGameWorld"/> on the registration worker thread.
    /// </summary>
    internal sealed class ExfilManager
    {
        private readonly ulong _lgw;
        private readonly string _mapId;
        private readonly bool _isPmc;
        private volatile IReadOnlyList<Exfil> _exfils = [];
        private volatile IReadOnlyList<TransitPoint> _transits = [];
        private int _initAttempts;
        private const int MaxInitAttempts = 20;
        private DateTime _lastRefresh;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

        /// <summary>Current exfil snapshot (thread-safe read).</summary>
        public IReadOnlyList<Exfil> Exfils => _exfils;

        /// <summary>Current transit point snapshot (thread-safe read).</summary>
        public IReadOnlyList<TransitPoint> Transits => _transits;

        public ExfilManager(ulong localGameWorld, string mapId, bool isPmc)
        {
            _lgw = localGameWorld;
            _mapId = mapId;
            _isPmc = isPmc;
        }

        /// <summary>
        /// Refreshes exfil status and transit active-flags via scatter reads. Initializes on first call (with retry).
        /// Called from the registration worker thread.
        /// </summary>
        public void Refresh()
        {
            var now = DateTime.UtcNow;
            if (now - _lastRefresh < RefreshInterval)
                return;
            _lastRefresh = now;

            var exfils = _exfils;

            // Initialize or retry if empty (ExfilController may not be ready immediately)
            if (exfils.Count == 0 && _initAttempts < MaxInitAttempts)
            {
                _initAttempts++;
                Init();
                exfils = _exfils;

                if (exfils.Count > 0)
                    Log.WriteLine($"[ExfilManager] Initialized {exfils.Count} exfils, {_transits.Count} transits on attempt {_initAttempts}");
            }

            var transits = _transits;

            if (exfils.Count == 0 && transits.Count == 0)
                return;

            // Upgrade any transit still on its JSON fallback to a live memory position once the
            // transform chain resolves (transit transforms aren't always ready at raid start, so the
            // one-shot read in the constructor can miss). Cheap — a handful of transits, and each
            // latches PositionResolved on first success so this stops attempting once resolved.
            for (int tx = 0; tx < transits.Count; tx++)
            {
                var transit = transits[tx];
                if (!transit.PositionResolved)
                    transit.TryResolvePositionFromMemory();
            }

            // Scatter-read status for all exfils + transits in a single DMA round-trip
            using var map = ScatterReadMap.Get();
            var round1 = map.AddRound();

            for (int ix = 0; ix < exfils.Count; ix++)
            {
                int i = ix;
                var exfil = exfils[i];
                round1[i].AddEntry<int>(0, exfil.StatusAddr);
                round1[i].Callbacks += index =>
                {
                    if (index.TryGetResult<int>(0, out var status))
                        exfil.Update(status);
                };
            }

            // Transits open ~1 min into a raid and then stay open. By default (once-per-raid) we
            // poll a transit's active flag only until it has been observed open, then latch and
            // stop — continuous status updates are pointless. The TransitContinuousRefresh option
            // re-reads every tick like exfils instead. Slots are offset past the exfil indices so
            // the two sets never collide in the round's index map.
            bool continuousTransits = SilkProgram.Config.TransitContinuousRefresh;
            for (int tx = 0; tx < transits.Count; tx++)
            {
                var transit = transits[tx];
                if (transit.ActiveAddr == 0)
                    continue;
                if (!continuousTransits && transit.StatusSettled)
                    continue;
                int slot = exfils.Count + tx;
                round1[slot].AddEntry<bool>(0, transit.ActiveAddr);
                round1[slot].Callbacks += index =>
                {
                    if (index.TryGetResult<bool>(0, out var active))
                        transit.Update(active);
                };
            }

            map.Execute();
        }

        /// <summary>
        /// Reads exfil arrays from the ExfilController — PMC/Scav array + Secret array.
        /// Also reads transit points from the TransitController dictionary.
        /// </summary>
        private void Init()
        {
            var list = new List<Exfil>();

            try
            {
                if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.ExfilController, out var exfilController, false)
                    || exfilController == 0)
                {
                    return;
                }

                // Read PMC or Scav exfil array
                var arrayOffset = _isPmc
                    ? Offsets.ExfilController.ExfiltrationPointArray
                    : Offsets.ExfilController.ScavExfiltrationPointArray;

                ReadExfilArray(exfilController + arrayOffset, _isPmc, list);

                // Read secret exfil array (always PMC-style)
                ReadExfilArray(exfilController + Offsets.ExfilController.SecretExfiltrationPointArray, true, list);

                _exfils = list;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ExfilManager] Init failed: {ex.Message}");
                _exfils = list;
            }

            // Read transit points once (separate try-catch — transits are independent of exfils)
            if (_transits.Count == 0)
            {
                try
                {
                    var transitList = new List<TransitPoint>();
                    ReadTransits(transitList);
                    _transits = transitList;
                }
                catch (Exception ex)
                {
                    Log.Write(AppLogLevel.Debug, $"[ExfilManager] Transit read error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reads an IL2CPP array of exfil pointers and creates <see cref="Exfil"/> objects.
        /// IL2CPP Array: [0x18] = count, [0x20..] = elements.
        /// </summary>
        private void ReadExfilArray(ulong arrayPtrAddr, bool isPmc, List<Exfil> list)
        {
            if (!Memory.TryReadPtr(arrayPtrAddr, out var arrayPtr, false) || arrayPtr == 0)
                return;

            var count = Memory.ReadValue<int>(arrayPtr + 0x18, false);
            if (count <= 0 || count > 64)
                return;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (!Memory.TryReadPtr(arrayPtr + 0x20 + (ulong)(i * 8), out var exfilAddr, false)
                        || exfilAddr == 0)
                        continue;

                    var exfil = new Exfil(exfilAddr, isPmc, _mapId);

                    if (exfil.Position == Vector3.Zero)
                    {
                        Log.Write(AppLogLevel.Debug, $"[ExfilManager] Skipped exfil '{exfil.Name}' — zero position");
                        continue;
                    }

                    list.Add(exfil);
                    Log.Write(AppLogLevel.Debug, $"[ExfilManager] Loaded exfil: '{exfil.Name}' @ {exfil.Position}");
                }
                catch (Exception ex)
                {
                    Log.Write(AppLogLevel.Debug, $"[ExfilManager] Failed to read exfil[{i}]: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reads transit points from the TransitController's IL2CPP dictionary.
        /// IL2CPP Dictionary layout:
        ///   0x18: _entries (Entry[])
        ///   0x20: _count (int)
        ///   Entry[] data starts at 0x20, each entry is 24 bytes.
        ///   Value pointer at offset 16 within each entry.
        /// </summary>
        private void ReadTransits(List<TransitPoint> list)
        {
            if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.TransitController, out var transitController, false)
                || transitController == 0)
            {
                Log.Write(AppLogLevel.Debug, "[ExfilManager] Transit read: TransitController not found.");
                return;
            }

            if (!Memory.TryReadPtr(transitController + Offsets.TransitController.TransitPoints, out var dictPtr, false)
                || dictPtr == 0)
            {
                Log.Write(AppLogLevel.Debug, "[ExfilManager] Transit read: TransitPoints dictionary null.");
                return;
            }

            const uint IL2CPP_DICT_COUNT = 0x20;
            const uint IL2CPP_DICT_ENTRIES = 0x18;
            const uint IL2CPP_ENTRIES_START = 0x20;
            const int IL2CPP_ENTRY_SIZE = 24;
            const int IL2CPP_ENTRY_VALUE_OFFSET = 16;

            var count = Memory.ReadValue<int>(dictPtr + IL2CPP_DICT_COUNT, false);
            if (count <= 0 || count > 100)
            {
                Log.Write(AppLogLevel.Debug, $"[ExfilManager] Transit read: invalid dict count {count} (dict=0x{dictPtr:X}).");
                return;
            }

            if (!Memory.TryReadPtr(dictPtr + IL2CPP_DICT_ENTRIES, out var entriesPtr, false) || entriesPtr == 0)
            {
                Log.Write(AppLogLevel.Debug, $"[ExfilManager] Transit read: entries array null (dict=0x{dictPtr:X}, count={count}).");
                return;
            }

            Log.Write(AppLogLevel.Debug, $"[ExfilManager] Transit read: {count} entries in dict @ 0x{dictPtr:X}.");

            var entriesBase = entriesPtr + IL2CPP_ENTRIES_START;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var entryAddr = entriesBase + (ulong)(i * IL2CPP_ENTRY_SIZE);
                    if (!Memory.TryReadPtr(entryAddr + IL2CPP_ENTRY_VALUE_OFFSET, out var transitAddr, false)
                        || transitAddr == 0)
                        continue;

                    var transit = new TransitPoint(transitAddr, _mapId);
                    list.Add(transit);
                }
                catch (Exception ex)
                {
                    Log.Write(AppLogLevel.Debug, $"[ExfilManager] Failed to read transit[{i}] @ entry 0x{(entriesBase + (ulong)(i * IL2CPP_ENTRY_SIZE)):X}: {ex.Message}");
                }
            }
        }
    }
}
