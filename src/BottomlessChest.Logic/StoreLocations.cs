using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>How a world's save files are arranged on disk.</summary>
    public enum WorldLayout
    {
        /// <summary>Through 0.221: "&lt;worlds&gt;/&lt;name&gt;.db" and ".fwl", side by side.</summary>
        Flat = 0,

        /// <summary>Valheim 1.0: "&lt;worlds&gt;/&lt;name&gt;/", holding chunks.</summary>
        Chunked
    }

    /// <summary>
    /// A place a store might be, and which storage it would be read from.
    /// </summary>
    /// <remarks>
    /// The two travel together because a candidate list spans both the world's own storage
    /// and the local fallback, and Valheim's file API needs to be told which. Deriving it
    /// afterwards by matching path prefixes would be one refactor away from reading a cloud
    /// path as a local one.
    /// </remarks>
    public readonly struct StoreCandidate
    {
        public StoreCandidate(string path, bool fromLocalFallback)
        {
            Path = path;
            FromLocalFallback = fromLocalFallback;
        }

        public string Path { get; }

        /// <summary>
        /// True when this path is in local storage rather than the world's own source.
        /// </summary>
        public bool FromLocalFallback { get; }

        public override string ToString() => Path;
    }

    /// <summary>
    /// Where a chest store is written, and everywhere an existing one might be.
    /// </summary>
    /// <remarks>
    /// The store has always lived beside the world save. Valheim 1.0 moved world saves into
    /// a directory per world and converts an existing world on its first load, which leaves
    /// the store behind in the parent directory - so "beside the world save" now names two
    /// different places depending on when the world was last opened.
    ///
    /// Reading therefore has to try both, and the ordering rule is the one from
    /// [[releases-must-preserve-user-data]]: locations may be added, never removed or
    /// reordered past a location that used to be found first. A candidate that reads as
    /// damaged falls through to the next, so preferring the newer location is free -
    /// a half-written file there cannot hide a good one behind it.
    ///
    /// Writing picks one place, from the layout the game reports for the world it actually
    /// loaded. The old file is deliberately left where it is: it costs a little disk and
    /// buys a free extra backup, and deleting a store is the one operation this project has
    /// no appetite for.
    ///
    /// Kept free of Unity types so the ordering can be tested, because the failure it
    /// guards against - a chest that opens empty and is then saved over - is invisible
    /// until it is permanent.
    /// </remarks>
    public static class StoreLocations
    {
        /// <summary>Appended to the world name. Unchanged since 0.1.0 and must stay so.</summary>
        public const string Extension = ".bottomless.dat";

        // Two generations, matching what Flush rotates. One was very nearly not enough:
        // a single bad save rotates the only good copy into ".old", and the next destroys it.
        private static readonly string[] Generations = { "", ".old", ".old2" };

        /// <summary>
        /// The single place this world's store should be written.
        /// </summary>
        /// <param name="worldsRoot">The worlds folder, with or without a trailing slash.</param>
        /// <param name="worldName">The world's name, as the game holds it.</param>
        /// <param name="layout">Which arrangement the loaded world is using.</param>
        public static string WritePath(string worldsRoot, string worldName, WorldLayout layout) =>
            layout == WorldLayout.Chunked
                ? Combine(Combine(worldsRoot, worldName), worldName + Extension)
                : Combine(worldsRoot, worldName + Extension);

        /// <summary>
        /// Every path that might hold this world's store, best first.
        /// </summary>
        /// <param name="worldsRoot">Worlds folder for the source the world was loaded from.</param>
        /// <param name="localWorldsRoot">
        /// The local worlds folder, tried afterwards. A store can end up here when a cloud
        /// world's contents were too large for the remaining cloud quota and fell back.
        /// </param>
        /// <param name="worldName">The world's name, as the game holds it.</param>
        /// <returns>
        /// Distinct paths in search order, or empty if there is no world name to build from.
        /// </returns>
        public static IReadOnlyList<StoreCandidate> ReadCandidates(
            string worldsRoot, string localWorldsRoot, string worldName)
        {
            var candidates = new List<StoreCandidate>();

            if (string.IsNullOrEmpty(worldName))
            {
                return candidates;
            }

            var seen = new HashSet<string>();

            // Order matters more than anything else here. Within a root the converted
            // location comes first because that is where a 1.0 save writes; the roots
            // themselves stay in the order 0.1.0 used.
            var roots = new[]
            {
                (Root: worldsRoot, FromLocalFallback: false),
                (Root: localWorldsRoot, FromLocalFallback: true)
            };

            foreach (var (root, fromLocalFallback) in roots)
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                foreach (var layout in new[] { WorldLayout.Chunked, WorldLayout.Flat })
                {
                    var basePath = WritePath(root, worldName, layout);

                    foreach (var generation in Generations)
                    {
                        var path = basePath + generation;

                        // A local world's own root is also the local fallback root, so
                        // without this every path would be stat'd twice. The first entry
                        // wins, which keeps the world's own source ahead of the fallback.
                        if (seen.Add(path))
                        {
                            candidates.Add(new StoreCandidate(path, fromLocalFallback));
                        }
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        /// Joins with "/", which is what Valheim's own path helpers produce and accept.
        /// </summary>
        /// <remarks>
        /// <c>World.GetSaveDirectory</c> returns a trailing slash and <c>GetDBPath</c> does
        /// not, so both shapes reach this.
        /// </remarks>
        private static string Combine(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
            {
                return right;
            }

            return left.TrimEnd('/', '\\') + "/" + right;
        }
    }
}
