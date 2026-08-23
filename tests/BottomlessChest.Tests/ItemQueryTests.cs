using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ItemQueryTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void BlankQueryMatchesEverything(string text)
        {
            var query = ItemQuery.Parse(text);

            Assert.True(query.IsEmpty);
            Assert.True(query.Matches(new TestItem("Iron")));
            Assert.True(query.Matches(new TestItem("Boar meat", ItemKind.Food)));
        }

        [Fact]
        public void SubstringMatchIgnoresCase()
        {
            var query = ItemQuery.Parse("IRON");

            Assert.True(query.Matches(new TestItem("Iron nails")));
        }

        [Fact]
        public void SubstringMatchesMidWord()
        {
            var query = ItemQuery.Parse("nail");

            Assert.True(query.Matches(new TestItem("Iron nails")));
        }

        [Fact]
        public void SubstringMatchIgnoresDiacritics()
        {
            var query = ItemQuery.Parse("angbat");

            Assert.True(query.Matches(new TestItem("Ångbåt")));
        }

        [Fact]
        public void NonMatchingTermExcludesItem()
        {
            var query = ItemQuery.Parse("bronze");

            Assert.False(query.Matches(new TestItem("Iron nails")));
        }

        [Fact]
        public void EveryTermMustMatch()
        {
            var query = ItemQuery.Parse("iron nail");

            Assert.True(query.Matches(new TestItem("Iron nails")));
            Assert.False(query.Matches(new TestItem("Iron ingot")));
        }

        [Fact]
        public void KindTokenSelectsByKind()
        {
            var query = ItemQuery.Parse("@food");

            Assert.True(query.Matches(new TestItem("Boar meat", ItemKind.Food)));
            Assert.False(query.Matches(new TestItem("Iron", ItemKind.Material)));
        }

        [Fact]
        public void KindTokenIsCombinableWithText()
        {
            var query = ItemQuery.Parse("@weapon iron");

            Assert.True(query.Matches(new TestItem("Iron sword", ItemKind.Weapon)));
            Assert.False(query.Matches(new TestItem("Iron", ItemKind.Material)));
            Assert.False(query.Matches(new TestItem("Bronze sword", ItemKind.Weapon)));
        }

        [Fact]
        public void UnknownKindTokenFallsBackToLiteralText()
        {
            // A typo should quietly find nothing rather than silently matching everything.
            var query = ItemQuery.Parse("@fod");

            Assert.False(query.Matches(new TestItem("Boar meat", ItemKind.Food)));
            Assert.True(query.Matches(new TestItem("Weird @fod item")));
        }
        [Fact]
        public void SeveralKindTokensWidenTheSearch()
        {
            // AND across kinds would make this query match nothing, which is never what
            // someone typing two categories means.
            var query = ItemQuery.Parse("@food @trophy");

            Assert.True(query.Matches(new TestItem("Boar meat", ItemKind.Food)));
            Assert.True(query.Matches(new TestItem("Boar trophy", ItemKind.Trophy)));
            Assert.False(query.Matches(new TestItem("Iron", ItemKind.Material)));
        }
    }
}
