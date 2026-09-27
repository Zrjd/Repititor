using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Persistence;

namespace Repetitor.Api.Infrastructure.Services;

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
    IOptions<AiOptions> aiOptions) : IContentGenerationService
{
    private readonly AiOptions _options = aiOptions.Value;

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

        var system = lessonPrompt ?? $$"""
            You are an expert {{target.NameEnglish}} course author. Write a full lesson for CEFR {{lessonLevel}} learners.
            The learner's interface language is {{interfaceLang.NameEnglish}}.
            Return a single valid JSON object with this exact shape:
            {
              "title": "lesson title in {{target.NameEnglish}}",
              "summary": "one-sentence summary in {{interfaceLang.NameEnglish}}",
              "content_markdown": "lesson body in {{target.NameEnglish}} using markdown: ## sections, examples, dialogues, grammar notes",
              "key_vocabulary": ["5-8 words or short phrases from the lesson"]
            }
            Rules: content must be accurate, level-appropriate, and pedagogically structured.
            """;

        var user = $"""
            Course: {course.Title}
            Topic: {topic ?? "choose a fitting topic for this course and level"}
            Level: CEFR {lessonLevel}
            {(durationMinutes is > 0 ? $"Duration: {durationMinutes} minutes" : "")}
            {(string.IsNullOrWhiteSpace(summary) ? "" : $"Summary: {summary}")}
            {(string.IsNullOrWhiteSpace(requirements) ? "" : $"Requirements: {requirements}")}
            """;

        var json = await gateway.CompleteJsonAsync(
            new JsonObject
            {
                ["title"] = "string",
                ["summary"] = "string",
                ["content_markdown"] = "string (markdown)",
                ["key_vocabulary"] = new JsonArray("string")
            },
            system,
            user,
            AiOperation.ChatCompletion,
            null,
            provider,
            temperature: 0.7,
            ct: ct);

        return new LessonContentGeneration(
            ReadString(json, "title") ?? $"{target.NameEnglish} lesson",
            ReadString(json, "summary"),
            ReadString(json, "content_markdown") ?? string.Empty,
            ReadStringArray(json, "key_vocabulary"),
            gateway.ResolveChat(provider).Name,
            string.IsNullOrWhiteSpace(model) ? gateway.ResolveChat(provider).ChatModel : model,
            0, 0);
    }

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
            """;

        var user = $"""
            Language: {language.NameEnglish}
            Topic: {topic ?? "choose a fitting theme"}
            Level: CEFR {courseLevel}
            Number of lessons: {lessonsCount}
            """;

        var json = await gateway.CompleteJsonAsync(
            new JsonObject
            {
                ["description"] = "string",
                ["lesson_titles"] = new JsonArray("string")
            },
            system,
            user,
            AiOperation.ChatCompletion,
            null,
            provider,
            temperature: 0.7,
            ct: ct);

        return new CourseContentGeneration(
            ReadString(json, "description") ?? string.Empty,
            ReadStringArray(json, "lesson_titles"),
            gateway.ResolveChat(provider).Name,
            string.IsNullOrWhiteSpace(model) ? gateway.ResolveChat(provider).ChatModel : model,
            0, 0);
    }

    private static string? ReadString(JsonNode json, string key) =>
        json[key]?.GetValue<string>();

    private static string[] ReadStringArray(JsonNode json, string key) =>
        json[key]?.AsArray().Select(n => n?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToArray() ?? [];
}
