using FluentAssertions;
using SqlPilot.Core.Search;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class FuzzyMatcherTests
    {
        [Fact]
        public void ExactMatch_ReturnsHighestScore()
        {
            var score = FuzzyMatcher.Score("Customer", "Customer");
            score.Should().Be(1000);
        }

        [Fact]
        public void ExactMatch_IsCaseInsensitive()
        {
            var score = FuzzyMatcher.Score("customer", "Customer");
            score.Should().Be(1000);
        }

        [Fact]
        public void PrefixMatch_ReturnsHighScore()
        {
            var score = FuzzyMatcher.Score("Cust", "Customer");
            score.Should().BeGreaterThan(700);
        }

        [Fact]
        public void PrefixMatch_ScoresHigherThanSubstring()
        {
            var prefixScore = FuzzyMatcher.Score("Cust", "Customer");
            var substringScore = FuzzyMatcher.Score("ustom", "Customer");
            prefixScore.Should().BeGreaterThan(substringScore);
        }

        [Fact]
        public void CamelCaseMatch_Works()
        {
            var score = FuzzyMatcher.Score("SOH", "SalesOrderHeader");
            score.Should().BeGreaterThan(0);
        }

        [Fact]
        public void CamelCaseMatch_ScoresHigherThanSubsequence()
        {
            var camelScore = FuzzyMatcher.Score("SOH", "SalesOrderHeader");
            var subseqScore = FuzzyMatcher.Score("slh", "SalesOrderHeader");
            camelScore.Should().BeGreaterThan(subseqScore);
        }

        [Fact]
        public void SubstringMatch_FindsMiddleOfString()
        {
            var score = FuzzyMatcher.Score("Order", "SalesOrderHeader");
            score.Should().BeGreaterThan(0);
        }

        [Fact]
        public void SubsequenceMatch_FindsScatteredCharacters()
        {
            var score = FuzzyMatcher.Score("SHdr", "SalesOrderHeader");
            score.Should().BeGreaterThan(0);
        }

        [Fact]
        public void NoMatch_ReturnsZero()
        {
            var score = FuzzyMatcher.Score("xyz", "Customer");
            score.Should().Be(0);
        }

        [Fact]
        public void EmptyQuery_ReturnsZero()
        {
            var score = FuzzyMatcher.Score("", "Customer");
            score.Should().Be(0);
        }

        [Fact]
        public void NullQuery_ReturnsZero()
        {
            var score = FuzzyMatcher.Score(null, "Customer");
            score.Should().Be(0);
        }

        [Fact]
        public void EmptyCandidate_ReturnsZero()
        {
            var score = FuzzyMatcher.Score("Cust", "");
            score.Should().Be(0);
        }

        [Fact]
        public void FuzzyMatch_ToleratesTypos()
        {
            // "Custmer" vs "Customer" - missing 'o'
            var score = FuzzyMatcher.Score("Custmer", "Customer");
            score.Should().BeGreaterThan(0);
        }

        [Fact]
        public void ScoreRanking_ExactBeatsPrefixBeatsSubstring()
        {
            var exact = FuzzyMatcher.Score("Product", "Product");
            var prefix = FuzzyMatcher.Score("Prod", "Product");
            var substring = FuzzyMatcher.Score("oduct", "Product");

            exact.Should().BeGreaterThan(prefix);
            prefix.Should().BeGreaterThan(substring);
        }

        [Fact]
        public void GetMatchedIndices_ExactPrefix_ReturnsFirstNIndices()
        {
            var indices = FuzzyMatcher.GetMatchedIndices("Cust", "Customer");
            indices.Should().Equal(0, 1, 2, 3);
        }

        [Fact]
        public void GetMatchedIndices_Substring_ReturnsCorrectPositions()
        {
            var indices = FuzzyMatcher.GetMatchedIndices("Order", "SalesOrderHeader");
            indices.Should().Equal(5, 6, 7, 8, 9);
        }

        [Fact]
        public void GetMatchedIndices_Subsequence_ReturnsMatchedPositions()
        {
            var indices = FuzzyMatcher.GetMatchedIndices("SHd", "SalesOrderHeader");
            indices.Should().HaveCountGreaterThan(0);
            indices.Length.Should().Be(3);
        }

        [Fact]
        public void GetMatchedIndices_NoMatch_ReturnsEmpty()
        {
            var indices = FuzzyMatcher.GetMatchedIndices("xyz", "Customer");
            indices.Should().BeEmpty();
        }

        [Theory]
        [InlineData("emp", "Employee", true)]
        [InlineData("addr", "Address", true)]
        [InlineData("sal", "SalesOrderHeader", true)]
        [InlineData("usp", "uspGetEmployeeManagers", true)]
        [InlineData("zzz", "Customer", false)]
        public void CommonSearchPatterns_MatchCorrectly(string query, string candidate, bool shouldMatch)
        {
            var score = FuzzyMatcher.Score(query, candidate);
            if (shouldMatch)
                score.Should().BeGreaterThan(0, $"'{query}' should match '{candidate}'");
            else
                score.Should().Be(0, $"'{query}' should not match '{candidate}'");
        }
    }
}
