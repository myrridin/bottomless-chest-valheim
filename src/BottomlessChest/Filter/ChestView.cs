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
        private static float _remoteWeight;

        /// <summary>Weight of the whole chest, not just the page the client holds.</summary>
        internal static float RemoteWeight => _remoteWeight;
        private static string _remoteStoreId;
        private static bool _awaitingPage;
        /// <summary>
        /// An item offered to a chest and awaiting the server's answer.
        /// </summary>
        /// <remarks>
        /// Like the stack offer, this deliberately outlives the window. Clearing it on close
        /// meant that closing between sending and being answered left the server holding the
        /// item and the player holding it too.
        /// </remarks>
        private static ItemDrop.ItemData _pendingPut;

        private static float _pendingPutSentAt;

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

        /// <summary>
        /// Notices contents changing under an open window, since Inventory.Changed cannot.
        /// </summary>
        private static readonly ReapplyTrigger Trigger = new ReapplyTrigger();

        internal static int MatchCount => _matchCount;

        internal static int TotalCount => _remote ? _remoteTotal : (_target?.m_inventory.Count ?? 0);

        internal static bool IsRemote => _remote;

        /// <summary>The inventory currently being displayed, for identity checks.</summary>
        internal static Inventory TargetInventory => _target;

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
        /// How many items a page carries, leaving the final slot free.
        /// </summary>
        /// <remarks>
        /// A remote chest shows only what the server sent, so a completely full page has
        /// nowhere to drop anything. One reserved slot is enough, and reads as a deliberate
        /// gap rather than the empty row it used to hold back.
        ///
        /// Scrolling is unaffected: the page still covers VisibleRows - 1 complete rows,
        /// which is what the scroll clamps are derived from on both sides.
        /// </remarks>
        internal static int PageSlots => (Width * VisibleRows) - 1;

        /// <summary>True while the chest is waiting on the server, so the grid is not yet real.</summary>
        internal static bool AwaitingContents =>
            _remote ? _awaitingPage : (_owner != null && _owner.AwaitingContents);

        internal static void Begin(Inventory inventory, Core.BottomlessContainer owner)
        {
            Core.PatchAudit.ReportOnce();

            _owner = owner;
            _target = inventory;
            _query = string.Empty;
            _scrollRow = 0;
            _remote = owner != null && !Storage.SidecarStore.IsServerAuthority;
            _version = 0;
            _remoteTotal = 0;
            _matchCount = 0;
            _remoteStoreId = owner?.CurrentStoreId;

            // A different chest can hold the same number of stacks as the last one.
            Trigger.Invalidate();

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
            _owner = null;
            _target = null;
            _view = null;
            _query = string.Empty;
            _matchCount = 0;
            _scrollRow = 0;
            Trigger.Invalidate();

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
                RequestPageThrottled();
                return true;
            }

            _scrollRow = clamped;
            Reapply();
            Refresh();
            return true;
        }

        internal static int MaxScroll => MaxScrollRow();

        /// <summary>Asks for a page, no more often than the interval allows.</summary>
        private static void RequestPageThrottled()
        {
            var now = Time.realtimeSinceStartup;

            if (now - _lastPageRequestAt < PageRequestInterval)
            {
                _pageRequestPending = true;
                return;
            }

            _pageRequestPending = false;
            _lastPageRequestAt = now;
            _awaitingPage = true;
            Net.ChestRpc.RequestPage(_remoteStoreId, _query, _scrollRow);
        }

        /// <summary>Flushes a throttled request once the interval has passed.</summary>
        internal static void Tick()
        {
            if (_remote && _pageRequestPending)
            {
                RequestPageThrottled();
            }
        }

        internal static void OnInventoryChanged(Inventory inventory)
        {
            if (_remote)
            {
                return;
            }

            if (_target != null && ReferenceEquals(_target, inventory))
            {
                Plugin.Log.LogDebug($"Chest changed: {inventory.m_inventory.Count} stacks; re-laying out.");
                Reapply();
            }
            else if (_target != null)
            {
                Plugin.Log.LogDebug("Chest changed, but on a different inventory than the open view.");
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

        /// <summary>Rows a page actually shows, for sizing the scrollbar handle.</summary>
        internal static int RowsOnScreen => RowsCarried;

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

            // Recorded before the work rather than after: both exits below leave a layout
            // built for exactly this many stacks, and nothing here changes the count.
            Trigger.NoteApplied(items.Count);

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
            string storeId, long version, int total, int matches, float weight, int scrollRow,
            List<ItemDrop.ItemData> items)
        {
            if (!_remote || _target == null || storeId != _remoteStoreId)
            {
                return;
            }

            _version = version;
            _remoteTotal = total;
            _matchCount = matches;
            _remoteWeight = weight;
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

        /// <summary>
        /// Updates the totals without touching the layout on screen.
        /// </summary>
        /// <remarks>
        /// Sent after a take. Re-sending a page would compact the remaining items upwards
        /// and the window would seem to scroll out from under the cursor; leaving a gap is
        /// what vanilla does when an item is removed.
        /// </remarks>
        internal static void ApplyCounts(string storeId, long version, int total, int matches, float weight)
        {
            if (!_remote || storeId != _remoteStoreId)
            {
                return;
            }

            _version = version;
            _remoteTotal = total;
            _matchCount = matches;
            _remoteWeight = weight;
            _awaitingPage = false;
            KnownTotals[storeId] = total;

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

        /// <summary>
        /// Items offered to a chest and awaiting the server's answer.
        /// </summary>
        /// <remarks>
        /// Deliberately not cleared when the chest window closes. Holding the use key
        /// stacks and then closes the window a moment later, so tying this to the view meant
        /// the confirmation arrived with nothing to act on - the server kept the items and
        /// the player kept them too. Duplication, from exactly the state the authoritative
        /// round trip was supposed to prevent.
        /// </remarks>
        /// <summary>
        /// Items offered to each chest and awaiting that chest's answer, keyed by store.
        /// </summary>
        /// <remarks>
        /// Per chest rather than one at a time. The guard exists because the server answers
        /// with positions into the offer, so two offers to the *same* chest would make the
        /// first answer name the wrong items - offers to different chests are independent.
        /// A single global guard also broke ValheimPlus's stack-to-nearby-chests, which
        /// loops over containers and would have reached only the first of ours.
        /// </remarks>
        private static readonly Dictionary<string, PendingOffer> Offers =
            new Dictionary<string, PendingOffer>(System.StringComparer.Ordinal);

        private sealed class PendingOffer
        {
            internal List<ItemDrop.ItemData> Items;
            internal float SentAt;
        }

        private static float _lastPageRequestAt;
        private static bool _pageRequestPending;

        /// <summary>
        /// Shortest gap between page requests while scrolling.
        /// </summary>
        /// <remarks>
        /// Dragging the scrollbar across a long chest crosses hundreds of rows, and asking
        /// the server for a page at every one floods it. The window follows the drag
        /// immediately; only the fetching is rationed.
        /// </remarks>
        private const float PageRequestInterval = 0.08f;

        /// <summary>True while a scroll has outrun the last page request.</summary>
        internal static bool PageRequestPending => _pageRequestPending;

        /// <summary>How long to wait for an answer before allowing another offer.</summary>
        private const float OfferTimeoutSeconds = 5f;

        /// <summary>
        /// Offers the player's stackable items to a chest, open or not.
        /// </summary>
        /// <remarks>
        /// Takes the store id rather than reading the open view, because depositing by
        /// holding the use key happens against a closed chest.
        /// </remarks>
        internal static bool RequestStackAll(string storeId)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                return false;
            }

            var player = Player.m_localPlayer?.GetInventory();
            if (player == null)
            {
                return false;
            }

            // One offer per chest at a time, expiring so a lost answer cannot wedge it.
            if (Offers.TryGetValue(storeId, out var inFlight)
                && Time.realtimeSinceStartup - inFlight.SentAt < OfferTimeoutSeconds)
            {
                return true;
            }

            var offered = new List<ItemDrop.ItemData>();
            foreach (var item in player.m_inventory)
            {
                if (item.m_shared.m_maxStackSize > 1 && !item.m_equipped)
                {
                    offered.Add(item);
                }
            }

            Plugin.Log.LogDebug($"Offering {offered.Count} stackable item(s) to chest {storeId}.");

            if (offered.Count == 0)
            {
                return true;
            }

            var scratch = new Inventory("offer", null, Width, 64);
            scratch.m_inventory.AddRange(offered);

            var package = new ZPackage();
            scratch.Save(package);

            Offers[storeId] = new PendingOffer { Items = offered, SentAt = Time.realtimeSinceStartup };
            Net.ChestRpc.StackAll(storeId, package.GetArray());
            return true;
        }

        /// <summary>Drops the items the given chest confirmed it kept.</summary>
        internal static void ApplyStacked(string storeId, List<int> keptIndices)
        {
            var player = Player.m_localPlayer?.GetInventory();
            if (player == null || !Offers.TryGetValue(storeId, out var offer))
            {
                return;
            }

            Offers.Remove(storeId);

            foreach (var index in keptIndices)
            {
                if (index >= 0 && index < offer.Items.Count)
                {
                    player.RemoveItem(offer.Items[index]);
                }
            }

            if (keptIndices.Count > 0 && Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"$msg_added {keptIndices.Count}");
            }
        }

        /// <summary>
        /// Trims a take request to what the player can actually carry.
        /// </summary>
        /// <remarks>
        /// A page holds more than a player has room for. Asking for all of it means the
        /// server hands over items with nowhere to go, and they end up on the ground - the
        /// chest empties by more than the player receives, which reads as taking too much.
        /// One free slot per item is conservative, since stacking may let more fit, but it
        /// never scatters anything.
        /// </remarks>
        internal static List<int> TrimToCapacity(IReadOnlyList<int> pageSlots)
        {
            var room = Player.m_localPlayer?.GetInventory()?.GetEmptySlots() ?? 0;
            var trimmed = new List<int>(System.Math.Min(room, pageSlots.Count));

            for (var i = 0; i < pageSlots.Count && trimmed.Count < room; i++)
            {
                trimmed.Add(pageSlots[i]);
            }

            return trimmed;
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

            RemoveSlotsLocally(pageSlots);

            Plugin.Log.LogDebug(
                $"Requesting {absolute.Count} item(s) from chest {_remoteStoreId} at v{_version}, " +
                $"row {_scrollRow} (first index {(absolute.Count > 0 ? absolute[0] : -1)}).");

            Net.ChestRpc.Take(_remoteStoreId, _version, absolute);
        }

        /// <summary>Offers an item from the player's inventory to the chest.</summary>
        internal static bool RequestPut(ItemDrop.ItemData item)
        {
            if (!_remote || item == null)
            {
                return false;
            }

            // One at a time, but never wedged: a lost answer frees the slot after a while.
            if (_pendingPut != null && Time.realtimeSinceStartup - _pendingPutSentAt < OfferTimeoutSeconds)
            {
                return false;
            }

            var scratch = new Inventory("put", null, Width, 1);
            scratch.m_inventory.Add(item);

            var package = new ZPackage();
            scratch.Save(package);

            _pendingPut = item;
            _pendingPutSentAt = Time.realtimeSinceStartup;
            Net.ChestRpc.Put(_remoteStoreId, package.GetArray());
            return true;
        }

        /// <summary>
        /// Clears the given page slots on screen, leaving the other items in place.
        /// </summary>
        /// <remarks>
        /// Optimistic only in appearance: the items are already committed to the server by
        /// the request that accompanies this, and the totals that follow are authoritative.
        /// </remarks>
        private static void RemoveSlotsLocally(IReadOnlyList<int> pageSlots)
        {
            if (_target == null)
            {
                return;
            }

            var doomed = new HashSet<ItemDrop.ItemData>();
            foreach (var slot in pageSlots)
            {
                if (slot >= 0 && slot < _target.m_inventory.Count)
                {
                    doomed.Add(_target.m_inventory[slot]);
                }
            }

            _target.m_inventory.RemoveAll(item => doomed.Contains(item));
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
            var start = Mathf.Clamp(_windowStart, 0, source.Count);
            var end = Mathf.Clamp(Mathf.Min(_windowEnd, start + WindowSlots), start, source.Count);

            var corrected = 0;

            for (var i = start; i < end; i++)
            {
                var item = source[i];

                // The view owns the positions of what it shows. Reapply sets the same
                // values, but it is not the only thing that can move an item - vanilla
                // AddItem picks its own slot - and an item drawn from outside the window
                // indexes past the end of the grid's element list. InventoryGrid.UpdateGui
                // then throws mid-draw, leaving whatever it had already rendered on screen:
                // items that look present after being taken, until something forces a
                // rebuild. Deriving the position here makes that unrepresentable.
                var pos = GridPacker.PositionOf(i - start, Width);
                if (item.m_gridPos.x != pos.X || item.m_gridPos.y != pos.Y)
                {
                    item.m_gridPos = new Vector2i(pos.X, pos.Y);
                    corrected++;
                }

                items.Add(item);
            }

            if (corrected > 0)
            {
                Plugin.Log.LogDebug(
                    $"Repositioned {corrected} of {items.Count} shown stacks " +
                    $"(window {start}..{end} of {source.Count}).");
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

                // Anything may have added to or taken from the chest since the last frame -
                // a deposit, another mod, ValheimPlus feeding a nearby station - and none of
                // it reaches us as an event. Catch up before drawing, so an item that now
                // matches the search appears without the player retyping it.
                if (Trigger.NeedsApply(_target.m_inventory.Count))
                {
                    Reapply();
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
