using System;
using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>
    /// Orders filtered results so like items sit together.
    /// </summary>
    /// <remarks>
    /// Filtering alone only makes the list shorter. Grouping by name is what makes it
    /// scannable, which is the point of searching in the first place. Within a name the
    /// most useful entry comes first: highest quality, then largest stack.
    /// </remarks>
    public sealed class ItemOrdering : IComparer<IStorableItem>
    {
        public static ItemOrdering ByName { get; } = new ItemOrdering();

        public int Compare(IStorableItem a, IStorableItem b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return -1;
            }

            if (b == null)
            {
                return 1;
            }

            var byName = string.CompareOrdinal(a.SearchKey ?? string.Empty, b.SearchKey ?? string.Empty);
            if (byName != 0)
            {
                return byName;
            }

            var byQuality = b.Quality.CompareTo(a.Quality);
            if (byQuality != 0)
            {
                return byQuality;
            }

            return b.Stack.CompareTo(a.Stack);
        }
    }
}
