using System;
using HarmonyLib;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// Answers vanilla's questions about a bottomless chest's contents from its index, on a client.
    /// </summary>
    /// <remarks>
    /// ValheimPlus asks a chest's inventory directly in about ten places the InventoryAssistant
    /// hooks do not cover: which chest a station's output goes to, whether a cooking station or
    /// fermenter can pull, choose-one-ingredient recipes, and fireplace fuel. On a client that
    /// inventory is a page or nothing. Patched once here rather than at each V+ site, so anything
    /// V+ adds later is covered too. Every other inventory pays one hash lookup.
    ///
    /// <c>RemoveItem(string, ...)</c> is included because V+'s fireplace swaps the chest's
    /// inventory in and vanilla then removes the fuel from it: from a page, that fuel was free.
    ///
    /// Argument types are explicit: HaveItem, GetItem and RemoveItem are all overloaded, and an
    /// ambiguous match during patching once cost a live chest.
    /// </remarks>
    internal static class InventoryQueryPatches
    {
        private static bool _warned;

        private static void Warn(Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning($"Could not answer for a bottomless chest's contents: {ex}");
            }
        }

        private static int MinWorldLevel(bool match) => match ? Game.m_worldLevel : -1;

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), new[] { typeof(string), typeof(bool) })]
        private static class HaveItemPatch
        {
            private static bool Prefix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = index != null
                        && index.Count(id => ChestContents.IsNamed(id, name), -1, MinWorldLevel(matchWorldLevel)) > 0;
                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = false;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems), new[] { typeof(string), typeof(int), typeof(bool) })]
        private static class CountItemsPatch
        {
            private static bool Prefix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = index == null
                        ? 0
                        : index.Count(
                            id => name == null || ChestContents.IsNamed(id, name), quality, MinWorldLevel(matchWorldLevel));
                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = 0;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetItem), new[] { typeof(string), typeof(int), typeof(bool) })]
        private static class GetItemPatch
        {
            private static bool Prefix(Inventory __instance, string name, int quality, bool isPrefabName, ref ItemDrop.ItemData __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = null;
                    if (index == null)
                    {
                        return false;
                    }

                    foreach (var entry in index.Entries)
                    {
                        // Vanilla GetItem filters on world level with no flag to turn it off,
                        // unlike HaveItem, CountItems and RemoveItem. Answering without it hands
                        // a station an item its own removal would then refuse to take, and it
                        // retries for ever.
                        if (entry.Count > 0
                            && (quality < 0 || entry.Quality == quality)
                            && entry.WorldLevel >= Game.m_worldLevel
                            && (isPrefabName ? entry.ItemId == name : ChestContents.IsNamed(entry.ItemId, name)))
                        {
                            __result = ChestContents.StandIn(entry, clampToMaxStack: true);
                            break;
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Counts that name several items or an item type at once.
        /// </summary>
        /// <remarks>
        /// Vanilla's own gates use these - <c>Smelter.CanAddOre</c> asks by name - and they read
        /// the page like every other question did before 0.3.0.
        /// </remarks>
        private static int CountMatching(
            Logic.ChestIndex index, Func<Logic.IndexEntry, bool> matches, int quality, bool matchWorldLevel, bool stacksOnly)
        {
            if (index == null)
            {
                return 0;
            }

            var min = MinWorldLevel(matchWorldLevel);
            var total = 0;
            foreach (var entry in index.Entries)
            {
                if (entry.Count <= 0
                    || (quality >= 0 && entry.Quality != quality)
                    || (min >= 0 && entry.WorldLevel < min)
                    || !matches(entry))
                {
                    continue;
                }

                total += stacksOnly ? ChestContents.StacksIn(entry) : entry.Count;
            }

            return total;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItemsByName), new[] { typeof(string[]), typeof(int), typeof(bool), typeof(bool) })]
        private static class CountItemsByNamePatch
        {
            private static bool Prefix(
                Inventory __instance, string[] names, int quality, bool matchWorldLevel, bool stacksOnly, ref int __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = CountMatching(
                        index,
                        entry => names == null || System.Array.Exists(names, n => ChestContents.IsNamed(entry.ItemId, n)),
                        quality,
                        matchWorldLevel,
                        stacksOnly);
                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = 0;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItemsByType), new[] { typeof(ItemDrop.ItemData.ItemType), typeof(int), typeof(bool), typeof(bool) })]
        private static class CountItemsByTypePatch
        {
            private static bool Prefix(
                Inventory __instance, ItemDrop.ItemData.ItemType type, int quality, bool matchWorldLevel, bool stacksOnly, ref int __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = CountMatching(
                        index,
                        entry => type == ItemDrop.ItemData.ItemType.None || ChestContents.TypeOf(entry.ItemId) == type,
                        quality,
                        matchWorldLevel,
                        stacksOnly);
                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = 0;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItemsByType), new[] { typeof(ItemDrop.ItemData.ItemType[]), typeof(int), typeof(bool), typeof(bool) })]
        private static class CountItemsByTypesPatch
        {
            private static bool Prefix(
                Inventory __instance, ItemDrop.ItemData.ItemType[] types, int quality, bool matchWorldLevel, bool stacksOnly, ref int __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = CountMatching(
                        index,
                        entry => types == null || System.Array.IndexOf(types, ChestContents.TypeOf(entry.ItemId)) >= 0,
                        quality,
                        matchWorldLevel,
                        stacksOnly);
                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = 0;
                    return false;
                }
            }
        }

        /// <summary>
        /// Removing a stand-in: the item handed out by <see cref="GetItemPatch"/> is not in the
        /// page, so vanilla would refuse it and whatever asked would try again for ever.
        /// </summary>
        /// <remarks>
        /// Only for an item the inventory does not hold. A real page item is vanilla's business,
        /// and the paging code removes those itself.
        /// </remarks>
        private static bool RemoveStandIn(Inventory inventory, ItemDrop.ItemData item, int amount, ref bool result)
        {
            if (item == null || inventory.m_inventory.Contains(item)
                || !ChestContents.Resolve(inventory, out var storeId, out var index))
            {
                return true;
            }

            var wanted = amount > 0 && amount < item.m_stack ? amount : item.m_stack;
            result = ChestContents.Take(storeId, index, item.m_shared.m_name, item.m_quality, wanted, -1) > 0;
            return false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(ItemDrop.ItemData) })]
        private static class RemoveStandInPatch
        {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                try
                {
                    return RemoveStandIn(__instance, item, 0, ref __result);
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = false;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(ItemDrop.ItemData), typeof(int) })]
        private static class RemoveStandInAmountPatch
        {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int amount, ref bool __result)
            {
                try
                {
                    return RemoveStandIn(__instance, item, amount, ref __result);
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = false;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
        private static class RemoveByNamePatch
        {
            private static bool Prefix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out var storeId, out var index))
                    {
                        return true;
                    }

                    ChestContents.Take(storeId, index, name, itemQuality, amount, MinWorldLevel(worldLevelBased));
                    return false;
                }
                catch (Exception ex)
                {
                    // Never let the original remove from a page: that is the free item.
                    Warn(ex);
                    return false;
                }
            }
        }
    }
}
