using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.Services;

public sealed record ExerciseGenerationRequest(
    ExerciseType Type,
    CefrLevel Level,
    int ItemCount,
    string? Topic,
    string? Provider,
    string? Model,
    Guid? CourseId,
    Guid? LessonId,
    Guid[] LexicalUnitIds,
    Guid? InterfaceLanguageId = null,
    bool Persist = true);

public sealed record GenerationReport(Exercise Exercise, int Items, IReadOnlyList<string> Warnings);

public interface IExerciseGeneratorService
{
    Task<GenerationReport> GenerateAsync(ExerciseGenerationRequest request, CancellationToken ct = default);
    IReadOnlyList<ExerciseType> SupportedTypes { get; }
}

public sealed class ExerciseGeneratorService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAiGateway gateway,
    IVectorSearchService vectorSearch,
    IOptions<AiOptions> aiOptions) : IExerciseGeneratorService
{
    private readonly AiOptions _options = aiOptions.Value;

    public IReadOnlyList<ExerciseType> SupportedTypes { get; } =
    [
        ExerciseType.MultipleChoice,
        ExerciseType.TranslateToTarget,
        ExerciseType.TranslateFromTarget,
        ExerciseType.GapFill,
        ExerciseType.WordOrder,
        ExerciseType.MatchPairs,
        ExerciseType.Listening,
        ExerciseType.Writing
    ];

    public async Task<GenerationReport> GenerateAsync(ExerciseGenerationRequest request, CancellationToken ct = default)
    {
        if (!SupportedTypes.Contains(request.Type))
        {
            throw new UnsupportedExerciseTypeException(request.Type);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var languageId = request.CourseId is { } cid
            ? await db.Courses.Where(c => c.Id == cid).Select(c => (Guid?)c.LanguageId).FirstOrDefaultAsync(ct)
            : null;
        var resolvedLanguageId = languageId ?? (request.LessonId is { } lid
            ? await db.Lessons.Where(l => l.Id == lid).Select(l => (Guid?)l.Course!.LanguageId).FirstOrDefaultAsync(ct)
            : null);

        if (resolvedLanguageId is not { } languageIdValue || languageIdValue == default)
        {
            throw new InvalidOperationException("CourseId or LessonId is required to determine the target language");
        }

        var language = await db.Languages.FirstAsync(l => l.Id == languageIdValue, ct);
        var interfaceLanguage = request.InterfaceLanguageId is { } iid
            ? await db.Languages.FirstAsync(l => l.Id == iid, ct)
            : await db.Languages.OrderBy(l => l.SortOrder).ThenBy(l => l.Code).FirstAsync(ct);

        var vocabulary = new List<string>();
        var targetIds = new List<Guid>();
        if (request.LexicalUnitIds.Length > 0)
        {
            var units = await db.LexicalUnits
                .Where(u => request.LexicalUnitIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Text, u.Translation })
                .ToListAsync(ct);
            targetIds.AddRange(units.Select(u => u.Id));
            vocabulary.AddRange(units.Select(u => $"{u.Text} = {u.Translation}").Where(s => !s.EndsWith("= ")));
        }

        if (vocabulary.Count == 0)
        {
            var topic = request.Topic ?? language.NameEnglish;
            var neighbours = await vectorSearch.SearchAsync(
                topic, languageIdValue, interfaceLanguage.Id, request.Provider, 8, _options.RagMinSimilarity, ct: ct);

            if (neighbours.Count > 0)
            {
                var ids = neighbours.Select(n => n.LexicalUnitId).ToArray();
                targetIds.AddRange(ids);
                var texts = await db.LexicalUnits
                    .Where(u => ids.Contains(u.Id))
                    .Select(u => new { u.Text, u.Translation })
                    .ToListAsync(ct);
                vocabulary.AddRange(texts.Select(t => $"{t.Text} = {t.Translation}"));
            }
        }

        var itemCount = Math.Clamp(request.ItemCount, 1, 20);
        var userPrompt = PromptTemplates.ExerciseGeneratorUser(
            language.NativeName ?? language.NameEnglish,
            interfaceLanguage.NativeName ?? interfaceLanguage.NameEnglish,
            request.Level,
            request.Type,
            itemCount,
            request.Topic ?? string.Empty,
            vocabulary.Take(30).ToArray());

        var json = await gateway.CompleteJsonAsync(
            PromptTemplates.ExerciseSchema(request.Type),
            PromptTemplates.ExerciseGeneratorSystem,
            userPrompt,
            AiOperation.ExerciseGeneration,
            null,
            request.Provider,
            temperature: 0.6,
            ct: ct);

        var warnings = new List<string>();
        var payload = NormalizePayload(json, request.Type, itemCount, warnings);
        var itemCountActual = CountItems(payload, request.Type);
        if (itemCountActual == 0)
        {
            throw new AiProviderException("generator", null,
                "The model did not return any usable exercise items. Adjust the request or try another model.");
        }

        var client = gateway.ResolveChat(request.Provider);
        var exercise = new Exercise
        {
            LanguageId = languageIdValue,
            TranslationLanguageId = interfaceLanguage.Id,
            Type = request.Type,
            Level = request.Level,
            Title = ReadString(json, "title") ?? $"{request.Type} — {request.Topic ?? language.NameEnglish}",
            Instructions = ReadString(json, "instructions") ?? DefaultInstructions(request.Type, language.NameEnglish),
            Payload = payload,
            ExplanationMarkdown = ReadString(json, "explanation_markdown"),
            Topics = request.Topic is null ? null : [request.Topic],
            TargetLexicalUnitIds = targetIds.Distinct().Take(50).ToArray(),
            Points = itemCountActual * 10,
            EstimatedSeconds = itemCountActual * 30,
            Source = ContentSource.AiGenerated,
            AiProvider = client.Name,
            AiModel = string.IsNullOrWhiteSpace(request.Model) ? client.ChatModel : request.Model,
            IsActive = true,
            IsPublished = false,
            CourseId = request.CourseId,
            LessonId = request.LessonId
        };

        if (request.Persist)
        {
            db.Exercises.Add(exercise);
            await db.SaveChangesAsync(ct);
        }

        return new GenerationReport(exercise, itemCountActual, warnings);
    }

    private static JsonNode NormalizePayload(JsonNode json, ExerciseType type, int expected, List<string> warnings)
    {
        var normalized = json.DeepClone();

        switch (type)
        {
            case ExerciseType.MatchPairs:
            {
                var pairs = normalized["pairs"]?.AsArray();
                if (pairs is not { Count: > 0 })
                {
                    warnings.Add("No pairs were returned by the model.");
                    break;
                }

                var kept = new JsonArray();
                var index = 0;
                foreach (var pair in pairs)
                {
                    var left = ReadString(pair, "left");
                    var right = ReadString(pair, "right");
                    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                    {
                        continue;
                    }

                    kept.Add(new JsonObject
                    {
                        ["id"] = pair?["id"]?.GetValue<string>() ?? $"p{++index}",
                        ["left"] = left,
                        ["right"] = right
                    });
                }

                if (kept.Count > 0)
                {
                    normalized["pairs"] = kept;
                }

                break;
            }

            case ExerciseType.MultipleChoice:
            case ExerciseType.Listening:
            {
                var items = normalized["items"]?.AsArray();
                if (items is null)
                {
                    break;
                }

                var kept = new JsonArray();
                var index = 0;
                foreach (var item in items)
                {
                    var options = item?["options"]?.AsArray();
                    if (options is null || options.Count < 2)
                    {
                        continue;
                    }

                    var correct = item?["correct_index"]?.GetValue<int>() ?? -1;
                    if (correct < 0 || correct >= options.Count)
                    {
                        correct = 0;
                        warnings.Add("Item had an invalid correct_index; defaulted to 0.");
                    }

                    kept.Add(new JsonObject
                    {
                        ["id"] = item?["id"]?.GetValue<string>() ?? $"i{++index}",
                        ["question"] = ReadString(item, "question") ?? ReadString(item, "audio_text") ?? string.Empty,
                        ["question_language"] = ReadString(item, "question_language") ?? "target",
                        ["audio_text"] = ReadString(item, "audio_text"),
                        ["transcription"] = ReadString(item, "transcription"),
                        ["options"] = options.DeepClone(),
                        ["correct_index"] = correct,
                        ["explanation"] = ReadString(item, "explanation") ?? string.Empty
                    });
                }

                if (kept.Count > 0)
                {
                    normalized["items"] = kept;
                }

                break;
            }

            case ExerciseType.WordOrder:
            {
                var items = normalized["items"]?.AsArray();
                if (items is null)
                {
                    break;
                }

                var kept = new JsonArray();
                var index = 0;
                foreach (var item in items)
                {
                    var sentence = ReadString(item, "correct_sentence");
                    var words = item?["shuffled_words"]?.AsArray();
                    if (string.IsNullOrWhiteSpace(sentence) || words is not { Count: > 1 })
                    {
                        continue;
                    }

                    kept.Add(new JsonObject
                    {
                        ["id"] = item?["id"]?.GetValue<string>() ?? $"i{++index}",
                        ["shuffled_words"] = words.DeepClone(),
                        ["correct_sentence"] = sentence,
                        ["explanation"] = ReadString(item, "explanation") ?? string.Empty
                    });
                }

                if (kept.Count > 0)
                {
                    normalized["items"] = kept;
                }

                break;
            }

            default:
            {
                var items = normalized["items"]?.AsArray();
                if (items is null)
                {
                    break;
                }

                var kept = new JsonArray();
                var index = 0;
                foreach (var item in items)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    var hasAnswer = item["correct_translation"] is not null
                                    || item["correct_answer"] is not null
                                    || item["prompt"] is not null;
                    if (!hasAnswer)
                    {
                        continue;
                    }

                    item["id"] = item["id"]?.GetValue<string>() ?? $"i{++index}";
                    item["explanation"] = item["explanation"]?.GetValue<string>() ?? string.Empty;
                    kept.Add(item);
                }

                if (kept.Count > 0)
                {
                    normalized["items"] = kept;
                }

                break;
            }
        }

        _ = expected;
        return normalized;
    }

    private static int CountItems(JsonNode payload, ExerciseType type)
    {
        if (type == ExerciseType.MatchPairs)
        {
            return payload["pairs"]?.AsArray().Count ?? 0;
        }

        return payload["items"]?.AsArray().Count ?? 0;
    }

    internal static string? ReadString(JsonNode? node, string property) =>
        node?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string DefaultInstructions(ExerciseType type, string language) => type switch
    {
        ExerciseType.MultipleChoice => $"Choose the correct option. All answers relate to {language}.",
        ExerciseType.TranslateToTarget => $"Translate the sentences into {language}.",
        ExerciseType.TranslateFromTarget => $"Translate the sentences from {language} into your language.",
        ExerciseType.GapFill => "Fill the gaps with the correct word form.",
        ExerciseType.WordOrder => "Arrange the words into a correct sentence.",
        ExerciseType.MatchPairs => "Match each phrase with its translation.",
        ExerciseType.Listening => "Listen and choose the matching answer.",
        _ => "Write your answer."
    };
}

public sealed class UnsupportedExerciseTypeException(ExerciseType type)
    : Exception($"Exercise type {type} cannot be generated");
