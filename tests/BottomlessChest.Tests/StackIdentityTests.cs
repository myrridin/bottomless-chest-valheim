using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class StackIdentityTests
    {
        [Fact]
        public void IdenticalItemsShareAKey()
        {
            Assert.Equal(
                StackIdentity.Key("Wood", "$item_wood", 1, 0, 0),
                StackIdentity.Key("Wood", "$item_wood", 1, 0, 0));
        }

        [Fact]
        public void DifferentPrefabsSharingANameDoNotShareAKey()
        {
            // Seen in a real store: raw fish merged into anglerfish, a female draugr trophy into
            // a male one, voidplasm into ectoplasm. Same name token, different items.
            Assert.NotEqual(
                StackIdentity.Key("FishRaw", "$item_fish_raw", 1, 0, 0),
                StackIdentity.Key("FishAnglerRaw", "$item_fish_raw", 1, 0, 0));
        }

        [Theory]
        [InlineData(2, 0, 0)]
        [InlineData(1, 1, 0)]
        [InlineData(1, 0, 1)]
        public void QualityVariantAndWorldLevelStillSeparate(int quality, int variant, int worldLevel)
        {
            Assert.NotEqual(
                StackIdentity.Key("Wood", "$item_wood", 1, 0, 0),
                StackIdentity.Key("Wood", "$item_wood", quality, variant, worldLevel));
        }

        [Fact]
        public void AFieldBoundaryCannotBeForgedByAName()
        {
            // "a|1" + "2" and "a" + "1|2" must not collide.
            Assert.NotEqual(
                StackIdentity.Key("a|1", "2", 1, 0, 0),
                StackIdentity.Key("a", "1|2", 1, 0, 0));
        }
    }
}
