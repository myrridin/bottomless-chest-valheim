using BottomlessChest.Storage;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Stops a client accepting items into a chest whose contents live on the server.
    /// </summary>
    /// <remarks>
    /// On a dedicated server a client holds only the page it is looking at, and nothing at
    /// all while the chest is shut. Anything that adds to that inventory is writing into a
    /// phantom: the add succeeds, and the items are discarded at the next save because a
    /// client is not the authority and declines to write.
    ///
    /// ValheimPlus does exactly this. Station output is deposited with a direct
    /// <c>Inventory.AddItem</c> - it does not go through InventoryAssistant like its removals
    /// do - and is then handed to <c>ConveyContainerToNetwork</c>, which calls
    /// <c>Container.Save</c>. Against a bottomless chest on a dedicated server every smelted
    /// bar and every piece of coal took that route into nothing.
    ///
    /// Refusing the add hands the caller back to its own fallback. ValheimPlus tries the next
    /// chest and then lets vanilla <c>Smelter.Spawn</c> run, which drops the output on the
    /// ground where the player can pick it up - the same thing that happens with no chest
    /// nearby. Losing the convenience is worth not losing the item.
    ///
    /// Skipping the original rather than undoing it afterwards matters: the vanilla method
    /// mutates <c>m_stack</c> and appends as it goes, so a partial add would leave the caller's
    /// item in a state we would then have to unpick.
    ///
    /// This is the floor, not the destination. Forwarding the deposit to the server needs the
    /// synced index designed in
    /// docs/superpowers/specs/2026-09-02-craft-from-chest-on-dedicated-servers-design.md,
    /// and belongs with that work.
    ///
    /// Only the single-argument overload is patched. Player deposits reach a container through
    /// <c>MoveItemToThis</c>, which calls the private <c>AddItem(item, amount, x, y)</c>, so
    /// dragging an item into a chest is untouched. The argument types are stated explicitly
    /// because <c>AddItem</c> is overloaded four ways, and an AmbiguousMatchException thrown
    /// during patching once cost a live chest.
    /// </remarks>
    internal static class ClientDepositGuard
    {
        private static bool _explained;

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
        private static class AddItemPatch
        {
            private static bool Prefix(Inventory __instance, ref bool __result)
            {
                if (Plugin.Degraded
                    || SidecarStore.IsServerAuthority
                    || !InventoryCapacity.IsUnbounded(__instance))
                {
                    return true;
                }

                if (!_explained)
                {
                    _explained = true;
                    Plugin.Log.LogInfo(
                        "Refused an item added straight into a bottomless chest by another mod: " +
                        "this client holds only a page, so the item would have been discarded at " +
                        "the next save. The caller keeps it - ValheimPlus stations will drop " +
                        "output on the ground instead.");
                }

                __result = false;
                return false;
            }
        }
    }
}
