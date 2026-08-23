using System.Collections.Generic;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class LayoutFitsWindowTests
    {
        private static List<GridPos> Packed(int count, int width)
        {
            var list = new List<GridPos>();
            for (var i = 0; i < count; i++)
            {
                list.Add(GridPacker.PositionOf(i, width));
            }

            return list;
        }

        [Fact]
        public void EmptyLayoutFits()
        {
            Assert.True(GridPacker.LayoutFitsWindow(new List<GridPos>(), width: 8, rows: 6));
        }

        [Fact]
        public void PackedLayoutInsideTheWindowFits()
        {
            Assert.True(GridPacker.LayoutFitsWindow(Packed(20, 8), width: 8, rows: 6));
        }

        [Fact]
        public void ArbitraryButValidLayoutFits()
        {
            // The point of this check: another mod's sort order is fine as long as every
            // item is somewhere visible and no two share a slot.
            var scattered = new List<GridPos> { new GridPos(7, 5), new GridPos(0, 0), new GridPos(3, 2) };

            Assert.True(GridPacker.LayoutFitsWindow(scattered, width: 8, rows: 6));
        }

        [Fact]
        public void LayoutOverflowingTheWindowDoesNotFit()
        {
            Assert.False(GridPacker.LayoutFitsWindow(Packed(49, 8), width: 8, rows: 6));
        }

        [Theory]
        [InlineData(8, 0)]
        [InlineData(-1, 0)]
        [InlineData(0, 6)]
        [InlineData(0, -2)]
        public void PositionOutsideTheWindowDoesNotFit(int x, int y)
        {
            var layout = new List<GridPos> { new GridPos(0, 0), new GridPos(x, y) };

            Assert.False(GridPacker.LayoutFitsWindow(layout, width: 8, rows: 6));
        }

        [Fact]
        public void OverlappingItemsDoNotFit()
        {
            // Two items in one slot means one of them is invisible.
            var layout = new List<GridPos> { new GridPos(2, 1), new GridPos(2, 1) };

            Assert.False(GridPacker.LayoutFitsWindow(layout, width: 8, rows: 6));
        }
    }
}
