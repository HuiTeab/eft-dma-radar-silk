// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        // Reusable text-input buffers for the "add entry" boxes, keyed by "{mapId}|{field}".
        private static readonly Dictionary<string, byte[]> _guardAddBuffers = new();
        // Buffer for the "add a new map rule" box.
        private static readonly byte[] _guardNewMapBuf = new byte[48];

        // ── Custom / captured-guard state ──
        // Gear pieces the user has clicked off live AI but not yet named & saved. A single shared
        // draft is fine: capture is only ever active for the one map you're currently in.
        private static readonly List<GuardGearMatch> _captureDraft = new();
        // Reusable scratch list for the distance-sorted nearby-AI snapshot (rebuilt per frame).
        private static readonly List<Player> _captureAI = new(64);
        // Current selection of the per-identifier "add gear by hand" slot combo, keyed by combo id.
        private static readonly Dictionary<string, int> _slotCombo = new();

        // Slots offered by the manual "add gear" combo → the equipment slot each maps to (index-aligned).
        private static readonly string[] _slotLabels = ["Helmet", "Armor", "Rig", "Backpack", "Face", "Weapon", "Ammo"];
        private static readonly string[] _slotValues = ["Headwear", "ArmorVest", "TacticalVest", "Backpack", "FaceCover", "FirstPrimaryWeapon", "Ammo"];

        // Equipment slots shown as one-click capture chips, in display order, with friendly labels.
        private static readonly (string Slot, string Label)[] _captureSlots =
        [
            ("Headwear", "Helmet"),
            ("FaceCover", "Face"),
            ("ArmorVest", "Armor"),
            ("TacticalVest", "Rig"),
            ("Backpack", "Backpack"),
            ("FirstPrimaryWeapon", "Primary"),
            ("SecondPrimaryWeapon", "Secondary"),
            ("Holster", "Pistol"),
        ];

        private static void DrawGuardsTab()
        {
            ImGui.Spacing();

            bool enabled = Config.GuardIdentificationEnabled;
            if (UIControls.ToggleRow("Enable Boss-Guard Identification", ref enabled,
                "Promote AI scavs whose gear matches a rule below to Raider (boss-guard) colouring.\n" +
                "Turning this off demotes any guards we promoted, on their next scan."))
            {
                Config.GuardIdentificationEnabled = enabled;
                GuardChanged();
            }

            ImGui.TextWrapped("Each map matches AI scavs by gear short-name (helmet / backpack / weapon) " +
                "or chambered ammo. Any single match promotes the AI to a boss guard. Changes apply to " +
                "newly-scanned AI; clearing a map's lists disables detection for that map.");

            ImGui.Spacing();
            ImGui.TextDisabled($"tarkov.dev boss gear: {BossDataManager.Status}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Refresh##bossdata"))
                _ = BossDataManager.RefreshAsync();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Re-download boss/follower spawn gear from tarkov.dev.\n" +
                    "Use the picker inside each map below to turn a follower's real gear into an identifier.");

            // Live count of AI currently promoted to guards — a quick gauge of false positives while tuning.
            if (Memory.InRaid && Memory.Players is { } livePlayers)
            {
                int flagged = 0;
                foreach (var lp in livePlayers)
                    if (lp.IsBossGuard) flagged++;
                ImGui.TextDisabled($"Currently flagged as guards: {flagged}");
            }

            ImGui.Spacing();

            if (!enabled)
                ImGui.BeginDisabled();

            // Stable display order: known guard maps first, then any user-added maps.
            var ordered = new List<string>();
            foreach (var key in GuardManager.KnownGuardMaps.Keys)
                if (Config.GuardRules.ContainsKey(key))
                    ordered.Add(key);
            foreach (var key in Config.GuardRules.Keys)
                if (!ordered.Contains(key, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(key);

            string? removeMap = null;

            foreach (var mapId in ordered)
            {
                if (!Config.GuardRules.TryGetValue(mapId, out var rule) || rule is null)
                    continue;

                string friendly = GuardManager.KnownGuardMaps.TryGetValue(mapId, out var name)
                    ? name
                    : mapId;

                if (!ImGui.CollapsingHeader($"{friendly}  ({mapId})##guard_{mapId}"))
                    continue;

                ImGui.Indent(12f);
                ImGui.PushID($"guard_{mapId}");

                bool ruleEnabled = rule.Enabled;
                if (ImGui.Checkbox("Enabled", ref ruleEnabled))
                {
                    rule.Enabled = ruleEnabled;
                    GuardChanged();
                }

                bool knifeShotgun = rule.RequireKnifeAndShotgun;
                if (ImGui.Checkbox("Match camper knife + 12ga shotgun (Shturman-style)", ref knifeShotgun))
                {
                    rule.RequireKnifeAndShotgun = knifeShotgun;
                    GuardChanged();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Identify by a camper knife + 12ga secondary instead of the gear lists.\nWhen on, the lists and custom identifiers below are ignored.");

                if (!rule.RequireKnifeAndShotgun)
                {
                    ImGui.Spacing();
                    DrawGuardList(mapId, "Helmets (Headwear)", rule.Helmets);
                    DrawGuardList(mapId, "Backpacks", rule.Backpacks);
                    DrawGuardList(mapId, "Weapons", rule.Weapons);
                    DrawGuardList(mapId, "Ammo (chambered)", rule.Ammo);

                    DrawGuardSuggestions(mapId, rule);
                    DrawCustomIdentifiers(mapId, rule);
                }

                ImGui.Spacing();

                var defaults = GuardManager.BuiltinDefaults();
                bool hasDefault = defaults.ContainsKey(mapId);
                ImGui.BeginDisabled(!hasDefault);
                if (ImGui.Button("Reset to default") && defaults.TryGetValue(mapId, out var def))
                {
                    Config.GuardRules[mapId] = def.Clone();
                    GuardChanged();
                }
                ImGui.EndDisabled();

                ImGui.SameLine();
                if (ImGui.Button("Remove map"))
                    removeMap = mapId;

                ImGui.PopID();
                ImGui.Unindent(12f);
            }

            if (removeMap is not null)
            {
                Config.GuardRules.Remove(removeMap);
                GuardChanged();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Add a brand-new map rule (e.g. for a map not seeded by default).
            ImGui.TextDisabled("Add map rule  (map id, e.g. lighthouse)");
            ImGui.SetNextItemWidth(180f);
            ImGui.InputText("##guardNewMap", _guardNewMapBuf, (uint)_guardNewMapBuf.Length);
            ImGui.SameLine();
            if (ImGui.Button("Add Map"))
            {
                string newId = ReadBuf(_guardNewMapBuf).Trim();
                if (!string.IsNullOrWhiteSpace(newId) && !Config.GuardRules.ContainsKey(newId))
                {
                    // Seed from the built-in default for this id if one exists, else an empty rule.
                    var defaults = GuardManager.BuiltinDefaults();
                    Config.GuardRules[newId] = defaults.TryGetValue(newId, out var d) ? d.Clone() : new GuardMapRule();
                    GuardChanged();
                }
                Array.Clear(_guardNewMapBuf);
            }

            if (!enabled)
                ImGui.EndDisabled();
        }

        /// <summary>Draws one editable string list (chips with remove + an add box).</summary>
        private static void DrawGuardList(string mapId, string label, List<string> items)
        {
            ImGui.TextDisabled(label);
            ImGui.Indent(10f);
            // Scope by label: all four lists render under the same per-map ID, so the bare "×"
            // remove buttons would otherwise share an ID by row index across lists.
            ImGui.PushID(label);

            for (int i = 0; i < items.Count; i++)
            {
                ImGui.PushID(i);
                if (ImGui.SmallButton("×")) // ×
                {
                    items.RemoveAt(i);
                    GuardChanged();
                    ImGui.PopID();
                    i--;
                    continue;
                }
                ImGui.SameLine();
                ImGui.TextUnformatted(items[i]);
                ImGui.PopID();
            }

            string key = mapId + "|" + label;
            var buf = GuardAddBuffer(key);
            ImGui.SetNextItemWidth(160f);
            ImGui.InputText($"##add_{key}", buf, (uint)buf.Length);
            ImGui.SameLine();
            if (ImGui.Button($"Add##{key}"))
            {
                string val = ReadBuf(buf).Trim();
                if (!string.IsNullOrWhiteSpace(val) && !items.Contains(val, StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(val);
                    GuardChanged();
                }
                Array.Clear(buf);
            }

            ImGui.PopID();
            ImGui.Unindent(10f);
            ImGui.Spacing();
        }

        /// <summary>
        /// tarkov.dev-powered picker: lists the bosses + followers that spawn on this map and lets
        /// the user turn any of their real spawn gear into an identifier with one click.
        /// </summary>
        private static void DrawGuardSuggestions(string mapId, GuardMapRule rule)
        {
            var entries = BossDataManager.GetMapEntries(mapId);
            if (entries is null || entries.Count == 0)
                return;

            ImGui.Spacing();
            if (!ImGui.TreeNode($"Pick identifiers from tarkov.dev gear##sugg_{mapId}"))
                return;

            ImGui.TextDisabled("Click gear to add it as an identifier. Prefer distinctive items");
            ImGui.TextDisabled("(rare helmets/weapons) — common kit causes false positives.");

            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                DrawMobGear(mapId, rule, entry.BossNorm, isEscort: false, shown);
                foreach (var esc in entry.EscortNorms)
                    DrawMobGear(mapId, rule, esc, isEscort: true, shown);
            }

            ImGui.TreePop();
        }

        private static void DrawMobGear(string mapId, GuardMapRule rule, string norm, bool isEscort, HashSet<string> shown)
        {
            if (string.IsNullOrEmpty(norm) || !shown.Add(norm))
                return;
            if (!BossDataManager.TryGetBoss(norm, out var info) || info is null || info.Equipment.Count == 0)
                return;

            string label = isEscort ? $"{info.Name}  (follower)" : $"{info.Name}  (boss)";
            ImGui.PushID($"mob_{mapId}_{norm}");
            if (ImGui.TreeNode(label))
            {
                DrawGearPickRow(mapId, norm, "Helmets", GuardGearSlot.Headwear, info, rule.Helmets);
                DrawGearPickRow(mapId, norm, "Backpacks", GuardGearSlot.Backpack, info, rule.Backpacks);
                DrawGearPickRow(mapId, norm, "Weapons", GuardGearSlot.Weapon, info, rule.Weapons);
                ImGui.TreePop();
            }
            ImGui.PopID();
        }

        /// <summary>Draws the gear of one slot as wrapping "+ shortName" buttons.</summary>
        private static void DrawGearPickRow(string mapId, string norm, string slotLabel, GuardGearSlot slot,
            BossInfo info, List<string> target)
        {
            // Gather this slot's distinct items first so we can skip the label when empty.
            var items = new List<BossGearItem>();
            foreach (var it in info.Equipment)
                if (it.Slot == slot)
                    items.Add(it);
            if (items.Count == 0)
                return;

            ImGui.TextDisabled(slotLabel);
            ImGui.Indent(10f);

            var style = ImGui.GetStyle();
            // Right edge of the content region, in screen coords (to match GetItemRectMax).
            float rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool present = target.Contains(it.ShortName, StringComparer.OrdinalIgnoreCase);
                string btnLabel = (present ? "✓ " : "+ ") + it.ShortName + $"##{mapId}_{norm}_{slotLabel}_{i}";

                if (i > 0)
                {
                    float lastRight = ImGui.GetItemRectMax().X;
                    float nextW = ImGui.CalcTextSize(btnLabel).X + style.FramePadding.X * 2f;
                    if (lastRight + style.ItemSpacing.X + nextW < rightEdge)
                        ImGui.SameLine();
                }

                ImGui.BeginDisabled(present);
                if (ImGui.SmallButton(btnLabel))
                {
                    target.Add(it.ShortName);
                    GuardChanged();
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(it.Name);
            }

            ImGui.Unindent(10f);
            ImGui.Spacing();
        }

        /// <summary>
        /// Per-map "Custom / captured guards" section: named identifiers (a set of gear matched
        /// ALL or ANY), plus the live-raid capture flow and a manual add path.
        /// </summary>
        private static void DrawCustomIdentifiers(string mapId, GuardMapRule rule)
        {
            ImGui.Spacing();
            if (!ImGui.TreeNode($"Custom / captured guards ({rule.Custom.Count})##custom_{mapId}"))
                return;

            ImGui.TextDisabled("Name a kit and match an AI by ALL of it (precise) or ANY of it.");
            ImGui.TextDisabled("Capture from a live raid below, or add pieces by hand.");
            ImGui.Spacing();

            // One-click: seed named identifiers from this map's tarkov.dev boss/follower gear.
            var tdEntries = BossDataManager.GetMapEntries(mapId);
            ImGui.BeginDisabled(tdEntries is null || tdEntries.Count == 0);
            if (ImGui.Button($"Generate from tarkov.dev##gen_{mapId}"))
                GenerateIdentifiersFromTarkovDev(mapId, rule, tdEntries);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Create one named identifier per boss/follower on this map from their\n" +
                    "tarkov.dev spawn gear (helmets, or weapons if they wear none). Match = ANY;\n" +
                    "they show their name on the radar/ESP. Prune or disable any that over-match.");
            ImGui.Spacing();

            // ── Existing identifiers ──
            int removeAt = -1;
            for (int i = 0; i < rule.Custom.Count; i++)
            {
                var id = rule.Custom[i];
                ImGui.PushID($"cid_{mapId}_{i}");

                string title = string.IsNullOrWhiteSpace(id.Name) ? "(unnamed)" : id.Name;
                if (ImGui.TreeNode($"{title}  [{(id.MatchAll ? "all" : "any")} of {id.Gear.Count}]##hdr"))
                {
                    bool en = id.Enabled;
                    if (ImGui.Checkbox("Enabled", ref en)) { id.Enabled = en; GuardChanged(); }

                    ImGui.SameLine();
                    if (ImGui.SmallButton(id.MatchAll ? "Match: ALL" : "Match: ANY"))
                    {
                        id.MatchAll = !id.MatchAll;
                        GuardChanged();
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(id.MatchAll
                            ? "Matches only when the AI has EVERY piece below. Click for ANY."
                            : "Matches when the AI has ANY one piece below. Click for ALL.");

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Remove##idrem"))
                        removeAt = i;

                    ImGui.Indent(10f);
                    int gremove = -1;
                    for (int g = 0; g < id.Gear.Count; g++)
                    {
                        ImGui.PushID(g);
                        if (ImGui.SmallButton("×"))
                            gremove = g;
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"{SlotShortLabel(id.Gear[g].Slot)}: {id.Gear[g].Short}");
                        ImGui.PopID();
                    }
                    if (gremove >= 0) { id.Gear.RemoveAt(gremove); GuardChanged(); }

                    DrawAddGearRow(mapId, i, id);
                    ImGui.Unindent(10f);

                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
            if (removeAt >= 0) { rule.Custom.RemoveAt(removeAt); GuardChanged(); }

            ImGui.Spacing();
            DrawCaptureSection(mapId, rule);

            // ── Add an empty identifier (populate it via capture or the add-gear row) ──
            ImGui.Spacing();
            ImGui.TextDisabled("New empty identifier:");
            string addKey = mapId + "|newcid";
            var nameBuf = GuardAddBuffer(addKey);
            ImGui.SetNextItemWidth(160f);
            ImGui.InputText($"##newcid_{mapId}", nameBuf, (uint)nameBuf.Length);
            ImGui.SameLine();
            if (ImGui.Button($"Add##newcid_{mapId}"))
            {
                string nm = ReadBuf(nameBuf).Trim();
                rule.Custom.Add(new GuardIdentifier { Name = string.IsNullOrEmpty(nm) ? "Custom guard" : nm });
                Array.Clear(nameBuf);
                GuardChanged();
            }

            ImGui.TreePop();
        }

        /// <summary>A "[slot ▼] [short-name] [Add gear]" row for adding a piece to an identifier by hand.</summary>
        private static void DrawAddGearRow(string mapId, int idIndex, GuardIdentifier id)
        {
            string key = $"{mapId}|cid{idIndex}|addgear";
            _slotCombo.TryGetValue(key, out int sel);

            ImGui.SetNextItemWidth(90f);
            ImGui.Combo($"##slot_{key}", ref sel, _slotLabels, _slotLabels.Length);
            _slotCombo[key] = sel;

            ImGui.SameLine();
            var buf = GuardAddBuffer(key);
            ImGui.SetNextItemWidth(120f);
            ImGui.InputText($"##short_{key}", buf, (uint)buf.Length);
            ImGui.SameLine();
            if (ImGui.SmallButton($"Add gear##{key}"))
            {
                string val = ReadBuf(buf).Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    id.Gear.Add(new GuardGearMatch { Slot = _slotValues[sel], Short = val });
                    GuardChanged();
                }
                Array.Clear(buf);
            }
        }

        /// <summary>
        /// Live-raid capture: lists the nearby AI (by distance) on the map you're currently in and
        /// lets you click their real gear into a draft, then name & save it as a custom identifier.
        /// </summary>
        private static void DrawCaptureSection(string mapId, GuardMapRule rule)
        {
            if (!ImGui.TreeNode($"Capture from live raid##cap_{mapId}"))
                return;

            bool onThisMap = Memory.InRaid
                && string.Equals(Memory.MapID, mapId, StringComparison.OrdinalIgnoreCase);
            if (!onThisMap)
            {
                ImGui.TextDisabled(Memory.InRaid
                    ? $"You're on '{Memory.MapID}', not this map — open this on the map you're in."
                    : "Join a raid on this map to capture a guard from the live AI list.");
                ImGui.TreePop();
                return;
            }

            // Draft preview (the kit being assembled).
            if (_captureDraft.Count > 0)
            {
                ImGui.TextDisabled("New identifier from captured gear:");
                ImGui.Indent(10f);
                int rem = -1;
                for (int i = 0; i < _captureDraft.Count; i++)
                {
                    ImGui.PushID($"draft{i}");
                    if (ImGui.SmallButton("×"))
                        rem = i;
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"{SlotShortLabel(_captureDraft[i].Slot)}: {_captureDraft[i].Short}");
                    ImGui.PopID();
                }
                if (rem >= 0) _captureDraft.RemoveAt(rem);

                var nameBuf = GuardAddBuffer("capture|name");
                ImGui.SetNextItemWidth(160f);
                ImGui.InputText("##capname", nameBuf, (uint)nameBuf.Length);
                ImGui.SameLine();
                if (ImGui.Button("Save identifier"))
                {
                    string nm = ReadBuf(nameBuf).Trim();
                    var id = new GuardIdentifier { Name = string.IsNullOrEmpty(nm) ? "Captured guard" : nm };
                    foreach (var g in _captureDraft)
                        id.Gear.Add(new GuardGearMatch { Slot = g.Slot, Short = g.Short });
                    rule.Custom.Add(id);
                    _captureDraft.Clear();
                    Array.Clear(nameBuf);
                    GuardChanged();
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear"))
                    _captureDraft.Clear();
                ImGui.Unindent(10f);
                ImGui.Separator();
            }
            else
            {
                ImGui.TextDisabled("Click an AI's gear below to add it. Build a kit, then name & save.");
            }

            // Distance-sorted snapshot of nearby AI with readable gear.
            var players = Memory.Players;
            if (players is null)
            {
                ImGui.TextDisabled("No players yet.");
                ImGui.TreePop();
                return;
            }
            _captureOrigin = Memory.LocalPlayer?.Position ?? default;
            _captureAI.Clear();
            foreach (var p in players)
            {
                if (p.Type is not (PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss))
                    continue;
                if (!p.IsEspVisible || !p.GearReady || p.Equipment.Count == 0)
                    continue;
                _captureAI.Add(p);
            }
            _captureAI.Sort(static (a, b) =>
                Vector3.DistanceSquared(_captureOrigin, a.Position).CompareTo(
                Vector3.DistanceSquared(_captureOrigin, b.Position)));

            if (_captureAI.Count == 0)
            {
                ImGui.TextDisabled("No AI with readable gear nearby yet.");
                ImGui.TreePop();
                return;
            }

            int shown = 0;
            foreach (var p in _captureAI)
            {
                if (shown++ >= 30) break;
                int dist = (int)Vector3.Distance(_captureOrigin, p.Position);
                ImGui.PushID($"ai_{shown}");
                if (ImGui.TreeNode($"{p.Name}  ({dist}m)  [{TypeShort(p.Type)}]"))
                {
                    DrawCaptureGearButtons(p);
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }

            ImGui.TreePop();
        }

        // Sort origin for the nearby-AI snapshot (set just before the sort; the comparer is static).
        private static Vector3 _captureOrigin;

        /// <summary>Draws one AI's capturable gear as one-click chips that add to the draft.</summary>
        private static void DrawCaptureGearButtons(Player p)
        {
            foreach (var (slot, label) in _captureSlots)
            {
                if (p.Equipment.TryGetValue(slot, out var g) && g is not null && !string.IsNullOrEmpty(g.Short))
                    DrawCaptureChip(slot, label, g.Short);
            }
            if (!string.IsNullOrEmpty(p.InHandsAmmo))
                DrawCaptureChip("Ammo", "Ammo", p.InHandsAmmo);
        }

        private static void DrawCaptureChip(string slot, string label, string shortName)
        {
            bool present = _captureDraft.Exists(d =>
                d.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase) &&
                d.Short.Equals(shortName, StringComparison.OrdinalIgnoreCase));
            ImGui.BeginDisabled(present);
            if (ImGui.SmallButton($"{(present ? "✓" : "+")} {label}: {shortName}##{slot}_{shortName}"))
                _captureDraft.Add(new GuardGearMatch { Slot = slot, Short = shortName });
            ImGui.EndDisabled();
        }

        /// <summary>Friendly label for a stored equipment slot name.</summary>
        private static string SlotShortLabel(string slot) => slot switch
        {
            "Headwear" => "Helmet",
            "FaceCover" => "Face",
            "ArmorVest" => "Armor",
            "TacticalVest" => "Rig",
            "Backpack" => "Backpack",
            "FirstPrimaryWeapon" => "Primary",
            "SecondPrimaryWeapon" => "Secondary",
            "Holster" => "Pistol",
            "Scabbard" => "Melee",
            "Ammo" => "Ammo",
            _ => slot,
        };

        private static string TypeShort(PlayerType t) => t switch
        {
            PlayerType.AIBoss => "Boss",
            PlayerType.AIRaider => "Raider",
            _ => "Scav",
        };

        /// <summary>
        /// Seeds one ANY-match custom identifier per boss/follower on the map from tarkov.dev spawn
        /// gear: their headwear short-names (most distinctive), or weapons when they wear no helmet
        /// (e.g. Kaban). Skips mobs that already have a same-named identifier. The user prunes from there.
        /// </summary>
        private static void GenerateIdentifiersFromTarkovDev(string mapId, GuardMapRule rule, List<MapBossEntry>? entries)
        {
            if (entries is null || entries.Count == 0)
                return;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in rule.Custom)
                if (!string.IsNullOrEmpty(c.Name))
                    existing.Add(c.Name);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var entry in entries)
            {
                added += AddGeneratedIdentifier(entry.BossNorm, rule, existing, seen);
                foreach (var esc in entry.EscortNorms)
                    added += AddGeneratedIdentifier(esc, rule, existing, seen);
            }
            if (added > 0)
                GuardChanged();
        }

        /// <summary>Builds one generated identifier for a boss/follower; returns 1 if added, else 0.</summary>
        private static int AddGeneratedIdentifier(string norm, GuardMapRule rule,
            HashSet<string> existing, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(norm) || !seen.Add(norm))
                return 0;
            if (!BossDataManager.TryGetBoss(norm, out var info) || info is null || info.Equipment.Count == 0)
                return 0;
            if (existing.Contains(info.Name))
                return 0;

            var id = new GuardIdentifier { Name = info.Name, MatchAll = false };
            var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Prefer distinctive headwear.
            foreach (var it in info.Equipment)
                if (it.Slot == GuardGearSlot.Headwear && picked.Add(it.ShortName))
                    id.Gear.Add(new GuardGearMatch { Slot = "Headwear", Short = it.ShortName });

            // Helmetless bosses (e.g. Kaban) — fall back to their weapons, capped to keep the list sane.
            if (id.Gear.Count == 0)
                foreach (var it in info.Equipment)
                {
                    if (it.Slot != GuardGearSlot.Weapon || !picked.Add(it.ShortName))
                        continue;
                    id.Gear.Add(new GuardGearMatch { Slot = "FirstPrimaryWeapon", Short = it.ShortName });
                    if (id.Gear.Count >= 12)
                        break;
                }

            if (id.Gear.Count == 0)
                return 0;

            rule.Custom.Add(id);
            existing.Add(info.Name);
            return 1;
        }

        private static void GuardChanged()
        {
            Config.MarkDirty();
            GuardManager.Rebuild(Config);
        }

        private static byte[] GuardAddBuffer(string key)
        {
            if (!_guardAddBuffers.TryGetValue(key, out var buf))
            {
                buf = new byte[64];
                _guardAddBuffers[key] = buf;
            }
            return buf;
        }

        private static string ReadBuf(byte[] buf)
        {
            int len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = buf.Length;
            return Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
