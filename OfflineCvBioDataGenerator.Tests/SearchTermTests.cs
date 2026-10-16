using OfflineCvBioDataGenerator.Data;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    public class SearchTermTests
    {
        [Fact]
        public void Escape_LeavesOrdinaryTextUntouched()
        {
            Assert.Equal("Sunil Perera", SearchTerm.Escape("Sunil Perera"));
        }

        [Fact]
        public void Escape_EscapesUnderscore()
        {
            Assert.Equal(@"Al\_ha", SearchTerm.Escape("Al_ha"));
        }

        [Fact]
        public void Escape_EscapesPercent()
        {
            Assert.Equal(@"100\%", SearchTerm.Escape("100%"));
        }

        [Fact]
        public void Escape_EscapesBackslashFirst()
        {
            // A literal backslash must not be mistaken for the escape character.
            Assert.Equal(@"a\\b", SearchTerm.Escape(@"a\b"));
        }

        [Fact]
        public void Escape_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, SearchTerm.Escape(null));
            Assert.Equal(string.Empty, SearchTerm.Escape(string.Empty));
        }

        [Fact]
        public void Contains_WrapsWithWildcards()
        {
            Assert.Equal("%abc%", SearchTerm.Contains("abc"));
        }

        [Fact]
        public void Contains_EmptyInput_MatchesEverything()
        {
            Assert.Equal("%", SearchTerm.Contains(""));
            Assert.Equal("%", SearchTerm.Contains(null));
        }

        [Fact]
        public void Contains_EscapesWildcardsInsideThePattern()
        {
            Assert.Equal(@"%100\%%", SearchTerm.Contains("100%"));
        }
    }
}
