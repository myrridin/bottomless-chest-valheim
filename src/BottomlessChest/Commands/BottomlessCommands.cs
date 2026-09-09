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
            "bottomless list | here | rebind <storeId> | fill <stacks> [prefab] | empty  " +
            "(fill and empty are off by default; see the Testing section of the config)";

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
                    if (RequireCheats())
                    {
                        Fill(args);
                    }

                    break;

                case "empty":
                    if (RequireCheats())
                    {
                        Empty();
                    }

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
        /// <summary>
        /// Gates the development commands behind a config switch and cheats, both.
        /// </summary>
        /// <remarks>
        /// Filling a chest with a hundred thousand items is a testing tool, not a feature.
        /// Devcommands alone is the convention players already know, but it is also a thing
        /// people turn on for an evening and forget about, and these two commands are
        /// destructive in a way spawning an item is not - "empty" discards a chest's
        /// contents outright.
        ///
        /// So the config switch is the real gate and devcommands is the second one. Off by
        /// default means a normal install cannot reach them at all, however the console is
        /// left.
        /// </remarks>
        private static bool RequireCheats()
        {
            if (Settings.ModConfig.EnableTestingCommands == null
                || !Settings.ModConfig.EnableTestingCommands.Value)
            {
                Console.instance?.Print(
                    "This is a testing command, disabled by default. Set EnableTestingCommands " +
                    "in the Testing section of the config to turn it on.");

                return false;
            }

            // Terminal.m_cheat is what "devcommands" toggles, and it is the only honest
            // question here. IsCheatsEnabled() reads like the right one and is not:
            //
            //     if (m_cheat) { if (ZNet.instance) return ZNet.instance.IsServer(); ... }
            //
            // so on a client connected to a dedicated server it always returns false, no
            // matter how many times you type devcommands. These commands were therefore
            // unusable on exactly the setup they are most wanted on, and had been since
            // before 1.0 - the same code is in 0.221.
            //
            // Relaxing this costs nothing: the destructive path is a server RPC, and the
            // server checks its own EnableTestingCommands before acting. This gate only
            // stops someone reaching them by accident on their own machine.
            if (Terminal.m_cheat)
            {
                return true;
            }

            Console.instance?.Print("This is a testing command. Enable it with 'devcommands' first.");
            return false;
        }

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

            if (!Storage.SidecarStore.IsServerAuthority)
            {
                // The chest lives on the server and this client only holds the page, so the
                // server generates and stores the stacks itself.
                Net.ChestRpc.Fill(chest.CurrentStoreId, stacks, args.Count > 2 ? args[2] : null);
                Console.instance.Print($"Asked the server for {stacks} filler stack(s).");
                return;
            }

            var items = TestData.Build(stacks, args.Count > 2 ? args[2] : null, out var error);
            if (error != null)
            {
                Console.instance.Print(error);
                return;
            }

            chest.Inventory.m_inventory.AddRange(items);
            chest.NotifyFilled();
            Console.instance.Print($"Added {items.Count} stack(s); chest now holds {chest.Inventory.m_inventory.Count}.");
        }

        private static void Empty()
        {
            var chest = Nearest();
            if (chest == null)
            {
                Console.instance.Print($"No bottomless chest within {SearchRadius}m.");
                return;
            }

            if (!Storage.SidecarStore.IsServerAuthority)
            {
                Net.ChestRpc.Clear(chest.CurrentStoreId);
                Console.instance.Print("Asked the server to empty the chest.");
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
