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
        internal static ConfigEntry<string> RecipeRequirements;
        internal static ConfigEntry<float> SnapshotDebounceSeconds;
        internal static ConfigEntry<float> ModelScale;
        internal static ConfigEntry<string> BodyTint;
        internal static ConfigEntry<string> LidTint;
        internal static ConfigEntry<string> GlowColour;
        internal static ConfigEntry<float> GlowStrength;
        internal static ConfigEntry<bool> EnableTestingCommands;

        internal static void Bind(ConfigFile cfg)
        {
            EnableTestingCommands = cfg.Bind("Testing", "EnableTestingCommands", false,
                "Enables 'bottomless fill' and 'bottomless empty', which exist to test the " +
                "mod rather than to play with. They still require devcommands on top of " +
                "this. Off by default because filling a chest with a hundred thousand items " +
                "is not something anyone should be one typo away from.");

            ModelScale = cfg.Bind("Appearance", "ModelScale", 1.0f,
                new ConfigDescription(
                    "Scale multiplier on the chest model, relative to the vanilla personal " +
                    "chest - which is already small. Colliders scale too, so low values make " +
                    "it fiddly to click. Requires a restart.",
                    new AcceptableValueRange<float>(0.25f, 3f)));

            SnapshotDebounceSeconds = cfg.Bind("Multiplayer", "SnapshotDebounceSeconds", 1.5f,
                new ConfigDescription(
                    "How long to wait after the last change before sending a chest's contents " +
                    "to the server. Batches a burst of item moves into one message.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            BodyTint = cfg.Bind("Appearance", "BodyTint", "#C4C2BC",
                "Hex colour multiplied over the chest body. Darker values read as a solid " +
                "object; avoid blue-cyan, which is the colour of the placement ghost.");

            LidTint = cfg.Bind("Appearance", "LidTint", "#2E2E33",
                "Hex colour multiplied over the chest lid. Darker than the body makes the " +
                "glow read against it.");

            GlowColour = cfg.Bind("Appearance", "GlowColour", "#8FA86B",
                "Hex colour of the emissive glow on the chest lid.");

            GlowStrength = cfg.Bind("Appearance", "GlowStrength", 0.35f,
                new ConfigDescription(
                    "Multiplier on the lid glow. Around 0.5 is a subtle sheen; above 1 pushes " +
                    "it into bloom and reads as a light source.",
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
