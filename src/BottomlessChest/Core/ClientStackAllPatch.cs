using BottomlessChest.Filter;
using BottomlessChest.Storage;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Every Place Stacks into a bottomless chest on a client, from wherever it came.
    /// </summary>
    /// <remarks>
    /// Replaces two intercepts - the window's Stack button and the use-key hold's reply - that
    /// shared no code and between them hid the call ValheimPlus hooks to start its sweep. The
    /// button, the use-key hold and V+'s sweep all end in <c>Inventory.StackAll</c> on the
    /// chest's inventory, which on a client is a page or nothing; the offer goes to the server
    /// instead. V+'s own prefix and postfix still run, so its sweep starts from a bottomless
    /// chest too.
    ///
    /// After V+'s prefix, which turns <c>message</c> off when its sweep will report instead.
    /// When V+ skips the original because a sweep is already running, this still queues an
    /// offer; the offer queue makes that harmless.
    /// </remarks>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
    [HarmonyAfter(Compat.ValheimPlusBridge.Guid)]
    internal static class ClientStackAllPatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, bool message, ref int __result)
        {
            if (Plugin.Degraded || SidecarStore.IsServerAuthority || !InventoryCapacity.IsUnbounded(__instance))
            {
                return true;
            }

            var player = Player.m_localPlayer;
            if (player == null || !ReferenceEquals(fromInventory, player.GetInventory())
                || !BottomlessContainer.TryResolveInventory(__instance, out var chest))
            {
                return true;
            }

            __result = 0;

            // The original still must not run: it would move the player's items into the page,
            // which is discarded. But a refusal - a chest whose store id is not bound yet - would
            // otherwise leave the button looking broken, so say what vanilla says.
            if (!ChestView.RequestStackAll(chest.CurrentStoreId, message) && message && player != null)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_stackall_none");
            }

            return false;
        }
    }
}
