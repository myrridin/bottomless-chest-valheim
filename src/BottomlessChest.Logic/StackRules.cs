using System;
using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>How far stacks are allowed to grow inside the chest.</summary>
    public sealed class StackPolicy
    {
        public StackPolicy(bool unlimited, int multiplier)
        {
            Unlimited = unlimited;
            Multiplier = multiplier;
        }

        public bool Unlimited { get; }

        public int Multiplier { get; }
    }

    public static class StackRules
    {
        /// <summary>
        /// Whether two entries describe the same thing closely enough to become one stack.
        /// </summary>
        /// <remarks>
        /// The <c>MaxStackSize &lt;= 1</c> check is the important one. Weapons, armour and
        /// tools carry per-instance durability, so merging two of them would silently
        /// destroy one item's condition.
        /// </remarks>
        public static bool CanMerge(IStorableItem a, IStorableItem b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.MaxStackSize <= 1 || b.MaxStackSize <= 1)
            {
                return false;
            }

            return string.Equals(a.ItemId, b.ItemId, StringComparison.Ordinal)
                   && a.Quality == b.Quality
                   && a.Variant == b.Variant
                   && string.Equals(a.CustomData ?? string.Empty, b.CustomData ?? string.Empty, StringComparison.Ordinal);
        }

        /// <summary>How large a stack of this item may grow while inside the chest.</summary>
        public static int EffectiveStackLimit(IStorableItem item, StackPolicy policy)
        {
            if (policy.Unlimited)
            {
                return int.MaxValue;
            }

            var scaled = (long)item.MaxStackSize * policy.Multiplier;

            // Never below vanilla: a nonsensical multiplier should not make the chest
            // worse at holding items than an ordinary one.
            if (scaled < item.MaxStackSize)
            {
                return item.MaxStackSize;
            }

            return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }

        /// <summary>
        /// Breaks an oversized stack into vanilla-legal pieces on its way out of the chest.
        /// </summary>
        /// <remarks>
        /// This is a correctness guard, not a convenience. An over-sized stack that escapes
        /// into a player inventory or an ordinary chest gets written to the vanilla save and
        /// outlives the mod.
        /// </remarks>
        public static IReadOnlyList<int> SplitForExit(int stack, int maxStackSize)
        {
            if (maxStackSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxStackSize), maxStackSize, "Stack ceiling must be at least 1.");
            }

            var chunks = new List<int>();
            var remaining = stack;

            while (remaining > 0)
            {
                var take = Math.Min(remaining, maxStackSize);
                chunks.Add(take);
                remaining -= take;
            }

            return chunks;
        }
    }
}
