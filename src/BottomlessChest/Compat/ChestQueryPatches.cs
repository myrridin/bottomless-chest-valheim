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
