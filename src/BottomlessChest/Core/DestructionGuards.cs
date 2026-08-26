using BottomlessChest.Filter;
using BottomlessChest.Storage;
using HarmonyLib;
using UnityEngine;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Stops a bottomless chest emptying itself onto the ground.
    /// </summary>
    /// <remarks>
    /// Vanilla drops container contents as loose items on destruction, spawning an ItemDrop
    /// GameObject - and a ZDO - per stack. For an ordinary chest that is a handful. For one
    /// of these it could be a hundred thousand, spawned in a single frame, and every one of
    /// them written into the world database permanently. That is not a lag spike, it is an
    /// unrecoverable world.
    ///
    /// So contents are never dropped. The store entry survives destruction and is listed by
    /// 'bottomless list' as orphaned, to be reattached to a new chest with 'bottomless
    /// rebind'. Removal by hammer is refused outright while a chest still holds anything.
    /// </remarks>
    internal static class DestructionGuards
    {
        /// <summary>Stacks held, or null when this side cannot know.</summary>
        private static int? StackCount(BottomlessContainer bottomless)
        {
            var storeId = bottomless.CurrentStoreId;
            if (string.IsNullOrEmpty(storeId))
            {
                return 0;
            }

            if (SidecarStore.IsServerAuthority)
            {
                if (ChestSessions.TryGet(storeId, out var session))
                {
                    return session.TotalCount;
                }

                return bottomless.Inventory?.m_inventory.Count ?? 0;
            }

            return ChestView.KnownTotalFor(storeId);
        }

        [HarmonyPatch(typeof(Container), nameof(Container.CanBeRemoved))]
        private static class RemovalGuard
        {
            private static bool Prefix(Container __instance, ref bool __result)
            {
                if (Plugin.Degraded || !BottomlessContainer.TryResolve(__instance, out var bottomless))
                {
                    return true;
                }

                var count = StackCount(bottomless);

                // Unknown means the chest has not been opened this session. Refusing is the
                // safe answer: the cost of being wrong is someone's entire storage.
                __result = count.HasValue && count.Value == 0;
                return false;
            }
        }

        private static bool SuppressDrop(Container container)
        {
            if (Plugin.Degraded || !BottomlessContainer.TryResolve(container, out var bottomless))
            {
                return true;
            }

            Plugin.Log.LogWarning(
                $"A bottomless chest was destroyed. Its contents were NOT dropped; store " +
                $"{bottomless.CurrentStoreId} is intact and can be reattached with " +
                "'bottomless rebind'.");

            return false;
        }

        [HarmonyPatch(typeof(Container), nameof(Container.DropAllItems), new System.Type[0])]
        private static class DropGuard
        {
            private static bool Prefix(Container __instance) => SuppressDrop(__instance);
        }

        /// <summary>
        /// The overload used when a container has a wreckage prefab.
        /// </summary>
        /// <remarks>
        /// Easy to miss: it spills the contents into spawned loot containers instead of
        /// loose items, but it is the same unbounded spawn.
        /// </remarks>
        [HarmonyPatch(typeof(Container), nameof(Container.DropAllItems), new[] { typeof(GameObject) })]
        private static class DropIntoContainerGuard
        {
            private static bool Prefix(Container __instance) => SuppressDrop(__instance);
        }
    }
}
