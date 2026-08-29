using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BottomlessChest.Storage
{
    /// <summary>
    /// Supplies item templates built the way vanilla builds them.
    /// </summary>
    /// <remarks>
    /// Cloning straight off an ObjectDB prefab looks equivalent to loading an item and is
    /// not. Vanilla's loader calls Object.Instantiate on the prefab, which runs
    /// ItemDrop.Awake, and Awake is where mods adjust shared item data - ValheimPlus sets
    /// max stack size, weight and teleportability there. Awake never runs on a prefab
    /// asset, so a clone taken from one carries unmodified shared data.
    ///
    /// ItemData.Clone is a MemberwiseClone, so every clone shares one SharedData instance
    /// with whatever it was cloned from. That is what makes this cheap to fix and easy to
    /// get wrong: the divergence is invisible until something reads a shared field.
    ///
    /// Instantiating once per distinct item type keeps the fast loader fast - the cost is
    /// the number of item kinds in a chest, not the number of stacks - while giving items
    /// the same shared data they would have had coming through vanilla.
    /// </remarks>
    internal static class ItemTemplates
    {
        private static readonly Dictionary<string, ItemDrop.ItemData> Cache =
            new Dictionary<string, ItemDrop.ItemData>();

        /// <summary>
        /// Returns a template for a prefab, or null when the prefab is unknown.
        /// </summary>
        internal static ItemDrop.ItemData For(string name)
        {
            if (Cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var db = ObjectDB.instance;
            var prefab = db != null ? db.GetItemPrefab(name) : null;
            if (prefab == null)
            {
                return null;
            }

            var template = Build(prefab);
            if (template != null)
            {
                Cache[name] = template;
            }

            return template;
        }

        private static ItemDrop.ItemData Build(GameObject prefab)
        {
            GameObject spawned = null;

            // Matches vanilla's loader, which disables network init around the same call so
            // the throwaway object never registers a ZDO.
            var previous = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;

            try
            {
                spawned = Object.Instantiate(prefab);
                var drop = spawned.GetComponent<ItemDrop>();

                // The ItemData is a plain object; it outlives the GameObject it came from.
                return drop != null ? drop.m_itemData : null;
            }
            catch (System.Exception ex)
            {
                // Falling back to the prefab is worse than this, but not by enough to
                // justify failing a chest load over it.
                Plugin.Log.LogWarning(
                    $"Could not build an item template from '{prefab.name}', " +
                    $"falling back to its prefab data: {ex.Message}");
                return null;
            }
            finally
            {
                ZNetView.m_forceDisableInit = previous;

                if (spawned != null)
                {
                    Object.Destroy(spawned);
                }
            }
        }

        internal static void Clear() => Cache.Clear();

        /// <summary>
        /// Drops templates whenever the item database is rebuilt.
        /// </summary>
        /// <remarks>
        /// Shared data belongs to the ObjectDB the template came from. Changing worlds in
        /// one session builds a new one, and a cached template would then hand out shared
        /// data the rest of the game no longer uses.
        /// </remarks>
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class AwakePatch
        {
            private static void Postfix() => Clear();
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class CopyPatch
        {
            private static void Postfix() => Clear();
        }
    }
}
