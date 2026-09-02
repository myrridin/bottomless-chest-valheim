using System.Collections.Generic;
using System.Text;

namespace BottomlessChest.Logic
{
    /// <summary>
    /// Compares two snapshots of a chest's contents and describes what moved.
    /// </summary>
    /// <remarks>
    /// Exists because a chest can be emptied without any code of ours running. ValheimPlus
    /// removes items straight off <c>Container.m_inventory</c> - CraftFromChest does it on
    /// every build placement - mutating stacks in place and never calling
    /// <c>Inventory.Changed()</c>. Nothing in that path logs, so a chest can drain with no
    /// trace in either our log or its own.
    ///
    /// Quantities are the unit of comparison, not stacks. The chest repacks and merges
    /// stacks constantly during normal use, and reporting that as a change would bury the
    /// one event worth seeing.
    /// </remarks>
    public static class ContentsDiff
    {
        /// <summary>Total quantity held per item id.</summary>
        public static Dictionary<string, int> Tally(IEnumerable<IStorableItem> items)
        {
            var tally = new Dictionary<string, int>();
            if (items == null)
            {
                return tally;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                Add(tally, item.ItemId, item.Stack);
            }

            return tally;
        }

        /// <summary>
        /// Accumulates one stack into a tally.
        /// </summary>
        /// <remarks>
        /// Exposed so the mod can tally straight off <c>ItemDrop.ItemData</c>. The watch
        /// runs once a second over the whole chest, and wrapping every item in an adapter
        /// just to add up two fields would allocate more than the diff is worth.
        /// </remarks>
        public static void Add(Dictionary<string, int> tally, string itemId, int stack)
        {
            if (tally == null || itemId == null)
            {
                return;
            }

            tally.TryGetValue(itemId, out var running);
            tally[itemId] = running + stack;
        }

        /// <summary>
        /// A compact "Coal +3, Wood -10" summary, or null when the totals are unchanged.
        /// </summary>
        public static string Describe(
            IReadOnlyDictionary<string, int> before,
            IReadOnlyDictionary<string, int> after)
        {
            if (before == null || after == null)
            {
                return null;
            }

            var names = new SortedSet<string>();
            foreach (var pair in before)
            {
                names.Add(pair.Key);
            }

            foreach (var pair in after)
            {
                names.Add(pair.Key);
            }

            var summary = new StringBuilder();
            foreach (var name in names)
            {
                before.TryGetValue(name, out var was);
                after.TryGetValue(name, out var now);

                var delta = now - was;
                if (delta == 0)
                {
                    continue;
                }

                if (summary.Length > 0)
                {
                    summary.Append(", ");
                }

                summary.Append(name).Append(' ').Append(delta > 0 ? "+" : "-").Append(delta > 0 ? delta : -delta);
            }

            return summary.Length > 0 ? summary.ToString() : null;
        }
    }
}
