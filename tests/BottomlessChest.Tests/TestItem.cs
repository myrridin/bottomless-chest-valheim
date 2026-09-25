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
            string customData = null,
            int worldLevel = 0,
            bool cheated = false)
        {
            DisplayName = displayName;
            Kind = kind;
            Stack = stack;
            Quality = quality;
            ItemId = itemId ?? displayName;
            MaxStackSize = maxStackSize;
            Variant = variant;
            CustomData = customData;
            WorldLevel = worldLevel;
            Cheated = cheated;
        }

        public string ItemId { get; }

        public string DisplayName { get; }

        public string SearchKey => TextKey.Of(DisplayName);

        public ItemKind Kind { get; }

        public int Stack { get; }

        public int MaxStackSize { get; }

        public int Quality { get; }

        public int Variant { get; }

        public string CustomData { get; }

        public int WorldLevel { get; }

        public bool Cheated { get; }
    }
}
