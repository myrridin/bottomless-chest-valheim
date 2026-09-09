using System;
using System.Collections.Generic;

namespace BottomlessChest.Storage
{
    /// <summary>
    /// Deserializes a saved inventory without instantiating a GameObject per stack.
    /// </summary>
    /// <remarks>
    /// Vanilla <c>Inventory.Load</c> routes every entry through <c>AddItem(string, ...)</c>,
    /// which does <c>Instantiate(prefab)</c>, copies the ItemData off the clone, then
    /// <c>Destroy</c>s it. That is roughly 0.3ms per stack - about three seconds for ten
    /// thousand - and none of the work is needed, since the prefab already holds a template
    /// ItemData that can simply be cloned.
    ///
    /// This only handles format 106. Anything else falls back to vanilla, so a future save
    /// format change degrades to "slow" rather than "wrong". Any failure mid-read also falls
    /// back, because a partially applied inventory is far worse than a slow one.
    ///
    /// Valheim 1.0 writes format 109, which is a different encoding entirely - a bitfield
    /// header, byte grid positions, and a prefab hash where 106 had a name. Stores written
    /// on 1.0 therefore take the vanilla path and load correctly but slowly. Teaching this
    /// reader 109 is worth doing and needs a running game to verify, so it waits for one.
    /// </remarks>
    internal static class FastInventoryReader
    {
        private const int SupportedVersion = 106;

        /// <summary>
        /// Fills <paramref name="inventory"/> from <paramref name="contents"/>.
        /// </summary>
        /// <returns>False if the caller should fall back to vanilla Inventory.Load.</returns>
        internal static bool TryLoad(Inventory inventory, byte[] contents, out int expected)
        {
            expected = 0;

            if (contents == null || ObjectDB.instance == null)
            {
                return false;
            }

            if (!Logic.InventoryPayload.TryReadHeader(contents, out var header)
                || header.ItemVersion != SupportedVersion)
            {
                return false;
            }

            List<ItemDrop.ItemData> loaded;

            try
            {
                var package = new ZPackage(contents);

                // Re-read the header through ZPackage rather than seeking, so the reader's
                // position is where the items start no matter how the header was shaped.
                package.ReadInt();
                package.ReadInt();

                expected = header.Count;
                loaded = new List<ItemDrop.ItemData>(expected);

                for (var i = 0; i < expected; i++)
                {
                    var name = package.ReadString();
                    var stack = package.ReadInt();
                    var durability = package.ReadSingle();
                    var gridPos = package.ReadVector2i();
                    var equipped = package.ReadBool();
                    var quality = package.ReadInt();
                    var variant = package.ReadInt();
                    var crafterId = package.ReadLong();
                    var crafterName = package.ReadString();

                    var customCount = package.ReadInt();
                    Dictionary<string, string> customData = null;
                    for (var j = 0; j < customCount; j++)
                    {
                        customData = customData ?? new Dictionary<string, string>(customCount);
                        customData[package.ReadString()] = package.ReadString();
                    }

                    var worldLevel = package.ReadInt();
                    var pickedUp = package.ReadBool();

                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var prefab = ObjectDB.instance.GetItemPrefab(name);
                    var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    if (drop == null || drop.m_itemData?.m_shared == null)
                    {
                        // Matches vanilla, which logs and skips unknown prefabs.
                        Plugin.Log.LogWarning($"Skipping unknown item prefab '{name}'.");
                        continue;
                    }

                    // Cloning the prefab directly would skip ItemDrop.Awake, and with it any
                    // mod that adjusts shared item data there - stack size, weight,
                    // teleportability. The template has been through Awake exactly once.
                    var source = ItemTemplates.For(name) ?? drop.m_itemData;
                    var item = source.Clone();
                    item.m_dropPrefab = prefab;
                    item.m_stack = stack;
                    item.m_durability = durability;
                    item.m_gridPos = gridPos;
                    item.m_equipped = equipped;
                    item.m_quality = quality;
                    item.m_variant = variant;
                    item.m_crafterID = crafterId;
                    item.m_crafterName = crafterName;
                    item.m_worldLevel = worldLevel;
                    item.m_pickedUp = pickedUp;

                    if (customData != null)
                    {
                        item.m_customData = customData;
                    }

                    loaded.Add(item);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Fast inventory read failed, falling back to the game's loader: {ex.Message}");
                return false;
            }

            // Only commit once the whole package has been read successfully.
            inventory.m_inventory.Clear();
            inventory.m_inventory.AddRange(loaded);

            return true;
        }
    }
}
