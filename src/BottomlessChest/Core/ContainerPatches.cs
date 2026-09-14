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

        /// <summary>
        /// Records the chest as loaded the way vanilla's own Load would have.
        /// </summary>
        /// <remarks>
        /// Vanilla sets <c>m_lastRevision</c> inside <c>Container.Load</c>, which this patch
        /// replaces, so a bottomless chest kept <c>uint.MaxValue</c> forever. ValheimPlus 10.1.2
        /// reads exactly that value as "not loaded yet" and leaves the chest out of every search -
        /// no crafting from it, no station pulling from it or depositing into it, anywhere.
        ///
        /// On the server authority only once the contents are really in memory: that is what the
        /// signal means to ValheimPlus, which waits on it before depositing, and an early deposit
        /// into an unloaded chest is refused a save. A client never holds the contents, and
        /// answers for the chest from its index instead, so it is ready as soon as it exists.
        /// </remarks>
        private static void MarkLoaded(Container container, BottomlessContainer bottomless)
        {
            var view = container.m_nview;
            if (view == null || !view.IsValid())
            {
                return;
            }

            if (SidecarStore.IsServerAuthority && bottomless.AwaitingContents)
            {
                return;
            }

            container.m_lastRevision = view.GetZDO().DataRevision;
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
                MarkLoaded(__instance, bottomless);
                return false;
            }
        }
    }
}
