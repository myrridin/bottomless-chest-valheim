namespace BottomlessChest.Logic
{
    /// <summary>
    /// What has to match for two stacks to be one stack.
    /// </summary>
    /// <remarks>
    /// The item itself, by prefab, and then vanilla's rule on top: name, quality, world level,
    /// plus variant, since items differing only by variant look different to the player.
    ///
    /// The prefab is what vanilla leaves out. Different items can share a name token - raw fish
    /// and anglerfish, the male and female draugr trophies, voidplasm and ectoplasm - and a
    /// key on the name alone merged one into the other, keeping the count and changing what
    /// the player owns. Vanilla only merges on the way into an inventory; a chest that tidies
    /// itself touches everything in it, so it has to be stricter.
    ///
    /// Strings are length-prefixed so no name can forge a field boundary.
    /// </remarks>
    public static class StackIdentity
    {
        public static string Key(string itemId, string sharedName, int quality, int variant, int worldLevel)
        {
            itemId = itemId ?? string.Empty;
            sharedName = sharedName ?? string.Empty;

            return $"{itemId.Length}:{itemId}|{sharedName.Length}:{sharedName}|{quality}|{variant}|{worldLevel}";
        }
    }
}
