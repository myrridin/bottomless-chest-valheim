using BottomlessChest.Filter;
using UnityEngine;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Scrolls the chest window with the mouse wheel.
    /// </summary>
    /// <remarks>
    /// The grid is a fixed-height window onto the contents, so scrolling moves the window
    /// rather than the view rectangle - there is no ScrollRect and no off-screen slots.
    /// Lives on the search field so it is destroyed with it.
    /// </remarks>
    internal sealed class ChestScroller : MonoBehaviour
    {
        private void Update()
        {
            if (!ChestView.IsOpen)
            {
                return;
            }

            // Flush any page request the scroll throttle is holding back.
            ChestView.Tick();

            ScrollWithWheel();
        }

        /// <summary>Wheel movement not yet adding up to a whole notch.</summary>
        private float _wheel;

        /// <summary>
        /// One row per whole notch of the wheel, and only with the pointer over the chest.
        /// </summary>
        /// <remarks>
        /// This used to scroll a row for any reported movement above 0.01, wherever the pointer
        /// was. A smooth-scrolling or high-resolution wheel, or a touchpad, reports small
        /// fractions continuously, so the window crept down a row at a time while the player
        /// was only hovering items - and wheeling over the player's own inventory scrolled the
        /// chest too. Fractions now accumulate into notches, and are dropped while the pointer
        /// is elsewhere.
        /// </remarks>
        private void ScrollWithWheel()
        {
            var delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f)
            {
                return;
            }

            if (!PointerOverChest())
            {
                _wheel = 0f;
                return;
            }

            // Wheel up moves up through the contents.
            _wheel += delta;
            while (_wheel >= 1f)
            {
                _wheel -= 1f;
                ChestView.Scroll(-1);
            }

            while (_wheel <= -1f)
            {
                _wheel += 1f;
                ChestView.Scroll(1);
            }
        }

        private static bool PointerOverChest()
        {
            var gui = InventoryGui.instance;
            var panel = gui != null ? gui.m_container : null;
            if (panel == null)
            {
                return false;
            }

            var canvas = panel.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(panel, Input.mousePosition, camera);
        }
    }
}
