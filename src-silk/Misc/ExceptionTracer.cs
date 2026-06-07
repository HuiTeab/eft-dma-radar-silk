// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

using VmmSharpEx;

namespace eft_dma_radar.Silk.Misc
{
    /// <summary>
    /// Diagnostic helper that logs the FIRST occurrence of every unique call site that throws
    /// a DMA-related exception (<see cref="VmmException"/> or <see cref="BadPtrException"/>).
    /// <para>
    /// Enabled by either setting <see cref="Enabled"/> = true before <see cref="Install"/>,
    /// or by setting the environment variable <c>SILK_TRACE_DMA_EXCEPTIONS=1</c>.
    /// </para>
    /// <para>
    /// Each unique (exception-type + call-site) pair is logged exactly once with a full stack trace
    /// so you can pinpoint which <c>Memory.ReadX</c>/<c>ReadArray</c>/<c>ReadBuffer</c> call is faulting
    /// without drowning the log in duplicates. Every site also keeps a running failure count, and a
    /// site is re-logged when its count crosses an escalating threshold (10, 100, 1k, …) so a read
    /// that "fails very bad" stands out from one that fails once.
    /// </para>
    /// </summary>
    internal static class ExceptionTracer
    {
        private sealed class SiteStat
        {
            public long Count;   // total times this call site has thrown (Interlocked)
            public int Index;    // 1-based log ordinal, assigned on first sighting
        }

        private static readonly ConcurrentDictionary<string, SiteStat> _sites = new(StringComparer.Ordinal);
        private static int _installed;
        private static int _distinctSites;

        /// <summary>Max distinct call sites to emit a full trace for; counts keep tracking past this.</summary>
        public const int MaxDistinctSites = 200;

        // Default OFF — opt-in by setting SILK_TRACE_DMA_EXCEPTIONS=1
        public static bool Enabled { get; set; } = false;

        /// <summary>
        /// Installs the first-chance exception handler if enabled (once).
        /// </summary>
        public static void Install()
        {
            var env = Environment.GetEnvironmentVariable("SILK_TRACE_DMA_EXCEPTIONS");
            if (!string.IsNullOrEmpty(env) && (env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase)))
                Enabled = true;

            if (!Enabled)
                return;

            if (Interlocked.Exchange(ref _installed, 1) == 1)
                return;

            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            Log.WriteLine("[ExceptionTracer] First-chance DMA exception tracing ENABLED. " +
                          $"Each call site logs a full trace once (max {MaxDistinctSites} sites), then re-logs at escalating failure counts.");
        }

        private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            var ex = e.Exception;
            if (ex is not VmmException && ex is not BadPtrException)
                return;

            // Cheap stack walk (no PDB/file-info lookup) just to fingerprint the call site.
            // This runs on EVERY DMA exception, so the expensive file-info walk is reserved
            // for the once-per-site detailed log below.
            var keyTrace = new System.Diagnostics.StackTrace(1, fNeedFileInfo: false);
            string siteKey = BuildSiteKey(ex, keyTrace);

            var stat = _sites.GetOrAdd(siteKey, static _ => new SiteStat());
            long count = Interlocked.Increment(ref stat.Count);

            if (count == 1L)
            {
                // First sighting of this call site — emit the full trace (with file/line info) once.
                int n = Interlocked.Increment(ref _distinctSites);
                stat.Index = n;
                if (n <= MaxDistinctSites)
                {
                    var detailed = new System.Diagnostics.StackTrace(1, fNeedFileInfo: true);
                    Log.WriteLine($"[ExceptionTracer #{n}] {ex.GetType().Name}: {ex.Message}");
                    Log.WriteLine(detailed.ToString());
                    if (n == MaxDistinctSites)
                        Log.WriteLine($"[ExceptionTracer] Reached limit ({MaxDistinctSites}) distinct sites — new sites won't emit a trace (counts still tracked).");
                }
            }
            else if (stat.Index > 0 && stat.Index <= MaxDistinctSites && IsEscalation(count))
            {
                // A read that keeps failing — re-surface it (with its current count) so a site
                // that "fails very bad" is obvious without scrolling back to the original trace.
                Log.WriteLine($"[ExceptionTracer #{stat.Index}] reached {count:N0} failures: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>True at 10, 100, 1k, 10k, 100k, then every 1M — escalating, log-spaced thresholds.</summary>
        private static bool IsEscalation(long c) =>
            c == 10 || c == 100 || c == 1_000 || c == 10_000 || c == 100_000 || (c % 1_000_000 == 0);

        private static string BuildSiteKey(Exception ex, System.Diagnostics.StackTrace trace)
        {
            // Fingerprint on the full managed frame chain (including VmmSharpEx). We only strip
            // the tracer itself. This guarantees different call sites always get different keys,
            // even if the app's own frames aren't yet on the stack at first-chance time.
            var sb = new System.Text.StringBuilder(ex.GetType().Name);
            var frames = trace.GetFrames();
            if (frames is null)
                return sb.ToString();

            int added = 0;
            foreach (var frame in frames)
            {
                var m = frame.GetMethod();
                if (m is null) continue;
                var declType = m.DeclaringType;
                if (declType == typeof(ExceptionTracer)) continue;

                sb.Append('|')
                  .Append(declType?.FullName ?? "?")
                  .Append('.')
                  .Append(m.Name);
                if (++added >= 8) break;
            }
            return sb.ToString();
        }
    }
}
