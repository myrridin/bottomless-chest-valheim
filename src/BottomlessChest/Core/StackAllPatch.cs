using BottomlessChest.Filter;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Makes "Stack" reach the whole chest, not just the page on screen.
    /// </summary>
    /// <remarks>
    /// Vanilla calls <c>m_inventory.StackAll(...)</c>, and on a client that inventory holds
    /// only the visible window - so stacking could merge into whatever happened to be shown
    /// and silently ignore the rest of the chest. The offer goes to the server instead,
    /// which can see everything.
    /// </remarks>
    internal static class StackAllPatch
    {
        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        private static class Patch
        {
            private static bool Prefix(Container __instance, bool granted)
            {
                if (Plugin.Degraded || !granted || Player.m_localPlayer == null)
                {
                    return true;
                }

                if (!BottomlessContainer.TryResolve(__instance, out _) || !ChestView.IsRemote)
                {
                    return true;
                }

                return !ChestView.RequestStackAll();
            }
        }
    }
}
