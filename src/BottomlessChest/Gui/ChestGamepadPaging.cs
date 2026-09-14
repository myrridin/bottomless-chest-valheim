using BottomlessChest.Filter;
using HarmonyLib;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Pages a bottomless chest when the gamepad selection is pushed past the window's edge.
    /// </summary>
    /// <remarks>
    /// Vanilla clamps the selection to the rows on screen and, at the edge, hands it to the
    /// next grid. The chest's window is only a few rows of something much taller, so pushing
    /// down on the last row pages the chest instead, and the button is consumed so vanilla
    /// does not also jump grids. At the true top or bottom of the chest it behaves as vanilla.
    /// The right stick scrolls as well; this is what makes the left stick and D-pad follow.
    /// </remarks>
    internal static class ChestGamepadPaging
    {
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGamepad")]
        private static class Patch
        {
            private static void Prefix(InventoryGrid __instance)
            {
                if (Plugin.Degraded || !ChestView.IsOpen || !ChestView.IsDisplaying(__instance.m_inventory))
                {
                    return;
                }

                if (__instance.m_uiGroup == null || !__instance.m_uiGroup.IsActive || !ZInput.IsExclusiveGamepadActive())
                {
                    return;
                }

                var down = ZInput.GetButtonDown("JoyDPadDown") || ZInput.GetButtonDown("JoyLStickDown");
                var up = ZInput.GetButtonDown("JoyDPadUp") || ZInput.GetButtonDown("JoyLStickUp");

                if (down && __instance.m_selected.y >= __instance.m_height - 1 && ChestView.ScrollRow < ChestView.MaxScroll)
                {
                    ChestView.Scroll(1);
                    ZInput.ResetButtonStatus("JoyDPadDown");
                    ZInput.ResetButtonStatus("JoyLStickDown");
                }
                else if (up && __instance.m_selected.y <= 0 && ChestView.ScrollRow > 0)
                {
                    ChestView.Scroll(-1);
                    ZInput.ResetButtonStatus("JoyDPadUp");
                    ZInput.ResetButtonStatus("JoyLStickUp");
                }
            }
        }
    }
}
