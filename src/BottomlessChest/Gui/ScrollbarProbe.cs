using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Reports the real shape of the container scrollbar's UI hierarchy, once.
    /// </summary>
    /// <remarks>
    /// A previous attempt to drive the scrollbar failed because it assumed the owning
    /// ScrollRect was among the scrollbar's ancestors; a scrollbar is usually a sibling of
    /// its ScrollRect instead, so it was never detached and the two fought every frame.
    /// Rather than guess a second time, this prints what is actually there.
    /// </remarks>
    internal static class ScrollbarProbe
    {
        private static bool _reported;

        internal static void Reset() => _reported = false;

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class Patch
        {
            private static void Postfix(InventoryGui __instance, Container container)
            {
                if (_reported || container == null || __instance.m_containerGrid == null)
                {
                    return;
                }

                if (!Core.BottomlessContainer.TryResolve(container, out _))
                {
                    return;
                }

                _reported = true;

                var report = new StringBuilder();
                var bar = __instance.m_containerGrid.m_scrollbar;

                report.AppendLine("--- container scrollbar probe ---");
                report.AppendLine($"scrollbar: {(bar == null ? "NULL" : bar.name)}");

                if (bar != null)
                {
                    report.AppendLine($"  direction: {bar.direction}, size: {bar.size:0.###}, value: {bar.value:0.###}");
                    report.AppendLine($"  ancestors: {Chain(bar.transform)}");

                    var parent = bar.transform.parent;
                    if (parent != null)
                    {
                        report.AppendLine("  siblings:");
                        for (var i = 0; i < parent.childCount; i++)
                        {
                            var sib = parent.GetChild(i);
                            report.AppendLine($"    {sib.name} [{Components(sib)}]");
                        }
                    }
                }

                report.AppendLine($"  gridRoot ancestors: {Chain(__instance.m_containerGrid.m_gridRoot)}");

                // Which ScrollRect, anywhere under the inventory window, claims this bar.
                foreach (var rect in __instance.GetComponentsInChildren<ScrollRect>(true))
                {
                    var owns = bar != null && rect.verticalScrollbar == bar;
                    report.AppendLine(
                        $"  ScrollRect '{rect.name}' ownsThisBar={owns} " +
                        $"content='{(rect.content == null ? "null" : rect.content.name)}' " +
                        $"viewport='{(rect.viewport == null ? "null" : rect.viewport.name)}'");
                }

                Plugin.Log.LogInfo(report.ToString());
            }
        }

        private static string Chain(Transform t)
        {
            if (t == null)
            {
                return "null";
            }

            var parts = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
            {
                parts.Append(" < ").Append(p.name);
            }

            return parts.ToString();
        }

        private static string Components(Transform t)
        {
            var names = new StringBuilder();
            foreach (var c in t.GetComponents<Component>())
            {
                if (names.Length > 0)
                {
                    names.Append(", ");
                }

                names.Append(c.GetType().Name);
            }

            return names.ToString();
        }
    }
}
