using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// BepInEx keeps whatever value a config file already holds, so a changed default only
    /// reaches new installs. <see cref="ConfigDefaults.Upgrade"/> moves a value along only
    /// when the player never touched it.
    /// </summary>
    public class ConfigDefaultsTests
    {
        private const string OldCost = "FineWood:20,BlackMetal:10,SurtlingCore:5";
        private const string NewCost = "Wood:10";

        [Fact]
        public void AnUneditedOldDefaultBecomesTheNewDefault()
        {
            Assert.Equal(NewCost, ConfigDefaults.Upgrade(OldCost, OldCost, NewCost));
        }

        [Fact]
        public void AnEditedValueIsKept()
        {
            Assert.Equal("Wood:1", ConfigDefaults.Upgrade("Wood:1", OldCost, NewCost));
        }

        [Fact]
        public void AnyDifferenceAtAllCountsAsAnEdit()
        {
            // Exact match only: a player who reformatted the old cost chose it deliberately.
            Assert.Equal("FineWood:20, BlackMetal:10, SurtlingCore:5",
                ConfigDefaults.Upgrade("FineWood:20, BlackMetal:10, SurtlingCore:5", OldCost, NewCost));
            Assert.Equal("finewood:20,blackmetal:10,surtlingcore:5",
                ConfigDefaults.Upgrade("finewood:20,blackmetal:10,surtlingcore:5", OldCost, NewCost));
        }

        [Fact]
        public void TheNewDefaultStaysPut()
        {
            Assert.Equal(NewCost, ConfigDefaults.Upgrade(NewCost, OldCost, NewCost));
        }

        [Fact]
        public void AMissingValueIsLeftAlone()
        {
            Assert.Null(ConfigDefaults.Upgrade(null, OldCost, NewCost));
        }
    }
}
