using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BottomlessChest.Core;
using BottomlessChest.Logic;
using BottomlessChest.Storage;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// Answers ValheimPlus's questions about bottomless chests from a client.
    /// </summary>
    /// <remarks>
    /// On a dedicated server a client holds only the page it is looking at, and nothing while
    /// the chest is shut, so ValheimPlus finds an empty chest and silently does nothing - no
    /// crafting from it, no station pulling from it. It reaches chests through one read
    /// method and two removal overloads on InventoryAssistant, so answering those from the
    /// synced index covers building, crafting, repair and every station.
    ///
    /// Only on a client. On the server authority the chest's inventory is the real contents,
    /// and ValheimPlus should see it untouched.
    /// </remarks>
    internal static class ChestQueryPatches
    {
        private static bool _warnedRead;

        /// <summary>Stand-ins handed out for each chest, reused while its index is unchanged.</summary>
        /// <remarks>
        /// ValheimPlus asks this from the crafting panel every frame, once per requirement.
        /// Cloning a stand-in per entry per ask would be a steady stream of garbage; counts are
        /// refreshed in place instead, since a take adjusts the index without replacing it.
        /// </remarks>
        private static readonly Dictionary<string, StandIns> Cached =
            new Dictionary<string, StandIns>(StringComparer.Ordinal);

        private sealed class StandIns
        {
            internal ChestIndex Index;
            internal readonly List<ItemDrop.ItemData> Items = new List<ItemDrop.ItemData>();
            internal readonly List<int> EntryOf = new List<int>();
        }

        /// <summary>
        /// Postfix on <c>InventoryAssistant.GetNearbyChestItemsByContainerList</c>: replaces
        /// whatever a bottomless chest's local inventory contributed with its synced totals.
        /// </summary>
        /// <remarks>
        /// Replaces rather than appends. While the chest is open, its local inventory is the
        /// page on screen, which the original has already listed - appending the index as well
        /// would count those stacks twice. A chest with no index yet contributes nothing, which
        /// is where the mod was before this existed.
        /// </remarks>
        internal static void ListItemsPostfix(List<Container> nearbyChests, List<ItemDrop.ItemData> __result)
        {
            if (Plugin.Degraded || SidecarStore.IsServerAuthority || nearbyChests == null || __result == null)
            {
                return;
            }

            try
            {
                HashSet<ItemDrop.ItemData> local = null;
                List<ItemDrop.ItemData> standIns = null;

                foreach (var container in nearbyChests)
                {
                    if (container == null || !BottomlessContainer.TryResolve(container, out var bottomless))
                    {
                        continue;
                    }

                    var inventory = container.m_inventory;
                    if (inventory != null && inventory.m_inventory.Count > 0)
                    {
                        local = local ?? new HashSet<ItemDrop.ItemData>(ByReference.Instance);
                        local.UnionWith(inventory.m_inventory);
                    }

                    var storeId = bottomless.CurrentStoreId;
                    if (string.IsNullOrEmpty(storeId) || !Net.ChestRpc.Indexes.TryGet(storeId, out var index))
                    {
                        continue;
                    }

                    standIns = standIns ?? new List<ItemDrop.ItemData>();
                    standIns.AddRange(StandInsFor(storeId, index));
                }

                if (local != null)
                {
                    __result.RemoveAll(local.Contains);
                }

                if (standIns != null)
                {
                    __result.AddRange(standIns);
                }
            }
            catch (Exception ex)
            {
                // A miscounted requirement is a wrong answer; a thrown one is a broken game.
                if (!_warnedRead)
                {
                    _warnedRead = true;
                    Plugin.Log.LogWarning($"Could not describe a bottomless chest to ValheimPlus: {ex}");
                }
            }
        }

        /// <summary>
        /// Prefix on <c>InventoryAssistant.RemoveItemFromChest(Container, ItemData, int)</c>.
        /// </summary>
        /// <remarks>
        /// Any quality, because ValheimPlus's own removal matches on the shared name alone -
        /// and its needle is usually a template at quality 1, which would refuse materials it
        /// means to take.
        /// </remarks>
        internal static bool RemoveByItemPrefix(Container chest, ItemDrop.ItemData needle, int amount, ref int __result) =>
            RunRemoval(chest, needle?.m_shared?.m_name, amount, ref __result);

        /// <summary>
        /// Prefix on <c>InventoryAssistant.RemoveItemFromChest(Container, string, int)</c>.
        /// </summary>
        internal static bool RemoveByNamePrefix(Container chest, string needle, int amount, ref int __result) =>
            RunRemoval(chest, needle, amount, ref __result);

        private static bool _warnedWrite;

        private static bool RunRemoval(Container chest, string sharedName, int amount, ref int __result)
        {
            try
            {
                if (!TryTake(chest, sharedName, amount, out var taken))
                {
                    return true;
                }

                __result = taken;
                return false;
            }
            catch (Exception ex)
            {
                if (!_warnedWrite)
                {
                    _warnedWrite = true;
                    Plugin.Log.LogWarning($"Could not take from a bottomless chest for ValheimPlus: {ex}");
                }

                // The original against a client's page could remove items that exist only
                // here and report them consumed. Claiming nothing was taken is the safe answer.
                if (chest != null && !SidecarStore.IsServerAuthority && BottomlessContainer.TryResolve(chest, out _))
                {
                    __result = 0;
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Answers a removal from the index immediately, then tells the server.
        /// </summary>
        /// <remarks>
        /// ValheimPlus expects an int back and cannot wait for a round trip. The index came
        /// from the server one round trip ago and <see cref="ChestIndex.Take"/> never reports
        /// more than it holds, so the error can only fall the player's way - an occasional
        /// unpaid craft, never an overdrawn chest.
        ///
        /// Handles every bottomless chest on a client, index or not. Letting the original run
        /// would remove items from the page on screen, which exist only here, and report them
        /// consumed while the server still holds them.
        /// </remarks>
        /// <returns>False when this is not ours to answer and the original should run.</returns>
        private static bool TryTake(Container chest, string sharedName, int amount, out int taken)
        {
            taken = 0;

            if (Plugin.Degraded || SidecarStore.IsServerAuthority || chest == null
                || !BottomlessContainer.TryResolve(chest, out var bottomless))
            {
                return false;
            }

            var storeId = bottomless.CurrentStoreId;
            if (amount <= 0 || string.IsNullOrEmpty(sharedName) || string.IsNullOrEmpty(storeId)
                || !Net.ChestRpc.Indexes.TryGet(storeId, out var index))
            {
                return true;
            }

            // ValheimPlus names items by their shared name token; the index is keyed by prefab
            // name, because that is what the server stores. Resolved through the same
            // templates the stand-ins are built from.
            var asked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in index.Entries)
            {
                if (taken >= amount)
                {
                    break;
                }

                if (entry.Count <= 0 || !asked.Add(entry.ItemId))
                {
                    continue;
                }

                var template = ItemTemplates.For(entry.ItemId);
                if (template == null || template.m_shared.m_name != sharedName)
                {
                    continue;
                }

                var got = index.Take(entry.ItemId, -1, amount - taken);
                if (got > 0)
                {
                    Net.ChestRpc.TakeByName(storeId, index.Version, entry.ItemId, -1, got);
                    taken += got;
                }
            }

            return true;
        }

        /// <summary>Drops the stand-ins kept for a chest, when the chest goes.</summary>
        internal static void Forget(string storeId)
        {
            if (!string.IsNullOrEmpty(storeId))
            {
                Cached.Remove(storeId);
            }
        }

        private static List<ItemDrop.ItemData> StandInsFor(string storeId, ChestIndex index)
        {
            if (!Cached.TryGetValue(storeId, out var cached) || !ReferenceEquals(cached.Index, index))
            {
                cached = new StandIns { Index = index };

                for (var i = 0; i < index.Entries.Count; i++)
                {
                    // Through ItemTemplates so the stand-in carries the shared data
                    // ItemDrop.Awake would have given it - stack size, weight - which other
                    // mods may have adjusted.
                    var template = ItemTemplates.For(index.Entries[i].ItemId);
                    if (template == null)
                    {
                        continue;
                    }

                    var standIn = template.Clone();
                    standIn.m_quality = index.Entries[i].Quality;
                    cached.Items.Add(standIn);
                    cached.EntryOf.Add(i);
                }

                Cached[storeId] = cached;
            }

            var visible = new List<ItemDrop.ItemData>(cached.Items.Count);
            for (var i = 0; i < cached.Items.Count; i++)
            {
                var count = index.Entries[cached.EntryOf[i]].Count;
                if (count <= 0)
                {
                    continue;
                }

                cached.Items[i].m_stack = count;
                visible.Add(cached.Items[i]);
            }

            return visible;
        }

        private sealed class ByReference : IEqualityComparer<ItemDrop.ItemData>
        {
            internal static readonly ByReference Instance = new ByReference();

            public bool Equals(ItemDrop.ItemData x, ItemDrop.ItemData y) => ReferenceEquals(x, y);

            public int GetHashCode(ItemDrop.ItemData item) => RuntimeHelpers.GetHashCode(item);
        }
    }
}
