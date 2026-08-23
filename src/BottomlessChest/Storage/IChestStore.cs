namespace BottomlessChest.Storage
{
    /// <summary>
    /// Where a bottomless chest's contents actually live.
    /// </summary>
    /// <remarks>
    /// Abstracted so the backend can change without touching the container or the GUI.
    /// Contents are opaque bytes here - a serialized <c>Inventory</c> - because nothing
    /// at this layer needs to understand items.
    /// </remarks>
    internal interface IChestStore
    {
        bool TryGet(string storeId, out byte[] contents);

        void Put(string storeId, byte[] contents);

        int Count { get; }

        /// <summary>
        /// True once the backing file has been read for the current world. Until then an
        /// absent entry means "unknown", not "empty" - and must never be treated as empty.
        /// </summary>
        bool IsReady { get; }

        /// <summary>Writes pending changes to disk. Safe to call when nothing is dirty.</summary>
        void Flush();

        /// <summary>Drops in-memory state, e.g. when leaving a world.</summary>
        void Unload();
    }
}
