using System;

namespace BottomlessChest.Storage
{
    /// <summary>
    /// Thin adapter over Valheim's world-save lifecycle.
    /// </summary>
    /// <remarks>
    /// Deliberately the only place that knows how saving is triggered. Valheim 1.0 reworks
    /// the save system, so when that breaks it should break here and nowhere else.
    ///
    /// Uses the public <c>ZNet.WorldSaveStarted</c> event rather than a Harmony patch -
    /// it is a supported extension point and cannot drift out from under us the way a
    /// patched method signature can.
    /// </remarks>
    internal static class WorldSaveHooks
    {
        private static Action _onSaveStarted;

        internal static void Install(IChestStore store)
        {
            _onSaveStarted = () =>
            {
                try
                {
                    store.Flush();
                }
                catch (Exception ex)
                {
                    // Never let our failure abort Valheim's world save.
                    Plugin.Log.LogError($"Chest store flush failed during world save: {ex}");
                }
            };

            ZNet.WorldSaveStarted += _onSaveStarted;
        }

        internal static void Uninstall()
        {
            if (_onSaveStarted != null)
            {
                ZNet.WorldSaveStarted -= _onSaveStarted;
                _onSaveStarted = null;
            }
        }
    }
}
