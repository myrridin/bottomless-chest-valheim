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

        // TakeAmount decides how much of a stack leaves the chest when the player asked for
        // part of it. The server is the authority on what is actually there, so "how much
        // is available" is always its own number, never the client's.

        [Fact]
        public void TakeAmountOfZeroMeansTheWholeStack()
        {
            // Zero is the wire's "whole stack" sentinel. Callers that have never cared about
            // partial takes - Take All, a plain click - send it, and must keep taking
            // everything even when the client's idea of the stack is stale.
            Assert.Equal(5000, StackRules.TakeAmount(0, 5000));
        }

        [Fact]
        public void TakeAmountTakesTheAskedForPart()
        {
            Assert.Equal(20, StackRules.TakeAmount(20, 5000));
        }

        [Fact]
        public void TakeAmountLeavesTheRemainder()
        {
            Assert.Equal(4980, 5000 - StackRules.TakeAmount(20, 5000));
        }

        [Theory]
        [InlineData(5000)]
        [InlineData(5001)]
        [InlineData(int.MaxValue)]
        public void TakeAmountNeverExceedsWhatIsThere(int requested)
        {
            // A stale client can ask for more than the stack now holds. Clamping here is
            // what stops the split producing a negative remainder in the chest.
            Assert.Equal(5000, StackRules.TakeAmount(requested, 5000));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void TakeAmountTreatsNegativesAsTheWholeStack(int requested)
        {
            Assert.Equal(50, StackRules.TakeAmount(requested, 50));
        }

        [Fact]
        public void TakeAmountFromAnEmptyStackIsNothing()
        {
            Assert.Equal(0, StackRules.TakeAmount(10, 0));
        }

        // MergeAmount decides how much of an incoming stack collapses into one already in
        // the chest. It is the whole of the consolidation arithmetic; everything else is
        // finding the stack to call it against.

        [Fact]
        public void MergeAmountFitsTheWholeIncomingStack()
        {
            Assert.Equal(10, StackRules.MergeAmount(10, 5, 50));
        }

        [Fact]
        public void MergeAmountIsLimitedByTheRoomLeft()
        {
            Assert.Equal(5, StackRules.MergeAmount(60, 45, 50));
        }

        [Fact]
        public void MergeAmountIntoAFullStackIsNothing()
        {
            Assert.Equal(0, StackRules.MergeAmount(10, 50, 50));
        }

        [Fact]
        public void MergeAmountIntoAnOversizedStackIsNothing()
        {
            // UnlimitedStacks can leave a stack above the vanilla ceiling. Topping it up
            // further would push it further out of spec, and the subtraction would go
            // negative if room were not floored.
            Assert.Equal(0, StackRules.MergeAmount(10, 80, 50));
        }

        [Fact]
        public void MergeAmountNeverStacksTheUnstackable()
        {
            // Max stack size of one is how the game says "these do not combine" - armour,
            // tools, anything carrying its own durability.
            Assert.Equal(0, StackRules.MergeAmount(1, 0, 1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void MergeAmountOfNothingIsNothing(int incoming)
        {
            Assert.Equal(0, StackRules.MergeAmount(incoming, 5, 50));
        }
    }
}
