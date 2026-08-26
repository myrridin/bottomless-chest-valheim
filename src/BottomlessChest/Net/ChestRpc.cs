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

        internal static void RequestPage(string storeId, string query, int scrollRow)
        {
            var package = new ZPackage();
            package.Write((int)ChestMessage.Page);
            package.Write(storeId);
            package.Write(query ?? string.Empty);
            package.Write(scrollRow);
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

                    var session = ChestSessions.Acquire(storeId);
                    if (session != null)
                    {
                        session.SetQuery(query);
                        SendPage(sender, session, scrollRow);
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
                        Plugin.Log.LogDebug($"Rejecting stale take on chest {storeId}.");
                        SendPage(sender, session, -1);
                        break;
                    }

                    var taken = session.Take(indices);
                    ChestSessions.Persist(session);

                    var granted = new ZPackage();
                    granted.Write((int)ChestMessage.Granted);
                    granted.Write(storeId);
                    granted.Write(Serialize(taken));
                    _rpc.SendPackage(sender, granted);

                    // Keep the player where they were rather than snapping to the top.
                    SendPage(sender, session, -1);
                    break;
                }

                case ChestMessage.Put:
                {
                    var itemBytes = package.ReadByteArray();

                    if (!ChestSessions.TryGet(storeId, out var session))
                    {
                        break;
                    }

                    foreach (var item in Deserialize(itemBytes))
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

                case ChestMessage.Close:
                    ChestSessions.Release(storeId);
                    break;
            }

            yield break;
        }

        /// <summary>Sends one window of items. A negative row means "keep the current one".</summary>
        private static void SendPage(long peer, ChestSession session, int scrollRow)
        {
            var row = scrollRow < 0 ? session.LastScrollRow : scrollRow;
            var page = session.Page(row, Filter.ChestView.PageSlots);

            var reply = new ZPackage();
            reply.Write((int)ChestMessage.PageResult);
            reply.Write(session.StoreId);
            reply.Write(session.Version);
            reply.Write(session.TotalCount);
            reply.Write(session.MatchCount);
            reply.Write(row);
            reply.Write(Serialize(page));

            _rpc.SendPackage(peer, reply);
        }

        private static byte[] Serialize(List<ItemDrop.ItemData> items)
        {
            var scratch = new Inventory("page", null, Filter.ChestView.Width, Filter.ChestView.VisibleRows);
            scratch.m_inventory.AddRange(items);

            var package = new ZPackage();
            scratch.Save(package);
            return package.GetArray();
        }

        private static List<ItemDrop.ItemData> Deserialize(byte[] bytes)
        {
            var scratch = new Inventory("page", null, Filter.ChestView.Width, 4096);

            if (bytes != null && bytes.Length > 0 && !FastInventoryReader.TryLoad(scratch, bytes, out _))
            {
                scratch.Load(new ZPackage(bytes));
            }

            return new List<ItemDrop.ItemData>(scratch.m_inventory);
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
                    var scrollRow = package.ReadInt();
                    var items = Deserialize(package.ReadByteArray());

                    Filter.ChestView.ApplyPage(storeId, version, total, matches, scrollRow, items);
                    break;
                }

                case ChestMessage.Granted:
                {
                    var items = Deserialize(package.ReadByteArray());
                    Filter.ChestView.ApplyGranted(items);
                    break;
                }

                case ChestMessage.Accepted:
                    Filter.ChestView.ApplyAccepted();
                    break;
            }

            yield break;
        }
    }
}
