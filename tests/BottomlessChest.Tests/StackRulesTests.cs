using System.Linq;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class StackRulesTests
    {
        private static TestItem Wood(int stack = 1) =>
            new TestItem("Wood", ItemKind.Material, stack, itemId: "Wood", maxStackSize: 50);

        [Fact]
        public void IdenticalStackablesMerge()
        {
            Assert.True(StackRules.CanMerge(Wood(10), Wood(20)));
        }

        [Fact]
        public void UnstackableItemsNeverMerge()
        {
            // Two iron swords are not interchangeable; each carries its own durability.
            var a = new TestItem("Iron sword", ItemKind.Weapon, 1, itemId: "SwordIron", maxStackSize: 1);
            var b = new TestItem("Iron sword", ItemKind.Weapon, 1, itemId: "SwordIron", maxStackSize: 1);

            Assert.False(StackRules.CanMerge(a, b));
        }

        [Fact]
        public void DifferentItemsNeverMerge()
        {
            var stone = new TestItem("Stone", ItemKind.Material, 1, itemId: "Stone", maxStackSize: 50);

            Assert.False(StackRules.CanMerge(Wood(), stone));
        }

        [Fact]
        public void DifferentQualityNeverMerges()
        {
            var a = new TestItem("Wood", ItemKind.Material, 1, quality: 1, itemId: "Wood", maxStackSize: 50);
            var b = new TestItem("Wood", ItemKind.Material, 1, quality: 2, itemId: "Wood", maxStackSize: 50);

            Assert.False(StackRules.CanMerge(a, b));
        }

        [Fact]
        public void DifferentVariantNeverMerges()
        {
            var a = new TestItem("Cape", ItemKind.Armor, 1, itemId: "Cape", maxStackSize: 50, variant: 0);
            var b = new TestItem("Cape", ItemKind.Armor, 1, itemId: "Cape", maxStackSize: 50, variant: 3);

            Assert.False(StackRules.CanMerge(a, b));
        }

        [Fact]
        public void DifferentCustomDataNeverMerges()
        {
            var a = new TestItem("Fish", ItemKind.Food, 1, itemId: "Fish", maxStackSize: 20, customData: "{\"a\":1}");
            var b = new TestItem("Fish", ItemKind.Food, 1, itemId: "Fish", maxStackSize: 20, customData: "{\"a\":2}");

            Assert.False(StackRules.CanMerge(a, b));
        }

        [Fact]
        public void AbsentCustomDataMatchesAbsentCustomData()
        {
            var a = new TestItem("Fish", ItemKind.Food, 1, itemId: "Fish", maxStackSize: 20, customData: null);
            var b = new TestItem("Fish", ItemKind.Food, 1, itemId: "Fish", maxStackSize: 20, customData: "");

            Assert.True(StackRules.CanMerge(a, b));
        }

        [Fact]
        public void UnlimitedPolicyLiftsTheCeiling()
        {
            Assert.Equal(int.MaxValue, StackRules.EffectiveStackLimit(Wood(), new StackPolicy(true, 100)));
        }

        [Fact]
        public void MultiplierPolicyScalesTheVanillaCeiling()
        {
            Assert.Equal(5000, StackRules.EffectiveStackLimit(Wood(), new StackPolicy(false, 100)));
        }

        [Fact]
        public void MultiplierNeverShrinksBelowVanilla()
        {
            Assert.Equal(50, StackRules.EffectiveStackLimit(Wood(), new StackPolicy(false, 0)));
        }

        [Fact]
        public void MultiplierDoesNotOverflowOnHugeValues()
        {
            var item = new TestItem("Wood", ItemKind.Material, 1, itemId: "Wood", maxStackSize: 1000000);

            Assert.Equal(int.MaxValue, StackRules.EffectiveStackLimit(item, new StackPolicy(false, 100000)));
        }

        [Fact]
        public void ExitSplitProducesVanillaLegalStacks()
        {
            var chunks = StackRules.SplitForExit(250, 100);

            Assert.Equal(new[] { 100, 100, 50 }, chunks);
        }

        [Fact]
        public void ExitSplitLeavesSmallStacksAlone()
        {
            Assert.Equal(new[] { 50 }, StackRules.SplitForExit(50, 100));
        }

        [Fact]
        public void ExitSplitOfHugeStackNeverExceedsTheCeiling()
        {
            var chunks = StackRules.SplitForExit(5000, 100);

            Assert.Equal(50, chunks.Count);
            Assert.All(chunks, c => Assert.InRange(c, 1, 100));
            Assert.Equal(5000, chunks.Sum());
        }

        [Fact]
        public void ExitSplitOfNothingIsEmpty()
        {
            Assert.Empty(StackRules.SplitForExit(0, 100));
        }
        [Theory(Timeout = 3000)]
        [InlineData(0)]
        [InlineData(-5)]
        public void ExitSplitRejectsNonPositiveCeiling(int maxStackSize)
        {
            // A zero ceiling would otherwise spin forever appending empty chunks, hanging
            // the game on the item-exit path.
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => StackRules.SplitForExit(100, maxStackSize));
        }
    }
}
