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
    /// Amounts travel with the request, so splitting a stack works in both directions. The
    /// server measures the amount against its own copy rather than trusting the client's, so
    /// a split built from a stale page takes what is really there.
    ///
    /// Single-player is untouched; there the client is the server and the chest is held
    /// whole in memory.
    /// </remarks>
    internal static class RemoteMovePatches
    {
        private static readonly List<int> OneSlot = new List<int>(1);
        private static readonly List<int> OneAmount = new List<int>(1);

        /// <summary>What should happen to a move aimed at a remotely-owned chest.</summary>
        private enum Outcome
        {
            /// <summary>Not our chest. Let the game do what it was going to do.</summary>
            RunVanilla,

            /// <summary>Replayed as a server request; the move itself must not happen.</summary>
            Sent,

            /// <summary>
            /// Ours, but we could not send it. The move must still not happen.
            /// </summary>
            /// <remarks>
            /// Letting vanilla run instead would move the item into the page inventory - a
            /// scratch object the next page wipes - which destroys it. Refusing leaves the
            /// item exactly where it was, which is what a player expects from a move that
            /// did not work.
            /// </remarks>
            Refused,
        }

        /// <param name="amount">How much of the stack is moving; 0 means all of it.</param>
        private static Outcome Intercept(
            Inventory destination, Inventory source, ItemDrop.ItemData item, int amount)
        {
            if (Plugin.Degraded || item == null)
            {
                return Outcome.RunVanilla;
            }

            // Out of the chest: ask for it rather than taking it.
            if (ChestView.IsRemotePage(source))
            {
                var slot = ChestView.PageSlotOf(item);
                if (slot < 0)
                {
                    return Outcome.RunVanilla;
                }

                OneSlot.Clear();
                OneSlot.Add(slot);
                OneAmount.Clear();
                OneAmount.Add(amount);
                ChestView.RequestTake(OneSlot, OneAmount);
                return Outcome.Sent;
            }

            // Into the chest: offer it, and keep it until the server says it has it.
            if (ChestView.IsRemotePage(destination))
            {
                return ChestView.RequestPut(item, amount) ? Outcome.Sent : Outcome.Refused;
            }

            return Outcome.RunVanilla;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis),
            new[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
        private static class MoveWhole
        {
            // This overload returns void, so there is no result to set - skipping it is the
            // whole of "the move did not happen".
            private static bool Prefix(Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item) =>
                Intercept(__instance, fromInventory, item, 0) == Outcome.RunVanilla;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis),
            new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
        private static class MovePart
        {
            private static bool Prefix(
                Inventory __instance,
                Inventory fromInventory,
                ItemDrop.ItemData item,
                int amount,
                ref bool __result)
            {
                // This is the overload the split dialog ends in: OnSplitOk sets up a drag
                // carrying an amount, and dropping it reaches InventoryGrid.DropItem, which
                // calls MoveItemToThis(from, item, amount, x, y). Dropping the amount here
                // is what used to make "take twenty" take the whole five-thousand stack and
                // scatter the overflow on the floor.
                var outcome = Intercept(__instance, fromInventory, item, amount);
                if (outcome == Outcome.RunVanilla)
                {
                    return true;
                }

                // Refused reports failure, so the caller does not treat the item as moved.
                __result = outcome == Outcome.Sent;
                return false;
            }
        }
    }
}
