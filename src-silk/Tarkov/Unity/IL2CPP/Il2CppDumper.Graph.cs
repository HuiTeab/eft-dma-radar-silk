// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

#pragma warning disable IDE0130
using UTF8String = eft_dma_radar.Silk.Misc.UTF8String;
using System.IO;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.Collections;

namespace eft_dma_radar.Silk.Tarkov.Unity.IL2CPP
{
    public static partial class Il2CppDumper
    {
        // ── Recursive object-graph dump (bot-brain research) ─────────────────────
        //
        // Built for inspecting EFT bot AI, i.e. the chain
        //   Player.AIData (Player+0x948) → BotOwner → BotsGroup / BotMemoryClass /
        //   EnemiesController → EnemyInfo
        // which holds the per-enemy "have-seen / is-visible / can-shoot / is-enemy"
        // state you'd modify to make AI passive or friendly.
        //
        // IMPORTANT — this only produces output against SPT (offline) clients:
        //   • LIVE EFT  → AI runs server-side. Other players are network-replicated
        //                 ObservedPlayerView puppets with no brain. The root
        //                 (Player.AIData) is null, so nothing is dumped.
        //   • SPT       → bots run locally as full EFT.Player objects, so the whole
        //                 brain graph lives in client memory and gets walked here.

        /// <summary>Local valid-virtual-address shorthand for this partial.</summary>
        private static bool IsVA(ulong a) => eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(a);

        // Heavy / cyclic non-AI subgraphs reachable via back-references. Skipping
        // them keeps the object budget focused on the bot brain instead of being
        // burned re-walking the whole Player / inventory / Unity scene graph.
        private static readonly string[] GraphSkipClassPrefixes =
        {
            "String", "Transform", "GameObject", "Component",
            "Player", "LocalPlayer", "ObservedPlayerView",
            "Profile", "Inventory", "PlayerBody", "Skeleton",
            "Physical", "GameWorld", "ClientLocalGameWorld",
            "HealthControllerClass", "MovementContext",
        };

        /// <summary>
        /// Recursively dumps an object and the reference-typed sub-objects it points
        /// to, breadth-first, up to <paramref name="maxDepth"/> levels and
        /// <paramref name="maxObjects"/> total objects. Each object is dumped with the
        /// full <see cref="DumpClassFieldsToWriter"/> style (offset, type, name, live
        /// value), then its <c>class</c>/<c>generic</c> reference fields and array
        /// elements are followed. <c>List`1</c> contents are enumerated so collections
        /// such as <c>EnemyInfo</c> lists are reached.
        /// <para>
        /// Pass a shared <paramref name="visited"/> set across multiple roots (e.g. one
        /// per bot) so group-shared objects like <c>BotsGroup</c> are dumped only once.
        /// </para>
        /// </summary>
        public static void DumpObjectGraphToWriter(
            ulong rootAddress,
            StreamWriter sw,
            string rootLabel,
            int maxDepth = 4,
            int maxObjects = 300,
            HashSet<ulong>? visited = null)
        {
            try
            {
                if (!IsVA(rootAddress))
                {
                    sw.WriteLine($"// {rootLabel}: null/invalid root (0x{rootAddress:X}) — no local AI brain here " +
                                 "(normal in LIVE EFT where AI is server-side; present in SPT).");
                    return;
                }

                visited ??= new HashSet<ulong>();
                var queue = new Queue<(ulong Addr, int Depth, string Label)>();
                queue.Enqueue((rootAddress, 0, rootLabel));
                int dumped = 0;

                while (queue.Count > 0 && dumped < maxObjects)
                {
                    var (addr, depth, label) = queue.Dequeue();
                    if (!IsVA(addr) || !visited.Add(addr))
                        continue;

                    ulong klass = ReadPtr(addr);
                    if (!IsVA(klass))
                        continue;

                    string cn = ReadStr(ReadPtr(klass + K_Name)) ?? "<unknown>";
                    string ns = ReadStr(ReadPtr(klass + 0x18)) ?? string.Empty;
                    string fq = string.IsNullOrEmpty(ns) ? cn : $"{ns}.{cn}";

                    sw.WriteLine($"┄┄ [d{depth}] {label}  →  {fq} @ 0x{addr:X} ┄┄");

                    var children = new List<(ulong Addr, string Label)>();
                    GraphDumpFields(addr, sw, children);
                    dumped++;

                    if (depth >= maxDepth)
                        continue;

                    // List<T> backing-store enumeration so EnemyInfo / enemy lists are reached.
                    if (cn.StartsWith("List`1", StringComparison.Ordinal))
                    {
                        try
                        {
                            using var list = MemList<ulong>.Get(addr, false);
                            int n = Math.Min(list.Count, 24);
                            sw.WriteLine($"  │  (List count={list.Count}, enumerating {n})");
                            for (int i = 0; i < n; i++)
                            {
                                var e = list[i];
                                if (IsVA(e) && !visited.Contains(e))
                                    children.Add((e, $"{label}[{i}]"));
                            }
                        }
                        catch { sw.WriteLine("  │  (List enumeration failed)"); }
                    }

                    foreach (var (cAddr, cLabel) in children)
                    {
                        if (visited.Contains(cAddr))
                            continue;
                        ulong cKlass = ReadPtr(cAddr);
                        if (!IsVA(cKlass))
                            continue;
                        string ccn = ReadStr(ReadPtr(cKlass + K_Name)) ?? string.Empty;
                        string cns = ReadStr(ReadPtr(cKlass + 0x18)) ?? string.Empty;
                        if (!ShouldFollow(cns, ccn))
                            continue;
                        queue.Enqueue((cAddr, depth + 1, cLabel));
                    }
                }

                if (dumped >= maxObjects)
                    sw.WriteLine($"// {rootLabel}: object budget ({maxObjects}) reached — graph truncated here.");
            }
            catch (Exception ex)
            {
                sw.WriteLine($"// DumpObjectGraph error: {ex.Message}");
            }
        }

        /// <summary>
        /// Decides whether the graph walk should descend into an object of the given
        /// namespace/class. Native Unity objects and heavy non-AI back-references are
        /// skipped; generic <c>List`1</c> containers are followed so their elements are
        /// reachable.
        /// </summary>
        private static bool ShouldFollow(string ns, string cn)
        {
            if (string.IsNullOrEmpty(cn))
                return false;
            // UnityEngine.* — deep, native-backed, cyclic; not AI logic.
            if (ns.StartsWith("UnityEngine", StringComparison.Ordinal))
                return false;
            // System.* — only follow List<T> (to enumerate its elements). Other
            // framework types (Dictionary internals, string, etc.) are noise here.
            if (ns.StartsWith("System", StringComparison.Ordinal))
                return cn.StartsWith("List`1", StringComparison.Ordinal);
            foreach (var skip in GraphSkipClassPrefixes)
                if (cn.StartsWith(skip, StringComparison.Ordinal))
                    return false;
            return true;
        }

        /// <summary>
        /// Walks the parent chain of <paramref name="objectAddress"/>, writing every
        /// field (offset/type/name/value) and collecting reference-typed children
        /// (class, generic, and array elements) into <paramref name="children"/>.
        /// </summary>
        private static void GraphDumpFields(ulong objectAddress, StreamWriter sw, List<(ulong, string)> children)
        {
            const int MaxHierarchyDepth = 16;
            ulong klass = ReadPtr(objectAddress);
            int hops = 0;
            while (IsVA(klass) && hops < MaxHierarchyDepth)
            {
                hops++;
                GraphDumpSingleClass(klass, objectAddress, sw, children);
                klass = ReadPtr(klass + Offsets.Il2CppClass.Parent);
            }
        }

        private static void GraphDumpSingleClass(ulong klassPtr, ulong objectAddress, StreamWriter sw, List<(ulong, string)> children)
        {
            string className = ReadStr(ReadPtr(klassPtr + K_Name)) ?? "<unknown>";
            var fieldCount = Memory.ReadValue<ushort>(klassPtr + K_FieldCount, false);
            sw.WriteLine($"  ┌ {className} (klass=0x{klassPtr:X}, {fieldCount} field(s))");

            if (fieldCount == 0 || fieldCount > 4096)
                return;

            ulong fieldsBase = ReadPtr(klassPtr + K_Fields);
            if (!IsVA(fieldsBase))
            {
                sw.WriteLine("  │  (fields pointer invalid)");
                return;
            }

            RawFieldInfoFull[] rawFields;
            try { rawFields = Memory.ReadArray<RawFieldInfoFull>(fieldsBase, fieldCount, false); }
            catch (Exception ex)
            {
                sw.WriteLine($"  │  (failed to read field array: {ex.Message})");
                return;
            }

            var nameEntries = new ScatterReadEntry<UTF8String>[rawFields.Length];
            var typeEntries = new ScatterReadEntry<RawIl2CppType>[rawFields.Length];
            var scatter = new List<IScatterEntry>(rawFields.Length * 2);

            for (int i = 0; i < rawFields.Length; i++)
            {
                if (IsVA(rawFields[i].NamePtr))
                {
                    nameEntries[i] = ScatterReadEntry<UTF8String>.Get(rawFields[i].NamePtr, MaxNameLen);
                    scatter.Add(nameEntries[i]);
                }
                if (IsVA(rawFields[i].TypePtr))
                {
                    typeEntries[i] = ScatterReadEntry<RawIl2CppType>.Get(rawFields[i].TypePtr, 0);
                    scatter.Add(typeEntries[i]);
                }
            }

            if (scatter.Count > 0)
                Memory.ReadScatter(scatter.ToArray(), false);

            for (int i = 0; i < rawFields.Length; i++)
            {
                string? name = nameEntries[i] is not null && !nameEntries[i].IsFailed
                    ? (string?)(UTF8String?)nameEntries[i].Result
                    : "<unreadable>";
                if (string.IsNullOrEmpty(name))
                    continue;

                byte typeEnum = 0;
                if (typeEntries[i] is not null && !typeEntries[i].IsFailed)
                    typeEnum = typeEntries[i].Result.TypeEnum;

                int offset = rawFields[i].Offset;
                string typeName = Il2CppTypeEnumName(typeEnum);
                string valueStr = offset < 0 ? "(static)" : ReadFieldValueString(objectAddress, (uint)offset, typeEnum);
                sw.WriteLine($"  │  [0x{(uint)offset:X4}] {typeName,-12} {name} = {valueStr}");

                if (offset < 0)
                    continue;

                // Collect reference-typed children to follow.
                //   0x12 class, 0x15 generic<> (List/Dictionary/etc.), 0x1C object → single pointer
                //   0x1D []                                                          → array, enumerate elements
                if (typeEnum is 0x12 or 0x15 or 0x1C)
                {
                    ulong ptr = ReadPtr(objectAddress + (uint)offset);
                    if (IsVA(ptr))
                        children.Add((ptr, $"{className}.{name}"));
                }
                else if (typeEnum == 0x1D)
                {
                    ulong arrPtr = ReadPtr(objectAddress + (uint)offset);
                    if (IsVA(arrPtr))
                    {
                        try
                        {
                            using var arr = MemArray<ulong>.Get(arrPtr, false);
                            int n = Math.Min(arr.Count, 12);
                            for (int e = 0; e < n; e++)
                            {
                                var elem = arr[e];
                                if (IsVA(elem))
                                    children.Add((elem, $"{className}.{name}[{e}]"));
                            }
                        }
                        catch { /* not a reference array (e.g. int[]) — skip */ }
                    }
                }
            }
        }
    }
}
