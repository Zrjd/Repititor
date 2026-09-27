using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Tests;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Hello,   World!  ", "hello world")]
    [InlineData("  давай   учиться  ", "давай учиться")]
    [InlineData("don't stop", "dont stop")]
    [InlineData("it’s fine", "its fine")]
    public void Normalize_StripsPunctuationAndCollapsesWhitespace(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_IsCaseInsensitive()
    {
        Assert.Equal(TextNormalizer.Normalize("Wasser"), TextNormalizer.Normalize("wASSER"));
    }

    [Fact]
    public void Normalize_KeepsHyphenAndApostropheInsideWords()
    {
        Assert.Equal("well-known", TextNormalizer.Normalize("Well-known"));
    }

    [Theory]
    [InlineData("cat", "cat", 0)]
    [InlineData("cat", "cats", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("same", "same", 0)]
    public void Levenshtein_ComputesEditDistance(string a, string b, int expected)
    {
        Assert.Equal(expected, TextNormalizer.Levenshtein(a, b));
    }

    [Theory]
    [InlineData("hello world", "hello world", 1.0)]
    [InlineData("", "", 1.0)]
    [InlineData("hello", "hell", 0.8)]
    public void Similarity_IsBoundedAndSymmetricEnough(string a, string b, double expected)
    {
        Assert.Equal(expected, TextNormalizer.Similarity(a, b), 3);
    }

    [Fact]
    public void Similarity_IsZeroForCompletelyDifferentWords()
    {
        Assert.Equal(0d, TextNormalizer.Similarity("apple", "zebra"), 3);
    }

    [Fact]
    public void ContentHash_IgnoresCasingAndPunctuation()
    {
        var first = TextNormalizer.ContentHash("Hello, world!", "Привет", "Example sentence");
        var second = TextNormalizer.ContentHash("hello world", "Привет", "Example sentence");
        Assert.Equal(first, second);
    }

    [Fact]
    public void ContentHash_ChangesWhenTranslationChanges()
    {
        Assert.NotEqual(
            TextNormalizer.ContentHash("book", "книга", null),
            TextNormalizer.ContentHash("book", "том", null));
    }

    [Fact]
    public void Hash_IsStableAndHexEncoded()
    {
        var hash = TextNormalizer.Hash("stable");
        Assert.Equal(hash, TextNormalizer.Hash("stable"));
        Assert.Equal(32, hash.Length);
        Assert.All(hash, c => Assert.True(char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f')));
    }

    [Fact]
    public void Tokenize_SplitsOnWhitespaceAndRemovesEmptyEntries()
    {
        Assert.Equal(["i", "drink", "water"], TextNormalizer.Tokenize("I drink  water "));
    }

    [Fact]
    public void IsSingleWord_DetectsSingleToken()
    {
        Assert.True(TextNormalizer.IsSingleWord("Water"));
        Assert.False(TextNormalizer.IsSingleWord("drink water"));
    }

    [Fact]
    public void NormalizeSentence_RemovesSpaceBeforePunctuation()
    {
        Assert.Equal("привет, как дела?", TextNormalizer.NormalizeSentence("Привет ,  как дела ?"));
    }

    [Fact]
    public void Collapse_TrimsAndCollapses()
    {
        Assert.Equal("a b c", TextNormalizer.Collapse("  a \t b \n c  "));
    }
}
