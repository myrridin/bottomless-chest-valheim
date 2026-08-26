using BottomlessChest.Storage;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Redirects a bottomless chest's persistence away from its ZDO and into the store.
    /// </summary>
    /// <remarks>
    /// These two methods are the whole of Valheim's container persistence.
    /// <c>m_inventory.m_onChanged</c> is wired to <c>OnContainerChanged</c> in
    /// <c>Container.Awake</c>, which calls <c>Save</c>, so intercepting here captures every
    /// write path - drag, StackAll, TakeAll - without reimplementing any change detection.
    /// </remarks>
    internal static class ContainerPersistencePatches
    {
        [HarmonyPatch(typeof(Container), "Save")]
        private static class SavePatch
        {
            private static bool Prefix(Container __instance)
            {
                if (Plugin.Degraded || !BottomlessContainer.TryResolve(__instance, out var bottomless))
                {
                    return true;
                }

                bottomless.EnsureRegistered();
                bottomless.SaveToStore();

                // Skip the original: items must never reach the ZDO.
                return false;
            }
        }

        /// <summary>
        /// Keeps a bottomless chest from being seeded with default contents.
        /// </summary>
        /// <remarks>
        /// Vanilla seeds a container from its drop table the first time its owner sees it.
        /// The cloned chest inherits an empty table so this does nothing today, but the path
        /// is wrong for us either way: on a client it would add items to the page, which is
        /// discarded, while setting the ZDO flag so the server never adds them either. Found
        /// by sweeping every call site that can mutate a container's inventory.
        /// </remarks>
        [HarmonyPatch(typeof(Container), "AddDefaultItems")]
        private static class NoDefaultItems
        {
            private static bool Prefix(Container __instance) =>
                Plugin.Degraded || !BottomlessContainer.TryResolve(__instance, out _);
        }

        [HarmonyPatch(typeof(Container), "Load")]
        private static class LoadPatch
        {
            private static bool Prefix(Container __instance, ref bool __result)
            {
                if (Plugin.Degraded || !BottomlessContainer.TryResolve(__instance, out var bottomless))
                {
                    return true;
                }

                bottomless.EnsureRegistered();
                __result = bottomless.LoadFromStore();
                return false;
            }
        }
    }
}
