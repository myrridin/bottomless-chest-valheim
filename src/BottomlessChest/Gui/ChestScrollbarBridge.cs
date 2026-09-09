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
    ///
    /// Detaching means inheriting a job as well as dropping one. Valheim never touches
    /// <c>InventoryGrid.m_scrollbar</c> in code - it is a bare field, wired in the prefab -
    /// so the only thing deciding whether the bar is *shown* is the ScrollRect's own
    /// auto-hide, which shows it exactly when the content is taller than the viewport. A
    /// fixed window is never taller than the viewport, so the moment the window stopped
    /// being six rows in a four-row panel, the ScrollRect hid the bar, and detaching froze
    /// it hidden. Whoever detaches the bar owns its visibility too; that is what
    /// <see cref="Sync"/> does now, from the virtual window rather than the grid.
    /// </remarks>
    internal sealed class ChestScrollbarBridge : MonoBehaviour
    {
        /// <summary>
        /// Smallest handle to leave grabbable, however long the chest is.
        /// </summary>
        /// <remarks>
        /// A true proportional handle is a hairline once a chest holds thousands of rows,
        /// so it is floored at something you can actually grab. The trade is that the
        /// handle stops representing how much of the chest is on screen.
        /// </remarks>
        private const float MinimumHandle = 0.12f;

        private Scrollbar _bar;
        private ScrollRect _detachedFrom;
        private bool _writing;

        /// <summary>The bar's own visibility, to hand back exactly as it was found.</summary>
        private bool _barWasActive;

        /// <summary>Whether the ScrollRect really owned this bar before we took it.</summary>
        private bool _hadBar;

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

            _barWasActive = _bar.gameObject.activeSelf;

            // A null verticalScrollbar is claimed as well as a matching one. It means an
            // earlier bridge detached the bar and was destroyed without putting it back;
            // taking ownership here is what lets this one hand it over on the way out,
            // instead of the detach leaking for the rest of the session.
            var owner = grid.GetComponent<ScrollRect>();
            if (owner != null && (owner.verticalScrollbar == _bar || owner.verticalScrollbar == null))
            {
                // Whether it had one decides whether it gets one back. A ScrollRect that
                // legitimately ships without a vertical scrollbar must not be handed ours on
                // the way out - vanilla would start driving the bar every frame, which is the
                // flickering this class exists to stop, reached from the other side.
                _hadBar = owner.verticalScrollbar == _bar;
                _detachedFrom = owner;
                owner.verticalScrollbar = null;
            }

            Plugin.Log.LogDebug(
                $"Scrollbar bridge bound: bar was {(_barWasActive ? "visible" : "hidden")}, " +
                $"ScrollRect {(_detachedFrom == null ? "not claimed" : "detached")}.");

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

            // Shown exactly when there is somewhere to scroll to, which is what the
            // ScrollRect would have decided if it still owned the bar. A chest that fits on
            // one page gets no bar, the same as a vanilla container that fits.
            var wanted = max > 0;
            if (_bar.gameObject.activeSelf != wanted)
            {
                _bar.gameObject.SetActive(wanted);
            }

            _writing = true;

            _bar.size = total <= carried
                ? 1f
                : Mathf.Clamp(carried / (float)total, MinimumHandle, 1f);

            // Do not rewrite the handle while a scroll is still catching up: the row is
            // quantised, so writing it back mid-drag drags the handle away from the cursor.
            if (!ChestView.PageRequestPending)
            {
                var fraction = max <= 0 ? 0f : ChestView.ScrollRow / (float)max;
                _bar.SetValueWithoutNotify(TopFraction(fraction));
            }

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

                // Handed back as found. Past this point the ScrollRect is managing it again
                // and will correct the visibility on its next layout pass, but it must not
                // inherit a hidden bar from a window it had nothing to do with.
                if (_bar.gameObject.activeSelf != _barWasActive)
                {
                    _bar.gameObject.SetActive(_barWasActive);
                }
            }

            if (_detachedFrom != null)
            {
                if (_hadBar)
                {
                    _detachedFrom.verticalScrollbar = _bar;
                }

                _detachedFrom = null;
            }
        }
    }
}
