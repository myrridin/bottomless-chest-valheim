using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using BottomlessChest.Logic;
using UnityEngine;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Notices when a chest's contents change without our code having run.
    /// </summary>
    /// <remarks>
    /// A bottomless chest is a plain <c>Container</c> holding a real <c>Inventory</c>, so
    /// anything in the process can reach into it. ValheimPlus does: CraftFromChest patches
    /// <c>Player.ConsumeResources</c>, so every build placement pulls the shortfall out of
    /// nearby chests, and its removal helper mutates stacks in place, reassigns
    /// <c>m_inventory.m_inventory</c>, and calls neither <c>Changed()</c> nor its own logger.
    /// A chest can therefore empty with no trace in any log - which is exactly how 253 wood
    /// left one and left nothing to read afterwards.
    ///
    /// Polling is the only way to see it: hooking <c>Inventory.Changed</c> would miss
    /// precisely the writes worth catching, because not firing it is what makes them silent.
    ///
    /// Whether the chest was open is the useful half of each report. Our code only touches
    /// contents through the open window, so a delta recorded while the chest is closed had
    /// no author on our side.
    /// </remarks>
    internal sealed class ContentsWatch
    {
        /// <summary>Matches ValheimPlus's own one-second tick, so no pull hides between polls.</summary>
        private const float IntervalSeconds = 1f;

        /// <summary>
        /// Above this, the per-second walk stops being free and the chest stops being watched.
        /// A hundred-thousand-stack chest is a stress test, not a session worth diagnosing.
        /// </summary>
        private const int MaxWatchedStacks = 5000;

        private static bool? _debugLogging;

        private Dictionary<string, int> _last;
        private float _nextPollAt;
        private bool _saidItWasTooBig;

        /// <summary>
        /// True only when something will actually print a Debug line.
        /// </summary>
        /// <remarks>
        /// The default install logs at Info and above, so this is false and the whole watch
        /// costs one comparison per frame. Turning Debug on in BepInEx.cfg is what arms it.
        /// </remarks>
        internal static bool Enabled
        {
            get
            {
                if (_debugLogging.HasValue)
                {
                    return _debugLogging.Value;
                }

                try
                {
                    _debugLogging = BepInEx.Logging.Logger.Listeners
                        .OfType<DiskLogListener>()
                        .Any(listener => (listener.DisplayedLogLevel & LogLevel.Debug) != 0);
                }
                catch
                {
                    // Never let a diagnostic decide whether the mod works.
                    _debugLogging = false;
                }

                return _debugLogging.Value;
            }
        }

        /// <summary>
        /// Re-baselines without reporting, for when contents are replaced wholesale.
        /// </summary>
        /// <remarks>
        /// Loading a chest, or adopting a page from the server, changes every quantity at
        /// once. That is not a leak, and reporting it would drown the deltas that are.
        /// </remarks>
        internal void Rebaseline()
        {
            _last = null;
        }

        internal void Poll(Inventory inventory, string label, bool chestIsOpen)
        {
            if (!Enabled || inventory == null || Time.unscaledTime < _nextPollAt)
            {
                return;
            }

            _nextPollAt = Time.unscaledTime + IntervalSeconds;

            var items = inventory.m_inventory;
            if (items == null)
            {
                return;
            }

            if (items.Count > MaxWatchedStacks)
            {
                if (!_saidItWasTooBig)
                {
                    _saidItWasTooBig = true;
                    Plugin.Log.LogDebug(
                        $"Not watching contents of {label}: {items.Count} stacks exceeds the {MaxWatchedStacks} watch limit.");
                }

                _last = null;
                return;
            }

            _saidItWasTooBig = false;

            var current = new Dictionary<string, int>(items.Count);
            foreach (var item in items)
            {
                if (item?.m_dropPrefab != null)
                {
                    ContentsDiff.Add(current, item.m_dropPrefab.name, item.m_stack);
                }
                else if (item?.m_shared != null)
                {
                    ContentsDiff.Add(current, item.m_shared.m_name, item.m_stack);
                }
            }

            var changed = ContentsDiff.Describe(_last, current);
            _last = current;

            if (changed != null)
            {
                Plugin.Log.LogDebug(
                    $"Contents of {label} changed while {(chestIsOpen ? "open" : "CLOSED - not by us")}: {changed}");
            }
        }
    }
}
