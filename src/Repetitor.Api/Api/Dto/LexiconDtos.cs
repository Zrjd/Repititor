using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Api.Dto;

public sealed record LexicalUnitResponse(
    Guid Id,
    Guid LanguageId,
    Guid TranslationLanguageId,
    string Text,
    string? Transcription,
    string? Translation,
    string[]? AlternativeTranslations,
    string PartOfSpeech,
    string? Gender,
    string? PluralForm,
    string? PastTense,
    string? AudioUrl,
    string? ExampleTarget,
    string? ExampleNative,
    string? Notes,
    string[]? Tags,
    string MinLearnerLevel,
    int FrequencyRank,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    double? SemanticSimilarity = null);

public sealed class CreateLexicalUnitRequest
{
    [Required, StringLength(400, MinimumLength = 1)] public string Text { get; set; } = string.Empty;
    [StringLength(200)] public string? Transcription { get; set; }
    [StringLength(1000)] public string? Translation { get; set; }
    public string[]? AlternativeTranslations { get; set; }
    public PartOfSpeech PartOfSpeech { get; set; } = PartOfSpeech.Unknown;
    [StringLength(16)] public string? Gender { get; set; }
    [StringLength(200)] public string? PluralForm { get; set; }
    [StringLength(200)] public string? PastTense { get; set; }
    [StringLength(512)] public string? AudioUrl { get; set; }
    [StringLength(1000)] public string? ExampleTarget { get; set; }
    [StringLength(1000)] public string? ExampleNative { get; set; }
    [StringLength(4000)] public string? Notes { get; set; }
    public string[]? Tags { get; set; }
    public CefrLevel MinLearnerLevel { get; set; } = CefrLevel.A1;
    [Range(0, 100000)] public int FrequencyRank { get; set; }
    public Guid? LanguageId { get; set; }
    public Guid? TranslationLanguageId { get; set; }
    public Guid? DeckId { get; set; }
    public bool ComputeEmbedding { get; set; } = true;
}

public sealed class UpdateLexicalUnitRequest
{
    [StringLength(400, MinimumLength = 1)] public string? Text { get; set; }
    [StringLength(200)] public string? Transcription { get; set; }
    [StringLength(1000)] public string? Translation { get; set; }
    public string[]? AlternativeTranslations { get; set; }
    public PartOfSpeech? PartOfSpeech { get; set; }
    [StringLength(16)] public string? Gender { get; set; }
    [StringLength(200)] public string? PluralForm { get; set; }
    [StringLength(200)] public string? PastTense { get; set; }
    [StringLength(512)] public string? AudioUrl { get; set; }
    [StringLength(1000)] public string? ExampleTarget { get; set; }
    [StringLength(1000)] public string? ExampleNative { get; set; }
    [StringLength(4000)] public string? Notes { get; set; }
    public string[]? Tags { get; set; }
    public CefrLevel? MinLearnerLevel { get; set; }
    [Range(0, 100000)] public int? FrequencyRank { get; set; }
    public ContentStatus? Status { get; set; }
    public bool? ComputeEmbedding { get; set; }
}

public sealed class SearchWordsRequest
{
    [StringLength(400)] public string? Query { get; set; }
    public Guid? LanguageId { get; set; }
    public Guid? TranslationLanguageId { get; set; }
    [Range(1, 200)] public int Limit { get; set; } = 20;
    [Range(0, 200)] public int Page { get; set; } = 1;
    public bool Semantic { get; set; }
    public CefrLevel? MaxMinLevel { get; set; }
    public string[]? Tags { get; set; }
    public PartOfSpeech? PartOfSpeech { get; set; }
    public string? Provider { get; set; }
    [Range(0, 1)] public double MinSimilarity { get; set; } = 0.3;
}

public sealed class ImportWordsRequest
{
    [Required, StringLength(200_000, MinimumLength = 2)] public string Text { get; set; } = string.Empty;
    [StringLength(1)] public string Delimiter { get; set; } = "\t";
    public bool HasHeader { get; set; } = true;
    public Guid? LanguageId { get; set; }
    public Guid? TranslationLanguageId { get; set; }
    public Guid? DeckId { get; set; }
    public CefrLevel MinLearnerLevel { get; set; } = CefrLevel.A1;
    public string[]? DefaultTags { get; set; }
    public bool SkipDuplicates { get; set; } = true;
    public bool ComputeEmbeddings { get; set; } = true;
}

public sealed record ImportWordsResponse(
    int Imported,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

public sealed record DeckResponse(
    Guid Id,
    string Name,
    string? Description,
    string? LanguageCode,
    string? CoverEmoji,
    string[]? Tags,
    bool IsArchived,
    int CardsCount,
    int DueCount,
    int NewCount,
    int MasteredCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CreateDeckRequest
{
    [Required, StringLength(160, MinimumLength = 1)] public string Name { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
    [StringLength(8)] public string? LanguageCode { get; set; }
    [StringLength(16)] public string? CoverEmoji { get; set; }
    public string[]? Tags { get; set; }
    public Guid[]? LexicalUnitIds { get; set; }
}

public sealed class UpdateDeckRequest
{
    [StringLength(160, MinimumLength = 1)] public string? Name { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
    [StringLength(8)] public string? LanguageCode { get; set; }
    [StringLength(16)] public string? CoverEmoji { get; set; }
    public string[]? Tags { get; set; }
    public bool? IsArchived { get; set; }
}

public sealed class DeckCardsRequest
{
    public Guid[] LexicalUnitIds { get; set; } = [];
}

public sealed record UserWordResponse(
    Guid UserLexicalUnitId,
    Guid LexicalUnitId,
    string Text,
    string? Translation,
    string? Transcription,
    string? AudioUrl,
    string? PartOfSpeech,
    string? ExampleTarget,
    string State,
    bool Suspended,
    int MasteryScore,
    int CorrectStreak,
    int IncorrectStreak,
    string? PersonalNote,
    string Source,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastReviewedAt);

public sealed class ReviewSubmissionRequest
{
    [Required] public IReadOnlyList<ReviewSubmissionItem> Reviews { get; set; } = [];
    public int SessionNewCards { get; set; }
}

public sealed class ReviewSubmissionItem
{
    [Required] public Guid ReviewCardId { get; set; }
    [Required] public ReviewRating Rating { get; set; }
    [Range(0, 600_000)] public int DurationMs { get; set; }
    [StringLength(1000)] public string? GivenAnswer { get; set; }
}

public sealed record PracticeSessionResponse(
    IReadOnlyList<DueCardResponse> Cards,
    int TotalDue,
    int Limit);

public sealed record DueCardResponse(
    Guid ReviewCardId,
    Guid LexicalUnitId,
    string Text,
    string? Translation,
    string? Transcription,
    string? AudioUrl,
    string? PartOfSpeech,
    string? ExampleTarget,
    string State,
    DateTimeOffset DueAt,
    double IntervalDays,
    int Repetitions);

public sealed record ReviewResultResponse(
    Guid ReviewCardId,
    string State,
    DateTimeOffset DueAt,
    double IntervalDays,
    double EaseFactor,
    bool WasCorrect,
    int MasteryScore,
    string? CorrectTranslation,
    int XpEarned);

public sealed record PracticeSummaryResponse(
    int Reviewed,
    int Correct,
    int Again,
    int NewCardsSeen,
    int XpEarned,
    int AccuracyPercent,
    int RemainingDue,
    IReadOnlyList<ForecastDayResponse> Forecast);

public sealed record ForecastDayResponse(DateOnly Date, int Cards);

public sealed record WordOfTheDayResponse(
    LexicalUnitResponse Word,
    string? Reason,
    string? AudioUrl);
