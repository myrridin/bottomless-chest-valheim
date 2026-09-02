namespace BottomlessChest.Logic
{
    /// <summary>
    /// Decides when the filtered view has fallen behind the chest behind it.
    /// </summary>
    /// <remarks>
    /// The view used to be re-filtered from a Harmony patch on <c>Inventory.Changed</c>.
    /// That patch applies and never runs: <c>Changed</c> is a small private method, and
    /// Mono inlines it into every caller, so the mod saw no mutation at all. Persistence
    /// was unaffected - the inlined body still invokes <c>m_onChanged</c>, so the container
    /// still saves - which is why only the filter appeared broken.
    ///
    /// So the view checks for itself instead, once a frame, off the stack count it last
    /// laid out. That costs an int comparison and cannot be inlined out from under us.
    ///
    /// A new item is always a new stack, so the count moves. Adding to a stack already on
    /// screen does not move it, and needs no re-filter - the stack is drawn either way.
    /// The gap is a removal and an addition landing in the same frame: the count comes back
    /// unchanged and the view stays stale until the next real change.
    /// </remarks>
    public sealed class ReapplyTrigger
    {
        /// <summary>No layout has been built yet, so any count is a change.</summary>
        private const int Unset = -1;

        private int _appliedAt = Unset;

        /// <summary>
        /// Whether the layout needs rebuilding for <paramref name="count"/> stacks.
        /// </summary>
        /// <remarks>
        /// Asking does not clear the trigger. A frame that notices staleness but cannot act
        /// - the chest is mid-load, or the grid is not ours to draw - must not convince the
        /// next frame the work was done.
        /// </remarks>
        public bool NeedsApply(int count) => _appliedAt != count;

        /// <summary>Records that the layout now reflects <paramref name="count"/> stacks.</summary>
        public void NoteApplied(int count) => _appliedAt = count;

        /// <summary>
        /// Forces a rebuild at the next opportunity, whatever the count.
        /// </summary>
        /// <remarks>
        /// For changes the count cannot see: a new search term, or a different chest.
        /// </remarks>
        public void Invalidate() => _appliedAt = Unset;
    }
}
