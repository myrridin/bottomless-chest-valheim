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
                        Forget(item);
                    }

                    continue;
                }

                var part = item.Clone();
                part.m_stack = amount;
                item.m_stack -= amount;
                taken.Add(part);

                // It has room now, so it is where the next deposit of its kind should go.
                Reopen(item);
            }

            if (taken.Count > 0)
            {
                Touch();
            }

            return taken;
        }

        /// <summary>
        /// Stacks with room left, one per kind of item.
        /// </summary>
        /// <remarks>
        /// This is what makes consolidation affordable in a chest of any size. Once the
        /// contents are collapsed there is at most one part-filled stack of each kind - every
        /// other stack is full, by definition - so this dictionary is the size of the item
        /// catalogue, not the size of the chest. A deposit is then a lookup rather than a
        /// walk, and stays a lookup at ten million stacks.
        /// </remarks>
        private readonly Dictionary<string, ItemDrop.ItemData> _openStacks =
            new Dictionary<string, ItemDrop.ItemData>(System.StringComparer.Ordinal);

        private bool _consolidated;

        /// <summary>What has to match for two stacks to be one stack.</summary>
        internal static string StackKey(ItemDrop.ItemData item) => StackConsolidation.StackKey(item);

        /// <summary>
        /// Collapses every stack that can be collapsed, once, and indexes what is left open.
        /// </summary>
        /// <remarks>
        /// Runs when the session opens, before any page has been sent, and never again -
        /// after this, <see cref="Deposit"/> keeps the contents collapsed as they arrive.
        /// Doing it here rather than lazily matters: it renumbers the contents, and a client
        /// holding a page numbered the old way would take the wrong items. Opening is the one
        /// moment when no page exists yet.
        /// </remarks>
        internal void Consolidate()
        {
            if (_consolidated)
            {
                return;
            }

            _consolidated = true;

            if (!StackConsolidation.Collapse(Inventory, out var collapsed, _openStacks))
            {
                // Contents in memory can no longer be trusted, so this session stops writing
                // and the store keeps what it already had.
                ReadOnly = true;
                return;
            }

            if (collapsed <= 0)
            {
                return;
            }

            Touch();
            Plugin.Log.LogInfo(
                $"Consolidated chest {StoreId}: {collapsed} part-stack(s) merged away, " +
                $"{Inventory.m_inventory.Count} left.");
        }

        /// <summary>
        /// Adds an item, collapsing it into stacks that have room before making a new one.
        /// </summary>
        /// <remarks>
        /// The counterpart to <see cref="Consolidate"/>: that one collapses what is already
        /// there, this one keeps it collapsed. Both directions matter, because a chest that
        /// is tidied once and then fragmented again by every deposit is not tidy.
        /// </remarks>
        internal void Deposit(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return;
            }

            var max = item.m_shared.m_maxStackSize;
            if (max <= 1)
            {
                Add(item);
                return;
            }

            var key = StackKey(item);

            if (_openStacks.TryGetValue(key, out var open) && !ReferenceEquals(open, item))
            {
                var moved = StackRules.MergeAmount(item.m_stack, open.m_stack, max);
                open.m_stack += moved;
                item.m_stack -= moved;

                if (open.m_stack >= max)
                {
                    _openStacks.Remove(key);
                }

                if (item.m_stack <= 0)
                {
                    Touch();
                    return;
                }
            }

            Inventory.m_inventory.Add(item);
            if (item.m_stack < max)
            {
                _openStacks[key] = item;
            }

            Touch();
        }

        /// <summary>
        /// Adds an item as its own stack, merging nothing.
        /// </summary>
        /// <remarks>
        /// The restore path uses this: when a reply cannot be sent, the items already removed
        /// go back exactly as they were rather than being folded into something else.
        /// Consolidation is given up rather than guessed at, so the index is dropped and
        /// rebuilt the next time the chest is opened.
        /// </remarks>
        internal void Add(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return;
            }

            Inventory.m_inventory.Add(item);
            _openStacks.Clear();
            _consolidated = false;
            Touch();
        }

        /// <summary>Drops a stack from the open-stack index if it was the one held there.</summary>
        private void Forget(ItemDrop.ItemData item)
        {
            var key = StackKey(item);
            if (_openStacks.TryGetValue(key, out var open) && ReferenceEquals(open, item))
            {
                _openStacks.Remove(key);
            }
        }

        /// <summary>
        /// Records a stack that now has room, so the next deposit of its kind finds it.
        /// </summary>
        /// <remarks>
        /// Indexes only; it deliberately does not merge. This runs from inside
        /// <see cref="Take"/>, which is working through indices into a list it has already
        /// fixed, and merging would move contents that a later index in the same request
        /// still refers to. Taking part of a stack is meant to leave a remainder, so nothing
        /// here is untidy; the rare case of a second part-stack of one kind is collapsed the
        /// next time the chest is opened.
        /// </remarks>
        private void Reopen(ItemDrop.ItemData item)
        {
            var max = item.m_shared.m_maxStackSize;
            if (max <= 1 || item.m_stack >= max)
            {
                return;
            }

            var key = StackKey(item);
            if (!_openStacks.ContainsKey(key))
            {
                _openStacks[key] = item;
            }
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
