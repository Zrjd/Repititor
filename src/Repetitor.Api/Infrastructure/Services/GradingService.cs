using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Services;

public sealed record GradingRequest(
    string Answer,
    string ReferenceAnswer,
    IReadOnlyList<string> AcceptedVariants,
    string Language,
    string InterfaceLanguage,
    CefrLevel Level,
    string Task,
    string? Provider = null,
    bool UseAi = true);

public sealed record GradingResult(
    bool IsCorrect,
    int ScorePercent,
    string CorrectedAnswer,
    string Explanation,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Hints,
    string Method,
    IReadOnlyList<JsonObject> Items)
{
    public IReadOnlyList<JsonObject> Items { get; init; } = Items ?? [];
}

public interface IAnswerGradingService
{
    Task<GradingResult> GradeAsync(GradingRequest request, Guid? userId, CancellationToken ct = default);
    Task<GradingResult> GradeExerciseAsync(Exercise exercise, JsonNode answers, Guid userId, CancellationToken ct = default);
}

public sealed class AnswerGradingService(IAiGateway gateway) : IAnswerGradingService
{
    public async Task<GradingResult> GradeAsync(GradingRequest request, Guid? userId, CancellationToken ct = default)
    {
        var answer = TextNormalizer.Collapse(request.Answer);
        if (answer.Length == 0)
        {
            return new GradingResult(false, 0, request.ReferenceAnswer, "Ответ пустой.", ["empty_answer"], ["Попробуй ещё раз"], "empty", []);
        }

        var heuristic = Heuristic(answer, request);

        if (!request.UseAi)
        {
            return heuristic;
        }

        try
        {
            var schema = new JsonObject
            {
                ["is_correct"] = "boolean",
                ["score"] = "0-100",
                ["corrected_answer"] = "string, the ideal answer",
                ["explanation"] = "string in the explanation language, 1-3 sentences",
                ["issues"] = new JsonArray("list of concrete problems found"),
                ["hints"] = new JsonArray("list of short hints for the learner")
            };

            var prompt = $"""
                Language being learned: {LanguageNames.Endonym(request.Language)} ({request.Language}).
                Learner level: CEFR {request.Level}.
                Task: {request.Task}

                Reference answer: {request.ReferenceAnswer}
                {(request.AcceptedVariants.Count > 0 ? "Also accepted: " + string.Join(" | ", request.AcceptedVariants) : string.Empty)}

                Learner answer: {answer}

                Judge the learner answer against the reference. Accept any semantically equivalent answer.
                Do not penalise punctuation or capitalisation errors. Report the issues in the explanation language.
                """;

            var json = await gateway.CompleteJsonAsync(schema, PromptTemplates.GradingSystem, prompt,
                AiOperation.AnswerGrading, userId, request.Provider, temperature: 0.1, ct: ct);

            return new GradingResult(
                json["is_correct"]?.GetValue<bool>() ?? heuristic.IsCorrect,
                Math.Clamp(json["score"]?.GetValue<int>() ?? heuristic.ScorePercent, 0, 100),
                json["corrected_answer"]?.GetValue<string>() ?? request.ReferenceAnswer,
                json["explanation"]?.GetValue<string>() ?? heuristic.Explanation,
                ReadArray(json, "issues"),
                ReadArray(json, "hints"),
                "ai",
                []);
        }
        catch (Exception ex) when (ex is AiProviderException or OperationCanceledException)
        {
            return heuristic;
        }
    }

    public async Task<GradingResult> GradeExerciseAsync(Exercise exercise, JsonNode answers, Guid userId, CancellationToken ct = default)
    {
        var payload = exercise.Payload;
        if (payload is null)
        {
            return new GradingResult(false, 0, string.Empty, "Упражнение не содержит данных.", ["invalid_exercise"], [], "invalid", []);
        }

        var items = payload["items"]?.AsArray() ?? [];
        var submitted = AnswerMap(answers);
        var results = new JsonArray();
        int correct = 0;
        var issues = new List<string>();
        var wrongIds = new List<string>();

        foreach (var item in items)
        {
            var id = item?["id"]?.GetValue<string>() ?? string.Empty;
            var given = submitted?[id]?.ToString();
            bool ok;
            string? expected = null;

            switch (exercise.Type)
            {
                case ExerciseType.MultipleChoice:
                case ExerciseType.Listening:
                {
                    var options = item?["options"]?.AsArray();
                    var correctIndex = item?["correct_index"]?.GetValue<int>() ?? -1;
                    expected = correctIndex >= 0 && options is not null && correctIndex < options.Count
                        ? options[correctIndex]?.GetValue<string>()
                        : null;

                    if (string.IsNullOrWhiteSpace(given))
                    {
                        ok = false;
                    }
                    else if (int.TryParse(given, NumberStyles.Integer, CultureInfo.InvariantCulture, out var picked))
                    {
                        ok = correctIndex >= 0 && picked == correctIndex;
                    }
                    else
                    {
                        ok = expected is not null &&
                             TextNormalizer.Normalize(given) == TextNormalizer.Normalize(expected);
                    }

                    break;
                }

                case ExerciseType.MatchPairs:
                {
                    var pairs = payload["pairs"]?.AsArray() ?? [];
                    var right = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var p in pairs)
                    {
                        var pid = p?["id"]?.GetValue<string>();
                        var left = p?["left"]?.GetValue<string>();
                        if (pid is not null && left is not null)
                        {
                            right[pid] = left;
                        }
                    }

                    var givenMap = AnswerMap(answers?["pairs"] is JsonObject ? answers["pairs"] : answers);
                    var matched = 0;
                    foreach (var (pid, left) in right)
                    {
                        if (givenMap is not null &&
                            TextNormalizer.Normalize(givenMap[pid]?.ToString() ?? string.Empty) == TextNormalizer.Normalize(left))
                        {
                            matched++;
                        }
                    }

                    ok = matched == right.Count && right.Count > 0;
                    correct += matched;
                    break;
                }

                case ExerciseType.GapFill:
                case ExerciseType.FillInTheBlanks:
                {
                    expected = item?["correct_answer"]?.GetValue<string>();
                    ok = Matches(given, expected, item?["accepted_variants"]?.AsArray());
                    break;
                }

                case ExerciseType.WordOrder:
                {
                    expected = item?["correct_sentence"]?.GetValue<string>();
                    ok = TextNormalizer.Normalize(given ?? string.Empty) == TextNormalizer.Normalize(expected ?? string.Empty);
                    break;
                }

                case ExerciseType.TranslateToTarget:
                case ExerciseType.TranslateFromTarget:
                {
                    expected = item?["correct_translation"]?.GetValue<string>();
                    ok = Matches(given, expected, item?["accepted_variants"]?.AsArray());
                    break;
                }

                default:
                    ok = !string.IsNullOrWhiteSpace(given);
                    expected = item?["reference_answer"]?.GetValue<string>();
                    break;
            }

            if (ok)
            {
                correct++;
            }
            else
            {
                wrongIds.Add(id);
                var explanation = item?["explanation"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(explanation))
                {
                    issues.Add(explanation);
                }
            }

            results.Add(new JsonObject
            {
                ["id"] = id,
                ["correct"] = ok,
                ["expected"] = expected,
                ["given"] = given,
                ["explanation"] = item?["explanation"]?.GetValue<string>()
            });
        }

        var total = items.Count == 0 ? 1 : items.Count;
        var percent = (int)Math.Round(correct * 100d / total);

        string? aiFeedback = null;
        var writtenAnswers = answers?["free_text"]?.AsArray();
        if (writtenAnswers is { Count: > 0 })
        {
            var text = string.Join("\n", writtenAnswers.Select(x => x?.ToString() ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(text))
            {
                var grading = await GradeAsync(new GradingRequest(
                    text,
                    payload["reference_answer"]?.GetValue<string>() ?? string.Empty,
                    [],
                    exercise.Language?.Code ?? "en",
                    "ru",
                    exercise.Level,
                    payload["prompt"]?.GetValue<string>() ?? "Write a short response.",
                    UseAi: true), userId, ct);
                aiFeedback = grading.Explanation;
            }
        }

        return new GradingResult(
            percent >= 70,
            percent,
            string.Empty,
            aiFeedback ?? (issues.Count > 0 ? string.Join("\n", issues) : "Все ответы верны."),
            issues,
            [],
            "rule_based",
            results.Select(r => r! as JsonObject ?? new JsonObject()).ToArray());
    }

    private static GradingResult Heuristic(string answer, GradingRequest request)
    {
        var candidates = new List<string> { request.ReferenceAnswer };
        candidates.AddRange(request.AcceptedVariants);

        var best = candidates
            .Select(c => (Candidate: c, Score: TextNormalizer.Similarity(answer, c)))
            .OrderByDescending(x => x.Score)
            .First();

        var isCorrect = best.Score >= 0.92 || candidates.Any(c => TextNormalizer.Normalize(answer) == TextNormalizer.Normalize(c));
        var score = (int)Math.Round(best.Score * 100);

        return new GradingResult(
            isCorrect,
            isCorrect ? 100 : score,
            best.Candidate,
            isCorrect
                ? "Ответ верный."
                : $"Ожидалось: {best.Candidate}. Близко, но есть отличия.",
            isCorrect ? [] : ["approximate_answer"],
            isCorrect ? [] : ["Проверь порядок слов и окончания."],
            "heuristic",
            []);
    }

    private static JsonObject? AnswerMap(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return obj["answers"] as JsonObject ?? obj;
    }

    private static bool Matches(string? given, string? expected, JsonArray? variants)
    {
        if (given is null || expected is null)
        {
            return false;
        }

        if (TextNormalizer.Normalize(given) == TextNormalizer.Normalize(expected))
        {
            return true;
        }

        if (variants is not null)
        {
            foreach (var v in variants)
            {
                var value = v?.GetValue<string>();
                if (value is not null && TextNormalizer.Normalize(given) == TextNormalizer.Normalize(value))
                {
                    return true;
                }
            }
        }

        return TextNormalizer.Similarity(given, expected) >= 0.94;
    }

    private static IReadOnlyList<string> ReadArray(JsonNode? node, string property) =>
        node?[property]?.AsArray()
            .Select(x => x?.GetValue<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray() ?? [];
}

public sealed record PronunciationRequest(
    string TargetText,
    string Transcript,
    string Language,
    IReadOnlyList<AiTranscriptSegment>? Segments = null,
    string? ReferenceTranscription = null,
    string? Provider = null,
    bool UseAi = true);

public sealed record PronunciationResult(
    int OverallScore,
    int AccuracyScore,
    int FluencyScore,
    int CompletenessScore,
    int ProsodyScore,
    string RecognizedText,
    string Feedback,
    IReadOnlyList<WordScore> Words,
    string Method);

public sealed record WordScore(string Word, string Expected, string Recognized, int Score, string? Hint);

public interface IPronunciationService
{
    Task<PronunciationResult> AssessAsync(PronunciationRequest request, Guid? userId, CancellationToken ct = default);
}

public sealed class PronunciationService(IAiGateway gateway) : IPronunciationService
{
    public async Task<PronunciationResult> AssessAsync(PronunciationRequest request, Guid? userId, CancellationToken ct = default)
    {
        var heuristic = ScoreHeuristically(request);

        if (!request.UseAi)
        {
            return heuristic;
        }

        try
        {
            var schema = new JsonObject
            {
                ["overall_score"] = "0-100",
                ["accuracy_score"] = "0-100",
                ["fluency_score"] = "0-100",
                ["completeness_score"] = "0-100",
                ["prosody_score"] = "0-100",
                ["recognized_text"] = "string, what you believe the learner said",
                ["feedback"] = "string in the explanation language, actionable advice",
                ["words"] = new JsonArray(new JsonObject
                {
                    ["word"] = "string",
                    ["expected"] = "string",
                    ["recognized"] = "string",
                    ["score"] = "0-100",
                    ["hint"] = "string or empty"
                })
            };

            var timings = request.Segments is { Count: > 0 }
                ? string.Join(", ", request.Segments.Select(s => $"{s.Start:0.00}-{s.End:0.00}"))
                : "not available";

            var prompt = $"""
                Target language: {LanguageNames.Endonym(request.Language)} ({request.Language}).
                Target phrase: {request.TargetText}
                Reference phonetic transcription: {(request.ReferenceTranscription ?? "not provided")}
                Speech-to-text transcript of the learner: {request.Transcript}
                Segment timings (seconds): {timings}

                Estimate accuracy mainly from phonetic similarity of the transcript to the target phrase,
                completeness from missing or extra words, fluency from the number and length of pauses,
                and prosody from the flow visible in the timings. Use Russian for feedback.
                """;

            var json = await gateway.CompleteJsonAsync(schema, PromptTemplates.PronunciationSystem, prompt,
                AiOperation.PronunciationAssessment, userId, request.Provider, temperature: 0.2, ct: ct);

            var words = (json["words"]?.AsArray() ?? [])
                .Select(w => new WordScore(
                    w?["word"]?.GetValue<string>() ?? string.Empty,
                    w?["expected"]?.GetValue<string>() ?? string.Empty,
                    w?["recognized"]?.GetValue<string>() ?? string.Empty,
                    Math.Clamp(w?["score"]?.GetValue<int>() ?? 0, 0, 100),
                    string.IsNullOrWhiteSpace(w?["hint"]?.GetValue<string>()) ? null : w?["hint"]?.GetValue<string>()))
                .ToArray();

            return new PronunciationResult(
                Clamp(json["overall_score"]),
                Clamp(json["accuracy_score"]),
                Clamp(json["fluency_score"]),
                Clamp(json["completeness_score"]),
                Clamp(json["prosody_score"]),
                json["recognized_text"]?.GetValue<string>() ?? request.Transcript,
                json["feedback"]?.GetValue<string>() ?? heuristic.Feedback,
                words,
                "ai");
        }
        catch (Exception ex) when (ex is AiProviderException or OperationCanceledException)
        {
            return heuristic;
        }
    }

    private static PronunciationResult ScoreHeuristically(PronunciationRequest request)
    {
        var expected = TextNormalizer.Tokenize(request.TargetText);
        var actual = TextNormalizer.Tokenize(request.Transcript);
        var accuracy = (int)Math.Round(TextNormalizer.Similarity(request.TargetText, request.Transcript) * 100);
        var completeness = expected.Count == 0
            ? 0
            : (int)Math.Round(Math.Min(1d, (double)expected.Count(s => actual.Contains(s)) / expected.Count) * 100);

        var fluency = 80;
        if (request.Segments is { Count: > 1 })
        {
            var gaps = request.Segments.Zip(request.Segments.Skip(1))
                .Where(p => p.Second.Start - p.First.End > 1.0)
                .Count();
            fluency = Math.Max(30, 100 - gaps * 15);
        }

        var words = expected.Zip(actual.Count > 0 ? actual : expected, (e, a) =>
        {
            var score = (int)Math.Round(TextNormalizer.Similarity(e, a) * 100);
            var hint = score >= 85 ? null : $"Повторите звуки в слове «{e}».";
            return new WordScore(e, e, a, score, hint);
        }).ToArray();

        var overall = (int)Math.Round(accuracy * 0.5 + completeness * 0.3 + fluency * 0.2);

        return new PronunciationResult(
            overall, accuracy, fluency, completeness, 70,
            request.Transcript,
            completeness < 60
                ? "Похоже, часть фразы не прозвучала. Попробуйте прочитать целиком, не спеша."
                : accuracy < 60
                    ? "Слова узнаются, но звуки пока неточные. Слушайте образец и повторяйте по слогам."
                    : "Хорошее произношение. Обратите внимание на интонацию.",
            words,
            "heuristic");
    }

    private static int Clamp(JsonNode? node) => Math.Clamp(node?.GetValue<int>() ?? 0, 0, 100);
}
