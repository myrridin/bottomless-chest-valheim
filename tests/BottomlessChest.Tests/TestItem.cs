using BottomlessChest.Logic;

namespace BottomlessChest.Tests
{
    internal sealed class TestItem : IStorableItem
    {
        public TestItem(
            string displayName,
            ItemKind kind = ItemKind.Material,
            int stack = 1,
            int quality = 1,
            string itemId = null,
            int maxStackSize = 100,
            int variant = 0,
            string customData = null)
        {
            DisplayName = displayName;
            Kind = kind;
            Stack = stack;
            Quality = quality;
            ItemId = itemId ?? displayName;
            MaxStackSize = maxStackSize;
            Variant = variant;
            CustomData = customData;
        }

        public string ItemId { get; }

        public string DisplayName { get; }

        public ItemKind Kind { get; }

        public int Stack { get; }

        public int MaxStackSize { get; }

        public int Quality { get; }

        public int Variant { get; }

        public string CustomData { get; }
    }
}
