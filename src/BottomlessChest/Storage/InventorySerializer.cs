using System;
using System.Collections.Generic;
using BottomlessChest.Logic;

namespace BottomlessChest.Storage
{
    /// <summary>
    /// Reads and writes a stored inventory, owning the payload format on both sides.
    /// </summary>
    /// <remarks>
    /// Vanilla <c>Inventory.Load</c> routes every entry through a per-item
    /// <c>Instantiate(prefab)</c>, copies the ItemData off the clone, then <c>Destroy</c>s
    /// it. That is roughly 0.3ms per stack - about three seconds for ten thousand - and none
    /// of the work is needed, since the prefab already holds a template ItemData whose
    /// shared data can simply be referenced.
    ///
    /// Three payload shapes reach this, and the caller must not have to know which:
    ///
    /// - **Ours** (marker, version, int count). Written by this build. Items are in the
    ///   game's own per-item encoding, so they are read by the game's own
    ///   <c>ItemData.Load</c> - which keeps this correct across item format changes we know
    ///   nothing about. The only thing we do ourselves is the count.
    /// - **Format 106** (Valheim through 0.221). Read by hand, below. Existing stores from
    ///   0.1.0 are all this shape.
    /// - **Anything else** - a vanilla payload from a version we do not walk. Handed
    ///   straight to the game's loader: slower, but right.
    ///
    /// Deciding here rather than returning "false, you try" matters. Our own format cannot
    /// be handed to <c>Inventory.Load</c> - it would read the marker as a version and walk
    /// off into nonsense - so a caller that guessed wrong would silently load garbage.
    ///
    /// A read that fails part way leaves the inventory empty and reports the count the
    /// payload claimed. That mismatch is what makes <c>BottomlessContainer</c> refuse to
    /// save, which is the whole reason a partial load is survivable.
    /// </remarks>
    internal static class InventorySerializer
    {
        /// <summary>The item format Valheim wrote through 0.221, read by hand below.</summary>
        private const int LegacyVersion = 106;

        /// <summary>
        /// Serializes an inventory for the store, recording the stack count ourselves.
        /// </summary>
        /// <param name="label">Chest identifier, for the log if this refuses.</param>
        /// <returns>The payload, or null if it could not be trusted - do not write null.</returns>
        /// <remarks>
        /// Valheim 1.0 writes the stack count as a <c>ushort</c>, so an inventory past
        /// 65,535 stacks serializes with a count that has wrapped. Nothing throws: the items
        /// are all in the buffer, the header just says there are fewer, and the next load
        /// stops early and drops the rest. Written back, that is permanent - and this mod
        /// exists to hold far more than that.
        ///
        /// So the header becomes ours and the items stay the game's. Only the count is
        /// rewritten; every byte after it is the game's own item encoding, which is what
        /// keeps this correct across item formats we know nothing about.
        ///
        /// Every write to the store goes through here. There are two callers - a chest
        /// saving itself, and a server-side session being persisted - and having them agree
        /// on the format is not optional, since they write to the same file. The bugs this
        /// project keeps hitting are all one behaviour reachable by two paths and handled on
        /// one of them.
        /// </remarks>
        internal static byte[] Save(Inventory inventory, string label)
        {
            if (inventory == null)
            {
                return null;
            }

            var package = new ZPackage();
            inventory.Save(package);
            var bytes = package.GetArray();

            var expected = inventory.m_inventory.Count;

            if (!InventoryPayload.TryReadHeader(bytes, out _))
            {
                Plugin.Log.LogError(
                    $"Refusing to save chest {label}: the game produced {bytes.Length} bytes " +
                    "this build cannot read back. Its save format has probably changed.");

                return null;
            }

            var wrapped = InventoryPayload.Wrap(bytes, expected);

            // Belt and braces: what we are about to store has to read back as what went in,
            // because the cost of being wrong is a chest that loads short and then saves.
            if (!InventoryPayload.TryReadHeader(wrapped, out var check) || check.Count != expected)
            {
                Plugin.Log.LogError(
                    $"Refusing to save chest {label}: it holds {expected} stacks but the " +
                    "payload does not read back as holding that many. The chest's stored " +
                    "contents are untouched.");

                return null;
            }

            return wrapped;
        }

        /// <summary>
        /// Fills <paramref name="inventory"/> from <paramref name="contents"/>.
        /// </summary>
        /// <param name="expected">
        /// Stacks the payload says it holds. Compare against what actually arrived: fewer
        /// means the load was partial and the contents must not be written back.
        /// </param>
        /// <returns>
        /// False if the payload could not be read at all. The caller must treat that as a
        /// partial load: <paramref name="expected"/> is unknowable in that case, so a bare
        /// count comparison would see nothing loaded, nothing expected, and conclude all was
        /// well - then write an empty chest over the stored bytes.
        /// </returns>
        internal static bool Load(Inventory inventory, byte[] contents, out int expected)
        {
            expected = 0;

            if (contents == null || contents.Length == 0)
            {
                return true;
            }

            if (!InventoryPayload.TryReadHeader(contents, out var header))
            {
                // Not a shape we recognise. The game's loader is more likely to be right
                // about its own format than we are - but if it cannot read it either, we
                // never learn how many stacks were in there.
                return LoadWithGame(inventory, contents);
            }

            expected = header.Count;

            if (header.Format == PayloadFormat.Bottomless)
            {
                return LoadOurs(inventory, contents, header);
            }

            if (header.Format == PayloadFormat.VanillaIntCount
                && header.ItemVersion == LegacyVersion
                && TryLoadLegacy(inventory, contents, header))
            {
                return true;
            }

            return LoadWithGame(inventory, contents);
        }

        /// <summary>Reads our own framing, delegating each item to the game.</summary>
        private static bool LoadOurs(Inventory inventory, byte[] contents, PayloadHeader header)
        {
            // An item encoding older than the compact one has no ItemData.Load to call, so
            // put the payload back into the shape the game wrote and let it read its own.
            // Unreachable today - we only ever wrap what 1.0 and later produce - but the
            // alternative to handling it is loading nonsense.
            if (header.ItemVersion < InventoryPayload.FirstUShortCountVersion)
            {
                if (InventoryPayload.TryUnwrapToVanilla(contents, out var vanilla))
                {
                    return LoadWithGame(inventory, vanilla);
                }
                else
                {
                    Plugin.Log.LogError(
                        $"Chest store is in this mod's format at item version {header.ItemVersion}, " +
                        "which cannot be expressed for the game's loader. Leaving the chest empty; " +
                        "it will refuse to save, so nothing stored is overwritten.");

                    inventory.m_inventory.Clear();
                    return false;
                }
            }

            if (ObjectDB.instance == null)
            {
                Plugin.Log.LogError(
                    "Cannot read a chest store before the item database exists. Leaving the " +
                    "chest empty; it will refuse to save.");

                inventory.m_inventory.Clear();
                return false;
            }

            var loaded = new List<ItemDrop.ItemData>(Math.Min(header.Count, 4096));

            try
            {
                var package = new ZPackage(contents);

                // Step past the header through ZPackage itself, so the read position is
                // wherever the header actually ended.
                package.ReadInt();
                package.ReadInt();
                package.ReadInt();

                var version = (Version.Item)header.ItemVersion;

                for (var i = 0; i < header.Count; i++)
                {
                    var item = new ItemDrop.ItemData();

                    // The game fills every per-instance field, including ones this mod has
                    // never heard of, and hands back the prefab hash it was saved under.
                    var prefabHash = ItemDrop.ItemData.Load(package, item, version);

                    var prefab = ObjectDB.instance.GetItemPrefab(prefabHash);
                    var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;

                    if (drop == null || drop.m_itemData?.m_shared == null)
                    {
                        // Matches vanilla, which logs and skips prefabs it cannot resolve.
                        Plugin.Log.LogWarning($"Skipping unknown item prefab hash {prefabHash}.");
                        continue;
                    }

                    // Shared data is shared by reference in vanilla too - every stack of a
                    // type points at one SharedData. Taking the template's means picking up
                    // whatever ItemDrop.Awake did to it, including another mod's changes to
                    // stack size, weight or teleportability.
                    var template = ItemTemplates.For(prefab.name) ?? drop.m_itemData;
                    item.m_shared = template.m_shared;
                    item.m_dropPrefab = prefab;

                    loaded.Add(item);
                }
            }
            catch (Exception ex)
            {
                // No fallback exists for our own format, so this is as far as it goes.
                // An empty inventory that refuses to save beats a partial one that does not.
                Plugin.Log.LogError(
                    $"Chest store could not be read after {loaded.Count} of {header.Count} " +
                    $"stacks: {ex.Message}. Leaving the chest empty; it will refuse to save, " +
                    "so the stored contents are untouched.");

                inventory.m_inventory.Clear();
                return false;
            }

            Commit(inventory, loaded);
            return true;
        }

        /// <summary>Reads format 106 by hand, which is every store written before 1.0.</summary>
        private static bool TryLoadLegacy(Inventory inventory, byte[] contents, PayloadHeader header)
        {
            if (ObjectDB.instance == null)
            {
                return false;
            }

            List<ItemDrop.ItemData> loaded;

            try
            {
                var package = new ZPackage(contents);
                package.ReadInt();
                package.ReadInt();

                loaded = new List<ItemDrop.ItemData>(Math.Min(header.Count, 4096));

                for (var i = 0; i < header.Count; i++)
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
                        Plugin.Log.LogWarning($"Skipping unknown item prefab '{name}'.");
                        continue;
                    }

                    // Cloning the prefab directly would skip ItemDrop.Awake, and with it any
                    // mod that adjusts shared item data there. The template has been through
                    // Awake exactly once.
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
                // Format 106 is also readable by the game, so there is somewhere safe to go.
                Plugin.Log.LogWarning(
                    $"Fast inventory read failed, falling back to the game's loader: {ex.Message}");

                return false;
            }

            Commit(inventory, loaded);
            return true;
        }

        /// <returns>False if even the game could not read it.</returns>
        private static bool LoadWithGame(Inventory inventory, byte[] contents)
        {
            try
            {
                inventory.Load(new ZPackage(contents));
                return true;
            }
            catch (Exception ex)
            {
                // Reporting this rather than swallowing it is what stops the caller writing
                // an empty chest over bytes it could not read.
                Plugin.Log.LogError(
                    $"The game could not read this chest store either: {ex.Message}. Leaving " +
                    "the chest empty; it will refuse to save.");

                inventory.m_inventory.Clear();
                return false;
            }
        }

        /// <summary>Replaces the contents only once the whole payload has been read.</summary>
        private static void Commit(Inventory inventory, List<ItemDrop.ItemData> loaded)
        {
            inventory.m_inventory.Clear();
            inventory.m_inventory.AddRange(loaded);
        }
    }
}
