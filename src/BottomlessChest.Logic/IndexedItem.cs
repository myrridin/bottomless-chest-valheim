namespace BottomlessChest.Logic
{
    /// <summary>
    /// An index entry wearing the item interface, so a summary can be built and rebuilt
    /// through the same <see cref="ChestIndex.From"/> path on both ends.
    /// </summary>
    /// <remarks>
    /// Only the three fields an index needs are real. The rest satisfy the interface and
    /// are never read: an index answers "how much", never "which one".
    /// </remarks>
    public sealed class IndexedItem : IStorableItem
    {
        public IndexedItem(string itemId, int quality, int stack)
        {
            ItemId = itemId;
            Quality = quality;
            Stack = stack;
        }

        public string ItemId { get; }

        public int Quality { get; }

        public int Stack { get; }

        public string DisplayName => ItemId;

        public string SearchKey => TextKey.Of(ItemId);

        public ItemKind Kind => ItemKind.Unknown;

        public int MaxStackSize => int.MaxValue;

        public int Variant => 0;

        public string CustomData => null;
    }
}
