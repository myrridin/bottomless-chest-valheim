using System.Collections.Generic;
using BottomlessChest.Filter;
using BottomlessChest.Storage;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Server-side registry of chests that currently have a player in them.
    /// </summary>
    /// <remarks>
    /// A session deserializes its chest once and then answers page requests from live data,
    /// rather than re-reading stored bytes per request. Sessions are dropped when the chest
    /// is closed, so idle chests cost nothing but the bytes in the store.
    /// </remarks>
    internal static class ChestSessions
    {
        private static readonly Dictionary<string, ChestSession> Open =
            new Dictionary<string, ChestSession>(System.StringComparer.Ordinal);

        /// <summary>Gets or creates the session for a chest, loading it from the store.</summary>
        internal static ChestSession Acquire(string storeId)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                return null;
            }

            if (Open.TryGetValue(storeId, out var existing))
            {
                existing.MarkUsed();
                return existing;
            }

            if (!SidecarStore.Instance.IsReady)
            {
                return null;
            }

            var inventory = new Inventory("bottomless", null, ChestView.WidthForSession, 1);

            if (SidecarStore.Instance.TryGet(storeId, out var contents) && contents != null && contents.Length > 0)
            {
                if (!InventorySerializer.Load(inventory, contents, out var expected)
                    || inventory.m_inventory.Count != expected)
                {
                    // Persisting this session would write what we failed to read over what
                    // is on disk. Refusing the session leaves the stored contents alone.
                    Plugin.Log.LogError(
                        $"Refusing to open chest {storeId}: its store did not load completely. " +
                        "The stored contents are untouched.");

                    return null;
                }
            }

            var session = new ChestSession(storeId, inventory);
            Open[storeId] = session;

            Plugin.Log.LogDebug($"Opened session for chest {storeId} with {session.TotalCount} stacks.");
            return session;
        }

        internal static bool TryGet(string storeId, out ChestSession session) =>
            Open.TryGetValue(storeId ?? string.Empty, out session);

        /// <summary>Writes a session's contents back to the store and forgets it.</summary>
        internal static void Release(string storeId)
        {
            if (!Open.TryGetValue(storeId ?? string.Empty, out var session))
            {
                return;
            }

            Persist(session);
            Open.Remove(storeId);
        }

        /// <summary>Serializes a session's live contents into the store.</summary>
        internal static void Persist(ChestSession session)
        {
            if (session == null)
            {
                return;
            }

            // Same door as BottomlessContainer.SaveToStore. This is the dedicated-server
            // write path, and it was writing raw Inventory.Save straight into the store -
            // a different format from the other writer, with no count check, into the same
            // file.
            var payload = InventorySerializer.Save(session.Inventory, session.StoreId);
            if (payload == null)
            {
                return;
            }

            SidecarStore.Instance.Put(session.StoreId, payload);
        }

        /// <summary>
        /// Drops sessions nobody has touched for a while.
        /// </summary>
        /// <remarks>
        /// A session is normally released when the client closes the chest, but nothing
        /// guarantees that message arrives - a crash, a lost connection or a force quit all
        /// skip it, and the session would then hold its chest in memory for the life of the
        /// server. Contents are written back before dropping, so expiring is lossless.
        /// </remarks>
        internal static void ReleaseIdle(System.TimeSpan olderThan)
        {
            var cutoff = System.DateTime.UtcNow - olderThan;
            List<string> stale = null;

            foreach (var pair in Open)
            {
                if (pair.Value.LastUsedUtc < cutoff)
                {
                    stale = stale ?? new List<string>();
                    stale.Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (var storeId in stale)
            {
                Plugin.Log.LogInfo($"Releasing idle chest session {storeId}.");
                Release(storeId);
            }
        }

        internal static void PersistAll()
        {
            foreach (var session in Open.Values)
            {
                Persist(session);
            }
        }

        internal static void Clear() => Open.Clear();
    }
}
