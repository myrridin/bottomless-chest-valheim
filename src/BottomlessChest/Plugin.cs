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
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Settings.ModConfig.Bind(Config);
            Piece.BottomlessChestPiece.Register();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
