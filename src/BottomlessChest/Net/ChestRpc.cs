using System.Collections;
using BottomlessChest.Core;
using BottomlessChest.Storage;
using Jotunn.Entities;
using Jotunn.Managers;

namespace BottomlessChest.Net
{
    /// <summary>
    /// Moves chest contents between the server, which owns them, and the client viewing them.
    /// </summary>
    /// <remarks>
    /// Contents cannot ride along in the ZDO - that is the whole point of the sidecar store -
    /// so they are fetched explicitly. Jotunn's CustomRPC is used rather than a raw
    /// ZRoutedRpc because it compresses and fragments automatically, and a full chest
    /// snapshot easily exceeds a single packet.
    ///
    /// The vanilla in-use lock guarantees only one client can have a chest open at a time,
    /// so there is exactly one writer per chest and no conflict resolution is needed.
    /// </remarks>
    internal static class ChestRpc
    {
        private const string RpcName = "BottomlessChest_Sync";

        private const int KindRequest = 0;
        private const int KindContents = 1;
        private const int KindSnapshot = 2;

        private static CustomRPC _rpc;

        internal static bool Ready => _rpc != null;

        internal static void Register()
        {
            _rpc = NetworkManager.Instance.AddRPC(RpcName, OnServerReceive, OnClientReceive);
            Plugin.Log.LogInfo($"Registered RPC '{RpcName}'.");
        }

        /// <summary>Client -> server: send me this chest's contents.</summary>
        internal static void RequestContents(string storeId)
        {
            if (_rpc == null || ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(KindRequest);
            package.Write(storeId);

            _rpc.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), package);
        }

        /// <summary>Client -> server: persist what the player just did.</summary>
        internal static void SendSnapshot(string storeId, byte[] contents)
        {
            if (_rpc == null || ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(KindSnapshot);
            package.Write(storeId);
            package.Write(contents);

            _rpc.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), package);
        }

        private static IEnumerator OnServerReceive(long sender, ZPackage package)
        {
            if (package == null || package.Size() == 0)
            {
                yield break;
            }

            var kind = package.ReadInt();
            var storeId = package.ReadString();

            switch (kind)
            {
                case KindRequest:
                {
                    SidecarStore.Instance.TryGet(storeId, out var contents);

                    var reply = new ZPackage();
                    reply.Write(KindContents);
                    reply.Write(storeId);
                    reply.Write(contents ?? new byte[0]);

                    _rpc.SendPackage(sender, reply);
                    Plugin.Log.LogDebug($"Sent {contents?.Length ?? 0} bytes for chest {storeId} to peer {sender}.");
                    break;
                }

                case KindSnapshot:
                {
                    var contents = package.ReadByteArray();
                    SidecarStore.Instance.Put(storeId, contents);

                    // Keep the server's own live copy in step, so anything reading the
                    // container server-side does not see a stale inventory.
                    if (BottomlessContainer.TryResolveByStoreId(storeId, out var container))
                    {
                        container.ApplyRemoteContents(contents);
                    }

                    Plugin.Log.LogDebug($"Stored {contents.Length} bytes for chest {storeId} from peer {sender}.");
                    break;
                }
            }

            yield break;
        }

        private static IEnumerator OnClientReceive(long sender, ZPackage package)
        {
            if (package == null || package.Size() == 0)
            {
                yield break;
            }

            var kind = package.ReadInt();
            if (kind != KindContents)
            {
                yield break;
            }

            var storeId = package.ReadString();
            var contents = package.ReadByteArray();

            if (BottomlessContainer.TryResolveByStoreId(storeId, out var container))
            {
                container.ApplyRemoteContents(contents);
            }
            else
            {
                Plugin.Log.LogWarning($"Contents arrived for chest {storeId}, which is no longer loaded.");
            }

            yield break;
        }
    }
}
