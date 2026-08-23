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

        private static void RequirePositiveWidth(int width)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Grid width must be at least 1.");
            }
        }
    }
}
