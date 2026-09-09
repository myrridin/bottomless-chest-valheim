using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;

namespace BottomlessChest
{
    /// <summary>
    /// BepInEx entry point.
    /// </summary>
    /// <remarks>
    /// The network compatibility attribute is load-bearing, not decoration. A client
    /// without this mod would fail to resolve the bottomless_chest prefab and Valheim
    /// would destroy those ZDOs along with everything stored in them. Requiring the mod
    /// on both ends turns that silent data loss into a clean join rejection.
    /// </remarks>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.myrridin.bottomlesschest";
        public const string PluginName = "BottomlessChest";
        // Jotunn's NetworkCompatibility check compares THIS string, not the manifest or
        // assembly version, and VersionStrictness.Minor means 0.1 and 0.2 are incompatible.
        // Leaving it behind lets a 0.1.0 client join a 0.2.0 server, which then sends it a
        // store format it will read as garbage. Bump it with the manifest, always.
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        /// <summary>True when patching failed and chests fall back to vanilla behaviour.</summary>
        internal static bool Degraded { get; private set; }

        /// <summary>
        /// Starts the mod, in an order chosen so that a failure cannot destroy save data.
        /// </summary>
        /// <remarks>
        /// Registering the prefab comes first and is insulated from everything after it.
        /// If bottomless_chest is not registered, Valheim cannot resolve the prefab for
        /// existing chest ZDOs and deletes them - so a mistake anywhere else in startup
        /// would take the chests with it. That is not hypothetical: an ambiguous Harmony
        /// patch target threw here once and did exactly that.
        ///
        /// If patching fails we unpatch entirely and run degraded. Chests then behave as
        /// ordinary vanilla containers, which is wrong but harmless, and the sidecar file
        /// keeps their real contents safe on disk until the mod works again.
        /// </remarks>
        private void Awake()
        {
            Log = Logger;

            try
            {
                Settings.ModConfig.Bind(Config);
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Could not read configuration, falling back to defaults: {ex}");
            }

            try
            {
                Piece.BottomlessChestPiece.Register();
            }
            catch (System.Exception ex)
            {
                Log.LogError($"CRITICAL: the chest prefab could not be registered. " +
                             $"Existing chests will be removed by the game. {ex}");
            }

            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (System.Exception ex)
            {
                Degraded = true;
                Log.LogError($"Patching failed - running in degraded mode. Chests will behave " +
                             $"as ordinary containers; their stored contents are untouched. {ex}");

                try
                {
                    _harmony?.UnpatchSelf();
                }
                catch (System.Exception unpatchEx)
                {
                    Log.LogError($"Could not roll back partial patches: {unpatchEx}");
                }
            }

            try
            {
                Net.ChestRpc.Register();
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Could not register networking; multiplayer chests will not sync: {ex}");
            }

            try
            {
                Jotunn.Managers.CommandManager.Instance.AddConsoleCommand(new Commands.BottomlessCommand());
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Could not register console commands: {ex}");
            }

            try
            {
                Storage.WorldSaveHooks.Install(Storage.SidecarStore.Instance);
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Could not hook world saving: {ex}");
            }

            Log.LogInfo($"{PluginName} {PluginVersion} loaded{(Degraded ? " (DEGRADED)" : string.Empty)}.");
        }

        private void OnDestroy()
        {
            Filter.ItemAdapter.ClearCache();
            Core.ChestSessions.PersistAll();
            Core.ChestSessions.Clear();
            Storage.WorldSaveHooks.Uninstall();
            Storage.SidecarStore.Instance.Flush();
            Storage.SidecarStore.Instance.Unload();
            _harmony?.UnpatchSelf();
        }
    }
}
