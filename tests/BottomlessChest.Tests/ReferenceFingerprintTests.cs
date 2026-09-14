using System.Collections.Generic;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// <see cref="ReferenceFingerprint"/> lets a chest session tell an outside change that only
    /// altered counts - a ValheimPlus station on the host taking one log - from one that added,
    /// removed or reordered stacks, which moves what a client's page index points at.
    /// </summary>
    public class ReferenceFingerprintTests
    {
        private sealed class Stack
        {
            internal int Count;
        }

        private static List<Stack> Stacks(int n)
        {
            var list = new List<Stack>();
            for (var i = 0; i < n; i++)
            {
                list.Add(new Stack { Count = 10 + i });
            }

            return list;
        }

        [Fact]
        public void TheSameListGivesTheSameFingerprint()
        {
            var stacks = Stacks(5);

            Assert.Equal(ReferenceFingerprint.Of(stacks), ReferenceFingerprint.Of(stacks));
        }

        [Fact]
        public void ChangingACountInPlaceKeepsTheFingerprint()
        {
            var stacks = Stacks(5);
            var before = ReferenceFingerprint.Of(stacks);

            stacks[2].Count -= 3;

            Assert.Equal(before, ReferenceFingerprint.Of(stacks));
        }

        [Fact]
        public void RemovingAStackChangesIt()
        {
            var stacks = Stacks(5);
            var before = ReferenceFingerprint.Of(stacks);

            stacks.RemoveAt(2);

            Assert.NotEqual(before, ReferenceFingerprint.Of(stacks));
        }

        [Fact]
        public void AddingAStackChangesIt()
        {
            var stacks = Stacks(5);
            var before = ReferenceFingerprint.Of(stacks);

            stacks.Add(new Stack { Count = 1 });

            Assert.NotEqual(before, ReferenceFingerprint.Of(stacks));
        }

        [Fact]
        public void SwappingTwoStacksChangesIt()
        {
            var stacks = Stacks(5);
            var before = ReferenceFingerprint.Of(stacks);

            (stacks[1], stacks[3]) = (stacks[3], stacks[1]);

            Assert.NotEqual(before, ReferenceFingerprint.Of(stacks));
        }

        [Fact]
        public void ReplacingAStackWithAnIdenticalLookingOneChangesIt()
        {
            // Identity, not contents: a different object in the same place is a different item
            // to a client holding that index, however alike the two look.
            var stacks = Stacks(5);
            var before = ReferenceFingerprint.Of(stacks);

            stacks[2] = new Stack { Count = stacks[2].Count };

            Assert.NotEqual(before, ReferenceFingerprint.Of(stacks));
        }

        [Fact]
        public void AnEmptyListHasAStableFingerprint()
        {
            Assert.Equal(ReferenceFingerprint.Of(new List<Stack>()), ReferenceFingerprint.Of(new List<Stack>()));
            Assert.NotEqual(ReferenceFingerprint.Of(new List<Stack>()), ReferenceFingerprint.Of(Stacks(1)));
        }
    }
}
