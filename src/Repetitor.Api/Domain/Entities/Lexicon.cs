using Repetitor.Api.Domain.Enums;

namespace Repetitor.Api.Domain.Entities;

public sealed class LexicalUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LanguageId { get; set; }
    public Language? Language { get; set; }
    public Guid TranslationLanguageId { get; set; }
    public Language? TranslationLanguage { get; set; }

    public required string Text { get; set; }
    public required string NormalizedText { get; set; }
    public string? Transcription { get; set; }
    public string? Translation { get; set; }
    public string[]? AlternativeTranslations { get; set; }
    public PartOfSpeech PartOfSpeech { get; set; } = PartOfSpeech.Unknown;
    public string? Gender { get; set; }
    public string? PluralForm { get; set; }
    public string? PastTense { get; set; }
    public string? AudioUrl { get; set; }
    public string? ExampleTarget { get; set; }
    public string? ExampleNative { get; set; }
    public string? Notes { get; set; }
    public string[]? Tags { get; set; }
    public CefrLevel MinLearnerLevel { get; set; } = CefrLevel.A1;
    public int FrequencyRank { get; set; }
    public ContentStatus Status { get; set; } = ContentStatus.Draft;
    public string? ContentHash { get; set; }
    public Guid? AuthorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<LexicalUnitEmbedding> Embeddings { get; set; } = [];
}

public sealed class LexicalUnitEmbedding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LexicalUnitId { get; set; }
    public LexicalUnit? LexicalUnit { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserLexicalUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid LexicalUnitId { get; set; }
    public LexicalUnit? LexicalUnit { get; set; }
    public ContentSource Source { get; set; } = ContentSource.UserCreated;
    public string? PersonalNote { get; set; }
    public CardState State { get; set; } = CardState.New;
    public bool Suspended { get; set; }
    public int CorrectStreak { get; set; }
    public int IncorrectStreak { get; set; }
    public int MasteryScore { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastReviewedAt { get; set; }
    public Guid? LastDeckId { get; set; }
    public Deck? LastDeck { get; set; }
    public ReviewCard? ReviewCard { get; set; }
}

public sealed class Deck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? LanguageCode { get; set; }
    public string? CoverEmoji { get; set; }
    public string[]? Tags { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DeckCard> Cards { get; set; } = [];
}

public sealed class DeckCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeckId { get; set; }
    public Deck? Deck { get; set; }
    public Guid UserLexicalUnitId { get; set; }
    public UserLexicalUnit? UserLexicalUnit { get; set; }
    public int Position { get; set; }
    public bool IsNew { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ReviewCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserLexicalUnitId { get; set; }
    public UserLexicalUnit? UserLexicalUnit { get; set; }
    public CardState State { get; set; } = CardState.New;
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow;
    public double IntervalDays { get; set; }
    public double EaseFactor { get; set; } = 2.5;
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public int LearningStep { get; set; }
    public int MaxIntervalDays { get; set; } = 365 * 3;
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
}

public sealed class ReviewLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ReviewCardId { get; set; }
    public ReviewCard? ReviewCard { get; set; }
    public Guid LexicalUnitId { get; set; }
    public LexicalUnit? LexicalUnit { get; set; }
    public ReviewRating Rating { get; set; }
    public double PreviousIntervalDays { get; set; }
    public double NewIntervalDays { get; set; }
    public CardState PreviousState { get; set; }
    public CardState NewState { get; set; }
    public int DurationMs { get; set; }
    public bool WasCorrect { get; set; }
    public string? GivenAnswer { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserDailyStat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateOnly Date { get; set; }
    public int XpEarned { get; set; }
    public int ReviewsCompleted { get; set; }
    public int CorrectAnswers { get; set; }
    public int NewWordsLearned { get; set; }
    public int ExercisesCompleted { get; set; }
    public int ChatMessagesSent { get; set; }
    public int MinutesStudied { get; set; }
    public bool GoalReached { get; set; }
}
