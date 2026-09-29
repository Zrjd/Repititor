using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.Services;

/// <summary>
/// Системный промпт для генерации урока. Пользовательский промпт преподавателя
/// дополняет контракт ответа, но не заменяет его: без схемы JSON небольшая модель
/// возвращает свободный текст и уходит в чужой язык.
/// </summary>
public static class LessonPrompts
{
    public static string BuildSystem(
        string? customPrompt,
        string targetLanguageEnglish,
        Language interfaceLanguage,
        CefrLevel level)
    {
        var contract = $$"""
            You are an expert {{targetLanguageEnglish}} course author. Write a full lesson for CEFR {{level}} learners.
            The learner's interface language is {{interfaceLanguage.NameEnglish}}.
            Return a single valid JSON object with this exact shape:
            {
              "title": "lesson title in {{targetLanguageEnglish}}",
              "summary": "one-sentence summary in {{interfaceLanguage.NameEnglish}}",
              "content_markdown": "lesson body in {{targetLanguageEnglish}} using markdown: ## sections, examples, dialogues, grammar notes",
              "key_vocabulary": ["5-8 words or short phrases from the lesson"]
            }
            Rules: content must be accurate, level-appropriate, and pedagogically structured.
            Write the lesson about the topic from the last line of the user request and about nothing else.
            The field "title" must name that topic; the rest of the lesson must stay on it.
            The field "summary" must be written in {{interfaceLanguage.NameEnglish}} ({{interfaceLanguage.NativeName}}) only. Never write it in another language.
            """;

        if (string.IsNullOrWhiteSpace(customPrompt))
        {
            return contract;
        }

        return $$"""
            {{customPrompt.Trim()}}

            Additional instructions from the teacher are above. The answer must still follow this contract:
            {{contract}}
            """;
    }

    /// <summary>
    /// Тема идёт последней строкой и в императиве: небольшие модели читают конец запроса
    /// как приоритетное и иначе подменяют тему «своим» типовым уроком.
    /// </summary>
    public static string BuildUser(
        string courseTitle,
        string? topic,
        CefrLevel level,
        int? durationMinutes,
        string? summary,
        string? requirements)
    {
        var lines = new List<string> { $"Course: {courseTitle}", $"Level: CEFR {level}" };

        if (durationMinutes is > 0)
        {
            lines.Add($"Duration: {durationMinutes} minutes");
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            lines.Add($"Lesson summary: {summary.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(requirements))
        {
            lines.Add($"Additional requirements: {requirements.Trim()}");
        }

        lines.Add(string.IsNullOrWhiteSpace(topic)
            ? "Lesson topic: choose one fitting topic for this course and level."
            : $"Lesson topic (mandatory, do not change, do not replace with another theme): {topic.Trim()}");

        return string.Join('\n', lines);
    }
}

public sealed record LessonContentGeneration(
    string Title,
    string? Summary,
    string ContentMarkdown,
    string[] KeyVocabulary,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens);

public sealed record CourseContentGeneration(
    string Description,
    string[] LessonTitles,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens);

public interface IContentGenerationService
{
    /// <summary>
    /// Генерирует содержимое урока с помощью ИИ.
    /// </summary>
    Task<LessonContentGeneration> GenerateLessonAsync(
        Guid courseId,
        string? topic,
        CefrLevel? level,
        string? requirements,
        string? provider,
        string? model,
        string? lessonPrompt = null,
        int? durationMinutes = null,
        string? summary = null,
        CancellationToken ct = default);

    /// <summary>
    /// Генерирует структуру курса с помощью ИИ.
    /// </summary>
    Task<CourseContentGeneration> GenerateCourseAsync(
        Guid languageId,
        string? topic,
        CefrLevel? level,
        int lessonsCount,
        string? provider,
        string? model,
        CancellationToken ct = default);
}

public sealed class ContentGenerationService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAiGateway gateway,
    IOptions<AiOptions> aiOptions,
    ILogger<ContentGenerationService> logger) : IContentGenerationService
{
    private readonly AiOptions _options = aiOptions.Value;

    /// <summary>
    /// Создаёт полное содержимое урока: заголовок, краткое описание, текст и ключевую лексику.
    /// Учитывает язык и уровень курса, а также пожелания по теме и длительности.
    /// </summary>
    public async Task<LessonContentGeneration> GenerateLessonAsync(
        Guid courseId,
        string? topic,
        CefrLevel? level,
        string? requirements,
        string? provider,
        string? model,
        string? lessonPrompt = null,
        int? durationMinutes = null,
        string? summary = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var course = await db.Courses.Include(c => c.Language).FirstOrDefaultAsync(c => c.Id == courseId, ct)
            ?? throw new InvalidOperationException("Course not found");

        var target = course.Language!;
        var interfaceLang = await db.Languages.OrderBy(l => l.SortOrder).ThenBy(l => l.Code).FirstAsync(ct);
        var lessonLevel = level ?? course.Level;

        var system = LessonPrompts.BuildSystem(lessonPrompt, target.NameEnglish, interfaceLang, lessonLevel);

        var user = LessonPrompts.BuildUser(course.Title, topic, lessonLevel, durationMinutes, summary, requirements);

        var (result, summaryText) = await EnsureLanguageAsync(
            new JsonObject
            {
                ["title"] = "string",
                ["summary"] = "string",
                ["content_markdown"] = "string (markdown)",
                ["key_vocabulary"] = new JsonArray("string")
            },
            system,
            user,
            "summary",
            interfaceLang,
            provider,
            ct);

        var json = result.Json;
        return new LessonContentGeneration(
            ReadString(json, "title") ?? $"{target.NameEnglish} lesson",
            summaryText.Length > 0 ? summaryText : null,
            ReadString(json, "content_markdown") ?? string.Empty,
            ReadStringArray(json, "key_vocabulary"),
            gateway.ResolveChat(provider).Name,
            string.IsNullOrWhiteSpace(model) ? gateway.ResolveChat(provider).ChatModel : model,
            result.InputTokens,
            result.OutputTokens);
    }

    /// <summary>
    /// Создаёт структуру курса: описание и список заголовков уроков с нарастающей сложностью.
    /// Заголовки уроков формируются на изучаемом языке, описание — на языке интерфейса.
    /// </summary>
    public async Task<CourseContentGeneration> GenerateCourseAsync(
        Guid languageId,
        string? topic,
        CefrLevel? level,
        int lessonsCount,
        string? provider,
        string? model,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var language = await db.Languages.FirstOrDefaultAsync(l => l.Id == languageId, ct)
            ?? throw new InvalidOperationException("Language not found");
        var interfaceLang = await db.Languages.OrderBy(l => l.SortOrder).ThenBy(l => l.Code).FirstAsync(ct);
        var courseLevel = level ?? CefrLevel.A1;

        var system = $$"""
            You are an expert {{language.NameEnglish}} course designer. Design a course outline for CEFR {{courseLevel}} learners.
            Return a single valid JSON object with this exact shape:
            {
              "description": "course description in {{interfaceLang.NameEnglish}}",
              "lesson_titles": ["{{lessonsCount}} lesson titles in {{language.NameEnglish}}, progressing from simple to complex"]
            }
            Rules: titles must be specific, level-appropriate, and form a coherent learning path.
            The field "description" must be written in {{interfaceLang.NameEnglish}} ({{interfaceLang.NativeName}}) only. Never write it in another language.
            """;

        var user = $"""
            Language: {language.NameEnglish}
            Topic: {topic ?? "choose a fitting theme"}
            Level: CEFR {courseLevel}
            Number of lessons: {lessonsCount}
            """;

        var (result, description) = await EnsureLanguageAsync(
            new JsonObject
            {
                ["description"] = "string",
                ["lesson_titles"] = new JsonArray("string")
            },
            system,
            user,
            "description",
            interfaceLang,
            provider,
            ct);

        var json = result.Json;
        return new CourseContentGeneration(
            description,
            ReadStringArray(json, "lesson_titles"),
            gateway.ResolveChat(provider).Name,
            string.IsNullOrWhiteSpace(model) ? gateway.ResolveChat(provider).ChatModel : model,
            result.InputTokens,
            result.OutputTokens);
    }

    /// <summary>
    /// Вызывает модель и проверяет, что указанное поле ответа написано на языке интерфейса.
    /// Если модель ушла в другой язык (частая беда небольших моделей), запрос повторяется
    /// с явным указанием нужного языка, а токены обеих попыток суммируются.
    /// </summary>
    private async Task<(AiJsonResult Result, string Text)> EnsureLanguageAsync(
        JsonObject schema,
        string system,
        string user,
        string field,
        Language interfaceLang,
        string? provider,
        CancellationToken ct)
    {
        var result = await gateway.CompleteJsonWithUsageAsync(
            schema, system, user, AiOperation.ChatCompletion, null, provider, temperature: 0.7, ct: ct);
        var text = ReadString(result.Json, field) ?? string.Empty;

        if (AiScript.MatchesLanguage(text, interfaceLang.Code))
        {
            return (result, text);
        }

        var detected = AiScript.Detect(text);
        logger.LogWarning(
            "AI returned field {Field} in {Detected} script instead of {Expected}; retrying",
            field, detected, interfaceLang.Code);

        var correction = $"""
            {user}

            The previous attempt wrote "{field}" in {AiScript.DisplayName(detected)} script. That is wrong.
            Rewrite the whole JSON object so that "{field}" is written in {interfaceLang.NameEnglish} ({interfaceLang.NativeName}) only.
            """;

        var retry = await gateway.CompleteJsonWithUsageAsync(
            schema, system, correction, AiOperation.ChatCompletion, null, provider, temperature: 0.3, ct: ct);
        var retryText = ReadString(retry.Json, field) ?? string.Empty;

        if (AiScript.MatchesLanguage(retryText, interfaceLang.Code))
        {
            logger.LogInformation("Field {Field} is in {Expected} script after retry", field, interfaceLang.Code);
            return (retry, retryText);
        }

        // Модель снова ушла не туда: возвращаем первый вариант, но честно учитываем обе попытки.
        logger.LogWarning("Field {Field} is still not in {Expected} script after retry; keeping first answer", field, interfaceLang.Code);
        return (new AiJsonResult(result.Json, result.InputTokens + retry.InputTokens, result.OutputTokens + retry.OutputTokens), text);
    }

    private static string? ReadString(JsonNode json, string key) =>
        json[key]?.GetValue<string>();

    private static string[] ReadStringArray(JsonNode json, string key) =>
        json[key]?.AsArray().Select(n => n?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToArray() ?? [];
}
