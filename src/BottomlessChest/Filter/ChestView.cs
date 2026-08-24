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

        // Reused across keystrokes: at a hundred thousand stacks, allocating two lists per
        // character typed is a large amount of garbage for no reason.
        private static readonly List<ItemDrop.ItemData> Matched = new List<ItemDrop.ItemData>();
        private static readonly List<ItemDrop.ItemData> Rest = new List<ItemDrop.ItemData>();

        internal static int MatchCount => _matchCount;

        internal static int TotalCount => _target?.m_inventory.Count ?? 0;

        internal static int ScrollRow => _scrollRow;

        internal static bool IsFiltering => _target != null && !string.IsNullOrWhiteSpace(_query);

        internal static bool IsOpen => _target != null;

        internal static int TotalRows => Width < 1 ? 0 : GridPacker.RowsNeeded(_matchCount, Width);

        internal static int VisibleRows
        {
            get
            {
                var rows = ModConfig.VisibleRows.Value;
                return rows < 1 ? 1 : rows;
            }
        }

        private static int Width
        {
            get
            {
                var width = ModConfig.GridWidth.Value;
                return width < 1 ? 1 : width;
            }
        }

        /// <summary>Slots reserved for the window, which hidden items are placed after.</summary>
        internal static int WindowSlots => Width * VisibleRows;

        /// <summary>True while the chest is waiting on the server, so the grid is not yet real.</summary>
        internal static bool AwaitingContents => _owner != null && _owner.AwaitingContents;

        internal static void Begin(Inventory inventory, Core.BottomlessContainer owner)
        {
            _owner = owner;
            _target = inventory;
            _query = string.Empty;
            _scrollRow = 0;
            Reapply();
        }

        internal static void End()
        {
            var previous = _target;
            _owner = null;
            _target = null;
            _view = null;
            _query = string.Empty;
            _matchCount = 0;
            _scrollRow = 0;

            // Repack only if the layout we leave behind would not be displayable; an
            // externally applied sort should survive closing the chest.
            if (previous != null && !Core.InventoryCapacity.LayoutIsUsable(previous, VisibleRowsFor(previous)))
            {
                Core.InventoryCapacity.Repack(previous);
            }
        }

        internal static void SetQuery(string query)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            _query = query ?? string.Empty;
            _scrollRow = 0;
            Reapply();
            var filtered = timer.ElapsedMilliseconds;
            Refresh();
        }

        /// <summary>Scrolls by whole rows. Returns true if the window actually moved.</summary>
        internal static bool Scroll(int rows)
        {
            if (_target == null || rows == 0)
            {
                return false;
            }

            var before = _scrollRow;
            _scrollRow = Mathf.Clamp(_scrollRow + rows, 0, MaxScrollRow());

            if (_scrollRow == before)
            {
                return false;
            }

            Reapply();
            Refresh();
            return true;
        }

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

            _scrollRow = clamped;
            Reapply();
            Refresh();
            return true;
        }

        internal static int MaxScroll => MaxScrollRow();

        internal static void OnInventoryChanged(Inventory inventory)
        {
            if (_target != null && ReferenceEquals(_target, inventory))
            {
                Reapply();
            }
        }

        private static int VisibleRowsFor(Inventory inventory) => VisibleRows;

        private static int MaxScrollRow()
        {
            var rows = TotalRows - VisibleRows;
            return rows < 0 ? 0 : rows;
        }

        /// <summary>
        /// Partitions matches to the front, then assigns grid positions so the visible
        /// window lands on rows 0..VisibleRows-1.
        /// </summary>
        private static void Reapply()
        {
            if (_target == null)
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

                if (ModConfig.SortFilteredResults.Value)
                {
                    // Only the matches are sorted. Leaving the remainder alone means an
                    // order applied by another mod survives once the filter is cleared.
                    matched.Sort((left, right) =>
                        ItemOrdering.ByName.Compare(new ItemAdapter(left), new ItemAdapter(right)));
                }

                _matchCount = matched.Count;
                items.Clear();
                items.AddRange(matched);
                items.AddRange(rest);
            }

            var width = Width;
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

            _windowStart = _scrollRow * width;
            _windowEnd = Mathf.Min(_matchCount, _windowStart + WindowSlots);

            var hidden = 0;
            for (var i = 0; i < items.Count; i++)
            {
                GridPos pos;

                if (i >= _windowStart && i < _windowEnd)
                {
                    pos = GridPacker.PositionOf(i - _windowStart, width);
                }
                else
                {
                    // Parked below the window. The view never contains these, so their exact
                    // position only has to be unique and out of the way.
                    pos = GridPacker.PositionOf(WindowSlots + hidden++, width);
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

        /// <summary>Builds the stand-in inventory holding just the windowed items.</summary>
        private static Inventory BuildView()
        {
            var width = Width;

            if (_view == null)
            {
                _view = new Inventory(_target.m_name, _target.m_bkg, width, VisibleRows);
            }

            _view.m_width = width;
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

                if (_target == null || !ReferenceEquals(__instance.m_inventory, _target))
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
