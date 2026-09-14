using System.Linq;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ChestIndexTests
    {
        private static TestItem Item(string id, int stack, int quality = 1) =>
            new TestItem(id, ItemKind.Material, stack, quality: quality, itemId: id, maxStackSize: 50);

        [Fact]
        public void StacksOfOneItemBecomeOneEntryCarryingTheTotal()
        {
            var index = ChestIndex.From(7, new[] { Item("Wood", 55), Item("Wood", 50), Item("Wood", 48) });

            var entry = Assert.Single(index.Entries);
            Assert.Equal("Wood", entry.ItemId);
            Assert.Equal(153, entry.Count);
        }

        [Fact]
        public void TheIndexIsBoundedByVarietyNotByStackCount()
        {
            // The whole reason this can exist without breaking paging.
            var manyStacks = Enumerable.Range(0, 5000).Select(_ => Item("Wood", 50));

            Assert.Single(ChestIndex.From(1, manyStacks).Entries);
        }

        [Fact]
        public void DifferentQualitiesStaySeparate()
        {
            // Merging them would let a repair claim materials it cannot use.
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 1, quality: 1), Item("SwordIron", 1, quality: 3) });

            Assert.Equal(2, index.Entries.Count);
            Assert.Equal(1, index.CountOf("SwordIron", 1));
            Assert.Equal(1, index.CountOf("SwordIron", 3));
        }

        [Fact]
        public void NegativeQualityMeansAnyQuality()
        {
            // ValheimPlus passes quality -1 when it does not care, and expects the sum.
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 1, quality: 1), Item("SwordIron", 1, quality: 3) });

            Assert.Equal(2, index.CountOf("SwordIron", -1));
        }

        [Fact]
        public void AnItemNotHeldCountsZero()
        {
            Assert.Equal(0, ChestIndex.From(1, new[] { Item("Wood", 10) }).CountOf("Stone", -1));
        }

        [Fact]
        public void TheVersionIsCarried()
        {
            Assert.Equal(42, ChestIndex.From(42, new[] { Item("Wood", 1) }).Version);
        }

        [Fact]
        public void AnEmptyChestIndexesToNothing()
        {
            Assert.Empty(ChestIndex.From(1, new IStorableItem[0]).Entries);
        }

        [Fact]
        public void TakingLessThanHeldReturnsWhatWasAsked()
        {
            var index = ChestIndex.From(1, new[] { Item("Wood", 100) });

            Assert.Equal(30, index.Take("Wood", -1, 30));
            Assert.Equal(70, index.CountOf("Wood", -1));
        }

        [Fact]
        public void TakingMoreThanHeldReturnsOnlyWhatWasThere()
        {
            // The invariant the optimistic write rests on: never claim more than exists.
            var index = ChestIndex.From(1, new[] { Item("Wood", 10) });

            Assert.Equal(10, index.Take("Wood", -1, 999));
            Assert.Equal(0, index.CountOf("Wood", -1));
        }

        [Fact]
        public void TakingWhatIsNotHeldTakesNothing()
        {
            var index = ChestIndex.From(1, new[] { Item("Wood", 10) });

            Assert.Equal(0, index.Take("Stone", -1, 5));
            Assert.Equal(10, index.CountOf("Wood", -1));
        }

        [Fact]
        public void TakingSpreadsAcrossQualitiesWhenQualityIsNotSpecified()
        {
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 2, quality: 1), Item("SwordIron", 2, quality: 3) });

            Assert.Equal(3, index.Take("SwordIron", -1, 3));
            Assert.Equal(1, index.CountOf("SwordIron", -1));
        }

        [Fact]
        public void TakingARequestedQualityLeavesTheOthersAlone()
        {
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 2, quality: 1), Item("SwordIron", 2, quality: 3) });

            Assert.Equal(2, index.Take("SwordIron", 3, 5));
            Assert.Equal(2, index.CountOf("SwordIron", 1));
            Assert.Equal(0, index.CountOf("SwordIron", 3));
        }

        [Fact]
        public void TakingZeroOrLessTakesNothing()
        {
            var index = ChestIndex.From(1, new[] { Item("Wood", 10) });

            Assert.Equal(0, index.Take("Wood", -1, 0));
            Assert.Equal(0, index.Take("Wood", -1, -5));
            Assert.Equal(10, index.CountOf("Wood", -1));
        }
    }
}
