using System.Collections.Generic;
using BottomlessChest.Filter;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Turns drags into and out of a remotely-owned chest into server operations.
    /// </summary>
    /// <remarks>
    /// A remote chest's inventory holds only the items currently on screen, so a local move
    /// would be meaningless - and worse, letting the move happen and telling the server
    /// afterwards would hand the player an item the server might still hold. Both directions
    /// are therefore cancelled and replayed as requests: the server decides, and only then
    /// does anything move.
    ///
    /// Single-player is untouched; there the client is the server and the chest is held
    /// whole in memory.
    /// </remarks>
    internal static class RemoteMovePatches
    {
        private static readonly List<int> OneSlot = new List<int>(1);

        private static bool Intercept(Inventory destination, Inventory source, ItemDrop.ItemData item)
        {
            if (Plugin.Degraded || item == null)
            {
                return true;
            }

            // Out of the chest: ask for it rather than taking it.
            if (ChestView.IsRemotePage(source))
            {
                var slot = ChestView.PageSlotOf(item);
                if (slot < 0)
                {
                    return true;
                }

                OneSlot.Clear();
                OneSlot.Add(slot);
                ChestView.RequestTake(OneSlot);
                return false;
            }

            // Into the chest: offer it, and keep it until the server says it has it.
            if (ChestView.IsRemotePage(destination))
            {
                return !ChestView.RequestPut(item);
            }

            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis),
            new[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
        private static class MoveWhole
        {
            private static bool Prefix(Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item) =>
                Intercept(__instance, fromInventory, item);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis),
            new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
        private static class MovePart
        {
            private static bool Prefix(
                Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item, ref bool __result)
            {
                // Partial stacks are not split across the wire yet; the whole stack moves.
                if (!Intercept(__instance, fromInventory, item))
                {
                    __result = true;
                    return false;
                }

                return true;
            }
        }
    }
}
