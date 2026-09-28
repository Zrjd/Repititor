using Repetitor.Api.Infrastructure.Ai;

namespace Repetitor.Api.Infrastructure.Services;

/// <summary>
/// Определяет письменность текста, чтобы проверять, что модель ответила на языке интерфейса.
/// Небольшие модели иногда уходят в другой язык (чаще всего в китайский) вопреки инструкции
/// в промпте, поэтому результат проверяется и при необходимости генерируется заново.
/// </summary>
internal static class AiScript
{
    public const string Latin = "Latin";
    public const string Cyrillic = "Cyrillic";
    public const string Greek = "Greek";
    public const string Hebrew = "Hebrew";
    public const string Arabic = "Arabic";
    public const string Devanagari = "Devanagari";
    public const string Thai = "Thai";
    public const string Han = "Han";
    public const string Kana = "Kana";
    public const string Hangul = "Hangul";
    public const string Unknown = "Unknown";

    /// <summary>Сколько букв нужно, чтобы вообще судить о языке.</summary>
    private const int MinLetters = 3;

    /// <summary>
    /// Доля букв ожидаемой письменности, начиная с которой язык считается подходящим.
    /// Порог в половину намеренно мягкий: в учебных текстах нормально встречаются
    /// отдельные слова на изучаемом языке, а ловить нужно полный уход модели в другой язык.
    /// </summary>
    private const double MinShare = 0.5;

    /// <summary>
    /// Возвращает название письменности, по которой в тексте больше всего букв.
    /// </summary>
    public static string Detect(string? text)
    {
        var counts = CountByScript(text);
        if (counts.Count == 0)
        {
            return Unknown;
        }

        var best = Unknown;
        var bestCount = 0;
        foreach (var pair in counts)
        {
            if (pair.Value > bestCount)
            {
                best = pair.Key;
                bestCount = pair.Value;
            }
        }

        return best;
    }

    /// <summary>
    /// Проверяет, что текст написан на письменности, характерной для языка с указанным кодом.
    /// Короткие тексты (меньше трёх букв) и пустые строки считаются подходящими:
    /// определить по ним язык нельзя, а ложное срабатывание хуже пропуска.
    /// </summary>
    public static bool MatchesLanguage(string? text, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(languageCode))
        {
            return true;
        }

        var counts = CountByScript(text);
        var total = counts.Values.Sum();
        if (total < MinLetters)
        {
            return true;
        }

        var expected = ExpectedScripts(languageCode.Trim().ToLowerInvariant());
        var matching = expected.Sum(script => counts.GetValueOrDefault(script));
        return (double)matching / total >= MinShare;
    }

    /// <summary>
    /// Даёт человекочитаемое название письменности, чтобы указать модели, на каком языке
    /// был её предыдущий ответ.
    /// </summary>
    public static string DisplayName(string script) => script switch
    {
        Han => "Chinese",
        Kana => "Japanese kana",
        Hangul => "Korean",
        Devanagari => "Hindi",
        Cyrillic => "Cyrillic",
        Greek => "Greek",
        Hebrew => "Hebrew",
        Arabic => "Arabic",
        Thai => "Thai",
        Latin => "Latin",
        _ => "another language"
    };

    private static string[] ExpectedScripts(string code) => code switch
    {
        "ru" or "uk" or "be" or "bg" or "sr" or "mk" => [Cyrillic],
        "el" => [Greek],
        "he" => [Hebrew],
        "ar" or "fa" or "ur" or "ps" => [Arabic],
        "hi" or "mr" or "ne" => [Devanagari],
        "th" => [Thai],
        "ko" => [Hangul],
        "zh" => [Han],
        "ja" => [Han, Kana],
        _ => [Latin]
    };

    private static Dictionary<string, int> CountByScript(string? text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return counts;
        }

        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }

            var script = ScriptOf(ch);
            counts[script] = counts.GetValueOrDefault(script) + 1;
        }

        return counts;
    }

    private static string ScriptOf(char ch)
    {
        return ch switch
        {
            >= '\u0000' and <= '\u024F' => Latin,
            >= '\u0370' and <= '\u052F' => ch <= '\u03FF' ? Greek : Cyrillic,
            >= '\u0590' and <= '\u05FF' => Hebrew,
            >= '\u0600' and <= '\u06FF' => Arabic,
            >= '\u0750' and <= '\u077F' => Arabic,
            >= '\u0900' and <= '\u097F' => Devanagari,
            >= '\u0E00' and <= '\u0E7F' => Thai,
            >= '\u1100' and <= '\u11FF' or >= '\uAC00' and <= '\uD7AF' => Hangul,
            >= '\u3040' and <= '\u30FF' => Kana,
            >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF' => Han,
            >= '\u1E00' and <= '\u1EFF' => Latin,
            _ => Unknown
        };
    }
}
