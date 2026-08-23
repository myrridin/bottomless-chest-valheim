using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Configs;

namespace BottomlessChest.Settings
{
    /// <summary>
    /// All BepInEx config bindings, read once at Awake.
    /// </summary>
    internal static class ModConfig
    {
        internal static ConfigEntry<int> GridWidth;
        internal static ConfigEntry<int> VisibleRows;
        internal static ConfigEntry<int> MinRows;
        internal static ConfigEntry<int> MaxItemEntries;
        internal static ConfigEntry<string> RecipeRequirements;
        internal static ConfigEntry<float> ModelScale;
        internal static ConfigEntry<string> BodyTint;
        internal static ConfigEntry<string> GlowColour;
        internal static ConfigEntry<float> GlowStrength;

        internal static void Bind(ConfigFile cfg)
        {
            GridWidth = cfg.Bind("Storage", "GridWidth", 8,
                "How many columns the chest window shows. Purely visual; capacity is unbounded either way.");

            VisibleRows = cfg.Bind("Storage", "VisibleRows", 6,
                "How many rows of the chest are shown at once. The grid stays this size no " +
                "matter how much the chest holds; the mouse wheel scrolls through the rest.");

            MinRows = cfg.Bind("Storage", "MinRows", 4,
                "Rows the chest shows even when empty, so a new chest does not look like a single slot.");

            MaxItemEntries = cfg.Bind("Storage", "MaxItemEntries", 0,
                "Safety valve: refuse to store more than this many distinct entries in one chest. 0 means no limit.");

            ModelScale = cfg.Bind("Appearance", "ModelScale", 1.0f,
                new ConfigDescription(
                    "Scale multiplier on the chest model, relative to the vanilla personal " +
                    "chest - which is already small. Colliders scale too, so low values make " +
                    "it fiddly to click. Requires a restart.",
                    new AcceptableValueRange<float>(0.25f, 3f)));

            BodyTint = cfg.Bind("Appearance", "BodyTint", "#4A3F2E",
                "Hex colour multiplied over the chest body. Darker values read as a solid " +
                "object; avoid blue-cyan, which is the colour of the placement ghost.");

            GlowColour = cfg.Bind("Appearance", "GlowColour", "#C8D93C",
                "Hex colour of the emissive glow on the chest lid.");

            GlowStrength = cfg.Bind("Appearance", "GlowStrength", 1.4f,
                new ConfigDescription(
                    "Multiplier on the glow. Above 1 pushes it into bloom.",
                    new AcceptableValueRange<float>(0f, 5f)));

            RecipeRequirements = cfg.Bind("Crafting", "Requirements", "FineWood:20,BlackMetal:10,SurtlingCore:5",
                "Build cost, as Item:Amount pairs separated by commas. Item names are prefab names, not display names.");
        }

        /// <summary>
        /// Parses <see cref="RecipeRequirements"/>. Malformed entries are skipped with a
        /// warning rather than throwing, so a typo in the config costs you a resource
        /// requirement instead of the whole mod.
        /// </summary>
        internal static RequirementConfig[] ParseRequirements()
        {
            var result = new List<RequirementConfig>();

            foreach (var pair in RecipeRequirements.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out var amount) || amount <= 0)
                {
                    Plugin.Log.LogWarning($"Ignoring malformed build requirement '{pair.Trim()}'. Expected Item:Amount.");
                    continue;
                }

                result.Add(new RequirementConfig
                {
                    Item = parts[0].Trim(),
                    Amount = amount,
                    Recover = true
                });
            }

            return result.ToArray();
        }
    }
}
