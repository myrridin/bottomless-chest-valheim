using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ReapplyTriggerTests
    {
        [Fact]
        public void AFreshTriggerAsksForAnApply()
        {
            // Opening a chest must filter once before anything has changed.
            Assert.True(new ReapplyTrigger().NeedsApply(12));
        }

        [Fact]
        public void AnUnchangedCountDoesNotAskAgain()
        {
            var trigger = new ReapplyTrigger();
            trigger.NoteApplied(12);

            Assert.False(trigger.NeedsApply(12));
        }

        [Fact]
        public void AnAddedStackAsksForAnApply()
        {
            var trigger = new ReapplyTrigger();
            trigger.NoteApplied(12);

            Assert.True(trigger.NeedsApply(13));
        }

        [Fact]
        public void ARemovedStackAsksForAnApply()
        {
            var trigger = new ReapplyTrigger();
            trigger.NoteApplied(12);

            Assert.True(trigger.NeedsApply(11));
        }

        [Fact]
        public void ReturningToAnEarlierCountStillAsksForAnApply()
        {
            // Take a stack out and put it back: the count matches a value seen before, but
            // not the one the current layout was built from, so the view is still stale.
            var trigger = new ReapplyTrigger();
            trigger.NoteApplied(12);
            trigger.NeedsApply(11);
            trigger.NoteApplied(11);

            Assert.True(trigger.NeedsApply(12));
        }

        [Fact]
        public void AskingDoesNotSatisfyTheTrigger()
        {
            // Only applying clears it. A frame that notices staleness but cannot act on it
            // must not convince the next frame that the work was done.
            var trigger = new ReapplyTrigger();
            trigger.NoteApplied(12);

            Assert.True(trigger.NeedsApply(13));
            Assert.True(trigger.NeedsApply(13));
        }

        [Fact]
        public void InvalidateForcesAnApplyEvenAtTheSameCount()
        {
            // Changing the search term leaves the count alone but changes what matches.
            var trigger = new ReapplyTrigger();
            trigger.NoteApplied(12);
            trigger.Invalidate();

            Assert.True(trigger.NeedsApply(12));
        }

        [Fact]
        public void AnEmptyChestIsDistinguishableFromAFreshTrigger()
        {
            var trigger = new ReapplyTrigger();
            trigger.NoteApplied(0);

            Assert.False(trigger.NeedsApply(0));
        }
    }
}
