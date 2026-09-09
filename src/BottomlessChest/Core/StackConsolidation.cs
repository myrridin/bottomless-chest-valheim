using System.Collections.Generic;
using BottomlessChest.Logic;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Collapses part-filled stacks in a chest into as few stacks as the game allows.
    /// </summary>
    /// <remarks>
    /// A bottomless chest has no slot pressure, so nothing ever forced its contents to be
    /// tidy: every deposit used to land as its own stack, and a chest filled a handful at a
    /// time became thousands of part-stacks of the same thing. That is not a storage problem
    /// - the chest is unbounded - but it is a *finding* problem, because the window shows
    /// stacks and a screen of eight-item piles is a screen that tells you nothing.
    ///
    /// Shared between the two ways a chest's contents arrive: a dedicated server's session,
    /// which consolidates when the chest is opened and stays consolidated as deposits land,
    /// and a single-player load, which does it once on the way in.
    /// </remarks>
    internal static class StackConsolidation
    {
        /// <summary>
        /// What has to match for two stacks to be one stack.
        /// </summary>
        /// <remarks>
        /// Vanilla's own rule is name, quality and world level. Variant is added to it: two
        /// items that differ only by variant look different, and silently merging them would
        /// change what the player is holding.
        /// </remarks>
        internal static string StackKey(ItemDrop.ItemData item) =>
            $"{item.m_shared.m_name}|{item.m_quality}|{item.m_variant}|{item.m_worldLevel}";

        /// <summary>
        /// Merges what can be merged, and reports how many stacks disappeared.
        /// </summary>
        /// <param name="inventory">Contents to collapse, rewritten in place.</param>
        /// <param name="openStacks">
        /// Filled, if given, with the one stack of each kind that still has room. That is the
        /// whole point of doing this: afterwards every other stack is full by definition, so
        /// this index is the size of the item catalogue rather than the size of the chest,
        /// and a later deposit is a lookup instead of a walk.
        /// </param>
        /// <remarks>
        /// One pass, compacting in place rather than building a second list - the caller may
        /// be holding ten million stacks, and a copy of that is real memory.
        ///
        /// Stacks already above the vanilla ceiling are left exactly as they are. Only
        /// UnlimitedStacks makes them, and topping one up would push it further out of spec.
        /// </remarks>
        internal static bool Collapse(
            Inventory inventory,
            out int collapsed,
            IDictionary<string, ItemDrop.ItemData> openStacks = null)
        {
            collapsed = 0;
            if (inventory == null)
            {
                return true;
            }

            // Consolidation moves item counts between stacks, so the one thing it must never
            // do is change how many items there are. Counted rather than trusted: this
            // rewrites contents, and the caller's response to a mismatch is to refuse to
            // save, which keeps the copy on disk - the one that matters - intact.
            //
            // Counted lazily, at the moment the first merge is about to happen and nothing
            // has been touched yet. An already-tidy chest never merges anything, so it never
            // pays for the count at all - which is every open after the first.
            long expected = -1;

            var index = openStacks ?? new Dictionary<string, ItemDrop.ItemData>(System.StringComparer.Ordinal);
            index.Clear();

            var items = inventory.m_inventory;
            var write = 0;

            for (var read = 0; read < items.Count; read++)
            {
                var item = items[read];
                var max = item.m_shared.m_maxStackSize;

                if (max > 1 && item.m_stack < max)
                {
                    var key = StackKey(item);
                    if (index.TryGetValue(key, out var open))
                    {
                        if (expected < 0)
                        {
                            expected = TotalItems(inventory);
                        }

                        var moved = StackRules.MergeAmount(item.m_stack, open.m_stack, max);
                        open.m_stack += moved;
                        item.m_stack -= moved;

                        if (item.m_stack <= 0)
                        {
                            // Wholly absorbed. Not writing it back is what removes it.
                            collapsed++;
                            continue;
                        }

                        // The stack it was merging into filled up, so this remainder is now
                        // the open one for its kind.
                        index[key] = item;
                    }
                    else
                    {
                        index[key] = item;
                    }
                }

                items[write++] = item;
            }

            if (collapsed > 0)
            {
                items.RemoveRange(write, items.Count - write);
            }

            if (expected < 0)
            {
                // Nothing was ever merged, so nothing can have been lost.
                return true;
            }

            var actual = TotalItems(inventory);
            if (actual == expected)
            {
                return true;
            }

            Plugin.Log.LogError(
                $"Consolidation changed the item count from {expected} to {actual}. This is a " +
                "bug in the mod. The chest will not be saved, so the stored contents are " +
                "untouched; reopen the world to get them back.");

            return false;
        }

        /// <summary>Every item in every stack. Long, because a big chest overflows an int.</summary>
        private static long TotalItems(Inventory inventory)
        {
            long total = 0;
            foreach (var item in inventory.m_inventory)
            {
                total += item.m_stack;
            }

            return total;
        }
    }
}
