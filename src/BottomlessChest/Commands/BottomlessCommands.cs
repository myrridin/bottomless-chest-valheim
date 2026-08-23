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
            "bottomless list | bottomless here | bottomless rebind <storeId>";

        public override void Run(string[] args)
        {
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
