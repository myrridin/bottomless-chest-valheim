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
        /// <remarks>
        /// An array, not a list. <see cref="Take"/> rewrites entries in place, and a list
        /// invalidates every walk over it when an element is replaced - which is exactly what
        /// the ValheimPlus removal prefix does while it walks the entries to match a name.
        /// </remarks>
        private readonly IndexEntry[] _entries;

        private ChestIndex(long generation, long version, IndexEntry[] entries)
        {
            Generation = generation;
            Version = version;
            _entries = entries;
        }

        /// <summary>
        /// Which server session this summary came from. Newer sessions have higher numbers.
        /// </summary>
        /// <remarks>
        /// A session's <see cref="Version"/> starts again from zero whenever the chest is
        /// reopened, so version alone cannot say which of two summaries is newer.
        /// </remarks>
        public long Generation { get; }

        /// <summary>The chest version this summary was taken at, for staleness checks.</summary>
        public long Version { get; }

        public IReadOnlyList<IndexEntry> Entries => _entries;

        public static ChestIndex From(long generation, long version, IEnumerable<IStorableItem> items)
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

            return new ChestIndex(generation, version, entries.ToArray());
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

        /// <summary>
        /// Deducts up to <paramref name="amount"/> and reports how much was actually taken.
        /// </summary>
        /// <remarks>
        /// Never returns more than the index held. The caller answers another mod with this
        /// number synchronously and only afterwards asks the server to make it true, so a
        /// figure larger than the chest can honour would overdraw it. Erring low costs the
        /// player a craft; erring high costs the chest.
        /// </remarks>
        public int Take(string itemId, int quality, int amount)
        {
            if (itemId == null || amount <= 0)
            {
                return 0;
            }

            var taken = 0;
            for (var i = 0; i < _entries.Length && taken < amount; i++)
            {
                var entry = _entries[i];
                if (entry.ItemId != itemId || (quality >= 0 && entry.Quality != quality))
                {
                    continue;
                }

                var from = entry.Count < amount - taken ? entry.Count : amount - taken;
                _entries[i] = new IndexEntry(entry.ItemId, entry.Quality, entry.Count - from);
                taken += from;
            }

            return taken;
        }
    }
}
