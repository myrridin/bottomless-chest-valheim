using System.Collections.Generic;
using System.Linq;
using BottomlessChest.Core;
using BottomlessChest.Storage;
using Jotunn.Entities;
using UnityEngine;

namespace BottomlessChest.Commands
{
    /// <summary>
    /// Console commands for inspecting and recovering chest stores.
    /// </summary>
    internal class BottomlessCommand : ConsoleCommand
    {
        private const float SearchRadius = 8f;

        public override string Name => "bottomless";

        public override string Help =>
            "bottomless list | here | rebind <storeId> | fill <stacks> [prefab] | empty";

        public override void Run(string[] args)
        {
            // A dedicated server has no Console instance to print to.
            if (Console.instance == null)
            {
                Plugin.Log.LogInfo("bottomless: no console available (headless server).");
                return;
            }

            if (args.Length == 0)
            {
                Console.instance.Print(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "list":
                    List();
                    break;

                case "here":
                    Here();
                    break;

                case "rebind":
                    Rebind(args);
                    break;

                case "fill":
                    Fill(args);
                    break;

                case "empty":
                    Empty();
                    break;

                default:
                    Console.instance.Print(Help);
                    break;
            }
        }

        private static void List()
        {
            var summaries = SidecarStore.Instance.Describe().ToList();

            if (summaries.Count == 0)
            {
                Console.instance.Print("No chest stores in this world.");
                return;
            }

            var live = new HashSet<string>(
                BottomlessContainer.Loaded.Select(c => c.CurrentStoreId).Where(id => !string.IsNullOrEmpty(id)));

            foreach (var summary in summaries)
            {
                var state = live.Contains(summary.StoreId) ? "in use" : "ORPHANED";
                Console.instance.Print($"{summary.StoreId}  {summary.StackCount} stacks  [{state}]");
            }
        }

        private static void Here()
        {
            var chest = Nearest();
            Console.instance.Print(chest == null
                ? $"No bottomless chest within {SearchRadius}m."
                : $"Nearest chest is bound to store {chest.CurrentStoreId}.");
        }

        private static void Rebind(IReadOnlyList<string> args)
        {
            if (args.Count < 2)
            {
                Console.instance.Print("Usage: bottomless rebind <storeId>   (stand next to the chest)");
                return;
            }

            var chest = Nearest();
            if (chest == null)
            {
                Console.instance.Print($"No bottomless chest within {SearchRadius}m. Stand closer.");
                return;
            }

            var storeId = args[1].Trim();
            if (!SidecarStore.Instance.TryGet(storeId, out _))
            {
                Console.instance.Print($"No store named '{storeId}'. Run 'bottomless list' to see them.");
                return;
            }

            var previous = chest.CurrentStoreId;
            Console.instance.Print(chest.Rebind(storeId)
                ? $"Chest rebound from {previous} to {storeId}."
                : $"Could not rebind - the chest is not owned by this client. Try again in a moment.");
        }

        /// <summary>
        /// Fills the nearest chest with test data.
        /// </summary>
        /// <remarks>
        /// Exists because vanilla's spawn command is registered with onlyAdmin, which the
        /// constructor turns into OnlyServer - so it cannot be run from a client connected
        /// to a dedicated server, no matter who you are. This is the only practical way to
        /// get bulk test data into a chest in the setup we actually develop against.
        /// </remarks>
        private static void Fill(IReadOnlyList<string> args)
        {
            var chest = Nearest();
            if (chest == null)
            {
                Console.instance.Print($"No bottomless chest within {SearchRadius}m.");
                return;
            }

            if (args.Count < 2 || !int.TryParse(args[1], out var stacks) || stacks < 1)
            {
                Console.instance.Print("Usage: bottomless fill <stacks> [prefab]   e.g. 'bottomless fill 500'");
                return;
            }

            var db = ObjectDB.instance;
            if (db == null)
            {
                Console.instance.Print("Item database is not loaded yet.");
                return;
            }

            var templates = new List<ItemDrop>();

            if (args.Count > 2)
            {
                var prefab = db.GetItemPrefab(args[2]);
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null)
                {
                    Console.instance.Print($"No item prefab called '{args[2]}'.");
                    return;
                }

                if (!IsDisplayable(drop))
                {
                    Console.instance.Print($"'{args[2]}' has no inventory icon and cannot be shown in a chest.");
                    return;
                }

                templates.Add(drop);
            }
            else
            {
                // A spread of item types rather than one repeated - stacking would otherwise
                // collapse the lot into a couple of entries and prove nothing about scale.
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
                Console.instance.Print("No usable item prefabs found.");
                return;
            }

            var inventory = chest.Inventory;
            var added = 0;

            for (var i = 0; i < stacks; i++)
            {
                var template = templates[i % templates.Count];
                var item = template.m_itemData.Clone();

                item.m_dropPrefab = template.gameObject;
                item.m_stack = Mathf.Max(1, item.m_shared.m_maxStackSize);
                item.m_quality = 1;

                // GetIcon() indexes m_icons by variant with no bounds check.
                item.m_variant = 0;

                inventory.m_inventory.Add(item);
                added++;
            }

            chest.NotifyFilled();
            Console.instance.Print($"Added {added} stack(s); chest now holds {inventory.m_inventory.Count}.");
        }

        /// <summary>
        /// Whether an item can actually be drawn in an inventory slot.
        /// </summary>
        /// <remarks>
        /// ObjectDB contains entries with no icons at all - internal or unused items.
        /// ItemData.GetIcon() is an unguarded `m_icons[m_variant]`, so one of those in a
        /// chest throws IndexOutOfRangeException on every single grid redraw, thousands of
        /// times a minute, and the inventory never finishes drawing.
        /// </remarks>
        private static bool IsDisplayable(ItemDrop drop)
        {
            var shared = drop != null ? drop.m_itemData?.m_shared : null;

            return shared != null
                   && shared.m_icons != null
                   && shared.m_icons.Length > 0
                   && !string.IsNullOrEmpty(shared.m_name);
        }

        private static void Empty()
        {
            var chest = Nearest();
            if (chest == null)
            {
                Console.instance.Print($"No bottomless chest within {SearchRadius}m.");
                return;
            }

            var before = chest.Inventory.m_inventory.Count;
            chest.Inventory.RemoveAll();
            chest.NotifyFilled();
            Console.instance.Print($"Removed {before} stack(s).");
        }

        private static BottomlessContainer Nearest()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return null;
            }

            BottomlessContainer best = null;
            var bestDistance = SearchRadius * SearchRadius;

            foreach (var candidate in BottomlessContainer.Loaded)
            {
                var distance = (candidate.Position - player.transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
