using System.Linq;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// Where a chest store is written, and everywhere it might already be.
    /// </summary>
    /// <remarks>
    /// Valheim 1.0 moved world saves into a directory per world, and converts an existing
    /// world on its first load - leaving the store behind in the parent. Get the read order
    /// wrong and a chest that is full on disk opens empty, and the next save makes that
    /// permanent. These tests exist because that failure is invisible until it is final.
    /// </remarks>
    public class StoreLocationsTests
    {
        private const string Cloud = "/save/worlds";
        private const string Local = "/save/worlds_local";

        private static System.Collections.Generic.List<string> Paths(
            System.Collections.Generic.IReadOnlyList<StoreCandidate> candidates) =>
            candidates.Select(c => c.Path).ToList();

        private static int IndexOf(
            System.Collections.Generic.IReadOnlyList<StoreCandidate> list, string value) =>
            Paths(list).IndexOf(value);

        [Fact]
        public void Writes_exactly_where_0_1_0_wrote()
        {
            // Pinned as a literal on purpose. This is the string every existing chest
            // depends on, so it should be read and checked by eye, not derived.
            Assert.Equal(
                "/save/worlds/CasualSolo.bottomless.dat",
                StoreLocations.WritePath(Cloud, "CasualSolo"));
        }

        /// <summary>
        /// The store must never be written inside the world's own directory.
        /// </summary>
        /// <remarks>
        /// Valheim prunes that directory: SaveSystem.RenameDirectory deletes every file
        /// whose extension is not one of .fwl2 .db2 .chunks .ok .chunk, and it runs when a
        /// player restores a world backup or makes one. All three of our generations would
        /// go in a single call, and the world backup would not contain them either.
        ///
        /// This test is the guard rail on a mistake that was actually made and shipped to
        /// this branch before review caught it.
        /// </remarks>
        [Fact]
        public void Never_writes_inside_the_directory_valheim_prunes()
        {
            var path = StoreLocations.WritePath(Cloud, "CasualSolo");

            Assert.DoesNotContain("/CasualSolo/", path);
        }

        [Fact]
        public void Tolerates_the_trailing_slash_the_game_returns()
        {
            // World.GetSaveDirectory ends with "/", World.GetDBPath does not.
            Assert.Equal(
                "/save/worlds/CasualSolo.bottomless.dat",
                StoreLocations.WritePath("/save/worlds/", "CasualSolo"));
        }

        /// <summary>
        /// Reading still looks inside the per-world directory, even though writing does not.
        /// </summary>
        /// <remarks>
        /// A build on this branch, before the fix, wrote there. Anyone who ran it has a
        /// store in that location, and dropping the candidate would strand it.
        /// </remarks>
        [Fact]
        public void Still_reads_the_directory_an_earlier_build_wrote_into()
        {
            Assert.Contains(
                "/save/worlds/CasualSolo/CasualSolo.bottomless.dat",
                Paths(StoreLocations.ReadCandidates(Cloud, Local, "CasualSolo")));
        }

        /// <summary>
        /// The standing rule, as a test: a user on any earlier version must still be found.
        /// </summary>
        /// <remarks>
        /// These four are every path 0.1.0 would read. If a change to the ordering ever
        /// drops one of them, that release silently empties somebody's chest. Adding
        /// locations is safe; removing one is not.
        /// </remarks>
        [Theory]
        [InlineData("/save/worlds/CasualSolo.bottomless.dat")]
        [InlineData("/save/worlds/CasualSolo.bottomless.dat.old")]
        [InlineData("/save/worlds/CasualSolo.bottomless.dat.old2")]
        [InlineData("/save/worlds_local/CasualSolo.bottomless.dat")]
        [InlineData("/save/worlds_local/CasualSolo.bottomless.dat.old")]
        public void Still_looks_everywhere_0_1_0_looked(string historical)
        {
            Assert.Contains(historical, Paths(StoreLocations.ReadCandidates(Cloud, Local, "CasualSolo")));
        }

        [Fact]
        public void Looks_in_the_per_world_directory_before_the_parent()
        {
            var candidates = StoreLocations.ReadCandidates(Cloud, Local, "CasualSolo");

            var inDirectory = IndexOf(candidates, "/save/worlds/CasualSolo/CasualSolo.bottomless.dat");
            var beside = IndexOf(candidates, "/save/worlds/CasualSolo.bottomless.dat");

            Assert.True(inDirectory >= 0 && beside >= 0);

            // After a migration both exist, and the one in the directory is the live copy.
            // A damaged file there falls through to the older one, which is why preferring
            // it costs nothing.
            Assert.True(inDirectory < beside);
        }

        [Fact]
        public void Prefers_the_worlds_the_game_is_using_over_the_local_fallback()
        {
            var candidates = StoreLocations.ReadCandidates(Cloud, Local, "CasualSolo");

            Assert.True(
                IndexOf(candidates, "/save/worlds/CasualSolo.bottomless.dat")
                < IndexOf(candidates, "/save/worlds_local/CasualSolo.bottomless.dat"));
        }

        [Fact]
        public void Offers_both_backup_generations_for_every_location()
        {
            var paths = Paths(StoreLocations.ReadCandidates(Cloud, Local, "W"));

            foreach (var b in new[] { "/save/worlds/W/W", "/save/worlds/W", "/save/worlds_local/W/W", "/save/worlds_local/W" })
            {
                Assert.Contains(b + ".bottomless.dat", paths);
                Assert.Contains(b + ".bottomless.dat.old", paths);
                Assert.Contains(b + ".bottomless.dat.old2", paths);
            }
        }

        [Fact]
        public void Does_not_search_the_same_place_twice_for_a_local_world()
        {
            // A local world's own root is the local fallback root, so a naive list would
            // stat every path twice.
            var paths = Paths(StoreLocations.ReadCandidates(Local, Local, "CasualSolo"));

            Assert.Equal(paths.Count, new System.Collections.Generic.HashSet<string>(paths).Count);
        }

        [Fact]
        public void Keeps_the_search_order_stable_for_a_local_world()
        {
            var paths = Paths(StoreLocations.ReadCandidates(Local, Local, "W"));

            Assert.Equal(
                new[]
                {
                    "/save/worlds_local/W/W.bottomless.dat",
                    "/save/worlds_local/W/W.bottomless.dat.old",
                    "/save/worlds_local/W/W.bottomless.dat.old2",
                    "/save/worlds_local/W.bottomless.dat",
                    "/save/worlds_local/W.bottomless.dat.old",
                    "/save/worlds_local/W.bottomless.dat.old2"
                },
                paths);
        }

        [Fact]
        public void Marks_which_storage_each_candidate_would_be_read_from()
        {
            var candidates = StoreLocations.ReadCandidates(Cloud, Local, "W");

            // Reading a cloud path as a local one finds nothing and reports the chest as
            // empty, which is the failure this flag exists to prevent.
            Assert.All(
                candidates.Where(c => c.Path.StartsWith("/save/worlds/")),
                c => Assert.False(c.FromLocalFallback));

            Assert.All(
                candidates.Where(c => c.Path.StartsWith("/save/worlds_local/")),
                c => Assert.True(c.FromLocalFallback));
        }

        [Fact]
        public void Treats_a_local_worlds_own_storage_as_its_own_not_as_the_fallback()
        {
            // Deduplication keeps the first occurrence, so these must stay unflagged -
            // otherwise a local world would read every path through the fallback branch.
            Assert.All(
                StoreLocations.ReadCandidates(Local, Local, "W"),
                c => Assert.False(c.FromLocalFallback));
        }

        [Fact]
        public void Returns_nothing_rather_than_a_path_built_from_a_missing_world_name()
        {
            Assert.Empty(StoreLocations.ReadCandidates(Cloud, Local, null));
            Assert.Empty(StoreLocations.ReadCandidates(Cloud, Local, ""));
        }
    }
}
