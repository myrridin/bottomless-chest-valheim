namespace BottomlessChest.Logic
{
    public static class StackRules
    {
        /// <summary>
        /// How much of an incoming stack collapses into one the chest already holds.
        /// </summary>
        /// <remarks>
        /// The whole of the consolidation arithmetic. A ceiling of one means the game says
        /// these do not combine - armour, tools, anything carrying durability - and an
        /// existing stack already at or above the ceiling has no room - a stack left oversized
        /// by a since-lowered stack multiplier - which would otherwise subtract into a negative.
        /// </remarks>
        public static int MergeAmount(int incoming, int targetStack, int maxStackSize)
        {
            if (incoming <= 0 || maxStackSize <= 1)
            {
                return 0;
            }

            var room = maxStackSize - targetStack;
            if (room <= 0)
            {
                return 0;
            }

            return incoming < room ? incoming : room;
        }

        /// <summary>
        /// How much of a stack actually leaves the chest for a requested amount.
        /// </summary>
        /// <remarks>
        /// Zero and anything negative mean "all of it". That sentinel exists so the callers
        /// that never cared about partial takes - Take All, an ordinary click - keep saying
        /// nothing about amounts and keep getting the whole stack, rather than each having
        /// to send a number it would have to read off a page that may be out of date.
        ///
        /// <paramref name="available"/> is always the server's own count. A client asking
        /// for more than the stack now holds is the ordinary case after a stale page, not an
        /// error, and clamping is what keeps the split from leaving a negative remainder
        /// behind in the chest.
        /// </remarks>
        public static int TakeAmount(int requested, int available)
        {
            if (available <= 0)
            {
                return 0;
            }

            return requested <= 0 || requested >= available ? available : requested;
        }

    }
}
