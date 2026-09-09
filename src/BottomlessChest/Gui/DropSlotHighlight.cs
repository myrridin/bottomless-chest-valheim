using BottomlessChest.Filter;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Marks the slot kept free at the end of the window so it reads as a drop target.
    /// </summary>
    /// <remarks>
    /// A bottomless chest always keeps its last visible slot free, otherwise there would be
    /// nowhere to drop anything into a chest whose window is full. Unmarked, that gap looks
    /// like an accident rather than an invitation.
    ///
    /// Valheim 1.0 gave every slot a <c>m_dropFocus</c> overlay for exactly this idea, and
    /// then only drives it on touch devices:
    ///
    ///     if (ZInput.IsTouchActive())
    ///         element.m_dropFocus.color = (!element.m_used || element.m_canBeDroppedOn) ? ... ;
    ///
    /// On mouse and keyboard it is set to <c>Color.clear</c> in Awake and never touched
    /// again. So it is an unused overlay, already positioned over the slot and already the
    /// shape players associate with "you can drop here" - which makes it the right thing to
    /// borrow, and means nothing fights us for it.
    ///
    /// Its designed colour is cached as <c>DropFocusOriginalColor</c> before Awake blanks
    /// it, so using that keeps the marker looking like part of the game rather than like a
    /// mod tinting a square.
    ///
    /// This replaces tinting the element's background image, which had to guess at the
    /// slot's own colour to restore it, and only ever ran for paged chests.
    /// </remarks>
    internal static class DropSlotHighlight
    {
        /// <summary>Used only if the prefab's own drop colour is fully transparent.</summary>
        private static readonly Color Fallback = new Color(0.65f, 0.85f, 0.55f, 0.55f);

        /// <summary>
        /// Drawn in the slot so it says what it is, not merely that it is special.
        /// </summary>
        /// <remarks>
        /// A glow alone tells you a slot is different; it does not tell you it wants
        /// something put in it. This borrows the stack-count label, which is idle on an
        /// empty slot, rather than adding an object to a grid the game pools and reuses.
        /// </remarks>
        private const string DropGlyph = "+";

        private static TextAlignmentOptions _defaultAlignment;
        private static float _defaultFontSize;
        private static bool _defaultsCaptured;

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class Patch
        {
            private static void Postfix(InventoryGrid __instance)
            {
                var root = __instance.m_gridRoot;
                if (root == null || Plugin.Degraded)
                {
                    return;
                }

                // On touch the game drives this overlay itself, and its answer - every empty
                // slot glows while dragging - is a better one than ours. Leave it alone.
                if (ZInput.IsTouchActive())
                {
                    return;
                }

                var dropSlot = ChestView.IsDisplaying(__instance.m_inventory)
                    ? ChestView.DropSlotIndex
                    : -1;

                for (var i = 0; i < root.childCount; i++)
                {
                    var element = root.GetChild(i).GetComponent<InventoryElement>();
                    if (element == null || element.m_dropFocus == null)
                    {
                        continue;
                    }

                    // Cleared as well as set: these elements are pooled and reused for
                    // ordinary containers, and a marker left behind would follow them there.
                    var isDropSlot = i == dropSlot;
                    element.m_dropFocus.color = isDropSlot ? DropColour(element) : Color.clear;

                    ApplyGlyph(element, isDropSlot);
                }
            }

            private static Color DropColour(InventoryElement element)
            {
                var designed = element.DropFocusOriginalColor;
                return designed.a > 0f ? designed : Fallback;
            }

            /// <summary>Marks or unmarks the slot's idle stack-count label.</summary>
            private static void ApplyGlyph(InventoryElement element, bool isDropSlot)
            {
                var label = element.m_amount;
                if (label == null)
                {
                    return;
                }

                if (!_defaultsCaptured)
                {
                    // Every slot comes from one prefab, so one sample is the default for
                    // all of them - and it has to be taken before we overwrite anything.
                    _defaultAlignment = label.alignment;
                    _defaultFontSize = label.fontSize;
                    _defaultsCaptured = true;
                }

                if (isDropSlot)
                {
                    label.alignment = TextAlignmentOptions.Center;
                    label.fontSize = _defaultFontSize * 1.8f;
                    label.text = DropGlyph;
                    label.enabled = true;
                    return;
                }

                // Restored unconditionally. The game decides whether an occupied slot shows
                // its count, but it never touches alignment or size, so anything we changed
                // would follow this element to whatever it is reused for next.
                if (_defaultsCaptured)
                {
                    label.alignment = _defaultAlignment;
                    label.fontSize = _defaultFontSize;
                }
            }
        }
    }
}
