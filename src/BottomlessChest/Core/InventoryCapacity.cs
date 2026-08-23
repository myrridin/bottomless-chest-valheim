using System.Collections.Generic;
using BottomlessChest.Logic;
using BottomlessChest.Settings;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Keeps a bottomless chest's grid one row ahead of its contents.
    /// </summary>
    /// <remarks>
    /// Valheim gates capacity in exactly one way - <c>m_inventory.Count &lt; m_width *
    /// m_height</c> - so growing the grid is enough to make slots unbounded. No patch on
    /// AddItem, CanAddItem or FindEmptySlot is needed, which keeps us off three more
    /// methods that could change shape in 1.0.
    ///
    /// The invariant is "always at least one empty row". Capacity is checked before an
    /// insert and this runs after, so the spare row is what makes the next insert fit.
    /// </remarks>
    internal static class InventoryCapacity
    {
        private static readonly HashSet<Inventory> Unbounded = new HashSet<Inventory>();

        /// <summary>
        /// Suppresses per-change work during a bulk load.
        /// </summary>
        /// <remarks>
        /// Filling an inventory fires Changed more than once, and each one would re-pack and
        /// re-filter the whole chest. The loader calls Repack and Apply itself once it is
        /// done, so doing it per event is pure waste - and at ten thousand stacks it is
        /// several seconds of it.
        /// </remarks>
        internal static bool Suspended { get; set; }

        internal static void Register(Inventory inventory)
        {
            if (inventory != null && Unbounded.Add(inventory))
            {
                Apply(inventory);
            }
        }

        internal static void Forget(Inventory inventory)
        {
            if (inventory != null)
            {
                Unbounded.Remove(inventory);
            }
        }

        internal static bool IsUnbounded(Inventory inventory) => Unbounded.Contains(inventory);

        /// <summary>
        /// Rewrites every item's grid position so the contents are packed from (0,0).
        /// </summary>
        /// <remarks>
        /// Items loaded from disk keep the positions they had under whatever grid shape
        /// they were saved with. Since <see cref="Apply"/> derives height from the item
        /// count, a sparse layout leaves items sitting outside the grid, where the GUI
        /// simply does not draw them - they look lost while sitting safely in the store.
        /// Packing on load makes the count-based height exact.
        /// </remarks>
        /// <summary>Whether the current layout can be shown untouched in one window.</summary>
        internal static bool LayoutIsUsable(Inventory inventory, int rows)
        {
            var width = ModConfig.GridWidth.Value;
            if (width < 1)
            {
                width = 1;
            }

            // Cheap disqualifier first: a chest that cannot fit in one window never needs
            // the per-item check, and at ten thousand stacks that list was being built on
            // every single inventory change.
            if (inventory.m_inventory.Count > width * rows)
            {
                return false;
            }

            var positions = new List<GridPos>(inventory.m_inventory.Count);
            foreach (var item in inventory.m_inventory)
            {
                positions.Add(new GridPos(item.m_gridPos.x, item.m_gridPos.y));
            }

            return GridPacker.LayoutFitsWindow(positions, width, rows);
        }

        internal static void Repack(Inventory inventory)
        {
            var width = ModConfig.GridWidth.Value;
            if (width < 1)
            {
                width = 1;
            }

            var items = inventory.m_inventory;
            for (var i = 0; i < items.Count; i++)
            {
                var pos = GridPacker.PositionOf(i, width);
                items[i].m_gridPos = new Vector2i(pos.X, pos.Y);
            }
        }

        internal static void Apply(Inventory inventory) => ApplyFor(inventory, inventory.m_inventory.Count);

        /// <summary>
        /// Sizes the grid for a given item count, which may not be in the inventory yet.
        /// </summary>
        /// <remarks>
        /// Needed before a bulk load. Inventory.Load calls AddItem per item, and AddItem
        /// silently drops anything that will not fit the current grid - so loading into a
        /// grid sized for the previous contents quietly discards the rest.
        /// </remarks>
        internal static void ApplyFor(Inventory inventory, int count)
        {
            var width = ModConfig.GridWidth.Value;
            if (width < 1)
            {
                width = 1;
            }

            // While a chest is open its hidden items are parked below the visible window,
            // so the grid must be tall enough for the window plus everything parked under it.
            var reserved = Filter.ChestView.WindowSlots;
            var rows = GridPacker.RowsForChest(count + reserved, width, ModConfig.MinRows.Value);

            // Positions are not always ours - another mod may have sorted the chest - so the
            // grid has to be tall enough for wherever the items actually are. An item below
            // the last row is simply never drawn.
            foreach (var item in inventory.m_inventory)
            {
                if (item.m_gridPos.y >= rows)
                {
                    rows = item.m_gridPos.y + 1;
                }
            }

            var cap = ModConfig.MaxItemEntries.Value;
            if (cap > 0)
            {
                var maxRows = GridPacker.RowsNeeded(cap, width);
                if (rows > maxRows)
                {
                    rows = maxRows;
                }
            }

            inventory.m_width = width;
            inventory.m_height = rows;
        }

        /// <summary>
        /// Makes every item fill the chest from the top-left.
        /// </summary>
        /// <remarks>
        /// Vanilla TopFirst is true only for weapons, tools, shields, utility, misc and
        /// trinkets; everything else is placed from the bottom row upwards. That is fine in
        /// a fixed chest, but ours grows, so "the bottom row" is the spare row and moves
        /// every time something is added - dropping materials in scatters them down the
        /// grid one lonely row at a time.
        /// </remarks>
        [HarmonyPatch(typeof(Inventory), "TopFirst")]
        private static class TopFirstPatch
        {
            private static bool Prefix(Inventory __instance, ref bool __result)
            {
                if (!Unbounded.Contains(__instance))
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Inventory), "Changed")]
        private static class ChangedPatch
        {
            // Runs on every inventory mutation in the game, so it must stay a hash lookup.
            private static void Postfix(Inventory __instance)
            {
                if (!Unbounded.Contains(__instance) || Suspended)
                {
                    return;
                }

                Apply(__instance);
                Filter.ChestView.OnInventoryChanged(__instance);
            }
        }
    }
}
