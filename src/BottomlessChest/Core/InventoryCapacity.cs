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

        internal static void Apply(Inventory inventory)
        {
            var width = ModConfig.GridWidth.Value;
            if (width < 1)
            {
                width = 1;
            }

            var rows = GridPacker.RowsForChest(inventory.m_inventory.Count, width, ModConfig.MinRows.Value);

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
                if (Unbounded.Contains(__instance))
                {
                    Apply(__instance);
                    Filter.ChestFilter.OnInventoryChanged(__instance);
                }
            }
        }
    }
}
