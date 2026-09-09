using System.Collections;
using System.Collections.Generic;
using BottomlessChest.Core;
using BottomlessChest.Storage;
using Jotunn.Entities;
using Jotunn.Managers;

namespace BottomlessChest.Net
{
    /// <summary>
    /// Pages chest contents between the server, which owns them, and the client viewing them.
    /// </summary>
    /// <remarks>
    /// An earlier design sent whole inventories. That caps out near a megabyte: Jotunn slices
    /// a package into 250KB fragments and waits for the peer's send queue to drain between
    /// each, disconnecting after thirty seconds - so the true limit is Valheim's per-peer
    /// bandwidth and no amount of chunking beats it. Sending only what is on screen removes
    /// the dependency on chest size entirely.
    ///
    /// Takes and puts are authoritative round trips rather than optimistic local edits. The
    /// client asks, the server decides, and only then does an item move. A lost message costs
    /// an action; an optimistic one that the server later rejects would duplicate an item.
    /// </remarks>
    internal static class ChestRpc
    {
        private const string RpcName = "BottomlessChest_Sync";

        private static CustomRPC _rpc;

        internal static bool Ready => _rpc != null;

        internal static void Register()
        {
            _rpc = NetworkManager.Instance.AddRPC(RpcName, OnServerReceive, OnClientReceive);
            Plugin.Log.LogInfo($"Registered RPC '{RpcName}'.");
        }

        private static void ToServer(ZPackage package)
        {
            if (_rpc != null && ZRoutedRpc.instance != null)
            {
                _rpc.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), package);
            }
        }

        internal static void Open(string storeId)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Open);
            package.Write(storeId);
            ToServer(package);
        }

        /// <summary>
        /// Asks for a page. <paramref name="requestId"/> comes back on the reply so the
        /// client can tell a current answer from a superseded one.
        /// </summary>
        internal static void RequestPage(string storeId, string query, int scrollRow, int requestId)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Page);
            package.Write(storeId);
            package.Write(query ?? string.Empty);
            package.Write(scrollRow);
            package.Write(requestId);
            ToServer(package);
        }

        internal static void Take(string storeId, long version, IReadOnlyList<int> indices)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Take);
            package.Write(storeId);
            package.Write(version);
            package.Write(indices.Count);
            foreach (var index in indices)
            {
                package.Write(index);
            }

            ToServer(package);
        }

        internal static void Put(string storeId, byte[] itemBytes)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Put);
            package.Write(storeId);
            package.Write(itemBytes);
            ToServer(package);
        }

        internal static void Fill(string storeId, int stacks, string prefabName)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Fill);
            package.Write(storeId);
            package.Write(stacks);
            package.Write(prefabName ?? string.Empty);
            ToServer(package);
        }

        internal static void Clear(string storeId)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Clear);
            package.Write(storeId);
            ToServer(package);
        }

        /// <summary>
        /// Offers the player's stackable items to the chest.
        /// </summary>
        /// <remarks>
        /// Sent whole because a player inventory is a few dozen items at most - unlike the
        /// chest, which is why this direction can afford what the other cannot.
        /// </remarks>
        internal static void StackAll(string storeId, byte[] candidateBytes)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.StackAll);
            package.Write(storeId);
            package.Write(candidateBytes);
            ToServer(package);
        }

        internal static void Close(string storeId)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Close);
            package.Write(storeId);
            ToServer(package);
        }

        private static IEnumerator OnServerReceive(long sender, ZPackage package)
        {
            if (package == null || package.Size() == 0)
            {
                yield break;
            }

            var kind = (ChestMessage)package.ReadInt();
            var storeId = package.ReadString();

            switch (kind)
            {
                case ChestMessage.Open:
                {
                    var session = ChestSessions.Acquire(storeId);
                    if (session != null)
                    {
                        SendPage(sender, session, 0);
                    }

                    break;
                }

                case ChestMessage.Page:
                {
                    var query = package.ReadString();
                    var scrollRow = package.ReadInt();
                    var requestId = package.ReadInt();

                    var session = ChestSessions.Acquire(storeId);
                    if (session != null)
                    {
                        session.SetQuery(query);
                        SendPage(sender, session, scrollRow, requestId);
                    }

                    break;
                }

                case ChestMessage.Take:
                {
                    var version = package.ReadLong();
                    var count = package.ReadInt();
                    var indices = new List<int>(count);
                    for (var i = 0; i < count; i++)
                    {
                        indices.Add(package.ReadInt());
                    }

                    if (!ChestSessions.TryGet(storeId, out var session))
                    {
                        break;
                    }

                    if (session.Version != version)
                    {
                        // The client acted on a stale view; give it a fresh one instead of
                        // removing something it did not mean to.
                        Plugin.Log.LogWarning(
                            $"Rejecting stale take on chest {storeId}: client is at v{version}, " +
                            $"session is at v{session.Version}.");
                        SendPage(sender, session, -1);
                        break;
                    }

                    var taken = session.Take(indices);
                    ChestSessions.Persist(session);

                    Plugin.Log.LogDebug(
                        $"Take from {storeId}: {indices.Count} requested, {taken.Count} removed, " +
                        $"{session.TotalCount} left.");

                    // The items are already out of the session and persisted, so an
                    // unsendable reply loses them outright. Serialize returning null is the
                    // "do not lose data" signal; turning it into an empty payload here would
                    // invert it on the one path where that is fatal.
                    var takenBytes = Serialize(taken);
                    if (takenBytes == null)
                    {
                        Plugin.Log.LogError(
                            $"Could not send {taken.Count} taken stack(s) from chest {storeId}. " +
                            "Putting them back rather than dropping them.");

                        foreach (var item in taken)
                        {
                            session.Add(item);
                        }

                        ChestSessions.Persist(session);
                        SendPage(sender, session, -1);
                        break;
                    }

                    var granted = new ZPackage();
                    granted.Write((int)ChestMessage.Granted);
                    granted.Write(storeId);
                    granted.Write(takenBytes);
                    _rpc.SendPackage(sender, granted);

                    // Counts only, not a page: re-sending one would compact the remaining
                    // items upwards and the window would appear to scroll under the cursor.
                    // The client blanks the slots it asked for instead.
                    SendCounts(sender, session);
                    break;
                }

                case ChestMessage.Put:
                {
                    var itemBytes = package.ReadByteArray();

                    if (!ChestSessions.TryGet(storeId, out var session))
                    {
                        break;
                    }

                    // Accepted makes the client delete its copy of the item. Sending it for
                    // a payload we could not read destroys the item outright.
                    if (!TryDeserialize(itemBytes, out var deposited))
                    {
                        Plugin.Log.LogError(
                            $"Could not read a deposit into chest {storeId}; refusing it so the " +
                            "sender keeps the item.");

                        SendPage(sender, session, -1);
                        break;
                    }

                    foreach (var item in deposited)
                    {
                        session.Add(item);
                    }

                    ChestSessions.Persist(session);

                    var accepted = new ZPackage();
                    accepted.Write((int)ChestMessage.Accepted);
                    accepted.Write(storeId);
                    _rpc.SendPackage(sender, accepted);

                    SendPage(sender, session, -1);
                    break;
                }

                case ChestMessage.Fill:
                {
                    var stacks = package.ReadInt();
                    var prefabName = package.ReadString();

                    if (!TestingAllowed(sender, "fill", storeId))
                    {
                        break;
                    }

                    var session = ChestSessions.Acquire(storeId);
                    if (session == null)
                    {
                        break;
                    }

                    // Generated on the server: both ends share an item database, so sending
                    // a hundred thousand stacks over the wire would be pointless.
                    var items = TestData.Build(stacks, prefabName, out var error);
                    if (error != null)
                    {
                        Plugin.Log.LogWarning($"Fill refused for chest {storeId}: {error}");
                        break;
                    }

                    foreach (var item in items)
                    {
                        session.Add(item);
                    }

                    ChestSessions.Persist(session);
                    Plugin.Log.LogDebug($"Added {items.Count} filler stacks to chest {storeId}.");
                    SendPage(sender, session, -1);
                    break;
                }

                case ChestMessage.Clear:
                {
                    if (!TestingAllowed(sender, "empty", storeId))
                    {
                        break;
                    }

                    var session = ChestSessions.Acquire(storeId);
                    if (session == null)
                    {
                        break;
                    }

                    var before = session.TotalCount;
                    session.Inventory.m_inventory.Clear();
                    session.Touch();
                    ChestSessions.Persist(session);

                    Plugin.Log.LogDebug($"Emptied chest {storeId} of {before} stacks.");
                    SendPage(sender, session, 0);
                    break;
                }

                case ChestMessage.StackAll:
                {
                    var offered = Deserialize(package.ReadByteArray());

                    // Depositing works on a closed chest, so this may be the only thing
                    // holding the session. Release it again if nobody had it open.
                    var wasOpen = ChestSessions.TryGet(storeId, out _);

                    var session = ChestSessions.Acquire(storeId);
                    if (session == null)
                    {
                        break;
                    }

                    // Only items the chest already holds are taken, which is what "stack"
                    // means as opposed to "dump everything in".
                    var held = new HashSet<string>(System.StringComparer.Ordinal);
                    foreach (var item in session.Inventory.m_inventory)
                    {
                        held.Add(StackKey(item));
                    }

                    var kept = new List<int>();
                    for (var i = 0; i < offered.Count; i++)
                    {
                        var item = offered[i];
                        if (item.m_shared.m_maxStackSize <= 1 || !held.Contains(StackKey(item)))
                        {
                            continue;
                        }

                        session.Add(item);
                        kept.Add(i);
                    }

                    Plugin.Log.LogDebug(
                        $"Deposit into {storeId}: {offered.Count} offered, {held.Count} distinct types held, " +
                        $"{kept.Count} kept.");

                    if (kept.Count > 0)
                    {
                        ChestSessions.Persist(session);
                    }

                    var stacked = new ZPackage();
                    stacked.Write((int)ChestMessage.Stacked);
                    stacked.Write(storeId);
                    stacked.Write(kept.Count);
                    foreach (var index in kept)
                    {
                        stacked.Write(index);
                    }

                    _rpc.SendPackage(sender, stacked);

                    if (wasOpen)
                    {
                        SendPage(sender, session, -1);
                    }
                    else
                    {
                        ChestSessions.Release(storeId);
                    }

                    break;
                }

                case ChestMessage.Close:
                    ChestSessions.Release(storeId);
                    break;
            }

            yield break;
        }

        /// <summary>Sends fresh totals without disturbing the layout the client is showing.</summary>
        private static void SendCounts(long peer, ChestSession session)
        {
            var counts = new ZPackage();
            counts.Write((int)ChestMessage.Counts);
            counts.Write(session.StoreId);
            counts.Write(session.Version);
            counts.Write(session.TotalCount);
            counts.Write(session.MatchCount);
            counts.Write(session.TotalWeight);

            _rpc.SendPackage(peer, counts);
        }

        /// <summary>Sends one window of items. A negative row means "keep the current one".</summary>
        private static void SendPage(long peer, ChestSession session, int scrollRow, int requestId = 0)
        {
            var requested = scrollRow < 0 ? session.LastScrollRow : scrollRow;
            var page = session.Page(requested, Filter.ChestView.PageSlots);

            // Page clamps to what actually exists. Echoing the requested row instead would
            // leave the client believing it is scrolled further than it is, and its own
            // clamp would then refuse to move.
            var row = session.LastScrollRow;

            var reply = new ZPackage();
            reply.Write((int)ChestMessage.PageResult);
            reply.Write(session.StoreId);
            reply.Write(session.Version);
            reply.Write(session.TotalCount);
            reply.Write(session.MatchCount);
            reply.Write(session.TotalWeight);
            reply.Write(row);
            reply.Write(requestId);
            reply.Write(Serialize(page));

            _rpc.SendPackage(peer, reply);
        }

        /// <summary>Identity for stacking: same item, same quality, same variant.</summary>
        private static string StackKey(ItemDrop.ItemData item) =>
            $"{item.m_shared.m_name}|{item.m_quality}|{item.m_variant}";

        private static byte[] Serialize(List<ItemDrop.ItemData> items)
        {
            var scratch = new Inventory("page", null, Filter.ChestView.Width, Filter.ChestView.VisibleRows);
            scratch.m_inventory.AddRange(items);

            // Wrapped, like everything else we write, so the other end reads it back with a
            // direct add rather than through Inventory.AddItem. AddItem places items by grid
            // position, and 1.0 stores grid positions as bytes - so y wraps at 256 and an
            // 8-wide chest has only 2048 distinct slots. Two stacks of one item from far
            // apart in a big chest can land on a page sharing a position, and AddItem would
            // merge them into one. On a Granted reply that is items the server has already
            // removed and will never send again.
            return Storage.InventorySerializer.Save(scratch, "page");
        }

        /// <summary>
        /// Whether this machine allows the testing commands to act on its chests.
        /// </summary>
        /// <remarks>
        /// The console gate in BottomlessCommands only governs the machine typing the
        /// command. These two messages are destructive - "empty" discards a chest outright -
        /// and the server has no way to know what the sender's config says, or whether the
        /// sender is running this version at all. A 0.1.0 client predates the setting
        /// entirely and would happily send either.
        ///
        /// So the server decides for its own chests. A peer cannot talk a server into
        /// wiping a chest by turning a flag on at its end.
        /// </remarks>
        private static bool TestingAllowed(long sender, string what, string storeId)
        {
            if (Settings.ModConfig.EnableTestingCommands != null
                && Settings.ModConfig.EnableTestingCommands.Value)
            {
                return true;
            }

            Plugin.Log.LogWarning(
                $"Refused '{what}' on chest {storeId} from peer {sender}: testing commands are " +
                "disabled here. Enable EnableTestingCommands in the Testing section of this " +
                "machine's config if that was intended.");

            return false;
        }

        private static List<ItemDrop.ItemData> Deserialize(byte[] bytes) =>
            TryDeserialize(bytes, out var items) ? items : new List<ItemDrop.ItemData>();

        /// <summary>Reads a wire payload, reporting whether it could be read at all.</summary>
        /// <remarks>
        /// The caller has to know. A payload that fails to load yields an empty list, which
        /// is indistinguishable from an empty chest unless the failure is reported - and on
        /// the deposit path that difference decides whether the sender keeps their item.
        /// </remarks>
        private static bool TryDeserialize(byte[] bytes, out List<ItemDrop.ItemData> items)
        {
            var scratch = new Inventory("page", null, Filter.ChestView.Width, 4096);

            var ok = bytes == null || bytes.Length == 0
                || InventorySerializer.Load(scratch, bytes, out _);

            items = new List<ItemDrop.ItemData>(scratch.m_inventory);
            return ok;
        }

        private static IEnumerator OnClientReceive(long sender, ZPackage package)
        {
            if (package == null || package.Size() == 0)
            {
                yield break;
            }

            var kind = (ChestMessage)package.ReadInt();
            var storeId = package.ReadString();

            switch (kind)
            {
                case ChestMessage.PageResult:
                {
                    var version = package.ReadLong();
                    var total = package.ReadInt();
                    var matches = package.ReadInt();
                    var weight = package.ReadSingle();
                    var scrollRow = package.ReadInt();
                    var requestId = package.ReadInt();
                    var items = Deserialize(package.ReadByteArray());

                    Filter.ChestView.ApplyPage(
                        storeId, version, total, matches, weight, scrollRow, requestId, items);
                    break;
                }

                case ChestMessage.Granted:
                {
                    var items = Deserialize(package.ReadByteArray());
                    Filter.ChestView.ApplyGranted(items);
                    break;
                }

                case ChestMessage.Counts:
                {
                    var version = package.ReadLong();
                    var total = package.ReadInt();
                    var matches = package.ReadInt();
                    var weight = package.ReadSingle();

                    Filter.ChestView.ApplyCounts(storeId, version, total, matches, weight);
                    break;
                }

                case ChestMessage.Accepted:
                    Filter.ChestView.ApplyAccepted();
                    break;

                case ChestMessage.Stacked:
                {
                    var count = package.ReadInt();
                    var kept = new List<int>(count);
                    for (var i = 0; i < count; i++)
                    {
                        kept.Add(package.ReadInt());
                    }

                    Filter.ChestView.ApplyStacked(storeId, kept);
                    break;
                }
            }

            yield break;
        }
    }
}
