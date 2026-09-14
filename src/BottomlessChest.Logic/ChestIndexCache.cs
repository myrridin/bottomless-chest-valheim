using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>
    /// The most recent summary this client has of each chest it can see.
    /// </summary>
    /// <remarks>
    /// Transient by design. Nothing here is persisted, written to a ZDO, or drawn - it
    /// exists only so another mod's questions about a server-held chest can be answered
    /// without shipping the chest.
    ///
    /// Lives in the Logic assembly so it can be tested without the game.
    /// </remarks>
    public sealed class ChestIndexCache
    {
        private readonly Dictionary<string, ChestIndex> _byStore =
            new Dictionary<string, ChestIndex>(System.StringComparer.Ordinal);

        /// <summary>
        /// Records a summary, unless the one already held is strictly newer.
        /// </summary>
        /// <remarks>
        /// Replies can arrive out of order. A late snapshot would otherwise resurrect
        /// quantities the chest no longer has, which is the direction that overdraws it.
        ///
        /// Newer means a later session first, then a later version within it. A reopened
        /// chest counts versions from zero again, so comparing versions alone would keep the
        /// old session's summary until the new one happened to catch up.
        /// </remarks>
        public void Put(string storeId, ChestIndex index)
        {
            if (string.IsNullOrEmpty(storeId) || index == null)
            {
                return;
            }

            if (_byStore.TryGetValue(storeId, out var held) && IsNewer(held, index))
            {
                return;
            }

            _byStore[storeId] = index;
        }

        public bool TryGet(string storeId, out ChestIndex index)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                index = null;
                return false;
            }

            return _byStore.TryGetValue(storeId, out index);
        }

        public void Forget(string storeId)
        {
            if (!string.IsNullOrEmpty(storeId))
            {
                _byStore.Remove(storeId);
            }
        }

        public void Clear() => _byStore.Clear();

        private static bool IsNewer(ChestIndex held, ChestIndex incoming) =>
            held.Generation > incoming.Generation
            || (held.Generation == incoming.Generation && held.Version > incoming.Version);
    }
}
