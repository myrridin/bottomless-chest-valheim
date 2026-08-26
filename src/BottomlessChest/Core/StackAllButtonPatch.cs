using BottomlessChest.Filter;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Routes the inventory window's Stack button through the server.
    /// </summary>
    /// <remarks>
    /// Stacking has two entry points that share no code. Holding the use key goes through
    /// Container.StackAll and the RPC handshake; the button in the window calls
    /// <c>m_currentContainer.GetInventory().StackAll(...)</c> directly. On a paged client
    /// that inventory is only the window, so the button silently ignored everything not on
    /// screen while the hold-key path worked.
    /// </remarks>
    internal static class StackAllButtonPatch
    {
        [HarmonyPatch(typeof(InventoryGui), "OnStackAll")]
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

                return !ChestView.RequestStackAll();
            }
        }
    }
}
