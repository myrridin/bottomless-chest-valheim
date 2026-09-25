using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// Place Stacks offers to several chests at once used to let two chests keep the same
    /// items. The queue sends one offer at a time and never sends an item twice.
    /// </summary>
    public class OfferQueueTests
    {
        private sealed class Item
        {
        }

        private static OfferQueue<Item> Queue() => new OfferQueue<Item>(5f);

        [Fact]
        public void TheFirstOfferGoesStraightOut()
        {
            var queue = Queue();
            queue.Enqueue("a", message: true);

            Assert.True(queue.TryNext(0f, out var store, out var message));
            Assert.Equal("a", store);
            Assert.True(message);
        }

        [Fact]
        public void ASecondChestWaitsForTheFirstReply()
        {
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.Enqueue("b", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { new Item() }, false, 0f);

            Assert.False(queue.TryNext(1f, out _, out _));
        }

        [Fact]
        public void AReplyLetsTheNextChestGo()
        {
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.Enqueue("b", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { new Item() }, false, 0f);

            Assert.True(queue.Complete("a", out _, out _));
            Assert.True(queue.TryNext(1f, out var store, out _));
            Assert.Equal("b", store);
        }

        [Fact]
        public void TheSameChestQueuedTwiceIsOneOfferAndKeepsAnyMessage()
        {
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.Enqueue("a", true);

            Assert.True(queue.TryNext(0f, out _, out var message));
            Assert.True(message);
            Assert.False(queue.TryNext(0f, out _, out _));
        }

        [Fact]
        public void ItemsInFlightAreReservedUntilTheirReply()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, false, 0f);

            Assert.True(queue.IsReserved(item));
            Assert.True(queue.Complete("a", out var items, out _));
            Assert.Same(item, Assert.Single(items));
            Assert.False(queue.IsReserved(item));
        }

        [Fact]
        public void ATimeoutLetsTheNextChestGoButKeepsTheReservation()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.Enqueue("b", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, false, 0f);

            Assert.True(queue.TryNext(6f, out var store, out _));
            Assert.Equal("b", store);
            Assert.True(queue.IsReserved(item));
        }

        [Fact]
        public void ALateReplyAfterATimeoutStillReleases()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, true, 0f);
            queue.TryNext(60f, out _, out _);

            Assert.True(queue.Complete("a", out _, out var message));
            Assert.True(message);
            Assert.False(queue.IsReserved(item));
        }

        [Fact]
        public void AChestWithAnOfferInFlightIsNotOfferedAgainUntilItAnswers()
        {
            // The server names kept items by position in the offer, so two offers to one chest
            // would make the first answer name the wrong items.
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { new Item() }, false, 0f);
            queue.Enqueue("a", false);

            Assert.False(queue.TryNext(60f, out _, out _));
            queue.Complete("a", out _, out _);
            Assert.True(queue.TryNext(60f, out var store, out _));
            Assert.Equal("a", store);
        }

        [Fact]
        public void AReplyForAChestNotInFlightIsIgnored()
        {
            Assert.False(Queue().Complete("a", out var items, out _));
            Assert.Empty(items);
        }

        [Fact]
        public void APutReservationIsReportedUntilReleased()
        {
            var queue = Queue();
            var item = new Item();

            queue.Reserve(item);
            Assert.True(queue.IsReserved(item));
            queue.Release(item);
            Assert.False(queue.IsReserved(item));
        }

        [Fact]
        public void AnItemReservedTwiceStaysReservedUntilBothRelease()
        {
            // A put and an offer can briefly hold the same item.
            var queue = Queue();
            var item = new Item();

            queue.Reserve(item);
            queue.Reserve(item);
            queue.Release(item);

            Assert.True(queue.IsReserved(item));
        }

        [Fact]
        public void ClearingDropsQueuedOffersAndReservations()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, false, 0f);
            queue.Enqueue("b", false);

            queue.Clear();

            Assert.False(queue.IsReserved(item));
            Assert.False(queue.TryNext(0f, out _, out _));
        }
    }
}
