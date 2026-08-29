using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Reports which other mods patch the methods this one depends on.
    /// </summary>
    /// <remarks>
    /// Reading another mod's source tells you what it patches, not what order the
    /// patches end up in, and ordering is exactly what breaks when two mods both
    /// prefix InventoryGrid.UpdateGui. Logging the resolved list once is the only
    /// way to answer that from a user's machine.
    /// </remarks>
    internal static class PatchAudit
    {
        private static bool _reported;

        private static readonly (System.Type Type, string Method)[] Watched =
        {
            (typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui)),
            (typeof(InventoryGui), nameof(InventoryGui.UpdateContainer)),
            (typeof(Inventory), nameof(Inventory.Changed)),
        };

        /// <summary>Logs co-patchers the first time a chest is opened.</summary>
        /// <remarks>
        /// Deferred to first open rather than run at startup because mods that load
        /// after this one would not appear yet.
        /// </remarks>
        internal static void ReportOnce()
        {
            if (_reported)
            {
                return;
            }

            _reported = true;

            foreach (var (type, method) in Watched)
            {
                try
                {
                    var target = AccessTools.Method(type, method);
                    if (target == null)
                    {
                        continue;
                    }

                    var info = Harmony.GetPatchInfo(target);
                    if (info == null)
                    {
                        continue;
                    }

                    var others = Describe("prefix", info.Prefixes)
                        .Concat(Describe("postfix", info.Postfixes))
                        .Concat(Describe("transpiler", info.Transpilers))
                        .Where(entry => !entry.Contains(Plugin.PluginGuid))
                        .ToList();

                    if (others.Count > 0)
                    {
                        Plugin.Log.LogInfo(
                            $"{type.Name}.{method} is also patched by: {string.Join(", ", others)}");
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogDebug($"Could not inspect patches on {type.Name}.{method}: {ex.Message}");
                }
            }
        }

        private static IEnumerable<string> Describe(string kind, IEnumerable<Patch> patches)
        {
            // Ordered by the priority Harmony actually resolved, so the log shows the
            // running order rather than registration order.
            return patches
                .OrderBy(patch => patch.index)
                .Select(patch => $"{patch.owner} ({kind}, priority {patch.priority})");
        }
    }
}
