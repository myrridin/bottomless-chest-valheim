using System.Collections.Generic;
using UnityEngine;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Builds filler stacks for the development commands.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in the command so the server can generate its own. Sending a
    /// hundred thousand generated stacks over the wire would be absurd when both ends have
    /// the same item database.
    /// </remarks>
    internal static class TestData
    {
        /// <summary>
        /// Whether an item can actually be drawn in an inventory slot.
        /// </summary>
        /// <remarks>
        /// ObjectDB holds entries with no icons at all - internal or unused items.
        /// ItemData.GetIcon() is an unguarded m_icons[m_variant], so one of those in a chest
        /// throws on every grid redraw and the inventory never finishes drawing.
        /// </remarks>
        internal static bool IsDisplayable(ItemDrop drop)
        {
            var shared = drop != null ? drop.m_itemData?.m_shared : null;

            return shared != null
                   && shared.m_icons != null
                   && shared.m_icons.Length > 0
                   && !string.IsNullOrEmpty(shared.m_name);
        }

        /// <summary>Generates stacks, cycling item types so they do not all merge.</summary>
        internal static List<ItemDrop.ItemData> Build(int stacks, string prefabName, out string error)
        {
            error = null;
            var result = new List<ItemDrop.ItemData>();

            var db = ObjectDB.instance;
            if (db == null)
            {
                error = "Item database is not loaded.";
                return result;
            }

            var templates = new List<ItemDrop>();

            if (!string.IsNullOrEmpty(prefabName))
            {
                var prefab = db.GetItemPrefab(prefabName);
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;

                if (!IsDisplayable(drop))
                {
                    error = $"'{prefabName}' is not an item that can sit in a chest.";
                    return result;
                }

                templates.Add(drop);
            }
            else
            {
                foreach (var candidate in db.m_items)
                {
                    var drop = candidate != null ? candidate.GetComponent<ItemDrop>() : null;
                    if (IsDisplayable(drop))
                    {
                        templates.Add(drop);
                    }
                }
            }

            if (templates.Count == 0)
            {
                error = "No usable item prefabs found.";
                return result;
            }

            for (var i = 0; i < stacks; i++)
            {
                var template = templates[i % templates.Count];

                // Same reason as the loader: shared data has to come from something that
                // has been through ItemDrop.Awake, or filled stacks are sized by vanilla
                // limits while the rest of the game uses a mod's.
                var source = Storage.ItemTemplates.For(template.gameObject.name)
                             ?? template.m_itemData;
                var item = source.Clone();

                item.m_dropPrefab = template.gameObject;
                item.m_stack = Mathf.Max(1, item.m_shared.m_maxStackSize);
                item.m_quality = 1;

                // GetIcon() indexes m_icons by variant with no bounds check.
                item.m_variant = 0;

                result.Add(item);
            }

            return result;
        }
    }
}
