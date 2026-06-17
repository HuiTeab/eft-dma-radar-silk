// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// Reads held-weapon firearm state: fire mode, magazine ammo count and capacity.
    /// Chambered ammo shortname is owned by <see cref="HandsManager"/>.
    /// </summary>
    internal static class FirearmManager
    {
        internal static void Refresh(ulong playerBase, Player player)
        {
            if (!player.IsWeaponInHands)
            {
                ClearFirearmState(player);
                return;
            }

            try
            {
                ulong weaponBase = HandsManager.GetCachedItem(playerBase);
                if (weaponBase == 0)
                {
                    ClearFirearmState(player);
                    return;
                }

                player.FireMode = TryReadFireMode(weaponBase);

                if (TryReadMagazineCounts(weaponBase, out int current, out int capacity))
                {
                    player.AmmoInMag = current;
                    player.MagCapacity = capacity;
                }
                else
                {
                    player.AmmoInMag = -1;
                    player.MagCapacity = -1;
                }
            }
            catch
            {
                ClearFirearmState(player);
            }
        }

        private static void ClearFirearmState(Player player)
        {
            player.FireMode = null;
            player.AmmoInMag = -1;
            player.MagCapacity = -1;
        }

        private static string? TryReadFireMode(ulong weaponBase)
        {
            if (!Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon.FireMode, out var fireModePtr, false) || fireModePtr == 0)
                return null;
            if (!Memory.TryReadValue<byte>(fireModePtr + Offsets.FireModeComponent.FireMode, out var raw, false))
                return null;
            return raw switch
            {
                0 => "single",
                1 => "burst",
                2 => "auto",
                3 => "semi",
                4 => "doublesemi",
                _ => null,
            };
        }

        private static bool TryReadMagazineCounts(ulong weaponBase, out int current, out int capacity)
        {
            current = 0;
            capacity = 0;
            int chamberSlotCount = 0;

            // Chambers
            if (Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon.Chambers, out var chambersPtr, false) && chambersPtr != 0)
            {
                if (TryReadChamberArray(chambersPtr, out int chambers, out int loaded))
                {
                    chamberSlotCount = chambers;
                    capacity += chambers;
                    current += loaded;
                }
            }

            // Magazine slot
            if (!Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon._magSlotCache, out var magSlot, false) || magSlot == 0)
                return capacity > 0;
            if (!Memory.TryReadPtr(magSlot + Offsets.Slot.ContainedItem, out var magItem, false) || magItem == 0)
                return capacity > 0;

            // Revolver/chamber path
            if (Memory.TryReadPtr(magItem + Offsets.LootItemMod.Slots, out var magChambersPtr, false)
                && magChambersPtr != 0
                && TryReadChamberArray(magChambersPtr, out int magChambers, out int magLoaded)
                && magChambers > 0)
            {
                capacity += magChambers;
                current += magLoaded;
                return true;
            }

            // Standard magazine cartridges path
            if (!Memory.TryReadPtr(magItem + Offsets.LootItemMagazine.Cartridges, out var cartridgesPtr, false) || cartridgesPtr == 0)
                return capacity > 0;

            if (chamberSlotCount > 0)
                capacity -= chamberSlotCount;

            if (!Memory.TryReadValue<int>(cartridgesPtr + Offsets.StackSlot.MaxCount, out var slotMax, false))
                return capacity > 0;
            capacity += slotMax;

            if (!Memory.TryReadPtr(cartridgesPtr + Offsets.StackSlot._items, out var stackListPtr, false) || stackListPtr == 0)
                return capacity > 0;

            if (!Memory.TryReadPtr(stackListPtr + 0x10, out var stackArr, false) || stackArr == 0)
                return capacity > 0;
            if (!Memory.TryReadValue<int>(stackListPtr + 0x18, out var stackCount, false) || stackCount <= 0)
                return capacity > 0;

            int safeCount = Math.Min(stackCount, 8);
            for (int i = 0; i < safeCount; i++)
            {
                if (Memory.TryReadPtr(stackArr + 0x20 + (ulong)i * 0x8, out var sp, false) && sp != 0
                    && Memory.TryReadValue<int>(sp + Offsets.MagazineClass.StackObjectsCount, out var cnt, false))
                    current += cnt;
            }

            return capacity > 0;
        }

        /// <summary>
        /// Resolves the template of the first loaded round in the weapon (chamber first, then the
        /// magazine), used by silent-aim ballistics to read muzzle velocity / drag. Returns false if
        /// nothing chambered or loaded.
        /// </summary>
        internal static bool TryGetAmmoTemplate(ulong weaponBase, out ulong ammoTemplate)
        {
            ammoTemplate = 0;
            try
            {
                // 1) Round in the chamber.
                if (Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon.Chambers, out var chambersPtr, false) && chambersPtr != 0
                    && TryFirstChamberRound(chambersPtr, out var chamberRound)
                    && Memory.TryReadPtr(chamberRound + Offsets.LootItem.Template, out ammoTemplate, false) && ammoTemplate != 0)
                    return true;

                // 2) Magazine.
                if (!Memory.TryReadPtr(weaponBase + Offsets.LootItemWeapon._magSlotCache, out var magSlot, false) || magSlot == 0)
                    return false;
                if (!Memory.TryReadPtr(magSlot + Offsets.Slot.ContainedItem, out var magItem, false) || magItem == 0)
                    return false;

                // 2a) Revolver-style chambers on the magazine item.
                if (Memory.TryReadPtr(magItem + Offsets.LootItemMod.Slots, out var magChambersPtr, false) && magChambersPtr != 0
                    && TryFirstChamberRound(magChambersPtr, out var magChamberRound)
                    && Memory.TryReadPtr(magChamberRound + Offsets.LootItem.Template, out ammoTemplate, false) && ammoTemplate != 0)
                    return true;

                // 2b) Standard cartridge stack.
                if (!Memory.TryReadPtr(magItem + Offsets.LootItemMagazine.Cartridges, out var cartridgesPtr, false) || cartridgesPtr == 0)
                    return false;
                if (!Memory.TryReadPtr(cartridgesPtr + Offsets.StackSlot._items, out var stackListPtr, false) || stackListPtr == 0)
                    return false;
                if (!Memory.TryReadPtr(stackListPtr + 0x10, out var stackArr, false) || stackArr == 0)
                    return false;
                if (!Memory.TryReadValue<int>(stackListPtr + 0x18, out var stackCount, false) || stackCount <= 0)
                    return false;

                int safe = Math.Min(stackCount, 8);
                for (int i = 0; i < safe; i++)
                {
                    if (Memory.TryReadPtr(stackArr + 0x20 + (ulong)i * 0x8, out var roundItem, false) && roundItem != 0
                        && Memory.TryReadPtr(roundItem + Offsets.LootItem.Template, out ammoTemplate, false) && ammoTemplate != 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Computes the weapon's muzzle-velocity multiplier (1.0 = no change) from the weapon barrel's
        /// own <c>WeaponTemplate.Velocity</c> % plus every attachment's <c>ModTemplate.Velocity</c> %.
        /// Used by silent-aim ballistics so the muzzle velocity reflects the actual build, not just the
        /// base round speed. Returns 1.0 on any failure (sanity-clamped to [0.01, 2.0]).
        /// </summary>
        internal static float GetVelocityModifier(ulong weaponBase)
        {
            float velPct = 0f;
            try
            {
                if (Memory.TryReadPtr(weaponBase + Offsets.LootItem.Template, out var weaponTpl, false) && weaponTpl != 0
                    && Memory.TryReadValue<float>(weaponTpl + Offsets.WeaponTemplate.Velocity, out var wv))
                    velPct += wv;
                AccumulateAttachmentVelocity(weaponBase, ref velPct, 0);
            }
            catch { }
            float mod = 1f + velPct / 100f;
            return (mod is >= 0.01f and <= 2f) ? mod : 1f;
        }

        /// <summary>Recurses the weapon's mod slots, summing each attachment's velocity % modifier.</summary>
        private static void AccumulateAttachmentVelocity(ulong itemBase, ref float velPct, int depth)
        {
            if (depth > 6)
                return;
            if (!Memory.TryReadPtr(itemBase + Offsets.LootItemMod.Slots, out var slotsArr, false) || slotsArr == 0)
                return;
            if (!Memory.TryReadValue<int>(slotsArr + 0x18, out int count, false) || count <= 0 || count > 32)
                return;
            for (int i = 0; i < count; i++)
            {
                if (!Memory.TryReadPtr(slotsArr + 0x20 + (ulong)i * 0x8, out var slot, false) || slot == 0)
                    continue;
                if (!Memory.TryReadPtr(slot + Offsets.Slot.ContainedItem, out var item, false) || item == 0)
                    continue;
                if (Memory.TryReadPtr(item + Offsets.LootItem.Template, out var tpl, false) && tpl != 0
                    && Memory.TryReadValue<float>(tpl + Offsets.ModTemplate.Velocity, out var mv))
                    velPct += mv;
                AccumulateAttachmentVelocity(item, ref velPct, depth + 1);
            }
        }

        /// <summary>Finds the first chamber slot that contains a round and returns that round item.</summary>
        private static bool TryFirstChamberRound(ulong arrayPtr, out ulong round)
        {
            round = 0;
            if (!Memory.TryReadValue<int>(arrayPtr + 0x18, out var arrCount, false) || arrCount <= 0 || arrCount > 16)
                return false;
            for (int i = 0; i < arrCount; i++)
            {
                if (!Memory.TryReadPtr(arrayPtr + 0x20 + (ulong)i * 0x8, out var slotPtr, false) || slotPtr == 0)
                    continue;
                if (Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var item, false) && item != 0)
                {
                    round = item;
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadChamberArray(ulong arrayPtr, out int count, out int loaded)
        {
            count = 0;
            loaded = 0;

            if (!Memory.TryReadValue<int>(arrayPtr + 0x18, out var arrCount, false) || arrCount <= 0 || arrCount > 16)
                return false;
            count = arrCount;

            for (int i = 0; i < arrCount; i++)
            {
                if (!Memory.TryReadPtr(arrayPtr + 0x20 + (ulong)i * 0x8, out var slotPtr, false) || slotPtr == 0)
                    continue;
                if (Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var item, false) && item != 0)
                    loaded++;
            }

            return true;
        }
    }
}
