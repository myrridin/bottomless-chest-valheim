using System.Collections.Generic;
using BottomlessChest.Logic;
using BottomlessChest.Settings;
using HarmonyLib;
using UnityEngine;

namespace BottomlessChest.Filter
{
    /// <summary>
    /// Decides which slice of a bottomless chest the grid actually draws.
    /// </summary>
    /// <remarks>
    /// Owns both concerns that make an unbounded chest displayable: the search filter, and
    /// the fixed-size window that keeps the grid from growing with the contents.
    ///
    /// The window is what makes the chest scale. InventoryGrid instantiates one GameObject
    /// per slot whenever the grid's dimensions change, so a grid sized to the contents would
    /// try to create thousands of objects in a single frame. Showing a constant
    /// VisibleRows-tall window means the cost of opening a chest with ten items and one with
    /// ten thousand is identical.
    ///
    /// Both are implemented by swapping the grid's inventory reference for a view during
    /// UpdateGui. Patching Inventory.GetHeight or GetAllItems instead does not work - Mono
    /// inlines those one-line accessors, so the patches apply and never run.
    ///
    /// Grid positions in the real inventory are assigned so the visible slice occupies rows
    /// 0..VisibleRows-1 and everything else sits below. That keeps view coordinates and real
    /// coordinates identical, so clicks and drags need no translation.
    /// </remarks>
    internal static class ChestView
    {
        private static Inventory _target;
        private static Inventory _view;
        private static string _query = string.Empty;
        private static int _matchCount;
        private static int _scrollRow;
        private static int _windowStart;
        private static int _windowEnd;
        private static Core.BottomlessContainer _owner;

        // Remote mode: the server owns the contents and sends one window at a time, so
        // _target holds only what is on screen and the counts come over the wire.
        private static bool _remote;
        private static long _version;
        private static int _remoteTotal;
        private static string _remoteStoreId;
        private static bool _awaitingPage;
        private static ItemDrop.ItemData _pendingPut;

        /// <summary>
        /// Last known stack count per chest, so a closed chest can still be judged empty.
        /// </summary>
        /// <remarks>
        /// A client holds nothing while a chest is shut, so without this there is no way to
        /// tell an empty chest from an unopened one - and the difference decides whether it
        /// is safe to let someone tear it down.
        /// </remarks>
        private static readonly Dictionary<string, int> KnownTotals =
            new Dictionary<string, int>(System.StringComparer.Ordinal);

        /// <summary>Stack count for a chest if we have seen it, otherwise null.</summary>
        internal static int? KnownTotalFor(string storeId)
        {
            if (!string.IsNullOrEmpty(storeId) && KnownTotals.TryGetValue(storeId, out var total))
            {
                return total;
            }

            return null;
        }

        // Reused across keystrokes: at a hundred thousand stacks, allocating two lists per
        // character typed is a large amount of garbage for no reason.
        private static readonly List<ItemDrop.ItemData> Matched = new List<ItemDrop.ItemData>();
        private static readonly List<ItemDrop.ItemData> Rest = new List<ItemDrop.ItemData>();

        internal static int MatchCount => _matchCount;

        internal static int TotalCount => _remote ? _remoteTotal : (_target?.m_inventory.Count ?? 0);

        internal static bool IsRemote => _remote;

        internal static long Version => _version;

        internal static int ScrollRow => _scrollRow;

        internal static bool IsFiltering => _target != null && !string.IsNullOrWhiteSpace(_query);

        internal static bool IsOpen => _target != null;

        /// <summary>
        /// The items the current search matches, in display order.
        /// </summary>
        /// <remarks>
        /// A snapshot, because callers move items out of the chest while iterating.
        /// Matches are kept at the front of the inventory, so this is a prefix.
        /// </remarks>
        internal static List<ItemDrop.ItemData> MatchingItems()
        {
            var result = new List<ItemDrop.ItemData>();
            if (_target == null)
            {
                return result;
            }

            var items = _target.m_inventory;
            var end = Mathf.Min(_matchCount, items.Count);
            for (var i = 0; i < end; i++)
            {
                result.Add(items[i]);
            }

            return result;
        }

        internal static int TotalRows => GridPacker.RowsNeeded(_matchCount, Width);

        /// <summary>
        /// Grid dimensions of the chest window.
        /// </summary>
        /// <remarks>
        /// Fixed rather than configurable: Valheim's container panel is a fixed size that
        /// nothing resizes, so other values overflow the window rather than enlarging it.
        /// Eight wide matches a vanilla chest.
        /// </remarks>
        internal const int Width = 8;

        internal const int VisibleRows = 6;

        /// <summary>Grid width, exposed for server-side paging which has no view of its own.</summary>
        internal static int WidthForSession => Width;

        /// <summary>Slots reserved for the window, which hidden items are placed after.</summary>
        internal static int WindowSlots => Width * VisibleRows;

        /// <summary>
        /// How many items a page carries, leaving the last row empty.
        /// </summary>
        /// <remarks>
        /// A remote chest shows only what the server sent, so a full page has no free slot
        /// and there is nowhere to drop anything. Holding a row back keeps the chest
        /// writable however much it holds.
        /// </remarks>
        internal static int PageSlots => Width * (VisibleRows - 1);

        /// <summary>True while the chest is waiting on the server, so the grid is not yet real.</summary>
        internal static bool AwaitingContents =>
            _remote ? _awaitingPage : (_owner != null && _owner.AwaitingContents);

        internal static void Begin(Inventory inventory, Core.BottomlessContainer owner)
        {
            _owner = owner;
            _target = inventory;
            _query = string.Empty;
            _scrollRow = 0;
            _remote = owner != null && !Storage.SidecarStore.IsServerAuthority;
            _version = 0;
            _remoteTotal = 0;
            _matchCount = 0;
            _remoteStoreId = owner?.CurrentStoreId;

            if (_remote)
            {
                // Nothing is held locally until the server sends a window.
                _target.m_inventory.Clear();
                _awaitingPage = true;
                Net.ChestRpc.Open(_remoteStoreId);
                return;
            }

            Reapply();
        }

        internal static void End()
        {
            if (_remote && !string.IsNullOrEmpty(_remoteStoreId))
            {
                Net.ChestRpc.Close(_remoteStoreId);
            }

            var wasRemote = _remote;
            var previous = _target;
            _remote = false;
            _awaitingPage = false;
            _remoteStoreId = null;
            _pendingPut = null;
            _offered = null;
            _owner = null;
            _target = null;
            _view = null;
            _query = string.Empty;
            _matchCount = 0;
            _scrollRow = 0;

            // Repack only if the layout we leave behind would not be displayable; an
            // externally applied sort should survive closing the chest.
            if (!wasRemote && previous != null && !Core.InventoryCapacity.LayoutIsUsable(previous, VisibleRowsFor(previous)))
            {
                Core.InventoryCapacity.Repack(previous);
            }
        }

        internal static void SetQuery(string query)
        {
            if (_remote)
            {
                _query = query ?? string.Empty;
                _scrollRow = 0;
                _awaitingPage = true;
                Net.ChestRpc.RequestPage(_remoteStoreId, _query, 0);
                return;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            _query = query ?? string.Empty;
            _scrollRow = 0;
            Reapply();
            var filtered = timer.ElapsedMilliseconds;
            Refresh();
        }

        /// <summary>Scrolls by whole rows. Returns true if the window actually moved.</summary>
        /// <summary>
        /// Scrolls by a number of rows.
        /// </summary>
        /// <remarks>
        /// Delegates rather than duplicating: this used to have its own copy of the clamp
        /// and refresh, which meant the remote paging branch added to ScrollTo was never
        /// reached from the mouse wheel.
        /// </remarks>
        internal static bool Scroll(int rows) => rows != 0 && ScrollTo(_scrollRow + rows);

        /// <summary>Scrolls to an absolute row, clamped. Returns true if the window moved.</summary>
        internal static bool ScrollTo(int row)
        {
            if (_target == null)
            {
                return false;
            }

            var clamped = Mathf.Clamp(row, 0, MaxScrollRow());
            if (clamped == _scrollRow)
            {
                return false;
            }

            if (_remote)
            {
                _scrollRow = clamped;
                _awaitingPage = true;
                Net.ChestRpc.RequestPage(_remoteStoreId, _query, clamped);
                return true;
            }

            _scrollRow = clamped;
            Reapply();
            Refresh();
            return true;
        }

        internal static int MaxScroll => MaxScrollRow();

        internal static void OnInventoryChanged(Inventory inventory)
        {
            if (_remote)
            {
                return;
            }

            if (_target != null && ReferenceEquals(_target, inventory))
            {
                Reapply();
            }
        }

        private static int VisibleRowsFor(Inventory inventory) => VisibleRows;

        /// <summary>Rows of items a page actually carries.</summary>
        /// <remarks>
        /// A remote page holds one row fewer than the window, to keep a slot free for
        /// dropping. The client's scroll clamp has to agree with the server's or the last
        /// row becomes unreachable.
        /// </remarks>
        private static int RowsCarried => _remote ? VisibleRows - 1 : VisibleRows;

        private static int MaxScrollRow()
        {
            var rows = TotalRows - RowsCarried;
            return rows < 0 ? 0 : rows;
        }

        /// <summary>
        /// Partitions matches to the front, then assigns grid positions so the visible
        /// window lands on rows 0..VisibleRows-1.
        /// </summary>
        private static void Reapply()
        {
            if (_target == null || _remote)
            {
                return;
            }

            var items = _target.m_inventory;

            if (string.IsNullOrWhiteSpace(_query))
            {
                _matchCount = items.Count;
            }
            else
            {
                var parsed = ItemQuery.Parse(_query);
                var matched = Matched;
                var rest = Rest;
                matched.Clear();
                rest.Clear();

                foreach (var item in items)
                {
                    if (parsed.Matches(new ItemAdapter(item)))
                    {
                        matched.Add(item);
                    }
                    else
                    {
                        rest.Add(item);
                    }
                }

                // Only the matches are sorted. Leaving the remainder alone means an order
                // applied by another mod survives once the filter is cleared.
                matched.Sort((left, right) =>
                    ItemOrdering.ByName.Compare(new ItemAdapter(left), new ItemAdapter(right)));

                _matchCount = matched.Count;
                items.Clear();
                items.AddRange(matched);
                items.AddRange(rest);
            }

            _scrollRow = Mathf.Clamp(_scrollRow, 0, MaxScrollRow());

            // Only rewrite positions when the window actually demands it. Repacking
            // unconditionally would silently overwrite a sort applied by another mod on
            // every inventory change, which makes this chest hostile to inventory mods.
            var needsLayout = !string.IsNullOrWhiteSpace(_query)
                              || _scrollRow > 0
                              || !Core.InventoryCapacity.LayoutIsUsable(_target, VisibleRows);

            if (!needsLayout)
            {
                _windowStart = 0;
                _windowEnd = items.Count;
                Core.InventoryCapacity.Apply(_target);
                return;
            }

            _windowStart = _scrollRow * Width;
            _windowEnd = Mathf.Min(_matchCount, _windowStart + WindowSlots);

            var hidden = 0;
            for (var i = 0; i < items.Count; i++)
            {
                GridPos pos;

                if (i >= _windowStart && i < _windowEnd)
                {
                    pos = GridPacker.PositionOf(i - _windowStart, Width);
                }
                else
                {
                    // Parked below the window. The view never contains these, so their exact
                    // position only has to be unique and out of the way.
                    pos = GridPacker.PositionOf(WindowSlots + hidden++, Width);
                }

                items[i].m_gridPos = new Vector2i(pos.X, pos.Y);
            }

            Core.InventoryCapacity.Apply(_target);
        }

        private static void Refresh()
        {
            var gui = InventoryGui.instance;
            if (gui != null && gui.m_currentContainer != null)
            {
                gui.m_containerGrid.UpdateInventory(gui.m_currentContainer.GetInventory(), null, gui.m_dragItem);
            }
        }

        /// <summary>Accepts a window of items sent by the server.</summary>
        internal static void ApplyPage(
            string storeId, long version, int total, int matches, int scrollRow, List<ItemDrop.ItemData> items)
        {
            if (!_remote || _target == null || storeId != _remoteStoreId)
            {
                return;
            }

            _version = version;
            _remoteTotal = total;
            _matchCount = matches;
            KnownTotals[storeId] = total;
            _scrollRow = scrollRow;
            _awaitingPage = false;

            Plugin.Log.LogDebug(
                $"Page: {items.Count} items at row {scrollRow}, {matches}/{total} match, " +
                $"max row {MaxScrollRow()}, v{version}.");

            var live = _target.m_inventory;
            live.Clear();
            live.AddRange(items);

            for (var i = 0; i < live.Count; i++)
            {
                var pos = GridPacker.PositionOf(i, Width);
                live[i].m_gridPos = new Vector2i(pos.X, pos.Y);
            }

            _target.m_width = Width;
            _target.m_height = VisibleRows;

            Refresh();
        }

        /// <summary>Receives items the server has removed from the chest for us.</summary>
        internal static void ApplyGranted(List<ItemDrop.ItemData> items)
        {
            var player = Player.m_localPlayer?.GetInventory();
            if (player == null)
            {
                return;
            }

            foreach (var item in items)
            {
                if (!player.AddItem(item))
                {
                    // Nowhere to put it: drop at the player's feet rather than lose it,
                    // since the server has already given it up.
                    ItemDrop.DropItem(item, item.m_stack, Player.m_localPlayer.transform.position, Quaternion.identity);
                }
            }
        }

        /// <summary>The server took the item we offered, so our copy can go.</summary>
        internal static void ApplyAccepted()
        {
            if (_pendingPut == null)
            {
                return;
            }

            Player.m_localPlayer?.GetInventory()?.RemoveItem(_pendingPut);
            _pendingPut = null;
        }

        /// <summary>Items the player has that are worth offering to a chest.</summary>
        private static List<ItemDrop.ItemData> _offered;

        /// <summary>Offers the player's stackable items to a remote chest.</summary>
        internal static bool RequestStackAll()
        {
            if (!_remote)
            {
                return false;
            }

            var player = Player.m_localPlayer?.GetInventory();
            if (player == null)
            {
                return false;
            }

            _offered = new List<ItemDrop.ItemData>();
            foreach (var item in player.m_inventory)
            {
                if (item.m_shared.m_maxStackSize > 1 && !item.m_equipped)
                {
                    _offered.Add(item);
                }
            }

            if (_offered.Count == 0)
            {
                return true;
            }

            var scratch = new Inventory("offer", null, Width, 64);
            scratch.m_inventory.AddRange(_offered);

            var package = new ZPackage();
            scratch.Save(package);

            Net.ChestRpc.StackAll(_remoteStoreId, package.GetArray());
            return true;
        }

        /// <summary>Drops the items the server confirmed it kept.</summary>
        internal static void ApplyStacked(List<int> keptIndices)
        {
            var player = Player.m_localPlayer?.GetInventory();
            if (player == null || _offered == null)
            {
                return;
            }

            foreach (var index in keptIndices)
            {
                if (index >= 0 && index < _offered.Count)
                {
                    player.RemoveItem(_offered[index]);
                }
            }

            if (keptIndices.Count > 0 && Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"$msg_added {keptIndices.Count}");
            }

            _offered = null;
        }

        /// <summary>Asks the server for items at the given page slots.</summary>
        internal static void RequestTake(IReadOnlyList<int> pageSlots)
        {
            if (!_remote || pageSlots.Count == 0)
            {
                return;
            }

            var absolute = new List<int>(pageSlots.Count);
            foreach (var slot in pageSlots)
            {
                absolute.Add((_scrollRow * Width) + slot);
            }

            Net.ChestRpc.Take(_remoteStoreId, _version, absolute);
        }

        /// <summary>Offers an item from the player's inventory to the chest.</summary>
        internal static bool RequestPut(ItemDrop.ItemData item)
        {
            if (!_remote || item == null || _pendingPut != null)
            {
                return false;
            }

            var scratch = new Inventory("put", null, Width, 1);
            scratch.m_inventory.Add(item);

            var package = new ZPackage();
            scratch.Save(package);

            _pendingPut = item;
            Net.ChestRpc.Put(_remoteStoreId, package.GetArray());
            return true;
        }

        /// <summary>Whether this inventory is the page of a remotely-owned chest.</summary>
        internal static bool IsRemotePage(Inventory inventory) =>
            _remote && _target != null && ReferenceEquals(_target, inventory);

        /// <summary>Slot of an item within the page, or -1.</summary>
        internal static int PageSlotOf(ItemDrop.ItemData item) =>
            _target == null ? -1 : _target.m_inventory.IndexOf(item);

        /// <summary>Builds the stand-in inventory holding just the windowed items.</summary>
        private static Inventory BuildView()
        {
            if (_view == null)
            {
                _view = new Inventory(_target.m_name, _target.m_bkg, Width, VisibleRows);
            }

            _view.m_width = Width;
            _view.m_height = VisibleRows;

            var items = _view.m_inventory;
            items.Clear();

            var source = _target.m_inventory;
            var end = Mathf.Min(_windowEnd, source.Count);
            for (var i = _windowStart; i < end; i++)
            {
                items.Add(source[i]);
            }

            return _view;
        }

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class GridRenderScope
        {
            private static Inventory _swappedOut;

            private static void Prefix(InventoryGrid __instance)
            {
                _swappedOut = null;

                // In remote mode the inventory already is the page, so there is nothing to
                // stand in for.
                if (_remote || _target == null || !ReferenceEquals(__instance.m_inventory, _target))
                {
                    return;
                }

                _swappedOut = __instance.m_inventory;
                __instance.m_inventory = BuildView();
            }

            // A Finalizer runs even if UpdateGui throws, so the grid can never be left
            // pointing at the view.
            private static void Finalizer(InventoryGrid __instance)
            {
                if (_swappedOut != null)
                {
                    __instance.m_inventory = _swappedOut;
                    _swappedOut = null;
                }
            }
        }
    }
}
