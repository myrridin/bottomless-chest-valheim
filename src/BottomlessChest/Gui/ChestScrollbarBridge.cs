using BottomlessChest.Filter;
using UnityEngine;
using UnityEngine.UI;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Makes the container scrollbar drive the chest window.
    /// </summary>
    /// <remarks>
    /// Vanilla scrolls a chest by making the grid tall enough for every slot and letting a
    /// ScrollRect slide it. The grid here is a fixed window - that is what makes a million
    /// stacks affordable - so its content always fits and the bar has nothing to move.
    /// It is therefore repurposed: handle size and position are driven from the virtual
    /// window, and dragging it moves that window.
    ///
    /// The owning ScrollRect lives on the ContainerGrid object, a *sibling* of the
    /// scrollbar and the same object that carries the InventoryGrid component. An earlier
    /// attempt searched the scrollbar's ancestors for it, never found it, and so never
    /// detached it - leaving vanilla rewriting size and value every frame while this wrote
    /// different ones, which looked like the handle flickering.
    /// </remarks>
    internal sealed class ChestScrollbarBridge : MonoBehaviour
    {
        /// <summary>Smallest handle to leave grabbable, however long the chest is.</summary>
        private const float MinimumHandle = 0.06f;

        private Scrollbar _bar;
        private ScrollRect _detachedFrom;
        private bool _writing;

        internal void Bind(InventoryGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            _bar = grid.m_scrollbar;
            if (_bar == null)
            {
                return;
            }

            var owner = grid.GetComponent<ScrollRect>();
            if (owner != null && owner.verticalScrollbar == _bar)
            {
                _detachedFrom = owner;
                owner.verticalScrollbar = null;
            }

            _bar.onValueChanged.AddListener(OnBarMoved);
            Sync();
        }

        private void OnBarMoved(float value)
        {
            if (_writing || !ChestView.IsOpen)
            {
                return;
            }

            ChestView.ScrollTo(Mathf.RoundToInt(TopFraction(value) * ChestView.MaxScroll));
        }

        private void Update()
        {
            if (_bar != null && ChestView.IsOpen)
            {
                Sync();
            }
        }

        private void Sync()
        {
            var total = ChestView.TotalRows;
            var carried = ChestView.RowsOnScreen;
            var max = ChestView.MaxScroll;

            _writing = true;

            _bar.size = total <= carried
                ? 1f
                : Mathf.Clamp(carried / (float)total, MinimumHandle, 1f);

            var fraction = max <= 0 ? 0f : ChestView.ScrollRow / (float)max;
            _bar.SetValueWithoutNotify(TopFraction(fraction));

            _writing = false;
        }

        /// <summary>
        /// Converts between scrollbar value and "0 at the top", whichever way the bar runs.
        /// </summary>
        /// <remarks>
        /// This container's bar is BottomToTop, so its value is inverted relative to a row
        /// index. Reading the direction rather than assuming keeps it correct if the layout
        /// ever changes.
        /// </remarks>
        private float TopFraction(float value) =>
            _bar.direction == Scrollbar.Direction.BottomToTop || _bar.direction == Scrollbar.Direction.RightToLeft
                ? 1f - value
                : value;

        private void OnDestroy()
        {
            if (_bar != null)
            {
                _bar.onValueChanged.RemoveListener(OnBarMoved);
                _bar.size = 1f;
            }

            if (_detachedFrom != null)
            {
                _detachedFrom.verticalScrollbar = _bar;
                _detachedFrom = null;
            }
        }
    }
}
