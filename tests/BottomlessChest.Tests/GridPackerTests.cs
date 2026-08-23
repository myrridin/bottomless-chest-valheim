using System.Collections.Generic;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class GridPackerTests
    {
        [Theory]
        [InlineData(0, 8, 0, 0)]
        [InlineData(7, 8, 7, 0)]
        [InlineData(8, 8, 0, 1)]
        [InlineData(9, 8, 1, 1)]
        [InlineData(23, 8, 7, 2)]
        public void IndexMapsToRowMajorPosition(int index, int width, int x, int y)
        {
            Assert.Equal(new GridPos(x, y), GridPacker.PositionOf(index, width));
        }

        [Theory]
        [InlineData(0, 8, 0)]
        [InlineData(1, 8, 1)]
        [InlineData(7, 8, 1)]
        [InlineData(8, 8, 1)]
        [InlineData(9, 8, 2)]
        [InlineData(10000, 8, 1250)]
        public void RowsNeededCoversEveryItem(int count, int width, int expected)
        {
            Assert.Equal(expected, GridPacker.RowsNeeded(count, width));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void NonPositiveWidthIsRejected(int width)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => GridPacker.PositionOf(0, width));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => GridPacker.RowsNeeded(1, width));
        }

        [Fact]
        public void PackingLeavesNoGapsOrCollisions()
        {
            const int count = 10000;
            const int width = 8;
            var seen = new HashSet<GridPos>();

            for (var i = 0; i < count; i++)
            {
                Assert.True(seen.Add(GridPacker.PositionOf(i, width)), $"duplicate position at index {i}");
            }

            Assert.Equal(count, seen.Count);
            Assert.Equal(1250, GridPacker.RowsNeeded(count, width));
        }
    }
}
