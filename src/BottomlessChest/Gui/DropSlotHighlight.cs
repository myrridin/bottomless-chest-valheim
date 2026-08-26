using BottomlessChest.Filter;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Marks the reserved slot at the end of a page so it reads as a drop target.
    /// </summary>
    /// <remarks>
    /// A paged chest always keeps its final slot free, otherwise there would be nowhere to
    /// drop anything into a full chest. Left unmarked that gap looks like an accident, so
    /// it is tinted to say it is deliberate.
    ///
    /// Works on the grid's child objects rather than InventoryGrid.Element, which is a
    /// private nested type. Children are created row-major, so the child index is the slot.
    /// </remarks>
    internal static class DropSlotHighlight
    {
        private static readonly Color DropTint = new Color(0.65f, 0.85f, 0.55f, 0.55f);

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class Patch
        {
            private static void Postfix(InventoryGrid __instance)
            {
                var root = __instance.m_gridRoot;
                if (root == null)
                {
                    return;
                }

                var isPagedChest = ChestView.IsRemote
                                   && ReferenceEquals(__instance.m_inventory, ChestView.TargetInventory);

                // The reserved slot sits immediately after the items the page carries.
                var dropSlot = isPagedChest ? __instance.m_inventory.m_inventory.Count : -1;

                for (var i = 0; i < root.childCount; i++)
                {
                    var image = root.GetChild(i).GetComponent<Image>();
                    if (image == null)
                    {
                        continue;
                    }

                    // Always written, not just when tinting: these elements are reused for
                    // ordinary containers too, and a stale tint would follow them there.
                    image.color = i == dropSlot ? DropTint : Color.white;
                }
            }
        }
    }
}
