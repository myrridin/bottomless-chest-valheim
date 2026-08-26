using System.Collections.Generic;
using BottomlessChest.Filter;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Routes the inventory window's Take All button through the server.
    /// </summary>
    /// <remarks>
    /// Like stacking, taking has two entry points that share no code. The hover interaction
    /// goes through Container.TakeAll and the RPC handshake; the button calls
    /// Player.GetInventory().MoveAll(container.GetInventory()) directly - and MoveAll uses
    /// AddItem and RemoveItem rather than MoveItemToThis, so the move interception does not
    /// see it either.
    ///
    /// On a paged client that handed the player real items out of the window while the
    /// server kept its copy: duplication, and the chest count never moved.
    /// </remarks>
    internal static class TakeAllButtonPatch
    {
        [HarmonyPatch(typeof(InventoryGui), "OnTakeAll")]
        private static class Patch
        {
            private static bool Prefix(InventoryGui __instance)
            {
                if (Plugin.Degraded || __instance.m_currentContainer == null || Player.m_localPlayer == null)
                {
                    return true;
                }

                if (!BottomlessContainer.TryResolve(__instance.m_currentContainer, out _) || !ChestView.IsRemote)
                {
                    return true;
                }

                var page = __instance.m_currentContainer.GetInventory();
                var slots = new List<int>(page.m_inventory.Count);
                for (var i = 0; i < page.m_inventory.Count; i++)
                {
                    slots.Add(i);
                }

                ChestView.RequestTake(ChestView.TrimToCapacity(slots));
                return false;
            }
        }
    }
}
