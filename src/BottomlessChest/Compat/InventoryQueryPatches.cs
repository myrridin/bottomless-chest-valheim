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
                        if (entry.Count > 0
                            && (quality < 0 || entry.Quality == quality)
                            && (isPrefabName ? entry.ItemId == name : ChestContents.IsNamed(entry.ItemId, name)))
                        {
                            __result = ChestContents.StandIn(entry);
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
