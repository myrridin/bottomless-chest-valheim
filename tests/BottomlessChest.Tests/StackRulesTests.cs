using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class StackRulesTests
    {
        // TakeAmount decides how much of a stack leaves the chest when the player asked for
        // part of it. The server is the authority on what is actually there, so "how much
        // is available" is always its own number, never the client's.

        [Fact]
        public void TakeAmountOfZeroMeansTheWholeStack()
        {
            // Zero is the wire's "whole stack" sentinel. Callers that have never cared about
            // partial takes - Take All, a plain click - send it, and must keep taking
            // everything even when the client's idea of the stack is stale.
            Assert.Equal(5000, StackRules.TakeAmount(0, 5000));
        }

        [Fact]
        public void TakeAmountTakesTheAskedForPart()
        {
            Assert.Equal(20, StackRules.TakeAmount(20, 5000));
        }

        [Fact]
        public void TakeAmountLeavesTheRemainder()
        {
            Assert.Equal(4980, 5000 - StackRules.TakeAmount(20, 5000));
        }

        [Theory]
        [InlineData(5000)]
        [InlineData(5001)]
        [InlineData(int.MaxValue)]
        public void TakeAmountNeverExceedsWhatIsThere(int requested)
        {
            // A stale client can ask for more than the stack now holds. Clamping here is
            // what stops the split producing a negative remainder in the chest.
            Assert.Equal(5000, StackRules.TakeAmount(requested, 5000));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void TakeAmountTreatsNegativesAsTheWholeStack(int requested)
        {
            Assert.Equal(50, StackRules.TakeAmount(requested, 50));
        }

        [Fact]
        public void TakeAmountFromAnEmptyStackIsNothing()
        {
            Assert.Equal(0, StackRules.TakeAmount(10, 0));
        }

        // MergeAmount decides how much of an incoming stack collapses into one already in
        // the chest. It is the whole of the consolidation arithmetic; everything else is
        // finding the stack to call it against.

        [Fact]
        public void MergeAmountFitsTheWholeIncomingStack()
        {
            Assert.Equal(10, StackRules.MergeAmount(10, 5, 50));
        }

        [Fact]
        public void MergeAmountIsLimitedByTheRoomLeft()
        {
            Assert.Equal(5, StackRules.MergeAmount(60, 45, 50));
        }

        [Fact]
        public void MergeAmountIntoAFullStackIsNothing()
        {
            Assert.Equal(0, StackRules.MergeAmount(10, 50, 50));
        }

        [Fact]
        public void MergeAmountIntoAnOversizedStackIsNothing()
        {
            // A stack can sit above the ceiling after ValheimPlus's stack multiplier is
            // lowered or removed. Topping it up would push it further out of spec, and the
            // subtraction would go negative if room were not floored.
            Assert.Equal(0, StackRules.MergeAmount(10, 80, 50));
        }

        [Fact]
        public void MergeAmountNeverStacksTheUnstackable()
        {
            // Max stack size of one is how the game says "these do not combine" - armour,
            // tools, anything carrying its own durability.
            Assert.Equal(0, StackRules.MergeAmount(1, 0, 1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void MergeAmountOfNothingIsNothing(int incoming)
        {
            Assert.Equal(0, StackRules.MergeAmount(incoming, 5, 50));
        }
    }
}
