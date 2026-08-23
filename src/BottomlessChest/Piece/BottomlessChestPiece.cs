using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace BottomlessChest.Piece
{
    /// <summary>
    /// Registers the buildable chest: a clone of the vanilla black metal chest, tinted
    /// so it is unmistakable on a shelf next to the real thing.
    /// </summary>
    internal static class BottomlessChestPiece
    {
        internal const string PrefabName = "bottomless_chest";
        private const string DisplayNameToken = "$piece_bottomlesschest";
        private const string BasePrefabName = "piece_chest_blackmetal";

        // Deliberately NOT blue-cyan: that is the colour of Valheim's translucent
        // placement ghost, and a cyan-tinted chest reads as an unplaced preview.
        private static readonly Color Tint = new Color(0.82f, 0.76f, 0.88f);
        private static readonly Color Emission = new Color(0.34f, 0.04f, 0.58f);

        // All six renderers share one material (BlackMetalChest_mat, shader Custom/Piece),
        // so parts can only be told apart by renderer name. Glowing just the lid reads as
        // deliberate; glowing everything looks like the chest was dipped in paint.
        private const string LidMarker = "top_";

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += AddPiece;
            var localization = LocalizationManager.Instance.GetLocalization();
            localization.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_bottomlesschest", "Bottomless Chest" },
                { "piece_bottomlesschest_desc", "Holds everything. Type to find it again." },
                { "bottomless_search", "Search..." }
            });
        }

        private static void AddPiece()
        {
            // Vanilla prefabs only exist once, so unsubscribe before doing anything that
            // could throw - otherwise a failure here repeats on every world load.
            PrefabManager.OnVanillaPrefabsAvailable -= AddPiece;

            try
            {
                var config = new PieceConfig
                {
                    Name = DisplayNameToken,
                    Description = "$piece_bottomlesschest_desc",
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Furniture,
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = Settings.ModConfig.ParseRequirements()
                };

                var piece = new CustomPiece(PrefabName, BasePrefabName, config);
                if (piece.PiecePrefab == null)
                {
                    Plugin.Log.LogError($"Could not clone '{BasePrefabName}'. The chest will not be buildable.");
                    return;
                }

                // PieceConfig.Name only covers the build menu. Hover text and the container
                // window read Container.m_name, which the clone inherited from the black
                // metal chest - so without this the chest still calls itself that.
                var container = piece.PiecePrefab.GetComponent<Container>();
                if (container != null)
                {
                    container.m_name = DisplayNameToken;
                }
                else
                {
                    Plugin.Log.LogWarning("Cloned chest has no Container component; its name will be wrong.");
                }

                ApplyTint(piece.PiecePrefab);
                piece.PiecePrefab.AddComponent<Core.BottomlessContainer>();
                PieceManager.Instance.AddPiece(piece);

                Plugin.Log.LogInfo($"Registered piece '{PrefabName}'.");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Failed to register the bottomless chest piece: {ex}");
            }
        }

        /// <summary>
        /// Gives the clone its own look: a faint violet cast with an emissive glow.
        /// </summary>
        /// <remarks>
        /// Every material is copied first. Vanilla prefabs share material instances, so
        /// writing to <c>sharedMaterials</c> in place would restyle every black metal chest
        /// in the world, including ones we do not own.
        /// </remarks>
        private static void ApplyTint(GameObject prefab)
        {
            var described = new List<string>();

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var originals = renderer.sharedMaterials;
                var copies = new Material[originals.Length];

                for (var i = 0; i < originals.Length; i++)
                {
                    if (originals[i] == null)
                    {
                        continue;
                    }

                    var material = new Material(originals[i]);
                    described.Add($"{renderer.name}/{originals[i].name} (shader {originals[i].shader?.name})");

                    if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", material.GetColor("_Color") * Tint);
                    }

                    var isLid = renderer.name.IndexOf(LidMarker, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isLid && material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", Emission);
                    }

                    copies[i] = material;
                }

                renderer.sharedMaterials = copies;
            }

            Plugin.Log.LogDebug($"Chest renderers: {string.Join(", ", described)}");
        }
    }
}
