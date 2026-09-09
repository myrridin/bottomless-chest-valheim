using System;

namespace BottomlessChest.Logic
{
    /// <summary>How the stack count at the head of a serialized inventory is encoded.</summary>
    public enum PayloadFormat
    {
        /// <summary>Not a payload this build knows how to read.</summary>
        Unknown = 0,

        /// <summary>Valheim through 0.221: item version, then the count as an int.</summary>
        VanillaIntCount,

        /// <summary>Valheim 1.0 and later: item version, then the count as a ushort.</summary>
        VanillaUShortCount,

        /// <summary>Ours: a marker, the item version, then the count as an int.</summary>
        Bottomless
    }

    /// <summary>What the head of a serialized inventory says about the rest of it.</summary>
    public readonly struct PayloadHeader
    {
        public PayloadHeader(PayloadFormat format, int itemVersion, int count, int itemsOffset)
        {
            Format = format;
            ItemVersion = itemVersion;
            Count = count;
            ItemsOffset = itemsOffset;
        }

        public PayloadFormat Format { get; }

        /// <summary>The game's own item format version, which decides how items are encoded.</summary>
        public int ItemVersion { get; }

        /// <summary>Number of stacks. Zero whenever <see cref="Format"/> is Unknown.</summary>
        public int Count { get; }

        /// <summary>Byte offset of the first item, so a reader can start there.</summary>
        public int ItemsOffset { get; }
    }

    /// <summary>
    /// Reads and writes the header of a serialized inventory.
    /// </summary>
    /// <remarks>
    /// Valheim 1.0 changed <c>Inventory.Save</c> to write the stack count as a
    /// <c>ushort</c>. That caps an inventory at 65,535 stacks, and this mod exists to hold
    /// far more than that - a chest of a million stacks would wrap to 16,960 and lose the
    /// rest on the next load, permanently, with nothing logged.
    ///
    /// So the count has to be ours. Everything else stays the game's: items are still
    /// encoded by <c>ItemData.Save</c>, and the version we record is whatever the game
    /// wrote, never a constant of ours. A future item format therefore costs nothing here.
    ///
    /// Three formats can appear in a store and all three must read, because a store written
    /// by any earlier build has to keep opening. Only the last is ever written.
    ///
    /// This lives in the logic assembly, away from Unity, because a byte layout that is
    /// wrong in a way nobody notices until a chest empties is exactly the kind of thing
    /// that should be pinned by tests rather than by playing.
    /// </remarks>
    public static class InventoryPayload
    {
        /// <summary>"BLCI" - big enough that no item version could be mistaken for it.</summary>
        public const int Marker = 0x424C4349;

        /// <summary>
        /// <c>Version.Item.Smaller</c>, the release that narrowed the count to a ushort.
        /// </summary>
        public const int FirstUShortCountVersion = 108;

        // Item versions have run 101..109. The ceiling only has to exclude Marker and
        // obvious garbage; a value outside it means we are not looking at an inventory.
        private const int LowestKnownItemVersion = 100;
        private const int HighestPlausibleItemVersion = 999;

        /// <summary>
        /// Reads the header, reporting failure rather than throwing on damaged input.
        /// </summary>
        /// <remarks>
        /// Every caller reads from a file on disk, so a truncated or corrupt buffer is an
        /// expected input and not an exceptional one. A throw here would propagate out of
        /// a chest load and take the world down with the store.
        /// </remarks>
        public static bool TryReadHeader(byte[] payload, out PayloadHeader header)
        {
            header = default;

            if (payload == null || payload.Length < 6)
            {
                return false;
            }

            var leading = ReadInt(payload, 0);

            if (leading == Marker)
            {
                // Marker, version, count - and the count is the last thing we can read
                // before items begin, so the buffer has to be long enough to hold it.
                if (payload.Length < 12)
                {
                    return false;
                }

                var version = ReadInt(payload, 4);
                var count = ReadInt(payload, 8);

                return Accept(PayloadFormat.Bottomless, version, count, 12, out header);
            }

            if (leading < LowestKnownItemVersion || leading > HighestPlausibleItemVersion)
            {
                return false;
            }

            if (leading >= FirstUShortCountVersion)
            {
                return Accept(PayloadFormat.VanillaUShortCount, leading, ReadUShort(payload, 4), 6, out header);
            }

            if (payload.Length < 8)
            {
                return false;
            }

            return Accept(PayloadFormat.VanillaIntCount, leading, ReadInt(payload, 4), 8, out header);
        }

        /// <summary>
        /// Rewrites a payload the game produced so it carries the true stack count.
        /// </summary>
        /// <param name="vanillaPayload">Exactly what <c>Inventory.Save</c> wrote.</param>
        /// <param name="trueCount">
        /// The count the caller holds. Above 65,535 this is the only place it survives -
        /// the game already truncated its own copy before we were handed the bytes.
        /// </param>
        /// <remarks>
        /// The item bytes are copied through untouched. Only the header is replaced, so
        /// this stays correct across item format changes we know nothing about.
        /// </remarks>
        public static byte[] Wrap(byte[] vanillaPayload, int trueCount)
        {
            if (vanillaPayload == null)
            {
                throw new ArgumentNullException(nameof(vanillaPayload));
            }

            if (trueCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trueCount), trueCount, "A stack count cannot be negative.");
            }

            if (!TryReadHeader(vanillaPayload, out var header))
            {
                throw new ArgumentException(
                    "Not a serialized inventory: its leading value is neither an item version nor our marker.",
                    nameof(vanillaPayload));
            }

            var itemBytes = vanillaPayload.Length - header.ItemsOffset;
            var wrapped = new byte[12 + itemBytes];

            WriteInt(wrapped, 0, Marker);
            WriteInt(wrapped, 4, header.ItemVersion);
            WriteInt(wrapped, 8, trueCount);
            Buffer.BlockCopy(vanillaPayload, header.ItemsOffset, wrapped, 12, itemBytes);

            return wrapped;
        }

        /// <summary>
        /// A negative count would size a grid, and a grid too small drops items silently.
        /// </summary>
        private static bool Accept(PayloadFormat format, int version, int count, int offset, out PayloadHeader header)
        {
            if (count < 0)
            {
                header = default;
                return false;
            }

            header = new PayloadHeader(format, version, count, offset);
            return true;
        }

        // ZPackage writes through a BinaryWriter, so everything is little-endian.
        private static int ReadInt(byte[] buffer, int offset) =>
            buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24);

        private static int ReadUShort(byte[] buffer, int offset) =>
            buffer[offset] | (buffer[offset + 1] << 8);

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
