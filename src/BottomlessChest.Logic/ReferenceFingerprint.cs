using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BottomlessChest.Logic
{
    public static class ReferenceFingerprint
    {
        /// <summary>
        /// A hash of which objects a list holds and in what order, ignoring their contents.
        /// </summary>
        /// <remarks>
        /// Two lists with the same fingerprint almost certainly hold the same objects in the
        /// same places, so anything that names an entry by its position still names the same
        /// one - while each object's fields, a stack's count included, may have changed freely.
        /// Identity hashes, not <c>GetHashCode</c>: an item's own hash may follow its contents.
        /// Order-sensitive FNV-1a over them, seeded with the count.
        /// </remarks>
        public static ulong Of<T>(List<T> items) where T : class
        {
            var hash = 14695981039346656037UL ^ (ulong)(items?.Count ?? 0);
            if (items == null)
            {
                return hash;
            }

            for (var i = 0; i < items.Count; i++)
            {
                hash ^= (uint)RuntimeHelpers.GetHashCode(items[i]);
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }
}
