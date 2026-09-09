using System.Collections.Generic;
using BottomlessChest.Filter;
using BottomlessChest.Logic;
using UnityEngine;

namespace BottomlessChest.Core
{
    /// <summary>
    /// The authoritative, live contents of one chest, held while a player has it open.
    /// </summary>
    /// <remarks>
    /// Exists so a client never has to receive a whole chest. Sending complete inventories
    /// caps out around a megabyte: Jotunn slices a package into 250KB fragments and waits
    /// for the peer's send queue to drain between each, giving up after thirty seconds - so
    /// the real limit is Valheim's per-peer bandwidth, and no amount of chunking beats it.
    /// Paging makes network cost independent of how much the chest holds.
    ///
    /// Ordering is stable for as long as a session lives, and Valheim's in-use lock means
    /// only one player can have a chest open, so a page index is a safe way to name an item.
    /// Clients quote the version they acted on; anything stale is rejected and re-paged.
    /// </remarks>
    internal sealed class ChestSession
    {
        private readonly List<ItemDrop.ItemData> _ordered = new List<ItemDrop.ItemData>();

        private string _query = string.Empty;
        private bool _orderDirty = true;
        private float _totalWeight;
        private bool _weightDirty = true;

        internal ChestSession(string storeId, Inventory inventory)
        {
            StoreId = storeId;
            Inventory = inventory;
        }

        internal string StoreId { get; }

        internal Inventory Inventory { get; }

        /// <summary>
        /// Set when the store did not load completely. The session may be read, never written.
        /// </summary>
        /// <remarks>
        /// The same rule the single-player path follows: a chest that loaded short must not
        /// be written back, because what is on disk is more complete than what is in memory.
        /// Opening it read-only beats refusing to open it, which would strand every good
        /// stack in the chest over one item whose prefab no longer resolves.
        /// </remarks>
        internal bool ReadOnly { get; set; }

        /// <summary>Bumped on every change, so clients can detect they acted on stale data.</summary>
        internal long Version { get; private set; }

        /// <summary>Row the client was last shown, so a refresh can hold its place.</summary>
        internal int LastScrollRow { get; private set; }

        /// <summary>When this session was last used, for expiring abandoned ones.</summary>
        internal System.DateTime LastUsedUtc { get; private set; } = System.DateTime.UtcNow;

        internal void MarkUsed() => LastUsedUtc = System.DateTime.UtcNow;

        internal int TotalCount => Inventory.m_inventory.Count;

        internal int MatchCount
        {
            get
            {
                EnsureOrder();
                return _ordered.Count;
            }
        }

        internal void SetQuery(string query)
        {
            var incoming = query ?? string.Empty;
            if (incoming == _query)
            {
                return;
            }

            _query = incoming;
            _orderDirty = true;
        }

        internal void Touch()
        {
            Version++;
            _orderDirty = true;
            _weightDirty = true;
        }

        /// <summary>
        /// Weight of everything the chest holds.
        /// </summary>
        /// <remarks>
        /// Cached because it walks every item, and a page is requested on every keystroke.
        /// </remarks>
        internal float TotalWeight
        {
            get
            {
                if (_weightDirty)
                {
                    _weightDirty = false;
                    _totalWeight = 0f;

                    foreach (var item in Inventory.m_inventory)
                    {
                        _totalWeight += item.GetWeight();
                    }
                }

                return _totalWeight;
            }
        }

        /// <summary>Items visible at a given scroll row, at most one window's worth.</summary>
        internal List<ItemDrop.ItemData> Page(int scrollRow, int count)
        {
            EnsureOrder();

            var maxRow = Mathf.Max(0, GridPacker.RowsNeeded(_ordered.Count, ChestView.Width) - (ChestView.VisibleRows - 1));
            LastScrollRow = Mathf.Clamp(scrollRow, 0, maxRow);

            var page = new List<ItemDrop.ItemData>(count);
            var start = LastScrollRow * ChestView.Width;

            for (var i = start; i < _ordered.Count && page.Count < count; i++)
            {
                page.Add(_ordered[i]);
            }

            return page;
        }

        /// <summary>
        /// Removes items named by their position in the current match order, in whole or in
        /// part.
        /// </summary>
        /// <remarks>
        /// <paramref name="amounts"/> runs alongside <paramref name="indices"/>; 0 means the
        /// whole stack, which is what every caller that does not split sends. The amount is
        /// measured against this session's own stack, never the client's, so a request built
        /// from a stale page takes what is actually there instead of going negative.
        ///
        /// A partial take leaves the original item in place and hands back a clone carrying
        /// the split-off amount. Leaving it in place matters: the order is by name, so
        /// removing and re-adding would move the remainder to a different slot and the page
        /// under the player's cursor would jump.
        /// </remarks>
        internal List<ItemDrop.ItemData> Take(IReadOnlyList<int> indices, IReadOnlyList<int> amounts = null)
        {
            EnsureOrder();

            var taken = new List<ItemDrop.ItemData>();
            for (var i = 0; i < indices.Count; i++)
            {
                var index = indices[i];
                if (index < 0 || index >= _ordered.Count)
                {
                    continue;
                }

                var item = _ordered[index];
                var requested = amounts != null && i < amounts.Count ? amounts[i] : 0;
                var amount = StackRules.TakeAmount(requested, item.m_stack);
                if (amount <= 0)
                {
                    continue;
                }

                if (amount >= item.m_stack)
                {
                    if (Inventory.m_inventory.Remove(item))
                    {
                        taken.Add(item);
                    }

                    continue;
                }

                var part = item.Clone();
                part.m_stack = amount;
                item.m_stack -= amount;
                taken.Add(part);
            }

            if (taken.Count > 0)
            {
                Touch();
            }

            return taken;
        }

        internal void Add(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return;
            }

            Inventory.m_inventory.Add(item);
            Touch();
        }

        /// <summary>Applies the query and sort once per change, not once per request.</summary>
        private void EnsureOrder()
        {
            if (!_orderDirty)
            {
                return;
            }

            _orderDirty = false;
            _ordered.Clear();

            var items = Inventory.m_inventory;

            if (string.IsNullOrWhiteSpace(_query))
            {
                _ordered.AddRange(items);
                return;
            }

            var parsed = ItemQuery.Parse(_query);
            foreach (var item in items)
            {
                if (parsed.Matches(new ItemAdapter(item)))
                {
                    _ordered.Add(item);
                }
            }

            _ordered.Sort((left, right) =>
                ItemOrdering.ByName.Compare(new ItemAdapter(left), new ItemAdapter(right)));
        }
    }
}
