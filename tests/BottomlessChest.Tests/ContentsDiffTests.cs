using System.Collections.Generic;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ContentsDiffTests
    {
        private static TestItem Item(string id, int stack) =>
            new TestItem(id, ItemKind.Material, stack, itemId: id, maxStackSize: 50);

        [Fact]
        public void TallySumsStacksOfTheSameItem()
        {
            var tally = ContentsDiff.Tally(new[] { Item("Wood", 55), Item("Wood", 50), Item("Coal", 5) });

            Assert.Equal(105, tally["Wood"]);
            Assert.Equal(5, tally["Coal"]);
        }

        [Fact]
        public void UnchangedContentsReportNothing()
        {
            var before = ContentsDiff.Tally(new[] { Item("Wood", 253) });
            var after = ContentsDiff.Tally(new[] { Item("Wood", 253) });

            Assert.Null(ContentsDiff.Describe(before, after));
        }

        [Fact]
        public void RegroupingStacksWithoutChangingTotalsReportsNothing()
        {
            // The chest merging five wood stacks into one is not a loss, and the watch
            // must not cry wolf about it - only quantities matter.
            var before = ContentsDiff.Tally(new[] { Item("Wood", 55), Item("Wood", 50), Item("Wood", 48) });
            var after = ContentsDiff.Tally(new[] { Item("Wood", 153) });

            Assert.Null(ContentsDiff.Describe(before, after));
        }

        [Fact]
        public void RemovalIsReportedAsANegativeDelta()
        {
            var before = ContentsDiff.Tally(new[] { Item("Wood", 253) });
            var after = ContentsDiff.Tally(new[] { Item("Wood", 231) });

            Assert.Equal("Wood -22", ContentsDiff.Describe(before, after));
        }

        [Fact]
        public void AnItemVanishingEntirelyIsReported()
        {
            var before = ContentsDiff.Tally(new[] { Item("Wood", 253), Item("Coal", 5) });
            var after = ContentsDiff.Tally(new[] { Item("Coal", 5) });

            Assert.Equal("Wood -253", ContentsDiff.Describe(before, after));
        }

        [Fact]
        public void AnItemAppearingIsReported()
        {
            var before = ContentsDiff.Tally(new[] { Item("Coal", 5) });
            var after = ContentsDiff.Tally(new[] { Item("Coal", 5), Item("Wood", 20) });

            Assert.Equal("Wood +20", ContentsDiff.Describe(before, after));
        }

        [Fact]
        public void SeveralChangesAreListedAlphabeticallySoTheLogIsStable()
        {
            var before = ContentsDiff.Tally(new[] { Item("Wood", 100), Item("Stone", 10) });
            var after = ContentsDiff.Tally(new[] { Item("Wood", 90), Item("Stone", 12), Item("Coal", 3) });

            Assert.Equal("Coal +3, Stone +2, Wood -10", ContentsDiff.Describe(before, after));
        }

        [Fact]
        public void EmptyBeforeAndAfterReportsNothing()
        {
            var empty = new Dictionary<string, int>();

            Assert.Null(ContentsDiff.Describe(empty, empty));
        }

        [Fact]
        public void ATallyOfNothingIsEmptyRatherThanNull()
        {
            Assert.Empty(ContentsDiff.Tally(new IStorableItem[0]));
        }
    }
}
