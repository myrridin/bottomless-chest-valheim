using System;
using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>A slot coordinate in the chest window.</summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is GridPos other && Equals(other);

        public override int GetHashCode() => (X * 397) ^ Y;

        public override string ToString() => $"({X}, {Y})";
    }

    public static class GridPacker
    {
        /// <summary>Row-major slot for the n-th item in the filtered view.</summary>
        public static GridPos PositionOf(int index, int width)
        {
            RequirePositiveWidth(width);

            return new GridPos(index % width, index / width);
        }

        /// <summary>How many rows it takes to show <paramref name="count"/> items.</summary>
        public static int RowsNeeded(int count, int width)
        {
            RequirePositiveWidth(width);

            return count <= 0 ? 0 : ((count - 1) / width) + 1;
        }

        /// <summary>
        /// The furthest a paged window can scroll: the first row from which every remaining item
        /// fits on one page.
        /// </summary>
        /// <remarks>
        /// A page holds <paramref name="pageSlots"/> items, fewer than a full window, because its
        /// last slot is kept free for dropping. Counting whole rows of items instead let the
        /// window scroll one row past the end and show a completely empty row under the last
        /// items.
        /// </remarks>
        public static int LastPageRow(int count, int width, int pageSlots)
        {
            RequirePositiveWidth(width);

            return count <= pageSlots ? 0 : RowsNeeded(count - pageSlots, width);
        }

        /// <summary>How many rows the chest window should offer for a given item count.</summary>
        /// <remarks>
        /// Always leaves one empty row beyond the contents. Valheim decides an inventory is
        /// full by comparing item count against width*height and checks that before
        /// inserting, so the spare row is precisely what makes slots unbounded.
        /// </remarks>
        public static int RowsForChest(int count, int width, int minRows)
        {
            RequirePositiveWidth(width);

            var needed = RowsNeeded(count, width) + 1;
            var floor = minRows < 1 ? 1 : minRows;

            return needed > floor ? needed : floor;
        }

        /// <summary>
        /// Whether an existing layout can be shown as-is in a width x rows window.
        /// </summary>
        /// <remarks>
        /// Used to leave an existing layout alone. Any arrangement counts as usable so long
        /// as every item is visible and no two share a slot - so a sort applied by another
        /// mod survives instead of being overwritten on the next redraw.
        /// </remarks>
        public static bool LayoutFitsWindow(IReadOnlyList<GridPos> positions, int width, int rows)
        {
            RequirePositiveWidth(width);

            if (rows < 1 || positions == null)
            {
                return false;
            }

            if (positions.Count > width * rows)
            {
                return false;
            }

            var occupied = new HashSet<int>();

            foreach (var pos in positions)
            {
                if (pos.X < 0 || pos.X >= width || pos.Y < 0 || pos.Y >= rows)
                {
                    return false;
                }

                if (!occupied.Add((pos.Y * width) + pos.X))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The first free slot in a width x height grid, or (-1, -1) when it is full.
        /// </summary>
        /// <remarks>
        /// The same answer as vanilla <c>Inventory.FindEmptySlot</c>: rows from the top, or from
        /// the bottom when <paramref name="topFirst"/> is false, and left to right within a row.
        /// Vanilla asks <c>GetItemAt</c> - a walk of every item - for each slot in turn, which is
        /// quadratic in a chest with ten thousand stacks and ran for every stack Place Stacks
        /// added. This marks the occupied slots once. Positions outside the grid are ignored,
        /// as vanilla's scan never visits them.
        /// </remarks>
        public static GridPos FirstEmptySlot(IEnumerable<GridPos> occupied, int width, int height, bool topFirst)
        {
            RequirePositiveWidth(width);

            if (height <= 0)
            {
                return new GridPos(-1, -1);
            }

            var taken = new bool[(long)width * height];
            if (occupied != null)
            {
                foreach (var pos in occupied)
                {
                    if (pos.X >= 0 && pos.X < width && pos.Y >= 0 && pos.Y < height)
                    {
                        taken[((long)pos.Y * width) + pos.X] = true;
                    }
                }
            }

            for (var row = 0; row < height; row++)
            {
                var y = topFirst ? row : height - 1 - row;
                for (var x = 0; x < width; x++)
                {
                    if (!taken[((long)y * width) + x])
                    {
                        return new GridPos(x, y);
                    }
                }
            }

            return new GridPos(-1, -1);
        }

        private static void RequirePositiveWidth(int width)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Grid width must be at least 1.");
            }
        }
    }
}
