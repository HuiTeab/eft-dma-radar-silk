// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Silk.Tarkov.Hideout;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;
using GameObjectManager = eft_dma_radar.Silk.Tarkov.Unity.GOM;
using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;
using VmmSharpEx.Scatter;

namespace eft_dma_radar.Silk.DMA
{
    /// <summary>
    /// Standalone DMA Memory module for the Silk.NET radar.
    /// Minimal dependencies, self-contained.
    /// </summary>
    internal static class Memory
    {
        #region Constants / Fields

        private const string ProcessName = "EscapeFromTarkov.exe";
        private const string MemMapFile = "mmap.txt";
        public const uint MAX_READ_SIZE = 0x1000u * 1500u;

        private static Vmm? _vmm;
        private static uint _pid;
        private static readonly Lock _restartLock = new();
        private static CancellationTokenSource _cts = new();
        private static volatile bool _shutdown;
        private static Thread? _workerThread;
        private static SilkConfig? _config;

        private static MemoryState _state = MemoryState.NotStarted;

        /// <summary>
        /// Optional UI notification callback. Set by RadarWindow after init.
        /// Thread-safe: invoked from the memory worker thread.
        /// </summary>
        public static Action<string, NotificationLevel>? ShowNotification;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VmmFlags ToFlags(bool useCache) =>
            useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;

        /// <summary>Returns the active VMM handle or throws if disposed.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vmm VmmOrThrow() =>
            _vmm ?? throw new ObjectDisposedException(nameof(Memory));

        #endregion

        #region Public State

        public static MemoryState State => _state;
        /// <summary>The active VMM handle, or <see langword="null"/> if not yet initialized.</summary>
        internal static Vmm? VmmHandle => _vmm;
        public static ulong UnityBase { get; private set; }
        public static ulong GOM { get; private set; }
        public static ulong GameAssemblyBase { get; private set; }

        /// <summary>
        /// UnityPlayer.dll FileVersion from its PE version resource, snapshotted
        /// at module load. Used by the PhysX snapshot fingerprint so a Unity
        /// engine bump auto-invalidates the on-disk scene cache. Empty if the
        /// version resource was unreadable.
        /// </summary>
        public static string UnityPlayerVersion { get; private set; } = string.Empty;
        public static bool Ready => _state is MemoryState.ProcessFound or MemoryState.InRaid or MemoryState.InHideout;
        public static bool InRaid => _state is MemoryState.InRaid;
        public static bool InHideout => _state is MemoryState.InHideout;

        /// <summary>Hideout manager instance — persists across hideout entries.</summary>
        public static HideoutManager Hideout { get; } = new();

        /// <summary>Persistent player history — tracks PMCs seen across sessions.</summary>
        public static PlayerHistory PlayerHistory { get; } = new();

        /// <summary>Manual player watchlist — persists user-tagged players across sessions.</summary>
        public static PlayerWatchlist PlayerWatchlist { get; } = new();

        /// <summary>Persistent manual teammates — marked via Shift+click, applied each raid.</summary>
        public static TeammateList Teammates { get; } = new();

        public static LocalGameWorld? Game { get; private set; }
        public static string? MapID => Game?.MapID;
        public static RegisteredPlayers? Players => Game?.RegisteredPlayers;
        public static Player? LocalPlayer => Game?.LocalPlayer;
        public static IReadOnlyList<LootItem>? Loot => Game?.Loot;
        public static IReadOnlyList<LootCorpse>? Corpses => Game?.Corpses;
        public static IReadOnlyList<LootContainer>? Containers => Game?.Containers;
        public static IReadOnlyList<LootAirdrop>? Airdrops => Game?.Airdrops;
        public static IReadOnlyList<Exfil>? Exfils => Game?.Exfils;
        public static IReadOnlyList<TransitPoint>? Transits => Game?.Transits;
        public static IReadOnlyList<Door>? Doors => Game?.Doors;
        public static IReadOnlyList<QuestLocation>? QuestLocations => Game?.QuestLocations;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Quests.QuestManager? QuestManager => Game?.QuestManager ?? LobbyQuestReader.QuestManager;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Profile.WishlistManager? WishlistManager => Game?.WishlistManager;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Explosives.ExplosivesManager? Explosives => Game?.Explosives;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Explosives.PredictedArc? InHandGrenadePrediction => Game?.Explosives?.InHandPrediction;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Btr.BtrTracker? Btr => Game?.Btr;
        public static IReadOnlyList<eft_dma_radar.Silk.Tarkov.GameWorld.Interactables.Switch>? Switches => Game?.Switches;
        #endregion

        #region Events

        /// <summary>Raised when the game process is found and ready.</summary>
        public static event EventHandler<EventArgs>? GameStarted;
        /// <summary>Raised when the game process is no longer running.</summary>
        public static event EventHandler<EventArgs>? GameStopped;
        /// <summary>Raised when a raid begins.</summary>
        public static event EventHandler<EventArgs>? RaidStarted;
        /// <summary>Raised when a raid ends.</summary>
        public static event EventHandler<EventArgs>? RaidStopped;
        /// <summary>Raised when the player enters the hideout.</summary>
        public static event EventHandler<EventArgs>? HideoutEntered;
        /// <summary>Raised when the player leaves the hideout.</summary>
        public static event EventHandler<EventArgs>? HideoutExited;

        private static void OnGameStarted()
        {
            SetState(MemoryState.ProcessFound);
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            LobbyQuestReader.Start();
            eft_dma_radar.Silk.Tarkov.QuestPlanner.QuestPlannerWorker.Start();
            GameStarted?.Invoke(null, EventArgs.Empty);
        }

        private static void OnGameStopped()
        {
            SetState(MemoryState.WaitingForProcess);
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            UnityBase = default;
            GOM = default;
            GameAssemblyBase = default;
            _pid = default;
            GameObjectManager.ResetCachedAddresses();
            MatchingProgressResolver.Reset();
            Hideout.Reset();
            KillfeedManager.Reset();
            LobbyQuestReader.InvalidateCache();
            eft_dma_radar.Silk.Tarkov.QuestPlanner.QuestPlannerWorker.InvalidateCache();
            GameStopped?.Invoke(null, EventArgs.Empty);
        }

        private static void OnRaidStarted()
        {
            SetState(MemoryState.InRaid);
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            RaidStarted?.Invoke(null, EventArgs.Empty);
        }

        private static void OnRaidStopped()
        {
            if (_state is MemoryState.InRaid or MemoryState.InHideout)
                SetState(MemoryState.ProcessFound);
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            Game = null;
            LootFilter.ClearCaches();
            RaidStopped?.Invoke(null, EventArgs.Empty);
        }

        private static void OnHideoutEntered()
        {
            SetState(MemoryState.InHideout);
            HideoutEntered?.Invoke(null, EventArgs.Empty);
        }

        private static void OnHideoutExited()
        {
            if (_state is MemoryState.InHideout)
                SetState(MemoryState.ProcessFound);
            Game = null;
            Hideout.InvalidatePointers();
            HideoutExited?.Invoke(null, EventArgs.Empty);
        }

        private static void SetState(MemoryState s)
        {
            _state = s;
            Log.WriteLine($"[Memory] State → {s}");
        }

        #endregion

        #region Init

        /// <summary>
        /// Initialize the DMA layer. Called once from <see cref="SilkProgram.Main"/>.
        /// </summary>
        public static void ModuleInit(SilkConfig config)
        {
            // Device connection is deferred to the worker thread (see ConnectDevice) so the radar
            // window can come up and show a "Waiting for DMA device" state — and keep retrying —
            // instead of hard-crashing when the card is powered down / unplugged at launch.
            // Power the card on and it connects automatically; no relaunch needed.
            _config = config;
            SetState(MemoryState.WaitingForDevice);

            _workerThread = new Thread(MemoryWorker) { IsBackground = true, Name = "MemoryWorker" };
            _workerThread.Start();
        }

        #endregion

        #region Worker

        /// <summary>
        /// Synchronous physical-memory throughput benchmark. Mirrors the DMA test tool
        /// approach exactly: iterate all 4 KB physical pages, find ones with ≥16 MB
        /// contiguous after them, shuffle, then time raw LeechCore.Read calls.
        /// Must be called before <see cref="Vmm.RegisterAutoRefresh"/>.
        /// Stores the result in <see cref="DmaStats.MaxThroughputMBps"/>.
        /// </summary>
        private static void RunThroughputBenchmark(Vmm vmm)
        {
            try
            {
                const uint ChunkSize  = 0x1000000u; // 16 MB — matches ThroughputTest
                const int  DurationMs = 3000;

                // ── Build page list exactly like DmaConnection.GetMemoryMap() ────────
                var physMap = vmm.Map_GetPhysMem();
                if (physMap is null || physMap.Length == 0)
                {
                    Log.WriteLine("[Memory] Throughput benchmark: no physical memory map.");
                    return;
                }

                var candidates = new List<ulong>(512);
                foreach (var section in physMap)
                {
                    for (ulong p = section.pa, remaining = section.cb;
                         remaining > 0x1000;
                         p += 0x1000, remaining -= 0x1000)
                    {
                        if (remaining >= ChunkSize)
                            candidates.Add(p);
                    }
                }

                if (candidates.Count == 0)
                {
                    Log.WriteLine("[Memory] Throughput benchmark: no 16 MB contiguous pages found.");
                    return;
                }

                // Shuffle so we don't always hit the same cached region.
                Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates));
                ulong pa = candidates[0];

                Log.WriteLine($"[Memory] Throughput benchmark: reading 16 MB chunks from PA 0x{pa:X} for {DurationMs / 1000} s...");

                long totalReads = 0, failedReads = 0;
                var sw = Stopwatch.StartNew();

                unsafe
                {
                    void* buf = NativeMemory.AlignedAlloc(ChunkSize, 0x1000);
                    try
                    {
                        while (sw.ElapsedMilliseconds < DurationMs)
                        {
                            if (!vmm.LeechCore.Read(pa, (IntPtr)buf, ChunkSize))
                                failedReads++;
                            totalReads++;
                        }
                    }
                    finally
                    {
                        NativeMemory.AlignedFree(buf);
                    }
                }

                sw.Stop();
                long ok   = totalReads - failedReads;
                float mbps = (float)(ok * ChunkSize / 1_048_576.0 / sw.Elapsed.TotalSeconds);
                DmaStats.SetMaxThroughput(mbps);
                Log.WriteLine($"[Memory] Throughput benchmark: {mbps:F1} MB/s ({ok}/{totalReads} reads OK in {sw.Elapsed.TotalSeconds:F1} s)");
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Warning, $"[Memory] Throughput benchmark failed: {ex.Message}");
            }
        }

        private static void MemoryWorker()
        {
            Log.WriteLine("[Memory] Worker thread started.");

            // Block here until the DMA device is reachable (retries indefinitely, drives the
            // WaitingForDevice UI state). Only fails if shutdown is requested first.
            if (!ConnectDevice())
            {
                Log.WriteLine("[Memory] Worker thread exiting (no device / shutdown).");
                return;
            }

            SetState(MemoryState.WaitingForProcess);

            while (!_shutdown)
            {
                try
                {
                    RunStartupLoop();
                    if (_shutdown) break;
                    OnGameStarted();
                    RunGameLoop();
                    OnGameStopped();
                }
                catch (OperationCanceledException) when (_shutdown)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_shutdown) break;
                    Log.Write(AppLogLevel.Error, $"FATAL on memory thread: {ex}");
                    Notify("FATAL error on memory thread — restarting", NotificationLevel.Error);
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }

            Log.WriteLine("[Memory] Worker thread exiting.");
        }

        /// <summary>
        /// Connects to the DMA device, retrying every ~3s until it succeeds or shutdown is
        /// requested. Surfaces the underlying leechcore reason (device not found, FPGA/FTDI link
        /// error, version mismatch, etc.) and drives the <see cref="MemoryState.WaitingForDevice"/>
        /// UI state, so a powered-down card no longer crashes the app — power it on and it connects.
        /// Generating the memory map, the throughput benchmark and auto-refresh registration all
        /// happen here because they require a live device.
        /// </summary>
        /// <returns><see langword="true"/> once connected; <see langword="false"/> if shutdown first.</returns>
        private static bool ConnectDevice()
        {
            var config = _config ?? throw new InvalidOperationException("ModuleInit was not called before ConnectDevice.");

            var vmmVer = FileVersionInfo.GetVersionInfo("vmm.dll").FileVersion;
            var lcVer = FileVersionInfo.GetVersionInfo("leechcore.dll").FileVersion;
            Log.WriteLine($"[Memory] Initializing DMA... (vmm {vmmVer}, leechcore {lcVer})");

            SetState(MemoryState.WaitingForDevice);
            bool warned = false;

            while (!_shutdown)
            {
                var args = new List<string>(["-norefresh", "-device", config.DeviceStr, "-waitinitialize"]);
                try
                {
                    if (config.MemMapEnabled && !File.Exists(MemMapFile))
                    {
                        Log.WriteLine("[Memory] No MemMap, generating...");
                        using var seed = new Vmm([.. args]);
                        seed.GetMemoryMap(applyMap: true, outputFile: MemMapFile);
                    }

                    if (config.MemMapEnabled)
                        args.AddRange(["-memmap", MemMapFile]);

                    _vmm = new Vmm([.. args]);

                    // Route VMM/LeechCore internal diagnostics (FPGA/TLP errors, failed device
                    // reads, refresh issues) into our own Log + notification path. By default VMM
                    // only emits Warning/Critical here — low-volume, high-signal — so this surfaces
                    // real DMA-link problems without flooding. (VMMDLL_LogCallback, MemProcFS v5.17+.)
                    unsafe { _vmm.LogCallback(&OnVmmLog); }

                    // Benchmark BEFORE registering auto-refresh so the PCIe bus is idle
                    // during measurement. RegisterAutoRefresh drives continuous TLP traffic
                    // that would starve concurrent LeechCore.Read calls.
                    RunThroughputBenchmark(_vmm);

                    _vmm.RegisterAutoRefresh(RefreshOption.MemoryPartial, TimeSpan.FromMilliseconds(300));
                    _vmm.RegisterAutoRefresh(RefreshOption.TlbPartial, TimeSpan.FromSeconds(2));

                    Log.WriteLine("[Memory] DMA initialized OK.");
                    if (warned)
                        Notify("DMA device connected", NotificationLevel.Info);
                    return true;
                }
                catch (Exception ex)
                {
                    // Drop any partially-initialized handle before retrying.
                    try { _vmm?.Dispose(); } catch { }
                    _vmm = null;

                    if (!warned)
                    {
                        Log.Write(AppLogLevel.Warning,
                            $"[Memory] DMA initialization failed: {ex.Message}\n" +
                            $"vmm: {vmmVer}  leechcore: {lcVer}\n" +
                            "Troubleshooting: power on the DMA card, check cable connections and the -device " +
                            "setting; if hardware changed, delete mmap.txt and the Symbols folder. Retrying every 3s…");
                        Notify("Waiting for DMA device — is the card powered on?", NotificationLevel.Warning);
                        warned = true;
                    }
                    else
                    {
                        Log.WriteLine($"[Memory] DMA still unavailable ({ex.Message}); retrying…");
                    }

                    SetState(MemoryState.WaitingForDevice);

                    // ~3s back-off, but stay responsive to shutdown.
                    for (int i = 0; i < 30 && !_shutdown; i++)
                        Thread.Sleep(100);
                }
            }

            return false;
        }

        #endregion

        #region Startup Loop

        private static void RunStartupLoop()
        {
            Log.WriteLine("[Memory] Waiting for game process...");
            SetState(MemoryState.WaitingForProcess);
            var cooldown = Stopwatch.StartNew();

            while (!_shutdown)
            {
                try
                {
                    if (cooldown.ElapsedMilliseconds >= 3000)
                    {
                        FullRefresh();
                        cooldown.Restart();
                    }

                    LoadProcess();

                    // Retry module loading — game may still be initializing
                    bool modulesReady = false;
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            if (i > 0)
                            {
                                FullRefresh();
                                cooldown.Restart();
                            }
                            LoadModules();
                            modulesReady = true;
                            break;
                        }
                        catch
                        {
                            if (i == 0)
                                Log.WriteLine("[Memory] Process found, waiting for modules...");
                            Thread.Sleep(1000);
                        }
                    }

                    if (!modulesReady)
                        throw new Exception("Modules failed to load after retries.");

                    // Resolve IL2CPP offsets. Il2CppDumper is self-pacing: it tries the PE-fingerprint
                    // cache first (no TypeInfoTable read needed — the common case), and only on a cache
                    // miss runs its own 30-retry loop that waits for the IL2CPP runtime to populate the
                    // TypeInfoTable. A separate pre-dump wait used to live here, but it probed a hardcoded
                    // (version-specific) RVA that goes stale on game updates, stalling every launch for the
                    // full 60 s timeout before the dumper loaded from cache anyway.
                    eft_dma_radar.Silk.Tarkov.Unity.IL2CPP.Il2CppDumper.Dump();
                    // NOTE: CameraManager.Initialize() (AllCameras sig-scan + camera_offsets.json)
                    // is intentionally deferred to Phase 4 (Aimview). Not needed for Phase 1.

                    SetState(MemoryState.Initializing);
                    Log.WriteLine("[Memory] Game startup OK.");
                    Notify("Game startup OK", NotificationLevel.Info);
                    return;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[Memory] Startup failed: {ex.Message}");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Game Loop

        private static void RunGameLoop()
        {
            while (!_shutdown)
            {
                try
                {
                    var ct = _cts.Token;

                    using var game = Game = LocalGameWorld.Create(ct);

                    if (game.IsHideout)
                    {
                        if (SilkProgram.Config.HideoutEnabled)
                        {
                            Log.WriteLine("[Memory] Entered hideout.");
                            RunHideoutLoop(game, ct);
                        }
                        else
                        {
                            Log.WriteLine("[Memory] Hideout detected but disabled by config — skipping.");
                        }
                        continue;
                    }

                    Log.WriteLine($"[Memory] Raid started. Map = '{game.MapID}'");
                    OnRaidStarted();
                    game.Start();

                    while (game.InRaid)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (_restartRequested)
                        {
                            _restartRequested = false;
                            RequestRestart();
                            break;
                        }

                        // Check if game process is still alive (expensive, rate-limited)
                        Thread.Sleep(133);
                    }

                    if (!game.InRaid)
                        Log.WriteLine("[Memory] Raid ended (player extracted, died, or left).");
                }
                catch (OperationCanceledException)
                {
                    if (_shutdown) break;
                    Log.WriteLine("[Memory] Radar restart requested.");
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break; // VMM handle disposed — shutdown in progress
                }
                catch (GameNotRunningException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_shutdown) break;
                    Log.WriteLine($"[Memory] CRITICAL in game loop: {ex}");
                    Notify("CRITICAL error in game loop", NotificationLevel.Error);
                    break;
                }
                finally
                {
                    OnRaidStopped();
                    if (!_shutdown)
                        Thread.Sleep(100);
                }
            }

            if (!_shutdown)
            {
                Log.WriteLine("[Memory] Game is no longer running.");
                Notify("Game is no longer running", NotificationLevel.Warning);
            }
        }

        /// <summary>
        /// Lightweight loop for hideout mode. No worker threads, no loot/player tracking.
        /// Automatically refreshes the HideoutManager, then polls until the GameWorld
        /// changes (player queued for raid or returned to main menu).
        /// </summary>
        private static void RunHideoutLoop(LocalGameWorld game, CancellationToken ct)
        {
            OnHideoutEntered();

            try
            {
                // Auto-refresh hideout data on entry (if enabled)
                if (SilkProgram.Config.HideoutAutoRefresh)
                {
                    try
                    {
                        var status = Hideout.RefreshAll();
                        Log.WriteLine($"[Memory] Hideout auto-refresh: {status}");
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"[Memory] Hideout auto-refresh failed: {ex.Message}");
                    }
                }
                else
                {
                    Log.WriteLine("[Memory] Hideout auto-refresh disabled by config.");
                }

                // Poll until hideout GameWorld is no longer valid
                int validityTick = 0;
                while (!_shutdown)
                {
                    ct.ThrowIfCancellationRequested();

                    if (_restartRequested)
                    {
                        _restartRequested = false;
                        RequestRestart();
                        break;
                    }

                    // Every 4th tick (~2s) verify the hideout GameWorld is still alive.
                    // When the player queues for a raid or returns to the main menu,
                    // the hideout scene is torn down and MainPlayer becomes invalid.
                    if ((++validityTick & 0x3) == 0 && !game.IsHideoutAlive())
                    {
                        Log.WriteLine("[Memory] Hideout GameWorld no longer valid.");
                        break;
                    }

                    Thread.Sleep(500);
                }

                Log.WriteLine("[Memory] Left hideout.");
            }
            finally
            {
                OnHideoutExited();
            }
        }

        private static volatile bool _restartRequested;

        public static bool RestartRadar
        {
            set { if (InRaid || InHideout) _restartRequested = value; }
        }

        #endregion

        #region Restart

        /// <summary>
        /// Issue a CancellationToken swap to break the current game loop iteration.
        /// </summary>
        public static void RequestRestart()
        {
            // Allow re-detection of the same GameWorld on user-initiated restart
            LocalGameWorld.ClearStaleGuard();
            lock (_restartLock)
            {
                var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
                old.Cancel();
                old.Dispose();
            }
        }

        #endregion

        #region Process / Module Loading

        private static void LoadProcess()
        {
            if (!VmmOrThrow().PidGetFromName(ProcessName, out uint pid))
                throw new Exception($"Process '{ProcessName}' not found.");
            _pid = pid;
        }

        private static void LoadModules()
        {
            var vmm = VmmOrThrow();
            var unityBase = vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");
            ArgumentOutOfRangeException.ThrowIfZero(unityBase, nameof(unityBase));
            UnityBase = unityBase;

            // Snapshot the Unity engine version from the PE version resource.
            // Used by the PhysX snapshot fingerprint so a Unity bump
            // invalidates the disk cache automatically. Failures non-fatal.
            try
            {
                var modules = vmm.Map_GetModule(_pid, fExtendedInfo: true);
                if (modules is not null)
                {
                    foreach (var m in modules)
                    {
                        if (!string.Equals(m.sText, "UnityPlayer.dll", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (m.VersionInfo.fValid && !string.IsNullOrEmpty(m.VersionInfo.sFileVersion))
                        {
                            UnityPlayerVersion = m.VersionInfo.sFileVersion;
                            Log.WriteLine($"[Memory] UnityPlayer.dll FileVersion={m.VersionInfo.sFileVersion}");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Memory] UnityPlayer version snapshot failed (non-fatal): {ex.Message}");
            }

            var gaBase = vmm.ProcessGetModuleBase(_pid, "GameAssembly.dll");
            if (gaBase != 0)
            {
                GameAssemblyBase = gaBase;
                Log.WriteLine($"[Memory] GameAssembly.dll base: 0x{gaBase:X}");
            }
            else
            {
                Log.WriteLine("[Memory] WARNING: GameAssembly.dll not found.");
            }

            GOM = GameObjectManager.GetAddr(unityBase);
            ArgumentOutOfRangeException.ThrowIfZero(GOM, nameof(GOM));
            Log.WriteLine($"[Memory] GOM: 0x{GOM:X}");
        }

        #endregion

        #region Scatter Read

        /// <summary>Executes a batch scatter read against the game process.</summary>
        public static void ReadScatter(IScatterEntry[] entries, int count, bool useCache = true)
        {
            if (count == 0) return;
            using var scatter = new VmmScatter(VmmOrThrow(), _pid, ToFlags(useCache));
            scatter.Executed += (count, pages) => DmaStats.AddScatterExecute(count, pages);

            for (int i = 0; i < count; i++)
            {
                var e = entries[i];
                if (!e.Address.IsValidVirtualAddress() || e.CB == 0 || (uint)e.CB > MAX_READ_SIZE)
                {
                    e.IsFailed = true;
                    continue;
                }
                if (!scatter.PrepareRead(e.Address, (uint)e.CB))
                    e.IsFailed = true;
            }

            scatter.Execute();

            for (int i = 0; i < count; i++)
            {
                var e = entries[i];
                if (!e.IsFailed)
                    e.ReadResult(scatter);
            }
        }

        public static void ReadScatter(IScatterEntry[] entries, bool useCache = true)
            => ReadScatter(entries, entries.Length, useCache);

        #endregion

        #region Read Methods

        /// <summary>
        /// Compile-time element size of <typeparamref name="T"/>. JIT-folds to a constant.
        /// Mirrors the <c>sizeof(T)</c> the underlying VmmSharpEx read uses, and accepts the
        /// same <c>allows ref struct</c> generics as <see cref="ReadValue{T}"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int SizeOf<T>() where T : unmanaged, allows ref struct => sizeof(T);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            DmaStats.AddDirectRead(addr, SizeOf<T>(), useCache);
            // Use the non-throwing variant so we can attach the faulting address to the
            // exception — the bare "Memory Read Failed!" from VmmSharpEx tells you nothing
            // about which read died. The interpolated string is only built on failure.
            if (!VmmOrThrow().MemReadValue<T>(_pid, addr, out var result, ToFlags(useCache)))
                throw new VmmException($"ReadValue<{typeof(T).Name}> failed @ 0x{addr:X}");
            return result;
        }

        public static ulong ReadPtr(ulong addr, bool useCache = true)
        {
            var ptr = ReadValue<ulong>(addr, useCache);
            if (!ptr.IsValidVirtualAddress())
                throw new BadPtrException(addr, ptr);
            return ptr;
        }

        public static ulong ReadPtrChain(ulong addr, ReadOnlySpan<uint> offsets, bool useCache = true)
        {
            var p = addr;
            foreach (var o in offsets)
                p = ReadPtr(p + o, useCache);
            return p;
        }

        public static void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true)
            where T : unmanaged
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            if (buffer.IsEmpty) return;
            DmaStats.AddDirectRead(addr, (long)buffer.Length * SizeOf<T>(), useCache);
            if (!VmmOrThrow().MemReadSpan(_pid, addr, buffer, ToFlags(useCache)))
                throw new VmmException($"ReadBuffer<{typeof(T).Name}> failed @ 0x{addr:X} ({buffer.Length} elems)");
        }

        public static T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (count <= 0) return [];
            T[] arr = new T[count];
            ReadBuffer(addr, arr.AsSpan(), useCache);
            return arr;
        }

        public static string ReadString(ulong addr, int cb = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cb, 0, nameof(cb));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cb, 0x1000, nameof(cb));
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            DmaStats.AddDirectRead(addr, cb, useCache);
            return VmmOrThrow().MemReadString(_pid, addr, cb, Encoding.UTF8, ToFlags(useCache))
                ?? throw new VmmException($"ReadString failed @ 0x{addr:X} (cb={cb})");
        }

        public static string ReadUnityString(ulong addr, int length = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0, nameof(length));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 0x1000, nameof(length));
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            DmaStats.AddDirectRead(addr + 0x14, length, useCache);
            return VmmOrThrow().MemReadString(_pid, addr + 0x14, length, Encoding.Unicode, ToFlags(useCache))
                ?? throw new VmmException($"ReadUnityString failed @ 0x{addr + 0x14:X} (len={length})");
        }

        #endregion

        #region Try Read Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadValue<T>(ulong addr, out T result, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            if (!addr.IsValidVirtualAddress()) { result = default; return false; }
            DmaStats.AddDirectRead(addr, SizeOf<T>(), useCache);
            return VmmOrThrow().MemReadValue(_pid, addr, out result, ToFlags(useCache));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadPtr(ulong addr, out ulong result, bool useCache = true)
        {
            if (!TryReadValue(addr, out result, useCache)) return false;
            return result.IsValidVirtualAddress();
        }

        public static bool TryReadPtrChain(ulong addr, ReadOnlySpan<uint> offsets, out ulong result, bool useCache = true)
        {
            result = addr;
            foreach (var o in offsets)
                if (!TryReadPtr(result + o, out result, useCache)) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true)
            where T : unmanaged
        {
            if (!addr.IsValidVirtualAddress()) return false;
            if (buffer.IsEmpty) return true;
            DmaStats.AddDirectRead(addr, (long)buffer.Length * SizeOf<T>(), useCache);
            return VmmOrThrow().MemReadSpan(_pid, addr, buffer, ToFlags(useCache));
        }

        public static bool TryReadString(ulong addr, out string? result, int cb = 128, bool useCache = true)
        {
            result = null;
            if (cb <= 0 || cb > 0x1000) return false;
            if (!addr.IsValidVirtualAddress()) return false;
            DmaStats.AddDirectRead(addr, cb, useCache);
            result = VmmOrThrow().MemReadString(_pid, addr, cb, Encoding.UTF8, ToFlags(useCache));
            return result is not null;
        }

        public static bool TryReadUnityString(ulong addr, out string? result, int length = 128, bool useCache = true)
        {
            result = null;
            if (length <= 0 || length > 0x1000) return false;
            if (!addr.IsValidVirtualAddress()) return false;
            DmaStats.AddDirectRead(addr + 0x14, length, useCache);
            result = VmmOrThrow().MemReadString(_pid, addr + 0x14, length, Encoding.Unicode, ToFlags(useCache));
            return result is not null;
        }

        public static (uint Timestamp, uint SizeOfImage) ReadPeFingerprint(ulong moduleBase)
        {
            if (!TryReadValue<uint>(moduleBase + 0x3C, out var eLfanew, false) || eLfanew == 0 || eLfanew > 0x1000)
                return (0, 0);
            if (!TryReadValue<uint>(moduleBase + eLfanew + 8, out var ts, false)) return (0, 0);
            if (!TryReadValue<uint>(moduleBase + eLfanew + 0x50, out var sz, false)) return (0, 0);
            return (ts, sz);
        }

        #endregion

        #region Signature Scanning

        public static ulong FindSignature(string signature, string moduleName)
        {
            var vmm = VmmOrThrow();
            int tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (tokens <= 32)
                return vmm.FindSignature(_pid, signature, moduleName);
            var results = vmm.FindSignatures(_pid, signature, moduleName, maxMatches: 1);
            return results.Length > 0 ? results[0] : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong[] FindSignatures(string signature, string moduleName, int maxMatches = int.MaxValue)
            => VmmOrThrow().FindSignatures(_pid, signature, moduleName, maxMatches);

        /// <summary>
        /// Returns the image size (in bytes) of the named module currently
        /// loaded. Used by <see cref="PhysX.PhysXProbe"/> to flag candidate
        /// pointers that fall inside UnityPlayer.dll's own image (those would
        /// be vtables / globals, not heap-allocated SDK pointers).
        /// </summary>
        public static uint GetModuleImageSize(string moduleName)
        {
            try
            {
                var modules = VmmOrThrow().Map_GetModule(_pid, fExtendedInfo: false);
                if (modules is null) return 0;
                foreach (var m in modules)
                    if (string.Equals(m.sText, moduleName, StringComparison.OrdinalIgnoreCase))
                        return m.cbImageSize;
            }
            catch { }
            return 0;
        }

        #endregion

        #region Write Methods

        public static unsafe void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            Span<byte> buf = stackalloc byte[sizeof(T)];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buf), value);
            VmmOrThrow().MemWriteSpan(_pid, addr, buf);
        }

        /// <summary>
        /// Write value to memory with verification — retries up to 3 times.
        /// </summary>
        public static unsafe void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged
        {
            int cb = sizeof(T);
            var b1 = new ReadOnlySpan<byte>(&value, cb);
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    WriteValue(addr, value);
                    Thread.SpinWait(5);
                    T temp = ReadValue<T>(addr, false);
                    var b2 = new ReadOnlySpan<byte>(&temp, cb);
                    if (b1.SequenceEqual(b2)) return;
                }
                catch { }
            }
            throw new VmmException("Memory write verification failed.");
        }

        /// <summary>Write a buffer of unmanaged values to memory.</summary>
        public static void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            VmmOrThrow().MemWriteSpan(_pid, addr, buffer);
        }

        /// <summary>Write a buffer with verification — retries up to 3 times.</summary>
        public static void WriteBufferEnsure<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            int cb = SizeChecker<T>.Size * buffer.Length;
            Span<byte> temp = cb > 0x1000 ? new byte[cb] : stackalloc byte[cb];
            ReadOnlySpan<byte> b1 = MemoryMarshal.Cast<T, byte>(buffer);
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    WriteBuffer(addr, buffer);
                    Thread.SpinWait(5);
                    temp.Clear();
                    ReadBuffer(addr, temp, false);
                    if (temp.SequenceEqual(b1)) return;
                }
                catch { }
            }
            throw new VmmException("Memory write buffer verification failed.");
        }

        #endregion

        #region Misc

        public static void FullRefresh() => _vmm?.ForceFullRefresh();

        /// <summary>
        /// Throws <see cref="GameNotRunningException"/> if the game process is gone.
        /// </summary>
        public static void ThrowIfNotInGame()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    FullRefresh();
                    if (VmmOrThrow().PidGetFromName(ProcessName, out uint pid) && pid == _pid)
                        return;
                }
                catch { Thread.Sleep(150); }
            }
            throw new GameNotRunningException();
        }

        /// <summary>Creates a new <see cref="VmmScatter"/> against the game process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VmmScatter GetScatter(VmmFlags flags)
        {
            var s = new VmmScatter(VmmOrThrow(), _pid, flags);
            s.Executed += s_scatterExecutedHandler;
            return s;
        }

        /// <summary>Creates a new <see cref="VmmScatter"/> against the game process with cache control.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VmmScatter CreateScatter(bool useCache = true)
        {
            var s = new VmmScatter(VmmOrThrow(), _pid, ToFlags(useCache));
            s.Executed += s_scatterExecutedHandler;
            return s;
        }

        // Cached delegate — avoids per-scatter closure allocation on the hot path.
        // Scatter handles are created and disposed many times per worker tick
        // (realtime, validation, gear, hands, firearm, etc.), so even a small
        // managed allocation here adds up to measurable GC pressure over a raid.
        private static readonly Action<int, int> s_scatterExecutedHandler =
            static (count, pages) => DmaStats.AddScatterExecute(count, pages);

        /// <summary>Signals the worker to stop, waits for it to exit, and disposes the VMM handle.</summary>
        public static void Close()
        {
            if (_shutdown) return; // idempotent
            _shutdown = true;
            LobbyQuestReader.Stop();
            eft_dma_radar.Silk.Tarkov.QuestPlanner.QuestPlannerWorker.Stop();
            try { _cts.Cancel(); } catch { }

            // Wait for the worker thread to finish — bounded to prevent hung shutdown
            _workerThread?.Join(TimeSpan.FromSeconds(5));

            // Drain any in-progress IL2CPP match dump before the VMM handle is disposed.
            // Without this the background task is cut off mid-write and the file is truncated.
            MatchDumper.Drain(TimeSpan.FromSeconds(120));

            // Unregister the log callback before disposing so VMM cannot call back into a
            // torn-down managed runtime during handle teardown.
            try { unsafe { _vmm?.LogCallback(null); } } catch { }

            _vmm?.Dispose();
            _vmm = null;
            Log.WriteLine("[Memory] Closed.");
        }

        private static void Notify(string msg, NotificationLevel level)
        {
            try { ShowNotification?.Invoke(msg, level); }
            catch { }
        }

        /// <summary>
        /// Logging callback invoked by VMM/LeechCore (registered via <see cref="Vmm.LogCallback"/>).
        /// Fires from internal VMM threads — must be thread-safe and must never let an exception
        /// cross the native boundary. Maps the VMM log level onto the radar's <see cref="Log"/>:
        /// Critical→Error (+ rate-limited user notification), Warning→Warning, everything else→Debug.
        /// </summary>
        /// <remarks>
        /// VMM levels: 1=Critical 2=Warning 3=Info 4=Verbose 5=Debug 6=Trace.
        /// <paramref name="uszModule"/> / <paramref name="uszMessage"/> are UTF-8 (LPCSTR) pointers.
        /// </remarks>
        [UnmanagedCallersOnly]
        private static void OnVmmLog(IntPtr hVMM, uint mid, IntPtr uszModule, uint level, IntPtr uszMessage)
        {
            try
            {
                string? msg = Marshal.PtrToStringUTF8(uszMessage);
                if (string.IsNullOrEmpty(msg))
                    return;
                string? module = Marshal.PtrToStringUTF8(uszModule);
                string prefix = string.IsNullOrEmpty(module) ? "[VMM]" : $"[VMM:{module}]";

                switch (level)
                {
                    case 1: // Critical — a stopping device/link error
                        Log.Write(AppLogLevel.Error, $"{prefix} {msg}");
                        // Surface to the user, but rate-limit so a continuously failing DMA
                        // link can't flood the notification queue.
                        if (Log.ShouldEmitRateLimited(AppLogLevel.Error, "vmm_crit_notify", TimeSpan.FromSeconds(10)))
                            Notify($"DMA device error: {msg}", NotificationLevel.Error);
                        break;
                    case 2: // Warning — severe but non-fatal (e.g. retried read)
                        Log.Write(AppLogLevel.Warning, $"{prefix} {msg}");
                        break;
                    default: // Info/Verbose/Debug/Trace — only surfaced when debug logging is on
                        Log.Write(AppLogLevel.Debug, $"{prefix} {msg}");
                        break;
                }
            }
            catch
            {
                // Never allow an exception to propagate across the native callback boundary.
            }
        }

        #endregion

        #region Nested Types

        /// <summary>Thrown when the game process is no longer running.</summary>
        public sealed class GameNotRunningException : DmaException
        {
            public GameNotRunningException()
                : base("Game process is no longer running.") { }
        }

        #endregion
    }

    /// <summary>Memory module lifecycle state.</summary>
    public enum MemoryState
    {
        NotStarted,
        WaitingForDevice,
        WaitingForProcess,
        Initializing,
        ProcessFound,
        InRaid,
        InHideout,
    }

    /// <summary>Notification severity used by <see cref="Memory.ShowNotification"/>.</summary>
    public enum NotificationLevel { Info, Warning, Error }
}
