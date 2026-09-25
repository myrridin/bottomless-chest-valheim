using System;
using System.Collections.Generic;
using BottomlessChest.Core;
using BottomlessChest.Logic;
using BottomlessChest.Storage;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// What a bottomless chest holds, answered on a client from its synced index.
    /// </summary>
    /// <remarks>
    /// A client holds only the page on screen, or nothing. Everything that answers what a
    /// bottomless chest holds comes through here, so every answer agrees - for ValheimPlus,
    /// for craft-from-chest mods asking the vanilla methods, and for the game itself.
    /// </remarks>
    internal static class ChestContents
    {
        /// <summary>
        /// Whether this inventory belongs to a bottomless chest on a client.
        /// </summary>
        /// <returns>
        /// True when the caller must answer for the chest. <paramref name="index"/> is null until
        /// the first summary arrives, and a chest with no summary holds nothing as far as another
        /// mod can tell - never whatever its page shows.
        /// </returns>
        internal static bool Resolve(Inventory inventory, out string storeId, out ChestIndex index)
        {
            storeId = null;
            index = null;

            if (!InventoryCapacity.IsUnbounded(inventory) || Plugin.Degraded
                || SidecarStore.IsServerAuthority || !BottomlessContainer.TryResolveInventory(inventory, out var chest))
            {
                return false;
            }

            // Asking is what starts this chest keeping its totals current, and keeps it doing so.
            // Any mod that asks the way the game does gets an answer; ValheimPlus is only the
            // first one this was written for.
            chest.MarkContentsWanted();

            storeId = chest.CurrentStoreId;
            if (!string.IsNullOrEmpty(storeId))
            {
                Net.ChestRpc.Indexes.TryGet(storeId, out index);
            }

            return true;
        }

        /// <summary>Whether an indexed prefab has the given shared name token.</summary>
        internal static bool IsNamed(string itemId, string sharedName)
        {
            var template = ItemTemplates.For(itemId);
            return template != null && template.m_shared.m_name == sharedName;
        }

        /// <summary>A throwaway item standing for an index entry. Never one the chest holds.</summary>
        /// <remarks>
        /// Through <see cref="ItemTemplates"/> so it carries the shared data ItemDrop.Awake would
        /// have given it - stack size, weight - which other mods may have adjusted; and at the
        /// quality, world level and cheat state the chest really holds.
        /// </remarks>
        /// <param name="clampToMaxStack">
        /// Cap the stand-in at one vanilla stack. Answering <c>GetItem</c> must: vanilla hands
        /// back one stack from the chest, never a total, and an oversized ItemData that escapes
        /// into a player inventory is written to the vanilla save that way.
        /// </param>
        internal static ItemDrop.ItemData StandIn(IndexEntry entry, bool clampToMaxStack = false)
        {
            var template = ItemTemplates.For(entry.ItemId);
            if (template == null)
            {
                return null;
            }

            var standIn = template.Clone();
            standIn.m_quality = entry.Quality;
            standIn.m_worldLevel = entry.WorldLevel;
            standIn.m_cheated = entry.Cheated;
            var max = standIn.m_shared.m_maxStackSize;
            standIn.m_stack = clampToMaxStack && max > 0 && entry.Count > max ? max : entry.Count;

            // Fermenter.RPC_AddItem names the item by its drop prefab.
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(entry.ItemId) : null;
            if (prefab != null)
            {
                standIn.m_dropPrefab = prefab;
            }

            return standIn;
        }

        /// <summary>The item type of an indexed prefab, or None when it cannot be resolved.</summary>
        internal static ItemDrop.ItemData.ItemType TypeOf(string itemId)
        {
            var template = ItemTemplates.For(itemId);
            return template != null ? template.m_shared.m_itemType : ItemDrop.ItemData.ItemType.None;
        }

        /// <summary>
        /// How many stacks an entry's total occupies, for vanilla's stacks-only counts.
        /// </summary>
        /// <remarks>
        /// An estimate: the index carries totals, not how the server split them. A consolidated
        /// chest holds full stacks and one remainder of each kind, which is what this computes.
        /// </remarks>
        internal static int StacksIn(IndexEntry entry)
        {
            var template = ItemTemplates.For(entry.ItemId);
            var max = template != null ? template.m_shared.m_maxStackSize : 1;
            if (max < 1)
            {
                max = 1;
            }

            return (entry.Count + max - 1) / max;
        }

        /// <summary>
        /// Deducts from the index at once and tells the server, which removes what is really there.
        /// </summary>
        /// <remarks>
        /// The caller has to answer synchronously; <see cref="ChestIndex.Take"/> never reports more
        /// than the index held, so the error only ever falls the player's way - an occasional
        /// unpaid item, never an overdrawn chest.
        /// </remarks>
        internal static int Take(string storeId, ChestIndex index, string sharedName, int quality, int amount, int minWorldLevel)
        {
            if (index == null || string.IsNullOrEmpty(storeId) || string.IsNullOrEmpty(sharedName) || amount <= 0)
            {
                return 0;
            }

            var taken = 0;
            var asked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in index.Entries)
            {
                if (taken >= amount)
                {
                    break;
                }

                if (entry.Count <= 0 || !asked.Add(entry.ItemId) || !IsNamed(entry.ItemId, sharedName))
                {
                    continue;
                }

                var got = index.Take(entry.ItemId, quality, amount - taken, minWorldLevel);
                if (got > 0)
                {
                    Net.ChestRpc.TakeByName(storeId, index.Version, entry.ItemId, quality, got, minWorldLevel);
                    taken += got;
                }
            }

            return taken;
        }
    }
}
