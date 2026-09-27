using System.Text.Json.Nodes;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Domain.Entities;

public sealed class Exercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public Guid LanguageId { get; set; }
    public Language? Language { get; set; }
    public Guid? TranslationLanguageId { get; set; }

    public ExerciseType Type { get; set; } = ExerciseType.MultipleChoice;
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public string? Prompt { get; set; }
    public JsonNode? Payload { get; set; }
    public string? ExplanationMarkdown { get; set; }
    public CefrLevel Level { get; set; } = CefrLevel.A1;
    public string[]? Topics { get; set; }
    public Guid[]? TargetLexicalUnitIds { get; set; }
    public int Points { get; set; } = 10;
    public int EstimatedSeconds { get; set; } = 60;
    public ContentSource Source { get; set; } = ContentSource.AiGenerated;
    public string? AiProvider { get; set; }
    public string? AiModel { get; set; }
    public double? AiConfidence { get; set; }
    public bool IsPublished { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int UsageCount { get; set; }
    public int CorrectRateBasisPoints { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}

public sealed class ExerciseAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public JsonNode? Answers { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public int ScorePercent { get; set; }
    public bool IsPassed { get; set; }
    public int XpEarned { get; set; }
    public string? AiFeedbackMarkdown { get; set; }
    public string? AiProvider { get; set; }
    public string? AiModel { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string? Title { get; set; }
    public TutorMode Mode { get; set; } = TutorMode.FreePractice;
    public CefrLevel Level { get; set; } = CefrLevel.A1;
    public string? Scenario { get; set; }
    public string? SystemPromptOverride { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool UseDictionaryContext { get; set; } = true;
    public bool IsArchived { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastMessageAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = [];
}

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public ChatSession? Session { get; set; }
    public ChatRole Role { get; set; }
    public required string Content { get; set; }
    public string? AudioUrl { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }
    public Guid[]? RagContextRefs { get; set; }
    public string? SourceLanguageCode { get; set; }
    public int? FeedbackRating { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public MediaKind Kind { get; set; }
    public required string StoragePath { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public int? DurationMs { get; set; }
    public string? SourceText { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public bool IsPublic { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class PronunciationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }
    public Guid? LexicalUnitId { get; set; }
    public LexicalUnit? LexicalUnit { get; set; }
    public required string TargetText { get; set; }
    public string? Transcript { get; set; }
    public string? RecognizedText { get; set; }
    public int OverallScore { get; set; }
    public int AccuracyScore { get; set; }
    public int FluencyScore { get; set; }
    public int CompletenessScore { get; set; }
    public int ProsodyScore { get; set; }
    public JsonNode? WordLevelScores { get; set; }
    public string? FeedbackMarkdown { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int XpEarned { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AiCallLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public AiOperation Operation { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public double EstimatedCostUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
