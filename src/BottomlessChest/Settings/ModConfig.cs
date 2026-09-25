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
        /// <summary>The wooden chest's cost, read from <c>piece_chest_wood</c>'s resources.</summary>
        private const string DefaultRequirements = "Wood:10";

        /// <summary>The default through 0.2.1. Never change: it identifies unedited configs.</summary>
        private const string PreviousDefaultRequirements = "FineWood:20,BlackMetal:10,SurtlingCore:5";

        /// <summary>The 0.3.0 look: a wooden chest with a warm cast and a low amber rim.</summary>
        private const string DefaultBodyTint = "#C9B79A";
        private const string DefaultLidTint = "#3A2E22";
        private const string DefaultGlowColour = "#FFC489";
        private const float DefaultGlowStrength = 0.25f;

        /// <summary>The look through 0.2.1. Never change: these identify unedited configs.</summary>
        private const string PreviousBodyTint = "#C4C2BC";
        private const string PreviousLidTint = "#2E2E33";
        private const string PreviousGlowColour = "#8FA86B";
        private const float PreviousGlowStrength = 0.35f;

        internal static ConfigEntry<string> RecipeRequirements;
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

            BodyTint = cfg.Bind("Appearance", "BodyTint", DefaultBodyTint,
                "Hex colour multiplied over the chest body. Darker values read as a solid " +
                "object; avoid blue-cyan, which is the colour of the placement ghost.");

            LidTint = cfg.Bind("Appearance", "LidTint", DefaultLidTint,
                "Hex colour multiplied over the chest lid. Darker than the body makes the " +
                "glow read against it.");

            GlowColour = cfg.Bind("Appearance", "GlowColour", DefaultGlowColour,
                "Hex colour of the emissive glow on the chest lid.");

            GlowStrength = cfg.Bind("Appearance", "GlowStrength", DefaultGlowStrength,
                new ConfigDescription(
                    "Multiplier on the lid glow. Around 0.5 is a subtle sheen; above 1 pushes " +
                    "it into bloom and reads as a light source.",
                    new AcceptableValueRange<float>(0f, 5f)));

            RecipeRequirements = cfg.Bind("Crafting", "Requirements", DefaultRequirements,
                "Build cost, as Item:Amount pairs separated by commas. Item names are prefab names, not display names.");

            // Once per file, not once per launch. Running these every start meant a player who
            // deliberately set the old cost or the old colours had them overwritten again on the
            // next launch, with no way to keep them.
            var applied = cfg.Bind("Advanced", "AppliedDefaultChanges", 0,
                "Internal. Which of this mod's default changes have already been applied to this " +
                "file, so they are applied once and never fight a value you chose. Do not edit.");

            if (applied.Value >= 1)
            {
                return;
            }

            applied.Value = 1;

            // 0.3.0 made the chest cost what a wooden chest does. Players who never edited the
            // cost get the new one; anyone who set their own keeps it.
            var upgraded = Logic.ConfigDefaults.Upgrade(
                RecipeRequirements.Value, PreviousDefaultRequirements, DefaultRequirements);
            if (upgraded != RecipeRequirements.Value)
            {
                RecipeRequirements.Value = upgraded;
                Plugin.Log.LogInfo($"Build cost was the old default; updated to {upgraded}.");
            }

            // 0.3.0 also changed how the chest looks. Same rule: a colour still exactly the old
            // default was never chosen, so it moves; anything else was, and stays. All four move
            // together or not at all - half of one palette and half of another is nobody's taste.
            if (BodyTint.Value == PreviousBodyTint
                && LidTint.Value == PreviousLidTint
                && GlowColour.Value == PreviousGlowColour
                && GlowStrength.Value == PreviousGlowStrength)
            {
                BodyTint.Value = DefaultBodyTint;
                LidTint.Value = DefaultLidTint;
                GlowColour.Value = DefaultGlowColour;
                GlowStrength.Value = DefaultGlowStrength;
                Plugin.Log.LogInfo("Chest colours were the old defaults; updated to the 0.3.0 look.");
            }
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
