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

            if (contents == null || contents.Length < 8 || ObjectDB.instance == null)
            {
                return false;
            }

            List<ItemDrop.ItemData> loaded;

            try
            {
                var package = new ZPackage(contents);

                if (package.ReadInt() != SupportedVersion)
                {
                    return false;
                }

                expected = package.ReadInt();
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

                    var item = drop.m_itemData.Clone();
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
