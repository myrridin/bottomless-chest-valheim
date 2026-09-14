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
            "bottomless list | here | probe | trace on [prefab] | trace off | rebind <storeId> [zdo-only] | fill <stacks> [prefab] | empty | " +
            "session-put [prefab] | session-hold | session-release  (fill, empty and the session commands are off by default; see the Testing " +
            "section of the config)";

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

                case "probe":
                    Probe();
                    break;

                case "trace":
                    Trace(args);
                    break;

                case "session-put":
                    if (RequireCheats())
                    {
                        SessionPut(args);
                    }

                    break;

                case "session-hold":
                case "session-release":
                    if (RequireCheats())
                    {
                        SessionHold(args[0].ToLowerInvariant() == "session-hold");
                    }

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

        /// <summary>
        /// Reports each copy of the nearest chest's contents that this machine holds.
        /// </summary>
        /// <remarks>
        /// On the server authority a chest can exist twice in memory: the Container's own
        /// inventory, loaded once from the store, and a ChestSession's, which remote players'
        /// takes and deposits change. Nothing reconciles the two, and both write the same store
        /// entry. This prints all three side by side so a disagreement is visible directly,
        /// rather than inferred from which items went missing afterwards.
        /// </remarks>
        private static void Probe()
        {
            var chest = Nearest();
            if (chest == null)
            {
                Console.instance.Print($"No bottomless chest within {SearchRadius}m.");
                return;
            }

            var storeId = chest.CurrentStoreId;
            Report($"Chest {storeId}");

            if (!SidecarStore.IsServerAuthority)
            {
                Report(
                    "  Not the server authority: this machine holds a page, not the chest. " +
                    "Run probe on the host.");
                return;
            }

            Report(
                "  container copy : " +
                (chest.AwaitingContents ? "not loaded" : StoreTrace.Fingerprint(chest.Inventory)));
            Report($"  store          : {StoreStacks(storeId)}");
            Report(ChestSessions.TryGet(storeId, out var session)
                ? $"  session        : open, v{session.Version}, {StoreTrace.Fingerprint(session.Inventory)}"
                : "  session        : none open");
        }

        /// <summary>
        /// Prints to the console and to the log.
        /// </summary>
        /// <remarks>
        /// The log copy is the one that matters: it carries a timestamp and is read back
        /// exactly, where console output had to be retyped and lost precision doing it. Only
        /// probe calls this, so it costs nothing unless someone asks.
        /// </remarks>
        private static void Report(string line)
        {
            Console.instance.Print(line);
            Plugin.Log.LogInfo("[probe] " + line);
        }

        private static void Trace(IReadOnlyList<string> args)
        {
            var mode = args.Count > 1 ? args[1].ToLowerInvariant() : string.Empty;
            if (mode == "on")
            {
                StoreTrace.Watch = args.Count > 2 ? args[2] : null;
                StoreTrace.On = true;
                Report($"Store trace on{(StoreTrace.Watch == null ? string.Empty : $", watching {StoreTrace.Watch}")}. Events go to LogOutput.log.");
            }
            else if (mode == "off")
            {
                StoreTrace.On = false;
                Report("Store trace off.");
            }
            else
            {
                Console.instance.Print("Usage: bottomless trace on [prefab] | bottomless trace off");
            }
        }

        private static string StoreStacks(string storeId) =>
            !string.IsNullOrEmpty(storeId)
            && SidecarStore.Instance.TryGet(storeId, out var bytes)
            && bytes != null
            && Logic.InventoryPayload.TryReadHeader(bytes, out var header)
                ? $"{header.Count} stacks"
                : "no entry";

        /// <summary>
        /// Deposits one stack into the nearest chest the way a remote player's deposit reaches
        /// the host: through a session.
        /// </summary>
        /// <remarks>
        /// Runs the same server-side sequence a client's open, deposit and close trigger -
        /// Acquire, Deposit, Persist, Release - without needing a second machine. It exists to
        /// reproduce the two-copies question on a single player: the host is the server
        /// authority, so this creates exactly the session a remote client would. Pick an item
        /// the chest does not already hold, or the deposit merges and adds no stack to see.
        /// </remarks>
        private static void SessionPut(IReadOnlyList<string> args)
        {
            var chest = Nearest();
            if (chest == null)
            {
                Console.instance.Print($"No bottomless chest within {SearchRadius}m.");
                return;
            }

            if (!SidecarStore.IsServerAuthority)
            {
                Console.instance.Print(
                    "session-put reproduces the host's side of a remote deposit, so it only runs " +
                    "on the server authority.");
                return;
            }

            var prefab = args.Count > 1 ? args[1] : "Thistle";
            var items = TestData.Build(1, prefab, out var error);
            if (error != null)
            {
                Console.instance.Print(error);
                return;
            }

            var storeId = chest.CurrentStoreId;

            // A session already open belongs to someone - a player with the chest open, or
            // session-hold - and releasing it would leave that player's next take or deposit
            // with nothing to answer it. Only a session opened here is released here.
            var alreadyOpen = ChestSessions.TryGet(storeId, out _);
            var session = ChestSessions.Acquire(storeId);
            if (session == null)
            {
                Console.instance.Print("Could not open a session on this chest; the store may not be ready yet.");
                return;
            }

            var before = session.TotalCount;
            session.Deposit(items[0]);
            ChestSessions.Persist(session);
            var after = session.TotalCount;

            if (!alreadyOpen)
            {
                ChestSessions.Release(storeId);
            }

            Console.instance.Print($"Deposited one {prefab} stack through a session.");
            Console.instance.Print(
                $"  session went {before} -> {after} stacks" +
                (after == before ? " - it merged into an existing stack; use an item the chest does not hold" : string.Empty));
            Probe();
        }

        /// <summary>
        /// Opens or releases a session on the nearest chest and leaves it that way.
        /// </summary>
        /// <remarks>
        /// Holding one open is what a remote player with the chest open looks like from the
        /// host. It lets the reverse of session-put be tested on one machine: change the chest
        /// on the host while the session is held, then release it and check the change
        /// survived the session writing the chest back.
        /// </remarks>
        /// <summary>Sessions opened by session-hold, the only ones session-release may close.</summary>
        private static readonly HashSet<string> HeldByCommand = new HashSet<string>(System.StringComparer.Ordinal);

        private static void SessionHold(bool hold)
        {
            var chest = Nearest();
            if (chest == null)
            {
                Console.instance.Print($"No bottomless chest within {SearchRadius}m.");
                return;
            }

            if (!SidecarStore.IsServerAuthority)
            {
                Console.instance.Print("Session commands only run on the server authority.");
                return;
            }

            var storeId = chest.CurrentStoreId;
            if (hold)
            {
                if (ChestSessions.TryGet(storeId, out _))
                {
                    Report("A session is already open on this chest; leaving it to whoever opened it.");
                }
                else if (ChestSessions.Acquire(storeId) == null)
                {
                    Report("Could not open a session on this chest; the store may not be ready yet.");
                }
                else
                {
                    HeldByCommand.Add(storeId);
                    Report("Holding a session open on this chest, as a remote player with it open would.");
                }
            }
            else if (!HeldByCommand.Remove(storeId ?? string.Empty))
            {
                // Releasing a real player's session would strand their next take or deposit.
                Report("No session on this chest was opened by session-hold, so nothing was released.");
            }
            else
            {
                ChestSessions.Release(storeId);
                Report("Released the session on this chest.");
            }

            Probe();
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

            // Changes the id without reloading, which is how a rebind made on another machine
            // reaches the one holding the chest. Lets that path be tested without a second player.
            if (args.Count > 2 && args[2].ToLowerInvariant() == "zdo-only")
            {
                if (!RequireCheats())
                {
                    return;
                }

                var was = chest.CurrentStoreId;
                Report(chest.SetStoreIdOnly(storeId)
                    ? $"Store id changed from {was} to {storeId} on the ZDO only, as a rebind from another machine arrives."
                    : "Could not change the store id - the chest is not owned by this client.");
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
