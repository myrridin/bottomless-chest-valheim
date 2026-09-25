using System;
using System.Collections.Generic;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// <see cref="GridPacker.FirstEmptySlot"/> replaces vanilla <c>Inventory.FindEmptySlot</c>
    /// for bottomless chests, so it has to answer exactly what vanilla would: rows from the top
    /// or from the bottom, and left to right within a row either way.
    /// </summary>
    public class EmptySlotTests
    {
        private static readonly GridPos None = new GridPos(-1, -1);

        [Fact]
        public void TopFirstTakesTheTopLeftOfAnEmptyGrid()
        {
            Assert.Equal(new GridPos(0, 0), GridPacker.FirstEmptySlot(new GridPos[0], 8, 4, topFirst: true));
        }

        [Fact]
        public void BottomFirstTakesTheLeftOfTheBottomRow()
        {
            Assert.Equal(new GridPos(0, 3), GridPacker.FirstEmptySlot(new GridPos[0], 8, 4, topFirst: false));
        }

        [Fact]
        public void TopFirstSkipsOccupiedSlotsAlongTheRow()
        {
            var occupied = new[] { new GridPos(0, 0), new GridPos(1, 0) };

            Assert.Equal(new GridPos(2, 0), GridPacker.FirstEmptySlot(occupied, 8, 4, topFirst: true));
        }

        [Fact]
        public void BottomFirstMovesUpAFullRow()
        {
            var occupied = new List<GridPos>();
            for (var x = 0; x < 8; x++)
            {
                occupied.Add(new GridPos(x, 3));
            }

            Assert.Equal(new GridPos(0, 2), GridPacker.FirstEmptySlot(occupied, 8, 4, topFirst: false));
        }

        [Fact]
        public void AFullGridHasNoSlot()
        {
            var occupied = new List<GridPos>();
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 3; x++)
                {
                    occupied.Add(new GridPos(x, y));
                }
            }

            Assert.Equal(None, GridPacker.FirstEmptySlot(occupied, 3, 2, topFirst: true));
            Assert.Equal(None, GridPacker.FirstEmptySlot(occupied, 3, 2, topFirst: false));
        }

        [Fact]
        public void ItemsOutsideTheGridDoNotFillIt()
        {
            // Another mod can leave an item below the last row; vanilla's per-slot lookup
            // never visits that position, so it must not count against a slot inside.
            var occupied = new[] { new GridPos(0, 9), new GridPos(-1, 0), new GridPos(8, 0) };

            Assert.Equal(new GridPos(0, 0), GridPacker.FirstEmptySlot(occupied, 8, 4, topFirst: true));
        }

        [Fact]
        public void AZeroHeightGridHasNoSlot()
        {
            Assert.Equal(None, GridPacker.FirstEmptySlot(new GridPos[0], 8, 0, topFirst: true));
        }

        [Fact]
        public void MatchesVanillaSlotScanOnRandomLayouts()
        {
            var random = new Random(20260914);

            for (var run = 0; run < 500; run++)
            {
                var width = random.Next(1, 9);
                var height = random.Next(0, 12);
                var occupied = new List<GridPos>();
                var count = random.Next(0, (width * height) + 3);
                for (var i = 0; i < count; i++)
                {
                    occupied.Add(new GridPos(random.Next(-1, width + 1), random.Next(-1, height + 1)));
                }

                foreach (var topFirst in new[] { true, false })
                {
                    Assert.Equal(
                        VanillaScan(occupied, width, height, topFirst),
                        GridPacker.FirstEmptySlot(occupied, width, height, topFirst));
                }
            }
        }

        /// <summary>Vanilla <c>FindEmptySlot</c>, with <c>GetItemAt</c> as a list walk.</summary>
        private static GridPos VanillaScan(List<GridPos> occupied, int width, int height, bool topFirst)
        {
            if (topFirst)
            {
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (!occupied.Contains(new GridPos(x, y)))
                        {
                            return new GridPos(x, y);
                        }
                    }
                }
            }
            else
            {
                for (var y = height - 1; y >= 0; y--)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (!occupied.Contains(new GridPos(x, y)))
                        {
                            return new GridPos(x, y);
                        }
                    }
                }
            }

            return None;
        }
    }
}
