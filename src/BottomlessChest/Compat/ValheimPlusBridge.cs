using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// Attaches our ValheimPlus patches, if ValheimPlus is here at all.
    /// </summary>
    /// <remarks>
    /// Bound by reflection rather than an assembly reference: a hard reference would make
    /// ValheimPlus a requirement for everyone, and this is a compatibility fix, not a
    /// dependency.
    ///
    /// Attached one method at a time with <c>Harmony.Patch</c>, never through attributes.
    /// The plugin runs <c>PatchAll</c> over this whole assembly, and an attributed patch whose
    /// target cannot be found throws there - which would put the entire mod into degraded
    /// mode for anyone without ValheimPlus. Every failure here is survivable instead: a method
    /// ValheimPlus refactors away returns that one path to its previous behaviour, a
    /// bottomless chest ValheimPlus cannot see into.
    /// </remarks>
    internal static class ValheimPlusBridge
    {
        /// <summary>ValheimPlus's plugin GUID, for the soft dependency that makes it load first.</summary>
        internal const string Guid = "org.bepinex.plugins.valheim_plus";

        private const string AssemblyName = "ValheimPlus";

        // 10.1.2 moved the class out of GameClasses. The old name is kept as a fallback.
        private static readonly string[] AssistantTypeNames =
        {
            "ValheimPlus.InventoryAssistant",
            "ValheimPlus.GameClasses.InventoryAssistant",
        };

        private const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>
        /// Whether any hook attached. Nothing else reads a chest's index, so without this a
        /// client has no reason to ask for one - and asking keeps the chest loaded on the server.
        /// </summary>
        internal static bool Attached { get; private set; }

        internal static void Attach(Harmony harmony)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == AssemblyName);

            if (assembly == null)
            {
                Plugin.Log.LogInfo("ValheimPlus not present; its chest integration is not needed.");
                return;
            }

            var assistant = AssistantTypeNames
                .Select(name => assembly.GetType(name, throwOnError: false))
                .FirstOrDefault(type => type != null);

            if (assistant == null)
            {
                Plugin.Log.LogWarning(
                    "ValheimPlus is present but its InventoryAssistant was not found. It will not " +
                    "be able to use bottomless chests on a dedicated server.");
                return;
            }

            var listItems = assistant.GetMethod(
                "GetNearbyChestItemsByContainerList", Statics, null, new[] { typeof(List<Container>) }, null);

            var removeByItem = assistant.GetMethod(
                "RemoveItemFromChest", Statics, null, new[] { typeof(Container), typeof(ItemDrop.ItemData), typeof(int) }, null);

            var removeByName = assistant.GetMethod(
                "RemoveItemFromChest", Statics, null, new[] { typeof(Container), typeof(string), typeof(int) }, null);

            var attached = 0;
            if (TryPatch(harmony, listItems, null, nameof(ChestQueryPatches.ListItemsPostfix), "chest item list"))
            {
                attached++;
            }

            if (TryPatch(harmony, removeByItem, nameof(ChestQueryPatches.RemoveByItemPrefix), null, "remove-by-item"))
            {
                attached++;
            }

            if (TryPatch(harmony, removeByName, nameof(ChestQueryPatches.RemoveByNamePrefix), null, "remove-by-name"))
            {
                attached++;
            }

            Attached = attached > 0;
            Plugin.Log.LogInfo($"ValheimPlus chest integration: {attached} of 3 hook(s) attached.");
        }

        private static bool TryPatch(Harmony harmony, MethodInfo target, string prefix, string postfix, string what)
        {
            if (target == null)
            {
                Plugin.Log.LogWarning(
                    $"ValheimPlus's {what} method has changed shape; leaving that path as it was.");
                return false;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(ChestQueryPatches), prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(ChestQueryPatches), postfix)));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not attach to ValheimPlus's {what}: {ex.Message}");
                return false;
            }
        }
    }
}
