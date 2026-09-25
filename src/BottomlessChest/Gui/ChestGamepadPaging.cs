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
        private const float FirstRepeat = 0.35f;
        private const float Repeat = 0.12f;

        /// <summary>Direction being held against an edge: 1 down, -1 up, 0 none.</summary>
        private static int _held;

        private static float _nextStep;

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

                // Held, not just pressed: vanilla only reacts to the press, so a stick held against
                // the edge paged once and stopped. A press steps at once; holding repeats.
                var atBottom = __instance.m_selected.y >= __instance.m_height - 1 && ChestView.ScrollRow < ChestView.MaxScroll;
                var atTop = __instance.m_selected.y <= 0 && ChestView.ScrollRow > 0;

                var direction =
                    atBottom && (ZInput.GetButton("JoyDPadDown") || ZInput.GetButton("JoyLStickDown")) ? 1 :
                    atTop && (ZInput.GetButton("JoyDPadUp") || ZInput.GetButton("JoyLStickUp")) ? -1 :
                    0;

                if (direction == 0)
                {
                    _held = 0;
                    return;
                }

                var pressed = direction > 0
                    ? ZInput.GetButtonDown("JoyDPadDown") || ZInput.GetButtonDown("JoyLStickDown")
                    : ZInput.GetButtonDown("JoyDPadUp") || ZInput.GetButtonDown("JoyLStickUp");

                var now = UnityEngine.Time.unscaledTime;
                if (_held != direction)
                {
                    // Arrived at the edge. A press this frame steps now; a stick already held
                    // from moving the selection there waits the first-repeat delay.
                    _held = direction;
                    _nextStep = now + FirstRepeat;
                    if (!pressed)
                    {
                        return;
                    }
                }
                else if (now < _nextStep && !pressed)
                {
                    return;
                }
                else
                {
                    _nextStep = now + (pressed ? FirstRepeat : Repeat);
                }

                ChestView.Scroll(direction);

                // Consumed, so vanilla does not also hand the selection to the next grid.
                if (direction > 0)
                {
                    ZInput.ResetButtonStatus("JoyDPadDown");
                    ZInput.ResetButtonStatus("JoyLStickDown");
                }
                else
                {
                    ZInput.ResetButtonStatus("JoyDPadUp");
                    ZInput.ResetButtonStatus("JoyLStickUp");
                }
            }
        }
    }
}
