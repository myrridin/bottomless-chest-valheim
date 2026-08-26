namespace BottomlessChest.Net
{
    /// <summary>Message kinds exchanged between a client and the server for one chest.</summary>
    /// <remarks>
    /// Deliberately small and fixed-size. A page is roughly three kilobytes whatever the
    /// chest holds, so a hundred thousand stacks costs no more to browse than a hundred.
    /// </remarks>
    internal enum ChestMessage
    {
        /// <summary>Client asks the server to start a session and send the first page.</summary>
        Open = 0,

        /// <summary>Client asks for a page: a query and a scroll row.</summary>
        Page = 1,

        /// <summary>Server returns one page plus the counts needed to draw the window.</summary>
        PageResult = 2,

        /// <summary>Client asks for items at given positions in the current match order.</summary>
        Take = 3,

        /// <summary>Server hands over the items it removed. Only now may the client keep them.</summary>
        Granted = 4,

        /// <summary>Client offers an item to the chest.</summary>
        Put = 5,

        /// <summary>Server confirms it took the item. Only now may the client drop its copy.</summary>
        Accepted = 6,

        /// <summary>Client is done with the chest; the server may persist and forget it.</summary>
        Close = 7,
    }
}
