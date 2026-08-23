using System.Collections.Generic;
using BottomlessChest.Logic;
using BottomlessChest.Settings;
using HarmonyLib;
using UnityEngine;

namespace BottomlessChest.Filter
{
    /// <summary>
    /// Hides non-matching items by moving them past the end of the visible grid.
    /// </summary>
    /// <remarks>
    /// This works because Valheim reads the grid's size two different ways. The GUI sizes
    /// itself from <c>Inventory.GetHeight()</c>, while capacity checks read the
    /// <c>m_height</c> field directly. Shrinking only the method hides rows without making
    /// the chest full, so items can still be added while a filter is active - they simply
    /// land out of view until it is cleared.
    ///
    /// Matching items are moved to the front of the list, so "hidden" means "in a row the
    /// GUI was never told about" rather than anything being removed.
    /// </remarks>
    internal static class ChestFilter
    {
        private static Inventory _target;
        private static string _query = string.Empty;
        private static int _matchCount;
        private static Inventory _view;
        private static int _renderScopeHits;
        private static int _trimHits;

        internal static string Query => _query;

        internal static int MatchCount => _matchCount;

        internal static int TotalCount => _target?.m_inventory.Count ?? 0;

        internal static bool IsActive => _target != null && !string.IsNullOrWhiteSpace(_query);

        internal static void Begin(Inventory inventory)
        {
            _target = inventory;
            _query = string.Empty;
            Reapply();
        }

        internal static void End()
        {
            var previous = _target;
            _target = null;
            _view = null;
            _query = string.Empty;
            _matchCount = 0;

            // Leave the chest packed as the player last saw it rather than in filter order.
            if (previous != null)
            {
                Core.InventoryCapacity.Repack(previous);
            }
        }

        internal static void SetQuery(string query)
        {
            _query = query ?? string.Empty;
            _renderScopeHits = _trimHits = 0;
            Reapply();
            Refresh();

            var rows = GridPacker.RowsForChest(_matchCount, ModConfig.GridWidth.Value, ModConfig.MinRows.Value);
            Plugin.Log.LogInfo(
                $"Filter '{_query}': {_matchCount}/{TotalCount} match, {rows} rows shown; " +
                $"view swaps={_renderScopeHits}, rebuilds={_trimHits}");
        }

        internal static void OnInventoryChanged(Inventory inventory)
        {
            if (_target != null && ReferenceEquals(_target, inventory))
            {
                Reapply();
            }
        }

        /// <summary>Partitions matches to the front and repacks positions.</summary>
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
                Core.InventoryCapacity.Repack(_target);
                return;
            }

            var parsed = ItemQuery.Parse(_query);
            var matched = new List<ItemDrop.ItemData>(items.Count);
            var rest = new List<ItemDrop.ItemData>();

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

            _matchCount = matched.Count;

            items.Clear();
            items.AddRange(matched);
            items.AddRange(rest);

            Core.InventoryCapacity.Repack(_target);
        }

        private static void Refresh()
        {
            var gui = InventoryGui.instance;
            if (gui != null && gui.m_currentContainer != null)
            {
                gui.m_containerGrid.UpdateInventory(gui.m_currentContainer.GetInventory(), null, gui.m_dragItem);
            }
        }

        /// <summary>
        /// Shows the grid a filtered stand-in while it draws itself.
        /// </summary>
        /// <remarks>
        /// The obvious approach - patching Inventory.GetHeight and GetAllItems so the grid
        /// sees fewer rows and fewer items - does not work. Both are one-line accessors and
        /// Mono inlines them, so UpdateGui never calls the patched methods at all. The
        /// patches apply cleanly and simply never run.
        ///
        /// So instead the grid's own inventory reference is swapped for a view holding just
        /// the matches, and restored the moment it is done. The view shares ItemData
        /// references with the real inventory, so nothing is copied and edits act on the
        /// real items. Everything outside this window - saving, weight, TakeAll - still
        /// sees the whole chest.
        /// </remarks>
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class GridRenderScope
        {
            private static Inventory _swappedOut;

            private static void Prefix(InventoryGrid __instance)
            {
                _swappedOut = null;

                if (_target == null
                    || !ReferenceEquals(__instance.m_inventory, _target)
                    || string.IsNullOrWhiteSpace(_query))
                {
                    ReportSkip(__instance);
                    return;
                }

                _renderScopeHits++;
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

        private static float _lastSkipReport;

        /// <summary>
        /// Explains, at most once a second, why a redraw did not get the filtered view.
        /// </summary>
        private static void ReportSkip(InventoryGrid grid)
        {
            if (_target == null || string.IsNullOrWhiteSpace(_query))
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastSkipReport < 1f)
            {
                return;
            }

            _lastSkipReport = Time.realtimeSinceStartup;

            var actual = grid.m_inventory;
            Plugin.Log.LogWarning(
                $"Redraw skipped the filter. grid='{grid.name}' " +
                $"gridInv={(actual == null ? "null" : actual.m_name + "/" + actual.m_inventory.Count + " items")} " +
                $"target={_target.m_name}/{_target.m_inventory.Count} items " +
                $"same={ReferenceEquals(actual, _target)}");
        }

        /// <summary>Builds the stand-in inventory holding only the matching items.</summary>
        private static Inventory BuildView()
        {
            var width = ModConfig.GridWidth.Value;
            if (width < 1)
            {
                width = 1;
            }

            var rows = GridPacker.RowsForChest(_matchCount, width, ModConfig.MinRows.Value);

            if (_view == null)
            {
                _view = new Inventory(_target.m_name, _target.m_bkg, width, rows);
            }

            _view.m_width = width;
            _view.m_height = rows;

            var items = _view.m_inventory;
            items.Clear();

            // Matches were sorted to the front of the real inventory and repacked, so their
            // grid positions are already correct for this view.
            var source = _target.m_inventory;
            var take = _matchCount < source.Count ? _matchCount : source.Count;
            for (var i = 0; i < take; i++)
            {
                items.Add(source[i]);
            }

            _trimHits++;
            return _view;
        }
    }
}
