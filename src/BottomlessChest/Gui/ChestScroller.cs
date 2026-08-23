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

            var delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f)
            {
                return;
            }

            // Wheel up should move up through the contents.
            ChestView.Scroll(delta > 0f ? -1 : 1);
        }
    }
}
