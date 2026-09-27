using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Repetitor.Api.Infrastructure.Services;

public static partial class TextNormalizer
{
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex { get; }

    [GeneratedRegex(@"[^\p{L}\p{N}\s'’-]", RegexOptions.Compiled)]
    private static partial Regex PunctuationRegex { get; }

    [GeneratedRegex(@"['’]", RegexOptions.Compiled)]
    private static partial Regex SpaceApostropheRegex { get; }

    [GeneratedRegex(@"\s+([,.;:!?])", RegexOptions.Compiled)]
    private static partial Regex SpaceBeforePunctuationRegex { get; }

    public static string Collapse(string value) => WhitespaceRegex.Replace(value, " ").Trim();

    public static string Normalize(string value)
    {
        var lowered = value.Trim().ToLower(CultureInfo.InvariantCulture);
        lowered = PunctuationRegex.Replace(lowered, " ");
        lowered = SpaceApostropheRegex.Replace(lowered, string.Empty);
        return Collapse(lowered);
    }
    public static string NormalizeSentence(string value)
    {
        var text = Collapse(value).ToLower(CultureInfo.InvariantCulture);
        text = SpaceBeforePunctuationRegex.Replace(text, "$1");
        return text.Trim();
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(value))))[..32].ToLowerInvariant();

    public static string ContentHash(string text, string? translation, string? example) =>
        Hash(string.Join('|', text, translation ?? string.Empty, example ?? string.Empty));

    public static int Levenshtein(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    public static double Similarity(string a, string b)
    {
        var max = Math.Max(Normalize(a).Length, Normalize(b).Length);
        return max == 0 ? 1d : 1d - (double)Levenshtein(a, b) / max;
    }

    public static IReadOnlyList<string> Tokenize(string value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string JoinTokens(IEnumerable<string> tokens) => string.Join(" ", tokens);

    public static bool IsSingleWord(string value) => Tokenize(value).Count == 1;
}

public static class LanguageNames
{
    private static readonly Dictionary<string, string> Endonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "русский",
        ["en"] = "English",
        ["de"] = "Deutsch",
        ["fr"] = "français",
        ["es"] = "español",
        ["it"] = "italiano",
        ["pt"] = "português",
        ["pl"] = "polski",
        ["tr"] = "Türkçe",
        ["uk"] = "українська",
        ["zh"] = "中文",
        ["ja"] = "日本語",
        ["ko"] = "한국어",
        ["ar"] = "العربية",
        ["cs"] = "čeština",
        ["nl"] = "Nederlands",
        ["sv"] = "svenska",
        ["no"] = "norsk",
        ["fi"] = "suomi",
        ["da"] = "dansk",
        ["el"] = "ελληνικά",
        ["he"] = "עברית",
        ["hi"] = "हिन्दी",
        ["id"] = "Bahasa Indonesia",
        ["ro"] = "română",
        ["vi"] = "Tiếng Việt"
    };

    public static string Endonym(string code) =>
        Endonyms.TryGetValue(code, out var name) ? name : code;

    public static string Instruction(string code) => Endonym(code);
}
