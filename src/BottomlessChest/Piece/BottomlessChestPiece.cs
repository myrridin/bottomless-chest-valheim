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
        // The personal chest: the smallest vanilla chest, chosen so a bottomless chest
        // takes as little building space as possible.
        private const string BasePrefabName = "piece_chest_private";

        // Fallbacks if the configured hex is unparseable. Deliberately NOT blue-cyan:
        // that is the colour of Valheim's placement ghost, and a cyan chest reads as an
        // unplaced preview rather than a real object.
        private static readonly Color FallbackTint = new Color(0.29f, 0.25f, 0.18f);
        private static readonly Color FallbackGlow = new Color(0.78f, 0.85f, 0.24f);

        private static Color ReadColour(string hex, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out var parsed))
            {
                return parsed;
            }

            Plugin.Log.LogWarning($"Could not read colour '{hex}'; using the default instead.");
            return fallback;
        }

        // All six renderers share one material (BlackMetalChest_mat, shader Custom/Piece),
        // so parts can only be told apart by renderer name. Glowing just the lid reads as
        // deliberate; glowing everything looks like the chest was dipped in paint.
        private static readonly string[] LidMarkers = { "top", "lid", "open", "closed" };

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

                    // The personal chest is Private, which restricts CheckAccess to the
                    // creator. Inheriting that would make the chest unopenable by anyone
                    // else on a server, so ownership is reset to Public regardless of base.
                    if (container.m_privacy != Container.PrivacySetting.Public)
                    {
                        Plugin.Log.LogInfo(
                            $"Base prefab '{BasePrefabName}' is {container.m_privacy}; forcing Public so " +
                            "other players can use the chest.");
                        container.m_privacy = Container.PrivacySetting.Public;
                    }
                }
                else
                {
                    Plugin.Log.LogWarning("Cloned chest has no Container component; its name will be wrong.");
                }

                ApplyScale(piece.PiecePrefab);
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
        /// Resizes the whole prefab uniformly.
        /// </summary>
        /// <remarks>
        /// Scaling the root scales its colliders too, so placement and the hover raycast
        /// follow the visible model rather than drifting out of alignment.
        /// </remarks>
        private static void ApplyScale(GameObject prefab)
        {
            var scale = Settings.ModConfig.ModelScale.Value;
            if (Mathf.Approximately(scale, 1f))
            {
                return;
            }

            // Multiply, never assign. Assigning Vector3.one * scale throws away whatever
            // scale the vanilla prefab shipped with, which silently changes its proportions
            // on top of the resize the player asked for.
            var original = prefab.transform.localScale;
            prefab.transform.localScale = original * scale;

            Plugin.Log.LogInfo(
                $"Chest model scaled by {scale:0.##}x: {original} -> {prefab.transform.localScale}.");
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
            var tint = ReadColour(Settings.ModConfig.BodyTint.Value, FallbackTint);
            var glow = ReadColour(Settings.ModConfig.GlowColour.Value, FallbackGlow)
                       * Settings.ModConfig.GlowStrength.Value;

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
                        material.SetColor("_Color", material.GetColor("_Color") * tint);
                    }

                    var isLid = System.Array.Exists(
                        LidMarkers,
                        marker => renderer.name.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isLid && material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", glow);
                    }

                    copies[i] = material;
                }

                renderer.sharedMaterials = copies;
            }

            Plugin.Log.LogInfo($"Chest renderers: {string.Join(", ", described)}");
        }
    }
}
