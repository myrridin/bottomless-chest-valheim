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
        /// The one switch for everything this mod does on ValheimPlus's behalf.
        /// </summary>
        /// <remarks>
        /// True only when ValheimPlus is present and all three hooks attached. Everything that
        /// exists for ValheimPlus's sake checks this and does nothing otherwise, so without
        /// ValheimPlus a client behaves exactly as 0.2.1 did:
        /// <list type="bullet">
        /// <item>the hooks themselves, which are only ever attached here;</item>
        /// <item>asking the server for chest summaries (<c>BottomlessContainer.RefreshIndex</c>),
        /// which would otherwise keep every nearby chest loaded on the server;</item>
        /// <item>forwarding items another mod adds to a chest (<c>ClientDepositGuard</c>), which
        /// otherwise falls back to refusing them.</item>
        /// </list>
        /// Per machine. A server answers summary, removal and deposit messages whether or not
        /// it runs ValheimPlus itself, because the clients asking might; with no client asking,
        /// those handlers never run.
        /// </remarks>
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

            var hooks = new[]
            {
                new Hook(listItems, null, nameof(ChestQueryPatches.ListItemsPostfix), "chest item list"),
                new Hook(removeByItem, nameof(ChestQueryPatches.RemoveByItemPrefix), null, "remove-by-item"),
                new Hook(removeByName, nameof(ChestQueryPatches.RemoveByNamePrefix), null, "remove-by-name"),
            };

            var attached = new List<Hook>();
            foreach (var hook in hooks)
            {
                if (TryPatch(harmony, hook))
                {
                    attached.Add(hook);
                }
            }

            // All or nothing. The read hook without the removal hooks is the dangerous half:
            // ValheimPlus would count a chest's materials, then run its own removal against the
            // page this client holds - taking nothing from the server and calling the craft paid.
            if (attached.Count < hooks.Length)
            {
                foreach (var hook in attached)
                {
                    Detach(harmony, hook);
                }

                Plugin.Log.LogWarning(
                    $"ValheimPlus chest integration disabled: only {attached.Count} of {hooks.Length} " +
                    "hooks could attach, and a partial set is worse than none. Bottomless chests " +
                    "behave as they did before the integration existed.");
                return;
            }

            Attached = true;
            Plugin.Log.LogInfo($"ValheimPlus chest integration: all {hooks.Length} hooks attached.");
        }

        private sealed class Hook
        {
            internal Hook(MethodInfo target, string prefix, string postfix, string what)
            {
                Target = target;
                Prefix = prefix == null ? null : AccessTools.Method(typeof(ChestQueryPatches), prefix);
                Postfix = postfix == null ? null : AccessTools.Method(typeof(ChestQueryPatches), postfix);
                What = what;
            }

            internal MethodInfo Target { get; }

            internal MethodInfo Prefix { get; }

            internal MethodInfo Postfix { get; }

            internal string What { get; }
        }

        private static bool TryPatch(Harmony harmony, Hook hook)
        {
            if (hook.Target == null)
            {
                Plugin.Log.LogWarning($"ValheimPlus's {hook.What} method has changed shape.");
                return false;
            }

            try
            {
                harmony.Patch(
                    hook.Target,
                    prefix: hook.Prefix == null ? null : new HarmonyMethod(hook.Prefix),
                    postfix: hook.Postfix == null ? null : new HarmonyMethod(hook.Postfix));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not attach to ValheimPlus's {hook.What}: {ex.Message}");
                return false;
            }
        }

        private static void Detach(Harmony harmony, Hook hook)
        {
            try
            {
                if (hook.Prefix != null)
                {
                    harmony.Unpatch(hook.Target, hook.Prefix);
                }

                if (hook.Postfix != null)
                {
                    harmony.Unpatch(hook.Target, hook.Postfix);
                }
            }
            catch (Exception ex)
            {
                // Still attached, but every hook body checks Attached, which stays false.
                Plugin.Log.LogWarning($"Could not detach from ValheimPlus's {hook.What}: {ex.Message}");
            }
        }
    }
}
