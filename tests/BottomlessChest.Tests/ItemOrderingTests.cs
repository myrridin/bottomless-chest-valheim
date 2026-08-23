using System.Collections.Generic;
using System.Linq;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ItemOrderingTests
    {
        private static List<string> Sorted(params IStorableItem[] items) =>
            items.OrderBy(i => i, ItemOrdering.ByName).Select(i => i.DisplayName).ToList();

        [Fact]
        public void SortsByNameAlphabetically()
        {
            var order = Sorted(
                new TestItem("Wood"),
                new TestItem("Iron"),
                new TestItem("Stone"));

            Assert.Equal(new[] { "Iron", "Stone", "Wood" }, order);
        }

        [Fact]
        public void SortingIgnoresCase()
        {
            var order = Sorted(new TestItem("wood"), new TestItem("Iron"));

            Assert.Equal(new[] { "Iron", "wood" }, order);
        }

        [Fact]
        public void SortingIgnoresDiacritics()
        {
            // "Ångbåt" should sort with A, not after Z, which is where raw ordinal puts it.
            var order = Sorted(new TestItem("Wood"), new TestItem("Ångbåt"), new TestItem("Bronze"));

            Assert.Equal(new[] { "Ångbåt", "Bronze", "Wood" }, order);
        }

        [Fact]
        public void SameNameOrdersByQualityDescending()
        {
            var order = new[]
            {
                new TestItem("Sword", quality: 1),
                new TestItem("Sword", quality: 3),
                new TestItem("Sword", quality: 2)
            }.OrderBy(i => i, ItemOrdering.ByName).Select(i => i.Quality).ToList();

            Assert.Equal(new[] { 3, 2, 1 }, order);
        }

        [Fact]
        public void SameNameAndQualityOrdersByLargestStackFirst()
        {
            var order = new[]
            {
                new TestItem("Stone", stack: 12),
                new TestItem("Stone", stack: 50),
                new TestItem("Stone", stack: 30)
            }.OrderBy(i => i, ItemOrdering.ByName).Select(i => i.Stack).ToList();

            Assert.Equal(new[] { 50, 30, 12 }, order);
        }

        [Fact]
        public void NullsDoNotThrow()
        {
            Assert.Equal(0, ItemOrdering.ByName.Compare(null, null));
            Assert.True(ItemOrdering.ByName.Compare(null, new TestItem("Wood")) < 0);
            Assert.True(ItemOrdering.ByName.Compare(new TestItem("Wood"), null) > 0);
        }
    }
}
