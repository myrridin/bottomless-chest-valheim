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
    /// A client holds only the page on screen, or nothing. Everything that asks a bottomless
    /// chest what it holds on ValheimPlus's behalf comes through here, so every answer agrees.
    /// </remarks>
    internal static class ChestContents
    {
        /// <summary>
        /// Whether this inventory is a bottomless chest's on a client with the V+ integration on.
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

            if (!InventoryCapacity.IsUnbounded(inventory) || Plugin.Degraded || !ValheimPlusBridge.Attached
                || SidecarStore.IsServerAuthority || !BottomlessContainer.TryResolveInventory(inventory, out var chest))
            {
                return false;
            }

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
        internal static ItemDrop.ItemData StandIn(IndexEntry entry)
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
            standIn.m_stack = entry.Count;

            // Fermenter.RPC_AddItem names the item by its drop prefab.
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(entry.ItemId) : null;
            if (prefab != null)
            {
                standIn.m_dropPrefab = prefab;
            }

            return standIn;
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
