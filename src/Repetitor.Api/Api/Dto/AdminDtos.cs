using System.ComponentModel.DataAnnotations;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Api.Dto;

public sealed record AdminCourseResponse(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    string Level,
    Guid LanguageId,
    string LanguageCode,
    string? CoverUrl,
    string? AccentColor,
    int EstimatedMinutes,
    bool IsPublished,
    int SortOrder,
    int LessonsCount,
    DateTimeOffset CreatedAt);

public sealed class CreateCourseRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Slug { get; set; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 2)] public string Title { get; set; } = string.Empty;
    [StringLength(2000)] public string? Description { get; set; }
    public CefrLevel Level { get; set; } = CefrLevel.A1;
    [Required] public Guid LanguageId { get; set; }
    [StringLength(512)] public string? CoverUrl { get; set; }
    [StringLength(16)] public string? AccentColor { get; set; }
    [Range(1, 10000)] public int EstimatedMinutes { get; set; } = 60;
    public bool IsPublished { get; set; } = true;
    [Range(0, 10000)] public int SortOrder { get; set; }
}

public sealed class UpdateCourseRequest
{
    [StringLength(120, MinimumLength = 2)] public string? Slug { get; set; }
    [StringLength(200, MinimumLength = 2)] public string? Title { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    public CefrLevel? Level { get; set; }
    public Guid? LanguageId { get; set; }
    [StringLength(512)] public string? CoverUrl { get; set; }
    [StringLength(16)] public string? AccentColor { get; set; }
    [Range(1, 10000)] public int? EstimatedMinutes { get; set; }
    public bool? IsPublished { get; set; }
    [Range(0, 10000)] public int? SortOrder { get; set; }
}

public sealed record AdminLessonResponse(
    Guid Id,
    Guid CourseId,
    string Slug,
    string Title,
    string? Summary,
    string? ContentMarkdown,
    int SortOrder,
    int EstimatedMinutes,
    bool IsPublished,
    Guid? GrammarTopicId,
    string[]? KeyVocabulary);

public sealed class CreateLessonRequest
{
    [Required] public Guid CourseId { get; set; }
    [Required, StringLength(160, MinimumLength = 2)] public string Slug { get; set; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 2)] public string Title { get; set; } = string.Empty;
    [StringLength(1000)] public string? Summary { get; set; }
    public string? ContentMarkdown { get; set; }
    [Range(0, 10000)] public int SortOrder { get; set; }
    [Range(1, 10000)] public int EstimatedMinutes { get; set; } = 10;
    public bool IsPublished { get; set; } = true;
    public Guid? GrammarTopicId { get; set; }
    public string[]? KeyVocabulary { get; set; }
}

public sealed class UpdateLessonRequest
{
    [StringLength(160, MinimumLength = 2)] public string? Slug { get; set; }
    [StringLength(200, MinimumLength = 2)] public string? Title { get; set; }
    [StringLength(1000)] public string? Summary { get; set; }
    public string? ContentMarkdown { get; set; }
    [Range(0, 10000)] public int? SortOrder { get; set; }
    [Range(1, 10000)] public int? EstimatedMinutes { get; set; }
    public bool? IsPublished { get; set; }
    public Guid? GrammarTopicId { get; set; }
    public string[]? KeyVocabulary { get; set; }
}

public sealed class GenerateLessonContentRequest
{
    [StringLength(200)] public string? Topic { get; set; }
    [StringLength(64)] public string? Provider { get; set; }
    [StringLength(128)] public string? Model { get; set; }
    public CefrLevel? Level { get; set; }
    [StringLength(2000)] public string? Requirements { get; set; }
}

public sealed record GenerateLessonContentResponse(
    string Title,
    string? Summary,
    string ContentMarkdown,
    string[] KeyVocabulary,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens);

public sealed class GenerateCourseContentRequest
{
    [StringLength(200)] public string? Topic { get; set; }
    [StringLength(64)] public string? Provider { get; set; }
    [StringLength(128)] public string? Model { get; set; }
    public CefrLevel? Level { get; set; }
    [Range(1, 30)] public int LessonsCount { get; set; } = 5;
}

public sealed record GenerateCourseContentResponse(
    string Description,
    string[] LessonTitles,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens);

public sealed class AiSettingsResponse
{
    public string DefaultChatProvider { get; set; } = string.Empty;
    public string DefaultEmbeddingProvider { get; set; } = string.Empty;
    public string DefaultChatModel { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int MaxOutputTokens { get; set; } = 1200;
    public List<AiProviderSettingsResponse> Providers { get; set; } = [];
}

public sealed class AiProviderSettingsResponse
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string ChatModel { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public string? TtsModel { get; set; }
    public string? SttModel { get; set; }
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
    public int RequestsPerMinute { get; set; } = 120;
}

public sealed class UpdateAiSettingsRequest
{
    [StringLength(64)] public string? DefaultChatProvider { get; set; }
    [StringLength(64)] public string? DefaultEmbeddingProvider { get; set; }
    [StringLength(128)] public string? DefaultChatModel { get; set; }
    [Range(0.0, 2.0)] public double? Temperature { get; set; }
    [Range(64, 8000)] public int? MaxOutputTokens { get; set; }
    public List<UpdateAiProviderRequest>? Providers { get; set; }
}

public sealed class UpdateAiProviderRequest
{
    [Required, StringLength(64)] public string Name { get; set; } = string.Empty;
    [StringLength(64)] public string? Kind { get; set; }
    [StringLength(512)] public string? BaseUrl { get; set; }
    [StringLength(512)] public string? ApiKey { get; set; }
    [StringLength(128)] public string? ChatModel { get; set; }
    [StringLength(128)] public string? EmbeddingModel { get; set; }
    [StringLength(128)] public string? TtsModel { get; set; }
    [StringLength(128)] public string? SttModel { get; set; }
    public bool? Enabled { get; set; }
    [Range(5, 600)] public int? TimeoutSeconds { get; set; }
    [Range(1, 1000)] public int? RequestsPerMinute { get; set; }
}

public sealed record AiTestResponse(
    bool Healthy,
    string Provider,
    string? Model,
    long LatencyMs,
    string? Error);
