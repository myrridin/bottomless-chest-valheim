namespace BottomlessChest.Logic
{
    /// <summary>
    /// The only view of an item the storage rules are allowed to have.
    /// </summary>
    /// <remarks>
    /// Keeping this deliberately narrow is what lets every rule in this assembly be
    /// tested without a running game. The mod adapts <c>ItemDrop.ItemData</c> onto it.
    /// </remarks>
    public interface IStorableItem
    {
        /// <summary>Stable prefab name. Identity for stacking.</summary>
        string ItemId { get; }

        /// <summary>Localized name. What the player searches against.</summary>
        string DisplayName { get; }

        ItemKind Kind { get; }

        int Stack { get; }

        /// <summary>Vanilla stack ceiling. 1 means the item is individually meaningful.</summary>
        int MaxStackSize { get; }

        int Quality { get; }

        int Variant { get; }

        /// <summary>Serialized per-instance data, or null. Two items with different custom data are different items.</summary>
        string CustomData { get; }
    }

    /// <summary>Coarse buckets over Valheim's much longer ItemType enum.</summary>
    public enum ItemKind
    {
        Unknown = 0,
        Material,
        Food,
        Weapon,
        Armor,
        Ammo,
        Tool,
        Trophy,
        Misc
    }
}
