using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// Pins the free space a sized chest grid leaves for writes we never see coming.
    /// </summary>
    /// <remarks>
    /// ValheimPlus deposits into chests with <c>Inventory.AddItem</c> - beehives, sap
    /// collectors, fermenters and every smelter do it - and AddItem needs a free grid slot,
    /// silently dropping what will not fit. Our grid is sized by InventoryCapacity.ApplyFor
    /// as RowsForChest(count + reserved), so the reserve is what stands between an
    /// unannounced deposit and a lost item.
    /// </remarks>
    public class CapacityHeadroomTests
    {
        private const int Width = 8;
        private const int VisibleRows = 6;
        private const int Reserved = Width * VisibleRows;

        private static int FreeSlotsFor(int count)
        {
            var rows = GridPacker.RowsForChest(count + Reserved, Width, VisibleRows);
            return (rows * Width) - count;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(47)]
        [InlineData(48)]
        [InlineData(49)]
        [InlineData(1000)]
        [InlineData(100000)]
        public void ASizedGridAlwaysLeavesAWindowOfFreeSlots(int count)
        {
            Assert.True(
                FreeSlotsFor(count) >= Reserved,
                $"{count} stacks left {FreeSlotsFor(count)} free slots, fewer than the {Reserved} reserved.");
        }

        [Fact]
        public void TheReserveDoesNotShrinkAsTheChestGrows()
        {
            // A chest with a hundred thousand stacks must be no worse defended against an
            // unannounced deposit than an empty one.
            Assert.True(FreeSlotsFor(100000) >= FreeSlotsFor(0) - Width);
        }
    }
}
