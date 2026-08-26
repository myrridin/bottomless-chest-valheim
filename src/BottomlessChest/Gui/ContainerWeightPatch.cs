using BottomlessChest.Filter;
using HarmonyLib;
using UnityEngine;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Shows the weight of the whole chest rather than of the page on screen.
    /// </summary>
    /// <remarks>
    /// Vanilla reads <c>m_currentContainer.GetInventory().GetTotalWeight()</c>, which on a
    /// paged client covers only the visible window - so a chest holding a million stacks
    /// reported the weight of about forty. The server sends the real figure with each page.
    /// </remarks>
    internal static class ContainerWeightPatch
    {
        [HarmonyPatch(typeof(InventoryGui), "UpdateContainerWeight")]
        private static class Patch
        {
            private static bool Prefix(InventoryGui __instance)
            {
                if (Plugin.Degraded || !ChestView.IsRemote || __instance.m_currentContainer == null)
                {
                    return true;
                }

                __instance.m_containerWeight.text =
                    Mathf.CeilToInt(ChestView.RemoteWeight).ToString();

                return false;
            }
        }
    }
}
