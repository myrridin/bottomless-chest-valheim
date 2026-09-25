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
    /// Since 0.3.0 the item is forwarded to the server instead, which holds the real chest,
    /// and refusal is the fallback for when forwarding is not possible: no network yet, no
    /// store id, or an item from the player's own inventory (see <see cref="TryForward"/>).
    /// A forwarded item the server cannot keep comes back and is dropped at the chest.
    ///
    /// Since 0.4.0 that applies to whichever mod added the item. It used to require the
    /// ValheimPlus integration, which left every other mod's deposit refused.
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

        /// <summary>
        /// Hands an item another mod added to the server, which holds the real chest.
        /// </summary>
        /// <remarks>
        /// Not the player's own items. Place Stacks and ValheimPlus's auto-stack sweep add
        /// those through this same method, and the stack-all offer already deposits them -
        /// forwarding them as well would put them in twice. Refusing them leaves that path to
        /// do its job, as before.
        /// </remarks>
        private static bool TryForward(Inventory inventory, ItemDrop.ItemData item)
        {
            // Any mod, not only ValheimPlus: a client holds a page, so an item added here is
            // discarded at the next save unless the server is told. Refusing is the fallback for
            // when it cannot be told, which is what 0.2.1 did for everyone.
            if (item == null || !Net.ChestRpc.CanSend)
            {
                return false;
            }

            var player = Player.m_localPlayer?.GetInventory();
            if (player != null && player.ContainsItem(item))
            {
                return false;
            }

            BottomlessContainer.TryResolveInventory(inventory, out var owner);

            var storeId = owner?.CurrentStoreId;
            if (string.IsNullOrEmpty(storeId))
            {
                return false;
            }

            var bytes = Net.ChestRpc.Serialize(new System.Collections.Generic.List<ItemDrop.ItemData> { item });
            if (bytes == null)
            {
                return false;
            }

            Net.ChestRpc.DepositForward(storeId, bytes);
            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
        private static class AddItemPatch
        {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (Plugin.Degraded
                    || SidecarStore.IsServerAuthority
                    || !InventoryCapacity.IsUnbounded(__instance))
                {
                    return true;
                }

                if (TryForward(__instance, item))
                {
                    __result = true;
                    return false;
                }

                if (!_explained)
                {
                    _explained = true;
                    Plugin.Log.LogInfo(
                        "Refused an item added straight into a bottomless chest by another mod: " +
                        "this client holds only a page, so the item would have been discarded at " +
                        "the next save, and it could not be handed to the server. The caller keeps " +
                        "it - ValheimPlus stations drop output on the ground instead.");
                }

                __result = false;
                return false;
            }
        }
    }
}
