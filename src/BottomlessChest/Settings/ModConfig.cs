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
        internal static ConfigEntry<bool> UnlimitedStacks;
        internal static ConfigEntry<int> MaxStackMultiplier;
        internal static ConfigEntry<int> MaxItemEntries;
        internal static ConfigEntry<string> RecipeRequirements;

        internal static void Bind(ConfigFile cfg)
        {
            GridWidth = cfg.Bind("Storage", "GridWidth", 8,
                "How many columns the chest window shows. Purely visual; capacity is unbounded either way.");

            VisibleRows = cfg.Bind("Storage", "VisibleRows", 6,
                "How many rows are visible before the contents start scrolling.");

            UnlimitedStacks = cfg.Bind("Storage", "UnlimitedStacks", true,
                "Merge stackable items into a single unbounded stack inside the chest. " +
                "Stacks are always split back down to vanilla-legal sizes on the way out, " +
                "so nothing illegal can end up in a player inventory or a vanilla chest.");

            MaxStackMultiplier = cfg.Bind("Storage", "MaxStackMultiplier", 100,
                new ConfigDescription(
                    "Stack cap as a multiple of the item's vanilla max stack size. Ignored when UnlimitedStacks is true.",
                    new AcceptableValueRange<int>(1, 100000)));

            MaxItemEntries = cfg.Bind("Storage", "MaxItemEntries", 0,
                "Safety valve: refuse to store more than this many distinct entries in one chest. 0 means no limit.");

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
