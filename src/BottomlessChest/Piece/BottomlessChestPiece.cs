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
        private const string BasePrefabName = "piece_chest_blackmetal";

        // Deliberately NOT blue-cyan: that is the colour of Valheim's translucent
        // placement ghost, and a cyan-tinted chest reads as an unplaced preview.
        private static readonly Color Tint = new Color(0.78f, 0.72f, 0.82f);
        private static readonly Color Emission = new Color(0.34f, 0.04f, 0.58f);

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += AddPiece;
            var localization = LocalizationManager.Instance.GetLocalization();
            localization.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_bottomlesschest", "Bottomless Chest" },
                { "piece_bottomlesschest_desc", "Holds everything. Type to find it again." }
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
                    Name = "$piece_bottomlesschest",
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

                ApplyTint(piece.PiecePrefab);
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

                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", Emission);
                    }

                    copies[i] = material;
                }

                renderer.sharedMaterials = copies;
            }

            // Logged once at registration so the parts can be targeted individually later
            // rather than restyling the whole prefab uniformly.
            Plugin.Log.LogInfo($"Chest renderers: {string.Join(", ", described)}");
        }
    }
}
