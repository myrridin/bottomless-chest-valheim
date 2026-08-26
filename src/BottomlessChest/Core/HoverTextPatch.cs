using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Stops a bottomless chest describing itself as empty when it is not.
    /// </summary>
    /// <remarks>
    /// Vanilla decides from <c>m_inventory.NrOfItems()</c>. On a client that is only ever
    /// the page currently on screen - and nothing at all while the chest is closed - so a
    /// full chest reads as "( empty )" from the outside.
    /// </remarks>
    internal static class HoverTextPatch
    {
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static class Patch
        {
            private static bool Prefix(Container __instance, ref string __result)
            {
                if (Plugin.Degraded || !BottomlessContainer.TryResolve(__instance, out _))
                {
                    return true;
                }

                if (__instance.m_checkGuardStone &&
                    !PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false))
                {
                    return true;
                }

                __result = Localization.instance.Localize(
                    __instance.m_name + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open");

                return false;
            }
        }
    }
}
