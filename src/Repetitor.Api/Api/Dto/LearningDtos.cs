using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Api.Dto;

public sealed record ExerciseSummaryResponse(
    Guid Id,
    string Type,
    string Title,
    string? Instructions,
    string Level,
    int ItemCount,
    int Points,
    int EstimatedSeconds,
    string Source,
    string? AiProvider,
    string? AiModel,
    Guid? CourseId,
    Guid? LessonId,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    int? LastScorePercent,
    int Attempts);

public sealed record ExerciseResponse(
    Guid Id,
    string Type,
    string Title,
    string? Instructions,
    string? Prompt,
    string Level,
    JsonNode? Payload,
    string? ExplanationMarkdown,
    string[]? Topics,
    int Points,
    int EstimatedSeconds,
    string Source,
    string? AiProvider,
    string? AiModel,
    Guid? CourseId,
    Guid? LessonId,
    Guid LanguageId,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    int? LastScorePercent,
    int Attempts);

public sealed class GenerateExerciseRequest
{
    [Required] public ExerciseType Type { get; set; }
    public CefrLevel? Level { get; set; }
    [Range(1, 20)] public int ItemCount { get; set; } = 5;
    [StringLength(300)] public string? Topic { get; set; }
    [StringLength(64)] public string? Provider { get; set; }
    [StringLength(128)] public string? Model { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? LessonId { get; set; }
    public Guid[]? LexicalUnitIds { get; set; }
    public bool UseVectorContext { get; set; } = true;
}

public sealed class UpdateExerciseRequest
{
    [StringLength(300)] public string? Title { get; set; }
    [StringLength(2000)] public string? Instructions { get; set; }
    [StringLength(2000)] public string? Prompt { get; set; }
    public JsonNode? Payload { get; set; }
    [StringLength(8000)] public string? ExplanationMarkdown { get; set; }
    public CefrLevel? Level { get; set; }
    [Range(0, 1000)] public int? Points { get; set; }
    [Range(5, 3600)] public int? EstimatedSeconds { get; set; }
    public bool? IsPublished { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class SubmitExerciseRequest
{
    public JsonNode? Answers { get; set; }
    [Range(0, 86_400_000)] public int DurationMs { get; set; }
    [StringLength(8000)] public string? FreeTextAnswer { get; set; }
    public bool UseAiGrading { get; set; } = true;
}

public sealed record ExerciseAttemptResponse(
    Guid Id,
    Guid ExerciseId,
    int ScorePercent,
    bool IsPassed,
    int CorrectCount,
    int TotalCount,
    int XpEarned,
    string? Feedback,
    JsonNode? Items,
    int DurationMs,
    DateTimeOffset CompletedAt);

public sealed class StartChatSessionRequest
{
    public TutorMode Mode { get; set; } = TutorMode.FreePractice;
    public CefrLevel? Level { get; set; }
    [StringLength(2000)] public string? Scenario { get; set; }
    [StringLength(200)] public string? Title { get; set; }
    [StringLength(64)] public string? Provider { get; set; }
    [StringLength(128)] public string? Model { get; set; }
    public bool UseDictionary { get; set; } = true;
}

public sealed record ChatSessionResponse(
    Guid Id,
    string? Title,
    string Mode,
    string Level,
    string? Scenario,
    string Provider,
    string Model,
    bool UseDictionaryContext,
    bool IsArchived,
    int MessageCount,
    int TotalInputTokens,
    int TotalOutputTokens,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);

public sealed record ChatMessageResponse(
    Guid Id,
    string Role,
    string Content,
    string? AudioUrl,
    string? Provider,
    string? Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    Guid[]? RagContextRefs,
    int? FeedbackRating,
    DateTimeOffset CreatedAt);

public sealed class SendChatMessageRequest
{
    [Required, StringLength(8000, MinimumLength = 1)] public string Message { get; set; } = string.Empty;
    [StringLength(64)] public string? Provider { get; set; }
    [StringLength(128)] public string? Model { get; set; }
    public bool? UseDictionary { get; set; }
    [StringLength(8)] public string? SourceLanguageCode { get; set; }
    [StringLength(512)] public string? AudioUrl { get; set; }
}

public sealed record SendChatMessageResponse(
    ChatMessageResponse UserMessage,
    ChatMessageResponse AssistantMessage,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    IReadOnlyList<RagContextResponse> Context);

public sealed record RagContextResponse(
    Guid LexicalUnitId,
    string Text,
    string? Translation,
    string? Transcription,
    double Similarity);

public sealed class RenameSessionRequest
{
    [Required, StringLength(200, MinimumLength = 1)] public string Title { get; set; } = string.Empty;
}

public sealed class FeedbackRequest
{
    [Range(-1, 1)] public int Rating { get; set; }
}

public sealed class GradeAnswerRequest
{
    [Required, StringLength(8000, MinimumLength = 1)] public string Answer { get; set; } = string.Empty;
    [Required, StringLength(8000, MinimumLength = 1)] public string ReferenceAnswer { get; set; } = string.Empty;
    public string[]? AcceptedVariants { get; set; }
    [Required, StringLength(8, MinimumLength = 2)] public string Language { get; set; } = "en";
    [StringLength(8, MinimumLength = 2)] public string InterfaceLanguage { get; set; } = "ru";
    public CefrLevel Level { get; set; } = CefrLevel.A1;
    [StringLength(500)] public string? Task { get; set; }
    [StringLength(64)] public string? Provider { get; set; }
    public bool UseAi { get; set; } = true;
}

public sealed record GradeAnswerResponse(
    bool IsCorrect,
    int ScorePercent,
    string CorrectedAnswer,
    string Explanation,
    string[] Issues,
    string[] Hints,
    string Method);

public sealed class PronunciationAssessmentRequest
{
    [Required, StringLength(2000, MinimumLength = 1)] public string TargetText { get; set; } = string.Empty;
    [StringLength(4000)] public string? Transcript { get; set; }
    [Required] public IFormFile Audio { get; set; } = null!;
    [StringLength(8, MinimumLength = 2)] public string? Language { get; set; }
    public Guid? LexicalUnitId { get; set; }
    [StringLength(64)] public string? Provider { get; set; }
    public bool UseAi { get; set; } = true;
}

public sealed record PronunciationResponse(
    Guid Id,
    string TargetText,
    string? RecognizedText,
    int OverallScore,
    int AccuracyScore,
    int FluencyScore,
    int CompletenessScore,
    int ProsodyScore,
    string Feedback,
    WordScoreResponse[] Words,
    string Method,
    string? AudioUrl,
    int XpEarned,
    DateTimeOffset CreatedAt);

public sealed record WordScoreResponse(string Word, string Expected, string Recognized, int Score, string? Hint);

public sealed class TranscribeRequest
{
    [Required] public IFormFile Audio { get; set; } = null!;
    [StringLength(8, MinimumLength = 2)] public string? Language { get; set; }
    [StringLength(2000)] public string? Prompt { get; set; }
}

public sealed record TranscriptionResponse(
    string Text,
    string? Language,
    double? DurationSeconds,
    IReadOnlyList<TranscriptSegmentResponse> Segments,
    string? AudioUrl);

public sealed record TranscriptSegmentResponse(double Start, double End, string Text, double? Confidence);

public sealed class SynthesizeRequest
{
    [Required, StringLength(4000, MinimumLength = 1)] public string Text { get; set; } = string.Empty;
    [StringLength(8, MinimumLength = 2)] public string? Language { get; set; }
    [StringLength(64)] public string? Voice { get; set; }
    [Range(0.5, 2.0)] public double Speed { get; set; } = 1.0;
}

public sealed record SynthesizeResponse(string AudioUrl, Guid MediaId, string ContentType, long SizeBytes, string Provider);

public sealed record AiProviderResponse(
    string Name,
    string Kind,
    bool Enabled,
    bool Configured,
    string? ChatModel,
    string? EmbeddingModel,
    string? TtsModel,
    string? SttModel,
    bool SupportsStreaming,
    bool SupportsJsonMode,
    int EmbeddingDimensions,
    int RequestsPerMinute);

public sealed record AiUsageResponse(
    DateOnly From,
    DateOnly To,
    int TotalCalls,
    int FailedCalls,
    int InputTokens,
    int OutputTokens,
    double EstimatedCostUsd,
    IReadOnlyList<AiUsageByProviderResponse> ByProvider,
    IReadOnlyList<AiUsageByDayResponse> ByDay);

public sealed record AiUsageByProviderResponse(string Provider, int Calls, int InputTokens, int OutputTokens, double EstimatedCostUsd);

public sealed record AiUsageByDayResponse(DateOnly Date, int Calls, int InputTokens, int OutputTokens);

public sealed record ReindexResponse(int Processed, int Created, int Failed);
