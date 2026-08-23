using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ChestRowsTests
    {
        [Theory]
        [InlineData(0, 4)]   // empty chest still looks like a chest
        [InlineData(1, 4)]
        [InlineData(24, 4)]  // 3 rows of content + 1 spare still fits under the floor
        [InlineData(25, 5)]  // 4 rows of content + 1 spare
        [InlineData(32, 5)]
        [InlineData(33, 6)]
        public void RowsGrowOnceTheFloorIsExceeded(int count, int expected)
        {
            Assert.Equal(expected, GridPacker.RowsForChest(count, width: 8, minRows: 4));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(1000)]
        [InlineData(9999)]
        public void ThereIsAlwaysRoomForOneMoreItem(int count)
        {
            // The whole unbounded-slots trick rests on this: capacity is checked before an
            // insert, so the grid must already be bigger than its contents.
            var rows = GridPacker.RowsForChest(count, width: 8, minRows: 4);

            Assert.True(rows * 8 > count, $"{count} items in {rows}x8 leaves no free slot");
        }

        [Fact]
        public void FloorIsIgnoredWhenSmallerThanOne()
        {
            Assert.Equal(1, GridPacker.RowsForChest(0, width: 8, minRows: 0));
            Assert.Equal(1, GridPacker.RowsForChest(0, width: 8, minRows: -3));
        }

        [Fact]
        public void NonPositiveWidthIsRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => GridPacker.RowsForChest(10, width: 0, minRows: 4));
        }
    }
}
