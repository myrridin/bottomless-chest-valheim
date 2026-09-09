using System.Linq;
using BepInEx.Logging;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Whether anything is listening for Debug lines.
    /// </summary>
    /// <remarks>
    /// Diagnostics in this mod are gated on a real consumer rather than merely written at a
    /// quiet level. A line logged at Debug with nobody reading it still costs whatever it
    /// took to build the string - and the diagnostics worth having are the expensive ones:
    /// a per-second walk of a chest's contents, a dump of a UI hierarchy. Asking this first
    /// means the default install pays one comparison instead.
    ///
    /// The disk listener is the honest place to ask. The console listener only exists when
    /// a console is open, so a dedicated server would report Debug as off no matter what
    /// its config said.
    /// </remarks>
    internal static class DebugLogging
    {
        private static bool? _enabled;

        internal static bool Enabled
        {
            get
            {
                if (_enabled.HasValue)
                {
                    return _enabled.Value;
                }

                try
                {
                    _enabled = Logger.Listeners
                        .OfType<DiskLogListener>()
                        .Any(listener => (listener.DisplayedLogLevel & LogLevel.Debug) != 0);
                }
                catch
                {
                    // Never let a diagnostic decide whether the mod works.
                    _enabled = false;
                }

                return _enabled.Value;
            }
        }
    }
}
