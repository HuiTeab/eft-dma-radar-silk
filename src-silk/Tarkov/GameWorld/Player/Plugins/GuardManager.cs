// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Collections.Frozen;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// JSON-serializable boss-guard identification rule for a single map. The radar marks
    /// an AI scav as a boss guard (promoting it to <see cref="PlayerType.AIRaider"/>) when its
    /// gear matches any of these sets by short-name — or, for Shturman/Woods-style camps, when
    /// <see cref="RequireKnifeAndShotgun"/> is set and the AI carries a camper knife + 12ga shotgun.
    /// Edited at runtime via the Settings → Boss Guards tab; persisted in <see cref="SilkConfig.GuardRules"/>.
    /// </summary>
    public sealed class GuardMapRule
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("backpacks")]
        public List<string> Backpacks { get; set; } = [];

        [JsonPropertyName("helmets")]
        public List<string> Helmets { get; set; } = [];

        [JsonPropertyName("weapons")]
        public List<string> Weapons { get; set; } = [];

        [JsonPropertyName("ammo")]
        public List<string> Ammo { get; set; } = [];

        /// <summary>
        /// User-named composite identifiers — typically captured from a real AI in-raid via the
        /// Boss Guards tab. Each matches on a set of gear pieces (see <see cref="GuardIdentifier"/>),
        /// checked in addition to the flat lists above. Ignored when <see cref="RequireKnifeAndShotgun"/> is set.
        /// </summary>
        [JsonPropertyName("custom")]
        public List<GuardIdentifier> Custom { get; set; } = [];

        /// <summary>
        /// Woods/Shturman-style match: identify by a camper knife in the scabbard + a 12ga
        /// shotgun as the secondary weapon. When true, the gear lists above are ignored.
        /// </summary>
        [JsonPropertyName("requireKnifeAndShotgun")]
        public bool RequireKnifeAndShotgun { get; set; }

        public GuardMapRule Clone()
        {
            var clone = new GuardMapRule
            {
                Enabled = Enabled,
                Backpacks = [.. Backpacks],
                Helmets = [.. Helmets],
                Weapons = [.. Weapons],
                Ammo = [.. Ammo],
                RequireKnifeAndShotgun = RequireKnifeAndShotgun,
            };
            foreach (var id in Custom)
                clone.Custom.Add(id.Clone());
            return clone;
        }
    }

    /// <summary>One captured gear piece: an equipment slot name plus the item short-name to match in
    /// it. The pseudo-slot <c>"Ammo"</c> matches against <see cref="Player.InHandsAmmo"/> (substring);
    /// weapon slots match across all three weapon slots so a slung/holstered gun still counts.</summary>
    public sealed class GuardGearMatch
    {
        [JsonPropertyName("slot")]
        public string Slot { get; set; } = "";

        [JsonPropertyName("short")]
        public string Short { get; set; } = "";
    }

    /// <summary>
    /// A user-named identifier built from a set of gear pieces (<see cref="Gear"/>), usually captured
    /// from a real AI in-raid. Promotes an AI when its gear contains every piece (<see cref="MatchAll"/>
    /// = true, the default) or any one piece (false). The name is a Settings label only — it does not
    /// appear on the radar.
    /// </summary>
    public sealed class GuardIdentifier
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>True → every entry in <see cref="Gear"/> must match (AND); false → any one (OR).</summary>
        [JsonPropertyName("matchAll")]
        public bool MatchAll { get; set; } = true;

        [JsonPropertyName("gear")]
        public List<GuardGearMatch> Gear { get; set; } = [];

        public GuardIdentifier Clone()
        {
            var clone = new GuardIdentifier { Name = Name, Enabled = Enabled, MatchAll = MatchAll };
            foreach (var g in Gear)
                clone.Gear.Add(new GuardGearMatch { Slot = g.Slot, Short = g.Short });
            return clone;
        }
    }

    /// <summary>
    /// Identifies AI boss-guards on specific maps using a small set of equipment heuristics
    /// (backpack / helmet / primary weapon / chambered ammo) against <see cref="Player.Equipment"/>
    /// and <see cref="Player.InHandsAmmo"/>.
    ///
    /// Rules live in <see cref="SilkConfig.GuardRules"/> (seeded from <see cref="BuiltinDefaults"/>
    /// on first run) and are user-editable. <see cref="Rebuild"/> compiles them into a fast,
    /// lock-free <see cref="FrozenSet{T}"/> snapshot that the registration worker reads each gear
    /// refresh; the UI thread mutates the config and calls <see cref="Rebuild"/> to publish.
    /// </summary>
    internal static class GuardManager
    {
        #region Compiled snapshot

        private sealed class CompiledRule
        {
            public bool RequireKnifeAndShotgun;
            public FrozenSet<string> Backpacks = FrozenSet<string>.Empty;
            public FrozenSet<string> Helmets = FrozenSet<string>.Empty;
            public FrozenSet<string> Weapons = FrozenSet<string>.Empty;
            public FrozenSet<string> Ammo = FrozenSet<string>.Empty;
            public CompiledIdentifier[] Custom = Array.Empty<CompiledIdentifier>();
        }

        /// <summary>Compiled form of a <see cref="GuardIdentifier"/> (gear pieces + match mode).</summary>
        private sealed class CompiledIdentifier
        {
            public string Name = "";
            public bool MatchAll;
            public (string Slot, string Short)[] Gear = Array.Empty<(string, string)>();
        }

        // Lock-free snapshots — swapped atomically by Rebuild (UI thread), read by the worker.
        private static volatile FrozenDictionary<string, CompiledRule> _compiled =
            FrozenDictionary<string, CompiledRule>.Empty;
        private static volatile bool _enabled = true;

        #endregion

        #region Defaults / friendly names

        /// <summary>
        /// Friendly map names (with the boss whose guards they cover) for the settings UI.
        /// Keyed by the map id the game reports (<see cref="Memory.MapID"/>).
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> KnownGuardMaps =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"]        = "Customs - Reshala",
                ["shoreline"]     = "Shoreline - Sanitar",
                ["rezervbase"]    = "Reserve - Glukhar",
                ["tarkovstreets"] = "Streets - Kollontay & Kaban",
                ["woods"]         = "Woods - Shturman",
            };

        /// <summary>
        /// The built-in baseline rules. Used to seed a fresh config and to power the
        /// per-map "Reset to default" button in the settings tab.
        /// </summary>
        internal static Dictionary<string, GuardMapRule> BuiltinDefaults() =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["shoreline"] = new GuardMapRule
                {
                    Backpacks = ["SFMP", "Beta 2", "Attack 2"],
                    Helmets = ["Altyn", "LShZ-2DTM"],
                    Ammo = ["m62", "m993", "pp", "bp", "ap-20", "ppbs"],
                },
                ["bigmap"] = new GuardMapRule
                {
                    Helmets = ["Altyn"],
                    Ammo = ["bp", "pp", "ppbs", "ap-m", "m856a1"],
                },
                ["rezervbase"] = new GuardMapRule
                {
                    Backpacks = ["Attack 2"],
                    Helmets = ["Altyn", "LShZ-2DTM", "Maska-1SCh", "Vulkan-5", "ZSh-1-2M"],
                    Ammo = ["m62", "m80", "zvezda", "shrap-10", "pp"],
                },
                ["tarkovstreets"] = new GuardMapRule
                {
                    // Streets has two boss lines: Kollontay and Kaban. Cover both.
                    Backpacks = ["Attack 2"],
                    // Headwear: Kollontay's heavy hats + the named helmets unique to Kaban's
                    // followers ("Basmach", "Gus") and his guards' heavy lids (SFERA-S, TC 800).
                    Helmets = ["Altyn", "LShZ-2DTM", "Maska-1SCh", "Vulkan-5", "ZSh-1-2M", "Tor-2",
                               "Basmach", "Gus", "SFERA-S", "TC 800"],
                    // Distinctive primaries/secondaries: Kollontay's guard kit + Kaban's LMGs.
                    Weapons = ["PP-9 Klin", "KS-23M", "RPDN", "PP-19-01",
                               "PKM", "PKP", "M60E6", "Mk 43 Mod 1"],
                    Ammo = ["m62", "m80", "zvezda", "shrap-10", "pp"],
                },
                ["woods"] = new GuardMapRule
                {
                    RequireKnifeAndShotgun = true,
                },
            };

        #endregion

        #region Config seeding / compile

        /// <summary>Seeds <see cref="SilkConfig.GuardRules"/> with the built-in defaults on first run.</summary>
        internal static void EnsureSeeded(SilkConfig cfg)
        {
            cfg.GuardRules ??= new Dictionary<string, GuardMapRule>(StringComparer.OrdinalIgnoreCase);
            if (cfg.GuardRules.Count == 0)
            {
                foreach (var (mapId, rule) in BuiltinDefaults())
                    cfg.GuardRules[mapId] = rule.Clone();
            }
        }

        /// <summary>
        /// Recompiles the lock-free lookup from the current config. Call after any edit
        /// (and once at startup). Cheap — the rule set is tiny.
        /// </summary>
        internal static void Rebuild(SilkConfig cfg)
        {
            _enabled = cfg.GuardIdentificationEnabled;

            var rules = cfg.GuardRules;
            if (rules is null || rules.Count == 0)
            {
                _compiled = FrozenDictionary<string, CompiledRule>.Empty;
                return;
            }

            var dict = new Dictionary<string, CompiledRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mapId, r) in rules)
            {
                if (r is null || !r.Enabled || string.IsNullOrWhiteSpace(mapId))
                    continue;
                dict[mapId] = new CompiledRule
                {
                    RequireKnifeAndShotgun = r.RequireKnifeAndShotgun,
                    Backpacks = ToSet(r.Backpacks),
                    Helmets = ToSet(r.Helmets),
                    Weapons = ToSet(r.Weapons),
                    Ammo = ToSet(r.Ammo),
                    Custom = CompileIdentifiers(r.Custom),
                };
            }
            _compiled = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private static FrozenSet<string> ToSet(List<string>? items)
        {
            if (items is null || items.Count == 0)
                return FrozenSet<string>.Empty;
            var cleaned = new List<string>(items.Count);
            foreach (var s in items)
                if (!string.IsNullOrWhiteSpace(s))
                    cleaned.Add(s.Trim());
            return cleaned.Count == 0 ? FrozenSet<string>.Empty : cleaned.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Compiles the enabled, non-empty custom identifiers of a rule into match-ready form.</summary>
        private static CompiledIdentifier[] CompileIdentifiers(List<GuardIdentifier>? ids)
        {
            if (ids is null || ids.Count == 0)
                return Array.Empty<CompiledIdentifier>();
            var result = new List<CompiledIdentifier>(ids.Count);
            foreach (var id in ids)
            {
                if (id is null || !id.Enabled || id.Gear is null || id.Gear.Count == 0)
                    continue;
                var gear = new List<(string, string)>(id.Gear.Count);
                foreach (var g in id.Gear)
                    if (!string.IsNullOrWhiteSpace(g.Slot) && !string.IsNullOrWhiteSpace(g.Short))
                        gear.Add((g.Slot.Trim(), g.Short.Trim()));
                if (gear.Count > 0)
                    result.Add(new CompiledIdentifier { Name = id.Name ?? "", MatchAll = id.MatchAll, Gear = gear.ToArray() });
            }
            return result.Count == 0 ? Array.Empty<CompiledIdentifier>() : result.ToArray();
        }

        #endregion

        /// <summary>
        /// Evaluate <paramref name="player"/> against the current <paramref name="mapId"/>.
        /// Promotes a matching AI scav to <see cref="PlayerType.AIRaider"/> and sets
        /// <see cref="Player.IsBossGuard"/>. When identification is disabled, demotes any
        /// player we previously promoted back to its original type.
        /// </summary>
        public static void Evaluate(Player player, string? mapId)
        {
            if (player is null)
                return;
            if (player.Type is not (PlayerType.AIScav or PlayerType.AIRaider))
                return;

            // Master toggle off — undo any promotion we made so the toggle is responsive.
            if (!_enabled)
            {
                Demote(player);
                return;
            }

            if (string.IsNullOrEmpty(mapId))
                return;
            // Gear not read yet — leave the current state untouched to avoid flicker.
            if (player.Equipment is null || player.Equipment.Count == 0)
                return;

            var snap = _compiled;
            if (!snap.TryGetValue(mapId, out var data))
            {
                // No (enabled) rule for this map — undo any promotion we previously made.
                Demote(player);
                return;
            }

            string? label = null;
            string? reason = null;
            bool matched;
            if (data.RequireKnifeAndShotgun)
            {
                matched = IsWoodsGuard(player);
                if (matched) reason = "Knife + 12ga";
            }
            else
            {
                matched = TryMatch(player, data, out label, out reason);
            }

            if (matched)
            {
                if (!player.IsBossGuard)
                {
                    player.IsBossGuard = true;
                    if (player.Type != PlayerType.AIRaider)
                    {
                        player.OriginalType ??= player.Type;
                        player.Type = PlayerType.AIRaider;
                    }
                    Log.WriteLine($"[GuardManager] Identified '{player.Name}' as boss guard on '{mapId}' ({reason}).");
                }
                player.BossGuardLabel = label;
                player.BossGuardMatch = reason;
            }
            else
            {
                // Gear no longer matches any rule (e.g. the user edited rules mid-raid) — demote.
                Demote(player);
            }
        }

        /// <summary>Reverts a player we previously promoted back to its original type, clearing labels.</summary>
        private static void Demote(Player player)
        {
            if (!player.IsBossGuard)
                return;
            player.IsBossGuard = false;
            player.BossGuardLabel = null;
            player.BossGuardMatch = null;
            if (player.OriginalType is { } original)
                player.Type = original;
        }

        /// <summary>
        /// Tests the flat lists then the custom identifiers, reporting the first match's
        /// <paramref name="reason"/> (always set on a hit) and <paramref name="label"/>
        /// (the custom-identifier name, or null for flat-list matches).
        /// </summary>
        private static bool TryMatch(Player p, CompiledRule d, out string? label, out string? reason)
        {
            label = null;
            reason = null;

            if (d.Helmets.Count > 0
                && p.Equipment.TryGetValue("Headwear", out var h) && h is not null && d.Helmets.Contains(h.Short))
            {
                reason = "Helmet: " + h.Short;
                return true;
            }
            if (d.Backpacks.Count > 0
                && p.Equipment.TryGetValue("Backpack", out var bp) && bp is not null && d.Backpacks.Contains(bp.Short))
            {
                reason = "Backpack: " + bp.Short;
                return true;
            }
            if (d.Weapons.Count > 0 && TryMatchWeapon(p, d.Weapons, out var wShort))
            {
                reason = "Weapon: " + wShort;
                return true;
            }
            if (d.Ammo.Count > 0 && TryMatchAmmo(p, d.Ammo, out var aShort))
            {
                reason = "Ammo: " + aShort;
                return true;
            }
            foreach (var id in d.Custom)
            {
                if (MatchesIdentifier(p, id))
                {
                    label = id.Name;
                    reason = string.IsNullOrEmpty(id.Name) ? "Custom kit" : "Kit: " + id.Name;
                    return true;
                }
            }
            return false;
        }

        private static bool TryMatchWeapon(Player p, FrozenSet<string> weapons, out string? matched)
        {
            matched = null;
            if (p.Equipment.TryGetValue("FirstPrimaryWeapon", out var w1) && w1 is not null && weapons.Contains(w1.Short)) { matched = w1.Short; return true; }
            if (p.Equipment.TryGetValue("SecondPrimaryWeapon", out var w2) && w2 is not null && weapons.Contains(w2.Short)) { matched = w2.Short; return true; }
            if (p.Equipment.TryGetValue("Holster", out var wh) && wh is not null && weapons.Contains(wh.Short)) { matched = wh.Short; return true; }
            return false;
        }

        private static bool TryMatchAmmo(Player p, FrozenSet<string> ammoSet, out string? matched)
        {
            matched = null;
            var ammo = p.InHandsAmmo;
            if (string.IsNullOrEmpty(ammo)) return false;
            foreach (var a in ammoSet)
                if (ammo.Contains(a, StringComparison.OrdinalIgnoreCase)) { matched = a; return true; }
            return false;
        }

        private static bool MatchesIdentifier(Player p, CompiledIdentifier id)
        {
            if (id.Gear.Length == 0)
                return false;
            if (id.MatchAll)
            {
                foreach (var (slot, shortName) in id.Gear)
                    if (!MatchesSlot(p, slot, shortName))
                        return false;
                return true;
            }
            foreach (var (slot, shortName) in id.Gear)
                if (MatchesSlot(p, slot, shortName))
                    return true;
            return false;
        }

        /// <summary>Matches one captured (slot, short-name) pair against the player's gear.</summary>
        private static bool MatchesSlot(Player p, string slot, string shortName)
        {
            if (slot.Equals("Ammo", StringComparison.OrdinalIgnoreCase))
            {
                var ammo = p.InHandsAmmo;
                return !string.IsNullOrEmpty(ammo) && ammo.Contains(shortName, StringComparison.OrdinalIgnoreCase);
            }
            // A weapon can sit in any of the three weapon slots — match across all of them so a
            // captured primary still counts if the AI has it slung as a secondary, etc.
            if (slot is "FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster")
                return SlotShortEquals(p, "FirstPrimaryWeapon", shortName)
                    || SlotShortEquals(p, "SecondPrimaryWeapon", shortName)
                    || SlotShortEquals(p, "Holster", shortName);
            return SlotShortEquals(p, slot, shortName);
        }

        private static bool SlotShortEquals(Player p, string slot, string shortName) =>
            p.Equipment.TryGetValue(slot, out var g) && g is not null
            && g.Short.Equals(shortName, StringComparison.OrdinalIgnoreCase);

        private static bool IsWoodsGuard(Player p)
        {
            bool knife = p.Equipment.TryGetValue("Scabbard", out var k) && k is not null
                && k.Short.Equals("camper", StringComparison.OrdinalIgnoreCase);
            bool shotgun = p.Equipment.TryGetValue("SecondPrimaryWeapon", out var sg) && sg is not null
                && sg.Long.Contains("12ga", StringComparison.OrdinalIgnoreCase);
            return knife && shotgun;
        }
    }
}
