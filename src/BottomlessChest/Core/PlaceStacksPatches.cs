using System.Collections.Generic;
using System.Diagnostics;
using BottomlessChest.Logic;
using BottomlessChest.Storage;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Keeps Place Stacks, and every other add into a bottomless chest, linear in the chest's size.
    /// </summary>
    /// <remarks>
    /// Vanilla adds with three costs that do not matter in a 32-slot chest and dominate in one
    /// holding ten thousand stacks:
    /// <list type="bullet">
    /// <item><c>AddItem</c> looks for a stack with room once per <em>unit</em>, and each look
    /// walks the chest - a stack of 500 is 500 walks.</item>
    /// <item><c>FindEmptySlot</c> asks <c>GetItemAt</c>, itself a walk, of every slot in turn.
    /// A chest filled top first has every slot but the spare row taken, so that is every item
    /// times every item.</item>
    /// <item>Every <c>AddItem</c> ends in <c>Changed</c>, which saves: a full serialize of the
    /// chest for each stack moved.</item>
    /// </list>
    /// Place Stacks into a 9,971-stack chest was "super slow". These patches give the answers
    /// vanilla gives, in the same order, without the repeated walks, and save once per Place
    /// Stacks. ValheimPlus's stations and auto-stack add through the same methods, so they
    /// speed up too, and its item filter - a transpiler on <c>StackAll</c> - still applies.
    ///
    /// Only on the authority, which holds the whole chest. A client holds a page of forty
    /// items, and its adds belong to <see cref="ClientDepositGuard"/>.
    /// </remarks>
    internal static class PlaceStacksPatches
    {
        /// <summary>Turned off only by the testing command, to measure against vanilla.</summary>
        internal static bool Enabled = true;

        /// <summary>Report each Place Stacks' time at Info. Set by the testing command.</summary>
        internal static bool Measuring;

        private sealed class Run
        {
            internal Inventory Chest;
            internal bool Batched;
            internal Stopwatch Timer;
            internal Container PendingSave;
        }

        private static readonly Stack<Run> Runs = new Stack<Run>();

        private static bool IsChestOnAuthority(Inventory inventory) =>
            InventoryCapacity.IsUnbounded(inventory) && !Plugin.Degraded && SidecarStore.IsServerAuthority;

        /// <summary>
        /// Holds back a chest's save while Place Stacks is still moving items into it.
        /// </summary>
        /// <returns>True if the save was deferred; the caller must then skip it.</returns>
        /// <remarks>
        /// Narrow on purpose: only the chest being stacked into, and only while its run is open.
        /// The run's finalizer performs the one save, even if something threw part way, so a
        /// deferred save cannot be lost.
        /// </remarks>
        internal static bool DeferSave(Container container)
        {
            if (Runs.Count == 0)
            {
                return false;
            }

            var run = Runs.Peek();
            if (!run.Batched || !ReferenceEquals(run.Chest, container.m_inventory))
            {
                return false;
            }

            run.PendingSave = container;
            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
        private static class SaveOncePerPlaceStacks
        {
            private static void Prefix(Inventory __instance)
            {
                if (!IsChestOnAuthority(__instance))
                {
                    return;
                }

                Runs.Push(new Run
                {
                    Chest = __instance,
                    Batched = Enabled,
                    Timer = Measuring || DebugLogging.Enabled ? Stopwatch.StartNew() : null,
                });
            }

            // A finalizer runs whether or not the original, or another mod's patch, threw.
            private static void Finalizer(Inventory __instance)
            {
                if (Runs.Count == 0 || !ReferenceEquals(Runs.Peek().Chest, __instance))
                {
                    return;
                }

                var run = Runs.Pop();
                if (run.PendingSave != null)
                {
                    try
                    {
                        // Back through Container.Save, so the store sees it the way it sees any
                        // other change - including a session sharing this inventory.
                        run.PendingSave.Save();
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"Could not save a chest after Place Stacks: {ex}");
                    }
                }

                if (run.Timer != null)
                {
                    var line =
                        $"Place Stacks into a chest of {__instance.m_inventory.Count} stacks took " +
                        $"{run.Timer.Elapsed.TotalMilliseconds:0} ms ({(run.Batched ? "fast" : "vanilla")} path).";
                    if (Measuring)
                    {
                        Plugin.Log.LogInfo(line);
                        Console.instance?.Print(line);
                    }
                    else
                    {
                        Plugin.Log.LogDebug(line);
                    }
                }
            }
        }

        /// <summary>
        /// Vanilla's slot search, marking occupied slots once instead of walking the chest per slot.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
        private static class FindEmptySlotPatch
        {
            private static bool Prefix(Inventory __instance, bool topFirst, ref Vector2i __result)
            {
                if (!Enabled || __instance.m_width <= 0 || !IsChestOnAuthority(__instance))
                {
                    return true;
                }

                var slot = GridPacker.FirstEmptySlot(
                    Positions(__instance), __instance.m_width, __instance.m_height, topFirst);
                __result = new Vector2i(slot.X, slot.Y);
                return false;
            }

            private static IEnumerable<GridPos> Positions(Inventory inventory)
            {
                foreach (var item in inventory.m_inventory)
                {
                    yield return new GridPos(item.m_gridPos.x, item.m_gridPos.y);
                }
            }
        }

        /// <summary>
        /// Vanilla <c>AddItem(item)</c>, merging a stack at a time instead of a unit at a time.
        /// </summary>
        /// <remarks>
        /// Vanilla tops up the first stack with room by one, looks again, and repeats; this
        /// moves as much as that stack has room for in one step. The stacks filled, the order
        /// they fill in, the remainder placed and the cheat flags all come out the same. The
        /// argument types are explicit because <c>AddItem</c> has four overloads.
        /// </remarks>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
        private static class AddItemPatch
        {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (!Enabled || item == null || !IsChestOnAuthority(__instance))
                {
                    return true;
                }

                __result = Add(__instance, item);
                return false;
            }

            private static bool Add(Inventory inventory, ItemDrop.ItemData item)
            {
                var added = true;
                var cheatedStateChanged = item.m_cheated && !Achievements.IsCheatedAtAll();

                if (item.m_shared.m_maxStackSize > 1)
                {
                    var remaining = item.m_stack;
                    while (remaining > 0)
                    {
                        var open = inventory.FindFreeStackItem(item.m_shared.m_name, item.m_quality, item.m_worldLevel);
                        var moved = open == null || ReferenceEquals(open, item)
                            ? 0
                            : StackRules.MergeAmount(remaining, open.m_stack, open.m_shared.m_maxStackSize);

                        if (moved > 0)
                        {
                            open.m_stack += moved;
                            remaining -= moved;
                            if (item.m_cheated && !PlayerProfile.s_bypassCheatChecks)
                            {
                                open.m_cheated = true;
                            }

                            continue;
                        }

                        item.m_stack = remaining;
                        added = Place(inventory, item);
                        break;
                    }
                }
                else
                {
                    added = Place(inventory, item);
                }

                inventory.Changed(added, cheatedStateChanged);
                return added;
            }

            private static bool Place(Inventory inventory, ItemDrop.ItemData item)
            {
                var slot = inventory.FindEmptySlot(inventory.TopFirst(item));
                if (slot.x < 0)
                {
                    ZLog.LogError($"Trying to add item to occupied slot {slot.x}, {slot.y}");
                    return false;
                }

                item.m_gridPos = slot;
                inventory.m_inventory.Add(item);
                return true;
            }
        }
    }
}
