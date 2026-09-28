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

    /// <summary>
    /// Схлопывает все последовательности пробельных символов в один пробел и убирает пробелы по краям.
    /// Используется для приведения пользовательского ввода к единому формату перед сохранением или сравнением.
    /// </summary>
    public static string Collapse(string value) => WhitespaceRegex.Replace(value, " ").Trim();

    /// <summary>
    /// Приводит текст к нижнему регистру, удаляет пунктуацию и лишние пробелы.
    /// Необходимо для корректного сравнения строк, вычисления хешей и поиска дубликатов.
    /// </summary>
    public static string Normalize(string value)
    {
        var lowered = value.Trim().ToLower(CultureInfo.InvariantCulture);
        lowered = PunctuationRegex.Replace(lowered, " ");
        lowered = SpaceApostropheRegex.Replace(lowered, string.Empty);
        return Collapse(lowered);
    }
    /// <summary>
    /// Нормализует предложение: приводит к нижнему регистру и убирает пробелы перед знаками препинания.
    /// Используется для подготовки текста к сравнению с эталонными ответами в упражнениях.
    /// </summary>
    public static string NormalizeSentence(string value)
    {
        var text = Collapse(value).ToLower(CultureInfo.InvariantCulture);
        text = SpaceBeforePunctuationRegex.Replace(text, "$1");
        return text.Trim();
    }

    /// <summary>
    /// Вычисляет короткий SHA-256 хеш нормализованного текста.
    /// Позволяет быстро сравнивать тексты на идентичность без хранения исходных строк.
    /// </summary>
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(value))))[..32].ToLowerInvariant();

    /// <summary>
    /// Вычисляет хеш на основе текста, перевода и примера использования.
    /// Позволяет отслеживать изменения содержимого карточки и определять, нужно ли обновлять связанные данные.
    /// </summary>
    public static string ContentHash(string text, string? translation, string? example) =>
        Hash(string.Join('|', text, translation ?? string.Empty, example ?? string.Empty));

    /// <summary>
    /// Вычисляет расстояние Левенштейна между двумя строками — минимальное количество операций вставки, удаления и замены символов.
    /// Используется для оценки близости ответа пользователя к эталону и поиска опечаток.
    /// </summary>
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

    /// <summary>
    /// Возвращает коэффициент сходства двух строк от 0 до 1 на основе расстояния Левенштейна.
    /// Значение 1 означает полное совпадение, 0 — полное отличие. Применяется для нечёткого сравнения текстов.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        var max = Math.Max(Normalize(a).Length, Normalize(b).Length);
        return max == 0 ? 1d : 1d - (double)Levenshtein(a, b) / max;
    }

    /// <summary>
    /// Разбивает нормализованный текст на отдельные слова (токены).
    /// Используется для анализа текста, подсчёта слов и построения индексов для поиска.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Объединяет последовательность токенов обратно в строку через пробел.
    /// Является обратной операцией к Tokenize и применяется при нормализации текста.
    /// </summary>
    public static string JoinTokens(IEnumerable<string> tokens) => string.Join(" ", tokens);

    /// <summary>
    /// Проверяет, состоит ли текст ровно из одного слова.
    /// Используется для валидации ввода и определения типа обучающей карточки.
    /// </summary>
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

    /// <summary>
    /// Возвращает название языка на самом языке (endonym) по его коду ISO.
    /// Например, для кода "en" вернёт "English". Если язык не найден, возвращается исходный код.
    /// </summary>
    public static string Endonym(string code) =>
        Endonyms.TryGetValue(code, out var name) ? name : code;

    /// <summary>
    /// Возвращает название языка для отображения в учебных подсказках и инструкциях.
    /// Синоним Endonym, существует для удобства семантического именования в контексте обучения.
    /// </summary>
    public static string Instruction(string code) => Endonym(code);
}
