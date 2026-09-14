using System;
using System.Runtime.CompilerServices;

namespace BottomlessChest.Core
{
    /// <summary>
    /// An opt-in log of every load and write of a chest's store entry, and which copy did it.
    /// </summary>
    /// <remarks>
    /// Exists for the two-copies investigation: on the server authority a chest can be held
    /// both as the Container's inventory and as a ChestSession's, and both write the same store
    /// entry. Reading console output back by hand lost precision the first time, so each event
    /// is logged with a timestamp, the copy's object identity and a fingerprint of what it held.
    ///
    /// Off unless `bottomless trace on` is typed, so the only standing cost is a bool check.
    /// The fingerprint walks the inventory, which is why it must stay behind that check.
    /// </remarks>
    internal static class StoreTrace
    {
        internal static bool On;

        /// <summary>Prefab name to count separately in every fingerprint, or null.</summary>
        internal static string Watch;

        internal static void Container(
            string what, string storeId, int chestId, Inventory inventory, bool loaded, bool partial)
        {
            if (!On)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[trace] container #{chestId} {what} store {Short(storeId)}: loaded={loaded} " +
                $"partial={partial} authority={Storage.SidecarStore.IsServerAuthority} {Fingerprint(inventory)}");
        }

        internal static void Session(string what, ChestSession session)
        {
            if (!On || session == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[trace] session {what} store {Short(session.StoreId)} v{session.Version}: " +
                Fingerprint(session.Inventory));
        }

        internal static string Fingerprint(Inventory inventory)
        {
            if (inventory == null)
            {
                return "no inventory";
            }

            long items = 0;
            var watched = 0;
            foreach (var item in inventory.m_inventory)
            {
                items += item.m_stack;
                if (Watch != null && string.Equals(PrefabName(item), Watch, StringComparison.OrdinalIgnoreCase))
                {
                    watched++;
                }
            }

            var watch = Watch == null ? string.Empty : $", {Watch} x{watched}";
            return $"inv@{RuntimeHelpers.GetHashCode(inventory):x8} {inventory.m_inventory.Count} stacks, {items} items{watch}";
        }

        private static string PrefabName(ItemDrop.ItemData item) =>
            item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared?.m_name;

        private static string Short(string id) =>
            string.IsNullOrEmpty(id) ? "(none)" : id.Length > 8 ? id.Substring(0, 8) : id;
    }
}
