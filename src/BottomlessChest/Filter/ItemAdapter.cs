using BottomlessChest.Logic;

namespace BottomlessChest.Filter
{
    /// <summary>
    /// Presents a Valheim item to the search rules.
    /// </summary>
    /// <remarks>
    /// The rules live in an assembly that knows nothing about Unity or Valheim so they can
    /// be tested; this is the seam where the two meet. A struct to keep filtering thousands
    /// of items per keystroke off the heap.
    /// </remarks>
    internal readonly struct ItemAdapter : IStorableItem
    {
        private readonly ItemDrop.ItemData _item;

        internal ItemAdapter(ItemDrop.ItemData item)
        {
            _item = item;
        }

        public string ItemId => _item.m_dropPrefab != null ? _item.m_dropPrefab.name : _item.m_shared.m_name;

        public string DisplayName => Localization.instance.Localize(_item.m_shared.m_name);

        public ItemKind Kind => Classify(_item.m_shared.m_itemType);

        public int Stack => _item.m_stack;

        public int MaxStackSize => _item.m_shared.m_maxStackSize;

        public int Quality => _item.m_quality;

        public int Variant => _item.m_variant;

        public string CustomData => null;

        private static ItemKind Classify(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Material:
                    return ItemKind.Material;

                case ItemDrop.ItemData.ItemType.Consumable:
                case ItemDrop.ItemData.ItemType.Fish:
                    return ItemKind.Food;

                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    return ItemKind.Weapon;

                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Shield:
                    return ItemKind.Armor;

                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return ItemKind.Ammo;

                case ItemDrop.ItemData.ItemType.Tool:
                    return ItemKind.Tool;

                case ItemDrop.ItemData.ItemType.Trophy:
                    return ItemKind.Trophy;

                default:
                    return ItemKind.Misc;
            }
        }
    }
}
