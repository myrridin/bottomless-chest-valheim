using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>One item type and quality, and how much of it a chest holds.</summary>
    public readonly struct IndexEntry
    {
        public IndexEntry(string itemId, int quality, int count)
        {
            ItemId = itemId;
            Quality = quality;
            Count = count;
        }

        /// <summary>Stable prefab name, so the server and client agree on what was asked for.</summary>
        public string ItemId { get; }

        public int Quality { get; }

        public int Count { get; }
    }

    /// <summary>
    /// What a chest holds, summarised so a client can answer questions about it without
    /// holding it.
    /// </summary>
    /// <remarks>
    /// One entry per item type and quality, not per stack. A chest of a million stacks
    /// spanning forty item types indexes to forty entries, which is what lets another mod
    /// query a bottomless chest on a dedicated server without breaking paging.
    ///
    /// Quality is kept distinct because it is not interchangeable: a repair cannot use
    /// materials of the wrong quality, and merging the two would let it claim it can.
    /// </remarks>
    public sealed class ChestIndex
    {
        private readonly List<IndexEntry> _entries;

        private ChestIndex(long version, List<IndexEntry> entries)
        {
            Version = version;
            _entries = entries;
        }

        /// <summary>The chest version this summary was taken at, for staleness checks.</summary>
        public long Version { get; }

        public IReadOnlyList<IndexEntry> Entries => _entries;

        public static ChestIndex From(long version, IEnumerable<IStorableItem> items)
        {
            var entries = new List<IndexEntry>();
            var seen = new Dictionary<string, int>(System.StringComparer.Ordinal);

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item?.ItemId == null)
                    {
                        continue;
                    }

                    var key = item.ItemId + "/" + item.Quality;

                    if (seen.TryGetValue(key, out var at))
                    {
                        entries[at] = new IndexEntry(
                            entries[at].ItemId, entries[at].Quality, entries[at].Count + item.Stack);
                    }
                    else
                    {
                        seen[key] = entries.Count;
                        entries.Add(new IndexEntry(item.ItemId, item.Quality, item.Stack));
                    }
                }
            }

            return new ChestIndex(version, entries);
        }

        /// <summary>How much of an item is held. A negative quality means any quality.</summary>
        public int CountOf(string itemId, int quality)
        {
            if (itemId == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var entry in _entries)
            {
                if (entry.ItemId == itemId && (quality < 0 || entry.Quality == quality))
                {
                    total += entry.Count;
                }
            }

            return total;
        }

    }
}
