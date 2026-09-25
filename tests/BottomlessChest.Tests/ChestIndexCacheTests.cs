using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ChestIndexCacheTests
    {
        private static ChestIndex Index(long generation, long version, int wood) =>
            ChestIndex.From(generation, version, new[]
            {
                new TestItem("Wood", ItemKind.Material, wood, itemId: "Wood", maxStackSize: 50)
            });

        [Fact]
        public void AnUnknownChestHasNoIndex()
        {
            var cache = new ChestIndexCache();

            Assert.False(cache.TryGet("nope", out _));
        }

        [Fact]
        public void AStoredIndexComesBack()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 1, 50));

            Assert.True(cache.TryGet("chest-a", out var index));
            Assert.Equal(50, index.CountOf("Wood", -1));
        }

        [Fact]
        public void ANewerVersionReplacesAnOlderOne()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 1, 50));
            cache.Put("chest-a", Index(1, 2, 20));

            cache.TryGet("chest-a", out var index);
            Assert.Equal(20, index.CountOf("Wood", -1));
        }

        [Fact]
        public void AnOlderVersionIsIgnored()
        {
            // Replies can arrive out of order; a late old snapshot must not undo a new one.
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 5, 20));
            cache.Put("chest-a", Index(1, 2, 999));

            cache.TryGet("chest-a", out var index);
            Assert.Equal(20, index.CountOf("Wood", -1));
        }

        [Fact]
        public void ANewerGenerationReplacesEvenAtALowerVersion()
        {
            // A reopened session starts counting again from zero. Ordering by version alone
            // would keep the stale index from the old session - the direction that overclaims.
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 57, 999));
            cache.Put("chest-a", Index(2, 0, 20));

            cache.TryGet("chest-a", out var index);
            Assert.Equal(20, index.CountOf("Wood", -1));
        }

        [Fact]
        public void AnOlderGenerationIsIgnoredEvenAtAHigherVersion()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(2, 0, 20));
            cache.Put("chest-a", Index(1, 57, 999));

            cache.TryGet("chest-a", out var index);
            Assert.Equal(20, index.CountOf("Wood", -1));
        }

        [Fact]
        public void ChestsAreKeptApart()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 1, 50));
            cache.Put("chest-b", Index(1, 1, 7));

            cache.TryGet("chest-b", out var b);
            Assert.Equal(7, b.CountOf("Wood", -1));
        }

        [Fact]
        public void ForgettingRemovesOnlyThatChest()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 1, 50));
            cache.Put("chest-b", Index(1, 1, 7));

            cache.Forget("chest-a");

            Assert.False(cache.TryGet("chest-a", out _));
            Assert.True(cache.TryGet("chest-b", out _));
        }

        [Fact]
        public void ClearingEmptiesEverything()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 1, 50));

            cache.Clear();

            Assert.False(cache.TryGet("chest-a", out _));
        }
    }
}
