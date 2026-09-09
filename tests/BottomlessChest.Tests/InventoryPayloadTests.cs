using System;
using System.IO;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// The header of a serialized inventory, which three separate readers depend on.
    /// </summary>
    /// <remarks>
    /// Valheim 1.0 narrowed the stack count in its own save format from int to ushort,
    /// which caps an inventory at 65,535 stacks. This mod's entire premise is chests far
    /// larger than that, so the count has to be ours. These tests pin the byte layout,
    /// because the failure they guard against is silent: a wrong count truncates a chest
    /// on load and the next save makes it permanent.
    /// </remarks>
    public class InventoryPayloadTests
    {
        /// <summary>Builds a vanilla payload header the way the game writes one.</summary>
        private static byte[] Vanilla(int itemVersion, int count, byte[] items = null)
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer);

            writer.Write(itemVersion);

            if (itemVersion >= InventoryPayload.FirstUShortCountVersion)
            {
                writer.Write((ushort)count);
            }
            else
            {
                writer.Write(count);
            }

            writer.Write(items ?? Array.Empty<byte>());
            writer.Flush();

            return buffer.ToArray();
        }

        [Fact]
        public void Reads_a_pre_1_0_payload_which_counted_in_an_int()
        {
            var ok = InventoryPayload.TryReadHeader(Vanilla(106, 4000), out var header);

            Assert.True(ok);
            Assert.Equal(PayloadFormat.VanillaIntCount, header.Format);
            Assert.Equal(106, header.ItemVersion);
            Assert.Equal(4000, header.Count);
            Assert.Equal(8, header.ItemsOffset);
        }

        [Fact]
        public void Reads_a_1_0_payload_which_counts_in_a_ushort()
        {
            var ok = InventoryPayload.TryReadHeader(Vanilla(109, 4000), out var header);

            Assert.True(ok);
            Assert.Equal(PayloadFormat.VanillaUShortCount, header.Format);
            Assert.Equal(109, header.ItemVersion);
            Assert.Equal(4000, header.Count);
            Assert.Equal(6, header.ItemsOffset);
        }

        // 108 is Version.Item.Smaller, the release that narrowed the field. Reading 108
        // with an int count silently shifts every item that follows.
        [Theory]
        [InlineData(107, PayloadFormat.VanillaIntCount)]
        [InlineData(108, PayloadFormat.VanillaUShortCount)]
        public void Picks_the_count_width_at_the_version_that_changed_it(int version, PayloadFormat expected)
        {
            Assert.True(InventoryPayload.TryReadHeader(Vanilla(version, 12), out var header));
            Assert.Equal(expected, header.Format);
            Assert.Equal(12, header.Count);
        }

        [Fact]
        public void Reads_back_a_payload_it_wrote_itself()
        {
            var items = new byte[] { 1, 2, 3, 4, 5 };
            var wrapped = InventoryPayload.Wrap(Vanilla(109, 3, items), 3);

            Assert.True(InventoryPayload.TryReadHeader(wrapped, out var header));
            Assert.Equal(PayloadFormat.Bottomless, header.Format);
            Assert.Equal(109, header.ItemVersion);
            Assert.Equal(3, header.Count);
            Assert.Equal(items, wrapped[header.ItemsOffset..]);
        }

        [Fact]
        public void Carries_a_count_that_would_not_fit_in_a_ushort()
        {
            const int tooManyForVanilla = 1_000_000;

            // Vanilla wrote (ushort)1_000_000, which is 16960. The true count comes from
            // the caller, which is the whole reason this format exists.
            var wrapped = InventoryPayload.Wrap(Vanilla(109, tooManyForVanilla), tooManyForVanilla);

            Assert.True(InventoryPayload.TryReadHeader(wrapped, out var header));
            Assert.Equal(tooManyForVanilla, header.Count);
        }

        [Fact]
        public void Keeps_the_item_bytes_untouched_when_wrapping_a_truncated_count()
        {
            var items = new byte[] { 9, 8, 7 };
            var wrapped = InventoryPayload.Wrap(Vanilla(109, 70_000, items), 70_000);

            Assert.True(InventoryPayload.TryReadHeader(wrapped, out var header));
            Assert.Equal(items, wrapped[header.ItemsOffset..]);
        }

        [Fact]
        public void Hands_back_the_exact_bytes_the_game_gave_it()
        {
            // The way an item version this reader cannot walk is still loadable: undo the
            // wrapping and let the game read its own format.
            var original = Vanilla(109, 3, new byte[] { 7, 7, 7 });

            Assert.True(InventoryPayload.TryUnwrapToVanilla(InventoryPayload.Wrap(original, 3), out var restored));
            Assert.Equal(original, restored);
        }

        [Fact]
        public void Hands_back_a_pre_1_0_payload_unchanged_too()
        {
            var original = Vanilla(106, 90_000, new byte[] { 1, 2 });

            Assert.True(InventoryPayload.TryUnwrapToVanilla(InventoryPayload.Wrap(original, 90_000), out var restored));
            Assert.Equal(original, restored);
        }

        [Fact]
        public void Will_not_unwrap_a_count_the_games_own_format_cannot_hold()
        {
            // 1.0 counts in a ushort, so there is no honest way to express this as a
            // vanilla payload. Saying so beats handing back a truncated one.
            var wrapped = InventoryPayload.Wrap(Vanilla(109, 70_000), 70_000);

            Assert.False(InventoryPayload.TryUnwrapToVanilla(wrapped, out var restored));
            Assert.Null(restored);
        }

        [Fact]
        public void Will_not_unwrap_something_that_was_never_wrapped()
        {
            Assert.False(InventoryPayload.TryUnwrapToVanilla(Vanilla(109, 3), out _));
            Assert.False(InventoryPayload.TryUnwrapToVanilla(null, out _));
        }

        [Fact]
        public void Refuses_a_payload_whose_leading_value_is_not_a_format_it_knows()
        {
            Assert.False(InventoryPayload.TryReadHeader(Vanilla(4242, 1), out var header));
            Assert.Equal(PayloadFormat.Unknown, header.Format);
            Assert.Equal(0, header.Count);
        }

        // Every caller of this runs on a file read off disk, so damaged input is expected
        // rather than exceptional. Throwing here would take the world down with the store.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(5)]
        public void Refuses_a_buffer_too_short_to_hold_a_header(int length)
        {
            Assert.False(InventoryPayload.TryReadHeader(new byte[length], out var header));
            Assert.Equal(PayloadFormat.Unknown, header.Format);
        }

        [Fact]
        public void Refuses_null_rather_than_throwing()
        {
            Assert.False(InventoryPayload.TryReadHeader(null, out var header));
            Assert.Equal(PayloadFormat.Unknown, header.Format);
        }

        [Fact]
        public void Refuses_a_negative_count_rather_than_sizing_a_grid_from_it()
        {
            Assert.False(InventoryPayload.TryReadHeader(Vanilla(106, -1), out var header));
            Assert.Equal(0, header.Count);
        }
    }
}
